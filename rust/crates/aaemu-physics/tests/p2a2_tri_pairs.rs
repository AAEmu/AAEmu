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
use aaemu_physics::geometry::primitives::{
    BoxPrim, HeightfieldPrim, RayPrim, SpherePrim, Triangle,
};
use aaemu_physics::math::{Mat33, Quat, Vec3};
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
        border: if has_border(w) {
            vec![Vec3::ZERO; w[16] as usize]
        } else {
            Vec::new()
        },
        border_max: if has_border(w) { w[17] as i32 } else { 0 },
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
    if has_border(init) {
        o.extend([
            BORDER_MARK,
            BORDER_MARK,
            p.border.len() as u32,
            p.border_max as u32,
        ]);
    } else {
        // ptborder / nborderpt / nbordersz without a buffer: untouched.
        assert!(p.border.is_empty());
        o.extend_from_slice(&init[14..18]);
    }
    vw(p.n_alt, &mut o);
    o.push(p.unk_84 as u32);
    o.extend_from_slice(&init[22..24]);
    if has_border(init) {
        // The oracle prints the first 16 points of its zeroed buffer.
        for k in 0..16 {
            vw(p.border.get(k).copied().unwrap_or(Vec3::ZERO), &mut o);
        }
    }
    o
}

/// `ptborder` words set to this marker make the oracle supply a real border buffer.
const BORDER_MARK: u32 = 0xB0B0_B0B0;

