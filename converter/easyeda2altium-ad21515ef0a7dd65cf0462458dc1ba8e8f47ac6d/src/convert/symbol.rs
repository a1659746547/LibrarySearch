use altium::coord::{Coord, CoordPoint};
use altium::enums::{PinElectricalType, PinOrientation, TextJustification};
use altium::sch::primitives::PrimitiveCommon;
use altium::sch::{
    Arc, Component, Ellipse, Implementation, Label, Parameter, Pin, Polygon, Polyline, Rectangle,
};

use crate::easyeda::canvas::snap_to_5px_grid;
use crate::easyeda::geometry::{path_polylines, single_arc};
use crate::easyeda::types::{
    EePinElectrical, EeSymbol, EeSymbolArc, EeSymbolCircle, EeSymbolEllipse, EeSymbolPin,
    EeSymbolPolyline, EeSymbolRectangle, EeSymbolText,
};

const ALTIUM_BODY_FILL_BGR: i32 = 0x00B0_FFFF;
const ALTIUM_BODY_BORDER_BGR: i32 = 0x0000_0080;

pub fn convert_symbol(
    ee: &EeSymbol,
    custom_fields: &[(String, String)],
    use_custom_pin_font: bool,
) -> Component {
    let font_id_for_text: i32 = if use_custom_pin_font { 2 } else { 1 };
    let origin_x = snap_to_5px_grid(ee.bbox.x);
    let origin_y = snap_to_5px_grid(ee.bbox.y);

    let mut comp = Component::new(ee.info.name.clone());

    let description = if !ee.info.description.is_empty() {
        ee.info.description.clone()
    } else if !ee.info.mpn.is_empty() && !ee.info.manufacturer.is_empty() {
        format!("{} {}", ee.info.manufacturer, ee.info.mpn)
    } else if !ee.info.mpn.is_empty() {
        ee.info.mpn.clone()
    } else {
        String::new()
    };
    comp.description = if description.is_empty() {
        None
    } else {
        Some(description.clone())
    };

    let raw_prefix = ee.info.prefix.replace('?', "");
    comp.designator_prefix = if raw_prefix.is_empty() {
        None
    } else {
        Some(raw_prefix.clone())
    };
    comp.lib_reference = Some(ee.info.name.clone());
    comp.design_item_id = Some(format!("{}-{}", ee.info.name, ee.info.lcsc_id));

    comp.area_color = ALTIUM_BODY_FILL_BGR;
    comp.color = ALTIUM_BODY_BORDER_BGR;
    comp.part_id_locked = true;

    let units: Vec<&EeSymbol> = std::iter::once(ee).chain(ee.sub_symbols.iter()).collect();
    comp.part_count = units.len() as i32;
    comp.current_part_id = 1;
    comp.display_mode_count = 1;

    for (i, unit) in units.iter().enumerate() {
        let owner_part_id = (i + 1) as i32;
        emit_unit(
            unit,
            owner_part_id,
            origin_x,
            origin_y,
            use_custom_pin_font,
            font_id_for_text,
            &mut comp,
        );
    }

    comp.add_default_labels(&raw_prefix, font_id_for_text);

    for (k, v) in builtin_parameters(ee).iter().chain(custom_fields.iter()) {
        comp.parameters.push(make_parameter(k, v, font_id_for_text));
    }

    if !ee.info.package.is_empty() {
        comp.implementations.push(make_implementation(&ee.info.package));
    }

    comp
}

