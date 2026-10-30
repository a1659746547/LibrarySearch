//! Refresh the official standard CAD cache before invoking the legacy converter.
use std::{path::Path,time::{Duration,SystemTime},io::Write};
use serde_json::Value;
use crate::{Result,Error};
fn validate(text:&[u8],code:&str)->Result<()> {
 let value:Value=serde_json::from_slice(text)?;
 if value.get("success")==Some(&Value::Bool(false))||value.pointer("/result/lcsc/number").and_then(Value::as_str)!=Some(code){return Err(Error::Api("official CAD returned a different or missing C number".into()));}Ok(())
}
pub async fn prepare(cache:&Path,code:&str,refresh:bool)->Result<()> {
 std::fs::create_dir_all(cache)?;let destination=cache.join(format!("{code}.json"));
 if !refresh&&destination.metadata().and_then(|m|m.modified()).ok().and_then(|time|SystemTime::now().duration_since(time).ok()).is_some_and(|age|age<Duration::from_secs(86400)){
  if let Ok(bytes)=std::fs::read(&destination){if bytes.len()<=64*1024*1024&&validate(&bytes,code).is_ok(){eprintln!("source-cache: verified existing {code}");return Ok(());}}
 }
 let _=rustls::crypto::ring::default_provider().install_default();
 let client=reqwest::Client::builder().user_agent("Mozilla/5.0 LibrarySearch-Source/0.6.1-dev.18").connect_timeout(Duration::from_secs(6)).timeout(Duration::from_secs(20)).build()?;
 let mut last="no usable official response".to_string();
 for host in ["lceda.cn","easyeda.com"]{
  let response=client.get(format!("https://{host}/api/products/{code}/components")).send().await;
  let mut response=match response{Ok(r)if r.status().is_success()=>r,Ok(r)=>{last=format!("{host}: HTTP {}",r.status());continue;},Err(error)=>{last=error.to_string();continue;}};
  let mut bytes=Vec::new();let mut error=false;
  loop{match response.chunk().await{Ok(Some(chunk))=>{if bytes.len()+chunk.len()>64*1024*1024{last="source response too large".into();error=true;break;}bytes.extend_from_slice(&chunk);},Ok(None)=>break,Err(e)=>{last=e.to_string();error=true;break;}}}
  if error{continue;}if let Err(e)=validate(&bytes,code){last=e.to_string();continue;}
  let temporary=cache.join(format!(".{code}.{}.tmp",std::process::id()));
  let result=(||->Result<()>{let mut file=std::fs::OpenOptions::new().create_new(true).write(true).open(&temporary)?;file.write_all(&bytes)?;file.sync_all()?;drop(file);std::fs::rename(&temporary,&destination)?;Ok(())})();
  if temporary.exists(){let _=std::fs::remove_file(&temporary);}result?;
  eprintln!("source-cache: {code} from https://{host}/ (嘉立创EDA/EasyEDA官方库; https://lceda.cn/ https://easyeda.com/)");return Ok(());
 }
 Err(Error::Api(format!("official CAD unavailable: {last}")))
}
#[cfg(test)]mod tests{
 use super::*;
 #[test]fn verify_source_identity(){assert!(validate(br#"{"success":true,"result":{"lcsc":{"number":"C1"}}}"#,"C1").is_ok());assert!(validate(br#"{"success":true,"result":{"lcsc":{"number":"C2"}}}"#,"C1").is_err());assert!(validate(br#"{"success":false,"result":{"lcsc":{"number":"C1"}}}"#,"C1").is_err());}
}
