//! Offline diagnostic: emit and reopen SchLibs, then export serialized pin geometry.
//! Independent expected SVG endpoints are checked by tests/Audit-Pin-Geometry.py.
use std::path::Path;
use altium::sch::Library;
use easyeda2altium::{convert::{appearance::Appearance, convert_symbol, symbol::register_symbol_text_fonts}, easyeda::parser::parse_symbol};
use serde_json::{json, Value};

fn geometry(library: &Library) -> Value {
    json!(library.components.iter().flat_map(|component| component.pins.iter().map(|pin| json!({
        "Part": pin.common.owner_part_id, "Number": pin.designator, "Name": pin.name,
        "Body": [pin.location.x.to_mils(), pin.location.y.to_mils()],
        "Length": pin.length.to_mils(), "Direction": pin.orientation as i32,
        "Electrical": pin.electrical_type as i32, "Hidden": pin.is_hidden,
        "ShowName":pin.show_name,"ShowNumber":pin.show_designator,"FormalType":pin.formal_type,
        "Symbols":[pin.symbol_inner_edge,pin.symbol_outer_edge,pin.symbol_inside,pin.symbol_outside]
    }))).collect::<Vec<_>>())
}

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    let args: Vec<_> = std::env::args().collect();
    if args.get(1).map(String::as_str) == Some("--library") {
        let library = Library::read(&args[2]).await?;
        std::fs::write(&args[3], serde_json::to_vec_pretty(&geometry(&library))?)?;
        return Ok(());
    }
    if args.len() != 4 { return Err("usage: audit_pin_geometry SOURCE_DIR OUTPUT_DIR MODERN_PROFILE".into()); }
    let output=Path::new(&args[2]); std::fs::create_dir_all(output)?;
    let modern=Appearance::load(&args[3])?;
    let mut sources=std::fs::read_dir(&args[1])?.collect::<std::result::Result<Vec<_>,_>>()?;
    sources.sort_by_key(|entry| entry.file_name());
    for entry in sources {
        let path=entry.path(); if path.extension().and_then(|value| value.to_str())!=Some("json") { continue; }
        let value:Value=serde_json::from_slice(&std::fs::read(&path)?)?;
        let source=parse_symbol(value.get("result").unwrap_or(&value))?;
        let code=path.file_stem().unwrap().to_string_lossy();
        for name in ["classic", "modern"] {
            let mut library=Library::default();
            let mut component=convert_symbol(&source,&[],false);
            register_symbol_text_fonts(&mut library,&source,&mut component,None);
            if name=="modern" { modern.apply(&mut library,&source,&mut component)?; }
            library.components.push(component);
            let generated=output.join(format!("{code}-{name}.SchLib"));
            library.write(&generated).await?;
            let reopened=Library::read(&generated).await?;
            std::fs::write(output.join(format!("{code}-{name}.json")),serde_json::to_vec_pretty(&geometry(&reopened))?)?;
        }
        println!("Emitted and reopened {code}: {}",source.info.name);
    }
    Ok(())
}
