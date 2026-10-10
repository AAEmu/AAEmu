//! P2a-1: the box pair functions against the real DLL.
//!
//! Each golden `tests/golden/p2a1_<pair>.trace` is the output of
//! `physoracle --math p2a1_<pair> <count> <seed>` (oracle/probe_p2a1.c): generated primitives (random
//! rotations from the DLL's own quat→matrix, axis permutations, quantised "touching" layouts,
//! degenerate sizes / directions) handed straight to the INTERSECT / OVERLAP cell, with every
//! output word printed. The port must reproduce every line bit for bit, including the fields a
//! function leaves untouched (pre-filled with a sentinel) and the border polyline.
//!
//! With `AAEMU_PHYS_ORACLE` set the goldens are regenerated and compared first (bless with
//! `AAEMU_PHYS_BLESS=1`).

use aaemu_physics::collision::contact::PrimInters;
use aaemu_physics::collision::pairs::box_pairs as bp;
use aaemu_physics::collision::pairs::PairTables;
use aaemu_physics::geometry::primitives::{
    BoxPrim, CylinderPrim, HeightfieldPrim, Primitive, RayPrim, SpherePrim, Triangle,
};
use aaemu_physics::ids::GeomType;
use aaemu_physics::math::{Mat33, Vec3};
use aaemu_physics::oracle;
use std::fmt::Write as _;

const SENT: u32 = 0x7FC0_DEAD;
/// Cases per golden: the primary cells get 1000, the swap wrappers (same generator, mirrored
/// call) 250, to keep the checked-in data small; the oracle-only stress pass adds 50,000 per
/// cell when the DLL is available.
fn count_for(name: &str) -> usize {
    const WRAPPERS: [&str; 8] = [
        "p2a1_ray_box",
        "p2a1_sph_box",
        "p2a1_cyl_box",
        "p2a1_cap_box",
        "p2a1_ov_ray_box",
        "p2a1_ov_sph_box",
        "p2a1_ov_tri_box",
        "p2a1_ov_hf_box",
    ];
    if WRAPPERS.contains(&name) {
        250
    } else {
        1000
    }
}
const SEED: u64 = 0x2A1;

fn golden(name: &str) -> String {
    let path = oracle::golden_dir().join(format!("{name}.trace"));
    let stored = std::fs::read_to_string(&path)
        .ok()
        .map(|s| s.replace("\r\n", "\n"));
    if let Some(exe) = oracle::oracle_path() {
        let fresh = oracle::run_math(&exe, name, count_for(name), SEED).expect("oracle run");
        let bless = std::env::var_os(oracle::BLESS_ENV).is_some_and(|v| v != "0");
        match &stored {
            Some(s) if *s == fresh => {}
            _ if bless || stored.is_none() => {
                std::fs::write(&path, &fresh).expect("write golden");
            }
            _ => panic!(
                "{} differs from the oracle; set {}=1 to rewrite",
                path.display(),
                oracle::BLESS_ENV
            ),
        }
        return fresh;
    }
    stored.unwrap_or_else(|| panic!("golden {} missing", path.display()))
}

fn hexw(t: &str) -> u32 {
    u32::from_str_radix(t, 16).unwrap()
}
fn f(w: u32) -> f32 {
    f32::from_bits(w)
}
fn v3(w: &[u32]) -> Vec3 {
    Vec3::new(f(w[0]), f(w[1]), f(w[2]))
}

fn boxp(w: &[u32]) -> BoxPrim {
    BoxPrim {
        basis: Mat33::from_array(std::array::from_fn(|i| f(w[i]))),
        oriented: w[9] as i32,
        center: v3(&w[10..]),
        size: v3(&w[13..]),
    }
}
fn ray(w: &[u32]) -> RayPrim {
    RayPrim {
        origin: v3(w),
        dir: v3(&w[3..]),
    }
}
fn sph(w: &[u32]) -> SpherePrim {
    SpherePrim {
        center: v3(w),
        r: f(w[3]),
    }
}
fn cyl(w: &[u32]) -> CylinderPrim {
    CylinderPrim {
        center: v3(w),
        axis: v3(&w[3..]),
        r: f(w[6]),
        hh: f(w[7]),
    }
}
fn tri(w: &[u32]) -> Triangle {
    Triangle {
        pt: [v3(w), v3(&w[3..]), v3(&w[6..])],
        n: v3(&w[9..]),
    }
}

