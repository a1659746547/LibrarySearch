//! Synthetic metadata fixture; does not contain user libraries or settings.
use std::{path::PathBuf,time::UNIX_EPOCH};
use easyeda2altium::{local_catalog::{Writer,Stamp},local_index::Entry};
fn main()->Result<(),Box<dyn std::error::Error>> {
 let base=PathBuf::from(std::env::args().nth(1).expect("fixture directory"));
 if base.exists(){return Err("Preserve the previous fixture; supply a new directory".into());}
 let input=base.join("inputs");let cache=base.join("cache");let snapshot=cache.join("original");std::fs::create_dir_all(&input)?;std::fs::create_dir_all(&snapshot)?;
 let mut writer=Writer::new(&snapshot.join("index.json"))?;let mut stamps=Vec::new();
 for (kind,filename,start) in [("Symbol","symbols.SchLib",0usize),("Footprint","footprints.PcbLib",1usize)] {
  let source=input.join(filename);
  let bytes=if kind=="Symbol"{altium::sch::Library::default().to_bytes()?}else{altium::pcb::Library::default().to_bytes()?};
  std::fs::write(&source,bytes)?;let metadata=std::fs::metadata(&source)?;
  let source=source.to_string_lossy().into_owned();
  let entries:Vec<_>=(start..250_001).step_by(2).map(|n|Entry{name:format!("PART_{n:07}"),kind:kind.into(),source:source.clone(),library:source.clone(),models:"QFN32".into(),description:if n==250_000{"末尾目标 tail component".into()}else{"本地元件 precision component".into()}}).collect();
  writer.write(&source,&entries)?;stamps.push(Stamp{path:source,length:metadata.len(),modified:metadata.modified()?.duration_since(UNIX_EPOCH)?.as_nanos().to_string()});
 }
 let data=writer.finish(vec![input.to_string_lossy().into_owned()],2,vec![],stamps,0)?;
 let summary=serde_json::json!({"IndexPath":snapshot.join("index.json").to_string_lossy(),"Input":input.to_string_lossy(),"Cache":cache.to_string_lossy(),"Count":data.record_count,"Preview":data.records.len(),"Segments":data.segments.len(),"SearchBytes":std::fs::metadata(snapshot.join("search-records.ndjson"))?.len()});
 std::fs::write(base.join("fixture.json"),serde_json::to_vec_pretty(&summary)?)?;println!("{summary}");Ok(())
}
