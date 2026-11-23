use altium::{compound::CompoundFile, parameter::ParameterMap, sch};

fn records(bytes: Vec<u8>, stream: &str) -> Vec<Option<ParameterMap>> {
    let mut compound = CompoundFile::open(bytes).unwrap();
    let data = compound.read_stream(stream).unwrap();
    let mut offset = 0;
    let mut result = Vec::new();
    while offset < data.len() {
        let header = u32::from_le_bytes(data[offset..offset + 4].try_into().unwrap());
        let length = (header & 0x00ff_ffff) as usize;
        let body = &data[offset + 4..offset + 4 + length];
        result.push(if header >> 24 == 0 {
            Some(ParameterMap::parse(altium::encoding::decode(body).trim_end_matches('\0')))
        } else { None });
        offset += length + 4;
    }
    result
}

fn symbol(name: &str, pins: usize) -> sch::Component {
    let mut component = sch::Component::new(name);
    component.add_default_labels("U", 2);
    for number in 1..=pins {
        component.pins.push(sch::Pin { designator: Some(number.to_string()), ..Default::default() });
    }
    component.implementations.push(sch::Implementation {
        model_name: Some("EXPECTED_FOOTPRINT".into()), model_type: Some("PCBLIB".into()),
        is_current: true, data_file_kinds: vec!["PCBLib".into()],
        data_file_entities: vec!["EXPECTED_FOOTPRINT".into()], ..Default::default()
    });
    component
}

fn verify_hierarchy(rows: &[Option<ParameterMap>], component_owner: i32, index_bias: i32) {
    let mut models = 0;
    for (index, row) in rows.iter().enumerate() {
        let Some(model) = row else { continue };
        if model.get_i32("RECORD") != Some(45) { continue; }
        models += 1;
        let owner = model.get_i32("OWNERINDEX").expect("native RECORD=45 must belong to RECORD=44");
        let list = rows[(owner + index_bias) as usize].as_ref().unwrap();
        assert_eq!(list.get_i32("RECORD"), Some(44));
        assert_eq!(list.get_i32("OWNERINDEX").unwrap_or(0), component_owner);
        assert_eq!(model.get_i32("INDEXINSHEET"), Some(-1));
        let model_index = index as i32 - index_bias;
        let maps = rows[index + 1].as_ref().unwrap();
        assert_eq!(maps.get_i32("RECORD"), Some(46));
        assert_eq!(maps.get_i32("OWNERINDEX"), Some(model_index));
        let mut next = index + 2;
        while rows[next].as_ref().and_then(|row| row.get_i32("RECORD")) == Some(47) {
            assert_eq!(rows[next].as_ref().unwrap().get_i32("OWNERINDEX"), Some(index as i32 + 1 - index_bias));
            next += 1;
        }
        let parameters = rows[next].as_ref().unwrap();
        assert_eq!(parameters.get_i32("RECORD"), Some(48));
        assert_eq!(parameters.get_i32("OWNERINDEX"), Some(model_index));
        if model.get("MODELTYPE") == Some("PCBLIB") {
            assert_eq!(model.get_i32("DATAFILECOUNT"), Some(1));
            assert_eq!(model.get("MODELDATAFILEKIND0"), Some("PCBLib"));
            assert_eq!(model.get("MODELDATAFILEENTITY0"), model.get("MODELNAME"));
            assert!(model.get("MODELDATAFILEENTITY1").is_none());
        }
    }
    assert!(models > 0);
}

#[test]
fn native_footprint_links_use_zero_based_entities_and_owned_containers() {
    for pins in [0, 1, 2, 6, 64] {
        let mut library = sch::Library::default();
        library.components.push(symbol("TEST", pins));
        verify_hierarchy(&records(library.to_bytes().unwrap(), "TEST/Data"), 0, 0);
    }
}

#[test]
fn multiple_models_and_explicit_pin_maps_have_distinct_recalculated_owners() {
    let mut component = symbol("MAPPED", 3);
    component.implementations[0].common.owner_index = 987;
    component.implementations[0].map_definers.push(sch::MapDefiner {
        designator_interface: Some("1".into()), designator_implementations: vec!["2".into()], ..Default::default()
    });
    let mut second = component.implementations[0].clone();
    second.is_current = false;
    component.implementations.push(second);
    let mut library = sch::Library::default();
    library.components.push(component);
    let bytes = library.to_bytes().unwrap();
    verify_hierarchy(&records(bytes.clone(), "MAPPED/Data"), 0, 0);
    let reread = sch::Library::from_bytes(bytes).unwrap();
    assert_eq!(reread.components[0].implementations[0].data_file_entities, ["EXPECTED_FOOTPRINT"]);
    verify_hierarchy(&records(reread.to_bytes().unwrap(), "MAPPED/Data"), 0, 0);
}

#[test]
fn native_zero_based_and_legacy_one_based_model_entities_are_read() {
    for start in [0, 1] {
        let params = ParameterMap::parse(&format!("|RECORD=45|ModelType=PCBLIB|ModelName=EXPECTED_FOOTPRINT|DatafileCount=1|ModelDatafileEntity{start}=EXPECTED_FOOTPRINT|ModelDatafileKind{start}=PCBLib"));
        let model = sch::codec::implementation_from_params(&params);
        assert_eq!(model.data_file_entities, ["EXPECTED_FOOTPRINT"]);
        assert_eq!(model.data_file_kinds, ["PCBLib"]);
    }
}
