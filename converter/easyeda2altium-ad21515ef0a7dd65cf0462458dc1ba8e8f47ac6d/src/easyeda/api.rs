//! EasyEDA API client. Async via reqwest. Optional disk cache.

use std::path::{Path, PathBuf};

use reqwest::Client;
use serde_json::Value;

use crate::error::{Error, Result};

const API_ENDPOINT: &str = "https://easyeda.com/api/products/{lcsc_id}/components";
const ENDPOINT_3D_OBJ: &str = "https://modules.easyeda.com/3dmodel/{uuid}";
const ENDPOINT_3D_STEP: &str = "https://modules.easyeda.com/qAxj6KHrDKw4blvCG8QJPs7Y/{uuid}";

const USER_AGENT: &str =
    "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

#[derive(Debug, Clone)]
pub struct EasyedaApi {
    client: Client,
    cache_dir: Option<PathBuf>,
    refresh_source: bool,
    cad_source: Option<PathBuf>,
    refresh_description: bool,
}

impl EasyedaApi {
    pub fn new(use_cache: bool) -> Result<Self> {Self::with_options(use_cache,None,false)}
    pub fn with_options(use_cache: bool, directory: Option<&str>, refresh_source: bool) -> Result<Self> {
        let _ = rustls::crypto::ring::default_provider().install_default();
        let client = Client::builder()
            .user_agent(USER_AGENT)
            .gzip(true)
            .connect_timeout(std::time::Duration::from_secs(10))
            .timeout(std::time::Duration::from_secs(30))
            .build()?;
        let cache_dir = if use_cache || directory.is_some() {
            let dir = directory.map(PathBuf::from).unwrap_or_else(||std::env::current_dir()
                .unwrap_or_else(|_| PathBuf::from(".")).join(".easyeda_cache"));
            std::fs::create_dir_all(&dir).ok();
            Some(dir)
        } else {
            None
        };
        Ok(Self { client, cache_dir, refresh_source, cad_source: None, refresh_description: false })
    }

    pub fn with_input_options(mut self, cad_source: Option<&str>, refresh_description: bool) -> Self {
        self.cad_source = cad_source.map(PathBuf::from);
        self.refresh_description = refresh_description;
        self
    }

    fn cache_path(&self, identifier: &str, ext: &str) -> Option<PathBuf> {
        let dir = self.cache_dir.as_ref()?;
        let safe = identifier.replace(['/', '\\'], "_");
        Some(dir.join(format!("{}.{}", safe, ext)))
    }

    fn read_text_cache(&self, p: &Path) -> Option<String> {
        std::fs::read_to_string(p).ok()
    }

    fn read_bytes_cache(&self, p: &Path) -> Option<Vec<u8>> {
        std::fs::read(p).ok()
    }

    fn write_text_cache(p: &Path, contents: &str) {
        if let Some(parent) = p.parent() {
            std::fs::create_dir_all(parent).ok();
        }
        let _ = std::fs::write(p, contents);
    }

    fn write_bytes_cache(p: &Path, bytes: &[u8]) {
        if let Some(parent) = p.parent() {
            std::fs::create_dir_all(parent).ok();
        }
        let _ = std::fs::write(p, bytes);
    }

    fn fresh(&self, path: &Path) -> bool {
        !self.refresh_source && std::fs::metadata(path).ok().and_then(|m|m.modified().ok())
            .and_then(|time|time.elapsed().ok()).is_some_and(|age|age.as_secs()<24*60*60)
    }

    pub fn cached_step(&self, uuid: &str) -> Option<Vec<u8>> {
        let compact: String=uuid.chars().filter(|c|*c!='-').collect();
        if compact.len()!=32 || !compact.bytes().all(|c|c.is_ascii_hexdigit()) {return None;}
        let bytes=self.read_bytes_cache(&self.cache_path(uuid,"step")?)?;
        crate::convert::model3d::normalize_step(&bytes).ok()
    }

