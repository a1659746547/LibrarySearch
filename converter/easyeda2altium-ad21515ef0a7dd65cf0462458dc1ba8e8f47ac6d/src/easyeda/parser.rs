//! Parse `dataStr.shape: Vec<String>` from the EasyEDA API into typed
//! `EeSymbol` / `EeFootprint`.
//!
//! Format reference: `easyeda2kicad/easyeda/easyeda_importer.py`. Each shape
//! is a `~`-separated string starting with a designator. Pins use `^^` for
//! sub-segments.

use serde_json::Value;

use super::types::*;
use crate::error::{Error, Result};

// ---------- helpers ----------

fn parse_f(s: Option<&str>) -> f64 {
    s.and_then(|x| x.trim().parse::<f64>().ok()).unwrap_or(0.0)
}

fn parse_i(s: Option<&str>) -> i32 {
    s.and_then(|x| x.trim().parse::<f64>().ok())
        .map(|x| x as i32)
        .unwrap_or(0)
}

fn parse_b(s: Option<&str>) -> bool {
    match s {
        Some(v) => {
            let lc = v.trim().to_ascii_lowercase();
            matches!(
                lc.as_str(),
                "true" | "1" | "yes" | "on" | "show" | "y" | "t"
            )
        }
        None => false,
    }
}

fn at<'a>(parts: &'a [&str], idx: usize) -> Option<&'a str> {
    parts.get(idx).copied()
}

// ---------- symbol top-level ----------

pub fn parse_symbol(api_result: &Value) -> Result<EeSymbol> {
    let datastr = api_result
        .get("dataStr")
        .ok_or_else(|| Error::Parse("missing dataStr".into()))?;
    let head = datastr
        .get("head")
        .ok_or_else(|| Error::Parse("missing dataStr.head".into()))?;
    let bbox = datastr.get("BBox").cloned().unwrap_or(Value::Null);
    let c_para = head.get("c_para").cloned().unwrap_or(Value::Null);
    let lcsc = api_result.get("lcsc").cloned().unwrap_or(Value::Null);
    let description = safe_string(api_result.get("description").unwrap_or(&Value::Null));
    let mut sym = build_symbol_unit(datastr, &c_para, &lcsc, &bbox, head, &description)?;

    // Sub-symbols for multi-unit components.
    if let Some(arr) = api_result.get("subparts").and_then(|v| v.as_array()) {
        for sp in arr {
            let sp_datastr = sp
                .get("dataStr")
                .ok_or_else(|| Error::Parse("subpart missing dataStr".into()))?;
            let sp_head = sp_datastr
                .get("head")
                .ok_or_else(|| Error::Parse("subpart missing dataStr.head".into()))?;
            let sp_bbox = sp_datastr.get("BBox").cloned().unwrap_or(Value::Null);
            let sp_cpara = sp_head.get("c_para").cloned().unwrap_or(Value::Null);
            let sub =
                build_symbol_unit(sp_datastr, &sp_cpara, &lcsc, &sp_bbox, sp_head, &description)?;
            sym.sub_symbols.push(sub);
        }
    }
    for unit in std::iter::once(&sym).chain(sym.sub_symbols.iter()) {
        for pin in &unit.pins {
            if !pin.pin_length.is_finite() || pin.pin_length < 0.0 {
                return Err(Error::Parse(format!("pin {} in {} has unsupported or inconsistent line geometry; refusing to guess its length", pin.designator, unit.info.name)));
            }
        }
    }
    Ok(sym)
}

