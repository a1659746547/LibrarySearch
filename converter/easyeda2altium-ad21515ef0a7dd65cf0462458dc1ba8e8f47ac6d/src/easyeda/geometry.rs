//! SVG path geometry in EasyEDA source units (one unit = 10 mil).
//! Endpoint-to-center conversion follows W3C SVG 2, Appendix B.2.
//! No external dependencies: `rustc --test geometry.rs` also tests this module.

use std::f64::consts::{PI, TAU};

pub type Point = (f64, f64);
pub const TOLERANCE: f64 = 0.05; // 0.5 mil before Altium coordinate quantization.
const MAX_POINTS: usize = 32_768;
const MAX_DEPTH: usize = 18;

#[derive(Clone, Copy, Debug)]
pub struct ArcSpec {
    pub start: Point,
    pub end: Point,
    pub rx: f64,
    pub ry: f64,
    pub rotation: f64,
    pub large: bool,
    pub sweep: bool,
}

#[derive(Clone, Copy, Debug)]
pub struct ArcGeometry {
    pub center: Point,
    pub rx: f64,
    pub ry: f64,
    pub rotation: f64,
    pub start: f64,
    pub delta: f64,
}

impl ArcSpec {
    pub fn geometry(self) -> Option<ArcGeometry> {
        if self.start == self.end || self.rx == 0.0 || self.ry == 0.0 {
            return None;
        }
        let phi = self.rotation.rem_euclid(360.0).to_radians();
        let (sin, cos) = phi.sin_cos();
        let dx = (self.start.0 - self.end.0) / 2.0;
        let dy = (self.start.1 - self.end.1) / 2.0;
        let x = cos * dx + sin * dy;
        let y = -sin * dx + cos * dy;
        let mut rx = self.rx.abs();
        let mut ry = self.ry.abs();
        let scale = (x / rx).hypot(y / ry).max(1.0);
        rx *= scale;
        ry *= scale;
        let ux = x / rx;
        let uy = y / ry;
        let denominator = ux * ux + uy * uy;
        if denominator == 0.0 || !denominator.is_finite() {
            return None;
        }
        let sign = if self.large == self.sweep { -1.0 } else { 1.0 };
        let coefficient = sign * ((1.0 - denominator).max(0.0) / denominator).sqrt();
        let cx = coefficient * rx * uy;
        let cy = -coefficient * ry * ux;
        let center = (
            cos * cx - sin * cy + (self.start.0 + self.end.0) / 2.0,
            sin * cx + cos * cy + (self.start.1 + self.end.1) / 2.0,
        );
        let u = ((x - cx) / rx, (y - cy) / ry);
        let v = ((-x - cx) / rx, (-y - cy) / ry);
        let start = u.1.atan2(u.0);
        let mut delta = (u.0 * v.1 - u.1 * v.0).atan2(u.0 * v.0 + u.1 * v.1);
        if self.sweep && delta < 0.0 {
            delta += TAU;
        } else if !self.sweep && delta > 0.0 {
            delta -= TAU;
        }
        if ![center.0, center.1, rx, ry, start, delta].iter().all(|v| v.is_finite()) {
            return None;
        }
        Some(ArcGeometry { center, rx, ry, rotation: phi, start, delta })
    }
}

impl ArcGeometry {
    pub fn point(self, t: f64) -> Point {
        let angle = self.start + self.delta * t;
        let (sin, cos) = self.rotation.sin_cos();
        let x = self.rx * angle.cos();
        let y = self.ry * angle.sin();
        (self.center.0 + cos * x - sin * y, self.center.1 + sin * x + cos * y)
    }

    /// Altium arcs run counterclockwise in Y-up coordinates. Reflect Y and
    /// reverse the endpoints when the source traverses increasing angles.
    pub fn altium_angles(self) -> Option<(f64, f64)> {
        if (self.rx - self.ry).abs() > self.rx.max(self.ry) * 1e-9 {
            return None;
        }
        let first = -(self.start + self.rotation).to_degrees();
        let last = -(self.start + self.rotation + self.delta).to_degrees();
        let (start, end) = if self.delta > 0.0 { (last, first) } else { (first, last) };
        Some((start.rem_euclid(360.0), end.rem_euclid(360.0)))
    }
}

