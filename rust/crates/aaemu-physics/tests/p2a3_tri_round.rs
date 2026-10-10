//! P2a-3 (triangle vs cylinder / capsule) checked bit for bit against the INTERSECT functions of
//! the real 10.x DLL, called directly by `physoracle --pairfn VA NA NB FILE`.
//!
//! * Goldens `tests/golden/p2a3_*.trace` hold oracle output for a branch-diverse case set (the
//!   inputs are part of each line), so CI checks the port without the DLL.
//! * With `AAEMU_PHYS_ORACLE` set, the goldens are regenerated from the DLL and must match
//!   byte for byte (`AAEMU_PHYS_BLESS=1` rewrites them), and `fuzz_*` tests compare the port
//!   with the DLL on a large random set (`AAEMU_P2A3_FUZZ=<cases>`, default 20000 per pair; `AAEMU_P2A3_SEED=<n>` varies
//!   the set).

use aaemu_physics::collision::contact::PrimInters;
use aaemu_physics::collision::pairs::tri_round_pairs::{
    capsule_tri, cylinder_tri, tri_capsule, tri_cylinder,
};
use aaemu_physics::collision::pairs::PairTables;
use aaemu_physics::geometry::primitives::{CylinderPrim, Primitive, Triangle};
use aaemu_physics::ids::GeomType;
use aaemu_physics::math::Vec3;
use aaemu_physics::oracle::{golden_dir, oracle_path, BLESS_ENV};
use std::collections::HashMap;
use std::path::Path;
use std::process::Command;

// ------------------------------------------------------------------ pair under test

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
enum Pair {
    TriCyl,
    TriCap,
    CylTri,
    CapTri,
}

impl Pair {
    fn va(self) -> &'static str {
        match self {
            Pair::TriCyl => "35164830",
            Pair::TriCap => "35168280",
            Pair::CylTri => "35168200",
            Pair::CapTri => "3516ad80",
        }
    }
    fn tri_first(self) -> bool {
        matches!(self, Pair::TriCyl | Pair::TriCap)
    }
    fn counts(self) -> (usize, usize) {
        if self.tri_first() {
            (12, 8)
        } else {
            (8, 12)
        }
    }
    fn name(self) -> &'static str {
        match self {
            Pair::TriCyl => "tri_cyl",
            Pair::TriCap => "tri_cap",
            Pair::CylTri => "cyl_tri",
            Pair::CapTri => "cap_tri",
        }
    }
    fn run(self, tri: &Triangle, shape: &CylinderPrim, out: &mut PrimInters) -> bool {
        match self {
            Pair::TriCyl => tri_cylinder(tri, shape, out),
            Pair::TriCap => tri_capsule(tri, shape, out),
            Pair::CylTri => cylinder_tri(shape, tri, out),
            Pair::CapTri => capsule_tri(shape, tri, out),
        }
    }
}

// ------------------------------------------------------------------ word conversions

fn f(w: u32) -> f32 {
    f32::from_bits(w)
}

fn tri_from(w: &[u32]) -> Triangle {
    let v = |i: usize| Vec3::new(f(w[i]), f(w[i + 1]), f(w[i + 2]));
    Triangle {
        pt: [v(0), v(3), v(6)],
        n: v(9),
    }
}

fn shape_from(w: &[u32]) -> CylinderPrim {
    CylinderPrim {
        center: Vec3::new(f(w[0]), f(w[1]), f(w[2])),
        axis: Vec3::new(f(w[3]), f(w[4]), f(w[5])),
        r: f(w[6]),
        hh: f(w[7]),
    }
}

fn v3(w: &[u32]) -> Vec3 {
    Vec3::new(f(w[0]), f(w[1]), f(w[2]))
}

const BORDER_FILL: u32 = 0x7fc0_dead;

/// prim_inters (22 dwords, see `physoracle.c` `pairfn_probe`) → PrimInters.
fn pi_from(w: &[u32]) -> PrimInters {
    let fb = w[9].to_le_bytes();
    let n0 = w[16] as i32;
    let fill = Vec3::new(f(BORDER_FILL), f(BORDER_FILL), f(BORDER_FILL));
    PrimInters {
        pt: [v3(&w[0..3]), v3(&w[3..6])],
        n: v3(&w[6..9]),
        i_feature: [[fb[0], fb[1]], [fb[2], fb[3]]],
        tolerance: f(w[10]),
        id: [w[11] as u16 as i16, (w[11] >> 16) as u16 as i16],
        i_node: [w[12] as i32, w[13] as i32],
        border: vec![fill; n0.clamp(0, 64) as usize],
        border_max: w[17] as i32,
        n_alt: v3(&w[18..21]),
        unk_84: w[21] as i32,
    }
}

