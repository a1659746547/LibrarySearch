use std::{path::Path, collections::BTreeMap};
use altium::{pcb, sch, compound::CompoundFile, coord::CoordPoint};
use easyeda2altium::{body_layers, unified_library::{merge, Input, Request}};

fn parameters(bytes: Vec<u8>) -> BTreeMap<String, String> {
    let mut cf = CompoundFile::open(bytes).unwrap();
    let data = cf.read_stream("Library/Data").unwrap();
    let n = u32::from_le_bytes(data[..4].try_into().unwrap()) as usize;
    assert_eq!(data[3+n], 0);
    std::str::from_utf8(&data[4..3+n]).unwrap().split('|').filter_map(|s|s.split_once('=')).map(|(k,v)|(k.to_owned(),v.to_owned())).collect()
}
#[test]
fn writes_layer_kinds_and_pair_in_v9_and_legacy_parameter_formats() {
    let mut lib = pcb::Library::default(); body_layers::configure(&mut lib);
    let p = parameters(lib.to_bytes().unwrap());
    for (cache, v8, legacy, id, kind, name) in [(12,16,62,16908294,"3DBodyTop","Top 3D Body"),(13,17,63,16908295,"3DBodyBottom","Bottom 3D Body")] {
        for (prefix,sep) in [(format!("V9_CACHE_LAYER{cache}"),"_"),(format!("LAYER_V8_{v8}"),"")] {
            assert_eq!(p[&format!("{prefix}{sep}LAYERID")], id.to_string());
            assert_eq!(p[&format!("{prefix}{sep}MECHKIND")], kind);
            assert_eq!(p[&format!("{prefix}{sep}MECHENABLED")], "TRUE");
        }
        assert_eq!(p[&format!("LAYER{legacy}MECHKIND")],kind);
        assert_eq!(p[&format!("LAYER{legacy}NAME")],name);
    }
    assert_eq!(p["MECHPAIR0L1"],"MECHANICAL6"); assert_eq!(p["MECHPAIR0L2"],"MECHANICAL7");
    // The existing valid master stack and utility cache must survive.
    assert!(p.contains_key("V9_MASTERSTACK_ID")); assert_eq!(p["V9_CACHE_LAYER0_NAME"],"Multi-Layer");
}
#[test]
fn layer_move_preserves_geometry_model_and_electrical_state() {
    let mut body=pcb::primitives::ComponentBody {model_id:Some("model".into()), model_2d_location:CoordPoint::mils(20.0,-30.0),model_3d_rot_z:90.0,..Default::default()};
    let before=body.clone(); body.additional_parameters=Some(BTreeMap::from([("V7_LAYER".into(),"MECHANICAL1".into())]));
    body_layers::set_body_layer(&mut body,false);
    assert_eq!(body.layer,62);assert_eq!(body.layer_name,"MECHANICAL6");
    assert_eq!(body.model_id,before.model_id);assert_eq!(body.model_2d_location,before.model_2d_location);assert_eq!(body.model_3d_rot_z,before.model_3d_rot_z);
    assert_eq!(body.net_index,before.net_index);assert!(!body.additional_parameters.as_ref().unwrap().contains_key("V7_LAYER"));
    let mut lib=pcb::Library::default();body_layers::configure(&mut lib);let mut comp=pcb::Component::new("X");comp.component_bodies.push(body);lib.components.push(comp);
    let parsed=pcb::Library::from_bytes(lib.to_bytes().unwrap()).unwrap();assert_eq!(parsed.components[0].component_bodies[0].layer,62);assert_eq!(parsed.components[0].component_bodies[0].layer_name,"MECHANICAL6");
}
#[test]
fn detects_bottom_from_types_or_legacy_pair_without_assuming_a_fixed_layer_number() {
    let body=pcb::primitives::ComponentBody{layer:70,layer_name:"MECHANICAL14".into(),..Default::default()};
    for p in [BTreeMap::from([("LAYER70MECHKIND".into(),"3DBodyBottom".into())]),BTreeMap::from([("V9_CACHE_LAYER99_LAYERID".into(),"16908302".into()),("V9_CACHE_LAYER99_MECHKIND".into(),"3DBodyBottom".into())]),BTreeMap::from([("MECHPAIR0L2".into(),"MECHANICAL14".into())])] {
        assert!(body_layers::is_bottom(&body,Some(&p)));
    }
    assert!(!body_layers::is_bottom(&body,None));
}
fn input(path:&Path)->Input {Input{code:None,symbol_name:None,symbol_alias:None,footprint_name:None,footprint_alias:None,symbol_path:path.with_extension("SchLib").to_string_lossy().into(),footprint_path:path.with_extension("PcbLib").to_string_lossy().into()}}
#[test]
fn old_downloads_and_project_totals_move_bodies_only_and_keep_bottom_pair_on_remerge() {
    let root=std::env::temp_dir().join(format!("ls-body-layers-{}-{}",std::process::id(),std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));std::fs::create_dir_all(&root).unwrap();
    let source=root.join("old");let mut pcb=pcb::Library::default();let mut sch=sch::Library::default();
    pcb.library_parameters=Some(BTreeMap::from([("LAYER70MECHKIND".into(),"3DBodyBottom".into())]));
    for (name,layer) in [("TOP",57),("BOTTOM",70)] {
        let mut comp=pcb::Component::new(name);comp.tracks.push(pcb::primitives::Track{layer:57,..Default::default()});comp.component_bodies.push(pcb::primitives::ComponentBody{model_embed:false,layer,layer_name:format!("MECHANICAL{}",layer-56),..Default::default()});pcb.components.push(comp);sch.components.push(sch::Component::new(name));
    }
    let old=pcb.to_bytes().unwrap();std::fs::write(source.with_extension("PcbLib"),&old).unwrap();std::fs::write(source.with_extension("SchLib"),sch.to_bytes().unwrap()).unwrap();
    let mut previous=source.clone();
    for name in ["total","project"] {
        let out=root.join(name);merge(&Request{model_namespace:"aabbccddeeff00112233445566778899".into(),entries:vec![input(&previous)]},&out).unwrap();
        let bytes=std::fs::read(out.with_extension("PcbLib")).unwrap();let lib=pcb::Library::from_bytes(bytes.clone()).unwrap();
        assert_eq!(lib.components[0].component_bodies[0].layer,62);assert_eq!(lib.components[1].component_bodies[0].layer,63);
        assert!(lib.components.iter().all(|c|c.tracks[0].layer==57));assert_eq!(parameters(bytes)["MECHPAIR0L2"],"MECHANICAL7");previous=out;
    }
    assert_eq!(std::fs::read(source.with_extension("PcbLib")).unwrap(),old);std::fs::remove_dir_all(root).unwrap();
}
