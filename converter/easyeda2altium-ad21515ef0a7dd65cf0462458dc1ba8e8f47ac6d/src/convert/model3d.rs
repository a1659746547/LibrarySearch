use altium::pcb::Library;
use std::collections::HashMap;

use crate::easyeda::types::Ee3dModel;
use crate::error::{Error, Result};

use super::footprint::make_model_entry;

/// Correct the placement only when an independently supplied outline agrees
/// with the dimensions of the STEP model. Its origin need not be its center.
/// Leave rotations, Z and ambiguous models unchanged.
pub fn align_xy_to_projection(model: &mut Ee3dModel, step: &[u8]) -> bool {
    let Some(target) = model.projection_xy_mm else { return false };
    if model.rotation_x_deg.rem_euclid(360.0) != 0.0
        || model.rotation_y_deg.rem_euclid(360.0) != 0.0
        || model.rotation_z_deg.rem_euclid(90.0) != 0.0
    {
        return false;
    }
    let Some(bounds) = step_projection(step, model.rotation_z_deg) else { return false };
    let mut shift = [0.0; 2];
    for i in 0..2 {
        let size = target[1][i] - target[0][i];
        let model_size = bounds[1][i] - bounds[0][i];
        if !size.is_finite() || size < 0.05
            || (size - model_size).abs() > (size * 0.025).min(0.05)
        {
            return false;
        }
        let center_delta = (target[0][i] + target[1][i] - bounds[0][i] - bounds[1][i]) / 2.0;
        if !center_delta.is_finite() || center_delta.abs() > 5000.0 {
            return false;
        }
        shift[i] = if center_delta.abs() < 0.001 { 0.0 } else { center_delta };
    }
    if (shift[0] - model.translation_x_mm).hypot(shift[1] - model.translation_y_mm) < 0.03 {
        return false;
    }
    [model.translation_x_mm, model.translation_y_mm] = shift;
    true
}

// STEP strings/comments may include semicolons or entity names. Do not
// mistake them for instance boundaries.
fn step_records(text: &str) -> impl Iterator<Item = &str> {
    let bytes = text.as_bytes();
    let mut start = 0;
    let mut at = 0;
    std::iter::from_fn(move || {
        let mut quoted = false;
        while at < bytes.len() {
            match bytes[at] {
                b'\'' if quoted && bytes.get(at + 1) == Some(&b'\'') => at += 1,
                b'\'' => quoted = !quoted,
                b'/' if !quoted && bytes.get(at + 1) == Some(&b'*') => {
                    let end = bytes[at + 2..].windows(2).position(|v| v == b"*/")?;
                    at += end + 3;
                    if text[start..at + 1].trim_start().starts_with("/*") {
                        start = at + 1;
                    }
                }
                b';' if !quoted => {
                    let record = &text[start..at];
                    at += 1;
                    start = at;
                    return Some(record.trim());
                }
                _ => {}
            }
            at += 1;
        }
        None
    })
}