fn build_symbol_unit(
    datastr: &Value,
    c_para: &Value,
    lcsc: &Value,
    bbox: &Value,
    head: &Value,
    description: &str,
) -> Result<EeSymbol> {
    let info = EeSymbolInfo {
        name: sanitize_component_name(&safe_string(c_para.get("name").unwrap_or(&Value::Null))),
        prefix: safe_string(c_para.get("pre").unwrap_or(&Value::Null)),
        package: safe_string(c_para.get("package").unwrap_or(&Value::Null)),
        manufacturer: pick_str(c_para, &["Manufacturer", "BOM_Manufacturer"]),
        mpn: pick_str(c_para, &["Manufacturer Part", "BOM_Manufacturer Part"]),
        datasheet: {
            let url = safe_string(lcsc.get("url").unwrap_or(&Value::Null));
            let lcsc_id = safe_string(lcsc.get("number").unwrap_or(&Value::Null));
            if !url.is_empty() {
                url
            } else if !lcsc_id.is_empty() {
                format!("https://www.lcsc.com/datasheet/{}.pdf", lcsc_id)
            } else {
                String::new()
            }
        },
        lcsc_id: safe_string(lcsc.get("number").unwrap_or(&Value::Null)),
        keywords: tags_to_string(datastr.get("tags")),
        description: description.to_string(),
    };

    let bbox_x = safe_float(bbox.get("x").unwrap_or(&Value::Null));
    let bbox_y = safe_float(bbox.get("y").unwrap_or(&Value::Null));
    let bbox_w = safe_float(bbox.get("width").unwrap_or(&Value::Null));
    let bbox_h = safe_float(bbox.get("height").unwrap_or(&Value::Null));
    let (origin_x, origin_y) = if bbox_w > 0.0 || bbox_h > 0.0 {
        (bbox_x + bbox_w / 2.0, bbox_y + bbox_h / 2.0)
    } else {
        (
            safe_float(head.get("x").unwrap_or(&Value::Null)),
            safe_float(head.get("y").unwrap_or(&Value::Null)),
        )
    };

    let mut sym = EeSymbol {
        info,
        bbox: EeBbox {
            x: origin_x,
            y: origin_y,
            width: bbox_w,
            height: bbox_h,
        },
        ..Default::default()
    };

    if let Some(shapes) = datastr.get("shape").and_then(|v| v.as_array()) {
        for shape in shapes {
            if let Some(s) = shape.as_str() {
                parse_symbol_shape(s, &mut sym);
            }
        }
    }
    for unit in std::iter::once(&sym).chain(sym.sub_symbols.iter()) {
        for pin in &unit.pins {
            if !pin.pin_length.is_finite() || pin.pin_length < 0.0 {
                return Err(Error::Parse(format!("pin {} in {} has unsupported or inconsistent line geometry; refusing to guess its length", pin.designator, unit.info.name)));
            }
        }
    }
    Ok(sym)
}

fn pick_str(c_para: &Value, keys: &[&str]) -> String {
    for k in keys {
        if let Some(v) = c_para.get(*k) {
            let s = safe_string(v);
            if !s.is_empty() {
                return s;
            }
        }
    }
    String::new()
}

fn tags_to_string(tags: Option<&Value>) -> String {
    let Some(arr) = tags.and_then(|v| v.as_array()) else {
        return String::new();
    };
    arr.iter()
        .filter_map(|v| v.as_str())
        .collect::<Vec<_>>()
        .join(" ")
}

fn sanitize_component_name(name: &str) -> String {
    let s = name;
    let mut end = s.len();
    for ch in ['(', '['] {
        if let Some(idx) = s.find(ch) {
            end = end.min(idx);
        }
    }
    s[..end].trim().to_string()
}

// ---------- symbol shape dispatch ----------

pub fn parse_symbol_shape(shape: &str, sym: &mut EeSymbol) {
    let designator = match shape.split('~').next() {
        Some(d) => d,
        None => return,
    };
    match designator {
        "P" => parse_pin(shape, sym),
        "R" => parse_rectangle(shape, sym),
        "C" => parse_circle(shape, sym),
        "E" => parse_ellipse(shape, sym),
        "PL" => parse_polyline(shape, sym, false),
        "PG" => parse_polyline(shape, sym, true),
        "A" => parse_arc(shape, sym),
        "T" => parse_text(shape, sym),
        "PT" => parse_symbol_path(shape, sym),
        _ => {}
    }
}

