use easyeda2altium::{local_catalog::{Writer,search,SearchRequest,read_catalog},local_index::Entry};
use std::path::PathBuf;
fn fixture(label:&str,count:usize)->PathBuf{
 let base=std::env::temp_dir().join(format!("ls-catalog-{}-{label}",std::process::id()));let snapshot=base.join("original");std::fs::create_dir_all(&snapshot).unwrap();
 let mut writer=Writer::new(&snapshot.join("index.json")).unwrap();
 for begin in (0..count).step_by(5000){let rows:Vec<_>=(begin..(begin+5000).min(count)).rev().map(|n|Entry{name:format!("PART_{n:07}"),kind:if n%2==0{"Symbol"}else{"Footprint"}.into(),library:base.join("library.SchLib").to_string_lossy().into_owned(),source:"source.SchLib".into(),models:"0603".into(),description:if n==count-1{"末尾"}else{"precision"}.into()}).collect();writer.write("source.SchLib",&rows).unwrap();}
 writer.finish(vec![],1,vec![],vec![],0).unwrap();snapshot.join("index.json")
}
#[test] fn more_than_two_hundred_thousand_entries_survive_storage_and_search(){
 let path=fixture("large",200_001);let index=read_catalog(&path).unwrap();assert_eq!(index.record_count,200_001);assert_eq!(index.records.len(),500);assert!(index.segments.len()>1);
 let result=search(&SearchRequest{index:path.to_string_lossy().into_owned(),query:"part_0200000".into(),kind:0}).unwrap();assert_eq!(result.total,1);assert_eq!(result.records[0].name,"PART_0200000");
 let all=search(&SearchRequest{index:path.to_string_lossy().into_owned(),query:"precision".into(),kind:0}).unwrap();assert_eq!(all.total,200_000);assert_eq!(all.records.len(),500);assert_eq!(all.records[0].name,"PART_0000000");assert_eq!(all.records[499].name,"PART_0000499");
 let symbols=search(&SearchRequest{index:path.to_string_lossy().into_owned(),query:"末尾".into(),kind:1}).unwrap();assert_eq!(symbols.total,1);
}
#[test] fn kind_filter_and_code_match_preserve_full_counts(){
 let path=fixture("filter",1001);let result=search(&SearchRequest{index:path.to_string_lossy().into_owned(),query:"SOURCE".into(),kind:2}).unwrap();assert_eq!(result.total,500);assert!(result.records.iter().all(|r|r.kind=="Footprint"));
}
#[test] fn invalid_segment_paths_are_rejected_before_any_lookup(){
 let path=fixture("path",10);let mut data=read_catalog(&path).unwrap();data.segments[0].file="../secret.json".into();std::fs::write(&path,serde_json::to_vec(&data).unwrap()).unwrap();assert!(search(&SearchRequest{index:path.to_string_lossy().into_owned(),query:"".into(),kind:0}).is_err());
}
#[test] fn missing_or_truncated_search_data_is_not_reported_as_zero_hits(){
 let path=fixture("truncated",10);std::fs::write(path.parent().unwrap().join("search-records.ndjson"),b"").unwrap();assert!(search(&SearchRequest{index:path.to_string_lossy().into_owned(),query:"x".into(),kind:0}).is_err());
}
