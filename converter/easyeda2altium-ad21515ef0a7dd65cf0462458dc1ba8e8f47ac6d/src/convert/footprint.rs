use altium::coord::{Coord, CoordPoint};
use altium::enums::{PadHoleType, PadShape};
use altium::pcb::primitives::{Arc, ComponentBody, Pad, Region, Text, Track};
use altium::pcb::{Component, Model3d};

use crate::easyeda::canvas::ee_px_to_coord;
use crate::easyeda::types::*;

fn ee_layer_allowed_for_solid_region(layer_id: i32) -> bool {
    matches!(layer_id, 3 | 4 | 13 | 14 | 99)
}

fn ee_layer_to_altium(layer_id: i32) -> i32 {
    match layer_id {
        1 => 1,
        2 => 32,
        3 => 33,
        4 => 34,
        5 => 35,
        6 => 36,
        7 => 37,
        8 => 38,
        10 => 56,
        11 => 74,
        12 => 57,
        13 => 69,
        14 => 70,
        15 => 71,
        99 => 57,
        _ => 57,
    }
}

pub fn convert_footprint(ee: &EeFootprint) -> Component {
    let ox = ee.bbox_x;
    let oy = ee.bbox_y;

    let mut comp = Component::new(ee.info.name.clone());
    if !ee.info.description.is_empty() {
        comp.description = Some(ee.info.description.clone());
    }
    if !ee.info.lcsc_id.is_empty() {
        comp.additional_parameters
            .insert("LCSC Part".to_string(), ee.info.lcsc_id.clone());
    }
    if !ee.info.manufacturer.is_empty() {
        comp.additional_parameters
            .insert("Manufacturer".to_string(), ee.info.manufacturer.clone());
    }
    if !ee.info.mpn.is_empty() {
        comp.additional_parameters
            .insert("MPN".to_string(), ee.info.mpn.clone());
    }

    for p in &ee.pads {
        comp.pads.push(convert_pad(p, ox, oy));
    }
    for t in &ee.tracks {
        comp.tracks.extend(convert_track(t, ox, oy));
    }
    for h in &ee.holes {
        comp.pads.push(convert_npth_hole(h, ox, oy));
    }
    for v in &ee.vias {
        comp.tracks
            .extend(via_to_marker_tracks(v, ox, oy));
    }
    for c in &ee.circles {
        comp.arcs.push(convert_circle(c, ox, oy));
    }
    for a in &ee.arcs {
        if let Some(arc) = convert_arc(a, ox, oy) {
            comp.arcs.push(arc);
        } else if let Some(paths) = crate::easyeda::geometry::path_polylines(&a.raw_path) {
            for path in paths {
                for pair in path.windows(2) {
                    let mut track = Track::default();
                    track.layer = ee_layer_to_altium(a.layer_id);
                    track.start = CoordPoint::new(ee_px_to_coord(pair[0].0 - ox), -ee_px_to_coord(pair[0].1 - oy));
                    track.end = CoordPoint::new(ee_px_to_coord(pair[1].0 - ox), -ee_px_to_coord(pair[1].1 - oy));
                    track.width = ee_px_to_coord(a.stroke_width.max(0.01));
                    comp.tracks.push(track);
                }
            }
        } else {
            eprintln!("warning: unsupported footprint arc path; no partial geometry emitted");
        }
    }
    for r in &ee.rectangles {
        comp.tracks.extend(convert_rectangle(r, ox, oy));
    }
    for t in &ee.texts {
        comp.texts.push(convert_text(t, ox, oy));
    }
    for sr in &ee.solid_regions {
        if let Some(region) = convert_solid_region(sr, ox, oy) {
            comp.regions.push(region);
        }
    }

    if let Some(model) = &ee.model_3d {
        if !model.uuid.is_empty() {
            let outline = component_bbox_outline(&comp);
            comp.component_bodies
                .push(make_component_body(model, outline));
        }
    }

    comp
}

