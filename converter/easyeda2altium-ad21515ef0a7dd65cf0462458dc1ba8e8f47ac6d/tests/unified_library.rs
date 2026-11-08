use std::{path::{Path,PathBuf}};
use altium::{sch,pcb,coord::CoordPoint};
use easyeda2altium::unified_library::{Input,Request,merge};
fn folder()->PathBuf{let p=std::env::temp_dir().join(format!("ls-unified-{}-{}",std::process::id(),std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));std::fs::create_dir_all(&p).unwrap();p}
fn pair(root:&Path,code:&str,family:&str,step:&str)->Input{
 let mut sch=sch::Library::default();
 for (key,value) in [("FontIdCount","2"),("FontName1","Times New Roman"),("Size1","10"),("FontName2",family),("Size2","8")]{sch.file_header_parameters.insert(key.into(),value.into());}
 let mut symbol=sch::Component::new("SAME_MODEL");symbol.lib_reference=Some("SAME_MODEL".into());symbol.add_default_labels("U",2);
 symbol.labels.push(sch::Label{text:"TEXT".into(),font_id:2,..Default::default()});
 symbol.pins.push(sch::Pin{name:Some("VCC".into()),designator:Some("1".into()),name_font_mode:2,name_custom_font_id:2,name_custom_color:0x123456,designator_font_mode:2,designator_custom_font_id:2,show_name:false,show_designator:true,..Default::default()});
 symbol.implementations.push(sch::Implementation{model_type:Some("PCBLIB".into()),model_name:Some("SAME_FOOTPRINT".into()),..Default::default()});sch.components.push(symbol);
 let mut pcb=pcb::Library::default();let mut footprint=pcb::Component::new("SAME_FOOTPRINT");
 footprint.pads.push(pcb::primitives::Pad{designator:Some("1".into()),..Default::default()});
 footprint.component_bodies.push(pcb::primitives::ComponentBody{model_embed:true,model_id:Some("same-model-id".into()),model_2d_location:CoordPoint::mils(33.0,-38.0),model_3d_rot_z:90.0,..Default::default()});
 pcb.components.push(footprint);pcb.models.push(pcb::Model3d{id:"same-model-id".into(),is_embedded:true,step_data:format!("ISO-10303-21;HEADER;ENDSEC;DATA;/*{step}*/ENDSEC;END-ISO-10303-21;"),..Default::default()});
 let s=root.join(format!("{code}.SchLib"));let p=root.join(format!("{code}.PcbLib"));std::fs::write(&s,sch.to_bytes().unwrap()).unwrap();std::fs::write(&p,pcb.to_bytes().unwrap()).unwrap();
 Input{code:Some(code.into()),symbol_name:Some("SAME_MODEL".into()),symbol_alias:Some(format!("LS_{code}_pair")),footprint_name:Some("SAME_FOOTPRINT".into()),footprint_alias:Some(format!("LS_{code}_footprint")),symbol_path:s.to_string_lossy().into(),footprint_path:p.to_string_lossy().into()}
}
fn request(entries:Vec<Input>)->Request{Request{model_namespace:"aabbccddeeff00112233445566778899".into(),entries}}
fn plain(path:&Path)->Input{Input{code:None,symbol_name:None,symbol_alias:None,footprint_name:None,footprint_alias:None,symbol_path:path.with_extension("SchLib").to_string_lossy().into(),footprint_path:path.with_extension("PcbLib").to_string_lossy().into()}}
#[test]fn duplicate_human_names_keep_distinct_pairs_fonts_pins_and_step_resources(){
 let f=folder();let a=pair(&f,"C1","Arial","FIRST");let b=pair(&f,"C2","Courier New","SECOND");let before=std::fs::read(&a.symbol_path).unwrap();let out=f.join("LibrarySearch");
 let summary=merge(&request(vec![a,b]),&out).unwrap();assert_eq!((summary.symbols,summary.footprints,summary.models),(2,2,2));
 let sch=sch::Library::from_bytes(std::fs::read(out.with_extension("SchLib")).unwrap()).unwrap();let pcb=pcb::Library::from_bytes(std::fs::read(out.with_extension("PcbLib")).unwrap()).unwrap();
 for (i,family) in ["Arial","Courier New"].iter().enumerate(){
  let symbol=&sch.components[i];assert_eq!(symbol.implementations[0].model_name.as_deref(),Some(pcb.components[i].name.as_str()));assert!(!symbol.pins[0].show_name);assert!(symbol.pins[0].show_designator);
  let id=symbol.labels[0].font_id;assert_eq!(sch.file_header_parameters[&format!("FontName{id}")],*family);
  let data=&symbol.additional_streams["PinTextData"];let mut ids=Vec::new();sch::binary::read_compressed_storage(data,|_,body|{ids.push(u16::from_le_bytes([body[1],body[2]]) as i32);Ok(())}).unwrap();assert_eq!(ids,vec![id]);
  let body=&pcb.components[i].component_bodies[0];assert_eq!(body.model_2d_location,CoordPoint::mils(33.0,-38.0));assert_eq!(body.model_3d_rot_z,90.0);
  let model=pcb.models.iter().find(|m|Some(&m.id)==body.model_id.as_ref()).unwrap();assert!(model.step_data.contains(if i==0{"FIRST"}else{"SECOND"}));
 }
 assert_ne!(pcb.models[0].id,pcb.models[1].id);assert_eq!(std::fs::read(f.join("C1.SchLib")).unwrap(),before);std::fs::remove_dir_all(f).unwrap();
}
#[test]fn project_merge_keeps_removed_or_older_parts_without_duplicates(){
 let f=folder();let first=f.join("old");merge(&request(vec![pair(&f,"C1","Arial","OLD")]),&first).unwrap();
 let current=f.join("new");merge(&request(vec![pair(&f,"C2","Arial","NEW")]),&current).unwrap();
 let project=f.join("project");let summary=merge(&request(vec![plain(&current),plain(&first),plain(&first)]),&project).unwrap();assert_eq!((summary.symbols,summary.footprints,summary.models),(2,2,2));std::fs::remove_dir_all(f).unwrap();
}
#[test]fn metadata_mismatch_does_not_publish_output_or_change_source(){
 let f=folder();let mut input=pair(&f,"C1","Arial","TEST");let old=std::fs::read(&input.footprint_path).unwrap();input.symbol_name=Some("WRONG".into());let out=f.join("bad");assert!(merge(&request(vec![input]),&out).is_err());assert!(!out.with_extension("SchLib").exists());assert_eq!(std::fs::read(f.join("C1.PcbLib")).unwrap(),old);std::fs::remove_dir_all(f).unwrap();
}
#[test]fn missing_embedded_model_is_rejected(){
 let f=folder();let input=pair(&f,"C1","Arial","TEST");let mut pcb=pcb::Library::from_bytes(std::fs::read(&input.footprint_path).unwrap()).unwrap();pcb.models.clear();std::fs::write(&input.footprint_path,pcb.to_bytes().unwrap()).unwrap();assert!(merge(&request(vec![input]),&f.join("bad")).is_err());std::fs::remove_dir_all(f).unwrap();
}
#[test]fn deleting_last_cache_entry_can_publish_valid_empty_libraries(){
 let f=folder();let out=f.join("empty");let result=merge(&request(vec![]),&out).unwrap();assert_eq!((result.symbols,result.footprints,result.models),(0,0,0));assert!(sch::Library::from_bytes(std::fs::read(out.with_extension("SchLib")).unwrap()).unwrap().components.is_empty());std::fs::remove_dir_all(f).unwrap();
}
