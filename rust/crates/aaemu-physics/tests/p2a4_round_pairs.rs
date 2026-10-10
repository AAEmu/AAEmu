//! P2a-4 (round + ray pairs) checked bit-for-bit against the pair functions executed inside the
//! real 10.x DLL (`physoracle --math p2a4_<pair>`, goldens `tests/golden/math_p2a4_*.trace`;
//! regenerated and verified when `AAEMU_PHYS_ORACLE` is set). With the oracle, a much larger
//! ad-hoc sample of every pair is compared as well (`*_exhaustive`, no golden).
//!
//! Every row carries the generated primitives and the complete observable output of the DLL
//! function: return value, `prim_inters` words 0..13 (pre-filled with 0xCD bytes, so fields the
//! function leaves alone are visible), `nborderpt` (pre-filled with 0xCDCDCDCD) and the border
//! points.

use aaemu_physics::collision::contact::PrimInters;
use aaemu_physics::collision::pairs::round_pairs::{
    overlap_hf_sph, overlap_sph_hf, overlap_sph_sph, sin_table,
};
use aaemu_physics::collision::pairs::PairTables;
use aaemu_physics::geometry::primitives::{
    CylinderPrim, HeightfieldPrim, Primitive, RayPrim, SpherePrim,
};
use aaemu_physics::ids::GeomType;
use aaemu_physics::math::{Mat33, Vec3};
use aaemu_physics::oracle::{math_trace, oracle_path, run_math, scenario_trace};
use aaemu_physics::trace::{f, MathTrace};
use std::cell::Cell;

const FILL: u32 = 0xCDCD_CDCD;

/// Golden rows per pair (kept small: the goldens are checked in).
const GOLDEN_ROWS: usize = 400;
/// Rows per pair of the oracle-only comparison.
const EXHAUSTIVE_ROWS: usize = 40_000;

fn v3(w: &[u64]) -> Vec3 {
    Vec3::new(f(w[0]), f(w[1]), f(w[2]))
}

/// Parse one primitive of `ty` from the row's input words; returns it and the words consumed.
fn prim(ty: GeomType, w: &[u64]) -> (Primitive, usize) {
    match ty {
        GeomType::Sphere => (
            Primitive::Sphere(SpherePrim {
                center: v3(w),
                r: f(w[3]),
            }),
            4,
        ),
        GeomType::Cylinder | GeomType::Capsule => {
            let c = CylinderPrim {
                center: v3(w),
                axis: v3(&w[3..]),
                r: f(w[6]),
                hh: f(w[7]),
            };
            let p = if ty == GeomType::Cylinder {
                Primitive::Cylinder(c)
            } else {
                Primitive::Capsule(c)
            };
            (p, 8)
        }
        GeomType::Ray => (
            Primitive::Ray(RayPrim {
                origin: v3(w),
                dir: v3(&w[3..]),
            }),
            6,
        ),
        _ => unreachable!("no generator for {ty:?}"),
    }
}

/// The output struct as the probe pre-fills it.
fn sentinel_out() -> PrimInters {
    let fv = f32::from_bits(FILL);
    let v = Vec3::splat(fv);
    PrimInters {
        pt: [v, v],
        n: v,
        i_feature: [[0xCD; 2]; 2],
        tolerance: fv,
        id: [0xCDCDu16 as i16; 2],
        i_node: [FILL as i32; 2],
        // A marker element: functions that use the border clear it first (the binary zeroes
        // nborderpt), the others leave it alone (nborderpt stays 0xCDCDCDCD).
        border: vec![v],
        border_max: 64, // the probe sets nbordersz = 64
        n_alt: v,
        unk_84: FILL as i32,
    }
}

/// The observable words of a `PrimInters` in the probe's output layout.
fn out_words(r: bool, o: &PrimInters) -> Vec<u32> {
    let mut w = vec![u32::from(r)];
    for p in [o.pt[0], o.pt[1], o.n] {
        w.extend(p.to_bits());
    }
    let fb = o.i_feature;
    w.push(u32::from_le_bytes([fb[0][0], fb[0][1], fb[1][0], fb[1][1]]));
    w.push(o.tolerance.to_bits());
    w.push(u32::from(o.id[0] as u16) | (u32::from(o.id[1] as u16) << 16));
    w.push(o.i_node[0] as u32);
    w.push(o.i_node[1] as u32);
    let untouched = o.border.len() == 1 && o.border[0].to_bits() == [FILL; 3];
    if untouched {
        w.push(FILL);
    } else {
        w.push(o.border.len() as u32);
        for p in &o.border {
            w.extend(p.to_bits());
        }
    }
    w
}

fn pair_types(name: &str) -> (GeomType, GeomType) {
    let t = |s: &str| match s {
        "sph" => GeomType::Sphere,
        "cyl" => GeomType::Cylinder,
        "cap" => GeomType::Capsule,
        "ray" => GeomType::Ray,
        "hf" => GeomType::Heightfield,
        _ => panic!("bad pair name {name}"),
    };
    let parts: Vec<&str> = name.split('_').collect();
    let n = parts.len();
    (t(parts[n - 2]), t(parts[n - 1]))
}

/// Compare every row of an INTERSECT probe trace with the port (through the pair table).
fn check_intersect(name: &str, tr: &MathTrace) -> usize {
    let (ta, tb) = pair_types(name);
    let tables = PairTables::build();
    let f = tables.intersect_fn(ta, tb);
    let mut hits = 0;
    for (i, (inp, out)) in tr.rows.iter().enumerate() {
        let (a, na) = prim(ta, inp);
        let (b, nb) = prim(tb, &inp[na..]);
        assert_eq!(na + nb, inp.len(), "{name} row {i}: input word count");
        let mut o = sentinel_out();
        let r = f(&a, &b, &mut o);
        hits += usize::from(r);
        let got = out_words(r, &o);
        let want: Vec<u32> = out.iter().map(|w| *w as u32).collect();
        assert_eq!(
            got, want,
            "{name} row {i}: in {:x?}\n  port   {got:08x?}\n  oracle {want:08x?}",
            inp
        );
    }
    hits
}