fn parse_pin(shape: &str, sym: &mut EeSymbol) {
    // Format: P~settings^^dot^^path^^name^^num^^dot_bis^^clock
    //   settings: visibility~type~spice_pin_number~pos_x~pos_y~rotation~id~is_locked
    let segments: Vec<&str> = shape.split("^^").collect();
    if segments.is_empty() {
        return;
    }
    let settings: Vec<&str> = segments[0].split('~').collect();
    if settings.len() < 7 {
        return;
    }
    let is_displayed = parse_b(at(&settings, 1));
    let pin_type = parse_i(at(&settings, 2));
    let anchor_x = parse_f(at(&settings, 4));
    let anchor_y = parse_f(at(&settings, 5));
    // The snap point is electrical. Legacy symbols keep the graphic anchor
    // at the body, whereas newer symbols put both fields at the connection.
    let snap: Option<(f64, f64)> = segments.get(1).and_then(|s| {
        let mut parts = s.split('~');
        Some((parts.next()?.trim().parse::<f64>().ok()?, parts.next()?.trim().parse::<f64>().ok()?))
    }).filter(|(x,y)| x.is_finite() && y.is_finite());
    let (pos_x, pos_y) = snap.unwrap_or((anchor_x, anchor_y));
    let rotation = parse_i(at(&settings, 6));

    // Pin number lives in segment 4[4] (per Python's importer)
    let designator = if segments.len() > 4 {
        let nseg: Vec<&str> = segments[4].split('~').collect();
        nseg.get(4).copied().unwrap_or("").to_string()
    } else {
        String::new()
    };

    let pin_length = if segments.len() > 2 {
        let path_seg: Vec<&str> = segments[2].split('~').collect();
        let path = path_seg.first().copied().unwrap_or("");
        let length = derive_pin_length(path);
        let valid = pin_path_endpoints(path).is_some_and(|(start, end)| {
            let expected = match rotation.rem_euclid(360) {
                0 => (pos_x - length, pos_y), 90 => (pos_x, pos_y + length),
                180 => (pos_x + length, pos_y), 270 => (pos_x, pos_y - length),
                _ => return false,
            };
            // SVG line order is graphical, not electrical: some capacitors
            // start at the body and draw toward the connection point.
            let matches = |connection: (f64, f64), body: (f64, f64)| {
                (connection.0 - pos_x).hypot(connection.1 - pos_y) < 1e-6
                    && (body.0 - expected.0).hypot(body.1 - expected.1) < 1e-6
            };
            let anchor_on_line_end = |p: (f64,f64)| (p.0-anchor_x).hypot(p.1-anchor_y) < 1e-6;
            (matches(start, end) || matches(end, start))
                && (anchor_on_line_end(start) || anchor_on_line_end(end))
        });
        if valid { length } else { f64::NAN }
    } else {
        f64::NAN
    };

    // Name (segment 3 index 4)
    let name = if segments.len() > 3 {
        let nseg: Vec<&str> = segments[3].split('~').collect();
        nseg.get(4).copied().unwrap_or("").to_string()
    } else {
        String::new()
    };

    // dot (segment 5 index 0)
    let is_inverted = if segments.len() > 5 {
        let dseg: Vec<&str> = segments[5].split('~').collect();
        parse_b(dseg.first().copied())
    } else {
        false
    };
    // clock (segment 6 index 0)
    let is_clock = if segments.len() > 6 {
        let cseg: Vec<&str> = segments[6].split('~').collect();
        parse_b(cseg.first().copied())
    } else {
        false
    };

    sym.pins.push(EeSymbolPin {
        electrical: EePinElectrical::from_int(pin_type as i64),
        designator,
        pos_x,
        pos_y,
        rotation_deg: rotation,
        name,
        pin_length,
        is_displayed,
        name_visible: segments.get(3).is_some_and(|text|parse_b(text.split('~').next())),
        number_visible: segments.get(4).is_some_and(|text|parse_b(text.split('~').next())),
        is_inverted,
        is_clock,
    });
}

/// Decode the full SVG line, including compact commands and absolute endpoints.
/// A pin must be a single straight axis-aligned segment, not a partial path.
fn pin_path_endpoints(path: &str) -> Option<((f64, f64), (f64, f64))> {
    let paths = crate::easyeda::geometry::path_polylines(path)?;
    if paths.len() != 1 { return None; }
    let points = &paths[0];
    let start = *points.first()?;
    let end = *points.last()?;
    let dx = end.0 - start.0;
    let dy = end.1 - start.1;
    if dx.abs() > 1e-7 && dy.abs() > 1e-7 { return None; }
    if points.iter().any(|point| if dx.abs() <= 1e-7 {
        (point.0 - start.0).abs() > 1e-7
    } else { (point.1 - start.1).abs() > 1e-7 }) { return None; }
    let travelled: f64 = points.windows(2).map(|pair| (pair[1].0 - pair[0].0).hypot(pair[1].1 - pair[0].1)).sum();
    if (travelled - dx.hypot(dy)).abs() > 1e-7 { return None; }
    Some((start, end))
}

fn derive_pin_length(path: &str) -> f64 {
    pin_path_endpoints(path).map(|(a, b)| (b.0-a.0).hypot(b.1-a.1)).unwrap_or(f64::NAN)
}

fn parse_rectangle(shape: &str, sym: &mut EeSymbol) {
    // R~x~y~rx~ry~width~height~stroke~stroke_width~stroke_style~fill~id~locked
    // OR R~x~y~~~width~height~... (no rounded corners)
    let parts: Vec<&str> = shape.split('~').collect();
    if parts.len() < 7 {
        return;
    }
    let pos_x = parse_f(at(&parts, 1));
    let pos_y = parse_f(at(&parts, 2));
    let (width, height, fill_idx) = if at(&parts, 3) == Some("") && at(&parts, 4) == Some("") {
        (parse_f(at(&parts, 5)), parse_f(at(&parts, 6)), 10usize)
    } else {
        (parse_f(at(&parts, 5)), parse_f(at(&parts, 6)), 10usize)
    };
    let fill_color = at(&parts, fill_idx).unwrap_or("none");
    let fill = !matches!(fill_color, "" | "none" | "None");
    sym.rectangles.push(EeSymbolRectangle {
        stroke_width: at(&parts, 8).and_then(|s|s.parse().ok()).unwrap_or(1.0),
        pos_x,
        pos_y,
        width,
        height,
        fill,
    });
}