fn bits3(v: Vec3) -> [u32; 3] {
    [v.x.to_bits(), v.y.to_bits(), v.z.to_bits()]
}

/// PrimInters → the oracle's output words (`<ret> <22 words> b <points>`).
fn pi_words(ret: bool, o: &PrimInters) -> Vec<String> {
    let mut w: Vec<u32> = Vec::with_capacity(22);
    w.extend(bits3(o.pt[0]));
    w.extend(bits3(o.pt[1]));
    w.extend(bits3(o.n));
    w.push(u32::from_le_bytes([
        o.i_feature[0][0],
        o.i_feature[0][1],
        o.i_feature[1][0],
        o.i_feature[1][1],
    ]));
    w.push(o.tolerance.to_bits());
    w.push(u32::from(o.id[0] as u16) | (u32::from(o.id[1] as u16) << 16));
    w.push(o.i_node[0] as u32);
    w.push(o.i_node[1] as u32);
    w.push(0);
    w.push(0);
    w.push(o.border.len() as u32);
    w.push(o.border_max as u32);
    w.extend(bits3(o.n_alt));
    w.push(o.unk_84 as u32);
    let mut s = vec![i32::from(ret).to_string()];
    s.extend(w.iter().map(|x| format!("{x:08x}")));
    s.push("b".into());
    for p in &o.border {
        s.extend(bits3(*p).iter().map(|x| format!("{x:08x}")));
    }
    s
}

/// Run the port on one input line (NA + NB + 22 words); returns the output tokens.
fn port_line(pair: Pair, w: &[u32]) -> Vec<String> {
    let (na, nb) = pair.counts();
    let (tw, sw) = if pair.tri_first() {
        (&w[..na], &w[na..na + nb])
    } else {
        (&w[na..na + nb], &w[..na])
    };
    let mut out = pi_from(&w[na + nb..]);
    let r = pair.run(&tri_from(tw), &shape_from(sw), &mut out);
    pi_words(r, &out)
}

// ------------------------------------------------------------------ case generator

struct Rng(u64);

impl Rng {
    fn next(&mut self) -> u64 {
        self.0 = self.0.wrapping_add(0x9E37_79B9_7F4A_7C15);
        let mut z = self.0;
        z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
        z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
        z ^ (z >> 31)
    }
    fn unit(&mut self) -> f32 {
        (self.next() >> 40) as f32 * (1.0 / 16_777_216.0)
    }
    fn range(&mut self, lo: f32, hi: f32) -> f32 {
        lo + (hi - lo) * self.unit()
    }
    fn below(&mut self, n: u64) -> u64 {
        self.next() % n
    }
    fn chance(&mut self, p: f32) -> bool {
        self.unit() < p
    }
    fn vec(&mut self, s: f32) -> [f32; 3] {
        [self.range(-s, s), self.range(-s, s), self.range(-s, s)]
    }
    fn dir(&mut self) -> [f32; 3] {
        loop {
            let v = self.vec(1.0);
            let l = (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]).sqrt();
            if l > 0.1 && l <= 1.0 {
                return [v[0] / l, v[1] / l, v[2] / l];
            }
        }
    }
}

fn add(a: [f32; 3], b: [f32; 3]) -> [f32; 3] {
    [a[0] + b[0], a[1] + b[1], a[2] + b[2]]
}

fn scale(a: [f32; 3], s: f32) -> [f32; 3] {
    [a[0] * s, a[1] * s, a[2] * s]
}

fn quant(a: [f32; 3], q: f32) -> [f32; 3] {
    [
        (a[0] / q).round() * q,
        (a[1] / q).round() * q,
        (a[2] / q).round() * q,
    ]
}

