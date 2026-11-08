//! Dedicated component layer pair for generated online libraries.
//! Layer kinds allow AD to map this pair to a PCB's own numbering scheme.
use std::collections::BTreeMap;
use altium::pcb::{Library, primitives::ComponentBody};

pub const TOP: i32 = 62; // Mechanical 6, within the legacy 16-layer range.
pub const BOTTOM: i32 = 63; // Mechanical 7.

pub fn set_body_layer(body: &mut ComponentBody, bottom: bool) {
    body.layer = if bottom { BOTTOM } else { TOP };
    body.layer_name = format!("MECHANICAL{}", body.layer - 56);
    // A carried parameter must not override the new layer during serialization.
    if let Some(extra) = &mut body.additional_parameters {
        extra.retain(|key, _| !key.eq_ignore_ascii_case("V7_LAYER"));
    }
}

pub fn is_bottom(body: &ComponentBody, params: Option<&BTreeMap<String, String>>) -> bool {
    let Some(params) = params else { return false; };
    let get = |key: &str| params.iter().find(|(k, _)| k.eq_ignore_ascii_case(key)).map(|(_, v)| v.as_str());
    if let Some(kind) = get(&format!("LAYER{}MECHKIND", body.layer)) {
        return kind.eq_ignore_ascii_case("3DBodyBottom");
    }
    let id = 0x0102_0000u32 + (body.layer - 56) as u32;
    for (key, value) in params {
        if key.starts_with("V9_CACHE_LAYER") && key.ends_with("_LAYERID") && value.parse::<u32>().ok() == Some(id) {
            if let Some(kind) = get(&key.replace("_LAYERID", "_MECHKIND")) {
                return kind.eq_ignore_ascii_case("3DBodyBottom");
            }
        }
    }
    params.iter().any(|(key, value)| key.starts_with("MECHPAIR") && key.ends_with("L2") && value.eq_ignore_ascii_case(&body.layer_name))
}

pub fn configure(library: &mut Library) {
    // Only used for newly authored LibrarySearch libraries, never user PCBs.
    let params = library.library_parameters.get_or_insert_with(BTreeMap::new);
    for (offset, number, name, kind) in [(0, 6, "Top 3D Body", "3DBodyTop"), (1, 7, "Bottom 3D Body", "3DBodyBottom")] {
        let id = (0x0102_0000u32 + number).to_string();
        let guid = format!("{{4C535243-3344-4000-8000-{:012}}}", number);
        for (prefix, separator) in [(format!("V9_CACHE_LAYER{}", 12 + offset), "_"), (format!("LAYER_V8_{}", 10 + number), "")] {
            for (field, value) in [("ID", guid.as_str()), ("NAME", name), ("LAYERID", id.as_str()), ("MECHENABLED", "TRUE"), ("MECHKIND", kind), ("USEDBYPRIMS", if offset == 0 {"TRUE"} else {"FALSE"})] {
                params.insert(format!("{prefix}{separator}{field}"), value.into());
            }
        }
        for (field, value) in [("NAME", name), ("MECHENABLED", "TRUE"), ("MECHKIND", kind), ("PREV", "0"), ("NEXT", "0")] {
            params.insert(format!("LAYER{}{field}", number + 56), value.into());
        }
    }
    params.insert("MECHPAIR0L1".into(), "MECHANICAL6".into());
    params.insert("MECHPAIR0L2".into(), "MECHANICAL7".into());
}