fn parse_circle(shape: &str, sym: &mut EeSymbol) {
    // C~cx~cy~radius~stroke~stroke_width~stroke_style~fill~id~locked
    let parts: Vec<&str> = shape.split('~').collect();
    if parts.len() < 4 {
        return;
    }
    let fill_color = at(&parts, 7).unwrap_or("none");
    sym.circles.push(EeSymbolCircle {
        stroke_width: at(&parts, 5).and_then(|s|s.parse().ok()).unwrap_or(1.0),
        center_x: parse_f(at(&parts, 1)),
        center_y: parse_f(at(&parts, 2)),
        radius: parse_f(at(&parts, 3)),
        fill: !matches!(fill_color, "" | "none" | "None"),
    });
}

fn parse_ellipse(shape: &str, sym: &mut EeSymbol) {
    // E~cx~cy~rx~ry~stroke~stroke_width~stroke_style~fill~id~locked
    let parts: Vec<&str> = shape.split('~').collect();
    if parts.len() < 5 {
        return;
    }
    let fill_color = at(&parts, 8).unwrap_or("none");
    sym.ellipses.push(EeSymbolEllipse {
        stroke_width: at(&parts, 6).and_then(|s|s.parse().ok()).unwrap_or(1.0),
        center_x: parse_f(at(&parts, 1)),
        center_y: parse_f(at(&parts, 2)),
        radius_x: parse_f(at(&parts, 3)),
        radius_y: parse_f(at(&parts, 4)),
        fill: !matches!(fill_color, "" | "none" | "None"),
    });
}

