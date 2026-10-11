//! P2b: box–box linear unprojection (0x3517CD60) and its helpers sub_3516E400 (polygon clip by a
//! line) / sub_3516E6C0 (segment clip to a slab) against the DLL (`physoracle --p2b`).

use aaemu_physics::collision::contact::UnprojParams;
use aaemu_physics::collision::swept::box_box::clip_polygon_line;
use aaemu_physics::collision::swept::common::clip_segment_slab;
use aaemu_physics::collision::swept::probe::{check, oracle_raw, seed_of, Case, Rng};
use aaemu_physics::geometry::primitives::{BoxPrim, Primitive};
use aaemu_physics::math::{Mat33, Vec3};
use aaemu_physics::oracle::{golden_dir, oracle_path, BLESS_ENV};
use std::fmt::Write as _;

// ------------------------------------------------------------------------------- generators

/// A signed permutation matrix (a multiple of 90° turns; exact).
fn signed_perm(rng: &mut Rng) -> Mat33 {
    let perms = [
        [0, 1, 2],
        [0, 2, 1],
        [1, 0, 2],
        [1, 2, 0],
        [2, 0, 1],
        [2, 1, 0],
    ];
    let p = rng.pick(&perms);
    let mut m = [0f32; 9];
    for (r, &c) in p.iter().enumerate() {
        m[3 * r + c] = if rng.chance(0.5) { 1.0 } else { -1.0 };
    }
    Mat33::from_array(m)
}

/// Rotation about one coordinate axis by a quantised angle (one exactly parallel face pair).
fn axis_rot(rng: &mut Rng) -> Mat33 {
    let ang = if rng.chance(0.25) {
        // small turn: edges almost parallel to the other box's face normals
        rng.f(-0.03, 0.03)
    } else {
        rng.below(24) as f32 * (std::f32::consts::PI / 12.0)
    };
    let (s, c) = ang.sin_cos();
    let m = match rng.below(3) {
        0 => [1.0, 0.0, 0.0, 0.0, c, s, 0.0, -s, c],
        1 => [c, 0.0, -s, 0.0, 1.0, 0.0, s, 0.0, c],
        _ => [c, s, 0.0, -s, c, 0.0, 0.0, 0.0, 1.0],
    };
    Mat33::from_array(m)
}

fn mul(a: &Mat33, b: &Mat33) -> Mat33 {
    a.mul_mat(b)
}

fn mk_box(basis: Mat33, center: Vec3, size: Vec3) -> BoxPrim {
    BoxPrim {
        basis,
        oriented: i32::from(!basis.is_identity()),
        center,
        size,
    }
}

fn size(rng: &mut Rng) -> Vec3 {
    match rng.below(10) {
        0 => Vec3::splat(rng.quant(0.25, 2.0, 0.25)),
        1 => Vec3::new(rng.f(0.05, 2.0), rng.f(0.05, 2.0), 0.001),
        9 => Vec3::new(0.001, rng.f(0.05, 2.0), rng.f(0.05, 2.0)),
        2 => Vec3::new(
            rng.quant(0.25, 3.0, 0.25),
            rng.quant(0.25, 3.0, 0.25),
            rng.quant(0.25, 3.0, 0.25),
        ),
        _ => Vec3::new(rng.f(0.05, 3.0), rng.f(0.05, 3.0), rng.f(0.05, 3.0)),
    }
}

fn rotate(m: &Mat33, v: Vec3) -> Vec3 {
    // basis rows are the box axes: world = Mᵀ·v
    m.t_mul_vec(v)
}

