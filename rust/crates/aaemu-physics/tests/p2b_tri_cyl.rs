//! P2b: tri–cyl linear unprojection (0x351716D0) and the cyl–tri swap wrapper (0x35175330)
//! against the DLL (`physoracle --p2b`).

use aaemu_physics::collision::contact::UnprojParams;
use aaemu_physics::collision::swept::probe::{check, Case, Rng};
use aaemu_physics::collision::swept::tri_cyl::unproject_tri_cyl;
use aaemu_physics::geometry::primitives::{CylinderPrim, Primitive, Triangle};
use aaemu_physics::math::Vec3;
use std::collections::BTreeMap;

fn add(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(a.x + b.x, a.y + b.y, a.z + b.z)
}
fn sub(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(a.x - b.x, a.y - b.y, a.z - b.z)
}
fn scale(a: Vec3, s: f32) -> Vec3 {
    Vec3::new(a.x * s, a.y * s, a.z * s)
}
fn cross(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(
        a.y * b.z - a.z * b.y,
        a.z * b.x - a.x * b.z,
        a.x * b.y - a.y * b.x,
    )
}
fn unit(v: Vec3) -> Vec3 {
    let l2 = v.x * v.x + v.y * v.y + v.z * v.z;
    if l2 > 0.0 {
        scale(v, 1.0 / l2.sqrt())
    } else {
        Vec3::Z
    }
}

/// Two unit vectors perpendicular to `a` (and to each other).
fn basis(a: Vec3) -> (Vec3, Vec3) {
    let t = if a.x.abs() < 0.6 { Vec3::X } else { Vec3::Y };
    let u = unit(cross(a, t));
    let v = cross(a, u);
    (u, v)
}

fn cylinder(rng: &mut Rng) -> CylinderPrim {
    let quant = rng.chance(0.3);
    let center = if quant {
        Vec3::new(
            rng.quant(-2.0, 2.0, 0.25),
            rng.quant(-2.0, 2.0, 0.25),
            rng.quant(-2.0, 2.0, 0.25),
        )
    } else {
        rng.vec(-3.0, 3.0)
    };
    let axis = rng.unit_or_axis(if quant { 0.8 } else { 0.35 });
    let r = if rng.chance(0.02) {
        0.0
    } else if quant {
        rng.quant(0.25, 2.0, 0.25)
    } else {
        rng.f(0.05, 2.0)
    };
    let hh = if quant {
        rng.quant(0.25, 2.0, 0.25)
    } else if rng.chance(0.05) {
        0.0
    } else {
        rng.f(0.05, 2.0)
    };
    CylinderPrim {
        center,
        axis,
        r,
        hh,
    }
}

/// A point at axial height `h` and radial distance `rad` (direction `ang`) from the cylinder.
fn cyl_point(c: &CylinderPrim, h: f32, rad: f32, ang: f32) -> Vec3 {
    let (u, v) = basis(c.axis);
    let radial = add(scale(u, rad * ang.cos()), scale(v, rad * ang.sin()));
    add(add(c.center, scale(c.axis, h)), radial)
}

