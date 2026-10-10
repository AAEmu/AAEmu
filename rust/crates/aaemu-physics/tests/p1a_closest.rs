//! P1a: slot 11 `FindClosestPoint` of box, sphere, cylinder and capsule, bit for bit against
//! the oracle (`gprobe probe=closest` and the `cprobe` distributions of
//! `oracle/p1a_probes_closest.c`).
//!
//! Scenarios: `tests/scenarios/p1a_closest.scn` (box, sphere), `p1a_closest_cyl.scn`
//! (cylinder, capsule) and `p1a_closest_crt.scn` (the MSVCR100 double functions the cylinder's
//! root finder calls). With `AAEMU_PHYS_ORACLE` set, `closest_stress` additionally runs a large
//! ad-hoc sample of every type and distribution (no golden; `AAEMU_P1A_CLOSEST_N` cases per
//! type × distribution × mode, default 400 → 15,200 cases per type) and of the CRT functions.
//!
//! Evidence at the time of writing: 304,000 stress cases (N=4000) matched bit for bit, every
//! reachable branch hit (the rare ones are pinned in the goldens by seed). The only routine
//! not ported bit-exactly is MSVCR100 `atan2` (`math::crt::atan2`, ≈0.015 % of random inputs
//! differ by an ulp; it is only reached by the trigonometric branch of the cubic resolvent, and
//! no output difference was observed).

mod p1a_support;

use aaemu_physics::geometry::primitives::{BoxPrim, CylinderPrim, SpherePrim};
use aaemu_physics::geometry::shapes::closest::{findroots, sturm_nroots, Poly};
use aaemu_physics::geometry::shapes::{BoxGeom, CapsuleGeom, CylinderGeom, SphereGeom};
use aaemu_physics::geometry::{GeomWorldData, Geometry};
use aaemu_physics::math::{Mat33, Vec3};
use aaemu_physics::oracle::run_scenario_text;
use p1a_support::*;

/// Replay one `gp closest` case through the port and format its outputs like the probe.
fn closest_tokens(c: &Case) -> Vec<String> {
    let a = AnyGeom::of(c);
    let gwd = c.gwd(0, false);
    let (p0, p1) = (c.v(13), c.v(16));
    let mut ip = c.int(19);
    let mut ifeat = c.int(20);
    let niter = c.int(21);
    let mut res = [Vec3::ZERO; 2];
    let r = a
        .g()
        .find_closest_point(&gwd, &mut ip, &mut ifeat, p0, p1, &mut res, niter);
    let mut o = vec![r.to_string(), ip.to_string(), ifeat.to_string()];
    push_v(&mut o, res[0]);
    push_v(&mut o, res[1]);
    o
}

fn run_cases(cs: &[Case]) -> (Vec<String>, [usize; 7], [usize; 7]) {
    let mut fails = Vec::new();
    let mut total = [0usize; 7];
    let mut bad = [0usize; 7];
    for c in cs.iter().filter(|c| c.probe == "closest") {
        total[c.ty as usize] += 1;
        let before = fails.len();
        check(c, &closest_tokens(c), &mut fails);
        if fails.len() > before {
            bad[c.ty as usize] += 1;
        }
    }
    (fails, total, bad)
}

#[test]
fn closest_box_sphere_match_the_oracle() {
    let cs = cases("p1a_closest");
    let (fails, total, _) = run_cases(&cs);
    assert!(total[0] >= 421 && total[4] >= 212, "{total:?}");
    finish("FindClosestPoint box/sphere", cs.len(), &fails);
}

#[test]
fn closest_cylinder_capsule_match_the_oracle() {
    let cs = cases("p1a_closest_cyl");
    let (fails, total, _) = run_cases(&cs);
    assert!(total[5] >= 369 && total[6] >= 364, "{total:?}");
    finish("FindClosestPoint cylinder/capsule", cs.len(), &fails);
}

