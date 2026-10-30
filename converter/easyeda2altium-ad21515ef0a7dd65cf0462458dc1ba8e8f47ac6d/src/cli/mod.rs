pub mod args;

use std::path::{Path, PathBuf};

use clap::Parser;

use altium::{pcb, sch};

use crate::convert::{attach_3d_model, convert_footprint, convert_symbol};
use crate::easyeda::EasyedaApi;
use crate::easyeda::parser::{parse_footprint, parse_symbol};
use crate::error::{Error, Result};

pub use args::Args;

pub async fn run() -> Result<()> {
    let args = Args::parse();
    args.validate()?;

    if let Some(request)=&args.search_index {
        let output=args.output.as_deref().ok_or_else(||Error::InvalidArg("--search-index requires --output".into()))?;
        crate::local_catalog::run_search(Path::new(request),Path::new(output))?;
        return Ok(());
    }

    if let Some(request) = &args.index_request {
        let output=args.output.as_deref().ok_or_else(||Error::InvalidArg("--index-request requires --output".into()))?;
        crate::local_index::run(Path::new(request),Path::new(output)).await?;
        return Ok(());
    }

    if let Some(code)=&args.describe {
        if !code.starts_with('C') || code.len()<2 || !code[1..].bytes().all(|b|b.is_ascii_digit()) {return Err(Error::InvalidArg("invalid description component code".into()));}
        let output=args.output.as_deref().ok_or_else(||Error::InvalidArg("--describe requires --output".into()))?;
        let api=EasyedaApi::with_options(args.use_cache,args.cache_dir.as_deref(),args.refresh_source)?
            .with_input_options(None,args.refresh_description);
        let description=api.get_product_description(code).await.unwrap_or_default();
        std::fs::write(output,serde_json::to_vec(&serde_json::json!({"Description":description,"DescriptionVersion":"product-cn-1","DescriptionSource":api.product_description_source(code).unwrap_or_default()}))?)?;
        return Ok(());
    }
    let mut want_symbol = args.symbol;
    let mut want_footprint = args.footprint;
    let mut want_3d = args.three_d;
    if args.full {
        want_symbol = true;
        want_footprint = true;
        want_3d = true;
    }
    if !want_symbol && !want_footprint && !want_3d {
        return Err(Error::InvalidArg(
            "specify at least one of --symbol / --footprint / --3d / --full".into(),
        ));
    }

    let appearance = args.appearance.as_deref().map(crate::convert::appearance::Appearance::load).transpose()?;
    let custom_fields = args.parsed_custom_fields()?;
    let api = EasyedaApi::with_options(args.use_cache,args.cache_dir.as_deref(),args.refresh_source)?
        .with_input_options(args.cad_source.as_deref(),args.refresh_description);

    let output = args.output.as_deref().map(PathBuf::from).unwrap_or_else(|| {
        let dir = dirs_home()
            .unwrap_or_else(|| PathBuf::from("."))
            .join("Documents/easyeda2altium");
        std::fs::create_dir_all(&dir).ok();
        dir.join("easyeda2altium")
    });

    let schlib_path = output.with_extension("SchLib");
    let pcblib_path = output.with_extension("PcbLib");

    if want_symbol {
        ensure_overwrite_ok(&schlib_path, args.overwrite)?;
    }
    if want_footprint || want_3d {
        ensure_overwrite_ok(&pcblib_path, args.overwrite)?;
    }

    let mut sch_library = sch::Library::default();
    let mut pcb_library = pcb::Library::default();
    populate_pcb_library_defaults(&mut pcb_library);

    for lcsc_id in &args.lcsc_id {
        eprintln!("[easyeda2altium] fetching {}", lcsc_id);
        let cad=api.get_cad_data(lcsc_id).await?;
        let product_description=api.cached_product_description(lcsc_id);
        let returned_id = cad.get("lcsc").and_then(|value| value.get("number")).and_then(|value| value.as_str()).unwrap_or_default();
        if !returned_id.eq_ignore_ascii_case(lcsc_id) {
            return Err(Error::Api(format!("requested {}, response identified as {:?}; refusing to substitute a different or unidentified component", lcsc_id, returned_id)));
        }

        if want_symbol {
            let mut ee = parse_symbol(&cad)?;
            if args.strip_chinese {
                strip_chinese_symbol(&mut ee);
            }
            let mut description=ee.info.description.clone();
            let mut description_source="https://easyeda.com/".to_string();
            if let Some(text)=&product_description {description=text.clone();description_source=api.product_description_source(lcsc_id).unwrap_or_default();}
            else if description.trim().is_empty() {
                if let Some(tags)=cad.get("tags").and_then(serde_json::Value::as_array) {
                    let tags:Vec<_>=tags.iter().filter_map(serde_json::Value::as_str).collect();
                    if !tags.is_empty(){description=format!("分类：{}",tags.join(" / "));}
                }
            }
            let metadata = serde_json::json!({"Description": description.chars().take(4000).collect::<String>(),"DescriptionSource":description_source,"DescriptionVersion":if product_description.is_some(){"product-cn-1"}else{"cad"},
                "PackageName": ee.info.package, "ManufacturerPart": ee.info.mpn});
            std::fs::write(output.with_extension("metadata.json"), serde_json::to_vec(&metadata)?)?;
            let use_custom_pin_font = args.font.is_some();
            let mut component = convert_symbol(&ee, &custom_fields, use_custom_pin_font);
            crate::convert::symbol::register_symbol_text_fonts(&mut sch_library, &ee, &mut component, args.font.as_deref());
            if let Some(style) = &appearance { style.apply(&mut sch_library, &ee, &mut component)?; eprintln!("  appearance: applied"); }
            sch_library.components.push(component);
            eprintln!("  symbol: {} ({} pins)", ee.info.name, ee.pins.len());
        }

        if want_footprint || want_3d {
            let mut ee_fp = parse_footprint(&cad)?;
            if args.strip_chinese {
                strip_chinese_footprint(&mut ee_fp);
            }

            // Remove the body unless a usable embedded resource was obtained.
            // Optional model failures must not discard the symbol/footprint pair.
            let source_model = ee_fp.model_3d.take();
            let mut model_status = if source_model.is_some() { "available" } else { "unavailable" };
            if want_3d || args.three_d_cached {
                if let Some(model) = source_model {
                    let step = if want_3d {api.get_step(&model.uuid).await} else {Ok(api.cached_step(&model.uuid))};
                    match step {
                        Ok(Some(step_bytes)) => {
                            match attach_3d_model(&mut pcb_library, &model, step_bytes) {
                                Ok(()) => { ee_fp.model_3d = Some(model); model_status = "embedded"; }
                                Err(error) => {
                                    model_status = "invalid";
                                    eprintln!("  model3d-detail: {}", error);
                                }
                            }
                        }
                        Ok(None) => {if want_3d {model_status="unavailable";}}
                        Err(error) => {
                            model_status = "download-failed";
                            eprintln!("  model3d-detail: {}", error);
                        }
                    }
                }
            }
            eprintln!("  model3d: {}", model_status);
            let component = convert_footprint(&ee_fp);
            pcb_library.components.push(component);
            eprintln!(
                "  footprint: {} ({} pads)",
                ee_fp.info.name,
                ee_fp.pads.len()
            );
        }
    }

    if want_symbol {
        sch_library.font_override = args.font.clone();
        sch_library.write(&schlib_path).await?;
        eprintln!("[easyeda2altium] wrote {}", schlib_path.display());
    }
    if want_footprint || want_3d {
        if let Some(font) = args.font.as_deref() {
            pcb_library.set_text_font(font);
        }
        pcb_library.write(&pcblib_path).await?;
        eprintln!("[easyeda2altium] wrote {}", pcblib_path.display());
    }
    Ok(())
}

