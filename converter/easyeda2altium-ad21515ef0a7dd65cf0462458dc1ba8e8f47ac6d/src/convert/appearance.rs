//! Optional visual treatment. Never relocates or renames electrical pins.
use altium::coord::{Coord, CoordPoint};
use altium::enums::TextJustification;
use altium::sch::{Component, Library, RoundedRectangle};
use serde::Deserialize;
use crate::easyeda::types::EeSymbol;
use crate::error::{Error, Result};

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Appearance {
    pub mode: String,
    pub background: String,
    pub body_fill: String,
    pub border_color: String,
    pub text_color: String,
    pub pin_number_color: String,
    pub accent_color: String,
    pub font_family: String,
    pub pin_name_size: i32,
    pub pin_number_size: i32,
    pub label_size: i32,
    pub line_width: i32,
    pub corner_radius: i32,
    pub fill_body: bool,
    pub rounded_corners: bool,
    pub hide_duplicate_pin_names: bool,
    pub show_comment: bool,
}

fn color(value: &str) -> Result<i32> {
    if value.len() != 7 || !value.starts_with('#') || !value[1..].bytes().all(|c| c.is_ascii_hexdigit()) {
        return Err(Error::InvalidArg("appearance colors must use #RRGGBB".into()));
    }
    let rgb = i32::from_str_radix(&value[1..], 16).unwrap();
    Ok(((rgb & 255) << 16) | (rgb & 0xff00) | ((rgb >> 16) & 255))
}

impl Appearance {
    pub fn load(path: &str) -> Result<Self> {
        if std::fs::metadata(path)?.len() > 16_384 { return Err(Error::InvalidArg("appearance profile is too large".into())); }
        let profile: Self = serde_json::from_str(&std::fs::read_to_string(path)?)?;
        if !["Modern", "Classic", "Custom"].contains(&profile.mode.as_str())
            || !["#FFFFFF", "#F8F9FA"].contains(&profile.background.as_str())
            || profile.font_family.trim().is_empty() || profile.font_family.len() > 80
            || profile.font_family.chars().any(|c| c.is_control() || c == '|')
            || ![profile.pin_name_size, profile.pin_number_size, profile.label_size].iter().all(|n| (5..=14).contains(n))
            || !(0..=2).contains(&profile.line_width) || !(10..=50).contains(&profile.corner_radius) {
            return Err(Error::InvalidArg("invalid appearance profile".into()));
        }
        for value in [&profile.body_fill, &profile.border_color, &profile.text_color, &profile.pin_number_color, &profile.accent_color] { color(value)?; }
        Ok(profile)
    }

    fn font(&self, library: &mut Library, size: i32, bold: bool, text_color: i32) -> i32 {
        let count = library.file_header_parameters.get("FontIdCount").and_then(|v| v.parse::<i32>().ok()).unwrap_or(1).max(1);
        for id in 1..=count {
            let p = &library.file_header_parameters;
            if p.get(&format!("FontName{id}")).map(String::as_str) == Some(self.font_family.as_str())
                && p.get(&format!("Size{id}")) == Some(&size.to_string())
                && p.get(&format!("Bold{id}")).map(String::as_str) == Some(if bold { "T" } else { "F" })
                && p.get(&format!("Color{id}")) == Some(&text_color.to_string()) { return id; }
        }
        let id = count + 1;
        for (key, value) in [("FontName", self.font_family.clone()), ("Size", size.to_string()),
            ("Bold", if bold { "T" } else { "F" }.into()), ("Italic", "F".into()), ("Color", text_color.to_string())] {
            library.file_header_parameters.insert(format!("{key}{id}"), value);
        }
        library.file_header_parameters.insert("FontIdCount".into(), id.to_string());
        id
    }

