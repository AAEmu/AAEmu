//! P2b `rot_cap`: rotational ray–capsule unprojection (0x351A4630) and its capsule–ray swap
//! wrapper (0x351A7AF0) against the DLL (`physoracle --p2b`).
//!
//! Besides the golden/oracle checks, `ray_cap_rot_replay_matches_dll` (oracle only) runs every
//! case through the probe's `RCAPLOG` command, which logs each call of the polynomial routines
//! inside the DLL; the port is then run with those calls replayed, checking that it builds the
//! very same quartics (bit for bit) and produces the same answer from the DLL's roots. This
//! verifies the cell independently of the polynomial solvers (ported by the `poly` group).

use aaemu_physics::collision::contact::UnprojParams;
use aaemu_physics::collision::swept::probe::{
    self, check, format_answer, params_from_words, prim_from_words, result_from_words, Case, Rng,
};
use aaemu_physics::collision::swept::common::swap_rot;
use aaemu_physics::collision::swept::rot_cap::{unproject_ray_cap_rot_with, QuarticSolver};
use aaemu_physics::geometry::primitives::{CylinderPrim, Primitive, RayPrim};
use aaemu_physics::math::Vec3;
use std::collections::BTreeMap;

fn add(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(a.x + b.x, a.y + b.y, a.z + b.z)
}
fn scale(a: Vec3, s: f32) -> Vec3 {
    Vec3::new(a.x * s, a.y * s, a.z * s)
}
fn qvec(rng: &mut Rng, lo: f32, hi: f32, step: f32) -> Vec3 {
    Vec3::new(
        rng.quant(lo, hi, step),
        rng.quant(lo, hi, step),
        rng.quant(lo, hi, step),
    )
}
/// A unit vector perpendicular to `k` (axis-aligned when `k` is).
fn perp(rng: &mut Rng, k: Vec3) -> Vec3 {
    let kl = (k.x * k.x + k.y * k.y + k.z * k.z).sqrt();
    let k = scale(k, 1.0 / kl);
    loop {
        let u = rng.unit_or_axis(0.5);
        let c = Vec3::new(
            k.y * u.z - k.z * u.y,
            k.z * u.x - k.x * u.z,
            k.x * u.y - k.y * u.x,
        );
        let l2 = c.x * c.x + c.y * c.y + c.z * c.z;
        if l2 > 0.01 {
            let inv = 1.0 / l2.sqrt();
            return scale(c, inv);
        }
    }
}

/// Rotate `v` about the unit axis `a` by `ang` (generator only).
fn rot(v: Vec3, a: Vec3, ang: f32) -> Vec3 {
    let (s, c) = ang.sin_cos();
    let d = v.x * a.x + v.y * a.y + v.z * a.z;
    let cr = Vec3::new(a.y * v.z - a.z * v.y, a.z * v.x - a.x * v.z, a.x * v.y - a.y * v.x);
    Vec3::new(
        v.x * c + cr.x * s + a.x * d * (1.0 - c),
        v.y * c + cr.y * s + a.y * d * (1.0 - c),
        v.z * c + cr.z * s + a.z * d * (1.0 - c),
    )
}