struct Reader<'a> {
    text: &'a [u8],
    at: usize,
}

impl<'a> Reader<'a> {
    fn new(text: &'a str) -> Self { Self { text: text.as_bytes(), at: 0 } }
    fn skip(&mut self) {
        while self.text.get(self.at).is_some_and(|c| c.is_ascii_whitespace() || *c == b',') {
            self.at += 1;
        }
    }
    fn peek(&mut self) -> Option<u8> { self.skip(); self.text.get(self.at).copied() }
    fn number(&mut self) -> Option<f64> {
        self.skip();
        let start = self.at;
        if matches!(self.text.get(self.at), Some(b'+' | b'-')) { self.at += 1; }
        let mut digits = 0;
        while self.text.get(self.at).is_some_and(u8::is_ascii_digit) { self.at += 1; digits += 1; }
        if self.text.get(self.at) == Some(&b'.') {
            self.at += 1;
            while self.text.get(self.at).is_some_and(u8::is_ascii_digit) { self.at += 1; digits += 1; }
        }
        if digits == 0 { return None; }
        if matches!(self.text.get(self.at), Some(b'e' | b'E')) {
            self.at += 1;
            if matches!(self.text.get(self.at), Some(b'+' | b'-')) { self.at += 1; }
            let exponent = self.at;
            while self.text.get(self.at).is_some_and(u8::is_ascii_digit) { self.at += 1; }
            if exponent == self.at { return None; }
        }
        let value: f64 = std::str::from_utf8(&self.text[start..self.at]).ok()?.parse().ok()?;
        (value.is_finite() && value.abs() <= 10_000_000.0).then_some(value)
    }
    fn flag(&mut self) -> Option<bool> {
        let value = self.peek()?;
        if !matches!(value, b'0' | b'1') { return None; }
        self.at += 1;
        Some(value == b'1')
    }
    fn point(&mut self, relative_to: Point) -> Option<Point> {
        Some((self.number()? + relative_to.0, self.number()? + relative_to.1))
    }
    fn arc(&mut self, start: Point, relative: bool) -> Option<ArcSpec> {
        let rx = self.number()?;
        let ry = self.number()?;
        let rotation = self.number()?;
        let large = self.flag()?;
        let sweep = self.flag()?;
        let end = self.point(if relative { start } else { (0.0, 0.0) })?;
        Some(ArcSpec { start, end, rx, ry, rotation, large, sweep })
    }
}

/// Recognize a single arc without losing its native circular representation.
pub fn single_arc(path: &str) -> Option<ArcSpec> {
    let mut reader = Reader::new(path);
    if !matches!(reader.peek()?, b'M' | b'm') { return None; }
    reader.at += 1;
    let start = reader.point((0.0, 0.0))?;
    let command = reader.peek()?;
    if !matches!(command, b'A' | b'a') { return None; }
    reader.at += 1;
    let arc = reader.arc(start, command == b'a')?;
    if reader.peek().is_some() { return None; }
    Some(arc)
}

fn push(points: &mut Vec<Point>, point: Point) -> Option<()> {
    if points.len() >= MAX_POINTS || !point.0.is_finite() || !point.1.is_finite() {
        return None;
    }
    points.push(point);
    Some(())
}

fn midpoint(a: Point, b: Point) -> Point { ((a.0 + b.0) / 2.0, (a.1 + b.1) / 2.0) }
fn reflected(control: Point, position: Point) -> Point { (2.0 * position.0 - control.0, 2.0 * position.1 - control.1) }
fn distance_to_segment(p: Point, a: Point, b: Point) -> f64 {
    let dx = b.0 - a.0;
    let dy = b.1 - a.1;
    let norm = dx * dx + dy * dy;
    let t = if norm == 0.0 { 0.0 } else { ((p.0 - a.0) * dx + (p.1 - a.1) * dy) / norm }.clamp(0.0, 1.0);
    (p.0 - a.0 - t * dx).hypot(p.1 - a.1 - t * dy)
}

fn cubic(points: &mut Vec<Point>, p: [Point; 4], depth: usize) -> Option<()> {
    if distance_to_segment(p[1], p[0], p[3]).max(distance_to_segment(p[2], p[0], p[3])) <= TOLERANCE {
        return push(points, p[3]);
    }
    if depth >= MAX_DEPTH { return None; }
    let a = midpoint(p[0], p[1]);
    let b = midpoint(p[1], p[2]);
    let c = midpoint(p[2], p[3]);
    let d = midpoint(a, b);
    let e = midpoint(b, c);
    let f = midpoint(d, e);
    cubic(points, [p[0], a, d, f], depth + 1)?;
    cubic(points, [f, e, c, p[3]], depth + 1)
}

fn arc_points(points: &mut Vec<Point>, arc: ArcSpec) -> Option<()> {
    if arc.start == arc.end { return Some(()); }
    if arc.rx == 0.0 || arc.ry == 0.0 { return push(points, arc.end); }
    let geometry = arc.geometry()?;
    // ||p''(theta)|| <= max(rx,ry); the chord deviation is <= R*step^2/8.
    let step = (8.0 * TOLERANCE / geometry.rx.max(geometry.ry)).sqrt().min(PI / 2.0);
    let count = (geometry.delta.abs() / step).ceil().max(1.0) as usize;
    if count >= MAX_POINTS || points.len().checked_add(count)? > MAX_POINTS { return None; }
    for index in 1..count {
        push(points, geometry.point(index as f64 / count as f64))?;
    }
    push(points, arc.end)
}

/// Parse the whole path or return None; never return a misleading partial shape.
/// Supports M/L/H/V/C/S/Q/T/A/Z, compact numbers, repetitions and relative forms.
pub fn path_polylines(path: &str) -> Option<Vec<Vec<Point>>> {
    if path.len() > 1_048_576 { return None; }
    let mut reader = Reader::new(path);
    let mut paths: Vec<Vec<Point>> = Vec::new();
    let mut position = (0.0, 0.0);
    let mut origin = position;
    let mut command = b'\0';
    let mut previous = b'\0';
    let mut cubic_control = position;
    let mut quadratic_control = position;
    let mut active = false;
    let mut total = 0;
    while let Some(next) = reader.peek() {
        if next.is_ascii_alphabetic() { command = next; reader.at += 1; }
        let upper = command.to_ascii_uppercase();
        let base = if command.is_ascii_lowercase() { position } else { (0.0, 0.0) };
        if paths.is_empty() && upper != b'M' { return None; }
        if upper != b'M' && upper != b'Z' && !active {
            paths.push(vec![position]);
            origin = position;
            active = true;
            total += 1;
        }
        let before = paths.last().map_or(0, Vec::len);
        match upper {
            b'M' => {
                position = reader.point(base)?;
                paths.push(vec![position]);
                origin = position;
                active = true;
                command = if command == b'm' { b'l' } else { b'L' };
            }
            b'L' => { position = reader.point(base)?; push(paths.last_mut()?, position)?; }
            b'H' => { position.0 = reader.number()? + base.0; push(paths.last_mut()?, position)?; }
            b'V' => { position.1 = reader.number()? + base.1; push(paths.last_mut()?, position)?; }
            b'C' | b'S' => {
                let first = if upper == b'S' {
                    if matches!(previous, b'C' | b'S') { reflected(cubic_control, position) } else { position }
                } else { reader.point(base)? };
                let second = reader.point(base)?;
                let end = reader.point(base)?;
                cubic(paths.last_mut()?, [position, first, second, end], 0)?;
                cubic_control = second;
                position = end;
            }
            b'Q' | b'T' => {
                let control = if upper == b'T' {
                    if matches!(previous, b'Q' | b'T') { reflected(quadratic_control, position) } else { position }
                } else { reader.point(base)? };
                let end = reader.point(base)?;
                let a = (position.0 + (control.0 - position.0) * 2.0 / 3.0, position.1 + (control.1 - position.1) * 2.0 / 3.0);
                let b = (end.0 + (control.0 - end.0) * 2.0 / 3.0, end.1 + (control.1 - end.1) * 2.0 / 3.0);
                cubic(paths.last_mut()?, [position, a, b, end], 0)?;
                quadratic_control = control;
                position = end;
            }
            b'A' => {
                let arc = reader.arc(position, command.is_ascii_lowercase())?;
                arc_points(paths.last_mut()?, arc)?;
                position = arc.end;
            }
            b'Z' => {
                if position != origin { push(paths.last_mut()?, origin)?; }
                position = origin;
                active = false;
                command = b'\0';
            }
            _ => return None,
        }
        // Count all paths, including moveto-only subpaths, against one budget.
        let added = if upper == b'M' { 1 } else { paths.last()?.len().saturating_sub(before) };
        total += added;
        if total > MAX_POINTS { return None; }
        previous = upper;
    }
    paths.retain(|points| points.len() >= 2);
    Some(paths)
}

#[cfg(test)]
mod tests {
    use super::*;
    fn near(a: f64, b: f64) { assert!((a-b).abs() < 1e-7, "{a} != {b}"); }
    fn point_near(a: Point, b: Point) { near(a.0, b.0); near(a.1, b.1); }

