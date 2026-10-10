//! P2a-2 triangle pairs checked bit for bit against the functions executed inside the real
//! 10.x DLL (`physoracle --p2a2`, added by P2a-2).
//!
//! * Goldens `tests/golden/p2a2_<probe>.trace` hold the oracle's lines (inputs, return value and
//!   the whole 96-byte `prim_inters` buffer after the call). Without the oracle the tests replay
//!   them; with `AAEMU_PHYS_ORACLE` set they are regenerated from the deterministic case
//!   generators below and must match byte for byte (`AAEMU_PHYS_BLESS=1` rewrites).
//! * `fuzz_*` tests (oracle only) run many more generated cases live, without goldens.
//!
//! The output buffer starts as a sentinel pattern, so the comparison also checks *which*
//! fields each function writes.

use aaemu_physics::collision::contact::PrimInters;
use aaemu_physics::collision::pairs::tri_pairs as tp;
use aaemu_physics::geometry::primitives::{BoxPrim, HeightfieldPrim, RayPrim, SpherePrim, Triangle};
use aaemu_physics::math::{Mat33, Vec3};
use std::io::Write as _;
use std::path::PathBuf;
use std::process::{Command, Stdio};

// ------------------------------------------------------------------------------------ rng

struct Rng(u64);
impl Rng {
    fn next(&mut self) -> u64 {
        self.0 = self.0.wrapping_add(0x9E37_79B9_7F4A_7C15);
        let mut z = self.0;
        z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
        z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
        z ^ (z >> 31)
    }
    fn f(&mut self, lo: f32, hi: f32) -> f32 {
        lo + (hi - lo) * ((self.next() >> 40) as f32 * (1.0 / 16_777_216.0))
    }
    fn i(&mut self, n: u32) -> u32 {
        (self.next() % u64::from(n)) as u32
    }
    fn chance(&mut self, pct: u32) -> bool {
        self.i(100) < pct
    }
    fn v(&mut self, r: f32) -> Vec3 {
        Vec3::new(self.f(-r, r), self.f(-r, r), self.f(-r, r))
    }
    /// Small integers or halves: exact arithmetic, many ties and zeros.
    fn vgrid(&mut self, n: i32) -> Vec3 {
        let mut c = || (self.i((2 * n + 1) as u32) as i32 - n) as f32 * 0.5;
        Vec3::new(c(), c(), c())
    }
    fn unit(&mut self) -> Vec3 {
        loop {
            let v = self.v(1.0);
            let l2 = v.len2();
            if l2 > 0.01 && l2 <= 1.0 {
                return v.normalized();
            }
        }
    }
}

// ------------------------------------------------------------------------------- words

fn fw(x: f32) -> u32 {
    x.to_bits()
}
fn wf(w: u32) -> f32 {
    f32::from_bits(w)
}
fn vw(v: Vec3, o: &mut Vec<u32>) {
    o.extend([fw(v.x), fw(v.y), fw(v.z)]);
}
fn wv(w: &[u32]) -> Vec3 {
    Vec3::new(wf(w[0]), wf(w[1]), wf(w[2]))
}

fn tri_words(t: &Triangle, o: &mut Vec<u32>) {
    for p in t.pt {
        vw(p, o);
    }
    vw(t.n, o);
}
fn words_tri(w: &[u32]) -> Triangle {
    Triangle {
        pt: [wv(&w[0..]), wv(&w[3..]), wv(&w[6..])],
        n: wv(&w[9..]),
    }
}
fn box_words(b: &BoxPrim, o: &mut Vec<u32>) {
    o.extend(b.basis.to_bits());
    o.push(b.oriented as u32);
    vw(b.center, o);
    vw(b.size, o);
}
fn words_box(w: &[u32]) -> BoxPrim {
    BoxPrim {
        basis: Mat33::from_bits(w[0..9].try_into().unwrap()),
        oriented: w[9] as i32,
        center: wv(&w[10..]),
        size: wv(&w[13..]),
    }
}
fn sph_words(s: &SpherePrim, o: &mut Vec<u32>) {
    vw(s.center, o);
    o.push(fw(s.r));
}
fn words_sph(w: &[u32]) -> SpherePrim {
    SpherePrim {
        center: wv(w),
        r: wf(w[3]),
    }
}
fn ray_words(r: &RayPrim, o: &mut Vec<u32>) {
    vw(r.origin, o);
    vw(r.dir, o);
}
fn words_ray(w: &[u32]) -> RayPrim {
    RayPrim {
        origin: wv(w),
        dir: wv(&w[3..]),
    }
}

