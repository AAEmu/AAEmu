//! P2b: cyl–cyl linear unprojection (0x351893C0) against the DLL (`physoracle --p2b`).
//!
//! Two generators: `nopoly` keeps every case away from the rim–rim quartic (a feature 0x43 or
//! parallel axes), `lin` covers everything (all relative orientations, both modes, areas).

use aaemu_physics::collision::contact::UnprojParams;
use aaemu_physics::collision::swept::probe::{check, Case, Rng};
use aaemu_physics::geometry::primitives::{CylinderPrim, Primitive};
use aaemu_physics::math::Vec3;

fn add(a: Vec3, b: Vec3, s: f32) -> Vec3 {
    Vec3::new(a.x + b.x * s, a.y + b.y * s, a.z + b.z * s)
}

fn quantv(rng: &mut Rng, lo: f32, hi: f32, step: f32) -> Vec3 {
    Vec3::new(
        rng.quant(lo, hi, step),
        rng.quant(lo, hi, step),
        rng.quant(lo, hi, step),
    )
}

/// A unit vector perpendicular to `a` (float-normalised).
fn perp(rng: &mut Rng, a: Vec3) -> Vec3 {
    loop {
        let v = rng.unit_or_axis(0.4);
        let c = Vec3::new(
            a.y * v.z - a.z * v.y,
            a.z * v.x - a.x * v.z,
            a.x * v.y - a.y * v.x,
        );
        let l2 = c.x * c.x + c.y * c.y + c.z * c.z;
        if l2 > 0.01 {
            let inv = 1.0 / l2.sqrt();
            return Vec3::new(c.x * inv, c.y * inv, c.z * inv);
        }
    }
}

fn radius(rng: &mut Rng) -> f32 {
    match rng.below(6) {
        0 => rng.f(0.01, 0.1),
        1 => rng.quant(0.25, 2.0, 0.25),
        _ => rng.f(0.05, 3.0),
    }
}

fn half_height(rng: &mut Rng) -> f32 {
    match rng.below(7) {
        0 => rng.f(0.005, 0.05), // very flat
        1 => rng.f(5.0, 20.0),   // very long
        2 => rng.quant(0.25, 3.0, 0.25),
        _ => rng.f(0.05, 4.0),
    }
}

