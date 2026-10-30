use easyeda2altium::{easyeda::{EasyedaApi,parser::parse_footprint},convert::convert_footprint};
use serde_json::json;
use std::path::PathBuf;

fn folder(label:&str)->PathBuf {
    let path=std::env::temp_dir().join(format!("librarysearch-contract-{label}-{}-{}",std::process::id(),std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
    std::fs::create_dir(&path).unwrap();path
}

#[tokio::test]
async fn pinned_source_wins_over_other_versions_in_shared_cache() {
    let path=folder("pinned");let snapshot=path.join("snapshot.json");
    std::fs::write(&snapshot,json!({"success":true,"result":{"lcsc":{"number":"C1"},"marker":"snapshot"}}).to_string()).unwrap();
    std::fs::write(path.join("C1.json"),json!({"success":true,"result":{"lcsc":{"number":"C1"},"marker":"other-ad"}}).to_string()).unwrap();
    let api=EasyedaApi::with_options(false,path.to_str(),false).unwrap().with_input_options(snapshot.to_str(),false);
    assert_eq!(api.get_cad_data("C1").await.unwrap()["marker"],"snapshot");
    std::fs::remove_dir_all(path).unwrap();
}

#[tokio::test]
async fn invalid_pinned_source_never_falls_back_to_shared_cache() {
    let path=folder("invalid");let snapshot=path.join("snapshot.json");
    std::fs::write(path.join("C1.json"),json!({"success":true,"result":{"lcsc":{"number":"C1"}}}).to_string()).unwrap();
    let api=EasyedaApi::with_options(false,path.to_str(),false).unwrap().with_input_options(snapshot.to_str(),false);
    for text in ["bad JSON",r#"{"success":false,"result":{"lcsc":{"number":"C1"}}}"#,r#"{"success":true,"result":{"lcsc":{"number":"C2"}}}"#] {
        std::fs::write(&snapshot,text).unwrap();assert!(api.get_cad_data("C1").await.is_err());
    }
    std::fs::remove_file(&snapshot).unwrap();assert!(api.get_cad_data("C1").await.is_err());
    std::fs::remove_dir_all(path).unwrap();
}

#[test]
fn description_refresh_does_not_invalidate_the_pinned_cad_or_step_cache() {
    let path=folder("description");
    std::fs::write(path.join("C1.description.json"),json!({"Version":"product-cn-1","Description":"old description"}).to_string()).unwrap();
    let api=EasyedaApi::with_options(false,path.to_str(),false).unwrap();
    assert_eq!(api.cached_product_description("C1").as_deref(),Some("old description"));
    let refreshed=api.with_input_options(Some("snapshot.json"),true);
    assert!(refreshed.cached_product_description("C1").is_none());
    assert!(path.join("C1.description.json").exists(),"refresh must not delete another AD's cache");
    std::fs::remove_dir_all(path).unwrap();
}

#[test]
fn source_y_is_reflected_once_for_offset_3d_models() {
    let node=json!({"attrs":{"uuid":"1234567890abcdef1234567890abcdef","c_origin":"20,40","z":"30","c_rotation":"0,0,90"}});
    let cad=json!({"lcsc":{"number":"C1"},"packageDetail":{"title":"offset","dataStr":{"head":{"x":0,"y":0,"c_para":{"package":"OFFSET"}},"shape":[format!("SVGNODE~{node}")]}}});
    let parsed=parse_footprint(&cad).unwrap();
    let model=parsed.model_3d.as_ref().unwrap();
    assert!((model.translation_y_mm+10.16).abs()<1e-9);
    let component=convert_footprint(&parsed);let body=&component.component_bodies[0];
    assert_eq!(body.model_2d_location.x.to_raw(),2_000_000);
    assert_eq!(body.model_2d_location.y.to_raw(),-4_000_000);
    assert_eq!(body.model_3d_dz.to_raw(),3_000_000);
    let mut lib=altium::pcb::Library::default();lib.components.push(component);
    let mut cf=altium::compound::CompoundFile::open(lib.to_bytes().unwrap()).unwrap();
    let raw=cf.read_stream("OFFSET/Data").unwrap();
    assert!(raw.windows(b"MODEL.2D.Y=-400mil".len()).any(|w|w==b"MODEL.2D.Y=-400mil"));
    let reopened=altium::pcb::Library::from_bytes(lib.to_bytes().unwrap()).unwrap();
    let read_body=&reopened.components[0].component_bodies[0];
    assert_eq!(read_body.model_2d_location,altium::coord::CoordPoint::mils(200.0,-400.0),"model XY must survive native-library readback");
    assert_eq!(read_body.model_3d_dz,altium::coord::Coord::from_mils(300.0),"model height must survive native-library readback");
}
