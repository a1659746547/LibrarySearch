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
    let component=convert_footprint(&parsed).unwrap();let body=&component.component_bodies[0];
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

#[test]
fn matching_projection_corrects_only_xy_and_can_preserve_a_nonzero_placement() {
    use easyeda2altium::convert::model3d::align_xy_to_projection;
    // Flat B-rep: a 2 x 4 mm projection with local center (3, -1).
    // The quoted fake entity, 2D trim and stray control point are not vertices.
    let step = b"ISO-10303-21;HEADER;ENDSEC;DATA;
        #1=(LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.));
        #2=ADVANCED_BREP_SHAPE_REPRESENTATION('contains ;#81=MAPPED_ITEM();',(),#1);
        #3=CARTESIAN_POINT('',(2.,-3.,0.));
        #4=CARTESIAN_POINT('',(4.,1.,0.));
        #5=CARTESIAN_POINT('',(2.,1.,1.));
        #6=CARTESIAN_POINT('',(9999.,9999.,0.));
        #7=CARTESIAN_POINT('',(5.,5.));
        /* #11=MAPPED_ITEM(); */ #8=VERTEX_POINT('',#3);
        #9=VERTEX_POINT('',#4); #10=VERTEX_POINT('',#5);
        ENDSEC;END-ISO-10303-21;";
    let node=json!({
        "attrs": {"uuid":"1234567890abcdef1234567890abcdef","c_origin":"123,70","z":"10",
                  "c_rotation":"0,0,90","c_etype":"outline3D","transform":"scale(1) translate(0, 0)"},
        "childNodes":[{"nodeName":"polyline","attrs":{"points":"96,58 112,58 112,50 96,50 96,58"}}]
    });
    let cad=json!({"lcsc":{"number":"C1"},"packageDetail":{"title":"offset","dataStr":{"head":{"x":100,"y":50,"c_para":{"package":"OFFSET"}},"shape":[format!("SVGNODE~{node}")]}}});
    let mut parsed=parse_footprint(&cad).unwrap();
    let mut before=parsed.model_3d.take().unwrap();
    // Use a precisely matching independent projection, centered at (1.5,-1).
    let svg_projection=before.projection_xy_mm.unwrap();
    assert_eq!(svg_projection[0],[-1.016,-2.032]);
    assert_eq!(svg_projection[1],[3.048,0.]);
    before.projection_xy_mm=Some([[-0.5,-2.],[3.5,0.]]);
    let mut aligned=before.clone();
    assert!(align_xy_to_projection(&mut aligned,step));
    assert!((aligned.translation_x_mm-0.5).abs()<1e-9);
    assert!((aligned.translation_y_mm+4.0).abs()<1e-9);
    assert_eq!(aligned.translation_z_mm,before.translation_z_mm);
    assert_eq!(aligned.rotation_z_deg,90.0);
    assert!(!align_xy_to_projection(&mut aligned,step));
    let mut no_outline=before.clone();
    no_outline.projection_xy_mm=None;
    assert!(!align_xy_to_projection(&mut no_outline,step));
    let mut rotated=before.clone();
    rotated.rotation_x_deg=90.0;
    assert!(!align_xy_to_projection(&mut rotated,step));
    let mut wrong_scale=before.clone();
    wrong_scale.projection_xy_mm=Some([[-0.5,-2.],[6.0,0.]]);
    assert!(!align_xy_to_projection(&mut wrong_scale,step));
    let assembly=String::from_utf8_lossy(step).replace("ENDSEC;END-ISO","#50=MAPPED_ITEM('',#2,#2);ENDSEC;END-ISO");
    assert!(!align_xy_to_projection(&mut before,assembly.as_bytes()));
}