/// `parallel`: 0 free, 1 exactly parallel (or anti-parallel), 2 within the 0.001 cutoff.
fn gen_case(rng: &mut Rng, parallel: u32) -> Case {
    let ra = radius(rng);
    let rb = if rng.chance(0.15) { ra } else { radius(rng) };
    let ha = half_height(rng);
    let hb = if rng.chance(0.15) {
        ha
    } else {
        half_height(rng)
    };
    let quant = rng.chance(0.3);
    let ca = if quant {
        quantv(rng, -10.0, 10.0, 0.5)
    } else {
        rng.vec(-20.0, 20.0)
    };
    let axa = rng.unit_or_axis(0.4);
    let axb = match parallel {
        1 => {
            if rng.chance(0.5) {
                axa
            } else {
                Vec3::new(-axa.x, -axa.y, -axa.z)
            }
        }
        2 => {
            let q = perp(rng, axa);
            let e = rng.f(-0.0009, 0.0009);
            let v = add(axa, q, e);
            let inv = 1.0 / (v.x * v.x + v.y * v.y + v.z * v.z).sqrt();
            Vec3::new(v.x * inv, v.y * inv, v.z * inv)
        }
        _ => match rng.below(6) {
            0 => perp(rng, axa),
            1 => {
                let q = perp(rng, axa);
                let e = rng.f(-0.05, 0.05);
                let v = add(axa, q, e);
                let inv = 1.0 / (v.x * v.x + v.y * v.y + v.z * v.z).sqrt();
                Vec3::new(v.x * inv, v.y * inv, v.z * inv)
            }
            2 => axa,
            3 => rng.unit_or_axis(0.8),
            _ => rng.unit(),
        },
    };
    // relative placement
    let reach = (ra + rb + ha + hb)
        * if rng.chance(0.7) {
            rng.f(0.0, 0.8)
        } else {
            rng.f(0.0, 1.3)
        };
    let off = match rng.below(6) {
        0 => add(Vec3::ZERO, axa, rng.f(-1.2, 1.2) * (ha + hb)), // coaxial / cap to cap
        1 => {
            let q = perp(rng, axa);
            let side = (ra + rb) * rng.f(0.0, 1.2);
            add(add(Vec3::ZERO, q, side), axa, rng.f(-1.0, 1.0) * (ha + hb))
        } // side to side / side to cap
        2 => quantv(rng, -3.0, 3.0, 0.25),
        3 => Vec3::ZERO,
        _ => add(Vec3::ZERO, rng.unit_or_axis(0.3), reach),
    };
    let cb = add(ca, off, 1.0);
    let mut p = UnprojParams::default();
    match rng.below(5) {
        0 | 1 => {} // least-penetration direction
        2 => p.dir = rng.unit_or_axis(0.4),
        3 => {
            let l = (off.x * off.x + off.y * off.y + off.z * off.z).sqrt();
            p.dir = if l > 1e-3 {
                Vec3::new(-off.x / l, -off.y / l, -off.z / l)
            } else {
                rng.unit()
            };
        }
        _ => {
            let u = rng.unit();
            let s = rng.f(0.3, 4.0);
            p.dir = Vec3::new(u.x * s, u.y * s, u.z * s);
        }
    }
    p.tmax = match rng.below(6) {
        0 => rng.f(0.0, 1.0),
        1 | 2 => 1e10,
        _ => rng.f(0.0, 12.0),
    };
    p.tmin = 0.0;
    p.min_pt_dist = rng.f(0.001, 0.05);
    p.max_cos = if rng.chance(0.5) { 0.1 } else { 0.9998 };
    p.flag = i32::from(rng.chance(0.5));
    let a = Primitive::Cylinder(CylinderPrim {
        center: ca,
        axis: axa,
        r: ra,
        hh: ha,
    });
    let b = Primitive::Cylinder(CylinderPrim {
        center: cb,
        axis: axb,
        r: rb,
        hh: hb,
    });
    let mut c = Case::new(0, &a, &b, &p);
    let (fa, fb) = match rng.below(12) {
        0 => (0x43, -1),
        1 => (-1, 0x43),
        2 => (0x43, 0x43),
        3 => (0x40, 0x41),
        _ => (-1, -1),
    };
    c = c.with_features(fa, fb);
    match rng.below(4) {
        0 => c.with_area(256, 0.01),
        1 => c.with_area(1 + rng.below(4), 0.01),
        _ => c,
    }
}

fn gen_nopoly(rng: &mut Rng) -> Case {
    if rng.chance(0.5) {
        let par = 1 + rng.below(2);
        gen_case(rng, par)
    } else {
        let mut c = gen_case(rng, 0);
        // force a 0x43 feature so phase 4 is skipped
        match rng.below(3) {
            0 => c.fa = 0x43,
            1 => c.fb = 0x43,
            _ => {
                c.fa = 0x43;
                c.fb = 0x43;
            }
        }
        c
    }
}

fn gen_lin(rng: &mut Rng) -> Case {
    let par = match rng.below(10) {
        0 => 1,
        1 => 2,
        _ => 0,
    };
    gen_case(rng, par)
}

fn axis(rng: &mut Rng) -> Vec3 {
    let s = if rng.chance(0.5) { 1.0 } else { -1.0 };
    match rng.below(3) {
        0 => Vec3::new(s, 0.0, 0.0),
        1 => Vec3::new(0.0, s, 0.0),
        _ => Vec3::new(0.0, 0.0, s),
    }
}

