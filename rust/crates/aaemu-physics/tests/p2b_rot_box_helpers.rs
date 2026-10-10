//! P2b `rot_box`: cross-check of the shared helpers in `swept/common.rs` that no cell test
//! exercises directly, against the routines inside the DLL (probe commands in
//! `oracle/p2b_ext_rot_box.c`):
//!
//! | DLL | port |
//! |---|---|
//! | sub_35020EA0 | `common::rotate_vec` |
//! | sub_3519A670 | `common::rotate_point` |
//! | sub_35021020 | `common::normalize_in_place` |
//! | sub_350210A0 | `common::normalized` |
//! | sub_35022380 | `common::mat_mul` |
//!
//! Without the oracle the stored golden `tests/golden/p2b_rot_box_helpers.trace` is replayed; with
//! `AAEMU_PHYS_ORACLE` it is regenerated (and must match, or `AAEMU_PHYS_BLESS=1` rewrites it);
//! `AAEMU_PHYS_P2B_LIVE=<n>` adds `n` fresh oracle-only cases per helper.
//!
//! Inputs are NaN-free (zeros of both signs, denormals, values around the 1e-20 threshold of the
//! normalisers, huge values that overflow, arbitrary finite bit patterns). NaN inputs are covered
//! separately in [`normalize_nan_input`], where the port is known to differ (see there).

use aaemu_physics::collision::swept::common::{
    mat_mul, normalize_in_place, normalized, rotate_point, rotate_vec,
};
use aaemu_physics::collision::swept::probe::{oracle_raw, seed_of, Rng, LIVE_ENV};
use aaemu_physics::math::Vec3;
use aaemu_physics::oracle::{golden_dir, oracle_path, BLESS_ENV};
use std::fmt::Write as _;

fn val(rng: &mut Rng) -> f32 {
    match rng.below(12) {
        0 => 0.0,
        1 => -0.0,
        2 => {
            // denormal
            let b = (rng.next_u64() as u32 & 0x007F_FFFF) | 1;
            f32::from_bits(b | if rng.chance(0.5) { 0x8000_0000 } else { 0 })
        }
        3 => rng.quant(-2.0, 2.0, 0.25),
        4 => rng.f(-1.0, 1.0) * 1e-10,
        5 => rng.f(-1.0, 1.0) * 1e18,
        6 => loop {
            // any finite bit pattern
            let f = f32::from_bits(rng.next_u64() as u32);
            if f.is_finite() {
                break f;
            }
        },
        7 => rng.f(-1.0, 1.0) * 1e-19,
        _ => rng.f(-10.0, 10.0),
    }
}

fn vec(rng: &mut Rng) -> Vec3 {
    if rng.chance(0.1) {
        let s = val(rng);
        let k = rng.below(3);
        Vec3::new(
            if k == 0 { s } else { 0.0 },
            if k == 1 { s } else { 0.0 },
            if k == 2 { s } else { 0.0 },
        )
    } else {
        Vec3::new(val(rng), val(rng), val(rng))
    }
}

/// Mostly unit axes (what the engine passes), sometimes anything.
fn axis(rng: &mut Rng) -> Vec3 {
    if rng.chance(0.8) {
        rng.unit_or_axis(0.3)
    } else {
        vec(rng)
    }
}

/// Mostly (cos, sin) of an angle in [−π, π], sometimes arbitrary values.
fn cos_sin(rng: &mut Rng) -> (f32, f32) {
    if rng.chance(0.8) {
        let a = rng.f(-std::f32::consts::PI, std::f32::consts::PI);
        (a.cos(), a.sin())
    } else {
        (val(rng), val(rng))
    }
}

/// Vectors for the normalisers: around the `|v|² ≤ 1e-20` threshold, plus the general mix.
fn norm_vec(rng: &mut Rng) -> Vec3 {
    match rng.below(4) {
        0 => {
            let s = 1e-10 * rng.f(0.5, 1.5);
            let u = rng.unit_or_axis(0.3);
            Vec3::new(u.x * s, u.y * s, u.z * s)
        }
        1 => Vec3::new(1e-10, 0.0, 0.0),
        _ => vec(rng),
    }
}

fn hex(s: &mut String, v: &[f32]) {
    for x in v {
        let _ = write!(s, " {:08x}", x.to_bits());
    }
}

fn floats(w: &[&str]) -> Vec<f32> {
    w.iter()
        .map(|t| f32::from_bits(u32::from_str_radix(t, 16).unwrap()))
        .collect()
}

const HELPERS: [&str; 5] = [
    "RBX_ROTV",
    "RBX_ROTP",
    "RBX_NORMI",
    "RBX_NORM",
    "RBX_MATMUL",
];

/// One probe command for a helper.
fn gen(helper: &str, rng: &mut Rng) -> String {
    let mut s = helper.to_string();
    match helper {
        "RBX_ROTV" => {
            let v = vec(rng);
            let a = axis(rng);
            let (c, sn) = cos_sin(rng);
            hex(&mut s, &[v.x, v.y, v.z, a.x, a.y, a.z, c, sn]);
        }
        "RBX_ROTP" => {
            let p = vec(rng);
            let ctr = if rng.chance(0.2) { p } else { vec(rng) };
            let a = axis(rng);
            let (c, sn) = cos_sin(rng);
            hex(
                &mut s,
                &[p.x, p.y, p.z, ctr.x, ctr.y, ctr.z, a.x, a.y, a.z, c, sn],
            );
        }
        "RBX_NORMI" | "RBX_NORM" => {
            let v = norm_vec(rng);
            hex(&mut s, &[v.x, v.y, v.z]);
        }
        "RBX_MATMUL" => {
            let mut m = [0.0f32; 18];
            if rng.chance(0.5) {
                let a = rng.rotation().to_array();
                let b = rng.rotation().to_array();
                m[..9].copy_from_slice(&a);
                m[9..].copy_from_slice(&b);
            } else {
                for x in &mut m {
                    *x = val(rng);
                }
            }
            hex(&mut s, &m);
        }
        _ => unreachable!(),
    }
    s
}