/// Params: dir = 0 / unit / axis / against the offset / non-unit; tmax / tmin / minPtDist /
/// maxcos as the engine uses them.
fn params(rng: &mut Rng, off: Vec3, frame: &Mat33) -> UnprojParams {
    let mut p = UnprojParams::default();
    match rng.below(7) {
        0 | 1 => {} // least-penetration
        2 => p.dir = rng.unit_or_axis(0.4),
        3 => p.dir = rotate(frame, rng.unit_or_axis(1.0)),
        4 => {
            let l = off.len();
            if l > 1e-6 {
                p.dir = Vec3::new(-off.x / l, -off.y / l, -off.z / l);
            } else {
                p.dir = rng.unit();
            }
        }
        5 => {
            let u = rng.unit();
            let s = rng.f(0.1, 5.0);
            p.dir = Vec3::new(u.x * s, u.y * s, u.z * s);
        }
        _ => {
            let u = rng.unit_or_axis(0.5);
            p.dir = Vec3::new(-u.x, -u.y, -u.z);
        }
    }
    p.tmax = match rng.below(40) {
        0 => f32::INFINITY,
        1 => -rng.f(0.0, 1.0),
        2..=10 => 1e10,
        11..=20 => rng.f(0.0, 1.0),
        _ => rng.f(0.0, 10.0),
    };
    p.tmin = if rng.chance(0.85) {
        0.0
    } else {
        rng.f(-1.0, 1.0)
    };
    p.min_pt_dist = match rng.below(5) {
        4 => 0.0,
        0 => 0.001,
        1 => 0.01,
        2 => 0.05,
        _ => rng.f(0.0, 0.1),
    };
    p.max_cos = match rng.below(4) {
        0 => 0.1,
        1 => 0.999_847_7,
        2 => rng.f(0.9, 1.0),
        _ => rng.f(0.0, 0.5),
    };
    p.flag = i32::from(rng.chance(0.5));
    p.vel = rng.f(0.0, 5.0);
    p
}

/// The general generator: oriented / axis-aligned / quantised-rotation boxes, overlapping,
/// touching and separated, all dir kinds, area NULL / 256 / tiny.
fn gen_case(rng: &mut Rng, area_bias: bool) -> Case {
    let mode = rng.below(7);
    let sa = size(rng);
    let sb = if rng.chance(0.15) { sa } else { size(rng) };
    let (ra, rb) = match mode {
        0 => (rng.rotation(), rng.rotation()),
        1 => (Mat33::IDENTITY, Mat33::IDENTITY),
        2 => (signed_perm(rng), signed_perm(rng)),
        3 => {
            let r = rng.rotation();
            (r, mul(&signed_perm(rng), &r))
        }
        4 => (Mat33::IDENTITY, axis_rot(rng)),
        5 => {
            let r = rng.rotation();
            (r, mul(&axis_rot(rng), &r))
        }
        _ => (
            Mat33::IDENTITY,
            if rng.chance(0.5) {
                signed_perm(rng)
            } else {
                rng.rotation()
            },
        ),
    };
    let ca = if rng.chance(0.5) {
        Vec3::new(
            rng.quant(-8.0, 8.0, 0.25),
            rng.quant(-8.0, 8.0, 0.25),
            rng.quant(-8.0, 8.0, 0.25),
        )
    } else {
        rng.vec(-10.0, 10.0)
    };
    // offset of B in A's frame
    let reach = (sa.len() + sb.len()) * rng.f(0.0, 1.3);
    let off_local = match rng.below(6) {
        0 => {
            // along an A face axis, touching or overlapping by a quantised amount
            let k = rng.below(3) as usize;
            let mut v = [
                rng.quant(-0.5, 0.5, 0.25),
                rng.quant(-0.5, 0.5, 0.25),
                rng.quant(-0.5, 0.5, 0.25),
            ];
            let sgn = if rng.chance(0.5) { 1.0 } else { -1.0 };
            let sum = sa.to_array()[k] + sb.to_array()[k];
            v[k] = sgn * (sum - rng.quant(0.0, sum, 0.125));
            Vec3::from_array(v)
        }
        1 => Vec3::ZERO,
        2 => Vec3::new(
            rng.quant(-3.0, 3.0, 0.5),
            rng.quant(-3.0, 3.0, 0.5),
            rng.quant(-3.0, 3.0, 0.5),
        ),
        _ => {
            let u = rng.unit_or_axis(0.3);
            Vec3::new(u.x * reach, u.y * reach, u.z * reach)
        }
    };
    let off = rotate(&ra, off_local);
    let cb = Vec3::new(ca.x + off.x, ca.y + off.y, ca.z + off.z);
    let (sb, rb, cb) = if mode == 1 && rng.chance(0.1) {
        (sa, ra, ca)
    } else {
        (sb, rb, cb)
    };
    let a = mk_box(ra, ca, sa);
    let b = mk_box(rb, cb, sb);
    let p = params(rng, off, &ra);
    let c = Case::new(0, &Primitive::Box(a), &Primitive::Box(b), &p);
    let pa = if area_bias { 0.9 } else { 0.6 };
    // tmax = inf makes num = 0*inf = NaN after the first candidate; with an area the NaN polygon
    // overflows the binary's 8-point clip buffers on the stack (not reproducible), so those
    // cases go without an area (they still reach the den = 0 -> t = 0 branch).
    if rng.chance(pa) && p.tmax != f32::INFINITY {
        let nmax = if rng.chance(0.75) {
            256
        } else {
            1 + rng.below(4)
        };
        let mut c = c.with_area(nmax, rng.f(0.0, 0.05));
        if rng.chance(0.03) {
            // a non-zero npt on entry short-circuits the area search
            if let Some(h) = c.area.as_mut() {
                h[1] = 1 + rng.below(3);
            }
        }
        c
    } else {
        c
    }
}