fn emit_unit(
    unit: &EeSymbol,
    owner_part_id: i32,
    origin_x: f64,
    origin_y: f64,
    use_custom_pin_font: bool,
    font_id_for_text: i32,
    comp: &mut Component,
) {
    let prefix = unit.info.prefix.trim_end_matches('?').to_ascii_uppercase();
    let preserve_source_fill = matches!(prefix.as_str(), "LED" | "D" | "ZD" | "Q" | "R" | "C" | "L" | "F");
    for pin in &unit.pins {
        let mut converted = convert_pin(
            pin,
            owner_part_id,
            origin_x,
            origin_y,
            use_custom_pin_font,
            font_id_for_text,
        );
        if preserve_source_fill {
            converted.color = ALTIUM_BODY_BORDER_BGR;
        }
        comp.pins.push(converted);
    }
    for r in &unit.rectangles {
        comp.rectangles.push(convert_rectangle(
            r,
            owner_part_id,
            origin_x,
            origin_y,
            preserve_source_fill,
        ));
    }
    for c in &unit.circles {
        comp.ellipses
            .push(convert_circle(c, owner_part_id, origin_x, origin_y));
    }
    for e in &unit.ellipses {
        comp.ellipses
            .push(convert_ellipse(e, owner_part_id, origin_x, origin_y));
    }
    for p in &unit.polylines {
        if let Some(polygon) = convert_polygon(p, owner_part_id, origin_x, origin_y, preserve_source_fill) {
            comp.polygons.push(polygon);
        } else {
            comp.polylines
                .push(convert_polyline(p, owner_part_id, origin_x, origin_y));
        }
    }
    for arc in &unit.arcs {
        if let Some(a) = convert_arc(arc, owner_part_id, origin_x, origin_y) {
            comp.arcs.push(a);
        } else if let Some(paths) = path_polylines(&arc.raw_path) {
            for points in paths {
                comp.polylines.push(convert_polyline(
                    &EeSymbolPolyline { points, fill: false, stroke_width: arc.stroke_width }, owner_part_id, origin_x, origin_y,
                ));
            }
        } else {
            eprintln!("[easyeda2altium] warning: invalid or over-complex symbol arc path");
        }
    }
    for text in &unit.texts {
        comp.labels.push(convert_text(text, owner_part_id, origin_x, origin_y, font_id_for_text));
    }
}

fn convert_text(text: &EeSymbolText, owner_part_id: i32, ox: f64, oy: f64, font_id: i32) -> Label {
    let rotation = text.rotation_deg.rem_euclid(360.0);
    let quarter_turn = (rotation / 90.0).round() * 90.0;
    if (quarter_turn - rotation).abs() > 1e-7 {
        eprintln!("[easyeda2altium] warning: symbol text rotation rounded to a 90-degree orientation");
    }
    let column = match text.anchor.as_str() { "middle" => 1, "end" => 2, _ => 0 };
    let row = match text.baseline.as_str() {
        "middle" | "central" => 1,
        "hanging" | "text-before-edge" => 2,
        _ => 0,
    };
    let justification = match row * 3 + column {
        1 => TextJustification::BottomCenter, 2 => TextJustification::BottomRight,
        3 => TextJustification::MiddleLeft, 4 => TextJustification::MiddleCenter,
        5 => TextJustification::MiddleRight, 6 => TextJustification::TopLeft,
        7 => TextJustification::TopCenter, 8 => TextJustification::TopRight,
        _ => TextJustification::BottomLeft,
    };
    Label {
        text: text.text.clone(),
        location: CoordPoint::new(ee_px_to_coord(text.pos_x - ox), -ee_px_to_coord(text.pos_y - oy)),
        font_id, justification, rotation: quarter_turn.rem_euclid(360.0),
        is_hidden: text.is_hidden,
        color: text.color,
        common: with_owner(owner_part_id),
        ..Default::default()
    }
}