    pub fn cached_product_description(&self, code: &str) -> Option<String> {
        if self.refresh_description {return None;}
        let path=self.cache_path(code,"description.json")?;
        if !self.fresh(&path) {return None;}
        serde_json::from_str::<Value>(&self.read_text_cache(&path)?).ok()?
            .as_object().and_then(|v|if v.get("Version").and_then(Value::as_str)==Some("product-cn-1"){v.get("Description").and_then(Value::as_str)}else{None}).filter(|s|!s.is_empty()).map(String::from)
    }

    pub fn product_description_source(&self,code:&str)->Option<String>{
        let path=self.cache_path(code,"description.json")?;
        serde_json::from_str::<Value>(&self.read_text_cache(&path)?).ok()?.get("DescriptionSource").and_then(Value::as_str).map(String::from)
    }
    pub async fn get_product_description(&self, code: &str) -> Option<String> {
        let timer=std::time::Instant::now();
        if let Some(path)=self.cache_path(code,"description.json") {
            if !self.refresh_description && self.fresh(&path) {
                if let Some(text)=self.read_text_cache(&path) {
                    if let Ok(value)=serde_json::from_str::<Value>(&text) {
                        eprintln!("  timing: description cache {}ms",timer.elapsed().as_millis());
                        if value.get("Version").and_then(Value::as_str)==Some("product-cn-1") {if let Some(text)=value.get("Description").and_then(Value::as_str).filter(|s|!s.is_empty()){return Some(text.to_string());}}
                    }
                }
            }
        }
        let cad=self.get_cad_data(code).await.ok();
        let product_id=cad.as_ref().and_then(|v|v.get("lcsc")).and_then(|v|v.get("id")).and_then(Value::as_u64);
        let url=product_id.map(|id|format!("https://item.szlcsc.com/{id}.html")).unwrap_or_else(||format!("https://www.lcsc.com/product-detail/{code}.html"));
        let response=self.client.get(&url)
            .timeout(std::time::Duration::from_millis(6000)).send().await;
        let mut result=async {
            let mut response=response.ok()?;
            if !response.status().is_success() || response.content_length().unwrap_or(0)>2*1024*1024 {return None;}
            let mut bytes=Vec::new();
            while let Some(chunk)=response.chunk().await.ok()? {if bytes.len()+chunk.len()>2*1024*1024{return None;}bytes.extend_from_slice(&chunk);}
            description_from_product_page(std::str::from_utf8(&bytes).ok()?,code)
        }.await;
        if result.is_none() && product_id.is_some() {
            if let Some(html)=super::browser::public_product_page(&url,self.cache_dir.as_deref()) {
                result=description_from_product_page(&html,code);
                eprintln!("  description: public page browser fallback {}",if result.is_some(){"verified"}else{"unavailable"});
            }
        }
        // A verification/error page is not a successful empty product description.
        if result.is_some() {if let Some(path)=self.cache_path(code,"description.json") {
            Self::write_text_cache(&path,&serde_json::json!({"Version":"product-cn-1","DescriptionSource":url,"Description":result.clone().unwrap_or_default()}).to_string());
        }}
        eprintln!("  timing: description network {}ms",timer.elapsed().as_millis());
        result
    }