/// A thin plate lying (almost) flat on one of A's faces, pushed out sideways (`dir` in the face
/// plane): the face–face pass is skipped (`|D_m| <= 0.01`) and the edge–face pass sees edges
/// nearly perpendicular to the face (the `-f` normal fallback).
fn gen_plate(rng: &mut Rng) -> Case {
    let sa = Vec3::new(
        rng.quant(0.25, 2.0, 0.25),
        rng.quant(0.25, 2.0, 0.25),
        rng.quant(0.25, 2.0, 0.25),
    );
    let ra = if rng.chance(0.5) {
        Mat33::IDENTITY
    } else {
        rng.rotation()
    };
    let j = rng.below(3) as usize;
    let thin = rng.pick(&[0.001f32, 0.002, 0.01]);
    let mut sb = [rng.f(0.2, 2.0), rng.f(0.2, 2.0), rng.f(0.2, 2.0)];
    sb[j] = thin;
    // plate basis: A's basis, slightly tilted about one of the in-plane axes
    let tilt = rng.f(-0.03, 0.03);
    let (sn, cs) = tilt.sin_cos();
    let q = (j + 1 + rng.below(2) as usize) % 3;
    let mut rot = [1.0f32, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0];
    let (u, v) = ((q + 1) % 3, (q + 2) % 3);
    rot[3 * u + u] = cs;
    rot[3 * u + v] = sn;
    rot[3 * v + u] = -sn;
    rot[3 * v + v] = cs;
    let rb = mul(&Mat33::from_array(rot), &ra);
    let ca = Vec3::new(
        rng.quant(-4.0, 4.0, 0.5),
        rng.quant(-4.0, 4.0, 0.5),
        rng.quant(-4.0, 4.0, 0.5),
    );
    let side = if rng.chance(0.5) { 1.0 } else { -1.0 };
    let mut o = [0f32; 3];
    o[(j + 1) % 3] = rng.f(-1.0, 1.0) * sa.to_array()[(j + 1) % 3];
    o[(j + 2) % 3] = rng.f(-1.0, 1.0) * sa.to_array()[(j + 2) % 3];
    o[j] = side * (sa.to_array()[j] + thin + rng.f(-0.02, 0.01));
    let offw = rotate(&ra, Vec3::from_array(o));
    let cb = Vec3::new(ca.x + offw.x, ca.y + offw.y, ca.z + offw.z);
    let a = mk_box(ra, ca, sa);
    let b = mk_box(rb, cb, Vec3::from_array(sb));
    let mut p = params(rng, offw, &ra);
    // dir in the face plane
    let mut dl = [0f32; 3];
    dl[(j + 1) % 3] = rng.f(-1.0, 1.0);
    dl[(j + 2) % 3] = rng.f(-1.0, 1.0);
    if rng.chance(0.8) {
        p.dir = rotate(&ra, Vec3::from_array(dl));
    }
    p.max_cos = rng.f(0.0, 0.4);
    if rng.chance(0.5) {
        p.min_pt_dist = 0.0;
    }
    if p.tmax == f32::INFINITY {
        p.tmax = 1e10;
    }
    let nmax = if rng.chance(0.75) {
        256
    } else {
        1 + rng.below(4)
    };
    Case::new(0, &Primitive::Box(a), &Primitive::Box(b), &p).with_area(nmax, 0.01)
}

