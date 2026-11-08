//! Offline aggregation of generated, downloaded libraries. Earlier entries win.
use std::{collections::{BTreeMap, BTreeSet},path::Path};
use altium::{pcb,sch,parameter::ParameterMap};
use serde::{Deserialize,Serialize};
use crate::{Error,Result};

#[derive(Deserialize)]
#[serde(rename_all="PascalCase")]
pub struct Input {pub code:Option<String>,pub symbol_name:Option<String>,pub symbol_alias:Option<String>,pub footprint_name:Option<String>,pub footprint_alias:Option<String>,pub symbol_path:String,pub footprint_path:String}
#[derive(Deserialize)]
#[serde(rename_all="PascalCase")]
pub struct Request {pub model_namespace:String,pub entries:Vec<Input>}
#[derive(Serialize)]
#[serde(rename_all="PascalCase")]
pub struct Summary {pub schema_version:u32,pub symbols:usize,pub footprints:usize,pub models:usize}

fn invalid(message:&str)->Error{Error::InvalidArg(message.into())}
fn bytes(path:&str)->Result<Vec<u8>>{
 if std::fs::metadata(path)?.len()>512*1024*1024{return Err(invalid("single input library exceeds 512 MB"));}
 Ok(std::fs::read(path)?)
}
fn font_value(lib:&sch::Library,key:&str)->Option<String>{lib.file_header_parameters.iter().find(|(k,_)|k.eq_ignore_ascii_case(key)).map(|(_,v)|v.clone())}
fn fonts(lib:&sch::Library,target:&mut sch::Library,table:&mut Vec<BTreeMap<String,String>>)->Result<BTreeMap<i32,i32>>{
 let count=font_value(lib,"FontIdCount").and_then(|v|v.parse::<i32>().ok()).unwrap_or(1);
 if !(1..=65535).contains(&count){return Err(invalid("invalid source font table"));}
 let mut map=BTreeMap::new();
 for id in 1..=count {
  let mut values=BTreeMap::new();
  for key in ["FontName","Size","Rotation","Bold","Italic","Underline","StrikeOut","Color"] {
   if let Some(v)=font_value(lib,&format!("{key}{id}")){values.insert(key.to_owned(),v);}
  }
  values.entry("FontName".into()).or_insert_with(||lib.font_override.clone().filter(|_|id>1).unwrap_or_else(||"Times New Roman".into()));
  values.entry("Size".into()).or_insert("10".into());
  let slot=if let Some(index)=table.iter().position(|v|v==&values){index+1}else{
   if table.len()>=65535{return Err(invalid("merged font table exceeds Altium font ID range"));}
   let slot=table.len()+1;
   for (key,value) in &values{target.file_header_parameters.insert(format!("{key}{slot}"),value.clone());}
   table.push(values);slot
  };
  map.insert(id,slot as i32);
 }
 target.file_header_parameters.insert("FontIdCount".into(),table.len().to_string());Ok(map)
}
fn font(id:i32,map:&BTreeMap<i32,i32>)->Result<i32>{map.get(&id.max(1)).copied().ok_or_else(||invalid("font reference missing from source library"))}
fn remap_fonts(component:&mut sch::Component,map:&BTreeMap<i32,i32>)->Result<()>{
 // Reader preserves PinTextData as a raw auxiliary stream. Decode it first so
 // the writer rebuilds custom font IDs rather than overwriting them with old IDs.
 if let Some(key)=component.additional_streams.keys().find(|k|k.eq_ignore_ascii_case("PinTextData")).cloned(){
  let data=component.additional_streams.remove(&key).unwrap();
  let mut decoded=Vec::new();
  sch::binary::read_compressed_storage(&data,|name,body|{decoded.push((name.to_owned(),body.to_vec()));Ok(())})?;
  let size=u32::from_le_bytes(data.get(..4).ok_or_else(||invalid("truncated pin text table"))?.try_into().unwrap()) as usize & 0x00ff_ffff;
  let header=data.get(4..4+size).ok_or_else(||invalid("truncated pin text header"))?;
  let expected=ParameterMap::parse(altium::encoding::decode(header).trim_end_matches('\0')).get_i32("Weight").ok_or_else(||invalid("pin text table missing count"))?;
  if expected<0||decoded.len()!=expected as usize{return Err(invalid("incomplete pin text table"));}
  for (name,body) in decoded {
   let index=name.parse::<usize>().map_err(|_|invalid("invalid pin text index"))?;
   let pin=component.pins.get_mut(index).ok_or_else(||invalid("pin text index exceeds pin count"))?;
   let mut offset=0;
   for segment in 0..2 {
    let tag=*body.get(offset).ok_or_else(||invalid("truncated pin text segment"))?;
    if tag==0{offset+=1;continue;}
    if tag!=0x10||offset+7>body.len(){return Err(invalid("unsupported pin text segment"));}
    let id=u16::from_le_bytes(body[offset+1..offset+3].try_into().unwrap()) as i32;
    let color=i32::from_le_bytes(body[offset+3..offset+7].try_into().unwrap());
    if segment==0{pin.name_font_mode=2;pin.name_custom_font_id=id;pin.name_custom_color=color;}
    else{pin.designator_font_mode=2;pin.designator_custom_font_id=id;pin.designator_custom_color=color;}
    offset+=7;
   }
   if offset!=body.len(){return Err(invalid("unexpected pin text data"));}
  }
 }
 for pin in &mut component.pins {
  pin.name_custom_font_id=font(if pin.name_font_mode==0{1}else{pin.name_custom_font_id},map)?;pin.name_font_mode=2;
  pin.designator_custom_font_id=font(if pin.designator_font_mode==0{1}else{pin.designator_custom_font_id},map)?;pin.designator_font_mode=2;
 }
 macro_rules! remap {($items:expr)=>{for value in $items{value.font_id=font(value.font_id,map)?;}}}
 remap!(&mut component.labels);remap!(&mut component.net_labels);remap!(&mut component.parameters);remap!(&mut component.text_frames);remap!(&mut component.power_objects);
 // Generated online pairs use typed records. Preserve unknown parameter records
 // but reject unknown binary records rather than silently corrupting their IDs.
 for record in &mut component.raw_records {
  if record.flag!=0{return Err(invalid("unmodelled binary schematic record cannot be safely merged"));}
  let mut rewritten=Vec::new();
  for (i,part) in record.bytes.split(|b|*b==b'|').enumerate(){
   if i>0{rewritten.push(b'|');}
   if let Some(eq)=part.iter().position(|b|*b==b'=') {
    if part[..eq].eq_ignore_ascii_case(b"FONTID") {
     let id=std::str::from_utf8(&part[eq+1..]).ok().and_then(|s|s.trim_end_matches('\0').parse::<i32>().ok()).ok_or_else(||invalid("invalid raw font ID"))?;
     rewritten.extend_from_slice(&part[..=eq]);rewritten.extend_from_slice(font(id,map)?.to_string().as_bytes());
     if part.last()==Some(&0){rewritten.push(0);}continue;
    }
   }rewritten.extend_from_slice(part);
  }record.bytes=rewritten;
 }
 Ok(())
}