fn component_bbox_outline(comp: &altium::pcb::Component) -> Vec<CoordPoint> {
    let mut min_x = i32::MAX;
    let mut min_y = i32::MAX;
    let mut max_x = i32::MIN;
    let mut max_y = i32::MIN;
    let mut bump = |x: Coord, y: Coord| {
        let (xr, yr) = (x.to_raw(), y.to_raw());
        if xr < min_x { min_x = xr; }
        if yr < min_y { min_y = yr; }
        if xr > max_x { max_x = xr; }
        if yr > max_y { max_y = yr; }
    };
    for p in &comp.pads {
        bump(p.location.x, p.location.y);
    }
    for t in &comp.tracks {
        bump(t.start.x, t.start.y);
        bump(t.end.x, t.end.y);
    }
    if min_x == i32::MAX {
        let half = Coord::from_mils(50.0);
        return vec![
            CoordPoint::new(-half, -half),
            CoordPoint::new(half, -half),
            CoordPoint::new(half, half),
            CoordPoint::new(-half, half),
        ];
    }
    let p1 = CoordPoint::new(Coord::from_raw(min_x), Coord::from_raw(min_y));
    let p2 = CoordPoint::new(Coord::from_raw(max_x), Coord::from_raw(min_y));
    let p3 = CoordPoint::new(Coord::from_raw(max_x), Coord::from_raw(max_y));
    let p4 = CoordPoint::new(Coord::from_raw(min_x), Coord::from_raw(max_y));
    vec![p1, p2, p3, p4]
}

pub fn make_model_entry(ee_model: &Ee3dModel, raw_step: Vec<u8>) -> Model3d {
    Model3d {
        id: ee_model.uuid.clone(),
        name: format!(
            "{}.step",
            if ee_model.name.is_empty() {
                ee_model.uuid.as_str()
            } else {
                ee_model.name.as_str()
            }
        ),
        is_embedded: true,
        model_source: "Undefined".to_string(),
        rotation_x: ee_model.rotation_x_deg,
        rotation_y: ee_model.rotation_y_deg,
        rotation_z: ee_model.rotation_z_deg,
        dz: Coord::from_mm(ee_model.translation_z_mm).to_raw(),
        checksum: 0,
        step_data: String::from_utf8_lossy(&raw_step).into_owned(),
    }
}

fn convert_pad(p: &EeFootprintPad, ox: f64, oy: f64) -> Pad {
    let layer = ee_layer_to_altium(p.layer_id);
    let shape = match p.shape.to_uppercase().as_str() {
        "ELLIPSE" => PadShape::Round,
        "RECT" => PadShape::Rectangular,
        "OVAL" => PadShape::Round,
        "POLYGON" => PadShape::Rectangular,
        _ => PadShape::Rectangular,
    };
    let hole_type = if p.hole_length > 0.0 {
        PadHoleType::Slot
    } else {
        PadHoleType::Round
    };
    let designator = normalize_pad_number(&p.number);
    let xsize = ee_px_to_coord(p.width);
    let ysize = ee_px_to_coord(p.height);
    let x = ee_px_to_coord(p.center_x - ox);
    let y = -ee_px_to_coord(p.center_y - oy);
    let mut pad = Pad::default();
    pad.designator = Some(designator);
    pad.layer = layer;
    pad.location = CoordPoint::new(x, y);
    pad.size_top = CoordPoint::new(xsize, ysize);
    pad.size_middle = CoordPoint::new(xsize, ysize);
    pad.size_bottom = CoordPoint::new(xsize, ysize);
    pad.shape_top = shape;
    pad.shape_middle = shape;
    pad.shape_bottom = shape;
    pad.hole_size = ee_px_to_coord(p.hole_radius * 2.0);
    pad.hole_type = hole_type;
    pad.hole_slot_length = ee_px_to_coord(p.hole_length).to_raw();
    pad.is_plated = p.is_plated;
    pad.rotation = p.rotation_deg;
    pad
}