/// One ray–capsule rotational configuration.
fn gen_cfg(rng: &mut Rng) -> (RayPrim, CylinderPrim, UnprojParams) {
    let quant = rng.chance(0.3);
    let r = if rng.chance(0.1) {
        rng.pick(&[0.0f32, 0.01])
    } else {
        rng.f(0.1, 2.0)
    };
    let hh = if rng.chance(0.1) {
        0.0
    } else {
        rng.f(0.1, 3.0)
    };
    let k = rng.unit_or_axis(if quant { 0.9 } else { 0.3 });
    let cc = if quant {
        qvec(rng, -5.0, 5.0, 0.5)
    } else {
        rng.vec(-10.0, 10.0)
    };
    let cap = CylinderPrim {
        center: cc,
        axis: k,
        r: if quant { (r / 0.25).round().max(1.0) * 0.25 } else { r },
        hh: if quant { (hh / 0.5).round() * 0.5 } else { hh },
    };
    // rotation axis: random, or parallel / perpendicular to the capsule axis
    let a = match rng.below(4) {
        0 => k,
        1 => perp(rng, k),
        _ => rng.unit_or_axis(0.3),
    };
    let a = if rng.chance(0.2) { scale(a, -1.0) } else { a };
    // a target point on (or near) the capsule surface
    let side = rng.below(3);
    let target = match side {
        0 => {
            // cylinder side
            let u = perp(rng, k);
            let t = rng.f(-1.1, 1.1) * cap.hh;
            add(add(cc, scale(k, t)), scale(u, cap.r * rng.f(0.9, 1.1)))
        }
        1 => {
            // a cap
            let sgn = if rng.chance(0.5) { 1.0 } else { -1.0 };
            let u = rng.unit();
            add(add(cc, scale(k, sgn * cap.hh)), scale(u, cap.r * rng.f(0.9, 1.1)))
        }
        _ => add(cc, rng.vec(-3.0, 3.0)),
    };
    // rotation centre near / far
    let dist = rng.pick(&[0.0f32, 0.5, 2.0, 5.0, 20.0]);
    let center = if quant {
        add(target, qvec(rng, -dist, dist, 0.5))
    } else {
        add(target, scale(rng.unit(), dist * rng.f(0.2, 1.0)))
    };
    // the ray passes through the target when rotated by `ang`; place it rotated back
    let ang = rng.f(0.0, std::f32::consts::FRAC_PI_2 * 1.05);
    let len = rng.pick(&[0.2f32, 1.0, 3.0, 8.0]) * rng.f(0.5, 1.5);
    // tangent to the surface at the target (line-vs-side / line-vs-sphere contacts)
    let rad = Vec3::new(target.x - cc.x, target.y - cc.y, target.z - cc.z);
    let rk = rad.x * k.x + rad.y * k.y + rad.z * k.z;
    let radial = if side == 0 {
        Vec3::new(rad.x - k.x * rk, rad.y - k.y * rk, rad.z - k.z * rk)
    } else {
        let sgn = if rk >= 0.0 { 1.0 } else { -1.0 };
        Vec3::new(
            rad.x - k.x * sgn * cap.hh,
            rad.y - k.y * sgn * cap.hh,
            rad.z - k.z * sgn * cap.hh,
        )
    };
    let dir0 = match rng.below(6) {
        0 => k,
        1 => perp(rng, k),
        2 => a,
        3 | 4 if radial.x * radial.x + radial.y * radial.y + radial.z * radial.z > 1e-6 => {
            perp(rng, radial)
        }
        _ => rng.unit_or_axis(0.3),
    };
    let at = if rng.chance(0.3) { 0.5 } else { rng.f(0.0, 1.0) };
    let mut origin0 = Vec3::new(
        target.x - center.x - dir0.x * len * at,
        target.y - center.y - dir0.y * len * at,
        target.z - center.z - dir0.z * len * at,
    );
    let mut dir = scale(dir0, len);
    if rng.chance(0.85) {
        origin0 = rot(origin0, a, -ang);
        dir = rot(dir, a, -ang);
    }
    let mut origin = add(center, origin0);
    if quant {
        origin = Vec3::new(
            (origin.x / 0.25).round() * 0.25,
            (origin.y / 0.25).round() * 0.25,
            (origin.z / 0.25).round() * 0.25,
        );
        dir = Vec3::new(
            (dir.x / 0.25).round() * 0.25,
            (dir.y / 0.25).round() * 0.25,
            (dir.z / 0.25).round() * 0.25,
        );
    }
    let mut p = UnprojParams::default();
    p.dir = a;
    p.center = if rng.chance(0.08) { origin } else { center };
    if rng.chance(0.05) {
        // origin on the rotation centre within min_pt_dist
        p.center = add(origin, rng.vec(-0.005, 0.005));
    }
    let maxang = rng.f(0.01, std::f32::consts::FRAC_PI_2);
    p.tmax = maxang.sin();
    p.min_pt_dist = rng.pick(&[0.001f32, 0.01, 0.05]);
    p.max_cos = 0.1;
    p.vel = rng.f(0.0, 5.0);
    match rng.below(100) {
        0..=2 => dir = Vec3::ZERO,
        3 => p.dir = Vec3::ZERO,
        4 | 5 => p.dir = scale(p.dir, rng.f(0.5, 2.0)),
        6 => origin = p.center,
        _ => {}
    }
    (RayPrim { origin, dir }, cap, p)
}

fn gen_ray_cap(rng: &mut Rng) -> Case {
    let (ray, cap, p) = gen_cfg(rng);
    let c = Case::new(1, &Primitive::Ray(ray), &Primitive::Capsule(cap), &p);
    if rng.chance(0.1) {
        c.with_area(4, 0.01)
    } else {
        c
    }
}

