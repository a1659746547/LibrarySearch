use altium::pcb::Library;

use crate::easyeda::types::Ee3dModel;
use crate::error::{Error, Result};

use super::footprint::make_model_entry;

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