/// Source points are absolute canvas coordinates (Y down). Altium's hole
/// rotation is local to the pad, so remove the generated pad rotation after
/// reflecting the source center-line into PCB coordinates (Y up).
pub fn slot_rotation(p: &EeFootprintPad, pad_rotation: f64) -> crate::Result<f64> {
    let values = p.hole_points.split_whitespace().map(str::parse::<f64>)
        .collect::<std::result::Result<Vec<_>, _>>()
        .map_err(|_| crate::Error::Convert(format!("invalid slot endpoints for {}", p.number)))?;
    if values.len() != 4 || values.iter().any(|v| !v.is_finite()) || !pad_rotation.is_finite() {
        return Err(crate::Error::Convert(format!("missing or invalid slot endpoints for {}", p.number)));
    }
    let dx=values[2]-values[0];let dy=values[3]-values[1];
    if dx.hypot(dy)<1e-6{return Err(crate::Error::Convert(format!("zero-length slot center line for {}",p.number)));}
    Ok(((-dy).atan2(dx).to_degrees()-pad_rotation).rem_euclid(180.0))
}

pub(crate) fn normalize_pad_number(num: &str) -> String {
    if num.contains('(') && num.contains(')') {
        let l = num.find('(').unwrap();
        let r = num.find(')').unwrap();
        if r > l {
            return num[l + 1..r].to_string();
        }
    }
    num.to_string()
}

fn convert_npth_hole(h: &EeFootprintHole, ox: f64, oy: f64) -> Pad {
    let dia = ee_px_to_coord(h.radius * 2.0);
    let mut pad = Pad::default();
    pad.designator = Some("MH".to_string());
    pad.layer = 74; // MultiLayer
    pad.location = CoordPoint::new(
        ee_px_to_coord(h.center_x - ox),
        -ee_px_to_coord(h.center_y - oy),
    );
    pad.size_top = CoordPoint::new(dia, dia);
    pad.size_middle = CoordPoint::new(dia, dia);
    pad.size_bottom = CoordPoint::new(dia, dia);
    pad.shape_top = PadShape::Round;
    pad.shape_middle = PadShape::Round;
    pad.shape_bottom = PadShape::Round;
    pad.hole_size = dia;
    pad.is_plated = false;
    pad
}

fn convert_track(t: &EeFootprintTrack, ox: f64, oy: f64) -> Vec<Track> {
    let pts = parse_point_pairs(&t.points);
    let layer = ee_layer_to_altium(t.layer_id);
    let width = ee_px_to_coord(t.stroke_width.max(0.01));
    let mut out = Vec::with_capacity(pts.len().saturating_sub(1));
    for w in pts.windows(2) {
        let mut tr = Track::default();
        tr.layer = layer;
        tr.start = CoordPoint::new(
            ee_px_to_coord(w[0].0 - ox),
            -ee_px_to_coord(w[0].1 - oy),
        );
        tr.end = CoordPoint::new(
            ee_px_to_coord(w[1].0 - ox),
            -ee_px_to_coord(w[1].1 - oy),
        );
        tr.width = width;
        tr.enabled = true;
        out.push(tr);
    }
    out
}

fn via_to_marker_tracks(_v: &EeFootprintVia, _ox: f64, _oy: f64) -> Vec<Track> {
    Vec::new()
}

fn convert_circle(c: &EeFootprintCircle, ox: f64, oy: f64) -> Arc {
    let mut a = Arc::default();
    a.layer = ee_layer_to_altium(c.layer_id);
    a.center = CoordPoint::new(
        ee_px_to_coord(c.cx - ox),
        -ee_px_to_coord(c.cy - oy),
    );
    a.radius = ee_px_to_coord(c.radius);
    a.start_angle = 0.0;
    a.end_angle = 360.0;
    a.width = ee_px_to_coord(c.stroke_width.max(0.01));
    a.enabled = true;
    a
}

