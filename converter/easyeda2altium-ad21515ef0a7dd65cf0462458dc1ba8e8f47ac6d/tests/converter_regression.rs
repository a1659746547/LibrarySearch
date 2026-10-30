use altium::coord::Coord;
use altium::enums::TextJustification;
use altium::sch::Library;
use easyeda2altium::convert::symbol::{convert_symbol, register_symbol_text_fonts};
use easyeda2altium::easyeda::parser::parse_symbol_shape;
use easyeda2altium::easyeda::types::EeSymbol;

fn symbol() -> EeSymbol {
    let mut symbol = EeSymbol::default();
    symbol.info.name = "ConversionRegression".into();
    symbol.info.prefix = "LED?".into();
    symbol.bbox.x = 400.0;
    symbol.bbox.y = 300.0;
    symbol
}

fn annotation(mark: &str, text: &str, visible: &str, rotation: &str) -> String {
    ["T", mark, "390", "300", rotation, "#0000FF", "Arial", "9pt", "bold", "italic",
        "middle", "comment", text, visible, "end", "gge1", "0"].join("~")
}

#[test]
fn annotation_metadata_is_not_duplicated() {
    let mut source = symbol();
    for mark in ["N", "P", "PK"] { parse_symbol_shape(&annotation(mark,"metadata","1","0"), &mut source); }
    parse_symbol_shape(&annotation("L","normal","1","90"), &mut source);
    parse_symbol_shape(&annotation("L","hidden","0","0"), &mut source);
    assert_eq!(source.texts.len(),2);
    assert_eq!(source.texts[0].font_family,"Arial");
    let converted=convert_symbol(&source,&[],false);
    assert_eq!(converted.labels.len(),2);
    assert_eq!(converted.labels[0].location.x,Coord::from_mils(-100.0));
    assert_eq!(converted.labels[0].location.y,Coord::ZERO);
    assert_eq!(converted.labels[0].justification,TextJustification::MiddleRight);
    assert_eq!(converted.labels[0].rotation,90.0);
    assert_eq!(converted.labels[0].color,0xff0000);
    assert!(!converted.labels[0].is_hidden);
    assert!(converted.labels[1].is_hidden);
    assert_eq!(converted.labels[0].common.owner_part_id,1);
}

#[test]
fn annotation_units_and_font_slots_are_preserved() {
    for custom in [None, Some("Tahoma")] {
        let mut source=symbol();
        parse_symbol_shape(&annotation("L","first","1","-90"),&mut source);
        let mut unit=EeSymbol::default();
        parse_symbol_shape(&annotation("L","second","1","0"),&mut unit);
        source.sub_symbols.push(unit);
        let mut converted=convert_symbol(&source,&[],custom.is_some());
        let mut library=Library::default();
        register_symbol_text_fonts(&mut library,&source,&mut converted,custom);
        assert_eq!(converted.labels[0].rotation,270.0);
        assert_eq!(converted.labels[1].common.owner_part_id,2);
        assert_eq!(converted.labels[0].font_id,converted.labels[1].font_id);
        let id=converted.labels[0].font_id;
        assert_eq!(id,if custom.is_some(){4}else{2});
        assert_eq!(library.file_header_parameters[&format!("Size{id}")],"9");
        assert_eq!(library.file_header_parameters[&format!("FontName{id}")],custom.unwrap_or("Arial"));
        assert_eq!(library.file_header_parameters[&format!("Bold{id}")],"T");
        assert_eq!(library.file_header_parameters[&format!("Italic{id}")],"T");
    }
}

#[test]
fn font_slots_do_not_collide_across_components() {
    let mut library=Library::default();
    let mut source=symbol();
    parse_symbol_shape(&annotation("L","first","1","0"),&mut source);
    let mut first=convert_symbol(&source,&[],false);
    register_symbol_text_fonts(&mut library,&source,&mut first,None);
    source.texts[0].font_size_mm=12.0*25.4/72.0;
    let mut second=convert_symbol(&source,&[],false);
    register_symbol_text_fonts(&mut library,&source,&mut second,None);
    assert_ne!(first.labels[0].font_id,second.labels[0].font_id);
    assert_eq!(library.file_header_parameters["FontIdCount"],"3");
}

