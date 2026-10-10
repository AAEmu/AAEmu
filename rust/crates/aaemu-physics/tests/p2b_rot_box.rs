//! P2b: rotational ray–box unprojection (0x3519EF90) and its box–ray swap wrapper (0x351A0A40)
//! against the DLL (`physoracle --p2b`).

use aaemu_physics::collision::contact::UnprojParams;
use aaemu_physics::collision::swept::probe::{check, Case, Rng};
use aaemu_physics::collision::swept::UnprojTables;
use aaemu_physics::geometry::primitives::{BoxPrim, Primitive, RayPrim};
use aaemu_physics::math::{Mat33, Vec3};

fn add(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(a.x + b.x, a.y + b.y, a.z + b.z)
}
fn scale(a: Vec3, s: f32) -> Vec3 {
    Vec3::new(a.x * s, a.y * s, a.z * s)
}

/// A box, axis-aligned (`oriented = 0`, identity) or rotated; sizes sometimes quantised.
fn gen_box(rng: &mut Rng) -> BoxPrim {
    let center = if rng.chance(0.3) {
        Vec3::new(
            rng.quant(-4.0, 4.0, 0.5),
            rng.quant(-4.0, 4.0, 0.5),
            rng.quant(-4.0, 4.0, 0.5),
        )
    } else {
        rng.vec(-6.0, 6.0)
    };
    let size = if rng.chance(0.3) {
        Vec3::new(
            rng.quant(0.25, 3.0, 0.25),
            rng.quant(0.25, 3.0, 0.25),
            rng.quant(0.25, 3.0, 0.25),
        )
    } else if rng.chance(0.1) {
        Vec3::splat(rng.f(0.02, 3.0))
    } else {
        rng.vec(0.02, 3.0)
    };
    let size = if rng.chance(0.03) {
        // a flat box
        match rng.below(3) {
            0 => Vec3::new(0.0, size.y, size.z),
            1 => Vec3::new(size.x, 0.0, size.z),
            _ => Vec3::new(size.x, size.y, 0.0),
        }
    } else {
        size
    };
    if rng.chance(0.4) {
        BoxPrim {
            basis: Mat33::IDENTITY,
            oriented: 0,
            center,
            size,
        }
    } else {
        BoxPrim {
            basis: rng.rotation(),
            oriented: 1,
            center,
            size,
        }
    }
}

/// A point in the box frame (`u` in units of the half sizes) mapped to world space.
fn box_point(b: &BoxPrim, u: Vec3) -> Vec3 {
    let l = Vec3::new(u.x * b.size.x, u.y * b.size.y, u.z * b.size.z);
    let m = &b.basis.m;
    // world = basisᵀ · l + center
    Vec3::new(
        m[0][0] * l.x + m[1][0] * l.y + m[2][0] * l.z + b.center.x,
        m[0][1] * l.x + m[1][1] * l.y + m[2][1] * l.z + b.center.y,
        m[0][2] * l.x + m[1][2] * l.y + m[2][2] * l.z + b.center.z,
    )
}

/// A box-frame direction mapped to world space.
fn box_dir(b: &BoxPrim, i: usize, s: f32) -> Vec3 {
    let r = b.basis.row(i);
    scale(r, s)
}