    /// Fetch component CAD data. Returns the `result` field from the API
    /// envelope (after success-check).
    pub async fn get_cad_data(&self, lcsc_id: &str) -> Result<Value> {
        let timer=std::time::Instant::now();
        if let Some(path) = &self.cad_source {
            // No fallback: conversion and slot verification must see the exact
            // same envelope even while another AD instance refreshes its cache.
            let file = std::fs::File::open(path)?;
            let mut bytes = Vec::new();
            use std::io::Read;
            file.take(64 * 1024 * 1024 + 1).read_to_end(&mut bytes)?;
            if bytes.len() > 64 * 1024 * 1024 {return Err(Error::Api("pinned CAD source exceeds 64 MiB".into()));}
            let data = Self::extract_result(serde_json::from_slice(&bytes)?)?;
            if data.pointer("/lcsc/number").and_then(Value::as_str) != Some(lcsc_id) {
                return Err(Error::Api("pinned CAD source has a different or missing C number".into()));
            }
            eprintln!("  source-input: pinned-cad-1");
            return Ok(data);
        }
        if let Some(p) = self.cache_path(lcsc_id, "json").filter(|p|self.fresh(p)) {
            if let Some(text) = self.read_text_cache(&p) {
                if let Ok(v) = serde_json::from_str::<Value>(&text) {
                    if let Ok(data)=Self::extract_result(v) {
                        if data.get("lcsc").and_then(|v|v.get("number")).and_then(Value::as_str)==Some(lcsc_id) {
                            eprintln!("  timing: cad cache {}ms",timer.elapsed().as_millis());return Ok(data);
                        }
                    }
                }
            }
        }

        let url = API_ENDPOINT.replace("{lcsc_id}", lcsc_id);
        let resp = self.client.get(&url).send().await?;
        let status = resp.status();
        if !status.is_success() {
            return Err(Error::Api(format!("status {} for {}", status, url)));
        }
        let text = resp.text().await?;
        if let Some(p) = self.cache_path(lcsc_id, "json") {
            Self::write_text_cache(&p, &text);
        }
        let v: Value = serde_json::from_str(&text)?;
        let data=Self::extract_result(v)?;
        eprintln!("  timing: cad network {}ms",timer.elapsed().as_millis());
        Ok(data)
    }

    fn extract_result(v: Value) -> Result<Value> {
        if v.get("success") == Some(&Value::Bool(false)) {
            return Err(Error::Api(format!(
                "API returned success=false: {}",
                v.get("message")
                    .map(|m| m.to_string())
                    .unwrap_or_default()
            )));
        }
        match v.get("result") {
            Some(r) => Ok(r.clone()),
            None => Err(Error::Api("missing 'result' in API response".into())),
        }
    }

    /// Fetch raw STEP bytes for a 3D-model uuid. Returns None on 404.
    pub async fn get_step(&self, uuid: &str) -> Result<Option<Vec<u8>>> {
        let compact: String = uuid.chars().filter(|c| *c != '-').collect();
        if compact.len() != 32 || !compact.bytes().all(|b| b.is_ascii_hexdigit()) {
            return Err(Error::Api("invalid 3D model identifier".into()));
        }
        if let Some(bytes)=self.cached_step(uuid) {eprintln!("  timing: step cache hit");return Ok(Some(bytes));}
        let timer=std::time::Instant::now();
        let url = ENDPOINT_3D_STEP.replace("{uuid}", uuid);
        let mut resp = self.client.get(&url).send().await?;
        if resp.status() == reqwest::StatusCode::NOT_FOUND { return Ok(None); }
        if !resp.status().is_success() {
            return Err(Error::Api(format!("STEP download status {}", resp.status())));
        }
        const LIMIT: usize = 64 * 1024 * 1024;
        if resp.content_length().unwrap_or(0) > LIMIT as u64 {
            return Err(Error::Api("STEP response exceeds 64 MiB".into()));
        }
        let mut bytes = Vec::new();
        while let Some(chunk) = resp.chunk().await? {
            if bytes.len() + chunk.len() > LIMIT {
                return Err(Error::Api("STEP response exceeds 64 MiB".into()));
            }
            bytes.extend_from_slice(&chunk);
        }
        let bytes=crate::convert::model3d::normalize_step(&bytes)?;
        eprintln!("  timing: step network {}ms",timer.elapsed().as_millis());
        if let Some(p) = self.cache_path(uuid, "step") {
            Self::write_bytes_cache(&p, &bytes);
        }
        Ok(Some(bytes))
    }

