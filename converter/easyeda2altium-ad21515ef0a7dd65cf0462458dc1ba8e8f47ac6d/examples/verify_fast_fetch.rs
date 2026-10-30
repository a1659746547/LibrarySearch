use altium::pcb::Library;
use easyeda2altium::convert::model3d::normalize_step;
use easyeda2altium::easyeda::api::description_from_product_page;
use serde_json::json;
fn id(value:&str)->String {value.chars().filter(|c|c.is_ascii_hexdigit()).flat_map(char::to_uppercase).collect()}
#[tokio::main]
async fn main()->Result<(),Box<dyn std::error::Error>> {
    let args:Vec<_>=std::env::args().collect();
    if args[1]=="--description" {
        let html=std::fs::read_to_string(&args[2])?;
        let text=description_from_product_page(&html,&args[3]).ok_or("verified product description missing")?;
        std::fs::write(&args[4],serde_json::to_vec_pretty(&json!({"Description":text}))?)?;
        println!("Verified source description: {text}");return Ok(());
    }
    let library=Library::read(&args[2]).await?;
    let bodies:Vec<_>=library.components.iter().flat_map(|c|c.component_bodies.iter()).collect();
    if args[1]=="--quick" {assert!(library.models.is_empty());assert!(bodies.is_empty());println!("Quick PcbLib has no dangling 3D resources/bodies");return Ok(());}
    assert!(!library.models.is_empty());assert!(!bodies.is_empty());
    for model in &library.models {assert!(model.is_embedded);assert!(model.step_data.is_ascii());normalize_step(model.step_data.as_bytes())?;}
    for body in &bodies {let linked=body.model_id.as_ref().ok_or("body has no model ID")?;assert!(library.models.iter().any(|m|id(&m.id)==id(linked)));}
    let report=json!({"EmbeddedModels":library.models.len(),"Bodies":bodies.len(),"StepCharacters":library.models.iter().map(|m|m.step_data.len()).sum::<usize>(),"AsciiStep":true,"ModelLinksMatch":true});
    std::fs::write(&args[3],serde_json::to_vec_pretty(&report)?)?;
    println!("{report}");Ok(())
}
