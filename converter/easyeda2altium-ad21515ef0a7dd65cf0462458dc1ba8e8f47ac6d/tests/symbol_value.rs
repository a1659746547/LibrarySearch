use easyeda2altium::{convert::convert_symbol,easyeda::parser::parse_symbol};
use serde_json::{json,Value};
fn convert(parameters:Value)->altium::sch::Component {
    let source=json!({"lcsc":{"number":"C1"},"dataStr":{"head":{"x":0,"y":0,"c_para":parameters},"shape":[]}});
    convert_symbol(&parse_symbol(&source).unwrap(),&[],false)
}
fn parameter<'a>(component:&'a altium::sch::Component,name:&str)->&'a str {
    &component.parameters.iter().find(|p|p.name==name).unwrap().value
}
#[test]
fn electrical_value_and_manufacturer_part_remain_distinct_after_library_roundtrip(){
    for (prefix,value,mpn) in [("R?","10kΩ","0603WAF1002T5E"),("C?","100nF","CC0603KRX7R9BB104"),("L?","4.7uH","NR3015T4R7M")] {
        let component=convert(json!({"name":"SOURCE_NAME","pre":prefix,"Value":value,"Manufacturer":"VENDOR","Manufacturer Part":mpn}));
        assert_eq!(parameter(&component,"Comment"),value);
        assert_eq!(parameter(&component,"Value"),value);
        assert_eq!(parameter(&component,"Manufacturer Part"),mpn);
        let mut library=altium::sch::Library::default();library.components.push(component);
        let read=altium::sch::Library::from_bytes(library.to_bytes().unwrap()).unwrap();
        assert_eq!(parameter(&read.components[0],"Comment"),value);
        assert_eq!(parameter(&read.components[0],"Value"),value);
    }
}
#[test]
fn lowercase_value_is_preserved_and_missing_values_use_source_identity(){
    let component=convert(json!({"name":"R0603","pre":"R?","value":"0Ω","Manufacturer Part":"PART0"}));
    assert_eq!(parameter(&component,"Comment"),"0Ω");
    let resistor=convert(json!({"name":"10kΩ","pre":"R?","Manufacturer Part":"PART10K"}));
    assert_eq!(parameter(&resistor,"Comment"),"10kΩ");assert_eq!(parameter(&resistor,"Value"),"10kΩ");
    let ic=convert(json!({"name":"OPAMP","pre":"U?","Manufacturer Part":"LM358"}));
    assert_eq!(parameter(&ic,"Comment"),"LM358");assert!(!ic.parameters.iter().any(|p|p.name=="Value"));
    let generic=convert(json!({"name":"CONNECTOR","pre":"J?"}));
    assert_eq!(parameter(&generic,"Comment"),"CONNECTOR");
}