const SENTINEL: u32 = 0xCDCD_CDCD;

/// The 24-word `prim_inters` image as a [`PrimInters`] (fields the port never touches are
/// kept in `rest` for the comparison).
fn words_inters(w: &[u32]) -> PrimInters {
    PrimInters {
        pt: [wv(&w[0..]), wv(&w[3..])],
        n: wv(&w[6..]),
        i_feature: [
            [w[9] as u8, (w[9] >> 8) as u8],
            [(w[9] >> 16) as u8, (w[9] >> 24) as u8],
        ],
        tolerance: wf(w[10]),
        id: [w[11] as u16 as i16, (w[11] >> 16) as u16 as i16],
        i_node: [w[12] as i32, w[13] as i32],
        border: Vec::new(),
        n_alt: wv(&w[18..]),
        unk_84: w[21] as i32,
    }
}
fn inters_words(p: &PrimInters, init: &[u32]) -> Vec<u32> {
    let mut o = Vec::new();
    vw(p.pt[0], &mut o);
    vw(p.pt[1], &mut o);
    vw(p.n, &mut o);
    o.push(
        u32::from(p.i_feature[0][0])
            | u32::from(p.i_feature[0][1]) << 8
            | u32::from(p.i_feature[1][0]) << 16
            | u32::from(p.i_feature[1][1]) << 24,
    );
    o.push(fw(p.tolerance));
    o.push(u32::from(p.id[0] as u16) | u32::from(p.id[1] as u16) << 16);
    o.push(p.i_node[0] as u32);
    o.push(p.i_node[1] as u32);
    o.extend_from_slice(&init[14..18]); // ptborder / nborderpt / nbordersz: untouched by tri pairs
    vw(p.n_alt, &mut o);
    o.push(p.unk_84 as u32);
    o.extend_from_slice(&init[22..24]);
    o
}

// ------------------------------------------------------------------------------ oracle

fn oracle() -> Option<PathBuf> {
    let p = PathBuf::from(std::env::var_os("AAEMU_PHYS_ORACLE")?);
    p.is_file().then_some(p)
}

fn run_oracle(exe: &PathBuf, input: &str) -> String {
    let mut child = Command::new(exe)
        .arg("--p2a2")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .spawn()
        .expect("cannot start the oracle");
    let mut stdin = child.stdin.take().unwrap();
    let data = input.to_owned();
    let writer = std::thread::spawn(move || stdin.write_all(data.as_bytes()));
    let out = child.wait_with_output().expect("oracle failed");
    writer.join().unwrap().unwrap();
    assert!(out.status.success(), "oracle exited with {}", out.status);
    String::from_utf8(out.stdout).unwrap()
}

fn golden_path(name: &str) -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("tests")
        .join("golden")
        .join(format!("p2a2_{name}.trace"))
}

fn bless() -> bool {
    std::env::var_os("AAEMU_PHYS_BLESS").is_some_and(|v| v != "0")
}

/// A probe case: the input words (operands + initial buffer).
struct Case {
    words: Vec<u32>,
}

fn render(probe: &str, cases: &[Case]) -> String {
    let mut s = String::new();
    for c in cases {
        s.push_str(probe);
        for w in &c.words {
            s.push_str(&format!(" {w:08x}"));
        }
        s.push('\n');
    }
    s
}

/// One parsed oracle line.
struct Row {
    input: Vec<u32>,
    ret: i32,
    out: Vec<u32>,
}

fn parse(probe: &str, text: &str) -> Vec<Row> {
    text.lines()
        .filter(|l| !l.is_empty())
        .map(|l| {
            let mut it = l.split_ascii_whitespace();
            assert_eq!(it.next(), Some(probe), "line: {l}");
            assert_eq!(it.next(), Some("in"));
            let mut input = Vec::new();
            for t in it.by_ref() {
                if t == "out" {
                    break;
                }
                input.push(u32::from_str_radix(t, 16).unwrap());
            }
            let ret = it.next().unwrap().parse().unwrap();
            let out = it.map(|t| u32::from_str_radix(t, 16).unwrap()).collect();
            Row { input, ret, out }
        })
        .collect()
}