fn normal(p: &[[f32; 3]; 3]) -> [f32; 3] {
    let e1 = [p[1][0] - p[0][0], p[1][1] - p[0][1], p[1][2] - p[0][2]];
    let e2 = [p[2][0] - p[0][0], p[2][1] - p[0][1], p[2][2] - p[0][2]];
    let c = [
        e1[1] * e2[2] - e1[2] * e2[1],
        e1[2] * e2[0] - e1[0] * e2[2],
        e1[0] * e2[1] - e1[1] * e2[0],
    ];
    let l = (c[0] * c[0] + c[1] * c[1] + c[2] * c[2]).sqrt();
    if l > 0.0 {
        scale(c, 1.0 / l)
    } else {
        [0.0, 0.0, 0.0]
    }
}

/// One case: triangle (12 floats), shape (8 floats), prim_inters init (22 words).
fn gen_case(rng: &mut Rng, kind: u64) -> ([f32; 12], [f32; 8], [u32; 22]) {
    let mut axis = rng.dir();
    let mut r = rng.range(0.05, 1.5);
    let mut hh = rng.range(0.0, 2.0);
    let center = rng.vec(3.0);
    let mut p: [[f32; 3]; 3];
    match kind {
        // generic triangle near the shape
        0 => {
            let s = rng.range(0.2, 6.0);
            let base = add(center, rng.vec(r + hh + 0.5));
            p = [
                add(base, rng.vec(s)),
                add(base, rng.vec(s)),
                add(base, rng.vec(s)),
            ];
        }
        // big triangle under the shape (face contacts), axis close to the normal
        1 => {
            let n = rng.dir();
            axis = if rng.chance(0.5) {
                n
            } else {
                add(n, rng.vec(0.2))
            };
            let l = (axis[0] * axis[0] + axis[1] * axis[1] + axis[2] * axis[2]).sqrt();
            axis = scale(axis, 1.0 / l);
            let plane = add(center, scale(n, -(hh * rng.range(0.6, 1.2))));
            let mut u = [n[1], -n[0], 0.0];
            if u[0].abs() + u[1].abs() < 0.1 {
                u = [0.0, n[2], -n[1]];
            }
            let lu = (u[0] * u[0] + u[1] * u[1] + u[2] * u[2]).sqrt();
            u = scale(u, 1.0 / lu);
            let w = [
                n[1] * u[2] - n[2] * u[1],
                n[2] * u[0] - n[0] * u[2],
                n[0] * u[1] - n[1] * u[0],
            ];
            let s = rng.range(2.0, 20.0);
            let off = scale(
                add(
                    scale(u, rng.range(-1.0, 1.0)),
                    scale(w, rng.range(-1.0, 1.0)),
                ),
                r,
            );
            let c0 = add(plane, off);
            p = [
                add(c0, add(scale(u, -s), scale(w, -s))),
                add(c0, scale(u, s)),
                add(c0, scale(w, s)),
            ];
        }
        // axis perpendicular to the normal (shape lying on the triangle)
        2 => {
            let n = rng.dir();
            let mut a = [n[1], -n[0], 0.0];
            if a[0].abs() + a[1].abs() < 0.1 {
                a = [0.0, n[2], -n[1]];
            }
            let la = (a[0] * a[0] + a[1] * a[1] + a[2] * a[2]).sqrt();
            axis = add(scale(a, 1.0 / la), scale(n, rng.range(-0.15, 0.15)));
            let plane = add(center, scale(n, -r * rng.range(0.5, 1.1)));
            let w = [
                n[1] * a[2] - n[2] * a[1],
                n[2] * a[0] - n[0] * a[2],
                n[0] * a[1] - n[1] * a[0],
            ];
            let s = rng.range(0.5, 8.0);
            p = [
                add(plane, add(scale(a, -s), scale(w, -s))),
                add(plane, scale(a, s * rng.range(0.2, 1.5))),
                add(plane, scale(w, s)),
            ];
        }
        // small triangle partly / fully inside
        3 => {
            let s = rng.range(0.01, 0.8) * r;
            let base = add(center, scale(axis, rng.range(-hh - r, hh + r)));
            p = [
                add(base, rng.vec(s)),
                add(base, rng.vec(s)),
                add(base, rng.vec(s)),
            ];
        }
        // quantised coordinates (ties, zeros, axis-aligned)
        4 => {
            let ax = rng.below(3) as usize;
            axis = [0.0; 3];
            axis[ax] = if rng.chance(0.5) { 1.0 } else { -1.0 };
            r = (rng.range(0.25, 1.5) * 4.0).round() / 4.0;
            hh = (rng.range(0.0, 1.5) * 4.0).round() / 4.0;
            let base = quant(add(center, rng.vec(r + hh)), 0.25);
            let s = rng.range(0.5, 4.0);
            p = [
                quant(add(base, rng.vec(s)), 0.25),
                quant(add(base, rng.vec(s)), 0.25),
                quant(add(base, rng.vec(s)), 0.25),
            ];
        }
        // edge parallel to the axis
        5 => {
            let base = add(center, rng.vec(r * 1.5));
            let l = rng.range(0.5, 4.0);
            p = [
                add(base, scale(axis, -l)),
                add(base, scale(axis, l)),
                add(base, rng.vec(2.0)),
            ];
        }
        // degenerate shapes / triangles
        _ => {
            let s = rng.range(0.2, 3.0);
            let base = add(center, rng.vec(r + hh));
            p = [
                add(base, rng.vec(s)),
                add(base, rng.vec(s)),
                add(base, rng.vec(s)),
            ];
            match rng.below(6) {
                0 => p[2] = p[1],
                1 => {
                    p[2] = add(
                        p[0],
                        scale(
                            [p[1][0] - p[0][0], p[1][1] - p[0][1], p[1][2] - p[0][2]],
                            0.5,
                        ),
                    )
                }
                2 => hh = 0.0,
                3 => r = 0.0,
                4 => axis = [0.0, 0.0, 0.0],
                _ => p = [base, base, base],
            }
        }
    }
    let mut n = normal(&p);
    if rng.chance(0.3) {
        n = scale(n, -1.0);
    }
    if kind == 6 && rng.chance(0.2) {
        n = [0.0, 0.0, 0.0];
    }
    let mut tri = [0.0f32; 12];
    for i in 0..3 {
        tri[3 * i..3 * i + 3].copy_from_slice(&p[i]);
    }
    tri[9..12].copy_from_slice(&n);
    let shape = [
        center[0], center[1], center[2], axis[0], axis[1], axis[2], r, hh,
    ];

    // prim_inters init: pt[1] steers the edge walk; other fields detect stray writes
    let mut pi = [0u32; 22];
    let pt1 = match rng.below(4) {
        0 => [0.0; 3],
        1 => add(center, rng.vec(3.0)),
        2 => p[rng.below(3) as usize],
        _ => rng.vec(50.0),
    };
    for k in 0..3 {
        pi[k] = rng.range(-5.0, 5.0).to_bits();
        pi[3 + k] = pt1[k].to_bits();
        pi[6 + k] = rng.range(-1.0, 1.0).to_bits();
        pi[18 + k] = rng.range(-9.0, 9.0).to_bits();
    }
    pi[9] = rng.next() as u32;
    pi[10] = rng.range(0.0, 1.0).to_bits();
    pi[11] = rng.next() as u32;
    pi[12] = rng.next() as u32;
    pi[13] = rng.next() as u32;
    let cap_choice = rng.below(10);
    pi[17] = match cap_choice {
        0 => 0,
        1 => 1,
        2 => 3,
        3 => 9,
        _ => 15,
    };
    pi[16] = if rng.chance(0.1) {
        rng.below(pi[17] as u64 + 1) as u32
    } else {
        0
    };
    pi[21] = rng.next() as u32 & 0xffff;
    (tri, shape, pi)
}