fn has_border(w: &[u32]) -> bool {
    w[14] == BORDER_MARK && w[15] == BORDER_MARK
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
            let out = it
                .filter(|t| *t != "border")
                .map(|t| u32::from_str_radix(t, 16).unwrap())
                .collect();
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
            panic!(
                "golden {} missing and AAEMU_PHYS_ORACLE not set",
                path.display()
            )
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
        assert_eq!(
            i32::from(ret),
            r.ret,
            "{probe} row {i}: input {:08x?}",
            r.input
        );
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
            let t = if rng.chance(50) {
                0.0
            } else {
                rng.f(-1.0, 2.0)
            };
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
    let off = if rng.chance(20) {
        rng.v(2000.0)
    } else {
        Vec3::ZERO
    };
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
    let nn = if t.n.len2() > 0.0 {
        t.n.normalized()
    } else {
        Vec3::Z
    };
    let (center, r) = match rng.i(8) {
        0 => (
            t.pt[rng.i(3) as usize] + rng.v(0.3 * size),
            rng.f(0.0, 0.6) * size,
        ),
        1 => {
            let u = rng.f(0.0, 1.0);
            (
                p0 + (p1 - p0) * u + rng.v(0.2 * size),
                rng.f(0.0, 0.5) * size,
            )
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

fn rand_box(rng: &mut Rng) -> BoxPrim {
    let basis = if rng.chance(25) {
        Mat33::IDENTITY
    } else {
        let mut q = [0.0f32; 4];
        loop {
            for c in &mut q {
                *c = rng.f(-1.0, 1.0);
            }
            let l2 = q.iter().map(|c| c * c).sum::<f32>();
            if l2 > 0.01 && l2 <= 1.0 {
                let inv = 1.0 / l2.sqrt();
                for c in &mut q {
                    *c *= inv;
                }
                break;
            }
        }
        Mat33::from_quat(Quat::new(q[0], q[1], q[2], q[3]))
    };
    let size = if rng.chance(15) {
        Vec3::new(
            (rng.i(4) + 1) as f32 * 0.5,
            (rng.i(4) + 1) as f32 * 0.5,
            (rng.i(4) + 1) as f32 * 0.5,
        )
    } else {
        Vec3::new(rng.f(0.05, 2.0), rng.f(0.05, 2.0), rng.f(0.05, 2.0))
    };
    let center = if rng.chance(20) {
        rng.v(500.0)
    } else {
        rng.v(2.0)
    };
    BoxPrim {
        oriented: i32::from(!basis.is_identity()),
        basis,
        center,
        size,
    }
}

/// A triangle related to a box: random around it, large through it, one vertex inside, all
/// inside, nearly parallel to a face (border / face-normal paths), on grid coordinates.
fn box_tri(rng: &mut Rng, b: &BoxPrim) -> Triangle {
    let to_world = |l: Vec3| b.basis.t_mul_vec(l) + b.center;
    let s = b.size;
    let in_box = |rng: &mut Rng| {
        Vec3::new(
            rng.f(-1.0, 1.0) * s.x,
            rng.f(-1.0, 1.0) * s.y,
            rng.f(-1.0, 1.0) * s.z,
        )
    };
    let around = |rng: &mut Rng, k: f32| {
        Vec3::new(rng.f(-k, k) * s.x, rng.f(-k, k) * s.y, rng.f(-k, k) * s.z)
    };
    let l = match rng.i(9) {
        0 => [in_box(rng), around(rng, 3.0), around(rng, 3.0)],
        1 => [in_box(rng), in_box(rng), in_box(rng)],
        2 | 3 => {
            // large, nearly parallel to a face, crossing the box
            let ax = rng.i(3) as usize;
            let h = rng.f(-1.1, 1.1) * s[ax];
            let big = rng.f(3.0, 30.0) * (s.x + s.y + s.z);
            let mut p = [Vec3::ZERO; 3];
            for (k, v) in p.iter_mut().enumerate() {
                let ang = k as f32 * 2.094_395 + rng.f(-0.3, 0.3);
                let mut w = Vec3::ZERO;
                w[(ax + 1) % 3] = big * ang.cos();
                w[(ax + 2) % 3] = big * ang.sin();
                w[ax] = h + rng.f(-0.1, 0.1) * s[ax] * f32::from(u8::from(rng.chance(50)));
                *v = w;
            }
            if rng.chance(50) {
                p.swap(1, 2);
            }
            p
        }
        4 => [around(rng, 6.0), around(rng, 6.0), around(rng, 6.0)],
        5 => {
            let mut g = || {
                let mut c = |n: f32| (rng.i(9) as f32 - 4.0) * 0.5 * n;
                Vec3::new(c(1.0), c(1.0), c(1.0))
            };
            [g(), g(), g()]
        }
        _ => [around(rng, 2.0), around(rng, 2.0), around(rng, 2.0)],
    };
    make_tri(rng, [to_world(l[0]), to_world(l[1]), to_world(l[2])])
}

fn gen_tri_box(seed: u64, n: usize, swapped: bool) -> Vec<Case> {
    let mut rng = Rng(seed);
    (0..n)
        .map(|_| {
            let b = rand_box(&mut rng);
            let t = box_tri(&mut rng, &b);
            let mut w = Vec::new();
            if swapped {
                box_words(&b, &mut w);
                tri_words(&t, &mut w);
            } else {
                tri_words(&t, &mut w);
                box_words(&b, &mut w);
            }
            let k = w.len();
            init_words(&mut rng, &mut w);
            if rng.chance(70) {
                w[k + 14] = BORDER_MARK;
                w[k + 15] = BORDER_MARK;
                w[k + 16] = rng.i(3);
                w[k + 17] = [0, 2, 4, 16, 16][rng.i(5) as usize];
            }
            match rng.i(3) {
                0 => {}
                1 => {
                    w[k + 3] = fw(b.center.x);
                    w[k + 4] = fw(b.center.y);
                    w[k + 5] = fw(b.center.z);
                }
                _ => {
                    let q = t.pt[0] + rng.v(2.0);
                    w[k + 3] = fw(q.x);
                    w[k + 4] = fw(q.y);
                    w[k + 5] = fw(q.z);
                }
            }
            if rng.chance(10) {
                w[k + 21] = rng.i(8); // unk_84 below / at / above 6
            }
            Case { words: w }
        })
        .collect()
}

fn gen_overlap_tri_sph(seed: u64, n: usize, swapped: bool) -> Vec<Case> {
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
            Case { words: w }
        })
        .collect()
}

fn gen_overlap_box_tri(seed: u64, n: usize, swapped: bool) -> Vec<Case> {
    let mut rng = Rng(seed);
    (0..n)
        .map(|_| {
            let mut b = rand_box(&mut rng);
            if rng.chance(15) {
                // a rotated basis flagged as not oriented (the flag alone decides)
                b.oriented = 0;
            }
            let t = box_tri(&mut rng, &b);
            let mut w = Vec::new();
            if swapped {
                tri_words(&t, &mut w);
                box_words(&b, &mut w);
            } else {
                box_words(&b, &mut w);
                tri_words(&t, &mut w);
            }
            Case { words: w }
        })
        .collect()
}

// ------------------------------------------------------------------------- heightfields

type Grid = (i32, i32, Vec<f32>);

thread_local! {
    static GRID: std::cell::RefCell<Grid> = const { std::cell::RefCell::new((0, 0, Vec::new())) };
}

/// The oracle's getHeight callback: the grid inside, `(ix·7 + iy·13)·0.25` outside.
fn test_height(ix: i32, iy: i32) -> f32 {
    GRID.with(|g| {
        let g = g.borrow();
        if ix >= 0 && iy >= 0 && ix < g.0 && iy < g.1 {
            g.2[(iy * g.0 + ix) as usize]
        } else {
            (ix * 7 + iy * 13) as f32 * 0.25
        }
    })
}

fn test_surf(_: i32, _: i32) -> u16 {
    0
}

/// Heightfield primitive words (30) followed by `gx gy heights…`.
fn hf_words(h: &HeightfieldPrim, grid: &Grid, o: &mut Vec<u32>) {
    o.extend(h.basis.to_bits());
    o.push(h.oriented as u32);
    vw(h.origin, o);
    o.extend([fw(h.step[0]), fw(h.step[1]), fw(h.stepr[0]), fw(h.stepr[1])]);
    o.extend([
        h.size[0] as u32,
        h.size[1] as u32,
        h.stride[0] as u32,
        h.stride[1] as u32,
    ]);
    o.push(fw(h.height_scale));
    o.push(u32::from(h.type_mask) | u32::from(h.height_mask) << 16);
    o.push(h.type_hole as u32);
    o.push(h.type_power as u32);
    o.extend([0; 5]); // padding + the two callback pointers (replaced by the oracle)
    o.push(grid.0 as u32);
    o.push(grid.1 as u32);
    o.extend(grid.2.iter().map(|x| fw(*x)));
}

/// Parse heightfield words and install the grid for [`test_height`]; returns the primitive
/// and the number of words consumed.
fn words_hf(w: &[u32]) -> (HeightfieldPrim, usize) {
    let h = HeightfieldPrim {
        basis: Mat33::from_bits(w[0..9].try_into().unwrap()),
        oriented: w[9] as i32,
        origin: wv(&w[10..]),
        step: [wf(w[13]), wf(w[14])],
        stepr: [wf(w[15]), wf(w[16])],
        size: [w[17] as i32, w[18] as i32],
        stride: [w[19] as i32, w[20] as i32],
        height_scale: wf(w[21]),
        type_mask: w[22] as u16,
        height_mask: (w[22] >> 16) as u16,
        type_hole: w[23] as i32,
        type_power: w[24] as i32,
        get_height: Some(test_height),
        get_surf_type: Some(test_surf),
    };
    let gx = w[30] as i32;
    let gy = w[31] as i32;
    let n = (gx * gy) as usize;
    let heights: Vec<f32> = w[32..32 + n].iter().map(|x| wf(*x)).collect();
    GRID.with(|g| *g.borrow_mut() = (gx, gy, heights));
    (h, 32 + n)
}

fn rand_rot(rng: &mut Rng) -> Mat33 {
    let mut q = [0.0f32; 4];
    loop {
        for c in &mut q {
            *c = rng.f(-1.0, 1.0);
        }
        let l2 = q.iter().map(|c| c * c).sum::<f32>();
        if l2 > 0.01 && l2 <= 1.0 {
            let inv = 1.0 / l2.sqrt();
            for c in &mut q {
                *c *= inv;
            }
            return Mat33::from_quat(Quat::new(q[0], q[1], q[2], q[3]));
        }
    }
}

fn rand_hf(rng: &mut Rng) -> (HeightfieldPrim, Grid) {
    let basis = if rng.chance(70) {
        Mat33::IDENTITY
    } else {
        rand_rot(rng)
    };
    let steps = [0.5f32, 1.0, 2.0, 0.37];
    let step = [steps[rng.i(4) as usize], steps[rng.i(4) as usize]];
    let size = [(rng.i(7) + 1) as i32, (rng.i(7) + 1) as i32];
    let (gx, gy) = if rng.chance(85) {
        (size[0] + 1, size[1] + 1)
    } else {
        (size[0], size[1] - 1)
    };
    let amp = rng.f(0.0, 3.0);
    let heights = (0..gx * gy)
        .map(|_| {
            if rng.chance(10) {
                0.0
            } else {
                rng.f(-amp, amp)
            }
        })
        .collect();
    let scales = [1.0f32, 0.5, 0.01];
    let h = HeightfieldPrim {
        oriented: i32::from(!basis.is_identity()),
        basis,
        origin: if rng.chance(50) {
            Vec3::ZERO
        } else {
            rng.v(3.0)
        },
        step,
        stepr: [1.0 / step[0], 1.0 / step[1]],
        size,
        stride: [0, 0],
        height_scale: scales[rng.i(3) as usize],
        ..HeightfieldPrim::default()
    };
    (h, (gx, gy, heights))
}

/// A point over a heightfield (world space): inside the grid, near its border or outside.
fn hf_point(rng: &mut Rng, h: &HeightfieldPrim) -> Vec3 {
    let ex = h.size[0] as f32 * h.step[0];
    let ey = h.size[1] as f32 * h.step[1];
    let l = Vec3::new(
        rng.f(-0.3, 1.3) * ex,
        rng.f(-0.3, 1.3) * ey,
        rng.f(-3.0, 4.0),
    );
    h.basis.t_mul_vec(l) + h.origin
}

fn gen_overlap_hf_sph(seed: u64, n: usize, swapped: bool) -> Vec<Case> {
    let mut rng = Rng(seed);
    (0..n)
        .map(|_| {
            let (h, g) = rand_hf(&mut rng);
            let s = SpherePrim {
                center: hf_point(&mut rng, &h),
                r: rng.f(0.0, 2.5),
            };
            let mut w = Vec::new();
            if swapped {
                sph_words(&s, &mut w);
                hf_words(&h, &g, &mut w);
            } else {
                hf_words(&h, &g, &mut w);
                sph_words(&s, &mut w);
            }
            Case { words: w }
        })
        .collect()
}

fn check_hf_sph(probe: &str, rows: &[Row], swapped: bool) -> usize {
    let mut hits = 0;
    for (i, r) in rows.iter().enumerate() {
        let got = if swapped {
            let s = words_sph(&r.input);
            let (h, _) = words_hf(&r.input[4..]);
            tp::overlap_sph_hf(&s, &h)
        } else {
            let (h, k) = words_hf(&r.input);
            let s = words_sph(&r.input[k..]);
            tp::overlap_hf_sph(&h, &s)
        };
        hits += usize::from(r.ret != 0);
        assert_eq!(
            i32::from(got),
            r.ret,
            "{probe} row {i}: input {:08x?}",
            r.input
        );
    }
    hits
}

fn gen_overlap_box_hf(seed: u64, n: usize, swapped: bool) -> Vec<Case> {
    let mut rng = Rng(seed);
    (0..n)
        .map(|_| {
            let (h, g) = rand_hf(&mut rng);
            let mut b = rand_box(&mut rng);
            b.center = hf_point(&mut rng, &h);
            if rng.chance(30) {
                // small boxes: few cells, triangle path
                b.size *= 0.3;
            }
            if rng.chance(10) {
                b.oriented = 0;
            }
            let mut w = Vec::new();
            if swapped {
                hf_words(&h, &g, &mut w);
                box_words(&b, &mut w);
            } else {
                box_words(&b, &mut w);
                hf_words(&h, &g, &mut w);
            }
            Case { words: w }
        })
        .collect()
}

fn check_box_hf(probe: &str, rows: &[Row], swapped: bool) -> usize {
    let mut hits = 0;
    for (i, r) in rows.iter().enumerate() {
        let got = if swapped {
            let (h, k) = words_hf(&r.input);
            let b = words_box(&r.input[k..]);
            tp::overlap_hf_box(&h, &b)
        } else {
            let b = words_box(&r.input);
            let (h, _) = words_hf(&r.input[16..]);
            tp::overlap_box_hf(&b, &h)
        };
        hits += usize::from(r.ret != 0);
        assert_eq!(
            i32::from(got),
            r.ret,
            "{probe} row {i}: input {:08x?}",
            r.input
        );
    }
    hits
}

// -------------------------------------------------------------------------------- tests

#[test]
fn tri_tri_matches_sub_35159410() {
    let rows = golden("p2a2_i_tri_tri", || gen_tri_tri(1, 600));
    let hits = check_inters(
        "tri_tri",
        &rows,
        12,
        12,
        words_tri,
        words_tri,
        tp::intersect_tri_tri,
    );
    assert!(hits > 60, "too few contacts in the golden set: {hits}");
}

#[test]
fn fuzz_tri_tri() {
    if let Some(rows) = live("p2a2_i_tri_tri", gen_tri_tri(1001, 20000)) {
        check_inters(
            "tri_tri",
            &rows,
            12,
            12,
            words_tri,
            words_tri,
            tp::intersect_tri_tri,
        );
    }
}

#[test]
fn ray_tri_matches_sub_3515ab20() {
    let rows = golden("p2a2_i_ray_tri", || gen_ray_tri(2, 600, false));
    let hits = check_inters(
        "ray_tri",
        &rows,
        6,
        12,
        words_ray,
        words_tri,
        tp::intersect_ray_tri,
    );
    assert!(hits > 60, "too few contacts in the golden set: {hits}");
}

#[test]
fn tri_ray_matches_sub_3515b1d0() {
    let rows = golden("p2a2_i_tri_ray", || gen_ray_tri(3, 300, true));
    let hits = check_inters(
        "tri_ray",
        &rows,
        12,
        6,
        words_tri,
        words_ray,
        tp::intersect_tri_ray,
    );
    assert!(hits > 30, "too few contacts in the golden set: {hits}");
}

#[test]
fn fuzz_ray_tri() {
    if let Some(rows) = live("p2a2_i_ray_tri", gen_ray_tri(1002, 20000, false)) {
        check_inters(
            "ray_tri",
            &rows,
            6,
            12,
            words_ray,
            words_tri,
            tp::intersect_ray_tri,
        );
    }
    if let Some(rows) = live("p2a2_i_tri_ray", gen_ray_tri(1003, 5000, true)) {
        check_inters(
            "tri_ray",
            &rows,
            12,
            6,
            words_tri,
            words_ray,
            tp::intersect_tri_ray,
        );
    }
}

#[test]
fn tri_sph_matches_sub_35159de0() {
    let rows = golden("p2a2_i_tri_sph", || gen_tri_sph(4, 600, false));
    let hits = check_inters(
        "tri_sph",
        &rows,
        12,
        4,
        words_tri,
        words_sph,
        tp::intersect_tri_sph,
    );
    assert!(hits > 60, "too few contacts in the golden set: {hits}");
}

#[test]
fn sph_tri_matches_sub_3515aaa0() {
    let rows = golden("p2a2_i_sph_tri", || gen_tri_sph(5, 300, true));
    let hits = check_inters(
        "sph_tri",
        &rows,
        4,
        12,
        words_sph,
        words_tri,
        tp::intersect_sph_tri,
    );
    assert!(hits > 30, "too few contacts in the golden set: {hits}");
}

#[test]
fn fuzz_tri_sph() {
    if let Some(rows) = live("p2a2_i_tri_sph", gen_tri_sph(1004, 20000, false)) {
        check_inters(
            "tri_sph",
            &rows,
            12,
            4,
            words_tri,
            words_sph,
            tp::intersect_tri_sph,
        );
    }
    if let Some(rows) = live("p2a2_i_sph_tri", gen_tri_sph(1005, 5000, true)) {
        check_inters(
            "sph_tri",
            &rows,
            4,
            12,
            words_sph,
            words_tri,
            tp::intersect_sph_tri,
        );
    }
}

#[test]
fn tri_box_matches_sub_35162aa0() {
    let rows = golden("p2a2_i_tri_box", || gen_tri_box(6, 800, false));
    let hits = check_inters(
        "tri_box",
        &rows,
        12,
        16,
        words_tri,
        words_box,
        tp::intersect_tri_box,
    );
    assert!(hits > 80, "too few contacts in the golden set: {hits}");
}

#[test]
fn box_tri_matches_sub_35164780() {
    let rows = golden("p2a2_i_box_tri", || gen_tri_box(7, 300, true));
    let hits = check_inters(
        "box_tri",
        &rows,
        16,
        12,
        words_box,
        words_tri,
        tp::intersect_box_tri,
    );
    assert!(hits > 30, "too few contacts in the golden set: {hits}");
}

#[test]
fn fuzz_tri_box() {
    if let Some(rows) = live("p2a2_i_tri_box", gen_tri_box(1006, 30000, false)) {
        check_inters(
            "tri_box",
            &rows,
            12,
            16,
            words_tri,
            words_box,
            tp::intersect_tri_box,
        );
    }
    if let Some(rows) = live("p2a2_i_box_tri", gen_tri_box(1007, 5000, true)) {
        check_inters(
            "box_tri",
            &rows,
            16,
            12,
            words_box,
            words_tri,
            tp::intersect_box_tri,
        );
    }
}

#[test]
fn overlap_tri_sph_matches_sub_35197530() {
    let rows = golden("p2a2_o_tri_sph", || gen_overlap_tri_sph(8, 1500, false));
    let hits = check_overlap(
        "o_tri_sph",
        &rows,
        12,
        words_tri,
        words_sph,
        tp::overlap_tri_sph,
    );
    assert!(hits > 150 && hits < 1350, "unbalanced golden set: {hits}");
    let rows = golden("p2a2_o_sph_tri", || gen_overlap_tri_sph(9, 300, true));
    check_overlap(
        "o_sph_tri",
        &rows,
        4,
        words_sph,
        words_tri,
        tp::overlap_sph_tri,
    );
}

#[test]
fn overlap_box_tri_matches_sub_351968f0() {
    let rows = golden("p2a2_o_box_tri", || gen_overlap_box_tri(10, 1500, false));
    let hits = check_overlap(
        "o_box_tri",
        &rows,
        16,
        words_box,
        words_tri,
        tp::overlap_box_tri,
    );
    assert!(hits > 150 && hits < 1350, "unbalanced golden set: {hits}");
    let rows = golden("p2a2_o_tri_box", || gen_overlap_box_tri(11, 300, true));
    check_overlap(
        "o_tri_box",
        &rows,
        12,
        words_tri,
        words_box,
        tp::overlap_tri_box,
    );
}

#[test]
fn overlap_hf_sph_matches_sub_351975e0() {
    let rows = golden("p2a2_o_hf_sph", || gen_overlap_hf_sph(12, 1000, false));
    let hits = check_hf_sph("o_hf_sph", &rows, false);
    assert!(hits > 100 && hits < 900, "unbalanced golden set: {hits}");
    let rows = golden("p2a2_o_sph_hf", || gen_overlap_hf_sph(13, 300, true));
    check_hf_sph("o_sph_hf", &rows, true);
}

#[test]
fn fuzz_overlaps() {
    if let Some(rows) = live("p2a2_o_tri_sph", gen_overlap_tri_sph(1008, 30000, false)) {
        check_overlap(
            "o_tri_sph",
            &rows,
            12,
            words_tri,
            words_sph,
            tp::overlap_tri_sph,
        );
    }
    if let Some(rows) = live("p2a2_o_box_tri", gen_overlap_box_tri(1009, 30000, false)) {
        check_overlap(
            "o_box_tri",
            &rows,
            16,
            words_box,
            words_tri,
            tp::overlap_box_tri,
        );
    }
    if let Some(rows) = live("p2a2_o_hf_sph", gen_overlap_hf_sph(1010, 20000, false)) {
        check_hf_sph("o_hf_sph", &rows, false);
    }
}

#[test]
fn overlap_box_hf_matches_sub_351986e0() {
    let rows = golden("p2a2_o_box_hf", || gen_overlap_box_hf(14, 1000, false));
    let hits = check_box_hf("o_box_hf", &rows, false);
    assert!(hits > 100 && hits < 900, "unbalanced golden set: {hits}");
    let rows = golden("p2a2_o_hf_box", || gen_overlap_box_hf(15, 300, true));
    check_box_hf("o_hf_box", &rows, true);
}

#[test]
fn fuzz_overlap_box_hf() {
    if let Some(rows) = live("p2a2_o_box_hf", gen_overlap_box_hf(1011, 20000, false)) {
        check_box_hf("o_box_hf", &rows, false);
    }
}

#[test]
fn table_cells_dispatch_to_the_ported_functions() {
    use aaemu_physics::collision::pairs::PairTables;
    use aaemu_physics::geometry::primitives::Primitive;
    use aaemu_physics::ids::GeomType as G;
    let t = PairTables::build();
    let tri = Triangle {
        pt: [
            Vec3::ZERO,
            Vec3::new(2.0, 0.0, 0.0),
            Vec3::new(0.0, 2.0, 0.0),
        ],
        n: Vec3::Z,
    };
    let sph = SpherePrim {
        center: Vec3::new(0.5, 0.5, 0.2),
        r: 0.5,
    };
    let bx = BoxPrim::aligned(Vec3::new(0.5, 0.5, 0.0), Vec3::splat(0.3));
    let (pt, ps, pb) = (
        Primitive::Triangle(tri),
        Primitive::Sphere(sph),
        Primitive::Box(bx),
    );
    let mut a = PrimInters::default();
    let mut b = PrimInters::default();
    assert_eq!(
        t.intersect_fn(G::TriMesh, G::Sphere)(&pt, &ps, &mut a),
        tp::intersect_tri_sph(&tri, &sph, &mut b)
    );
    assert_eq!(a, b);
    let (mut a, mut b) = (PrimInters::default(), PrimInters::default());
    assert!(t.intersect_fn(G::Box, G::TriMesh)(&pb, &pt, &mut a));
    assert!(tp::intersect_box_tri(&bx, &tri, &mut b));
    assert_eq!(a, b);
    assert!(t.overlap_fn(G::TriMesh, G::Sphere)(&pt, &ps));
    assert!(t.overlap_fn(G::Box, G::TriMesh)(&pb, &pt));
    assert!(t.overlap_fn(G::TriMesh, G::Box)(&pt, &pb));
    // Heightfield rows/columns of INTERSECT stay empty.
    let mut c = PrimInters::default();
    let hf = Primitive::Heightfield(HeightfieldPrim::default());
    assert!(!t.intersect_fn(G::Heightfield, G::TriMesh)(
        &hf, &pt, &mut c
    ));
}
