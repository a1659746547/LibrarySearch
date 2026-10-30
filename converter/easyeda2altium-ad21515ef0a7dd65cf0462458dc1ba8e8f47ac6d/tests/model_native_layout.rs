use altium::{compound::CompoundFile,coord::{Coord,CoordPoint},pcb::{Library,Component},pcb::primitives::ComponentBody};

// Inspect bytes directly. A roundtrip through the same permissive reader alone
// would not detect the wrong leading pipe in this nested AD record.
#[test]
fn body_nested_params_match_ad_native_framing() {
    let mut library=Library::default();let mut component=Component::default();component.name="MODEL_LAYOUT".into();
    let mut body=ComponentBody::default();body.model_id=Some("1234567890abcdef1234567890abcdef".into());body.model_name=Some("sample.step".into());
    body.outline=vec![CoordPoint::new(Coord::from_mils(-10.0),Coord::ZERO),CoordPoint::new(Coord::from_mils(10.0),Coord::ZERO),CoordPoint::new(Coord::from_mils(10.0),Coord::from_mils(10.0))];
    component.component_bodies.push(body);library.components.push(component);
    let mut cf=CompoundFile::open(library.to_bytes().unwrap()).unwrap();let data=cf.read_stream("MODEL_LAYOUT/Data").unwrap();
    let u32at=|n|u32::from_le_bytes(data[n..n+4].try_into().unwrap()) as usize;
    let record=4+u32at(0);assert_eq!(data[record],12);
    let main=record+5;assert_eq!(main+u32at(record+1),data.len(),"one body block, no trailing record");
    let params=main+22;let count=u32at(main+18);assert!(data[params..].starts_with(b"V7_LAYER="));assert_eq!(data[params+count-1],0);
    let text=std::str::from_utf8(&data[params..params+count-1]).unwrap();
    assert!(text.contains("MODEL.MODELTYPE=1"));assert!(text.contains("MODEL.EMBED=TRUE"));assert!(text.contains("MODELID={12345678-90AB-CDEF-1234-567890ABCDEF}"));
    let outline=params+count;assert_eq!(u32at(outline),3);assert_eq!(outline+4+3*16,data.len());
    for coord in data[outline+4..].chunks_exact(8){let value=f64::from_le_bytes(coord.try_into().unwrap());assert_eq!(value.fract(),0.0);}
}