/// Axis-aligned, quantised and degenerate configurations (exact zeros in the dot / cross
/// products, coincident centres, zero sizes or axes).
fn gen_degen(rng: &mut Rng) -> Case {
    let prim = |rng: &mut Rng, c: Vec3| {
        let ax = match rng.below(12) {
            0 => Vec3::ZERO,
            1 => rng.unit(),
            _ => axis(rng),
        };
        let r = match rng.below(10) {
            0 => 0.0,
            _ => rng.quant(0.25, 2.0, 0.25),
        };
        let hh = match rng.below(10) {
            0 => 0.0,
            _ => rng.quant(0.25, 3.0, 0.25),
        };
        CylinderPrim {
            center: c,
            axis: ax,
            r,
            hh,
        }
    };
    let ca = quantv(rng, -2.0, 2.0, 0.25);
    let cb = if rng.chance(0.2) {
        ca
    } else {
        add(ca, quantv(rng, -3.0, 3.0, 0.25), 1.0)
    };
    let a = prim(rng, ca);
    let b = prim(rng, cb);
    let mut p = UnprojParams::default();
    match rng.below(4) {
        0 | 1 => {}
        2 => p.dir = axis(rng),
        _ => {
            let v = quantv(rng, -1.0, 1.0, 0.5);
            p.dir = Vec3::new(v.x * 2.0, v.y * 2.0, v.z * 2.0);
        }
    }
    p.tmax = if rng.chance(0.5) {
        1e10
    } else {
        rng.quant(0.0, 8.0, 0.5)
    };
    p.min_pt_dist = 0.01;
    p.max_cos = if rng.chance(0.5) { 0.1 } else { 0.9998 };
    let c = Case::new(0, &Primitive::Cylinder(a), &Primitive::Cylinder(b), &p);
    let c = match rng.below(10) {
        0 => c.with_features(0x43, -1),
        1 => c.with_features(-1, 0x43),
        _ => c,
    };
    match rng.below(3) {
        0 => c.with_area(256, 0.01),
        1 => c.with_area(1 + rng.below(4), 0.01),
        _ => c,
    }
}

#[test]
fn cyl_cyl_nopoly_matches_dll() {
    check("p2b_cyl_cyl_nopoly", 300, &gen_nopoly);
}

#[test]
fn cyl_cyl_linear_matches_dll() {
    check("p2b_cyl_cyl_lin", 300, &gen_lin);
}

#[test]
fn cyl_cyl_degenerate_matches_dll() {
    check("p2b_cyl_cyl_degen", 300, &gen_degen);
}

/// The quarter-wave table `dword_3537B450` (built at DLL load by sub_351E5900) against the port.
#[test]
fn sin_table_matches_dll() {
    use aaemu_physics::collision::swept::cyl_cyl::sin_table;
    use aaemu_physics::collision::swept::probe::oracle_raw;
    let Some(exe) = std::env::var_os("AAEMU_PHYS_ORACLE") else {
        return;
    };
    let ans = oracle_raw(std::path::Path::new(&exe), &["CCSIN".to_string()]).unwrap();
    let words: Vec<u32> = ans[0]
        .split_whitespace()
        .skip(1)
        .map(|w| u32::from_str_radix(w, 16).unwrap())
        .collect();
    let port: Vec<u32> = sin_table().iter().map(|v| v.to_bits()).collect();
    assert_eq!(words, port);
}

/// Distribution of results over `AAEMU_PHYS_P2B_LIVE` (default 2000) oracle cases per generator
/// (`cargo test --test p2b_cyl_cyl -- --ignored --nocapture`): ret, feature pair, area size.
#[test]
#[ignore = "statistics only; needs the oracle"]
fn cyl_cyl_stats() {
    use aaemu_physics::collision::swept::probe::{oracle_cases, seed_of};
    use std::collections::BTreeMap;
    let Some(exe) = std::env::var_os("AAEMU_PHYS_ORACLE") else {
        return;
    };
    let n: usize = std::env::var("AAEMU_PHYS_P2B_LIVE")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(2000);
    for (name, gen) in [
        ("nopoly", gen_nopoly as fn(&mut Rng) -> Case),
        ("lin", gen_lin),
        ("degen", gen_degen),
    ] {
        let mut rng = Rng::new(seed_of(name) ^ 0x1234);
        let cases: Vec<Case> = (0..n).map(|_| gen(&mut rng)).collect();
        let ans = oracle_cases(std::path::Path::new(&exe), &cases).unwrap();
        let mut hist: BTreeMap<String, usize> = BTreeMap::new();
        for a in &ans {
            let w: Vec<&str> = a.split_whitespace().collect();
            let r = w.iter().position(|x| *x == "R").unwrap();
            let key = if w[1] == "00000000" {
                "ret0".to_string()
            } else {
                let npt = w
                    .iter()
                    .position(|x| *x == "AREA")
                    .map_or(-1, |i| i64::from_str_radix(w[i + 2], 16).unwrap());
                format!("{}/{} npt {}", &w[r + 11][6..], &w[r + 12][6..], npt.min(9))
            };
            *hist.entry(key).or_default() += 1;
        }
        println!("{name}: {hist:#?}");
    }
}