/// Golden rows of a probe (regenerated from the oracle and verified when it is configured).
fn golden(probe: &str, cases: impl FnOnce() -> Vec<Case>) -> Vec<Row> {
    let path = golden_path(probe.trim_start_matches("p2a2_"));
    let stored = std::fs::read_to_string(&path)
        .ok()
        .map(|s| s.replace("\r\n", "\n"));
    let text = match oracle() {
        Some(exe) => {
            let fresh = run_oracle(&exe, &render(probe, &cases()));
            match &stored {
                Some(s) if *s == fresh => fresh,
                _ if bless() || stored.is_none() => {
                    std::fs::write(&path, &fresh).unwrap();
                    fresh
                }
                Some(_) => panic!(
                    "{} differs from the oracle; set AAEMU_PHYS_BLESS=1 to rewrite",
                    path.display()
                ),
                None => unreachable!(),
            }
        }
        None => stored.unwrap_or_else(|| {
            panic!("golden {} missing and AAEMU_PHYS_ORACLE not set", path.display())
        }),
    };
    parse(probe, &text)
}

fn live(probe: &str, cases: Vec<Case>) -> Option<Vec<Row>> {
    let exe = oracle()?;
    Some(parse(probe, &run_oracle(&exe, &render(probe, &cases))))
}

// ------------------------------------------------------------------------------ checks

type InterFn<A, B> = fn(&A, &B, &mut PrimInters) -> bool;

fn check_inters<A, B>(
    probe: &str,
    rows: &[Row],
    na: usize,
    nb: usize,
    pa: fn(&[u32]) -> A,
    pb: fn(&[u32]) -> B,
    f: InterFn<A, B>,
) -> usize {
    let mut hits = 0;
    for (i, r) in rows.iter().enumerate() {
        let a = pa(&r.input[..na]);
        let b = pb(&r.input[na..na + nb]);
        let init = &r.input[na + nb..na + nb + 24];
        let mut out = words_inters(init);
        let ret = f(&a, &b, &mut out);
        let got = inters_words(&out, init);
        hits += usize::from(r.ret != 0);
        if i32::from(ret) != r.ret || got != r.out {
            let diff: Vec<String> = got
                .iter()
                .zip(&r.out)
                .enumerate()
                .filter(|(_, (g, w))| g != w)
                .map(|(k, (g, w))| format!("w{k}: got {g:08x} want {w:08x}"))
                .collect();
            panic!(
                "{probe} row {i}: ret got {ret} want {}; {}\n input {:08x?}",
                r.ret,
                diff.join(", "),
                r.input
            );
        }
    }
    hits
}

fn check_overlap<A, B>(
    probe: &str,
    rows: &[Row],
    na: usize,
    pa: fn(&[u32]) -> A,
    pb: fn(&[u32]) -> B,
    f: fn(&A, &B) -> bool,
) -> usize {
    let mut hits = 0;
    for (i, r) in rows.iter().enumerate() {
        let a = pa(&r.input[..na]);
        let b = pb(&r.input[na..]);
        let ret = f(&a, &b);
        hits += usize::from(r.ret != 0);
        assert_eq!(i32::from(ret), r.ret, "{probe} row {i}: input {:08x?}", r.input);
    }
    hits
}

// --------------------------------------------------------------------------- generators

fn init_words(rng: &mut Rng, o: &mut Vec<u32>) {
    // Sentinel buffer; the tolerance word (+40) is an input for some pairs.
    let start = o.len();
    o.extend([SENTINEL; 24]);
    let tol = match rng.i(4) {
        0 => 0.0,
        1 => 1e-4,
        2 => rng.f(0.0, 0.1),
        _ => wf(SENTINEL),
    };
    o[start + 10] = fw(tol);
}

