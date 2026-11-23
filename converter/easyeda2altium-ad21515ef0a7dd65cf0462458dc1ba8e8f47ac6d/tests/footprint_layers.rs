use easyeda2altium::{convert::convert_footprint, easyeda::types::*};

fn graphics(layer_id: i32) -> EeFootprint {
    let mut source = EeFootprint::default();
    source.tracks.push(EeFootprintTrack { layer_id, points: "0 0 10 10".into(), stroke_width: 1.0 });
    source.circles.push(EeFootprintCircle { layer_id, radius: 5.0, ..Default::default() });
    source.arcs.push(EeFootprintArc { layer_id, raw_path: "M0 0 A10 10 0 0 1 10 10".into(), ..Default::default() });
    source.rectangles.push(EeFootprintRect { layer_id, width: 10.0, height: 10.0, ..Default::default() });
    source.texts.push(EeFootprintText { layer_id, text: "reference".into(), ..Default::default() });
    source.solid_regions.push(EeFootprintSolidRegion { layer_id, region_type: "solid".into(), raw_path: "M0 0 L10 0 L10 10 Z".into() });
    source
}

#[test]
fn auxiliary_and_unknown_graphics_do_not_create_mechanical_one() {
    for layer in [12, 99, 0, -1, 1000] {
        let component = convert_footprint(&graphics(layer)).unwrap();
        assert!(component.tracks.is_empty(), "layer {layer}");
        assert!(component.arcs.is_empty(), "layer {layer}");
        assert!(component.texts.is_empty(), "layer {layer}");
        assert!(component.regions.is_empty(), "layer {layer}");
    }
}

#[test]
fn named_layers_keep_their_geometry() {
    for (source, target) in [(1, 1), (2, 32), (3, 33), (4, 34), (5, 35), (6, 36), (7, 37), (8, 38), (10, 56), (11, 74), (13, 69), (14, 70), (15, 71)] {
        let component = convert_footprint(&graphics(source)).unwrap();
        assert_eq!(component.tracks.len(), 5);
        assert_eq!(component.arcs.len(), 2);
        assert_eq!(component.texts.len(), 1);
        assert!(component.tracks.iter().all(|shape| shape.layer == target));
        assert!(component.arcs.iter().all(|shape| shape.layer == target));
        assert!(component.texts.iter().all(|shape| shape.layer == target));
        if [3, 4, 13, 14, 15].contains(&source) {
            assert_eq!(component.regions.len(), 1);
            assert_eq!(component.regions[0].layer, target);
        }
    }
}

#[test]
fn electrical_pads_and_embedded_body_are_kept() {
    let mut source = graphics(99);
    for layer_id in [1, 2, 11] {
        source.pads.push(EeFootprintPad { layer_id, number: layer_id.to_string(), width: 10.0, height: 8.0, ..Default::default() });
    }
    source.model_3d = Some(Ee3dModel { uuid: "model-identity".into(), ..Default::default() });
    let component = convert_footprint(&source).unwrap();
    assert_eq!(component.pads.iter().map(|pad| pad.layer).collect::<Vec<_>>(), [1, 32, 74]);
    assert_eq!(component.component_bodies.len(), 1);
    assert!(component.component_bodies[0].model_embed);
    assert_eq!(component.component_bodies[0].model_id.as_deref(), Some("model-identity"));
    assert_eq!(component.component_bodies[0].layer, easyeda2altium::body_layers::TOP);
}

#[test]
fn unknown_pad_layer_is_rejected_instead_of_discarded() {
    for layer_id in [12, 99, 1000] {
        let mut source = EeFootprint::default();
        source.pads.push(EeFootprintPad { layer_id, number: "1".into(), ..Default::default() });
        let error = convert_footprint(&source).unwrap_err().to_string();
        assert!(error.contains("unsupported pad layer"));
    }
}
