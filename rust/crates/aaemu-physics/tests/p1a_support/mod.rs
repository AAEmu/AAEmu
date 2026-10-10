//! P1a test support: parse the `gp` / `gb` lines of the P1a oracle probes
//! (`oracle/p1a_probes.c`) and rebuild the primitives with the port.
//!
//! A probe line is `gp <probe> <type> P <prim words> I <inputs> O <outputs>`; the tests
//! replay the inputs through the port, format the outputs the way the probe prints them and
//! compare the token strings (every float as raw bits).

#![allow(dead_code)]

use aaemu_physics::geometry::primitives::{BoxPrim, CylinderPrim, Plane, RayPrim, SpherePrim};
use aaemu_physics::geometry::shapes::{BoxGeom, CapsuleGeom, CylinderGeom, RayGeom, SphereGeom};
use aaemu_physics::geometry::{GeomWorldData, Geometry};
use aaemu_physics::math::{Mat33, Vec3};
use aaemu_physics::oracle::scenario_trace;

/// One probe case.
#[derive(Clone, Debug)]
pub struct Case {
    pub line: usize,
    pub probe: String,
    pub ty: i32,
    pub prim: Vec<u32>,
    pub inp: Vec<String>,
    pub out: Vec<String>,
}

impl Case {
    /// Input word `i` as bits.
    pub fn iw(&self, i: usize) -> u32 {
        u32::from_str_radix(&self.inp[i], 16).unwrap_or_else(|e| panic!("{}: {e}", self.inp[i]))
    }
    /// Input word `i` as a float.
    pub fn f(&self, i: usize) -> f32 {
        f32::from_bits(self.iw(i))
    }
    /// Input words `i..i+3` as a vector.
    pub fn v(&self, i: usize) -> Vec3 {
        Vec3::new(self.f(i), self.f(i + 1), self.f(i + 2))
    }
    /// Input token `i` as a decimal int.
    pub fn int(&self, i: usize) -> i32 {
        self.inp[i].parse().unwrap()
    }
    /// The water plane at input word `i` (`n`, `origin`).
    pub fn plane(&self, i: usize) -> Plane {
        Plane {
            n: self.v(i),
            origin: self.v(i + 3),
        }
    }
    /// A `geom_world_data` at input word `i`: 13 words (offset, R, scale) or 22 (+ v, w, com).
    pub fn gwd(&self, i: usize, full: bool) -> GeomWorldData {
        let mut r = [0f32; 9];
        for (k, x) in r.iter_mut().enumerate() {
            *x = self.f(i + 3 + k);
        }
        let mut g = GeomWorldData {
            offset: self.v(i),
            r: Mat33::from_array(r),
            scale: self.f(i + 12),
            ..GeomWorldData::default()
        };
        if full {
            g.v = self.v(i + 13);
            g.w = self.v(i + 16);
            g.center_of_mass = self.v(i + 19);
        }
        g
    }
}

/// Hex word of a float.
pub fn hx(f: f32) -> String {
    format!("{:08x}", f.to_bits())
}
/// Hex word of an int.
pub fn hi(i: i32) -> String {
    format!("{:08x}", i as u32)
}
/// Push the three words of a vector.
pub fn push_v(out: &mut Vec<String>, v: Vec3) {
    out.push(hx(v.x));
    out.push(hx(v.y));
    out.push(hx(v.z));
}
/// Push the 16 words of a box primitive.
pub fn push_box(out: &mut Vec<String>, b: &BoxPrim) {
    for f in b.basis.to_array() {
        out.push(hx(f));
    }
    out.push(hi(b.oriented));
    push_v(out, b.center);
    push_v(out, b.size);
}

/// All `gp` cases of `tests/scenarios/<name>.scn` (golden, regenerated with the oracle).
pub fn cases(name: &str) -> Vec<Case> {
    let t = scenario_trace(name).unwrap_or_else(|e| panic!("{name}: {e}"));
    let v = parse_cases(&t.other);
    assert!(!v.is_empty(), "{name}: no gp lines");
    v
}

/// `gp` cases of an ad-hoc scenario run through the oracle (no golden); `None` without the
/// oracle. Used for large fuzz runs that would make goldens too big.
pub fn adhoc_cases(text: &str) -> Option<Vec<Case>> {
    let t = aaemu_physics::oracle::run_scenario_text(text)?.unwrap_or_else(|e| panic!("{e}"));
    Some(parse_cases(&t.other))
}