    pub fn apply(&self, library: &mut Library, source: &EeSymbol, comp: &mut Component) -> Result<()> {
        if self.mode == "Classic" { return Ok(()); }
        let border = color(&self.border_color)?;
        let fill = color(&self.body_fill)?;
        let text = color(&self.text_color)?;
        let number = color(&self.pin_number_color)?;
        let accent = color(&self.accent_color)?;
        let background = color(&self.background)?;
        library.file_header_parameters.insert("AreaColor".into(), background.to_string());
        let name_font = self.font(library, self.pin_name_size, false, text);
        let number_font = self.font(library, self.pin_number_size, false, number);
        let label_font = self.font(library, self.label_size, true, text);
        let comment_font = self.font(library, self.pin_name_size, false, text);
        // Explicit first slot prevents imported annotations falling back to TNR.
        for (key, value) in [("FontName1", self.font_family.clone()), ("Size1", self.pin_name_size.to_string())] {
            library.file_header_parameters.insert(key.into(), value);
        }
        let prefix = source.info.prefix.trim_end_matches('?').to_ascii_uppercase();
        let passive = matches!(prefix.as_str(), "LED" | "D" | "ZD" | "Q" | "R" | "C" | "L" | "F");
        let width = match self.line_width { 1 => 2.0, 2 => 4.0, _ => 1.0 };
        let native_width=self.line_width+1;
        comp.color = border;
        comp.area_color = fill;
        for pin in &mut comp.pins {
            pin.color = border;
            pin.name_font_mode = 2;
            pin.designator_font_mode = 2;
            pin.name_custom_font_id = name_font;
            pin.designator_custom_font_id = number_font;
            pin.name_custom_color = text;
            pin.designator_custom_color = number;
            if self.mode=="Custom" && self.hide_duplicate_pin_names && pin.name.as_ref().zip(pin.designator.as_ref())
                .is_some_and(|(name, id)| name.trim() == id.trim()) { pin.show_name = false; }
        }
        for rectangle in &mut comp.rectangles {
            rectangle.color = border; rectangle.fill_color = fill;
            rectangle.line_width = Coord::from_mils(width);
            rectangle.is_filled = rectangle.is_filled && self.fill_body;
        }
        if self.rounded_corners && !passive {
            let mut straight = Vec::new();
            for rectangle in std::mem::take(&mut comp.rectangles) {
                let radius = Coord::from_mils(self.corner_radius as f64)
                    .min((rectangle.corner2.x - rectangle.corner1.x) / 4)
                    .min((rectangle.corner2.y - rectangle.corner1.y) / 4);
                // Do not round a corner bearing a source pin body endpoint.
                let near_corner = comp.pins.iter().filter(|pin| pin.common.owner_part_id == rectangle.common.owner_part_id).any(|pin| {
                    let near_x = (pin.location.x - rectangle.corner1.x).to_mils().abs() <= radius.to_mils()
                        || (pin.location.x - rectangle.corner2.x).to_mils().abs() <= radius.to_mils();
                    let near_y = (pin.location.y - rectangle.corner1.y).to_mils().abs() <= radius.to_mils()
                        || (pin.location.y - rectangle.corner2.y).to_mils().abs() <= radius.to_mils();
                    near_x && near_y
                });
                if radius < Coord::from_mils(10.0) || near_corner { straight.push(rectangle); continue; }
                comp.rounded_rectangles.push(RoundedRectangle {
                    corner1: rectangle.corner1, corner2: rectangle.corner2,
                    corner_radius_x: radius, corner_radius_y: radius, line_width: native_width,
                    color: border, fill_color: fill, is_filled: rectangle.is_filled,
                    common: rectangle.common, ..Default::default()
                });
            }
            comp.rectangles = straight;
        }
        for polygon in &mut comp.polygons { polygon.color=border; polygon.fill_color=fill; polygon.line_width=native_width; polygon.is_filled &= self.fill_body; }
        for line in &mut comp.polylines { line.color=border; line.line_width=native_width; }
        for arc in &mut comp.arcs { arc.color=border; arc.line_width=native_width; }
        for ellipse in &mut comp.ellipses { ellipse.color=border; ellipse.fill_color=fill; ellipse.line_width=native_width; ellipse.is_filled &= self.fill_body; }
        for label in &mut comp.labels {
            label.color = if matches!(label.text.trim(), "+" | "-" | "−") { accent } else { text };
            label.font_id = comment_font;
        }
        let bounds = comp.bounds();
        for parameter in &mut comp.parameters {
            parameter.color = text;
            parameter.font_id = comment_font;
            parameter.justification = TextJustification::BottomLeft;
            if parameter.name == "Designator" {
                parameter.font_id = label_font;
                if !bounds.is_empty() { parameter.location = CoordPoint::new(bounds.min.x, bounds.max.y + Coord::from_mils(60.0)); }
            } else if parameter.name == "Comment" {
                parameter.is_visible = self.show_comment;
                if !bounds.is_empty() { parameter.location = CoordPoint::new(bounds.min.x, bounds.min.y - Coord::from_mils(120.0)); }
            }
        }
        Ok(())
    }
}