/// Font slots belong to the whole library, not an individual component. Reserve
/// the writer's slots 1..3 and deduplicate additional annotation font styles.
pub fn register_symbol_text_fonts(
    library: &mut altium::sch::Library,
    source: &EeSymbol,
    component: &mut Component,
    font_override: Option<&str>,
) {
    let base = if font_override.is_some() { 3 } else { 1 };
    let mut count = library.file_header_parameters.get("FontIdCount")
        .and_then(|value| value.parse::<i32>().ok()).unwrap_or(base).max(base);
    library.file_header_parameters.insert("FontIdCount".into(), count.to_string());
    let texts = std::iter::once(source).chain(source.sub_symbols.iter()).flat_map(|unit| unit.texts.iter());
    for (label, text) in component.labels.iter_mut().zip(texts) {
        let points = text.font_size_mm * 72.0 / 25.4;
        let size = if points.is_finite() && points > 0.0 { points.round().clamp(1.0, 200.0) as i32 } else { 7 };
        if (points - size as f64).abs() > 0.01 {
            eprintln!("[easyeda2altium] warning: symbol annotation font size rounded/clamped to {size}pt");
        }
        let family = font_override.unwrap_or(if text.font_family.is_empty() { "Verdana" } else { &text.font_family });
        let bold = if text.bold { "T" } else { "F" };
        let italic = if text.italic { "T" } else { "F" };
        let existing = ((base+1)..=count).find(|id| {
            let p = &library.file_header_parameters;
            p.get(&format!("Size{id}")) == Some(&size.to_string())
                && p.get(&format!("FontName{id}")).map(String::as_str) == Some(family)
                && p.get(&format!("Bold{id}")).map(String::as_str) == Some(bold)
                && p.get(&format!("Italic{id}")).map(String::as_str) == Some(italic)
        });
        let id = existing.unwrap_or_else(|| {
            count += 1;
            for (key, value) in [("Size", size.to_string()), ("FontName", family.to_string()),
                ("Bold", bold.to_string()), ("Italic", italic.to_string())] {
                library.file_header_parameters.insert(format!("{key}{count}"), value);
            }
            count
        });
        label.font_id = id;
    }
    library.file_header_parameters.insert("FontIdCount".into(), count.to_string());
}

fn convert_pin(
    pin: &EeSymbolPin,
    owner_part_id: i32,
    ox: f64,
    oy: f64,
    use_custom_pin_font: bool,
    font_id_for_text: i32,
) -> Pin {
    let (font_mode, custom_font_id) = if use_custom_pin_font {
        (2, font_id_for_text)
    } else {
        (0, 0)
    };
    // parse_symbol validates the source line; never manufacture a 200-mil
    // stub when decoding fails. Intentional zero-length pins stay zero.
    let length = Coord::from_mils(pin.pin_length * 10.0);
    let orientation = ee_orientation(pin.rotation_deg);
    let connection = CoordPoint::new(
        ee_px_to_coord(pin.pos_x - ox),
        -ee_px_to_coord(pin.pos_y - oy),
    );
    let location = match orientation {
        PinOrientation::Right => CoordPoint::new(connection.x - length, connection.y),
        PinOrientation::Up => CoordPoint::new(connection.x, connection.y - length),
        PinOrientation::Left => CoordPoint::new(connection.x + length, connection.y),
        PinOrientation::Down => CoordPoint::new(connection.x, connection.y + length),
    };
    Pin {
        name: if pin.name.is_empty() {
            None
        } else {
            Some(pin.name.clone())
        },
        designator: if pin.designator.is_empty() {
            None
        } else {
            Some(pin.designator.clone())
        },
        location,
        length,
        electrical_type: ee_pin_electrical(pin.electrical),
        formal_type: 1, // AD normal pin, independent of electrical role or appearance.
        orientation,
        show_name: pin.name_visible && !pin.name.is_empty(),
        show_designator: pin.number_visible && !pin.designator.is_empty(),
        is_hidden: !pin.is_displayed,
        symbol_inner_edge: if pin.is_clock { 3 } else { 0 },
        symbol_outer_edge: if pin.is_inverted { 1 } else { 0 },
        designator_font_mode: font_mode,
        designator_custom_font_id: custom_font_id,
        name_font_mode: font_mode,
        name_custom_font_id: custom_font_id,
        common: with_owner(owner_part_id),
        ..Default::default()
    }
}