fn convert_rectangle(r: &EeFootprintRect, ox: f64, oy: f64) -> Vec<Track> {
    let layer = ee_layer_to_altium(r.layer_id);
    let width = ee_px_to_coord(r.stroke_width.max(0.01));
    let x0 = ee_px_to_coord(r.x - ox);
    let y0 = -ee_px_to_coord(r.y - oy);
    let x1 = ee_px_to_coord(r.x + r.width - ox);
    let y1 = -ee_px_to_coord(r.y + r.height - oy);
    let make = |sx, sy, ex, ey| {
        let mut tr = Track::default();
        tr.layer = layer;
        tr.start = CoordPoint::new(sx, sy);
        tr.end = CoordPoint::new(ex, ey);
        tr.width = width;
        tr.enabled = true;
        tr
    };
    vec![
        make(x0, y0, x1, y0),
        make(x1, y0, x1, y1),
        make(x1, y1, x0, y1),
        make(x0, y1, x0, y0),
    ]
}

fn convert_text(t: &EeFootprintText, ox: f64, oy: f64) -> Text {
    let layer = ee_layer_to_altium(t.layer_id);
    let mut text = Text::default();
    text.layer = layer;
    text.text = t.text.clone();
    text.location = CoordPoint::new(
        ee_px_to_coord(t.center_x - ox),
        -ee_px_to_coord(t.center_y - oy),
    );
    text.height = ee_px_to_coord(t.font_size.max(1.0));
    text.rotation = t.rotation_deg;
    text.stroke_width = ee_px_to_coord(t.stroke_width.max(0.01));
    text.is_designator = t.kind == "P";
    text.is_comment = t.kind == "N";
    text.font_name = Some("Arial".to_string());
    text.is_hidden = !t.is_displayed;
    text
}

fn convert_arc(a: &EeFootprintArc, ox: f64, oy: f64) -> Option<Arc> {
    let spec = crate::easyeda::geometry::single_arc(&a.raw_path)?;
    let geometry = spec.geometry()?;
    let (start_angle, end_angle) = geometry.altium_angles()?;
    let mut arc = Arc::default();
    arc.layer = ee_layer_to_altium(a.layer_id);
    arc.center = CoordPoint::new(ee_px_to_coord(geometry.center.0 - ox), -ee_px_to_coord(geometry.center.1 - oy));
    arc.radius = ee_px_to_coord(geometry.rx);
    arc.start_angle = start_angle;
    arc.end_angle = end_angle;
    arc.width = ee_px_to_coord(a.stroke_width.max(0.01));
    arc.enabled = true;
    Some(arc)
}

fn convert_solid_region(sr: &EeFootprintSolidRegion, ox: f64, oy: f64) -> Option<Region> {
    if sr.region_type != "solid" && sr.region_type != "npth" {
        return None;
    }
    if !ee_layer_allowed_for_solid_region(sr.layer_id) {
        return None;
    }
    let layer = ee_layer_to_altium(sr.layer_id);
    let pts = parse_svg_path_to_points(&sr.raw_path);
    if pts.len() < 3 {
        eprintln!("warning: unsupported footprint region path; no partial geometry emitted");
        return None;
    }
    let outline: Vec<CoordPoint> = pts
        .into_iter()
        .map(|(x, y)| CoordPoint::new(ee_px_to_coord(x - ox), -ee_px_to_coord(y - oy)))
        .collect();
    let mut region = Region::default();
    region.layer = layer;
    region.outline = outline;
    region.kind = 0;
    region.enabled = true;
    Some(region)
}

fn make_component_body(ee_model: &Ee3dModel, outline: Vec<CoordPoint>) -> ComponentBody {
    let mut body = ComponentBody::default();
    body.outline = outline;
    body.model_id = Some(ee_model.uuid.clone());
    body.model_name = Some(format!(
        "{}.step",
        if ee_model.name.is_empty() {
            ee_model.uuid.as_str()
        } else {
            ee_model.name.as_str()
        }
    ));
    body.model_embed = true;
    body.model_3d_rot_x = ee_model.rotation_x_deg;
    body.model_3d_rot_y = ee_model.rotation_y_deg;
    body.model_3d_rot_z = ee_model.rotation_z_deg;
    body.model_2d_location = CoordPoint::new(
        Coord::from_mm(ee_model.translation_x_mm),
        Coord::from_mm(ee_model.translation_y_mm),
    );
    body.model_3d_dz = Coord::from_mm(ee_model.translation_z_mm);
    body.model_source = Some("Undefined".to_string());
    body.body_color_3d = 0xE0_E0_E0;
    body.body_opacity_3d = 1.0;
    body.layer_name = "MECHANICAL1".to_string();
    body.layer = 57;
    body
}

