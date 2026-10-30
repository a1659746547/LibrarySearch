use altium::sch;
#[tokio::main]
async fn main()->Result<(),Box<dyn std::error::Error>>{
 let args:Vec<String>=std::env::args().collect();
 let lib=sch::Library::read(&args[1]).await?;
 let template=sch::Document::read(&args[2]).await?;
 let mut doc=sch::Document::default();doc.header_parameters=template.header_parameters;doc.sheet_settings=template.sheet_settings;
 let mut c=lib.components[0].clone();c.designator_prefix=Some("U1".into());
 for p in &mut c.parameters {if p.name=="Designator" {p.value="U1".into();}}
 doc.components.push(c);doc.write(&args[3]).await?;
 altium::pcb::Document::default().write(&args[4]).await?;
 Ok(())
}
