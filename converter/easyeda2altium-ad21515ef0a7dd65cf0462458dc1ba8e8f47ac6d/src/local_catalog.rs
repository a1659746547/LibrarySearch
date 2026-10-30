//! Bounded, offline storage and search for large local libraries.
use std::{cmp::Ordering,collections::BinaryHeap,io::{Write,BufRead,Read,BufReader,BufWriter},path::{Path,PathBuf}};
use serde::{Deserialize,Serialize};
use crate::{Error,Result,local_index::Entry};

const MAX_JSON:u64=64*1024*1024;
const SEGMENT_BYTES:usize=8*1024*1024;
const SEGMENT_ROWS:usize=10_000;
const PREVIEW:usize=500;

#[derive(Clone,Deserialize,Serialize,PartialEq,Debug)]
#[serde(rename_all="PascalCase")]
pub struct Stamp {pub path:String,pub length:u64,pub modified:String}
#[derive(Clone,Deserialize,Serialize,Debug)]
#[serde(rename_all="PascalCase")]
pub struct Segment {pub file:String,pub count:usize,pub source:String}
#[derive(Deserialize,Serialize,Default)]
#[serde(rename_all="PascalCase")]
pub struct Catalog {
 pub schema_version:u32,pub roots:Vec<String>,pub scanned_files:usize,pub records:Vec<Entry>,pub warnings:Vec<String>,
 #[serde(default)]pub sources:Vec<Stamp>,#[serde(default)]pub cache_hits:usize,
 #[serde(default)]pub record_count:usize,#[serde(default)]pub segments:Vec<Segment>,
 #[serde(default)]pub records_file:String,
}
#[derive(Deserialize)]
#[serde(rename_all="PascalCase")]
pub struct SearchRequest {pub index:String,#[serde(default)]pub query:String,#[serde(default)]pub kind:u8}
#[derive(Serialize,Deserialize)]
#[serde(rename_all="PascalCase")]
pub struct SearchResult {pub schema_version:u32,pub total:usize,pub records:Vec<Entry>}
#[derive(Deserialize)]
#[serde(rename_all="PascalCase")]
struct SegmentData {records:Vec<Entry>}

#[derive(Clone)]
struct Hit {name:String,kind:String,order:usize,entry:Entry}
impl PartialEq for Hit {fn eq(&self,other:&Self)->bool{self.cmp(other)==Ordering::Equal}}
impl Eq for Hit {}
impl PartialOrd for Hit {fn partial_cmp(&self,other:&Self)->Option<Ordering>{Some(self.cmp(other))}}
impl Ord for Hit {fn cmp(&self,other:&Self)->Ordering{self.name.cmp(&other.name).then(self.kind.cmp(&other.kind)).then(self.order.cmp(&other.order))}}
#[derive(Clone,Default)]
pub struct Best {heap:BinaryHeap<Hit>,order:usize}
impl Best {
 pub fn observe(&mut self,entry:&Entry){
  let order=self.order;self.order+=1;let name=entry.name.to_lowercase();
  if self.heap.len()==PREVIEW {
   let worst=self.heap.peek().unwrap();
   if name.cmp(&worst.name).then(entry.kind.cmp(&worst.kind)).then(order.cmp(&worst.order))!=Ordering::Less{return;}
   self.heap.pop();
  }
  self.heap.push(Hit{name,kind:entry.kind.clone(),order,entry:entry.clone()});
 }
 pub fn records(self)->Vec<Entry>{self.heap.into_sorted_vec().into_iter().map(|item|item.entry).collect()}
}

pub fn read_catalog(path:&Path)->Result<Catalog>{
 if std::fs::metadata(path)?.len()>MAX_JSON{return Err(Error::InvalidArg("本地库清单过大，请重新扫描".into()));}
 Ok(serde_json::from_slice(&std::fs::read(path)?)?)
}
fn segment_root(index:&Path)->Result<PathBuf>{
 Ok(index.parent().and_then(Path::parent).ok_or_else(||Error::InvalidArg("本地库清单位置无效".into()))?.join("Segments"))
}
fn valid_segment_name(name:&str)->bool{
 name.len()<=160&&name.starts_with("segment-")&&name.ends_with(".json")&&name.bytes().all(|b|b.is_ascii_alphanumeric()||matches!(b,b'-'|b'_'|b'.'))
}
pub fn read_segment(index:&Path,segment:&Segment)->Result<Vec<Entry>>{
 if !valid_segment_name(&segment.file){return Err(Error::InvalidArg("本地库缓存段路径无效，请重新扫描".into()));}
 let root=segment_root(index)?;let path=root.join(&segment.file);
 if crate::local_index::linked_directory(&root)||crate::local_index::linked_directory(&path){return Err(Error::InvalidArg("本地库缓存段被重定向，请重新扫描".into()));}
 if std::fs::metadata(&path)?.len()>MAX_JSON{return Err(Error::InvalidArg("本地库缓存段过大，请重新扫描".into()));}
 let data:SegmentData=serde_json::from_slice(&std::fs::read(path)?)?;
 if data.records.len()!=segment.count||data.records.iter().any(|e|!e.source.eq_ignore_ascii_case(&segment.source)){
  return Err(Error::InvalidArg("本地库缓存段与清单不一致，请重新扫描".into()));
 }
 Ok(data.records)
}

pub struct Writer {output:PathBuf,root:PathBuf,prefix:String,search:BufWriter<std::fs::File>,pub segments:Vec<Segment>,pub count:usize,pub best:Best}
impl Writer {
 pub fn new(output:&Path)->Result<Self>{
  let root=segment_root(output)?;std::fs::create_dir_all(&root)?;
  if crate::local_index::linked_directory(&root){return Err(Error::InvalidArg("本地库缓存目录被重定向".into()));}
  let prefix=output.parent().and_then(Path::file_name).and_then(|s|s.to_str()).filter(|s|s.len()<=100&&s.bytes().all(|b|b.is_ascii_alphanumeric()||matches!(b,b'-'|b'_'))).ok_or_else(||Error::InvalidArg("本地库快照名称无效".into()))?.to_owned();
  let search=BufWriter::new(std::fs::OpenOptions::new().write(true).create_new(true).open(output.parent().unwrap().join("search-records.ndjson"))?);
  Ok(Self{output:output.into(),root,prefix,search,segments:Vec::new(),count:0,best:Best::default()})
 }
 fn flush(&mut self,source:&str,bytes:&mut Vec<u8>,rows:&mut usize)->Result<()>{
  if *rows==0{return Ok(());}bytes.extend_from_slice(b"]}");
  let name=format!("segment-{}-{:06}.json",self.prefix,self.segments.len());
  let destination=self.root.join(&name);
  let mut file=std::fs::OpenOptions::new().write(true).create_new(true).open(destination)?;
  file.write_all(bytes)?;file.sync_all()?;
  self.segments.push(Segment{file:name,count:*rows,source:source.into()});bytes.clear();bytes.extend_from_slice(b"{\"Records\":[");*rows=0;Ok(())
 }
 pub fn write(&mut self,source:&str,entries:&[Entry])->Result<()>{
  let mut bytes=b"{\"Records\":[".to_vec();let mut rows=0;
  for entry in entries {
   let encoded=serde_json::to_vec(entry)?;
   if encoded.len()>MAX_JSON as usize-1024{return Err(Error::InvalidArg("单条本地库元数据异常过大".into()));}
   if rows>0&&(rows>=SEGMENT_ROWS||bytes.len()+encoded.len()+3>SEGMENT_BYTES){self.flush(source,&mut bytes,&mut rows)?;}
   if rows>0{bytes.push(b',');}bytes.extend_from_slice(&encoded);rows+=1;self.count+=1;self.best.observe(entry);self.search.write_all(&encoded)?;self.search.write_all(b"\n")?;
  }
  self.flush(source,&mut bytes,&mut rows)
 }
 pub fn reuse(&mut self,previous:&Path,segments:&[Segment])->Result<()>{
  // Both snapshots must refer to the same append-only segment store.
  if std::fs::canonicalize(segment_root(previous)?)?!=std::fs::canonicalize(&self.root)?{return Err(Error::InvalidArg("本地库旧缓存位置不同，将重新扫描".into()));}
  self.search.flush()?;
  let checkpoint=(self.count,self.best.clone(),self.segments.len(),self.search.get_ref().metadata()?.len());
  let result=(||{
   let mut checked=std::collections::HashSet::new();
   for segment in segments {
    let entries=read_segment(previous,segment)?;
    for entry in &entries {
     if checked.insert(entry.library.clone())&&!Path::new(&entry.library).is_file(){return Err(Error::InvalidArg("本地库提取缓存缺失".into()));}
     self.best.observe(entry);self.count+=1;serde_json::to_writer(&mut self.search,entry)?;self.search.write_all(b"\n")?;
    }
    self.segments.push(segment.clone());
   }Ok(())
  })();
  if result.is_err(){self.search.flush()?;self.search.get_ref().set_len(checkpoint.3)?;use std::io::Seek;self.search.seek(std::io::SeekFrom::End(0))?;self.count=checkpoint.0;self.best=checkpoint.1;self.segments.truncate(checkpoint.2);}
  result
 }
 pub fn finish(mut self,roots:Vec<String>,scanned_files:usize,warnings:Vec<String>,sources:Vec<Stamp>,cache_hits:usize)->Result<Catalog>{
  self.search.flush()?;self.search.get_ref().sync_all()?;
  let index=Catalog{schema_version:3,roots,scanned_files,records:self.best.records(),warnings,sources,cache_hits,record_count:self.count,segments:self.segments,records_file:"search-records.ndjson".into()};
  let bytes=serde_json::to_vec(&index)?;
  if bytes.len()>MAX_JSON as usize{return Err(Error::InvalidArg("本地库清单元数据过大".into()));}
  std::fs::write(&self.output,bytes)?;Ok(index)
 }
}

pub fn search(request:&SearchRequest)->Result<SearchResult>{
 if request.kind>2||request.query.len()>16_384{return Err(Error::InvalidArg("本地库搜索条件无效".into()));}
 let path=Path::new(&request.index);let index=read_catalog(path)?;
 if index.schema_version!=3||index.records.len()>PREVIEW||index.segments.iter().any(|s|!valid_segment_name(&s.file))||index.segments.iter().try_fold(0usize,|n,s|n.checked_add(s.count))!=Some(index.record_count){return Err(Error::InvalidArg("本地库清单格式无效，请重新扫描".into()));}
 let query=request.query.trim().to_lowercase();
 if index.records_file!="search-records.ndjson"{return Err(Error::InvalidArg("本地库搜索数据路径无效".into()));}
 if query.is_empty()&&request.kind==0{return Ok(SearchResult{schema_version:1,total:index.record_count,records:index.records});}
 let data=path.parent().ok_or_else(||Error::InvalidArg("本地库清单位置无效".into()))?.join(&index.records_file);
 if crate::local_index::linked_directory(&data){return Err(Error::InvalidArg("本地库搜索数据被重定向".into()));}
 let mut reader=BufReader::new(std::fs::File::open(data)?);let mut line=Vec::new();let mut read_count=0;
 let mut best=Best::default();let mut total=0;
 loop {
   line.clear();let bytes=reader.by_ref().take(MAX_JSON+1).read_until(b'\n',&mut line)?;if bytes==0{break;}if bytes as u64>MAX_JSON{return Err(Error::InvalidArg("本地库搜索记录异常过大".into()));}
   let entry:Entry=serde_json::from_slice(&line)?;read_count+=1;
   if (request.kind==1&&entry.kind!="Symbol")||(request.kind==2&&entry.kind!="Footprint"){continue;}
   let code=Path::new(&entry.source).file_stem().and_then(|s|s.to_str()).unwrap_or("");
   if !query.is_empty()&&![entry.name.as_str(),code,entry.models.as_str(),entry.description.as_str()].iter().any(|s|s.to_lowercase().contains(&query)){continue;}
   total+=1;best.observe(&entry);
 }
 if read_count!=index.record_count{return Err(Error::InvalidArg("本地库搜索数据不完整，请重新扫描".into()));}
 Ok(SearchResult{schema_version:1,total,records:best.records()})
}
pub fn run_search(request:&Path,output:&Path)->Result<()>{
 if std::fs::metadata(request)?.len()>128*1024{return Err(Error::InvalidArg("本地库搜索请求过大".into()));}
 let request:SearchRequest=serde_json::from_slice(&std::fs::read(request)?)?;
 let result=search(&request)?;std::fs::write(output,serde_json::to_vec(&result)?)?;Ok(())
}