fn gen(rng: &mut Rng) -> Case {
    if rng.chance(0.1) {
        return gen_plate(rng);
    }
    gen_case(rng, false)
}

fn gen_area(rng: &mut Rng) -> Case {
    gen_case(rng, true)
}

#[test]
fn box_box_linear_matches_dll() {
    check("p2b_box_box", 300, &gen);
}

#[test]
fn box_box_linear_plate_matches_dll() {
    check("p2b_box_box_plate", 150, &gen_plate);
}

#[test]
fn box_box_linear_area_matches_dll() {
    check("p2b_box_box_area", 250, &gen_area);
}

// ------------------------------------------------------------------------------- helpers

/// `BBCLIP` / `BBSEG` probes (sub_3516E400 / sub_3516E6C0) with a golden of `cmd` / `= answer`
/// line pairs; regenerated from the oracle when available.
fn helper_check(
    name: &str,
    n: usize,
    gen: &dyn Fn(&mut Rng) -> String,
    port: &dyn Fn(&str) -> String,
) {
    let golden = golden_dir().join(format!("{name}.trace"));
    let recs: Vec<(String, String)> = if let Some(exe) = oracle_path() {
        let mut rng = Rng::new(seed_of(name));
        let mut cmds: Vec<String> = (0..n).map(|_| gen(&mut rng)).collect();
        let answers = oracle_raw(&exe, &cmds).unwrap_or_else(|e| panic!("{name}: {e}"));
        let mut text = format!("p2b-helper 1 {name}\n");
        for (c, a) in cmds.iter().zip(&answers) {
            let _ = write!(text, "{c}\n= {a}\n");
        }
        let stored = std::fs::read_to_string(&golden)
            .ok()
            .map(|s| s.replace("\r\n", "\n"));
        let bless = std::env::var_os(BLESS_ENV).is_some_and(|v| v != "0");
        if stored.as_deref() != Some(text.as_str()) {
            assert!(
                bless || stored.is_none(),
                "{} differs from the oracle; set {BLESS_ENV}=1 to rewrite",
                golden.display()
            );
            std::fs::write(&golden, &text).unwrap();
        }
        let mut answers = answers;
        let live: usize = std::env::var("AAEMU_PHYS_P2B_LIVE")
            .ok()
            .and_then(|v| v.parse().ok())
            .unwrap_or(0);
        if live > 0 {
            let mut rng = Rng::new(seed_of(name) ^ 0x5DEE_CE66_D1CE_4E5B);
            let more: Vec<String> = (0..live).map(|_| gen(&mut rng)).collect();
            answers.extend(oracle_raw(&exe, &more).unwrap_or_else(|e| panic!("{name}: {e}")));
            cmds.extend(more);
        }
        cmds.into_iter().zip(answers).collect()
    } else {
        let text = std::fs::read_to_string(&golden)
            .unwrap_or_else(|e| panic!("golden {} missing: {e}", golden.display()))
            .replace("\r\n", "\n");
        let mut it = text.lines();
        assert!(it.next().is_some_and(|h| h.starts_with("p2b-helper 1")));
        let mut v = Vec::new();
        while let Some(c) = it.next() {
            let a = it
                .next()
                .and_then(|a| a.strip_prefix("= "))
                .expect("bad golden");
            v.push((c.to_string(), a.to_string()));
        }
        v
    };
    let mut bad = 0;
    let mut report = String::new();
    for (c, want) in &recs {
        let got = port(c);
        if &got != want {
            bad += 1;
            if bad <= 3 {
                let _ = write!(report, "\n  cmd:    {c}\n  oracle: {want}\n  port:   {got}");
            }
        }
    }
    assert!(bad == 0, "{name}: {bad} of {} differ{report}", recs.len());
}