/// The oracle's heightfield callbacks (oracle/probe_p2a1.c), same formulas.
fn hf_height(ix: i32, iy: i32) -> f32 {
    (ix.wrapping_mul(7).wrapping_add(iy.wrapping_mul(13)) & 15) as f32 * 0.25 - 1.0
}
fn hf_surf(ix: i32, iy: i32) -> u16 {
    if (ix + 3 * iy) % 11 == 0 {
        0x8000
    } else {
        0
    }
}
fn hfp(w: &[u32]) -> HeightfieldPrim {
    HeightfieldPrim {
        basis: Mat33::from_array(std::array::from_fn(|i| f(w[i]))),
        oriented: w[9] as i32,
        origin: v3(&w[10..]),
        step: [f(w[13]), f(w[14])],
        stepr: [f(w[15]), f(w[16])],
        size: [w[17] as i32, w[18] as i32],
        stride: [w[19] as i32, w[20] as i32],
        height_scale: f(w[21]),
        type_mask: (w[22] & 0xFFFF) as u16,
        height_mask: (w[22] >> 16) as u16,
        type_hole: w[23] as i32,
        type_power: w[24] as i32,
        get_height: Some(hf_height),
        get_surf_type: Some(hf_surf),
    }
}

fn sentinel_inters() -> PrimInters {
    let s = f(SENT);
    let sv = Vec3::splat(s);
    let b = SENT.to_le_bytes();
    PrimInters {
        pt: [sv, sv],
        n: sv,
        i_feature: [[b[0], b[1]], [b[2], b[3]]],
        tolerance: s,
        id: [(SENT & 0xFFFF) as u16 as i16, (SENT >> 16) as u16 as i16],
        i_node: [SENT as i32, SENT as i32],
        border: vec![sv],
        n_alt: sv,
        unk_84: SENT as i32,
    }
}

fn fmt_out(ret: bool, o: &PrimInters) -> String {
    let mut s = String::new();
    let mut w = |x: u32| {
        let _ = write!(s, " {x:08x}");
    };
    w(u32::from(ret));
    for v in [o.pt[0], o.pt[1], o.n] {
        for b in v.to_bits() {
            w(b);
        }
    }
    let fb = o.i_feature;
    w(u32::from_le_bytes([fb[0][0], fb[0][1], fb[1][0], fb[1][1]]));
    w(o.tolerance.to_bits());
    w(u32::from(o.id[0] as u16) | (u32::from(o.id[1] as u16) << 16));
    w(o.i_node[0] as u32);
    w(o.i_node[1] as u32);
    let untouched = o.border.len() == 1 && o.border[0].to_bits() == [SENT; 3];
    let count = if untouched {
        SENT
    } else {
        o.border.len() as u32
    };
    w(count);
    w(SENT);
    for b in o.n_alt.to_bits() {
        w(b);
    }
    w(o.unk_84 as u32);
    if count <= 64 {
        let _ = write!(s, " b {count}");
        for p in &o.border {
            for b in p.to_bits() {
                let _ = write!(s, " {b:08x}");
            }
        }
    }
    s
}

/// Run every case of a golden through `run` (which returns the formatted ` out …` tail) and
/// require bit equality on every line.
fn check(name: &str, nb: usize, run: impl Fn(&[u32], &[u32]) -> String) {
    check_text(name, &golden(name), count_for(name), nb, &run);
    // extra seeds straight from the DLL (no golden) when the oracle is available
    if let Some(exe) = oracle::oracle_path() {
        for seed in [7u64, 0xB0C5] {
            let text = oracle::run_math(&exe, name, STRESS, seed).expect("oracle run");
            check_text(name, &text, STRESS, nb, &run);
        }
    }
}

/// Extra cases per seed in the oracle-only stress pass.
const STRESS: usize = 25_000;

fn check_text(
    name: &str,
    text: &str,
    count: usize,
    nb: usize,
    run: &dyn Fn(&[u32], &[u32]) -> String,
) {
    let mut lines = text.lines();
    let head = lines.next().unwrap();
    assert!(head.starts_with("physoracle-math 1 "), "{head}");
    let mut n = 0;
    let mut bad = Vec::new();
    let mut hits = 0;
    for line in lines {
        let (inp, outp) = line.split_once(" out").expect("out");
        let words: Vec<u32> = inp.split_whitespace().skip(1).map(hexw).collect();
        assert_eq!(words.len(), 16 + nb, "{name}: input width");
        let got = run(&words[..16], &words[16..]);
        if outp.split_whitespace().next() == Some("00000001") {
            hits += 1;
        }
        if got != format!(" out{outp}") && got != outp {
            if bad.len() < 5 {
                bad.push(format!("case {n}\n  in{inp}\n  dll:{outp}\n  rust:{got}"));
            } else {
                bad.push(String::new());
            }
        }
        n += 1;
    }
    assert_eq!(n, count, "{name}: case count");
    assert!(hits > count / 10, "{name}: only {hits} hits");
    assert!(
        bad.is_empty(),
        "{name}: {} of {n} cases differ\n{}",
        bad.len(),
        bad.iter()
            .filter(|s| !s.is_empty())
            .cloned()
            .collect::<Vec<_>>()
            .join("\n")
    );
}