fn step_projection(step: &[u8], rotation_z: f64) -> Option<[[f64; 2]; 2]> {
    // Deliberately limited to flat, millimeter B-reps with a single shape
    // representation. Assembly transforms and non-mm files need a CAD reader.
    if step.len() > 16 * 1024 * 1024 {
        return None;
    }
    let text = std::str::from_utf8(step).ok()?;
    let mut cartesian = HashMap::new();
    let mut vertices = Vec::new();
    let mut representations = 0;
    let mut mm = false;
    let mut sections = 0;
    let mut in_data = false;
    for record in step_records(text) {
        if record == "DATA" {
            sections += 1;
            in_data = true;
        } else if record == "ENDSEC" {
            in_data = false;
        }
        if !in_data || !record.starts_with('#') {
            continue;
        }
        let (id, entity) = record[1..].split_once('=')?;
        let id = id.trim().parse::<u32>().ok()?;
        let entity = entity.trim();
        let (kind, args) = entity.split_once('(')?;
        match kind.trim() {
            "ADVANCED_BREP_SHAPE_REPRESENTATION" => representations += 1,
            "MAPPED_ITEM" | "REPRESENTATION_MAP" | "ITEM_DEFINED_TRANSFORMATION"
            | "CONTEXT_DEPENDENT_SHAPE_REPRESENTATION" => return None,
            "CARTESIAN_POINT" => {
                let (_, coordinates) = args.rsplit_once('(')?;
                let (coordinates, _) = coordinates.split_once(')')?;
                let mut coordinates = coordinates.split(',');
                let point = [
                    coordinates.next()?.trim().parse::<f64>().ok()?,
                    coordinates.next()?.trim().parse::<f64>().ok()?,
                ];
                // 2D trim curves also contain Cartesian points. Only 3D
                // points can be used by the solid's vertices below.
                let Some(z) = coordinates.next() else { continue };
                if !point.iter().all(|v| v.is_finite()) || !z.trim().parse::<f64>().ok()?.is_finite()
                    || coordinates.next().is_some() || cartesian.insert(id, point).is_some()
                {
                    return None;
                }
            }
            "VERTEX_POINT" => {
                let (_, point) = args.rsplit_once('#')?;
                vertices.push(point.trim().strip_suffix(')')?.trim().parse::<u32>().ok()?);
            }
            "" if args.contains("LENGTH_UNIT") => {
                let compact = args.replace(char::is_whitespace, "");
                if !compact.contains("SI_UNIT(.MILLI.,.METRE.)") || compact.contains("CONVERSION_BASED_UNIT") {
                    return None;
                }
                mm = true;
            }
            "" if args.contains("REPRESENTATION_RELATIONSHIP_WITH_TRANSFORMATION") => return None,
            _ => {}
        }
    }
    if !mm || sections != 1 || representations != 1 || vertices.len() < 3 {
        return None;
    }
    let (s, c) = rotation_z.rem_euclid(360.0).to_radians().sin_cos();
    let mut min = [f64::INFINITY; 2];
    let mut max = [f64::NEG_INFINITY; 2];
    for vertex in vertices {
        let &[x, y] = cartesian.get(&vertex)?;
        let point = [c * x - s * y, s * x + c * y];
        for i in 0..2 {
            min[i] = min[i].min(point[i]);
            max[i] = max[i].max(point[i]);
        }
    }
    Some([min, max])
}

pub fn attach_3d_model(
    library: &mut Library,
    ee_model: &Ee3dModel,
    raw_step: Vec<u8>,
) -> Result<()> {
    let raw_step=normalize_step(&raw_step)?;
    validate_step(&raw_step)?;
    if library.models.iter().any(|m| m.id == ee_model.uuid) {
        return Ok(());
    }
    library.models.push(make_model_entry(ee_model, raw_step));
    Ok(())
}

/// Reject empty/error-page/truncated responses before embedding them as STEP.
/// This checks the exchange-file envelope, not CAD geometry validity.
pub fn validate_step(bytes: &[u8]) -> Result<()> {
    if bytes.is_empty() || bytes.len() > 64 * 1024 * 1024 {
        return Err(Error::Convert("STEP is empty or exceeds 64 MiB".into()));
    }
    let text = std::str::from_utf8(bytes)
        .map_err(|_| Error::Convert("STEP is not a UTF-8/ASCII exchange file".into()))?;
    let text = text.trim_start_matches('\u{feff}').trim();
    if !text.starts_with("ISO-10303-21;") || !text.contains("HEADER;")
        || !text.contains("DATA;") || !text.ends_with("END-ISO-10303-21;") {
        return Err(Error::Convert("response is not a complete STEP exchange file".into()));
    }
    Ok(())
}