fn case_line(pair: Pair, rng: &mut Rng) -> String {
    let kind = rng.below(9);
    let (mut tri, mut shape, pi) = gen_case(rng, kind % 7);
    if kind == 7 {
        // far from the origin (world coordinates), shifted by an exact multiple of 0.5
        let off = quant(rng.vec(30_000.0), 0.5);
        for k in 0..3 {
            for v in 0..3 {
                tri[3 * v + k] += off[k];
            }
            shape[k] += off[k];
        }
    } else if kind == 8 {
        // special values in one or two fields: ±0, ±inf, NaN, denormals, huge
        const SPECIAL: [u32; 9] = [
            0x0000_0000,
            0x8000_0000,
            0x7f80_0000,
            0xff80_0000,
            0x7fc0_0000,
            0x0000_0001,
            0x8040_0000,
            0x7f7f_ffff,
            0x5000_0000,
        ];
        for _ in 0..1 + rng.below(2) {
            let v = f32::from_bits(SPECIAL[rng.below(SPECIAL.len() as u64) as usize]);
            let i = rng.below(20) as usize;
            if i < 12 {
                tri[i] = v;
            } else {
                shape[i - 12] = v;
            }
        }
    }
    let mut w: Vec<u32> = Vec::with_capacity(42);
    let tw = tri.iter().map(|x| x.to_bits());
    let sw = shape.iter().map(|x| x.to_bits());
    if pair.tri_first() {
        w.extend(tw);
        w.extend(sw);
    } else {
        w.extend(sw);
        w.extend(tw);
    }
    w.extend(pi);
    w.iter()
        .map(|x| format!("{x:08x}"))
        .collect::<Vec<_>>()
        .join(" ")
}