fn convert_rectangle(
    r: &EeSymbolRectangle,
    owner_part_id: i32,
    ox: f64,
    oy: f64,
    preserve_source_fill: bool,
) -> Rectangle {
    let lx_ee = ee_px_to_coord(r.pos_x - ox);
    let ty_ee = -ee_px_to_coord(r.pos_y - oy);
    let rx_ee = ee_px_to_coord(r.pos_x - ox + r.width);
    let by_ee = -ee_px_to_coord(r.pos_y - oy + r.height);
    let lx = lx_ee.min(rx_ee);
    let rx = lx_ee.max(rx_ee);
    let by = by_ee.min(ty_ee);
    let ty = by_ee.max(ty_ee);
    Rectangle {
        corner1: CoordPoint::new(lx, by),
        corner2: CoordPoint::new(rx, ty),
        is_filled: !preserve_source_fill || r.fill,
        is_transparent: false,
        color: ALTIUM_BODY_BORDER_BGR,
        fill_color: ALTIUM_BODY_FILL_BGR,
        line_width: source_stroke_coord(r.stroke_width),
        line_style: 0,
        common: with_owner(owner_part_id),
    }
}

fn convert_circle(c: &EeSymbolCircle, owner_part_id: i32, ox: f64, oy: f64) -> Ellipse {
    Ellipse {
        center: CoordPoint::new(
            ee_px_to_coord(c.center_x - ox),
            -ee_px_to_coord(c.center_y - oy),
        ),
        radius_x: ee_px_to_coord(c.radius),
        radius_y: ee_px_to_coord(c.radius),
        is_filled: c.fill,
        color: ALTIUM_BODY_BORDER_BGR,
        line_width: source_stroke_index(c.stroke_width),
        fill_color: ALTIUM_BODY_FILL_BGR,
        common: with_owner(owner_part_id),
        ..Default::default()
    }
}

fn convert_ellipse(e: &EeSymbolEllipse, owner_part_id: i32, ox: f64, oy: f64) -> Ellipse {
    Ellipse {
        center: CoordPoint::new(
            ee_px_to_coord(e.center_x - ox),
            -ee_px_to_coord(e.center_y - oy),
        ),
        radius_x: ee_px_to_coord(e.radius_x),
        radius_y: ee_px_to_coord(e.radius_y),
        is_filled: e.fill,
        color: ALTIUM_BODY_BORDER_BGR,
        line_width: source_stroke_index(e.stroke_width),
        fill_color: ALTIUM_BODY_FILL_BGR,
        common: with_owner(owner_part_id),
        ..Default::default()
    }
}

fn convert_polyline(p: &EeSymbolPolyline, owner_part_id: i32, ox: f64, oy: f64) -> Polyline {
    let vertices = p
        .points
        .iter()
        .map(|&(x, y)| CoordPoint::new(ee_px_to_coord(x - ox), -ee_px_to_coord(y - oy)))
        .collect();
    Polyline {
        vertices,
        color: ALTIUM_BODY_BORDER_BGR,
        line_width: source_stroke_index(p.stroke_width),
        common: with_owner(owner_part_id),
        ..Default::default()
    }
}

fn convert_polygon(
    polyline: &EeSymbolPolyline,
    owner_part_id: i32,
    origin_x: f64,
    origin_y: f64,
    preserve_source_fill: bool,
) -> Option<Polygon> {
    let is_closed = polyline.points.len() >= 4
        && polyline.points.first() == polyline.points.last();
    if !polyline.fill && !is_closed {
        return None;
    }
    let points = if is_closed {
        &polyline.points[..polyline.points.len() - 1]
    } else {
        &polyline.points[..]
    };
    if points.len() < 3 {
        return None;
    }
    Some(Polygon {
        vertices: points
            .iter()
            .map(|&(point_x, point_y)| {
                CoordPoint::new(
                    ee_px_to_coord(point_x - origin_x),
                    -ee_px_to_coord(point_y - origin_y),
                )
            })
            .collect(),
        color: ALTIUM_BODY_BORDER_BGR,
        line_width: source_stroke_index(polyline.stroke_width),
        fill_color: ALTIUM_BODY_FILL_BGR,
        is_filled: !preserve_source_fill || polyline.fill,
        is_transparent: false,
        common: with_owner(owner_part_id),
        ..Default::default()
    })
}