fn triangle(rng: &mut Rng, c: &CylinderPrim) -> Triangle {
    let size = rng.f(0.2, 4.0);
    let mut pts = match rng.below(7) {
        // random triangle around the cylinder
        0 | 1 => {
            let base = add(c.center, rng.vec(-2.5, 2.5));
            [
                add(base, rng.vec(-size, size)),
                add(base, rng.vec(-size, size)),
                add(base, rng.vec(-size, size)),
            ]
        }
        // triangle in a plane perpendicular to the axis (parallel to the caps)
        2 => {
            let (u, v) = basis(c.axis);
            let h = c.hh * rng.pick(&[-1.0f32, 1.0]) + rng.f(-0.5, 0.5);
            let o = add(
                add(c.center, scale(c.axis, h)),
                add(scale(u, rng.f(-2.0, 2.0)), scale(v, rng.f(-2.0, 2.0))),
            );
            let mut p = [o; 3];
            for q in &mut p {
                *q = add(
                    o,
                    add(scale(u, rng.f(-size, size)), scale(v, rng.f(-size, size))),
                );
            }
            p
        }
        // triangle with an edge perpendicular to the axis
        3 => {
            let (u, v) = basis(c.axis);
            let h = rng.f(-c.hh - 1.0, c.hh + 1.0);
            let a = cyl_point(c, h, rng.f(0.0, c.r * 2.0), rng.f(0.0, 6.3));
            let w = add(scale(u, rng.f(-1.0, 1.0)), scale(v, rng.f(-1.0, 1.0)));
            let b = add(a, scale(unit(w), size));
            let d = add(
                add(a, scale(c.axis, rng.f(-size, size))),
                rng.vec(-1.0, 1.0),
            );
            [a, b, d]
        }
        // a vertex inside / near the cylinder
        4 => {
            let a = cyl_point(
                c,
                rng.f(-c.hh * 1.2, c.hh * 1.2),
                rng.f(0.0, c.r * 1.2),
                rng.f(0.0, 6.3),
            );
            [
                a,
                add(a, rng.vec(-size, size)),
                add(a, rng.vec(-size, size)),
            ]
        }
        // a big triangle crossing the cylinder
        5 => {
            let o = add(c.center, rng.vec(-1.0, 1.0));
            let s = size * 3.0;
            [
                add(o, rng.vec(-s, s)),
                add(o, rng.vec(-s, s)),
                add(o, rng.vec(-s, s)),
            ]
        }
        // quantised triangle (exact parallels with quantised axis-aligned cylinders)
        _ => {
            let q = |rng: &mut Rng| {
                Vec3::new(
                    rng.quant(-3.0, 3.0, 0.25),
                    rng.quant(-3.0, 3.0, 0.25),
                    rng.quant(-3.0, 3.0, 0.25),
                )
            };
            let mut p = [q(rng), q(rng), q(rng)];
            if rng.chance(0.5) {
                // flatten one coordinate to make it axis-plane parallel
                let k = rng.below(3) as usize;
                let v = p[0][k];
                for x in &mut p {
                    x[k] = v;
                }
            }
            p
        }
    };
    if rng.chance(0.03) {
        pts[2] = pts[1]; // degenerate
    }
    let mut n = unit(cross(sub(pts[1], pts[0]), sub(pts[2], pts[0])));
    if rng.chance(0.08) {
        n = rng.unit();
    } else if rng.chance(0.1) {
        n = scale(n, -1.0);
    }
    Triangle { pt: pts, n }
}

fn params(rng: &mut Rng, c: &CylinderPrim, t: &Triangle) -> UnprojParams {
    let mut dir = match rng.below(9) {
        0 => Vec3::ZERO,
        1 | 2 => rng.unit_or_axis(0.3),
        3 => t.n,
        4 => scale(t.n, -1.0),
        5 => scale(c.axis, rng.pick(&[-1.0f32, 1.0])),
        6 => {
            let (u, _) = basis(c.axis);
            u
        }
        7 => {
            let s = rng.f(0.1, 5.0);
            scale(rng.unit(), s)
        }
        _ => unit(sub(
            add(add(t.pt[0], t.pt[1]), t.pt[2]),
            scale(c.center, 3.0),
        )),
    };
    if rng.chance(0.04) {
        // tiny direction: |dir x axis|^2 and the other denominators fall below 1e-20
        let s = rng.pick(&[1e-6f32, 1e-9, 1e-11, 1e-13]);
        dir = scale(dir, s);
    }
    let tmax = match rng.below(5) {
        0 if rng.chance(0.1) => f32::INFINITY,
        0 => 1e10,
        1 => rng.f(0.0, 1.0),
        _ => rng.f(0.0, 10.0),
    };
    let tmin = if rng.chance(0.8) {
        0.0
    } else {
        rng.f(-1.0, 1.0)
    };
    let min_pt_dist = rng.f(0.001, 0.05);
    let max_cos = if rng.chance(0.5) { 0.1 } else { 0.9998 };
    let flag = rng.below(2) as i32;
    UnprojParams {
        dir,
        tmax,
        tmin,
        min_pt_dist,
        max_cos,
        flag,
        ..UnprojParams::default()
    }
}

