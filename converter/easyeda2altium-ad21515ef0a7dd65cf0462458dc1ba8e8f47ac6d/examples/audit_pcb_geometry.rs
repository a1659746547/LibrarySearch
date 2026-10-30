//! Read back emitted PCB geometry for an independently inspectable report.
use altium::pcb::Library;
use serde_json::json;
#[tokio::main]
async fn main() {
 let path=std::env::args().nth(1).expect("PcbLib path");
 let library=Library::read(&path).await.expect("read generated PcbLib");
 let mut reports=Vec::new();
 for component in library.components {
  let point=|p:&altium::coord::CoordPoint|json!([p.x.to_mm(),p.y.to_mm()]);
  reports.push(json!({"name":component.name,"pads":component.pads.iter().map(|p|json!({"number":p.designator,"point":point(&p.location)})).collect::<Vec<_>>(),
   "regions":component.regions.iter().map(|r|json!({"layer":r.layer,"outline":r.outline.iter().map(point).collect::<Vec<_>>()})).collect::<Vec<_>>(),
   "bodies":component.component_bodies.iter().map(|b|json!({"outline":b.outline.iter().map(point).collect::<Vec<_>>()})).collect::<Vec<_>>() }));
 }
 println!("{}",serde_json::to_string_pretty(&reports).unwrap());
}