fn parse_words(s: &str) -> Vec<u32> {
    s.split_whitespace()
        .map(|t| u32::from_str_radix(t, 16).unwrap_or_else(|_| panic!("bad word {t}")))
        .collect()
}

/// A path signature of a result, used to pick a branch-diverse golden set and to report the
/// fuzz coverage: return value, edge/face contact, the axis orientation class (cylinder:
/// |n×a|² ≤ 0.001 takes the cap-circle branch; capsule: |n·a| < 0.1 adds the axis point),
/// whether `n` was written and the number of border points.
fn signature(pair: Pair, inp: &[u32], out: &[String]) -> String {
    if out[0] == "0" {
        return "0".into();
    }
    let (na, nb) = pair.counts();
    let (tw, sw) = if pair.tri_first() {
        (&inp[..na], &inp[na..na + nb])
    } else {
        (&inp[na..na + nb], &inp[..na])
    };
    let (n, a) = (v3(&tw[9..12]), v3(&sw[3..6]));
    let c = n.cross(a);
    let class = match pair {
        Pair::TriCyl | Pair::CylTri => {
            if c.len2() <= 0.001 {
                "par"
            } else {
                "obl"
            }
        }
        _ => {
            if n.dot(a).abs() < 0.1 {
                "flat"
            } else {
                "tilt"
            }
        }
    };
    let feat = u32::from_str_radix(&out[10], 16).unwrap().to_le_bytes();
    let kind = if feat[0] == 0x40 && feat[3] == 0x40 && feat[1] == 0x40 {
        "face"
    } else {
        "edge"
    };
    let n_written = out[22] == "000003e8";
    let nb = (out.len() - 24) / 3;
    format!("1:{kind}:{class}:{n_written}:{nb}")
}

/// A golden from the oracle: tame candidates from the generator, keeping at most `per_sig`
/// lines per path signature *of the DLL result* and `max` in total (selection does not depend
/// on the port).
fn golden_from_oracle(exe: &Path, pair: Pair, seed: u64, per_sig: usize, max: usize) -> String {
    let mut rng = Rng(seed);
    let cands: Vec<String> = (0..8_000)
        .map(|_| case_line(pair, &mut rng))
        .filter(|l| tame(pair, &parse_words(l)))
        .collect();
    let text = run_oracle(exe, pair, &cands);
    let mut lines = text.lines();
    let mut golden = format!("{}\n", lines.next().unwrap_or_default());
    let mut seen: HashMap<String, usize> = HashMap::new();
    let mut kept = 0;
    for line in lines {
        if kept >= max {
            break;
        }
        let (inp, rest) = line
            .strip_prefix("in ")
            .and_then(|l| l.split_once(" out "))
            .unwrap();
        let out: Vec<String> = rest.split_whitespace().map(String::from).collect();
        let c = seen
            .entry(signature(pair, &parse_words(inp), &out))
            .or_insert(0);
        if *c < per_sig {
            *c += 1;
            kept += 1;
            golden.push_str(line);
            golden.push('\n');
        }
    }
    golden
}

// ------------------------------------------------------------------ oracle plumbing