fn feature(rng: &mut Rng) -> i32 {
    if rng.chance(0.7) {
        -1
    } else {
        rng.pick(&[0x43, 0x41, 0x42, 0x40, 0, 0x20, 0x43, i32::MIN + 0x43])
    }
}

fn setup(rng: &mut Rng) -> (Triangle, CylinderPrim, UnprojParams, i32, i32) {
    let c = cylinder(rng);
    let t = triangle(rng, &c);
    let p = params(rng, &c, &t);
    let ft = feature(rng);
    let fc = feature(rng);
    (t, c, p, ft, fc)
}

fn with_area(rng: &mut Rng, c: Case) -> Case {
    if rng.chance(0.15) {
        c.with_area(rng.pick(&[1u32, 4, 256]), 0.01)
    } else {
        c
    }
}

fn gen_tri_cyl(rng: &mut Rng) -> Case {
    let (t, c, p, ft, fc) = setup(rng);
    let case =
        Case::new(0, &Primitive::Triangle(t), &Primitive::Cylinder(c), &p).with_features(ft, fc);
    with_area(rng, case)
}

fn gen_cyl_tri(rng: &mut Rng) -> Case {
    let (t, c, p, ft, fc) = setup(rng);
    let case =
        Case::new(0, &Primitive::Cylinder(c), &Primitive::Triangle(t), &p).with_features(fc, ft);
    with_area(rng, case)
}

#[test]
fn tri_cyl_linear_matches_dll() {
    check("p2b_tri_cyl_tri_cyl", 300, &gen_tri_cyl);
}

#[test]
fn cyl_tri_linear_matches_dll() {
    check("p2b_tri_cyl_cyl_tri", 200, &gen_cyl_tri);
}

/// Coverage of the generator (run with `--nocapture`): result classes of the port on many cases.
#[test]
fn tri_cyl_branch_distribution() {
    let mut rng = Rng::new(0x0712_1C11);
    let mut hist: BTreeMap<String, usize> = BTreeMap::new();
    let n = 20_000;
    for _ in 0..n {
        let (t, c, mut p, ft, fc) = setup(&mut rng);
        let mut out = aaemu_physics::collision::swept::probe::sentinel_result();
        let r = unproject_tri_cyl(&mut p, &t, ft, &c, fc, &mut out);
        let key = if r == 0 {
            "ret0".to_string()
        } else {
            format!(
                "ret1 f0={:02x} f1={:02x}",
                out.i_feature[0] & 0xE0,
                out.i_feature[1]
            )
        };
        *hist.entry(key).or_default() += 1;
    }
    for (k, v) in &hist {
        println!("{k}: {v}");
    }
    let classes = hist.keys().filter(|k| k.starts_with("ret1")).count();
    assert!(classes >= 8, "too few result classes: {hist:?}");
}

#[test]
fn vertex_against_side_hand_checked() {
    // a vertical cylinder at the origin (r 1, hh 1); a triangle with a vertex 0.5 inside the
    // side at height 0, moved along +x: it leaves after t = 1.5 (vertex from x = 0.5 to 2? no:
    // the vertex at x = 0.5 must reach x = 1 → t = 0.5 moving +x).
    let c = CylinderPrim {
        center: Vec3::ZERO,
        axis: Vec3::Z,
        r: 1.0,
        hh: 1.0,
    };
    let t = Triangle {
        pt: [
            Vec3::new(0.5, 0.0, 0.0),
            Vec3::new(3.0, 1.0, 0.0),
            Vec3::new(3.0, -1.0, 0.0),
        ],
        n: Vec3::Z,
    };
    let mut p = UnprojParams {
        dir: Vec3::X,
        tmax: 10.0,
        ..UnprojParams::default()
    };
    let mut out = aaemu_physics::collision::swept::probe::sentinel_result();
    assert_eq!(unproject_tri_cyl(&mut p, &t, -1, &c, 0x43, &mut out), 1);
    assert_eq!(out.t, 0.5);
    assert_eq!(out.pt, Vec3::new(1.0, 0.0, 0.0));
    assert_eq!(out.n, Vec3::new(-1.0, 0.0, 0.0));
    assert_eq!(out.i_feature, [0x80, 0x40]);
}