fn words(s: &str) -> Vec<u32> {
    s.split_whitespace()
        .skip(1)
        .map(|w| u32::from_str_radix(w, 16).unwrap())
        .collect()
}

fn gen_clip(rng: &mut Rng) -> String {
    let n = rng.below(9) as usize;
    let code = rng.below(4);
    let edge = rng.below(0x40);
    let half = [rng.quant(0.0, 2.0, 0.25), rng.quant(0.0, 2.0, 0.25)];
    let mut s = format!(
        "BBCLIP {n:x} {code:x} {edge:x} {:08x} {:08x}",
        half[0].to_bits(),
        half[1].to_bits()
    );
    // a convex-ish polygon around a random centre (sometimes quantised to hit the line exactly)
    let cx = rng.f(-2.0, 2.0);
    let cy = rng.f(-2.0, 2.0);
    let q = rng.chance(0.4);
    let nan = rng.chance(0.1);
    let r = rng.f(0.1, 3.0);
    for k in 0..n {
        let ang = (k as f32 + rng.f(0.0, 0.8)) * std::f32::consts::TAU / n as f32;
        let (mut x, mut y) = (cx + r * ang.cos(), cy + r * ang.sin());
        if nan && rng.chance(0.3) {
            // NaN coordinates (both signs): every product test counts as a crossing
            x = f32::from_bits(if rng.chance(0.5) {
                0xFFC0_0000
            } else {
                0x7FC0_0000
            });
        }
        if nan && rng.chance(0.3) {
            y = f32::from_bits(if rng.chance(0.5) {
                0xFFC0_0000
            } else {
                0x7FC0_0000
            });
        }
        if q {
            x = (x * 4.0).round() / 4.0;
            y = (y * 4.0).round() / 4.0;
        }
        let _ = write!(s, " {:08x} {:08x}", x.to_bits(), y.to_bits());
    }
    for _ in 0..n {
        let _ = write!(s, " {:x}", rng.below(0x20));
    }
    s
}

fn port_clip(cmd: &str) -> String {
    let w = words(cmd);
    let n = w[0] as usize;
    let code = w[1] as i32;
    let edge = w[2] as i32;
    let half = [f32::from_bits(w[3]), f32::from_bits(w[4])];
    let src: Vec<f32> = w[5..5 + 2 * n].iter().map(|&x| f32::from_bits(x)).collect();
    let ids: Vec<i32> = w[5 + 2 * n..5 + 3 * n].iter().map(|&x| x as i32).collect();
    let mut dst = vec![0f32; 128];
    let mut dids = vec![0i32; 64];
    for i in 0..64u32 {
        dst[2 * i as usize] = f32::from_bits(0xDEAD_0000 + i);
        dst[2 * i as usize + 1] = f32::from_bits(0xDEAD_4000 + i);
        dids[i as usize] = (0xBEEF_0000u32 + i) as i32;
    }
    let r = clip_polygon_line(&src, &ids, n as i32, half, code, edge, &mut dst, &mut dids);
    let mut s = format!("clip {:08x}", r as u32);
    for i in 0..((r + 1).min(64) as usize) {
        let _ = write!(
            s,
            " {:08x} {:08x} {:08x}",
            dst[2 * i].to_bits(),
            dst[2 * i + 1].to_bits(),
            dids[i] as u32
        );
    }
    s
}

fn gen_seg(rng: &mut Rng) -> String {
    let h = if rng.chance(0.5) {
        rng.quant(0.0, 2.0, 0.25)
    } else {
        rng.f(0.0, 2.0)
    };
    let i = rng.below(2);
    let mut v = [0f32; 4];
    for x in &mut v {
        *x = if rng.chance(0.3) {
            rng.quant(-3.0, 3.0, 0.25)
        } else {
            rng.f(-3.0, 3.0)
        };
    }
    if rng.chance(0.1) {
        v[i as usize + 2] = v[i as usize];
    }
    let mut s = format!(
        "BBSEG {:08x} {i:x} {:x} {:x}",
        h.to_bits(),
        rng.below(0x40),
        rng.below(0x40)
    );
    for x in v {
        let _ = write!(s, " {:08x}", x.to_bits());
    }
    for _ in 0..4 {
        let _ = write!(s, " {:x}", rng.below(0x100));
    }
    s
}