fn run_oracle(exe: &Path, pair: Pair, inputs: &[String]) -> String {
    let (na, nb) = pair.counts();
    let dir = std::env::temp_dir().join(format!("aaemu-p2a3-{}", std::process::id()));
    std::fs::create_dir_all(&dir).unwrap();
    let file = dir.join(format!("{}-{}.txt", pair.name(), inputs.len()));
    std::fs::write(&file, inputs.join("\n") + "\n").unwrap();
    let out = Command::new(exe)
        .args([
            "--pairfn",
            pair.va(),
            &na.to_string(),
            &nb.to_string(),
            &file.to_string_lossy(),
        ])
        .output()
        .expect("cannot run the oracle");
    let _ = std::fs::remove_file(&file);
    let text = String::from_utf8(out.stdout).expect("oracle output is not UTF-8");
    assert!(
        out.status.success(),
        "oracle failed: {}",
        text.lines().last().unwrap_or("")
    );
    text
}

/// Inputs whose geometry words are all finite and of moderate size (|x| < 1e18). Outside that
/// domain (NaN, ±inf, overflow to inf) the result can depend on the sign of NaNs, which SSE
/// takes from the *first* operand when both are NaN; LLVM commutes `addss`/`mulss` operands
/// freely, so such inputs are not guaranteed to match (see the module docs of the port).
fn tame(pair: Pair, inp: &[u32]) -> bool {
    let (na, nb) = pair.counts();
    inp[..na + nb]
        .iter()
        .all(|w| f(*w).is_finite() && f(*w).abs() < 1e18)
}

/// Comparison result: lines, mismatches among tame inputs (first few), mismatches among the
/// others (count and first few).
struct Cmp {
    n: usize,
    tame_bad: Vec<String>,
    wild: usize,
    wild_bad: usize,
    wild_first: Vec<String>,
}

/// Compare every oracle line with the port.
fn compare(pair: Pair, text: &str) -> Cmp {
    let mut c = Cmp {
        n: 0,
        tame_bad: Vec::new(),
        wild: 0,
        wild_bad: 0,
        wild_first: Vec::new(),
    };
    for line in text.lines().skip(1) {
        let (inp, rest) = line
            .strip_prefix("in ")
            .and_then(|l| l.split_once(" out "))
            .unwrap_or_else(|| panic!("bad oracle line: {line}"));
        let want: Vec<&str> = rest.split_whitespace().collect();
        let w = parse_words(inp);
        let got = port_line(pair, &w);
        c.n += 1;
        let ok = got.iter().map(String::as_str).eq(want.iter().copied());
        let msg = || {
            format!(
                "in {inp}\n  dll : {}\n  port: {}",
                want.join(" "),
                got.join(" ")
            )
        };
        if tame(pair, &w) {
            if !ok && c.tame_bad.len() < 5 {
                c.tame_bad.push(msg());
            }
        } else {
            c.wild += 1;
            if !ok {
                c.wild_bad += 1;
                if c.wild_first.len() < 2 {
                    c.wild_first.push(msg());
                }
            }
        }
    }
    c
}

fn golden(pair: Pair, seed: u64, per_sig: usize, max: usize) -> String {
    let path = golden_dir().join(format!("p2a3_{}.trace", pair.name()));
    let stored = std::fs::read_to_string(&path)
        .ok()
        .map(|s| s.replace("\r\n", "\n"));
    let Some(exe) = oracle_path() else {
        return stored
            .unwrap_or_else(|| panic!("{} missing and the oracle is not set", path.display()));
    };
    let fresh = golden_from_oracle(&exe, pair, seed, per_sig, max);
    let bless = std::env::var_os(BLESS_ENV).is_some_and(|v| v != "0");
    match stored {
        Some(s) if s == fresh => s,
        Some(_) if !bless => panic!(
            "{} differs from the oracle; set {BLESS_ENV}=1 to rewrite",
            path.display()
        ),
        _ => {
            std::fs::write(&path, &fresh).unwrap();
            fresh
        }
    }
}

fn check_golden(pair: Pair, seed: u64, per_sig: usize, max: usize) {
    let text = golden(pair, seed, per_sig, max);
    let c = compare(pair, &text);
    assert!(c.n > 0, "{}: empty golden", pair.name());
    assert!(
        c.wild == 0,
        "{}: the golden set holds only tame inputs",
        pair.name()
    );
    assert!(
        c.tame_bad.is_empty(),
        "{}: port differs from the DLL:\n{}",
        pair.name(),
        c.tame_bad.join("\n")
    );
}