/// Ray + box + rotation params with the ray ends inside / outside the box, the centre near the
/// box or on the ray, and axes that are random, axis-aligned, along a box axis or along the ray.
fn gen_rot(rng: &mut Rng) -> (RayPrim, BoxPrim, UnprojParams) {
    let b = gen_box(rng);
    let inside = |rng: &mut Rng| rng.vec(-0.95, 0.95);
    let anywhere = |rng: &mut Rng| rng.vec(-2.5, 2.5);
    let o = match rng.below(4) {
        0 | 1 => box_point(&b, inside(rng)),
        2 => box_point(&b, anywhere(rng)),
        _ => box_point(
            &b,
            Vec3::new(
                rng.quant(-1.5, 1.5, 0.5),
                rng.quant(-1.5, 1.5, 0.5),
                rng.quant(-1.5, 1.5, 0.5),
            ),
        ),
    };
    let end = match rng.below(4) {
        0 => box_point(&b, inside(rng)),
        1 | 2 => box_point(&b, anywhere(rng)),
        _ => add(o, scale(rng.unit_or_axis(0.3), rng.f(0.05, 5.0))),
    };
    let mut dir = Vec3::new(end.x - o.x, end.y - o.y, end.z - o.z);
    if rng.chance(0.1) {
        // parallel to a box axis
        let l = rng.f(0.1, 5.0) * if rng.chance(0.5) { 1.0 } else { -1.0 };
        dir = box_dir(&b, rng.below(3) as usize, l);
    }
    if rng.chance(0.03) {
        dir = Vec3::ZERO; // degenerate ray
    }
    let ray = RayPrim { origin: o, dir };
    let center = match rng.below(6) {
        0 => o,
        1 => add(o, scale(rng.unit(), rng.f(0.0, 0.05))),
        2 => add(o, scale(dir, rng.f(0.0, 1.0))),
        3 => b.center,
        _ => box_point(&b, rng.vec(-2.0, 2.0)),
    };
    let axis = match rng.below(6) {
        0 => rng.unit_or_axis(1.0),
        1 => box_dir(
            &b,
            rng.below(3) as usize,
            if rng.chance(0.5) { 1.0 } else { -1.0 },
        ),
        2 => {
            // along the ray (normalised in float)
            let l2 = dir.x * dir.x + dir.y * dir.y + dir.z * dir.z;
            if l2 > 1e-6 {
                scale(dir, 1.0 / l2.sqrt())
            } else {
                rng.unit()
            }
        }
        _ => rng.unit(),
    };
    let tmax = (rng.f(0.01, std::f32::consts::FRAC_PI_2)).sin();
    let min_pt_dist = match rng.below(5) {
        0 => 0.0,
        1 => rng.f(0.1, 2.0),
        _ => rng.f(0.001, 0.05),
    };
    let vel = rng.f(0.0, 5.0);
    let p = UnprojParams {
        dir: axis,
        center,
        vel,
        tmax,
        tmin: 0.0,
        min_pt_dist,
        max_cos: 0.1,
        ..UnprojParams::default()
    };
    (ray, b, p)
}

fn gen_ray_box(rng: &mut Rng) -> Case {
    let (ray, b, p) = gen_rot(rng);
    let c = Case::new(1, &Primitive::Ray(ray), &Primitive::Box(b), &p);
    if rng.chance(0.2) {
        c.with_area(8, 0.01)
    } else {
        c
    }
}

fn gen_box_ray(rng: &mut Rng) -> Case {
    let (ray, b, p) = gen_rot(rng);
    let c = Case::new(1, &Primitive::Box(b), &Primitive::Ray(ray), &p).with_features(-1, -1);
    if rng.chance(0.2) {
        c.with_area(8, 0.01)
    } else {
        c
    }
}

#[test]
fn ray_box_rot_matches_dll() {
    check("p2b_rot_box_ray_box", 300, &gen_ray_box);
}

#[test]
fn box_ray_rot_matches_dll() {
    check("p2b_rot_box_box_ray", 300, &gen_box_ray);
}

/// Distribution of outcomes on the generators (run with `--nocapture`): returns and features.
#[test]
fn rot_box_generator_coverage() {
    let tables = UnprojTables::build();
    for (name, gen) in [
        ("ray_box", &gen_ray_box as &dyn Fn(&mut Rng) -> Case),
        ("box_ray", &gen_box_ray),
    ] {
        let mut rng = Rng::new(0x1234_5678 ^ name.len() as u64);
        let mut hist = std::collections::BTreeMap::<String, usize>::new();
        let n = 20_000;
        for _ in 0..n {
            let c = gen(&mut rng);
            let ans = c.run_port(&tables);
            let w: Vec<&str> = ans.split_whitespace().collect();
            let key = if w[1] == "00000000" {
                "ret0".to_string()
            } else {
                // R words start after "ret x P <13>" "R": features are the last two R words
                let fa = u32::from_str_radix(w[2 + 14 + 11], 16).unwrap();
                let fb = u32::from_str_radix(w[2 + 14 + 12], 16).unwrap();
                let (fa, fb) = if name == "box_ray" {
                    (fb, fa)
                } else {
                    (fa, fb)
                };
                if fa == 0xA0 {
                    format!("edge axis {}", (fb & 0x1F) >> 2)
                } else {
                    format!("end {} face {}", fa & 1, fb & 7)
                }
            };
            *hist.entry(key).or_default() += 1;
        }
        println!("{name}: {hist:?}");
        let ok = n - hist.get("ret0").copied().unwrap_or(0);
        assert!(ok > n / 10, "{name}: too few successes ({ok})");
        assert!(hist.len() >= 10, "{name}: poor feature coverage");
    }
}