fn port_seg(cmd: &str) -> String {
    let w = words(cmd);
    let h = f32::from_bits(w[0]);
    let i = w[1] as usize;
    let (id0, id1) = (w[2] as i32, w[3] as i32);
    let mut seg = [w[4], w[5], w[6], w[7]].map(f32::from_bits);
    let mut ids0 = [w[8] as i32, w[9] as i32];
    let mut ids1 = [w[10] as i32, w[11] as i32];
    let r = clip_segment_slab(&mut seg, h, i, &mut ids0, &mut ids1, id0, id1);
    let mut s = format!("seg {:08x}", r as u32);
    for x in seg {
        let _ = write!(s, " {:08x}", x.to_bits());
    }
    let _ = write!(
        s,
        " {:08x} {:08x} {:08x} {:08x}",
        ids0[0] as u32, ids0[1] as u32, ids1[0] as u32, ids1[1] as u32
    );
    s
}

#[test]
fn clip_polygon_line_matches_dll() {
    helper_check("p2b_box_box_clip", 300, &gen_clip, &port_clip);
}

#[test]
fn clip_segment_slab_matches_dll() {
    helper_check("p2b_box_box_seg", 300, &gen_seg, &port_seg);
}

/// A case generator.
type Gen = dyn Fn(&mut Rng) -> Case;

/// Branch distribution of the generators (`cargo test --test p2b_box_box -- --ignored
/// --nocapture`); run through the port, which the checks above verify against the DLL.
#[test]
#[ignore]
fn box_box_branch_stats() {
    use aaemu_physics::collision::swept::UnprojTables;
    use std::collections::BTreeMap;
    let tables = UnprojTables::build();
    let gens: [(&str, &Gen); 3] = [
        ("gen", &gen),
        ("gen_area", &gen_area),
        ("gen_plate", &gen_plate),
    ];
    for (name, g) in gens {
        let mut rng = Rng::new(seed_of(name) ^ 0x5DEE_CE66_D1CE_4E5B);
        let mut tally: BTreeMap<String, usize> = BTreeMap::new();
        let n = 200_000;
        for _ in 0..n {
            let c = g(&mut rng);
            let zero = c.params[1..4] == [0, 0, 0];
            let ans = c.run_port(&tables);
            let w: Vec<&str> = ans.split_whitespace().collect();
            let ret = w[1];
            let r = w.iter().position(|&x| x == "R").unwrap();
            let f0 = u32::from_str_radix(w[r + 11], 16).unwrap();
            let f1 = u32::from_str_radix(w[r + 12], 16).unwrap();
            let mut key = format!("ret={ret} zero={zero}");
            if ret == "00000001" {
                let kind = if f0 & 0x40 != 0 && f0 < 0x80 {
                    "faceA"
                } else if f1 & 0x40 != 0 && f1 < 0x80 {
                    "faceB"
                } else {
                    "edge"
                };
                key.push_str(&format!(" {kind}"));
            }
            if let Some(a) = w.iter().position(|&x| x == "AREA") {
                let it = w[a + 1];
                let npt = u32::from_str_radix(w[a + 2], 16).unwrap();
                let nmax = u32::from_str_radix(w[a + 3], 16).unwrap();
                let over = if npt > nmax { " npt>nmax" } else { "" };
                key.push_str(&format!(
                    " area(type={} npt={}{over})",
                    &it[7..],
                    npt.min(9)
                ));
            }
            *tally.entry(key).or_default() += 1;
        }
        println!("{name}: {n} cases");
        for (k, v) in &tally {
            println!("  {v:7}  {k}");
        }
    }
}
