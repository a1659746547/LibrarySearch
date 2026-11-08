use clap::Parser;
use easyeda2altium::cli::Args;
use serde_json::json;

#[test]
fn footprint_identity_rejects_ambiguous_or_invalid_requests() {
    for arguments in [
        vec!["converter", "--lcsc_id", "C1", "C2", "--full", "--footprint-id", "unique"],
        vec!["converter", "--lcsc_id", "C1", "--symbol", "--footprint-id", "unique"],
        vec!["converter", "--lcsc_id", "C1", "--full", "--footprint-id", "../bad"],
    ] {
        assert!(Args::try_parse_from(arguments).unwrap().validate().is_err());
    }
}

#[test]
fn upgrading_to_3d_preserves_old_library_and_binds_new_symbol_to_new_footprint() {
    let folder = std::env::temp_dir().join(format!("ls-paired-model-{}-{}", std::process::id(),
        std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
    std::fs::create_dir_all(&folder).unwrap();
    let uuid = "1234567890abcdef1234567890abcdef";
    let node = json!({"attrs":{"uuid":uuid,"c_origin":"0,0","z":"0","c_rotation":"0,0,0","title":"test-model"}});
    let source = json!({"success":true,"result":{
        "lcsc":{"number":"C15127"},
        "dataStr":{"head":{"x":0,"y":0,"c_para":{"name":"TEST_SYMBOL","pre":"Q?","package":"SOT23"}},"shape":[]},
        "packageDetail":{"title":"SOT23","dataStr":{"head":{"x":0,"y":0,"c_para":{"package":"SOT23"}},
        "shape":["PAD~RECT~0~0~2~2~1~~1~0~~0~~0~~Y",format!("SVGNODE~{node}")]}}
    }});
    let snapshot = folder.join("source.json");
    std::fs::write(&snapshot,source.to_string()).unwrap();
    let mut previous = None;
    for (label, identity) in [("first", "LS_C15127_11111111111111111111111111111111"),
                               ("model", "LS_C15127_22222222222222222222222222222222")] {
        if label == "model" {
            std::fs::write(folder.join(format!("{uuid}.step")),
                "ISO-10303-21;HEADER;ENDSEC;DATA;ENDSEC;END-ISO-10303-21;").unwrap();
        }
        let output = folder.join(label);
        let result = std::process::Command::new(env!("CARGO_BIN_EXE_easyeda2altium"))
            .args(["--lcsc_id","C15127","--symbol","--footprint","--3d-cached","--footprint-id",identity,"--cad-source"])
            .arg(&snapshot).arg("--cache-dir").arg(&folder).arg("--output").arg(&output).output().unwrap();
        assert!(result.status.success(), "{}", String::from_utf8_lossy(&result.stderr));
        let pcb_bytes = std::fs::read(output.with_extension("PcbLib")).unwrap();
        let pcb = altium::pcb::Library::from_bytes(pcb_bytes.clone()).unwrap();
        let sch = altium::sch::Library::from_bytes(std::fs::read(output.with_extension("SchLib")).unwrap()).unwrap();
        assert_eq!(pcb.components[0].name, identity);
        assert_eq!(sch.components[0].implementations[0].model_name.as_deref(), Some(identity));
        assert_eq!(pcb.models.len(), if label == "model" {1} else {0});
        assert_eq!(pcb.components[0].component_bodies.len(), pcb.models.len());
        if label == "model" {
            assert_eq!(pcb.components[0].component_bodies[0].layer,62);
            assert_eq!(pcb.components[0].component_bodies[0].layer_name,"MECHANICAL6");
            assert_eq!(pcb.library_parameters.as_ref().unwrap()["LAYER62MECHKIND"],"3DBodyTop");
            assert_eq!(pcb.library_parameters.as_ref().unwrap()["MECHPAIR0L2"],"MECHANICAL7");
        }
        if label == "first" { previous = Some(pcb_bytes); }
    }
    assert_eq!(std::fs::read(folder.join("first.PcbLib")).unwrap(),previous.unwrap());
    std::fs::remove_dir_all(folder).unwrap();
}