fn parse_cases(other: &[Vec<String>]) -> Vec<Case> {
    let mut v = Vec::new();
    for (line, l) in other.iter().enumerate() {
        if l[0] != "gp" {
            continue;
        }
        let p = l.iter().position(|s| s == "P").unwrap();
        let i = l.iter().position(|s| s == "I").unwrap();
        let o = l.iter().position(|s| s == "O").unwrap();
        v.push(Case {
            line,
            probe: l[1].clone(),
            ty: l[2].parse().unwrap(),
            prim: l[p + 1..i]
                .iter()
                .map(|s| u32::from_str_radix(s, 16).unwrap())
                .collect(),
            inp: l[i + 1..o].to_vec(),
            out: l[o + 1..].to_vec(),
        });
    }
    v
}

/// Raw `other` lines starting with `key`.
pub fn lines(name: &str, key: &str) -> Vec<Vec<String>> {
    let t = scenario_trace(name).unwrap_or_else(|e| panic!("{name}: {e}"));
    t.other.into_iter().filter(|l| l[0] == key).collect()
}

fn fw(w: &[u32], i: usize) -> f32 {
    f32::from_bits(w[i])
}
fn vw(w: &[u32], i: usize) -> Vec3 {
    Vec3::new(fw(w, i), fw(w, i + 1), fw(w, i + 2))
}

/// The probe's primitive words as the typed primitive.
pub fn box_prim(w: &[u32]) -> BoxPrim {
    let mut m = [0f32; 9];
    for (k, x) in m.iter_mut().enumerate() {
        *x = fw(w, k);
    }
    BoxPrim {
        basis: Mat33::from_array(m),
        oriented: w[9] as i32,
        center: vw(w, 10),
        size: vw(w, 13),
    }
}
pub fn sphere_prim(w: &[u32]) -> SpherePrim {
    SpherePrim {
        center: vw(w, 0),
        r: fw(w, 3),
    }
}
pub fn cyl_prim(w: &[u32]) -> CylinderPrim {
    CylinderPrim {
        center: vw(w, 0),
        axis: vw(w, 3),
        r: fw(w, 6),
        hh: fw(w, 7),
    }
}
pub fn ray_prim(w: &[u32]) -> RayPrim {
    RayPrim {
        origin: vw(w, 0),
        dir: vw(w, 3),
    }
}

/// A geometry of any primitive type, built like `CreatePrimitive`.
pub enum AnyGeom {
    Box(BoxGeom),
    Sphere(SphereGeom),
    Cylinder(CylinderGeom),
    Capsule(CapsuleGeom),
    Ray(RayGeom),
}

impl AnyGeom {
    pub fn new(ty: i32, w: &[u32]) -> Self {
        match ty {
            0 => AnyGeom::Box(BoxGeom::new(&box_prim(w))),
            3 => AnyGeom::Ray(RayGeom::new(&ray_prim(w))),
            4 => AnyGeom::Sphere(SphereGeom::new(&sphere_prim(w))),
            5 => AnyGeom::Cylinder(CylinderGeom::new(&cyl_prim(w))),
            6 => AnyGeom::Capsule(CapsuleGeom::new(&cyl_prim(w))),
            _ => panic!("type {ty}"),
        }
    }
    pub fn of(c: &Case) -> Self {
        Self::new(c.ty, &c.prim)
    }
    pub fn g(&self) -> &dyn Geometry {
        match self {
            AnyGeom::Box(g) => g,
            AnyGeom::Sphere(g) => g,
            AnyGeom::Cylinder(g) => g,
            AnyGeom::Capsule(g) => g,
            AnyGeom::Ray(g) => g,
        }
    }
}

/// Compare the port's tokens with the oracle's for a case; collect a readable failure.
pub fn check(c: &Case, got: &[String], fails: &mut Vec<String>) {
    if got != c.out.as_slice() {
        let k = got
            .iter()
            .zip(&c.out)
            .position(|(a, b)| a != b)
            .unwrap_or(got.len().min(c.out.len()));
        fails.push(format!(
            "{} type {} (line {}): first difference at output token {k}\n  prim {:08x?}\n  in   {:?}\n  port   {:?}\n  oracle {:?}",
            c.probe, c.ty, c.line, c.prim, c.inp, got, c.out
        ));
    }
}

/// Panic with the first few failures.
pub fn finish(what: &str, total: usize, fails: &[String]) {
    assert!(total > 0, "{what}: no cases");
    if !fails.is_empty() {
        let shown: Vec<&String> = fails.iter().take(5).collect();
        panic!(
            "{what}: {} of {total} cases differ from the oracle\n{}",
            fails.len(),
            shown
                .iter()
                .map(|s| s.as_str())
                .collect::<Vec<_>>()
                .join("\n")
        );
    }
}