    pub async fn get_obj(&self, uuid: &str) -> Result<Option<String>> {
        if let Some(p) = self.cache_path(uuid, "obj") {
            if let Some(t) = self.read_text_cache(&p) {
                return Ok(Some(t));
            }
        }
        let url = ENDPOINT_3D_OBJ.replace("{uuid}", uuid);
        let resp = self.client.get(&url).send().await?;
        if !resp.status().is_success() {
            return Ok(None);
        }
        let text = resp.text().await?;
        if let Some(p) = self.cache_path(uuid, "obj") {
            Self::write_text_cache(&p, &text);
        }
        Ok(Some(text))
    }
}

fn plain_html(input: &str) -> String {
    let mut text=String::new();let mut tag=false;
    for ch in input.chars() { if ch=='<' {tag=true;text.push(' ');} else if ch=='>' {tag=false;} else if !tag {text.push(ch);} }
    for (entity,value) in [("&amp;","&"),("&lt;","<"),("&gt;",">"),("&quot;","\""),("&#39;","'"),("&nbsp;"," ")] {text=text.replace(entity,value);}
    text.split_whitespace().collect::<Vec<_>>().join(" ").chars().take(1000).collect()
}

/// Read only SKU-verified product data, never SEO price/stock descriptions.
pub fn description_from_product_page(html: &str, code: &str) -> Option<String> {
    fn matches_sku(value:&Value,code:&str)->bool {
        if let Some(rows)=value.as_array(){return rows.iter().any(|row|matches_sku(row,code));}
        if let Some(rows)=value.get("@graph"){return matches_sku(rows,code);}
        value.get("@type").and_then(Value::as_str)==Some("Product")&&value.get("sku").and_then(Value::as_str)==Some(code)
    }
    let mut verified=false;
    for script in html.split("<script").skip(1){
        let Some((header,rest))=script.split_once('>') else {continue};
        if !header.contains("application/ld+json"){continue;}
        if let Ok(value)=serde_json::from_str::<Value>(rest.split("</script>").next()?){if matches_sku(&value,code){verified=true;break;}}
    }
    if !verified{return None;}
    if let Some((_,rest))=html.split_once(">描述</dt>"){
        let (_,dd)=rest.split_once("<dd")?;let (_,content)=dd.split_once('>')?;
        let text=plain_html(content.split("</dd>").next()?);
        if !text.is_empty(){return Some(text);}
    }
    if let Some((_,rest))=html.split_once(">Key Attributes</td>"){
        let text=plain_html(rest.split("</tr>").next()?);if !text.is_empty(){return Some(text);}
    }
    None
}

#[cfg(test)]
mod description_tests {
    use super::*;
    #[test]
    fn chinese_product_description_is_not_seo_or_category(){
        let html=r#"<script type="application/ld+json">{"@graph":[{"@type":"Product","sku":"C46634460","description":"SEO price text"}]}</script><dt class="label">描述</dt><dd title="ignored">用于电流检测,SMD贴片安装,高功率,超低阻值(低至0.0R)</dd>"#;
        assert_eq!(description_from_product_page(html,"C46634460").as_deref(),Some("用于电流检测,SMD贴片安装,高功率,超低阻值(低至0.0R)"));
        assert!(description_from_product_page(html,"C1").is_none());
    }
    #[test]
    fn product_description_requires_exact_sku_and_prefers_function() {
        let html=r#"<script type="application/ld+json">{"@type":"Product","sku":"C962091","description":"TQFN Power Management RoHS"}</script><tr><td>Key Attributes</td><td><span>Integrated Temperature Controllers for Peltier Modules</span></td></tr>"#;
        assert_eq!(description_from_product_page(html,"C962091").as_deref(),Some("Integrated Temperature Controllers for Peltier Modules"));
        assert_eq!(description_from_product_page(html,"C1"),None);
    }
}