fn convert_arc(arc: &EeSymbolArc, owner_part_id: i32, ox: f64, oy: f64) -> Option<Arc> {
    let geometry = single_arc(&arc.raw_path)?.geometry()?;
    let (start_angle, end_angle) = geometry.altium_angles()?;
    Some(Arc {
        center: CoordPoint::new(ee_px_to_coord(geometry.center.0 - ox), -ee_px_to_coord(geometry.center.1 - oy)),
        radius: ee_px_to_coord(geometry.rx),
        start_angle,
        end_angle,
        color: ALTIUM_BODY_BORDER_BGR,
        line_width: source_stroke_index(arc.stroke_width),
        common: with_owner(owner_part_id),
        ..Default::default()
    })
}

fn make_parameter(name: &str, value: &str, font_id: i32) -> Parameter {
    Parameter {
        name: name.to_string(),
        value: value.to_string(),
        is_visible: false,
        font_id,
        common: PrimitiveCommon {
            owner_part_id: -1,
            is_not_accessible: true,
            ..Default::default()
        },
        ..Default::default()
    }
}

fn make_implementation(footprint_name: &str) -> Implementation {
    Implementation {
        model_name: Some(footprint_name.to_string()),
        model_type: Some("PCBLIB".to_string()),
        is_current: true,
        ..Default::default()
    }
}

// AD TSize includes ZeroSize=0. Source strokes must not accidentally become
// zero-width display hairlines. Preserve their relative small/medium/large class.
fn source_stroke_index(width:f64)->i32 {if width.is_finite()&&width>2.0{3}else if width>1.0{2}else{1}}
fn source_stroke_coord(width:f64)->Coord {Coord::from_mils(match source_stroke_index(width){3=>4.0,2=>2.0,_=>1.0})}

fn ee_pin_electrical(e: EePinElectrical) -> PinElectricalType {
    match e {
        EePinElectrical::Input => PinElectricalType::Input,
        EePinElectrical::Output => PinElectricalType::Output,
        EePinElectrical::Bidirectional => PinElectricalType::InputOutput,
        EePinElectrical::Power => PinElectricalType::Power,
        EePinElectrical::Unspecified => PinElectricalType::Passive,
    }
}