#[test]
fn native_circular_arc_keeps_quarter_sweep_instead_of_full_circle() {
    for (path, expected_start, expected_end) in [
        ("M410 300 A10 10 0 0 1 400 310",270.0,0.0),
        ("M410 300 A10 10 0 0 0 400 290",0.0,90.0),
    ] {
        let mut source=symbol();
        parse_symbol_shape(&format!("A~{path}~~#000000~1~0~none~gge1~0"),&mut source);
        let converted=convert_symbol(&source,&[],false);
        assert_eq!(converted.arcs.len(),1);
        let arc=&converted.arcs[0];
        assert!(arc.center.x.to_mils().abs()<0.001);
        assert!(arc.center.y.to_mils().abs()<0.001);
        assert_eq!(arc.radius,Coord::from_mils(100.0));
        assert!((arc.start_angle-expected_start).abs()<1e-7);
        assert!((arc.end_angle-expected_end).abs()<1e-7);
    }
}

#[test]
fn rotated_elliptical_arc_is_not_substituted_with_a_circle() {
    let mut source=symbol();
    parse_symbol_shape("A~M410 300 A20 10 30 1 1 400 310~~#000000~1~0~none~gge1~0",&mut source);
    let converted=convert_symbol(&source,&[],false);
    assert!(converted.arcs.is_empty());
    assert_eq!(converted.polylines.len(),1);
    let p=&converted.polylines[0].vertices;
    assert!(p.len()>5);
    assert_eq!(p[0].x,Coord::from_mils(100.0));
    assert_eq!(p.last().unwrap().y,Coord::from_mils(-100.0));
}

#[test]
fn pt_curves_are_emitted_and_invalid_paths_are_atomic() {
    let mut source=symbol();
    parse_symbol_shape("PT~M400 300 C400 320 420 320 420 300 S440 280 440 300~#000000~1~0~none~gge1~0",&mut source);
    assert_eq!(source.polylines.len(),1);
    assert!(source.polylines[0].points.len()>5);
    parse_symbol_shape("PT~M400 300 L410 300 Q420~#000000~1~0~none~gge2~0",&mut source);
    assert_eq!(source.polylines.len(),1);
    let converted=convert_symbol(&source,&[],false);
    assert_eq!(converted.polylines.len(),1);
}

#[tokio::test]
async fn schlib_roundtrip_preserves_arc_text_fonts_and_curve_vertices() {
    let mut source=symbol();
    parse_symbol_shape(&annotation("L","内部文字 / annotation","0","90"),&mut source);
    parse_symbol_shape(&annotation("L","Café €","1","0"),&mut source);
    parse_symbol_shape("A~M410 300 A10 10 0 0 1 400 310~~#000000~1~0~none~gge1~0",&mut source);
    parse_symbol_shape("PT~M400 300 Q410 320 420 300~#000000~1~0~none~gge2~0",&mut source);
    let mut component=convert_symbol(&source,&[],false);
    let mut library=Library::default();
    register_symbol_text_fonts(&mut library,&source,&mut component,None);
    library.components.push(component.clone());
    let path=std::env::temp_dir().join(format!("librarysearch-regression-{}-{}.SchLib",std::process::id(),
        std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
    library.write(&path).await.unwrap();
    let result=Library::read(&path).await;
    std::fs::remove_file(&path).unwrap();
    let actual=result.unwrap();
    let actual_component=&actual.components[0];
    assert_eq!(actual_component.labels.len(),2);
    assert_eq!(actual_component.labels[1].text,"Café €");
    let a=&actual_component.labels[0]; let b=&component.labels[0];
    assert_eq!(a.text,b.text); assert_eq!(a.location,b.location); assert_eq!(a.rotation,b.rotation);
    assert_eq!(a.is_hidden,b.is_hidden); assert_eq!(a.justification,b.justification);
    assert_eq!(a.color,b.color);
    assert_eq!(a.common.owner_part_id,1); assert_eq!(a.font_id,b.font_id);
    assert_eq!(actual.file_header_parameters[&format!("Size{}",a.font_id)],"9");
    assert_eq!(actual.file_header_parameters[&format!("FontName{}",a.font_id)],"Arial");
    assert_eq!(actual_component.arcs[0].start_angle,component.arcs[0].start_angle);
    assert_eq!(actual_component.arcs[0].end_angle,component.arcs[0].end_angle);
    assert_eq!(actual_component.polylines[0].vertices,component.polylines[0].vertices);
}
