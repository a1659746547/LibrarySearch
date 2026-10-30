//! Offline local library discovery. No AD automation and no network client.
use std::collections::{BTreeMap, HashSet};
use std::path::{Path, PathBuf};
use altium::{intlib::IntegratedLibrary, pcb, sch};
use serde::{Deserialize, Serialize};
use crate::error::{Error, Result};
use crate::local_catalog::{Catalog,Stamp,Segment,Writer,read_catalog};

#[derive(Deserialize)]
#[serde(rename_all="PascalCase")]
pub struct Request { pub roots: Vec<String>, pub cache_directory: String, pub previous_index:Option<String>, #[serde(default)] pub force:bool }
#[derive(Clone,Deserialize,Serialize)]
#[serde(rename_all="PascalCase")]
pub struct Entry {pub name:String,pub kind:String,pub library:String,pub source:String,pub models:String,#[serde(default)] pub description:String}

fn native_path(path:&Path)->String {
    let text=path.to_string_lossy();
    if let Some(rest)=text.strip_prefix("\\\\?\\UNC\\") {format!("\\\\{rest}")}
    else {text.strip_prefix("\\\\?\\").unwrap_or(&text).to_string()}
}
fn add_warning(warnings:&mut Vec<String>,message:String){if warnings.len()<200 {warnings.push(message);}}
fn valid_name(name:&str)->bool { !name.trim().is_empty()&&!name.contains(['|','\t','\r','\n','\0']) }
fn symbol_entries(library:&sch::Library,path:&Path,source:&Path)->Vec<Entry>{
    library.components.iter().filter(|item|valid_name(&item.name)).map(|item|{
        let names:Vec<_>=item.implementations.iter().filter(|model|model.model_type.as_deref().is_some_and(|kind|kind.eq_ignore_ascii_case("PCBLIB"))).filter_map(|model|model.model_name.as_deref()).filter(|name|valid_name(name)).collect();
        Entry{name:item.name.clone(),kind:"Symbol".into(),library:native_path(path),source:native_path(source),models:names.join("; "),description:item.description.clone().unwrap_or_default()}
    }).collect()
}
fn footprint_entries(library:&pcb::Library,path:&Path,source:&Path)->Vec<Entry>{
    library.components.iter().filter(|item|valid_name(&item.name)).map(|item|Entry{name:item.name.clone(),kind:"Footprint".into(),library:native_path(path),source:native_path(source),models:String::new(),description:item.description.clone().unwrap_or_default()}).collect()
}
pub(crate) fn linked_directory(path:&Path)->bool{
    let Ok(metadata)=std::fs::symlink_metadata(path) else{return true};
    if metadata.file_type().is_symlink(){return true;}
    #[cfg(windows)] {use std::os::windows::fs::MetadataExt;if metadata.file_attributes()&0x400!=0{return true;}}
    false
}
fn discover(roots:&[String],warnings:&mut Vec<String>,excluded:&Path)->Result<Vec<PathBuf>>{
    let excluded=std::fs::canonicalize(excluded).map(|path|native_path(&path).to_lowercase()).unwrap_or_else(|_|native_path(excluded).to_lowercase());
    let mut directories=Vec::new();
    for root in roots {
        let path=PathBuf::from(root);
        if !path.is_absolute(){return Err(Error::InvalidArg("本地库目录必须是完整路径".into()));}
        match std::fs::canonicalize(&path){Ok(path) if path.is_dir()=>directories.push(path),_=>add_warning(warnings,format!("目录不可访问：{root}"))}
    }
    let mut seen_dirs=HashSet::new();let mut files=BTreeMap::new();
    while let Some(directory)=directories.pop(){
        let name=native_path(&directory).to_lowercase();
        if name==excluded||name.starts_with(&(excluded.clone()+"\\")){continue;}
        if !seen_dirs.insert(native_path(&directory).to_lowercase()){continue;}
        if seen_dirs.len()>50_000{return Err(Error::InvalidArg("目录过多，请选择更具体的元件库文件夹".into()));}
        let entries=match std::fs::read_dir(&directory){Ok(entries)=>entries,Err(error)=>{add_warning(warnings,format!("无法读取目录 {}：{error}",native_path(&directory)));continue;}};
        for entry in entries {
            let entry=match entry{Ok(entry)=>entry,Err(error)=>{add_warning(warnings,error.to_string());continue;}};
            let path=entry.path();
            let kind=match entry.file_type(){Ok(kind)=>kind,Err(error)=>{add_warning(warnings,error.to_string());continue;}};
            if kind.is_dir(){if !linked_directory(&path){directories.push(path);}continue;}
            if !kind.is_file(){continue;}
            let extension=path.extension().and_then(|s|s.to_str()).unwrap_or("").to_ascii_lowercase();
            if matches!(extension.as_str(),"schlib"|"pcblib"|"intlib"){
                files.insert(native_path(&path).to_lowercase(),path);
                if files.len()>20_000{return Err(Error::InvalidArg("库文件超过 20000 个，请分目录建立索引".into()));}
            }
            else if matches!(extension.as_str(),"dblib"|"svndblib"){add_warning(warnings,format!("暂不支持数据库库：{}",native_path(&path)));}
        }
    }
    Ok(files.into_values().collect())
}
async fn inspect(path:&Path,cache:&Path,index:usize)->Result<Vec<Entry>>{
    if std::fs::metadata(path)?.len()>256*1024*1024{return Err(Error::InvalidArg("库文件超过 256MB，已跳过".into()));}
    let extension=path.extension().and_then(|s|s.to_str()).unwrap_or("").to_ascii_lowercase();
    match extension.as_str(){
        "schlib"=>Ok(symbol_entries(&sch::Library::read(path).await?,path,path)),
        "pcblib"=>Ok(footprint_entries(&pcb::Library::read(path).await?,path,path)),
        "intlib"=>{
            let library=IntegratedLibrary::read(path).await?;
            let mut records=Vec::new();
            for (part,item) in library.schematic_libraries.iter().enumerate(){
                let extracted=cache.join(format!("{index}-symbol-{part}.SchLib"));
                // Only cache copies are authored. The original IntLib is never changed.
                std::fs::write(&extracted,item.library.to_bytes()?)?;
                records.extend(symbol_entries(&item.library,&extracted,path));
            }
            for (part,item) in library.footprint_libraries.iter().enumerate(){
                let extracted=cache.join(format!("{index}-footprint-{part}.PcbLib"));
                std::fs::write(&extracted,item.library.to_bytes()?)?;
                records.extend(footprint_entries(&item.library,&extracted,path));
            }
            Ok(records)
        }
        _=>Ok(Vec::new())
    }
}
pub async fn run(request_path:&Path,output:&Path)->Result<()> {
    let request:Request=serde_json::from_slice(&std::fs::read(request_path)?)?;
    if request.roots.is_empty()||request.roots.len()>32{return Err(Error::InvalidArg("请选择 1–32 个本地库目录".into()));}
    let cache=PathBuf::from(&request.cache_directory);
    if !cache.is_absolute()||!output.is_absolute(){return Err(Error::InvalidArg("索引输出和缓存必须使用完整路径".into()));}
    std::fs::create_dir_all(&cache)?;
    let excluded=cache.parent().and_then(Path::parent).ok_or_else(||Error::InvalidArg("索引缓存目录无效".into()))?;
    let mut warnings=Vec::new();let files=discover(&request.roots,&mut warnings,excluded)?;
    let previous=if !request.force {request.previous_index.as_deref().and_then(|path|{
        let value=read_catalog(Path::new(path)).ok()?;
        matches!(value.schema_version,2|3).then_some(value)
    })}else{None};
    let mut previous_sources=BTreeMap::new();let mut previous_records=BTreeMap::<String,Vec<Entry>>::new();let mut previous_segments=BTreeMap::<String,Vec<Segment>>::new();
    if let Some(old)=previous {
        for stamp in old.sources {previous_sources.insert(stamp.path.to_lowercase(),stamp);}
        if old.schema_version==2 {for item in old.records {previous_records.entry(item.source.to_lowercase()).or_default().push(item);}}
        else {for segment in old.segments {previous_segments.entry(segment.source.to_lowercase()).or_default().push(segment);}}
    }
    let mut writer=Writer::new(output)?;let mut sources=Vec::new();let mut cache_hits=0;
    for (index,path) in files.iter().enumerate(){
        println!("正在扫描 {}/{}：{}",index+1,files.len(),path.file_name().unwrap_or_default().to_string_lossy());
        let metadata=match std::fs::metadata(path){Ok(value)=>value,Err(error)=>{add_warning(&mut warnings,error.to_string());continue;}};
        let stamp=Stamp{path:native_path(path),length:metadata.len(),modified:metadata.modified().ok().and_then(|time|time.duration_since(std::time::UNIX_EPOCH).ok()).map(|value|value.as_nanos().to_string()).unwrap_or_default()};
        let key=stamp.path.to_lowercase();
        let unchanged=previous_sources.get(&key).is_some_and(|saved|!stamp.modified.is_empty()&&saved==&stamp);
        if unchanged {
            if let (Some(previous),Some(segments))=(request.previous_index.as_deref(),previous_segments.remove(&key)) {
                if !segments.is_empty()&&writer.reuse(Path::new(previous),&segments).is_ok(){cache_hits+=1;sources.push(stamp);continue;}
            }
        }
        let cached=previous_sources.get(&key).and_then(|saved|{
            if stamp.modified.is_empty()||saved!=&stamp{return None;}
            let entries=previous_records.remove(&key)?;
            let libraries:HashSet<_>=entries.iter().map(|item|item.library.as_str()).collect();
            (!entries.is_empty()&&libraries.iter().all(|item|Path::new(item).is_file())).then_some(entries)
        });
        let result=if let Some(entries)=cached {cache_hits+=1;Ok(entries)}else{inspect(path,&cache,index).await};
        match result{
            Ok(items)=>{
                if items.is_empty(){add_warning(&mut warnings,format!("未读到有效元件：{}",native_path(path)));}
                let mut seen=HashSet::new();let mut unique=Vec::new();
                for item in items {
                    let key=(item.kind.clone(),item.name.to_lowercase(),item.library.to_lowercase());
                    if seen.insert(key){unique.push(item);}
                }
                writer.write(&stamp.path,&unique)?;
            }
            Err(error)=>add_warning(&mut warnings,format!("跳过 {}：{error}",native_path(path)))
        }
        sources.push(stamp);
    }
    let index:Catalog=writer.finish(request.roots,files.len(),warnings,sources,cache_hits)?;
    println!("索引完成：{} 个库文件，{} 个元件，{} 个缓存段",index.scanned_files,index.record_count,index.segments.len());
    Ok(())
}