    #[test]
    fn circular_arc_endpoints_and_sweeps() {
        for large in [false, true] { for sweep in [false, true] {
            let spec = ArcSpec { start:(10.0,0.0), end:(0.0,10.0), rx:10.0, ry:10.0, rotation:0.0, large, sweep };
            let arc = spec.geometry().unwrap();
            point_near(arc.point(0.0),spec.start); point_near(arc.point(1.0),spec.end);
            assert_eq!(arc.delta > 0.0,sweep);
            near(arc.delta.abs().to_degrees(),if large {270.0} else {90.0});
            let (start,end) = arc.altium_angles().unwrap();
            near((end-start).rem_euclid(360.0),arc.delta.abs().to_degrees());
            let native = |angle:f64| (arc.center.0+arc.rx*angle.to_radians().cos(), -arc.center.1+arc.rx*angle.to_radians().sin());
            let (a,b) = if sweep {(spec.end,spec.start)} else {(spec.start,spec.end)};
            point_near(native(start),(a.0,-a.1)); point_near(native(end),(b.0,-b.1));
        }}
    }
    #[test]
    fn rotated_ellipse_and_radius_correction() {
        for rotation in [0.0,30.0,90.0,240.0] {
            let spec = ArcSpec {start:(-20.0,7.0),end:(40.0,17.0),rx:5.0,ry:2.0,rotation,large:true,sweep:false};
            let arc=spec.geometry().unwrap();
            point_near(arc.point(0.0),spec.start); point_near(arc.point(1.0),spec.end);
            assert!(arc.altium_angles().is_none()); assert!(arc.rx>=5.0);
        }
    }
    #[test]
    fn compact_relative_paths_and_close() {
        let paths=path_polylines("M1e2,20l10-5h5v10z m20,0 5,5").unwrap();
        assert_eq!(paths[0],vec![(100.0,20.0),(110.0,15.0),(115.0,15.0),(115.0,25.0),(100.0,20.0)]);
        assert_eq!(paths[1],vec![(120.0,20.0),(125.0,25.0)]);
    }
    #[test]
    fn cubic_curve_error_is_bounded() {
        let points=path_polylines("M0 0 C0 100 100 100 100 0").unwrap().remove(0);
        for index in 0..=1000 {
            let t=index as f64/1000.0;
            let p=(300.0*t*t-200.0*t*t*t,300.0*t*(1.0-t));
            let error=points.windows(2).map(|s|distance_to_segment(p,s[0],s[1])).fold(f64::INFINITY,f64::min);
            assert!(error<=TOLERANCE,"{error}");
        }
        assert_eq!(points.last(),Some(&(100.0,0.0)));
    }
    #[test]
    fn collinear_cubic_backtracking_is_not_lost() {
        let p=path_polylines("M0 0 C100 0 -100 0 1 0").unwrap();
        assert!(p[0].iter().any(|p|p.0>20.0)); assert!(p[0].iter().any(|p|p.0< -20.0));
    }
    #[test]
    fn smooth_cubic_reflects_only_after_cubic() {
        assert_eq!(path_polylines("M0 0 C0 10 10 10 10 0 S20 -10 20 0"),path_polylines("M0 0 C0 10 10 10 10 0 C10 -10 20 -10 20 0"));
        assert_eq!(path_polylines("M0 0 L10 0 S20 10 20 0"),path_polylines("M0 0 L10 0 C10 0 20 10 20 0"));
    }
    #[test]
    fn relative_quadratic_and_smooth_quadratic() {
        assert_eq!(path_polylines("M0 0 q10 20 20 0 t20 0"),path_polylines("M0 0 Q10 20 20 0 Q30 -20 40 0"));
        assert_eq!(path_polylines("M0 0 L10 0 T20 0"),path_polylines("M0 0 L10 0 Q10 0 20 0"));
    }
    #[test]
    fn elliptical_tessellation_error_is_bounded() {
        let path="M10 0 A20 10 30 1 1 0 10";
        let arc=single_arc(path).unwrap().geometry().unwrap();
        let points=&path_polylines(path).unwrap()[0];
        for index in 0..=1000 {
            let p=arc.point(index as f64/1000.0);
            let error=points.windows(2).map(|s|distance_to_segment(p,s[0],s[1])).fold(f64::INFINITY,f64::min);
            assert!(error<=TOLERANCE+1e-9,"{error}");
        }
    }
    #[test]
    fn relative_arcs_and_compact_flags() {
        let arc=single_arc("m10 0a10 10 0 01-10 10").unwrap();
        assert!(!arc.large && arc.sweep); assert_eq!(arc.end,(0.0,10.0));
        assert_eq!(path_polylines("m10 0a10 10 0 01-10 10"),path_polylines("M10 0 A10 10 0 0 1 0 10"));
    }
    #[test]
    fn degenerate_arcs_follow_svg_rules() {
        assert_eq!(path_polylines("M0 0 A0 10 0 0 1 5 5").unwrap()[0],vec![(0.0,0.0),(5.0,5.0)]);
        assert!(path_polylines("M0 0 A10 10 0 0 1 0 0").unwrap().is_empty());
        assert_eq!(path_polylines("M10 0 A-10 -10 0 0 1 0 10"),path_polylines("M10 0 A10 10 0 0 1 0 10"));
    }
    #[test]
    fn bad_paths_reject_the_whole_shape() {
        for path in ["L0 0","M0 0 L10","M0 0 C20 20 30 30","M0 0 @","M0 0 Z 10 20","M0 0 A10 10 0 2 0 10 10","M0 0 LNaN 0","M0 0 L1e999 0","M0 0 L1e 0"] {
            assert!(path_polylines(path).is_none(),"accepted {path}");
        }
    }
    #[test]
    fn curves_preserve_subpaths_and_close_then_line() {
        let p=path_polylines("M0 0 Q10 20 20 0Z L30 0 M40 0C40 20 60 20 60 0").unwrap();
        assert_eq!(p.len(),3); assert_eq!(p[0].first(),p[0].last()); assert_eq!(p[1],vec![(0.0,0.0),(30.0,0.0)]);
    }
    #[test]
    fn complexity_limit_rejects_instead_of_hanging_or_emitting_partial_geometry() {
        let huge=format!("M0 0 {}", "L1 1 ".repeat(MAX_POINTS));
        assert!(path_polylines(&huge).is_none());
        assert!(path_polylines("M0 0 C100000000 100000000 -100000000 -100000000 1 1").is_none());
    }
}