fn fuzz(pair: Pair, seed: u64) {
    let Some(exe) = oracle_path() else {
        return;
    };
    let count: usize = std::env::var("AAEMU_P2A3_FUZZ")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(20_000);
    let seed_off: u64 = std::env::var("AAEMU_P2A3_SEED")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(0);
    let mut rng = Rng(seed ^ seed_off.wrapping_mul(0x1000_0001));
    let inputs: Vec<String> = (0..count).map(|_| case_line(pair, &mut rng)).collect();
    let text = run_oracle(&exe, pair, &inputs);
    let c = compare(pair, &text);
    assert_eq!(c.n, count, "{}: oracle line count", pair.name());
    // path coverage (visible with --nocapture)
    let mut hist: HashMap<String, usize> = HashMap::new();
    for line in text.lines().skip(1) {
        let (inp, rest) = line
            .strip_prefix("in ")
            .and_then(|l| l.split_once(" out "))
            .unwrap();
        let out: Vec<String> = rest.split_whitespace().map(String::from).collect();
        *hist
            .entry(signature(pair, &parse_words(inp), &out))
            .or_insert(0) += 1;
    }
    let mut h: Vec<_> = hist.into_iter().collect();
    h.sort();
    eprintln!(
        "{} paths (ret:kind:class:n written:border pts): {h:?}",
        pair.name()
    );
    eprintln!(
        "{}: {} cases, {} with NaN/inf/huge inputs of which {} differ{}",
        pair.name(),
        c.n,
        c.wild,
        c.wild_bad,
        c.wild_first
            .iter()
            .map(|m| format!("\n{m}"))
            .collect::<String>()
    );
    assert!(
        c.tame_bad.is_empty(),
        "{}: port differs from the DLL:\n{}",
        pair.name(),
        c.tame_bad.join("\n")
    );
}

// ------------------------------------------------------------------ tests

#[test]
fn golden_tri_cyl() {
    check_golden(Pair::TriCyl, 0x7C1, 6, 250);
}

#[test]
fn golden_tri_cap() {
    check_golden(Pair::TriCap, 0x7CA, 6, 250);
}

#[test]
fn golden_cyl_tri() {
    check_golden(Pair::CylTri, 0xC71, 2, 80);
}

#[test]
fn golden_cap_tri() {
    check_golden(Pair::CapTri, 0xCA7, 2, 80);
}

#[test]
fn fuzz_tri_cyl() {
    fuzz(Pair::TriCyl, 101);
}

#[test]
fn fuzz_tri_cap() {
    fuzz(Pair::TriCap, 202);
}

#[test]
fn fuzz_cyl_tri() {
    fuzz(Pair::CylTri, 303);
}

#[test]
fn fuzz_cap_tri() {
    fuzz(Pair::CapTri, 404);
}

/// The table cells dispatch to the ported functions (and only for matching primitives).
#[test]
fn pair_table_cells() {
    let t = PairTables::build();
    let tri = Primitive::Triangle(Triangle {
        pt: [
            Vec3::new(-10.0, -10.0, 0.0),
            Vec3::new(10.0, -10.0, 0.0),
            Vec3::new(0.0, 10.0, 0.0),
        ],
        n: Vec3::Z,
    });
    let shape = CylinderPrim {
        center: Vec3::new(0.0, 0.0, 0.5),
        axis: Vec3::Z,
        r: 1.0,
        hh: 1.0,
    };
    for (a, b, ok) in [
        (GeomType::TriMesh, GeomType::Cylinder, true),
        (GeomType::TriMesh, GeomType::Capsule, true),
        (GeomType::Cylinder, GeomType::TriMesh, true),
        (GeomType::Capsule, GeomType::TriMesh, true),
    ] {
        let pa = if a == GeomType::TriMesh {
            tri
        } else if a == GeomType::Cylinder {
            Primitive::Cylinder(shape)
        } else {
            Primitive::Capsule(shape)
        };
        let pb = if b == GeomType::TriMesh {
            tri
        } else if b == GeomType::Cylinder {
            Primitive::Cylinder(shape)
        } else {
            Primitive::Capsule(shape)
        };
        let mut out = PrimInters {
            border_max: 15,
            ..PrimInters::default()
        };
        assert_eq!(
            (t.intersect_fn(a, b))(&pa, &pb, &mut out),
            ok,
            "{a:?} {b:?}"
        );
        // mismatched primitive kinds never collide
        let mut out2 = PrimInters::default();
        assert!(!(t.intersect_fn(a, b))(&pb, &pa, &mut out2));
    }
}