/// `crtd` lines → (function, port result bits, MSVCR100 result bits).
fn crt_rows(t: &aaemu_physics::trace::Trace) -> Vec<(String, u64, u64)> {
    use aaemu_physics::geometry::shapes::closest::crt64;
    use aaemu_physics::math::crt;
    let mut v = Vec::new();
    for l in t.other.iter().filter(|l| l[0] == "crtd") {
        let w = |i: usize| f64::from_bits(u64::from_str_radix(&l[i], 16).unwrap());
        let (got, want) = match l[1].as_str() {
            "exp" => (crt64::exp(w(2)), w(3)),
            "log" => (crt::log(w(2)), w(3)),
            "sin" => (crt64::sin(w(2)), w(3)),
            "cos" => (crt64::cos(w(2)), w(3)),
            "atan2" => (crt::atan2(w(2), w(3)), w(4)),
            f => panic!("crtd {f}"),
        };
        v.push((l[1].clone(), got.to_bits(), want.to_bits()));
    }
    v
}

/// Mismatch count per function; exp/sin/cos/log must be exact, atan2 below `atan2_max`.
fn check_crt(rows: &[(String, u64, u64)], atan2_max: usize) {
    let mut bad = std::collections::BTreeMap::<&str, (usize, usize)>::new();
    for (f, got, want) in rows {
        let e = bad.entry(f.as_str()).or_default();
        e.0 += usize::from(got != want);
        e.1 += 1;
    }
    eprintln!("CRT mismatches (bad, total): {bad:?}");
    for (f, (b, n)) in &bad {
        let max = if *f == "atan2" { atan2_max } else { 0 };
        assert!(*b <= max, "{f}: {b} of {n} differ from MSVCR100");
    }
}

#[test]
fn crt_functions_match_msvcr100() {
    let t = aaemu_physics::oracle::scenario_trace("p1a_closest_crt").expect("golden");
    let rows = crt_rows(&t);
    assert!(rows.len() >= 1500);
    check_crt(&rows, 1);
}