fn inter(name: &str, nb: usize, f: impl Fn(&[u32], &[u32], &mut PrimInters) -> bool) {
    check(name, nb, |a, b| {
        let mut o = sentinel_inters();
        let r = f(a, b, &mut o);
        format!(" out{}", fmt_out(r, &o))
    });
}

fn over(name: &str, nb: usize, f: impl Fn(&[u32], &[u32]) -> bool) {
    check(name, nb, |a, b| format!(" out {:08x}", u32::from(f(a, b))));
}

#[test]
fn box_sphere() {
    inter("p2a1_box_sph", 4, |a, b, o| {
        bp::intersect_box_sphere(&boxp(a), &sph(b), o)
    });
    inter("p2a1_sph_box", 4, |a, b, o| {
        bp::intersect_sphere_box(&sph(b), &boxp(a), o)
    });
}

#[test]
fn box_ray() {
    inter("p2a1_box_ray", 6, |a, b, o| {
        bp::intersect_box_ray(&boxp(a), &ray(b), o)
    });
    inter("p2a1_ray_box", 6, |a, b, o| {
        bp::intersect_ray_box(&ray(b), &boxp(a), o)
    });
}

#[test]
fn box_box() {
    inter("p2a1_box_box", 16, |a, b, o| {
        bp::intersect_box_box(&boxp(a), &boxp(b), o)
    });
}

#[test]
fn box_cylinder() {
    inter("p2a1_box_cyl", 8, |a, b, o| {
        bp::intersect_box_cylinder(&boxp(a), &cyl(b), o)
    });
    inter("p2a1_cyl_box", 8, |a, b, o| {
        bp::intersect_cylinder_box(&cyl(b), &boxp(a), o)
    });
}

#[test]
fn box_capsule() {
    inter("p2a1_box_cap", 8, |a, b, o| {
        bp::intersect_box_capsule(&boxp(a), &cyl(b), o)
    });
    inter("p2a1_cap_box", 8, |a, b, o| {
        bp::intersect_capsule_box(&cyl(b), &boxp(a), o)
    });
}

#[test]
fn overlaps() {
    over("p2a1_ov_box_box", 16, |a, b| {
        bp::overlap_box_box(&boxp(a), &boxp(b))
    });
    over("p2a1_ov_box_sph", 4, |a, b| {
        bp::overlap_box_sphere(&boxp(a), &sph(b))
    });
    over("p2a1_ov_box_ray", 6, |a, b| {
        bp::overlap_box_ray(&boxp(a), &ray(b))
    });
    over("p2a1_ov_box_tri", 12, |a, b| {
        bp::overlap_box_tri(&boxp(a), &tri(b))
    });
    over("p2a1_ov_box_hf", 26, |a, b| {
        bp::overlap_box_heightfield(&boxp(a), &hfp(b))
    });
}

#[test]
fn overlap_wrappers_via_table() {
    let t = PairTables::build();
    over("p2a1_ov_ray_box", 6, |a, b| {
        (t.overlap_fn(GeomType::Ray, GeomType::Box))(
            &Primitive::Ray(ray(b)),
            &Primitive::Box(boxp(a)),
        )
    });
    over("p2a1_ov_sph_box", 4, |a, b| {
        (t.overlap_fn(GeomType::Sphere, GeomType::Box))(
            &Primitive::Sphere(sph(b)),
            &Primitive::Box(boxp(a)),
        )
    });
    over("p2a1_ov_tri_box", 12, |a, b| {
        (t.overlap_fn(GeomType::TriMesh, GeomType::Box))(
            &Primitive::Triangle(tri(b)),
            &Primitive::Box(boxp(a)),
        )
    });
    over("p2a1_ov_hf_box", 26, |a, b| {
        (t.overlap_fn(GeomType::Heightfield, GeomType::Box))(
            &Primitive::Heightfield(hfp(b)),
            &Primitive::Box(boxp(a)),
        )
    });
}