fn gen_cap_ray(rng: &mut Rng) -> Case {
    let (ray, cap, mut p) = gen_cfg(rng);
    // the wrapper negates the axis, so pass the mirrored sense half of the time
    if rng.chance(0.5) {
        p.dir = scale(p.dir, -1.0);
    }
    Case::new(1, &Primitive::Capsule(cap), &Primitive::Ray(ray), &p)
}

// ------------------------------------------------------------------------- replay of the DLL

#[derive(Debug, Clone)]
enum Call {
    Has { p: [u64; 6], ret: i32 },
    Roots { p: [u64; 6], ret: i32, roots: Vec<u64> },
}

fn parse_log(line: &str) -> Result<(Vec<Call>, String), String> {
    let rest = line.strip_prefix("L ").ok_or("no L prefix")?;
    let pos = rest.find("ret ").ok_or("no ret")?;
    let (log, ans) = rest.split_at(pos);
    let mut it = log.split_whitespace();
    let mut calls = Vec::new();
    let q = |t: Option<&str>| -> Result<u64, String> {
        u64::from_str_radix(t.ok_or("short log")?, 16).map_err(|e| e.to_string())
    };
    while let Some(tag) = it.next() {
        let mut p = [0u64; 6];
        for x in &mut p {
            *x = q(it.next())?;
        }
        let _lo = q(it.next())?;
        let _hi = q(it.next())?;
        match tag {
            "H" => {
                let ret = q(it.next())? as i32;
                calls.push(Call::Has { p, ret });
            }
            "S" => {
                let _max = q(it.next())?;
                let _itn = q(it.next())?;
                let _fl = q(it.next())?;
                let ret = q(it.next())? as i32;
                let mut roots = Vec::new();
                for _ in 0..ret.clamp(0, 20) {
                    roots.push(q(it.next())?);
                }
                calls.push(Call::Roots { p, ret, roots });
            }
            t => return Err(format!("bad tag {t}")),
        }
    }
    Ok((calls, ans.to_string()))
}

struct Replay {
    calls: Vec<Call>,
    pos: usize,
    err: Option<String>,
}

fn bits(p: &[f64; 6]) -> [u64; 6] {
    p.map(f64::to_bits)
}

impl QuarticSolver for Replay {
    fn has_root(&mut self, p: &[f64; 6]) -> i32 {
        let c = self.calls.get(self.pos).cloned();
        self.pos += 1;
        match c {
            Some(Call::Has { p: q, ret }) => {
                if q != bits(p) && self.err.is_none() {
                    self.err = Some(format!("H#{} poly {:x?} vs dll {:x?}", self.pos - 1, bits(p), q));
                }
                ret
            }
            other => {
                self.err.get_or_insert(format!("expected H at {}, dll {:?}", self.pos - 1, other));
                0
            }
        }
    }
    fn roots(&mut self, p: &[f64; 6], roots: &mut [f64; 20]) -> i32 {
        let c = self.calls.get(self.pos).cloned();
        self.pos += 1;
        match c {
            Some(Call::Roots { p: q, ret, roots: r }) => {
                if q != bits(p) && self.err.is_none() {
                    self.err = Some(format!("S poly {:x?} vs dll {:x?}", bits(p), q));
                }
                for (i, x) in r.iter().enumerate() {
                    roots[i] = f64::from_bits(*x);
                }
                ret
            }
            other => {
                self.err.get_or_insert(format!("expected S at {}, dll {:?}", self.pos - 1, other));
                0
            }
        }
    }
}

/// Run one ray–cap case through the port with the logged solver calls replayed.
fn replay(case: &Case, calls: Vec<Call>) -> (String, Option<String>) {
    let mut p = params_from_words(&case.params);
    let a = prim_from_words(case.ta, &case.a);
    let b = prim_from_words(case.tb, &case.b);
    let mut out = result_from_words(&case.res);
    let area = case.area.as_ref().map(probe::sentinel_area);
    let mut solver = Replay {
        calls,
        pos: 0,
        err: None,
    };
    let ret = match (&a, &b) {
        (Primitive::Ray(ray), Primitive::Capsule(cap)) => {
            unproject_ray_cap_rot_with(&mut p, ray, cap, &mut out, &mut solver)
        }
        (Primitive::Capsule(cap), Primitive::Ray(ray)) => swap_rot(&mut p, &mut out, |p, out| {
            unproject_ray_cap_rot_with(p, ray, cap, out, &mut solver)
        }),
        _ => panic!("ray-cap case expected"),
    };
    if solver.pos != solver.calls.len() {
        solver.err.get_or_insert(format!(
            "port made {} solver calls, dll {}",
            solver.pos,
            solver.calls.len()
        ));
    }
    (format_answer(ret, &p, &out, area.as_ref()), solver.err)
}