fn parse_point_pairs(s: &str) -> Vec<(f64, f64)> {
    let toks: Vec<&str> = s.split_whitespace().collect();
    let mut out = Vec::with_capacity(toks.len() / 2);
    let mut i = 0;
    while i + 1 < toks.len() {
        let x = toks[i].parse::<f64>().unwrap_or(0.0);
        let y = toks[i + 1].parse::<f64>().unwrap_or(0.0);
        out.push((x, y));
        i += 2;
    }
    out
}

fn parse_svg_path_to_points(path: &str) -> Vec<(f64, f64)> {
    // Reuse the complete SVG parser used by symbols. Splitting on whitespace
    // loses the first coordinate in legal compact commands such as M380.315.
    // A Region represents one contour; never join separate subpaths through
    // artificial diagonals or fill an unrepresented hole.
    match crate::easyeda::geometry::path_polylines(path) {
        Some(mut paths) if paths.len() == 1 => paths.remove(0),
        _ => Vec::new(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn pad_number_normalize() {
        assert_eq!(normalize_pad_number("A(1)"), "1");
        assert_eq!(normalize_pad_number("3"), "3");
        assert_eq!(normalize_pad_number("VCC"), "VCC");
    }

    #[test]
    fn compact_qfn_outline_keeps_all_coordinates() {
        // Actual mechanical outline from the official C54540373 CAD envelope.
        let path = "M380.315 319.685 419.685 319.685 L419.685 319.685 419.685 280.315 L419.685 280.315 380.315 280.315 L380.315 280.315 380.315 319.685  Z";
        let points = parse_svg_path_to_points(path);
        assert!(points.len() >= 4);
        assert!(points.iter().all(|&(x,y)| (380.315..=419.685).contains(&x) && (280.315..=319.685).contains(&y)), "corrupted coordinates: {points:?}");
        assert_eq!(points[0], (380.315,319.685));
    }

    #[test]
    fn compact_relative_region_preserves_polygon() {
        let points = parse_svg_path_to_points("m10,20h4v3h-4z");
        assert_eq!(points, vec![(10.0,20.0),(14.0,20.0),(14.0,23.0),(10.0,23.0),(10.0,20.0)]);
    }

    #[test]
    fn malformed_region_is_not_partially_drawn() {
        assert!(parse_svg_path_to_points("M10 20L30 ? 40 50Z").is_empty());
    }

    #[test]
    fn separate_region_contours_are_not_connected_by_diagonals() {
        assert!(parse_svg_path_to_points("M0 0H10V10H0Z M2 2H8V8H2Z").is_empty());
    }

    #[test]
    fn compact_arc_keeps_center_radius_and_reflected_sweep() {
        let source=EeFootprintArc{stroke_width:0.5,layer_id:3,raw_path:"M10,0A10,10 0 0 1 0,10".into()};
        let arc=convert_arc(&source,0.0,0.0).unwrap();
        assert!(arc.center.x.to_mils().abs()<0.001 && arc.center.y.to_mils().abs()<0.001);
        assert!((arc.radius.to_mils()-100.0).abs()<0.001);
        assert_eq!((arc.start_angle,arc.end_angle),(270.0,0.0));
    }

    #[test]
    fn elliptical_footprint_arc_is_flattened_without_a_circle_guess() {
        let mut source=EeFootprint::default();
        source.arcs.push(EeFootprintArc{stroke_width:0.5,layer_id:3,raw_path:"M10,0A10,5 0 0 1 0,5".into()});
        let converted=convert_footprint(&source);
        assert!(converted.arcs.is_empty());
        assert!(converted.tracks.len()>2);
        assert_eq!(converted.tracks.first().unwrap().start.x,ee_px_to_coord(10.0));
        assert_eq!(converted.tracks.last().unwrap().end.y,-ee_px_to_coord(5.0));
    }
}