/// Triangle with the engine's normal (`normalize((p1-p0) x (p2-p0))`), sometimes reversed,
/// unnormalised or zero.
fn make_tri(rng: &mut Rng, p: [Vec3; 3]) -> Triangle {
    let c = (p[1] - p[0]).cross(p[2] - p[0]);
    let l2 = c.len2();
    let mut n = if l2 > 0.0 { c.normalized() } else { Vec3::ZERO };
    match rng.i(20) {
        0 => n = -n,
        1 => n = c,
        2 => n = rng.unit(),
        _ => {}
    }
    Triangle { pt: p, n }
}

fn rand_tri(rng: &mut Rng, center: Vec3, r: f32) -> Triangle {
    let p = match rng.i(10) {
        0 => {
            let g = rng.vgrid(4);
            [center + g, center + rng.vgrid(4), center + rng.vgrid(4)]
        }
        1 => {
            // degenerate: collinear or repeated vertex
            let a = center + rng.v(r);
            let b = center + rng.v(r);
            let t = if rng.chance(50) { 0.0 } else { rng.f(-1.0, 2.0) };
            [a, b, a + (b - a) * t]
        }
        _ => [center + rng.v(r), center + rng.v(r), center + rng.v(r)],
    };
    make_tri(rng, p)
}

/// A second triangle related to `a` (shared plane, shared edge or vertex, piercing, far).
fn related_tri(rng: &mut Rng, a: &Triangle) -> Triangle {
    let c = (a.pt[0] + a.pt[1] + a.pt[2]) * (1.0 / 3.0);
    match rng.i(8) {
        0 => {
            // coplanar
            let w = |rng: &mut Rng| {
                let s = rng.f(-1.0, 2.0);
                let t = rng.f(-1.0, 2.0);
                a.pt[0] + (a.pt[1] - a.pt[0]) * s + (a.pt[2] - a.pt[0]) * t
            };
            let p = [w(rng), w(rng), w(rng)];
            make_tri(rng, p)
        }
        1 => {
            // shared edge
            let p = [a.pt[0], a.pt[1], c + rng.v(1.0)];
            make_tri(rng, p)
        }
        2 => {
            // shared vertex
            let p = [a.pt[rng.i(3) as usize], c + rng.v(1.0), c + rng.v(1.0)];
            make_tri(rng, p)
        }
        3 => {
            // a vertex exactly on A's plane / A's vertex reused
            let p = [a.pt[2], a.pt[0] + rng.vgrid(2), c + rng.v(1.0)];
            make_tri(rng, p)
        }
        4 => {
            let o = c + rng.v(5.0);
            rand_tri(rng, o, 1.0)
        }
        5 => {
            // the same triangle (or reversed)
            let mut t = *a;
            if rng.chance(50) {
                t.pt.swap(1, 2);
                t.n = -t.n;
            }
            t
        }
        _ => {
            let r = rng.f(0.2, 1.5);
            rand_tri(rng, c, r)
        }
    }
}

fn base_tri(rng: &mut Rng) -> Triangle {
    let scale = match rng.i(8) {
        0 => 1000.0,
        1 => 0.01,
        _ => 1.0,
    };
    let off = if rng.chance(20) { rng.v(2000.0) } else { Vec3::ZERO };
    rand_tri(rng, off, scale)
}

fn gen_tri_tri(seed: u64, n: usize) -> Vec<Case> {
    let mut rng = Rng(seed);
    (0..n)
        .map(|_| {
            let a = base_tri(&mut rng);
            let b = related_tri(&mut rng, &a);
            let (a, b) = if rng.chance(50) { (a, b) } else { (b, a) };
            let mut w = Vec::new();
            tri_words(&a, &mut w);
            tri_words(&b, &mut w);
            init_words(&mut rng, &mut w);
            Case { words: w }
        })
        .collect()
}