pub fn merge(request:&Request,output:&Path)->Result<Summary>{
 if request.entries.len()>20000{return Err(invalid("too many library inputs"));}
 let namespace=u128::from_str_radix(&request.model_namespace,16).map_err(|_|invalid("invalid model namespace"))?;
 if request.model_namespace.len()!=32{return Err(invalid("invalid model namespace length"));}
 let mut sch=sch::Library::default();let mut pcb=pcb::Library::default();crate::cli::populate_pcb_library_defaults(&mut pcb);
 let mut font_table=Vec::new();let mut symbols=BTreeSet::new();let mut footprints=BTreeSet::new();let mut model_sequence=0u128;
 for input in &request.entries {
  let source_sch=sch::Library::from_bytes(bytes(&input.symbol_path)?)?;
  let source_pcb=pcb::Library::from_bytes(bytes(&input.footprint_path)?)?;
  if !source_sch.embedded_images.is_empty()||source_sch.raw_storage_stream.is_some()||source_sch.components.iter().any(|c|!c.images.is_empty()){
   return Err(invalid("embedded schematic images require a resource-aware importer"));
  }
  if let Some(code)=&input.code {
   if code.len()<2||!code.starts_with('C')||!code[1..].bytes().all(|c|c.is_ascii_digit())||source_sch.components.len()!=1||source_pcb.components.len()!=1{return Err(invalid("downloaded pair must contain one identified component"));}
   if input.symbol_name.as_deref()!=Some(source_sch.components[0].name.as_str())||input.footprint_name.as_deref()!=Some(source_pcb.components[0].name.as_str()){return Err(invalid(&format!("downloaded pair metadata does not match library: {} / {}",source_sch.components[0].name,source_pcb.components[0].name)));}
  }
  let font_map=fonts(&source_sch,&mut sch,&mut font_table)?;
  let mut used_models=BTreeSet::new();let mut selected=Vec::new();
  for mut component in source_pcb.components {
   for body in &mut component.component_bodies {
    let bottom=crate::body_layers::is_bottom(body,source_pcb.library_parameters.as_ref());
    crate::body_layers::set_body_layer(body,bottom);
   }
   if input.code.is_some(){component.name=input.footprint_alias.clone().filter(|s|!s.is_empty()&&s.is_ascii()&&!s.contains(['|','/','\\'])).ok_or_else(||invalid("missing footprint alias"))?;}
   if !footprints.insert(component.name.to_lowercase()){continue;}
   for body in &component.component_bodies{if body.model_embed{if let Some(id)=&body.model_id{used_models.insert(id.clone());}}}
   selected.push(component);
  }
  let mut model_map=BTreeMap::new();
  for mut model in source_pcb.models {
   if !used_models.contains(&model.id){continue;}
   if model_map.contains_key(&model.id){return Err(invalid("duplicate source model ID"));}
   model_sequence=model_sequence.checked_add(1).ok_or_else(||invalid("model count overflow"))?;
   let id=format!("{:032x}",namespace^model_sequence);model_map.insert(model.id.clone(),id.clone());model.id=id;pcb.models.push(model);
  }
  if used_models.iter().any(|id|!model_map.contains_key(id)){return Err(invalid("embedded model resource is missing"));}
  for mut component in selected {
   for body in &mut component.component_bodies{if body.model_embed{if let Some(id)=&body.model_id{body.model_id=Some(model_map[id].clone());}}}
   pcb.section_keys.insert(component.name.clone(),format!("P{:08}",pcb.components.len()));pcb.components.push(component);
  }
  for mut component in source_sch.components {
   if input.code.is_some() {
    component.name=input.symbol_alias.clone().filter(|s|!s.is_empty()&&s.is_ascii()&&!s.contains(['|','/','\\'])).ok_or_else(||invalid("missing symbol alias"))?;component.lib_reference=Some(component.name.clone());
    for model in &mut component.implementations {if model.model_type.as_deref()==Some("PCBLIB"){model.model_name=input.footprint_alias.clone();}}
   }
   if !symbols.insert(component.name.to_lowercase()){continue;}
   remap_fonts(&mut component,&font_map)?;
   sch.section_keys.insert(component.name.clone(),format!("S{:08}",sch.components.len()));sch.components.push(component);
  }
 }
 for component in &sch.components {for model in &component.implementations{
  if model.model_type.as_deref()==Some("PCBLIB")&&model.model_name.as_ref().is_some_and(|name|!footprints.contains(&name.to_lowercase())){return Err(invalid("symbol references a missing footprint"));}
 }}
 let result=Summary{schema_version:1,symbols:sch.components.len(),footprints:pcb.components.len(),models:pcb.models.len()};
 let sch_bytes=sch.to_bytes()?;let pcb_bytes=pcb.to_bytes()?;
 let reread_sch=sch::Library::from_bytes(sch_bytes.clone())?;let reread_pcb=pcb::Library::from_bytes(pcb_bytes.clone())?;
 if reread_sch.components.len()!=result.symbols||reread_pcb.components.len()!=result.footprints||reread_pcb.models.len()!=result.models{return Err(invalid(&format!("merged library readback failed: symbols {}/{}, footprints {}/{}, models {}/{}",reread_sch.components.len(),result.symbols,reread_pcb.components.len(),result.footprints,reread_pcb.models.len(),result.models)));}
 std::fs::write(output.with_extension("SchLib"),sch_bytes)?;std::fs::write(output.with_extension("PcbLib"),pcb_bytes)?;
 Ok(result)
}
pub fn run(request:&Path,output:&Path)->Result<()>{
 if std::fs::metadata(request)?.len()>16*1024*1024{return Err(invalid("merge request exceeds size limit"));}
 let request:Request=serde_json::from_slice(&std::fs::read(request)?)?;
 let summary=merge(&request,output)?;std::fs::write(output.with_extension("merged.json"),serde_json::to_vec(&summary)?)?;
 eprintln!("unified-library: {} symbols, {} footprints, {} models",summary.symbols,summary.footprints,summary.models);Ok(())
}