/// Large ad-hoc sample (oracle only): every type × distribution, `n` cases each.
#[test]
fn closest_stress() {
    let n: usize = std::env::var("AAEMU_P1A_CLOSEST_N")
        .ok()
        .and_then(|s| s.parse().ok())
        .unwrap_or(400);
    let mut text = String::new();
    for (ty, name) in [(0, "box"), (4, "sphere"), (5, "cylinder"), (6, "capsule")] {
        for dist in 0..9 {
            for mode in 0..2 {
                let seed = 7_000_000 + ty * 1000 + dist * 10 + mode;
                text.push_str(&format!(
                    "cprobe type={name} n={n} seed={seed} dist={dist} mode={mode}\n"
                ));
            }
        }
        text.push_str(&format!(
            "gprobe probe=closest type={name} n={n} seed={}\n",
            8_000 + ty
        ));
    }
    let crt_cases = [
        ("exp", "-30", "10"),
        ("exp", "-745", "710"),
        ("sin", "0", "1.0471975511965976"),
        ("sin", "-1000", "1000"),
        ("cos", "0", "1.0471975511965976"),
        ("cos", "-1000", "1000"),
        ("log", "1e-12", "1e6"),
        ("atan2", "-100", "100"),
    ];
    for (k, (f, lo, hi)) in crt_cases.iter().enumerate() {
        text.push_str(&format!(
            "crtd fn={f} n={} seed={} lo={lo} hi={hi}\n",
            n * 25,
            9_000 + k
        ));
    }
    let Some(t) = run_scenario_text(&text) else {
        eprintln!("closest_stress: oracle not configured, skipped");
        return;
    };
    let t = t.expect("oracle run");
    let mut cs = Vec::new();
    for (line, l) in t.other.iter().enumerate() {
        if l[0] != "gp" {
            continue;
        }
        let p = l.iter().position(|s| s == "P").unwrap();
        let i = l.iter().position(|s| s == "I").unwrap();
        let o = l.iter().position(|s| s == "O").unwrap();
        cs.push(Case {
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
    let rows = crt_rows(&t);
    check_crt(&rows, (n * 25) / 1000 + 1);
    let (fails, total, bad) = run_cases(&cs);
    eprintln!("closest_stress: per type (box 0, sphere 4, cyl 5, cap 6) total {total:?} mismatches {bad:?}");
    finish("FindClosestPoint stress", cs.len(), &fails);
}

// ---- unit tests of edge cases ------------------------------------------------------------

fn unit_box(size: Vec3) -> BoxGeom {
    BoxGeom::new(&BoxPrim {
        basis: Mat33::IDENTITY,
        oriented: 0,
        center: Vec3::ZERO,
        size,
    })
}

fn call(g: &dyn Geometry, p0: Vec3, p1: Vec3) -> (i32, i32, i32, [Vec3; 2]) {
    let gwd = GeomWorldData::default();
    let (mut ip, mut f) = (-7, -7);
    let mut res = [Vec3::ZERO; 2];
    let r = g.find_closest_point(&gwd, &mut ip, &mut f, p0, p1, &mut res, 5);
    (r, ip, f, res)
}

#[test]
fn box_point_features() {
    let g = unit_box(Vec3::new(1.0, 2.0, 3.0));
    // Face +x: 0x40 | axis 0 << 1 | 1.
    let (r, ip, f, res) = call(&g, Vec3::new(5.0, 0.5, 0.5), Vec3::new(5.0, 0.5, 0.5));
    assert_eq!((r, ip, f), (1, 0, 0x41));
    assert_eq!(res[0], Vec3::new(1.0, 0.5, 0.5));
    // Vertex (+,-,+): bits x=1, y=0, z=4.
    let (_, _, f, res) = call(&g, Vec3::new(4.0, -4.0, 4.0), Vec3::new(4.0, -4.0, 4.0));
    assert_eq!(f, 5);
    assert_eq!(res[0], Vec3::new(1.0, -2.0, 3.0));
    // Edge along z at (+x, +y): 0x20 | 2<<2 | e (inc 2 = x bit 0, dec 2 = y bit 1).
    let (_, _, f, res) = call(&g, Vec3::new(3.0, 3.0, 0.0), Vec3::new(3.0, 3.0, 0.0));
    assert_eq!(f, 0x20 | 2 << 2 | 3);
    assert_eq!(res[0], Vec3::new(1.0, 2.0, 0.0));
    // Inside: pushed through the face of least depth (x, depth 0.5).
    let (_, _, f, res) = call(&g, Vec3::new(0.5, 0.0, 0.0), Vec3::new(0.5, 0.0, 0.0));
    assert_eq!(f, 0x41);
    assert_eq!(res[0], Vec3::new(1.0, 0.0, 0.0));
}

#[test]
fn box_edge_branch() {
    let g = unit_box(Vec3::splat(1.0));
    // A segment parallel to x passing diagonally beyond the (+y,+z) edge: the edge scan finds
    // the x-edge at (y, z) = (+1, +1).
    let (_, ip, f, res) = call(&g, Vec3::new(-0.5, 2.0, 2.0), Vec3::new(0.5, 2.0, 2.5));
    assert_eq!(ip, 0);
    assert_eq!(f & !3, 0x20);
    assert_eq!(f & 3, 3);
    assert_eq!(res[0].y, 1.0);
    assert_eq!(res[0].z, 1.0);
}

#[test]
fn box_vertex_branch_quirks() {
    let g = unit_box(Vec3::splat(1.0));
    // A segment beyond the (+,+,+) corner: no edge is taken, and the vertex scan accepts the
    // FIRST vertex whose projection onto the segment is interior and lands in the (+,+,+)
    // octant beyond the box. Here vertex 0 (−,−,−) already qualifies (its projection is the
    // segment midpoint (2.5, 2.5, 2.5)), so the reported box point is the far corner (bug kept).
    let (_, _, f, res) = call(&g, Vec3::new(2.0, 3.0, 2.5), Vec3::new(3.0, 2.0, 2.5));
    assert_eq!(f, 0);
    assert_eq!(res[0], Vec3::splat(-1.0));
    assert_eq!(res[1], Vec3::splat(2.5));
    // The mirrored segment beyond (−,−,−) is rejected by the signed octant test (bug kept)
    // and falls back to the end-point path (a vertex feature again, but from the point path).
    let (_, _, f, res) = call(&g, Vec3::new(-2.0, -3.0, -2.5), Vec3::new(-3.0, -2.0, -2.5));
    assert_eq!(f, 0);
    assert_eq!(res[0], Vec3::splat(-1.0));
    assert_eq!(res[1], Vec3::new(-2.0, -3.0, -2.5));
}

#[test]
fn sphere_segment_feature_codes() {
    let g = SphereGeom::new(&SpherePrim {
        center: Vec3::ZERO,
        r: 1.0,
    });
    let (_, ip, f, res) = call(&g, Vec3::new(-2.0, 2.0, 0.0), Vec3::new(2.0, 2.0, 0.0));
    assert_eq!(
        (ip, f),
        (-7, 0x20),
        "iPrim untouched, interior of the segment"
    );
    assert_eq!(res[0], Vec3::new(0.0, 1.0, 0.0));
    assert_eq!(res[1], Vec3::new(0.0, 2.0, 0.0));
    let (_, _, f, _) = call(&g, Vec3::new(5.0, 0.0, 0.0), Vec3::new(3.0, 0.0, 0.0));
    assert_eq!(f, 1, "the closer end is index 1");
    // Degenerate segment, point at the centre: direction defaults to +z.
    let (_, _, f, res) = call(&g, Vec3::ZERO, Vec3::ZERO);
    assert_eq!(f, 0);
    assert_eq!(res[0], Vec3::Z);
}

#[test]
fn cylinder_and_capsule_leave_ids_untouched() {
    let prim = CylinderPrim {
        center: Vec3::ZERO,
        axis: Vec3::Z,
        r: 1.0,
        hh: 2.0,
    };
    let cyl = CylinderGeom::new(&prim);
    let cap = CapsuleGeom::new(&prim);
    // Side wall vs. a segment parallel to the axis.
    let (r, ip, f, res) = call(&cyl, Vec3::new(3.0, 0.0, -1.0), Vec3::new(3.0, 0.0, 1.0));
    assert_eq!((r, ip, f), (1, -7, -7));
    assert_eq!(res[0].x, 1.0);
    // Cap plane: a point above the top cap inside the radius projects onto it.
    let (_, _, _, res) = call(&cyl, Vec3::new(0.25, 0.0, 5.0), Vec3::new(0.25, 0.0, 5.0));
    assert_eq!(res[0], Vec3::new(0.25, 0.0, 2.0));
    // Capsule: the same point lands on the hemisphere.
    let (r, ip, f, res) = call(&cap, Vec3::new(0.0, 0.0, 5.0), Vec3::new(0.0, 0.0, 5.0));
    assert_eq!((r, ip, f), (1, -7, -7));
    assert_eq!(res[0], Vec3::new(0.0, 0.0, 3.0));
}

#[test]
fn cylinder_rim_quartic() {
    let cyl = CylinderGeom::new(&CylinderPrim {
        center: Vec3::ZERO,
        axis: Vec3::Z,
        r: 1.0,
        hh: 1.0,
    });
    // A segment skimming past the top rim, outside the radius and above the cap: a root of the
    // quartic gives a rim point and an interior segment point.
    let (p0, p1) = (Vec3::new(2.0, -1.0, 1.5), Vec3::new(2.0, 1.4, 1.7));
    let (_, _, _, res) = call(&cyl, p0, p1);
    assert!((res[0].z - 1.0).abs() < 1e-6, "{res:?}");
    assert!(
        ((res[0].x * res[0].x + res[0].y * res[0].y) - 1.0).abs() < 1e-5,
        "{res:?}"
    );
    assert!(
        res[1] != p0 && res[1] != p1 && res[1].y > -1.0 && res[1].y < 1.4,
        "{res:?}"
    );
    // Exactly symmetric, the accepted root makes `t·|Δ⊥|² + w⊥·Δ` exactly 0 and the strict
    // `> 0` test rejects it: the end-point path answers instead (quirk kept).
    let (p0, p1) = (Vec3::new(2.0, -1.0, 1.5), Vec3::new(2.0, 1.0, 1.5));
    let (_, _, _, res) = call(&cyl, p0, p1);
    assert_eq!(res[1], p0);
    assert!((res[0].z - 1.0).abs() < 1e-6, "{res:?}");
}

#[test]
fn polynomial_helpers() {
    // (t - 0.2)(t - 0.4)(t - 0.7)(t - 1.5): three roots in [0, 1].
    let mut c = [1.0, 0.0, 0.0, 0.0, 0.0];
    for z in [0.2, 0.4, 0.7, 1.5] {
        let mut nc = [0.0; 5];
        for i in 0..4 {
            nc[i + 1] += c[i];
            nc[i] -= z * c[i];
        }
        c = nc;
    }
    let p = Poly { denom: 1.0, c };
    assert_eq!(sturm_nroots(&p, 0.0, 1.0), 3);
    let mut r = [0.0; 4];
    assert_eq!(findroots(&p, 0.0, 1.0, &mut r, 4, false), 3);
    assert!((r[2] - 0.7).abs() < 1e-9, "{r:?}");
}