fn ee_orientation(rotation_deg: i32) -> PinOrientation {
    match ((rotation_deg % 360 + 360) % 360) / 90 {
        0 => PinOrientation::Right,
        1 => PinOrientation::Up,
        2 => PinOrientation::Left,
        _ => PinOrientation::Down,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn easyeda_pin_directions_point_away_from_symbol_body() {
        assert!(matches!(ee_orientation(0), PinOrientation::Right));
        assert!(matches!(ee_orientation(90), PinOrientation::Up));
        assert!(matches!(ee_orientation(180), PinOrientation::Left));
        assert!(matches!(ee_orientation(270), PinOrientation::Down));
    }

    #[test]
    fn converted_pin_uses_source_length() {
        let source = EeSymbolPin {
            electrical: EePinElectrical::Input,
            designator: "1".to_string(),
            pos_x: 360.0,
            pos_y: 270.0,
            rotation_deg: 180,
            name: "OFFSET N1".to_string(),
            pin_length: 10.0,
            is_displayed: true,
            name_visible: true,
            number_visible: true,
            is_inverted: false,
            is_clock: false,
        };
        let converted = convert_pin(&source, 1, 0.0, 0.0, false, 1);
        assert_eq!(converted.length, Coord::from_mils(100.0));
        assert!(matches!(converted.orientation, PinOrientation::Left));
        assert_eq!(converted.location.x, ee_px_to_coord(370.0));
    }

    #[test]
    fn wire_connection_stays_at_source_endpoint_in_all_four_directions() {
        for rotation in [0, 90, 180, 270, -90, 450] {
            let source = EeSymbolPin {
                electrical: EePinElectrical::Unspecified,
                designator: "1".to_string(),
                pos_x: 380.0,
                pos_y: 290.0,
                rotation_deg: rotation,
                name: "K".to_string(),
                pin_length: 10.0,
                is_displayed: true,
            name_visible: true,
            number_visible: true,
                is_inverted: false,
                is_clock: false,
            };
            let converted = convert_pin(&source, 1, 400.0, 290.0, false, 1);
            let endpoint = match converted.orientation {
                PinOrientation::Right => CoordPoint::new(converted.location.x + converted.length, converted.location.y),
                PinOrientation::Up => CoordPoint::new(converted.location.x, converted.location.y + converted.length),
                PinOrientation::Left => CoordPoint::new(converted.location.x - converted.length, converted.location.y),
                PinOrientation::Down => CoordPoint::new(converted.location.x, converted.location.y - converted.length),
            };
            assert_eq!(endpoint, CoordPoint::new(Coord::from_mils(-200.0), Coord::from_mils(0.0)));
        }
    }

    #[test]
    fn closed_symbol_outline_uses_filled_body_style() {
        let outline = EeSymbolPolyline {
            stroke_width: 1.0,
            points: vec![(380.0, 330.0), (420.0, 310.0), (380.0, 290.0), (380.0, 330.0)],
            fill: false,
        };
        let converted = convert_polygon(&outline, 1, 0.0, 0.0, false).unwrap();
        assert_eq!(converted.vertices.len(), 3);
        assert_eq!(converted.color, 0x80);
        assert_eq!(converted.fill_color, 0x00B0_FFFF);
        assert!(converted.is_filled);
    }

    #[test]
    fn rectangle_keeps_source_body_bounds() {
        let source = EeSymbolRectangle {
            stroke_width: 1.0,
            pos_x: 380.0,
            pos_y: 290.0,
            width: 80.0,
            height: 80.0,
            fill: false,
        };
        let converted = convert_rectangle(&source, 1, 0.0, 0.0, false);
        assert_eq!(converted.corner1.x, ee_px_to_coord(380.0));
        assert_eq!(converted.corner2.x, ee_px_to_coord(460.0));
        assert_eq!(converted.color, 0x80);
        assert!(converted.is_filled);
    }
}

fn with_owner(owner_part_id: i32) -> PrimitiveCommon {
    PrimitiveCommon {
        owner_part_id,
        is_not_accessible: true,
        ..Default::default()
    }
}

fn ee_px_to_coord(px: f64) -> Coord {
    Coord::from_mils(px * 10.0)
}

fn builtin_parameters(ee: &EeSymbol) -> Vec<(String, String)> {
    let mut v = Vec::new();
    if !ee.info.manufacturer.is_empty() {
        v.push(("Manufacturer".to_string(), ee.info.manufacturer.clone()));
    }
    if !ee.info.mpn.is_empty() {
        v.push(("MPN".to_string(), ee.info.mpn.clone()));
        v.push(("Manufacturer Part".to_string(), ee.info.mpn.clone()));
    }
    if !ee.info.lcsc_id.is_empty() {
        v.push(("LCSC Part".to_string(), ee.info.lcsc_id.clone()));
    }
    if !ee.info.datasheet.is_empty() {
        v.push(("Datasheet".to_string(), ee.info.datasheet.clone()));
    }
    if !ee.info.keywords.is_empty() {
        v.push(("Keywords".to_string(), ee.info.keywords.clone()));
    }
    v
}