fn parse_polyline(shape: &str, sym: &mut EeSymbol, closed: bool) {
    // PL/PG~points~stroke~stroke_width~stroke_style~fill~id~locked
    let parts: Vec<&str> = shape.split('~').collect();
    if parts.len() < 2 {
        return;
    }
    let points = parse_point_pairs(parts[1]);
    let fill_color = at(&parts, 5).unwrap_or("none");
    let fill = closed || !matches!(fill_color, "" | "none" | "None");
    sym.polylines.push(EeSymbolPolyline { points, fill, stroke_width: at(&parts, 3).and_then(|s|s.parse().ok()).unwrap_or(1.0) });
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

fn parse_symbol_path(shape: &str, sym: &mut EeSymbol) {
    let parts: Vec<&str> = shape.split('~').collect();
    let Some(path) = parts.get(1) else {
        return;
    };
    let fill = !matches!(at(&parts, 5).unwrap_or("none"), "" | "none" | "None");
    if let Some(polylines) = symbol_path_polylines(path, fill) {
        let width=at(&parts,3).and_then(|s|s.parse().ok()).unwrap_or(1.0);
        sym.polylines.extend(polylines.into_iter().map(|mut line|{line.stroke_width=width;line}));
    } else {
        eprintln!("[easyeda2altium] warning: unsupported or invalid symbol PT path");
    }
}

fn symbol_path_polylines(path: &str, fill: bool) -> Option<Vec<EeSymbolPolyline>> {
    Some(super::geometry::path_polylines(path)?.into_iter()
        .map(|points| EeSymbolPolyline { points, fill, stroke_width: 1.0 }).collect())
}

fn parse_arc(shape: &str, sym: &mut EeSymbol) {
    // A~path~helper_dots~stroke~stroke_width~stroke_style~fill~id~locked
    let parts: Vec<&str> = shape.split('~').collect();
    if parts.len() < 2 {
        return;
    }
    sym.arcs.push(EeSymbolArc {
        stroke_width: at(&parts, 4).and_then(|s|s.parse().ok()).unwrap_or(1.0),
        raw_path: parts[1].to_string(),
    });
}

fn parse_text(shape: &str, sym: &mut EeSymbol) {
    // EasyEDA: mark=N/P/PK are component metadata, already emitted as AD labels.
    // Ordinary T~L text uses baseline at 10, visible at 13, anchor at 14.
    let parts: Vec<&str> = shape.split('~').collect();
    if parts.len() < 13 || parts[12].is_empty() || at(&parts, 1) != Some("L") {
        return;
    }
    let font_size_str = parts.get(7).copied().unwrap_or("7pt");
    let font_size_mm = font_size_str
        .trim_end_matches("pt")
        .parse::<f64>()
        .map(|v| v * 25.4 / 72.0)
        .unwrap_or(1.27);

    sym.texts.push(EeSymbolText {
        pos_x: parse_f(at(&parts, 2)),
        pos_y: parse_f(at(&parts, 3)),
        rotation_deg: parse_f(at(&parts, 4)),
        text: parts[12].to_string(),
        font_size_mm,
        font_family: at(&parts, 6).unwrap_or("").to_string(),
        bold: at(&parts, 8) == Some("bold"),
        italic: at(&parts, 9) == Some("italic"),
        baseline: at(&parts, 10).unwrap_or("").to_string(),
        anchor: at(&parts, 14).unwrap_or("start").to_string(),
        is_hidden: matches!(at(&parts, 13), Some("0" | "none" | "hide")),
        color: at(&parts, 5).and_then(|value| value.strip_prefix('#'))
            .filter(|value| value.len() == 6)
            .and_then(|value| u32::from_str_radix(value, 16).ok())
            .map(|rgb| (((rgb & 0xff) << 16) | (rgb & 0xff00) | ((rgb >> 16) & 0xff)) as i32)
            .unwrap_or(0x80),
    });
}

// ---------- footprint ----------

pub fn parse_footprint(api_result: &Value) -> Result<EeFootprint> {
    let pkg = api_result
        .get("packageDetail")
        .ok_or_else(|| Error::Parse("missing packageDetail".into()))?;
    let datastr = pkg
        .get("dataStr")
        .ok_or_else(|| Error::Parse("missing packageDetail.dataStr".into()))?;
    let head = datastr
        .get("head")
        .ok_or_else(|| Error::Parse("missing packageDetail.dataStr.head".into()))?;
    let c_para = head.get("c_para").cloned().unwrap_or(Value::Null);

    let assembly = api_result
        .get("customData")
        .and_then(|v| v.get("jlcPara"))
        .and_then(|v| v.get("assemblyProcess"))
        .and_then(|v| v.as_str())
        .unwrap_or("");
    let title = pkg
        .get("title")
        .and_then(|v| v.as_str())
        .unwrap_or("")
        .to_string();
    let is_smd = if !assembly.is_empty() {
        assembly.eq_ignore_ascii_case("SMT")
    } else {
        api_result.get("SMT").map(|v| v.as_bool().unwrap_or(false)).unwrap_or(false)
            && !title.contains("-TH_")
    };

    let info = EeFootprintInfo {
        name: safe_string(c_para.get("package").unwrap_or(&Value::Null)),
        fp_type: if is_smd { "smd".into() } else { "tht".into() },
        model_3d_uuid: safe_string(c_para.get("3DModel").unwrap_or(&Value::Null)),
        manufacturer: pick_str(&c_para, &["Manufacturer", "BOM_Manufacturer"]),
        mpn: pick_str(&c_para, &["Manufacturer Part", "BOM_Manufacturer Part"]),
        lcsc_id: safe_string(
            api_result
                .get("lcsc")
                .and_then(|v| v.get("number"))
                .unwrap_or(&Value::Null),
        ),
        description: safe_string(api_result.get("description").unwrap_or(&Value::Null)),
    };

    let bbox_x = safe_float(head.get("x").unwrap_or(&Value::Null));
    let bbox_y = safe_float(head.get("y").unwrap_or(&Value::Null));

    let mut fp = EeFootprint {
        info,
        bbox_x,
        bbox_y,
        ..Default::default()
    };

    // canvas origin override (canvas string positions 16,17)
    let mut canvas_ox = bbox_x;
    let mut canvas_oy = bbox_y;
    if let Some(c) = datastr.get("canvas").and_then(|v| v.as_str()) {
        let c_parts: Vec<&str> = c.split('~').collect();
        if c_parts.len() > 17 {
            canvas_ox = parse_f(c_parts.get(16).copied());
            canvas_oy = parse_f(c_parts.get(17).copied());
        }
    }

    if let Some(shapes) = datastr.get("shape").and_then(|v| v.as_array()) {
        for s in shapes {
            if let Some(text) = s.as_str() {
                parse_footprint_shape(text, &mut fp, canvas_ox, canvas_oy);
            }
        }
    }
    Ok(fp)
}

pub fn parse_footprint_shape(
    shape: &str,
    fp: &mut EeFootprint,
    canvas_ox: f64,
    canvas_oy: f64,
) {
    let parts: Vec<&str> = shape.split('~').collect();
    let designator = match parts.first() {
        Some(d) => d,
        None => return,
    };
    match *designator {
        "PAD" => {
            if parts.len() < 7 {
                return;
            }
            let is_plated = at(&parts, 15).map(|v| v != "N").unwrap_or(true);
            fp.pads.push(EeFootprintPad {
                shape: at(&parts, 1).unwrap_or("").to_string(),
                center_x: parse_f(at(&parts, 2)),
                center_y: parse_f(at(&parts, 3)),
                width: parse_f(at(&parts, 4)),
                height: parse_f(at(&parts, 5)),
                layer_id: parse_i(at(&parts, 6)),
                number: at(&parts, 8).unwrap_or("").to_string(),
                hole_radius: parse_f(at(&parts, 9)),
                points: at(&parts, 10).unwrap_or("").to_string(),
                rotation_deg: parse_f(at(&parts, 11)),
                hole_length: parse_f(at(&parts, 13)),
                hole_points: at(&parts, 14).unwrap_or("").to_string(),
                is_plated,
            });
        }
        "TRACK" => {
            if parts.len() < 5 {
                return;
            }
            fp.tracks.push(EeFootprintTrack {
                stroke_width: parse_f(at(&parts, 1)),
                layer_id: parse_i(at(&parts, 2)),
                points: at(&parts, 4).unwrap_or("").to_string(),
            });
        }
        "HOLE" => {
            if parts.len() < 4 {
                return;
            }
            fp.holes.push(EeFootprintHole {
                center_x: parse_f(at(&parts, 1)),
                center_y: parse_f(at(&parts, 2)),
                radius: parse_f(at(&parts, 3)),
            });
        }
        "VIA" => {
            if parts.len() < 6 {
                return;
            }
            fp.vias.push(EeFootprintVia {
                center_x: parse_f(at(&parts, 1)),
                center_y: parse_f(at(&parts, 2)),
                diameter: parse_f(at(&parts, 3)),
                drill_radius: parse_f(at(&parts, 5)),
            });
        }
        "CIRCLE" => {
            if parts.len() < 6 {
                return;
            }
            fp.circles.push(EeFootprintCircle {
                cx: parse_f(at(&parts, 1)),
                cy: parse_f(at(&parts, 2)),
                radius: parse_f(at(&parts, 3)),
                stroke_width: parse_f(at(&parts, 4)),
                layer_id: parse_i(at(&parts, 5)),
            });
        }
        "ARC" => {
            if parts.len() < 5 {
                return;
            }
            fp.arcs.push(EeFootprintArc {
                stroke_width: parse_f(at(&parts, 1)),
                layer_id: parse_i(at(&parts, 2)),
                raw_path: at(&parts, 4).unwrap_or("").to_string(),
            });
        }
        "RECT" => {
            if parts.len() < 9 {
                return;
            }
            fp.rectangles.push(EeFootprintRect {
                x: parse_f(at(&parts, 1)),
                y: parse_f(at(&parts, 2)),
                width: parse_f(at(&parts, 3)),
                height: parse_f(at(&parts, 4)),
                layer_id: parse_i(at(&parts, 5)),
                stroke_width: parse_f(at(&parts, 8)),
            });
        }
        "TEXT" => {
            if parts.len() < 11 {
                return;
            }
            let display = at(&parts, 11).map(|v| v != "0" && !v.is_empty()).unwrap_or(true);
            let font_size_str = at(&parts, 9).unwrap_or("5");
            let font_size = font_size_str
                .trim_end_matches("pt")
                .parse::<f64>()
                .unwrap_or(5.0);
            fp.texts.push(EeFootprintText {
                kind: at(&parts, 1).unwrap_or("").to_string(),
                center_x: parse_f(at(&parts, 2)),
                center_y: parse_f(at(&parts, 3)),
                stroke_width: parse_f(at(&parts, 4)),
                rotation_deg: parse_f(at(&parts, 5)),
                layer_id: parse_i(at(&parts, 7)),
                font_size,
                text: at(&parts, 10).unwrap_or("").to_string(),
                is_displayed: display,
            });
        }
        "SOLIDREGION" => {
            if parts.len() < 5 {
                return;
            }
            fp.solid_regions.push(EeFootprintSolidRegion {
                layer_id: parse_i(at(&parts, 1)),
                raw_path: at(&parts, 3).unwrap_or("").to_string(),
                region_type: at(&parts, 4).unwrap_or("solid").to_string(),
            });
        }
        "SVGNODE" => {
            if parts.len() >= 2 {
                if let Ok(v) = serde_json::from_str::<Value>(parts[1]) {
                    if let Some(model) = parse_svgnode_model(&v, canvas_ox, canvas_oy) { fp.model_3d = Some(model); }
                }
            }
        }
        _ => {}
    }
}

fn parse_svgnode_model(node: &Value, canvas_ox: f64, canvas_oy: f64) -> Option<Ee3dModel> {
    let attrs = node.get("attrs")?;
    let uuid = safe_string(attrs.get("uuid").unwrap_or(&Value::Null));
    if uuid.is_empty() {
        return None;
    }
    let title = safe_string(attrs.get("title").unwrap_or(&Value::Null));
    let scale = 0.254_f64;
    let c_origin: Vec<f64> = safe_string(attrs.get("c_origin")?)
        .split(',').map(|s| s.trim().parse::<f64>().ok())
        .collect::<Option<Vec<_>>>()?;
    if c_origin.len() != 2 || !c_origin.iter().all(|v| v.is_finite()) { return None; }
    let tx = (c_origin[0] - canvas_ox) * scale;
    let ty = -(c_origin[1] - canvas_oy) * scale;
    let z = attrs.get("z").map(safe_string).unwrap_or_default();
    let tz = if z.is_empty() { 0.0 } else { z.parse::<f64>().ok()? * scale };
    let rotation = attrs.get("c_rotation").map(safe_string).unwrap_or_default();
    let c_rot = if rotation.is_empty() { vec![0.0; 3] } else {
        rotation.split(',').map(|s| s.trim().parse::<f64>().ok()).collect::<Option<Vec<_>>>()?
    };
    if c_rot.len() != 3 || !c_rot.iter().all(|v| v.is_finite())
        || ![tx, ty, tz].iter().all(|v| v.is_finite()) { return None; }

    Some(Ee3dModel {
        uuid,
        name: title,
        translation_x_mm: tx,
        translation_y_mm: ty,
        translation_z_mm: tz,
        rotation_x_deg: c_rot.first().copied().unwrap_or(0.0),
        rotation_y_deg: c_rot.get(1).copied().unwrap_or(0.0),
        rotation_z_deg: c_rot.get(2).copied().unwrap_or(0.0),
        raw_step: None,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn pin_length_horizontal() {
        assert_eq!(derive_pin_length("M 400 300 h 20"), 20.0);
        assert_eq!(derive_pin_length("M 400 300 h -20"), 20.0);
        assert_eq!(derive_pin_length("M 0 0 v 10"), 10.0);
        assert_eq!(derive_pin_length("M360,270 h20"), 20.0);
        assert_eq!(derive_pin_length("M400,350 v-20"), 20.0);
    }

    #[test]
    fn compact_and_absolute_pin_paths_keep_exact_length() {
        for path in ["M315,165h10", "M485,165h-10", "M400,455v-10", "M400,145v10",
            "M315,165H325", "M315 165L325 165", "M315,165h5 5", "M3.15e2,165h1e1"] {
            assert_eq!(derive_pin_length(path), 10.0, "{path}");
        }
        assert_eq!(derive_pin_length("M0 0h0"), 0.0);
        for path in ["M0 0h", "M0 0h10 garbage", "M0 0L10 10", "M0 0h10h-5", "M0 0h10 M20 0h10"] {
            assert!(derive_pin_length(path).is_nan(), "must reject {path}");
        }
    }

    #[test]
    fn compact_esp32_pin_body_ends_at_outline() {
        let mut symbol = EeSymbol::default();
        parse_pin("P~show~0~1~315~165~180~p1~0^^315~165^^M315,165h10~#880000^^1~328~169~0~LNA_IN~start^^1~324~164~0~1~end^^0^^0", &mut symbol);
        let converted = crate::convert::symbol::convert_symbol(&symbol, &[], false);
        assert_eq!(symbol.pins[0].pin_length, 10.0);
        assert_eq!(converted.pins[0].length, altium::coord::Coord::from_mils(100.0));
        assert_eq!(converted.pins[0].location, altium::coord::CoordPoint::mils(3250.0,-1650.0));
    }

    #[test]
    fn capacitor_path_can_start_at_body_and_end_at_connection() {
        let mut symbol = EeSymbol::default();
        parse_pin("P~show~0~1~-20~0~180~p1~0^^-20~0^^M -10 0 h -10~#800^^0~-6~0~0~1~start^^0~-14~-4~0~1~end^^0^^0", &mut symbol);
        assert_eq!(symbol.pins[0].pin_length, 10.0);
        let converted = crate::convert::symbol::convert_symbol(&symbol, &[], false);
        assert_eq!(converted.pins[0].location, altium::coord::CoordPoint::mils(-100.0,0.0));
    }

    #[test]
    fn source_passive_pin_labels_stay_hidden_without_erasing_pin_identity() {
        let mut symbol=EeSymbol::default();
        parse_pin("P~show~0~1~0~0~180~p1~0^^0~0^^M0 0h10~#800^^0~10~0~0~1~start^^0~5~0~0~1~end^^0^^0",&mut symbol);
        let comp=crate::convert::symbol::convert_symbol(&symbol,&[],false);
        assert!(!comp.pins[0].show_name);assert!(!comp.pins[0].show_designator);
        assert_eq!(comp.pins[0].designator.as_deref(),Some("1"));assert!(!comp.pins[0].is_hidden);
    }


    #[test]
    fn legacy_irf3710_snap_points_preserve_connections_and_body_ends() {
        let mut symbol = EeSymbol::default();
        parse_pin("P~show~0~1~-20~10~180~gge38~0^^-30~10^^M -20 10 h -10~#800^^0~-6~10~0~G~start~~~#800^^0~-14~6~0~1~end~~~#800^^0~-33~10^^0~M -30 13 L -27 10 L -30 7", &mut symbol);
        parse_pin("P~show~0~3~0~10~270~gge52~0^^0~20^^M 0 10 v 10~#800^^0~0~36~270~S~start~~~#800^^0~-4~44~270~3~end~~~#800^^0~0~23^^0~M 3 20 L 0 17 L -3 20", &mut symbol);
        parse_pin("P~show~0~2~0~-10~90~gge59~0^^0~-20^^M 0 -10 v -10~#800^^0~0~2~270~D~end~~~#800^^0~-4~-4~270~2~start~~~#800^^0~0~-23^^0~M -3 -20 L 0 -17 L 3 -20", &mut symbol);
        assert_eq!(symbol.pins.iter().map(|p|(p.pos_x,p.pos_y,p.pin_length)).collect::<Vec<_>>(),
            vec![(-30.0,10.0,10.0),(0.0,20.0,10.0),(0.0,-20.0,10.0)]);
        let comp=crate::convert::symbol::convert_symbol(&symbol,&[],false);
        use altium::coord::CoordPoint;
        assert_eq!(comp.pins[0].location,CoordPoint::mils(-200.0,-100.0));
        assert_eq!(comp.pins[1].location,CoordPoint::mils(0.0,-100.0));
        assert_eq!(comp.pins[2].location,CoordPoint::mils(0.0,100.0));
        assert!(comp.pins.iter().all(|p| p.length==altium::coord::Coord::from_mils(100.0)));
    }
    #[test]
    fn inconsistent_snap_point_is_still_rejected() {
        let mut symbol=EeSymbol::default();
        parse_pin("P~show~0~1~-20~10~180~p1~0^^-40~10^^M -20 10 h -10~#800^^0~-6~10~0~G~start^^0~-14~6~0~1~end^^0^^0",&mut symbol);
        assert!(symbol.pins[0].pin_length.is_nan());
    }

    #[test]
    fn split_point_pairs() {
        let p = parse_point_pairs("0 0 5 5 -3 -3");
        assert_eq!(p, vec![(0.0, 0.0), (5.0, 5.0), (-3.0, -3.0)]);
    }

    #[test]
    fn led_path_preserves_body_and_arrowheads() {
        let mut symbol = EeSymbol::default();
        symbol.info.prefix = "LED?".to_string();
        for shape in [
            "PT~M 384 277 L 388 279 L 386 281 Z ~#880000~1~0~none~gge38~0~",
            "PT~M 388 273 L 392 275 L 390 277 Z ~#880000~1~0~none~gge39~0~",
            "PT~M 405 283 L 395 290 L 405 297 Z ~#880000~1~0~none~gge49~0~",
        ] {
            parse_symbol_shape(shape, &mut symbol);
        }
        assert_eq!(symbol.polylines.len(), 3);
        assert_eq!(symbol.polylines[2].points, vec![
            (405.0, 283.0), (395.0, 290.0), (405.0, 297.0), (405.0, 283.0),
        ]);
        let converted = crate::convert::symbol::convert_symbol(&symbol, &[], false);
        assert_eq!(converted.polygons.len(), 3);
        assert!(converted.polygons.iter().all(|polygon| polygon.vertices.len() == 3));
        assert!(converted.polygons.iter().all(|polygon| !polygon.is_filled));
    }

    #[test]
    fn symbol_path_handles_compact_relative_and_separate_subpaths() {
        let paths = symbol_path_polylines("M1e2,20l10-5h5v10z m20,0 5,5", false).unwrap();
        assert_eq!(paths.len(), 2);
        assert_eq!(paths[0].points, vec![
            (100.0, 20.0), (110.0, 15.0), (115.0, 15.0), (115.0, 25.0), (100.0, 20.0),
        ]);
        assert_eq!(paths[1].points, vec![(120.0, 20.0), (125.0, 25.0)]);
        assert!(!paths[1].fill);
    }

    #[test]
    fn unsupported_or_incomplete_symbol_paths_do_not_emit_partial_geometry() {
        for path in ["M0 0 L10", "M0 0 L10 10 C20 20 30 30", "M0 0 @", "M0 0 Z 10 20"] {
            assert!(symbol_path_polylines(path, false).is_none());
        }
    }

    #[test]
    fn sanitize_strips_packaging_suffix() {
        assert_eq!(sanitize_component_name("ATmega328P (TQFP)"), "ATmega328P");
        assert_eq!(sanitize_component_name("BC547[CutTape]"), "BC547");
        assert_eq!(sanitize_component_name("LM358"), "LM358");
    }
}