/// Hand-checked face contact: an upright cylinder whose bottom cap sinks 0.5 into a big
/// horizontal triangle (values from the DLL, see the oracle smoke test in the module docs).
#[test]
fn upright_cylinder_on_triangle() {
    let tri = Triangle {
        pt: [
            Vec3::new(-10.0, -10.0, 0.0),
            Vec3::new(10.0, -10.0, 0.0),
            Vec3::new(0.0, 10.0, 0.0),
        ],
        n: Vec3::Z,
    };
    let cyl = CylinderPrim {
        center: Vec3::new(0.0, 0.0, 0.5),
        axis: Vec3::Z,
        r: 1.0,
        hh: 1.0,
    };
    let mut out = PrimInters {
        border_max: 15,
        ..PrimInters::default()
    };
    assert!(tri_cylinder(&tri, &cyl, &mut out));
    assert_eq!(out.pt[0], Vec3::ZERO);
    assert_eq!(out.pt[1], Vec3::ZERO);
    assert_eq!(out.n, Vec3::Z);
    assert_eq!(out.i_feature, [[0x40; 2]; 2]);
    assert_eq!(out.unk_84, 1000);
    // the 8 points of the bottom cap circle at z = -0.5
    assert_eq!(out.border.len(), 8);
    for p in &out.border {
        assert_eq!(p.z, -0.5);
        assert!((p.x * p.x + p.y * p.y - 1.0).abs() < 1e-6);
    }
    // a capacity of 3 keeps the first 3
    let mut out = PrimInters {
        border_max: 3,
        ..PrimInters::default()
    };
    assert!(tri_cylinder(&tri, &cyl, &mut out));
    assert_eq!(out.border.len(), 3);
}

/// A triangle entirely inside the shape produces no contact (quirk of the original).
#[test]
fn triangle_inside_gives_no_contact() {
    let tri = Triangle {
        pt: [
            Vec3::new(-0.1, -0.1, 0.0),
            Vec3::new(0.1, -0.1, 0.0),
            Vec3::new(0.0, 0.1, 0.0),
        ],
        n: Vec3::Z,
    };
    let shape = CylinderPrim {
        center: Vec3::ZERO,
        axis: Vec3::Z,
        r: 1.0,
        hh: 1.0,
    };
    let mut out = PrimInters::default();
    let before = out.clone();
    assert!(!tri_cylinder(&tri, &shape, &mut out));
    assert!(!tri_capsule(&tri, &shape, &mut out));
    assert_eq!(out, before, "nothing is written");
}

/// The swap wrappers reverse the feature bytes and swap the points.
#[test]
fn wrappers_swap_points_and_features() {
    let tri = Triangle {
        pt: [
            Vec3::new(-3.0, 0.0, -3.0),
            Vec3::new(3.0, 0.0, -3.0),
            Vec3::new(0.0, 0.0, 3.0),
        ],
        n: Vec3::new(0.0, -1.0, 0.0),
    };
    let shape = CylinderPrim {
        center: Vec3::new(0.0, 0.3, 0.0),
        axis: Vec3::X,
        r: 0.5,
        hh: 1.0,
    };
    for (direct, wrapped) in [
        (
            tri_cylinder as fn(&Triangle, &CylinderPrim, &mut PrimInters) -> bool,
            cylinder_tri as fn(&CylinderPrim, &Triangle, &mut PrimInters) -> bool,
        ),
        (tri_capsule, capsule_tri),
    ] {
        let mut a = PrimInters {
            border_max: 15,
            ..PrimInters::default()
        };
        let mut b = a.clone();
        let ra = direct(&tri, &shape, &mut a);
        let rb = wrapped(&shape, &tri, &mut b);
        assert_eq!(ra, rb);
        assert_eq!(b.pt, [a.pt[1], a.pt[0]]);
        assert_eq!(
            b.i_feature,
            [
                [a.i_feature[1][1], a.i_feature[1][0]],
                [a.i_feature[0][1], a.i_feature[0][0]]
            ]
        );
        assert_eq!(b.n, a.n);
        assert_eq!(b.border, a.border);
    }
}