/// Normalize legacy Chinese STEP strings to portable ISO-10303 Unicode escapes.
/// Numeric/entity geometry is unchanged. Non-ASCII identifiers are rejected.
pub fn normalize_step(bytes: &[u8]) -> Result<Vec<u8>> {
    const LIMIT: usize = 64 * 1024 * 1024;
    if bytes.is_empty() || bytes.len() > LIMIT { return Err(Error::Convert("STEP is empty or exceeds 64 MiB".into())); }
    let decoded = match std::str::from_utf8(bytes) {
        Ok(text) => std::borrow::Cow::Borrowed(text),
        Err(_) => {
            let (text, errors) = encoding_rs::GBK.decode_without_bom_handling(bytes);
            if errors { return Err(Error::Convert("STEP text is neither UTF-8 nor valid GBK".into())); }
            text
        }
    };
    let text = decoded.trim_start_matches('\u{feff}').trim();
    if !text.starts_with("ISO-10303-21;") || !text.contains("HEADER;") || !text.contains("DATA;") || !text.ends_with("END-ISO-10303-21;") {
        return Err(Error::Convert("response is not a complete STEP exchange file".into()));
    }
    let mut result=String::with_capacity(text.len());
    let mut chars=text.chars().peekable(); let mut quoted=false; let mut comment=false;
    while let Some(ch)=chars.next() {
        if !quoted && !comment && ch=='/' && chars.peek()==Some(&'*') { result.push_str("/*");chars.next();comment=true;continue; }
        if comment && ch=='*' && chars.peek()==Some(&'/') { result.push_str("*/");chars.next();comment=false;continue; }
        if !comment && ch=='\'' {
            result.push(ch);
            if quoted && chars.peek()==Some(&'\'') { result.push('\'');chars.next();continue; }
            quoted=!quoted;continue;
        }
        if ch.is_ascii() { result.push(ch); }
        else {
            if !quoted && !comment { return Err(Error::Convert("non-ASCII STEP identifier outside a string/comment".into())); }
            result.push_str("\\X2\\");
            let mut units=[0u16;2];
            for unit in ch.encode_utf16(&mut units).iter() { use std::fmt::Write; write!(result,"{unit:04X}").unwrap(); }
            result.push_str("\\X0\\");
        }
        if result.len()>LIMIT { return Err(Error::Convert("normalized STEP exceeds 64 MiB".into())); }
    }
    if quoted || comment { return Err(Error::Convert("unterminated STEP string/comment".into())); }
    Ok(result.into_bytes())
}

#[cfg(test)]
mod encoding_tests {
    use super::*;
    #[test]
    fn legacy_gbk_step_strings_are_escaped_without_changing_geometry() {
        let source="ISO-10303-21;\nHEADER;ENDSEC;DATA;#1=NAME('任何');#2=POINT('',(1.0,-2.0,3.0));ENDSEC;END-ISO-10303-21;";
        let (gbk,_,errors)=encoding_rs::GBK.encode(source);assert!(!errors);
        let normalized=normalize_step(&gbk).unwrap();let text=String::from_utf8(normalized.clone()).unwrap();
        assert!(text.contains("\\X2\\4EFB\\X0\\\\X2\\4F55\\X0\\"));
        assert!(text.contains("#2=POINT('',(1.0,-2.0,3.0));"));assert!(normalized.is_ascii());
        assert_eq!(normalize_step(&normalized).unwrap(),normalized);
    }
    #[test]
    fn step_html_and_truncation_are_rejected() {
        for bytes in [b"<html>Error</html>".as_slice(),b"ISO-10303-21;HEADER;DATA;".as_slice()] { assert!(normalize_step(bytes).is_err()); }
        let source="ISO-10303-21;HEADER;ENDSEC;DATA;#1=NAME('a''任');ENDSEC;END-ISO-10303-21;";
        assert!(String::from_utf8(normalize_step(source.as_bytes()).unwrap()).unwrap().contains("'a''\\X2\\4EFB\\X0\\'"));
    }
}