/// A segment related to a triangle: through a random barycentric point (inside, on an edge or
/// vertex, slightly outside), parallel to the plane, ending on the plane, zero length.
fn rand_ray(rng: &mut Rng, t: &Triangle) -> RayPrim {
    let [p0, p1, p2] = t.pt;
    let (u, v) = match rng.i(6) {
        0 => (0.5, 0.0),
        1 => (0.0, 0.0),
        2 => (rng.f(0.0, 1.0), 0.0),
        3 => {
            let u = rng.f(0.0, 1.0);
            (u, 1.0 - u)
        }
        4 => (rng.f(-0.2, 1.2), rng.f(-0.2, 1.2)),
        _ => {
            let u = rng.f(0.0, 1.0);
            (u, rng.f(0.0, 1.0 - u))
        }
    };
    let target = p0 + (p1 - p0) * u + (p2 - p0) * v;
    let mut dir = match rng.i(8) {
        0 => {
            // parallel to the plane
            let e = p1 - p0;
            e * rng.f(-2.0, 2.0)
        }
        1 => Vec3::ZERO,
        2 => t.n * rng.f(-3.0, 3.0),
        _ => rng.v(3.0),
    };
    if rng.chance(5) {
        dir = rng.vgrid(3);
    }
    let s = match rng.i(5) {
        0 => 0.0,
        1 => 1.0,
        _ => rng.f(-0.3, 1.3),
    };
    RayPrim {
        origin: target - dir * s,
        dir,
    }
}

fn gen_ray_tri(seed: u64, n: usize, swapped: bool) -> Vec<Case> {
    let mut rng = Rng(seed);
    (0..n)
        .map(|_| {
            let t = base_tri(&mut rng);
            let r = rand_ray(&mut rng, &t);
            let mut w = Vec::new();
            if swapped {
                tri_words(&t, &mut w);
                ray_words(&r, &mut w);
            } else {
                ray_words(&r, &mut w);
                tri_words(&t, &mut w);
            }
            init_words(&mut rng, &mut w);
            Case { words: w }
        })
        .collect()
}

/// A sphere related to a triangle: around a vertex, an edge point, the face, far away,
/// swallowing the whole triangle, tangent to the plane.
fn rand_sph(rng: &mut Rng, t: &Triangle) -> SpherePrim {
    let [p0, p1, p2] = t.pt;
    let size = ((p1 - p0).len() + (p2 - p0).len()).max(1e-3);
    let nn = if t.n.len2() > 0.0 { t.n.normalized() } else { Vec3::Z };
    let (center, r) = match rng.i(8) {
        0 => (t.pt[rng.i(3) as usize] + rng.v(0.3 * size), rng.f(0.0, 0.6) * size),
        1 => {
            let u = rng.f(0.0, 1.0);
            (p0 + (p1 - p0) * u + rng.v(0.2 * size), rng.f(0.0, 0.5) * size)
        }
        2 => {
            let c = (p0 + p1 + p2) * (1.0 / 3.0);
            (c, 3.0 * size)
        }
        3 => {
            let c = (p0 + p1 + p2) * (1.0 / 3.0);
            let r = rng.f(0.05, 0.5) * size;
            (c + nn * (r * rng.f(0.8, 1.05)), r)
        }
        4 => (p0 + rng.v(4.0 * size), rng.f(0.0, 1.0) * size),
        5 => {
            let r = (rng.i(4) + 1) as f32 * 0.5;
            (p0 + rng.vgrid(3), r)
        }
        _ => {
            let u = rng.f(-0.2, 1.0);
            let v = rng.f(-0.2, 1.0 - u);
            let c = p0 + (p1 - p0) * u + (p2 - p0) * v;
            (c + nn * rng.f(-0.5, 0.5) * size, rng.f(0.01, 0.7) * size)
        }
    };
    SpherePrim { center, r }
}

fn gen_tri_sph(seed: u64, n: usize, swapped: bool) -> Vec<Case> {
    let mut rng = Rng(seed);
    (0..n)
        .map(|_| {
            let t = base_tri(&mut rng);
            let s = rand_sph(&mut rng, &t);
            let mut w = Vec::new();
            if swapped {
                sph_words(&s, &mut w);
                tri_words(&t, &mut w);
            } else {
                tri_words(&t, &mut w);
                sph_words(&s, &mut w);
            }
            let k = w.len();
            init_words(&mut rng, &mut w);
            // tri-sph reads pt[1] (+12) of the buffer: sometimes give it a real point.
            match rng.i(3) {
                0 => {}
                1 => {
                    w[k + 3] = fw(s.center.x);
                    w[k + 4] = fw(s.center.y);
                    w[k + 5] = fw(s.center.z);
                }
                _ => {
                    let q = t.pt[0] + rng.v(2.0);
                    w[k + 3] = fw(q.x);
                    w[k + 4] = fw(q.y);
                    w[k + 5] = fw(q.z);
                }
            }
            Case { words: w }
        })
        .collect()
}