/// The port's answer to a probe command.
fn port(line: &str) -> String {
    let w: Vec<&str> = line.split_whitespace().collect();
    let f = floats(&w[1..]);
    let v3 = |i: usize| Vec3::new(f[i], f[i + 1], f[i + 2]);
    let mut s = "rbx".to_string();
    match w[0] {
        "RBX_ROTV" => {
            let r = rotate_vec(v3(0), v3(3), f[6], f[7]);
            hex(&mut s, &[r.x, r.y, r.z]);
        }
        "RBX_ROTP" => {
            let r = rotate_point(v3(0), v3(3), v3(6), f[9], f[10]);
            hex(&mut s, &[r.x, r.y, r.z]);
        }
        "RBX_NORMI" => {
            let mut v = v3(0);
            normalize_in_place(&mut v);
            hex(&mut s, &[v.x, v.y, v.z]);
        }
        "RBX_NORM" => {
            let v = v3(0);
            let r = normalized(v);
            hex(&mut s, &[r.x, r.y, r.z, v.x, v.y, v.z]);
        }
        "RBX_MATMUL" => {
            let a: [f32; 9] = f[..9].try_into().unwrap();
            let b: [f32; 9] = f[9..18].try_into().unwrap();
            hex(&mut s, &mat_mul(&a, &b));
        }
        _ => unreachable!(),
    }
    s
}

fn compare(recs: &[(String, String)]) -> String {
    let mut bad = 0;
    let mut rep = String::new();
    for (q, want) in recs {
        let got = port(q);
        if &got != want {
            bad += 1;
            if bad <= 5 {
                let _ = writeln!(rep, "  {q}\n    dll:  {want}\n    port: {got}");
            }
        }
    }
    if bad == 0 {
        String::new()
    } else {
        format!("{bad} of {} helper cases differ\n{rep}", recs.len())
    }
}

#[test]
fn common_helpers_match_dll() {
    const N: usize = 120;
    let golden = golden_dir().join("p2b_rot_box_helpers.trace");
    let mut lines = Vec::new();
    for h in HELPERS {
        let mut rng = Rng::new(seed_of(h));
        lines.extend((0..N).map(|_| gen(h, &mut rng)));
    }
    let recs: Vec<(String, String)> = if let Some(exe) = oracle_path() {
        let answers = oracle_raw(&exe, &lines).unwrap();
        let mut text = "p2b-golden 1 p2b_rot_box_helpers\n".to_string();
        for (q, a) in lines.iter().zip(&answers) {
            let _ = write!(text, "{q}\n= {a}\n");
        }
        let stored = std::fs::read_to_string(&golden)
            .ok()
            .map(|s| s.replace("\r\n", "\n"));
        if stored.as_deref() != Some(text.as_str()) {
            let bless = std::env::var_os(BLESS_ENV).is_some_and(|v| v != "0");
            assert!(
                bless || stored.is_none(),
                "{} differs from the oracle; set {BLESS_ENV}=1 to rewrite",
                golden.display()
            );
            std::fs::write(&golden, &text).unwrap();
        }
        let mut recs: Vec<(String, String)> = lines.into_iter().zip(answers).collect();
        let live: usize = std::env::var(LIVE_ENV)
            .ok()
            .and_then(|v| v.parse().ok())
            .unwrap_or(0);
        if live > 0 {
            let mut more = Vec::new();
            for h in HELPERS {
                let mut rng = Rng::new(seed_of(h) ^ 0x5DEE_CE66_D1CE_4E5B);
                more.extend((0..live).map(|_| gen(h, &mut rng)));
            }
            let answers = oracle_raw(&exe, &more).unwrap();
            recs.extend(more.into_iter().zip(answers));
        }
        recs
    } else {
        let text = std::fs::read_to_string(&golden)
            .unwrap_or_else(|e| panic!("golden {} missing: {e}", golden.display()))
            .replace("\r\n", "\n");
        let mut it = text.lines();
        assert!(it.next().is_some_and(|h| h.starts_with("p2b-golden 1")));
        let mut recs = Vec::new();
        while let Some(q) = it.next() {
            let a = it.next().unwrap().strip_prefix("= ").unwrap();
            recs.push((q.to_string(), a.to_string()));
        }
        recs
    };
    let rep = compare(&recs);
    assert!(rep.is_empty(), "{rep}");
}

/// NaN inputs to the normalisers: sub_35021020 / sub_350210A0 compare `|v|²` with `comiss` +
/// `jbe`, so an unordered (NaN) squared length takes the "too short" branch and yields (0, 0, 1).
/// `common::normalize_in_place` / `normalized` use `l2 <= 1e-20`, which is false for NaN, and
/// return NaNs instead. This test pins the DLL's behaviour (oracle only) and prints the port's.
#[test]
fn normalize_nan_input() {
    let Some(exe) = oracle_path() else { return };
    let nan = f32::NAN.to_bits();
    let lines = vec![
        format!("RBX_NORMI {nan:08x} 3f800000 00000000"),
        format!("RBX_NORM 00000000 {nan:08x} 00000000"),
    ];
    let answers = oracle_raw(&exe, &lines).unwrap();
    assert_eq!(answers[0], "rbx 00000000 00000000 3f800000");
    assert!(answers[1].starts_with("rbx 00000000 00000000 3f800000"));
    for (q, a) in lines.iter().zip(&answers) {
        println!("{q}\n  dll:  {a}\n  port: {}", port(q));
    }
}