fn ensure_overwrite_ok(path: &Path, overwrite: bool) -> Result<()> {
    if path.exists() && !overwrite {
        return Err(Error::AlreadyExists(path.display().to_string()));
    }
    Ok(())
}

fn dirs_home() -> Option<PathBuf> {
    std::env::var_os("HOME").map(PathBuf::from)
}

/// Strip CJK ideographs (and any wrapping ASCII parentheses + adjacent
/// whitespace) from a string. Mirrors the regex `\(?[CJK]+\)?` used by
/// `--strip-chinese`. Whitespace runs left over after the strip are
/// collapsed and the result is trimmed.
///
/// Examples:
/// - `Raspberry Pi(树莓派)` → `Raspberry Pi`
/// - `树莓派 RP2040` → `RP2040`
/// - `Foo (bar)` → `Foo (bar)` (parens preserved when contents aren't CJK)
fn strip_cjk(input: &str) -> String {
    if input.is_empty() {
        return String::new();
    }
    let chars: Vec<char> = input.chars().collect();
    let mut out = String::with_capacity(input.len());
    let mut i = 0;
    while i < chars.len() {
        let c = chars[i];
        if c == '(' || c == '（' {
            let mut j = i + 1;
            let mut saw_cjk = false;
            while j < chars.len() && is_cjk(chars[j]) {
                saw_cjk = true;
                j += 1;
            }
            if saw_cjk && j < chars.len() && (chars[j] == ')' || chars[j] == '）') {
                i = j + 1;
                continue;
            }
        }
        if is_cjk(c) {
            while i < chars.len() && is_cjk(chars[i]) {
                i += 1;
            }
            continue;
        }
        out.push(c);
        i += 1;
    }
    let mut collapsed = String::with_capacity(out.len());
    let mut prev_ws = false;
    for c in out.chars() {
        if c.is_whitespace() {
            if !prev_ws {
                collapsed.push(' ');
            }
            prev_ws = true;
        } else {
            collapsed.push(c);
            prev_ws = false;
        }
    }
    collapsed.trim().to_string()
}

fn is_cjk(c: char) -> bool {
    let u = c as u32;
    (0x3400..=0x4DBF).contains(&u) || (0x4E00..=0x9FFF).contains(&u)
}

fn strip_chinese_symbol(ee: &mut crate::easyeda::types::EeSymbol) {
    strip_chinese_symbol_info(&mut ee.info);
    for sub in &mut ee.sub_symbols {
        strip_chinese_symbol_info(&mut sub.info);
    }
}

fn strip_chinese_symbol_info(info: &mut crate::easyeda::types::EeSymbolInfo) {
    info.name = strip_cjk(&info.name);
    info.manufacturer = strip_cjk(&info.manufacturer);
    info.mpn = strip_cjk(&info.mpn);
    info.description = strip_cjk(&info.description);
    info.keywords = strip_cjk(&info.keywords);
    info.package = strip_cjk(&info.package);
}

fn strip_chinese_footprint(fp: &mut crate::easyeda::types::EeFootprint) {
    fp.info.name = strip_cjk(&fp.info.name);
    fp.info.manufacturer = strip_cjk(&fp.info.manufacturer);
    fp.info.mpn = strip_cjk(&fp.info.mpn);
    fp.info.description = strip_cjk(&fp.info.description);
}


fn populate_pcb_library_defaults(library: &mut pcb::Library) {
    if library.unique_id.is_empty() {
        library.unique_id = "AAAAAAAA".to_string();
    }
}