// -------------------------------------------------------------------------------- tests

#[test]
fn tri_tri_matches_sub_35159410() {
    let rows = golden("p2a2_i_tri_tri", || gen_tri_tri(1, 600));
    let hits = check_inters("tri_tri", &rows, 12, 12, words_tri, words_tri, tp::intersect_tri_tri);
    assert!(hits > 60, "too few contacts in the golden set: {hits}");
}

#[test]
fn fuzz_tri_tri() {
    if let Some(rows) = live("p2a2_i_tri_tri", gen_tri_tri(1001, 20000)) {
        check_inters("tri_tri", &rows, 12, 12, words_tri, words_tri, tp::intersect_tri_tri);
    }
}

#[test]
fn ray_tri_matches_sub_3515ab20() {
    let rows = golden("p2a2_i_ray_tri", || gen_ray_tri(2, 600, false));
    let hits = check_inters("ray_tri", &rows, 6, 12, words_ray, words_tri, tp::intersect_ray_tri);
    assert!(hits > 60, "too few contacts in the golden set: {hits}");
}

#[test]
fn tri_ray_matches_sub_3515b1d0() {
    let rows = golden("p2a2_i_tri_ray", || gen_ray_tri(3, 300, true));
    let hits = check_inters("tri_ray", &rows, 12, 6, words_tri, words_ray, tp::intersect_tri_ray);
    assert!(hits > 30, "too few contacts in the golden set: {hits}");
}

#[test]
fn fuzz_ray_tri() {
    if let Some(rows) = live("p2a2_i_ray_tri", gen_ray_tri(1002, 20000, false)) {
        check_inters("ray_tri", &rows, 6, 12, words_ray, words_tri, tp::intersect_ray_tri);
    }
    if let Some(rows) = live("p2a2_i_tri_ray", gen_ray_tri(1003, 5000, true)) {
        check_inters("tri_ray", &rows, 12, 6, words_tri, words_ray, tp::intersect_tri_ray);
    }
}

#[test]
fn tri_sph_matches_sub_35159de0() {
    let rows = golden("p2a2_i_tri_sph", || gen_tri_sph(4, 600, false));
    let hits = check_inters("tri_sph", &rows, 12, 4, words_tri, words_sph, tp::intersect_tri_sph);
    assert!(hits > 60, "too few contacts in the golden set: {hits}");
}

#[test]
fn sph_tri_matches_sub_3515aaa0() {
    let rows = golden("p2a2_i_sph_tri", || gen_tri_sph(5, 300, true));
    let hits = check_inters("sph_tri", &rows, 4, 12, words_sph, words_tri, tp::intersect_sph_tri);
    assert!(hits > 30, "too few contacts in the golden set: {hits}");
}

#[test]
fn fuzz_tri_sph() {
    if let Some(rows) = live("p2a2_i_tri_sph", gen_tri_sph(1004, 20000, false)) {
        check_inters("tri_sph", &rows, 12, 4, words_tri, words_sph, tp::intersect_tri_sph);
    }
    if let Some(rows) = live("p2a2_i_sph_tri", gen_tri_sph(1005, 5000, true)) {
        check_inters("sph_tri", &rows, 4, 12, words_sph, words_tri, tp::intersect_sph_tri);
    }
}

#[allow(dead_code)]
fn _unused(_: HeightfieldPrim, _: fn(&[u32]) -> RayPrim, _: fn(&RayPrim, &mut Vec<u32>)) {}
#[allow(dead_code)]
fn _unused2() -> (fn(&BoxPrim, &mut Vec<u32>), fn(&[u32]) -> BoxPrim, fn(&SpherePrim, &mut Vec<u32>), fn(&[u32]) -> SpherePrim, fn(&RayPrim, &mut Vec<u32>), fn(&[u32]) -> RayPrim) {
    (box_words, words_box, sph_words, words_sph, ray_words, words_ray)
}