thread_local! {
    static HF_FN: Cell<(f32, f32)> = const { Cell::new((0.0, 0.0)) };
}
/// The oracle's "bumps" height function: `h0 + amp · ((7·ix + 13·iy) mod 5)` (single).
fn bumps(ix: i32, iy: i32) -> f32 {
    let (h0, amp) = HF_FN.with(Cell::get);
    h0 + amp * (((ix * 7 + iy * 13) % 5) as f32)
}

fn check_overlap(name: &str, tr: &MathTrace) -> usize {
    let mut yes = 0;
    for (i, (inp, out)) in tr.rows.iter().enumerate() {
        let want = out[0] as u32;
        let got = if name.contains("_hf") {
            let w = inp;
            let mut m = [[0.0f32; 3]; 3];
            for (k, x) in w[..9].iter().enumerate() {
                m[k / 3][k % 3] = f(*x);
            }
            let hf = HeightfieldPrim {
                basis: Mat33 { m },
                origin: v3(&w[9..]),
                step: [f(w[12]), f(w[13])],
                stepr: [f(w[14]), f(w[15])],
                size: [w[16] as i32, w[17] as i32],
                height_scale: f(w[18]),
                get_height: Some(bumps),
                ..HeightfieldPrim::default()
            };
            HF_FN.with(|c| c.set((f(w[19]), f(w[20]))));
            let s = SpherePrim {
                center: v3(&w[21..]),
                r: f(w[24]),
            };
            if name.ends_with("hf_sph") {
                overlap_hf_sph(&hf, &s)
            } else {
                overlap_sph_hf(&s, &hf)
            }
        } else {
            let a = SpherePrim {
                center: v3(inp),
                r: f(inp[3]),
            };
            let b = SpherePrim {
                center: v3(&inp[4..]),
                r: f(inp[7]),
            };
            overlap_sph_sph(&a, &b)
        };
        yes += usize::from(got);
        assert_eq!(u32::from(got), want, "{name} row {i}: in {inp:x?}");
    }
    yes
}

const INTERSECT: [&str; 15] = [
    "p2a4_sph_sph",
    "p2a4_sph_ray",
    "p2a4_ray_sph",
    "p2a4_cyl_sph",
    "p2a4_sph_cyl",
    "p2a4_cyl_ray",
    "p2a4_ray_cyl",
    "p2a4_cyl_cyl",
    "p2a4_cyl_cap",
    "p2a4_cap_cyl",
    "p2a4_cap_sph",
    "p2a4_sph_cap",
    "p2a4_cap_ray",
    "p2a4_ray_cap",
    "p2a4_cap_cap",
];
const OVERLAP: [&str; 3] = ["p2a4_ovl_sph_sph", "p2a4_ovl_hf_sph", "p2a4_ovl_sph_hf"];

fn golden(name: &str, seed: u64) -> MathTrace {
    let t = math_trace(name, GOLDEN_ROWS, seed).unwrap_or_else(|e| panic!("{e}"));
    assert_eq!(t.rows.len(), GOLDEN_ROWS, "{name}: row count");
    t
}

#[test]
fn intersect_cells_match_the_dll() {
    for (k, name) in INTERSECT.iter().enumerate() {
        let hits = check_intersect(name, &golden(name, 100 + k as u64));
        // the generator must exercise both outcomes
        assert!(hits > 0 && hits < GOLDEN_ROWS, "{name}: {hits} hits");
    }
}

#[test]
fn overlap_cells_match_the_dll() {
    for (k, name) in OVERLAP.iter().enumerate() {
        let yes = check_overlap(name, &golden(name, 200 + k as u64));
        assert!(yes > 0 && yes < GOLDEN_ROWS, "{name}: {yes} overlaps");
    }
}

/// Large random sample of every cell (oracle only; skipped without `AAEMU_PHYS_ORACLE`).
#[test]
fn pairs_exhaustive_against_the_dll() {
    let Some(exe) = oracle_path() else {
        eprintln!("AAEMU_PHYS_ORACLE not set: skipping the exhaustive pair comparison");
        return;
    };
    for (k, name) in INTERSECT.iter().chain(OVERLAP.iter()).enumerate() {
        let text = run_math(&exe, name, EXHAUSTIVE_ROWS, 9000 + k as u64).unwrap();
        let tr = MathTrace::parse(&text).unwrap();
        assert_eq!(tr.rows.len(), EXHAUSTIVE_ROWS);
        if name.contains("_ovl_") {
            check_overlap(name, &tr);
        } else {
            check_intersect(name, &tr);
        }
    }
}

/// The 1025-entry sine table the cylinder pairs search equals the DLL's (read from the image
/// after its static initialiser ran).
#[test]
fn sine_table_matches_the_dll_image() {
    let t = scenario_trace("p2a4_sintab").unwrap_or_else(|e| panic!("{e}"));
    let words: Vec<u32> = t
        .other_line("peekdll")
        .expect("peekdll line")
        .iter()
        .skip(2)
        .map(|s| u32::from_str_radix(s, 16).unwrap())
        .collect();
    let tab = sin_table();
    assert_eq!(words.len(), tab.len());
    for (i, (w, v)) in words.iter().zip(tab.iter()).enumerate() {
        assert_eq!(*w, v.to_bits(), "sin table entry {i}");
    }
}