#[test]
fn ray_cap_rot_replay_matches_dll() {
    let Some(exe) = aaemu_physics::oracle::oracle_path() else {
        return;
    };
    let n: usize = std::env::var("AAEMU_PHYS_P2B_REPLAY")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(2000);
    let mut rng = Rng::new(probe::seed_of("p2b_rot_cap_replay"));
    let cases: Vec<Case> = (0..n)
        .map(|i| {
            if i % 4 == 3 {
                gen_cap_ray(&mut rng)
            } else {
                gen_ray_cap(&mut rng)
            }
        })
        .collect();
    let lines: Vec<String> = cases
        .iter()
        .map(|c| format!("RCAPLOG {}", c.to_line()))
        .collect();
    let mut answers = Vec::with_capacity(n);
    for chunk in lines.chunks(10_000) {
        answers.extend(probe::oracle_raw(&exe, chunk).expect("oracle"));
    }
    let mut bad = 0;
    let mut report = String::new();
    let mut stats: BTreeMap<String, usize> = BTreeMap::new();
    let mut roots_seen = 0usize;
    for (i, (c, l)) in cases.iter().zip(&answers).enumerate() {
        let (calls, want) = parse_log(l).expect("log");
        roots_seen += calls
            .iter()
            .map(|c| match c {
                Call::Roots { ret, .. } => *ret as usize,
                Call::Has { .. } => 0,
            })
            .sum::<usize>();
        let w: Vec<&str> = want.split_whitespace().collect();
        let key = if w[1] == "00000000" {
            "ret0".to_string()
        } else {
            format!("feat {} {}", w[27], w[28])
        };
        *stats.entry(key).or_default() += 1;
        let (got, err) = replay(c, calls);
        if got != want || err.is_some() {
            bad += 1;
            if bad <= 5 {
                report.push_str(&format!(
                    "#{i}\n  case: {}\n  dll:  {want}\n  port: {got}\n  diff: {}\n  solver: {err:?}\n",
                    c.to_line(),
                    probe::word_diff(&want, &got)
                ));
            }
        }
    }
    eprintln!("replay: {n} cases, roots {roots_seen}, outcomes {stats:?}");
    assert!(bad == 0, "{bad} of {n} differ\n{report}");
}

#[test]
fn ray_cap_rot_no_solver_needed_cases() {
    // a ray whose ends sweep through a cap sphere only: hand-checked sanity of the end-sphere
    // branch (no quartic has roots in [0, 1] is not guaranteed, so this only runs the
    // closed-form part through a solver that reports no roots)
    struct NoRoots;
    impl QuarticSolver for NoRoots {
        fn has_root(&mut self, _p: &[f64; 6]) -> i32 {
            0
        }
        fn roots(&mut self, _p: &[f64; 6], _r: &mut [f64; 20]) -> i32 {
            0
        }
    }
    // capsule: sphere of radius 1 at (2, 0, 0) (hh = 0); ray end at (2, -1.5, 0) rotating about
    // z through the origin: it reaches the sphere surface.
    let cap = CylinderPrim {
        center: Vec3::new(2.0, 0.0, 0.0),
        axis: Vec3::Z,
        r: 1.0,
        hh: 0.0,
    };
    let ray = RayPrim {
        origin: Vec3::new(0.5, -3.0, 0.0),
        dir: Vec3::new(1.5, 1.5, 0.0),
    };
    let mut p = UnprojParams::default();
    p.mode = 1;
    p.dir = Vec3::Z;
    p.tmax = 1.0;
    p.min_pt_dist = 0.01;
    let mut out = aaemu_physics::collision::contact::UnprojResult::default();
    let r = unproject_ray_cap_rot_with(&mut p, &ray, &cap, &mut out, &mut NoRoots);
    assert_eq!(r, 1);
    // the end point (2, -1.5) rotated by φ lies on the circle |p − (2,0)| = 1
    let s = out.t;
    let c = out.cos_t;
    let (x, y) = (2.0 * c + 1.5 * s, 2.0 * s - 1.5 * c);
    assert!(((x - 2.0).powi(2) + y * y - 1.0).abs() < 1e-9, "{out:?}");
    assert_eq!(out.i_feature[0], 0x81);
}

#[test]
fn ray_cap_rot_matches_dll() {
    check("p2b_rot_cap_ray_cap", 300, &gen_ray_cap);
}

#[test]
fn cap_ray_rot_matches_dll() {
    check("p2b_rot_cap_cap_ray", 200, &gen_cap_ray);
}

