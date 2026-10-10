//! P2b `ray_cyl` group: ray–cyl (0x35179650), cyl–ray (0x3517BC40), cyl–sph (0x35187780) and
//! sph–cyl (0x35189260) linear unprojection against the DLL (`physoracle --p2b`).

use aaemu_physics::collision::contact::UnprojParams;
use aaemu_physics::collision::swept::probe::{check, Case, Rng};
use aaemu_physics::collision::swept::UnprojTables;
use aaemu_physics::geometry::primitives::{CylinderPrim, Primitive, RayPrim, SpherePrim};
use aaemu_physics::math::Vec3;
use std::collections::BTreeMap;

fn add(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(a.x + b.x, a.y + b.y, a.z + b.z)
}
fn scale(a: Vec3, s: f32) -> Vec3 {
    Vec3::new(a.x * s, a.y * s, a.z * s)
}
fn neg(a: Vec3) -> Vec3 {
    Vec3::new(-a.x, -a.y, -a.z)
}

/// A unit vector perpendicular to `a` (float arithmetic; exact for axis-aligned `a`).
fn perp(a: Vec3, rng: &mut Rng) -> Vec3 {
    let t = if a.x.abs() < 0.6 { Vec3::X } else { Vec3::Y };
    let mut u = Vec3::new(
        a.y * t.z - a.z * t.y,
        a.z * t.x - a.x * t.z,
        a.x * t.y - a.y * t.x,
    );
    let l = (u.x * u.x + u.y * u.y + u.z * u.z).sqrt();
    u = scale(u, 1.0 / l);
    if rng.chance(0.5) {
        // rotate within the plane by mixing with a × u
        let w = Vec3::new(
            a.y * u.z - a.z * u.y,
            a.z * u.x - a.x * u.z,
            a.x * u.y - a.y * u.x,
        );
        let ang = rng.f(0.0, std::f32::consts::TAU);
        u = add(scale(u, ang.cos()), scale(w, ang.sin()));
    }
    u
}

fn gen_cyl(rng: &mut Rng) -> CylinderPrim {
    let quant = rng.chance(0.3);
    let center = if quant {
        Vec3::new(
            rng.quant(-4.0, 4.0, 0.25),
            rng.quant(-4.0, 4.0, 0.25),
            rng.quant(-4.0, 4.0, 0.25),
        )
    } else {
        rng.vec(-6.0, 6.0)
    };
    let axis = rng.unit_or_axis(if quant { 0.8 } else { 0.35 });
    let r = if rng.chance(0.03) {
        0.0
    } else if quant {
        rng.quant(0.25, 2.0, 0.25)
    } else {
        rng.f(0.05, 2.5)
    };
    let hh = if rng.chance(0.03) {
        0.0
    } else if quant {
        rng.quant(0.25, 2.0, 0.25)
    } else {
        rng.f(0.05, 2.5)
    };
    CylinderPrim {
        center,
        axis,
        r,
        hh,
    }
}

/// A point near the cylinder in cylinder coordinates (height `h`, radius `rho`).
fn cyl_point(rng: &mut Rng, c: &CylinderPrim, hr: (f32, f32), rr: (f32, f32)) -> Vec3 {
    let h = rng.f(hr.0, hr.1);
    let rho = rng.f(rr.0, rr.1);
    let u = perp(c.axis, rng);
    add(add(c.center, scale(c.axis, h)), scale(u, rho))
}

fn gen_dir(rng: &mut Rng, c: &CylinderPrim, rd: Vec3) -> Vec3 {
    match rng.below(9) {
        0 => Vec3::ZERO,
        1 | 2 => rng.unit_or_axis(0.3),
        3 => c.axis,
        4 => neg(c.axis),
        5 => perp(c.axis, rng),
        6 => {
            let u = rng.unit();
            scale(u, rng.f(0.1, 4.0))
        }
        7 => {
            // perpendicular to the ray
            let l = (rd.x * rd.x + rd.y * rd.y + rd.z * rd.z).sqrt();
            if l > 1e-6 {
                perp(scale(rd, 1.0 / l), rng)
            } else {
                rng.unit()
            }
        }
        _ => {
            // a sweep: −v̂ with the ray along v
            let l = (rd.x * rd.x + rd.y * rd.y + rd.z * rd.z).sqrt();
            if l > 1e-6 {
                scale(rd, -1.0 / l)
            } else {
                rng.unit()
            }
        }
    }
}

fn gen_ray_cyl_parts(rng: &mut Rng) -> (RayPrim, CylinderPrim, UnprojParams, i32) {
    let c = gen_cyl(rng);
    let span = c.hh + c.r + 1.0;
    let (origin, dir) = match rng.below(9) {
        0 => {
            // generic
            let o = add(c.center, rng.vec(-span, span));
            let d = scale(rng.unit(), rng.f(0.1, 2.5 * span));
            (o, d)
        }
        1 => {
            // through the side
            let a = cyl_point(rng, &c, (-c.hh, c.hh), (0.0, 2.0 * c.r + 0.5));
            let b = cyl_point(rng, &c, (-c.hh, c.hh), (0.0, 2.0 * c.r + 0.5));
            (a, Vec3::new(b.x - a.x, b.y - a.y, b.z - a.z))
        }
        2 => {
            // parallel to the axis
            let o = cyl_point(rng, &c, (-c.hh - 1.0, c.hh + 1.0), (0.0, c.r * 1.3 + 0.1));
            let s = if rng.chance(0.5) { 1.0 } else { -1.0 };
            (o, scale(c.axis, s * rng.f(0.0, 2.0 * c.hh + 2.0)))
        }
        3 => {
            // perpendicular to the axis (cap slide), near a cap plane
            let hs = if rng.chance(0.5) { c.hh } else { -c.hh };
            let o = cyl_point(rng, &c, (hs - 0.3, hs + 0.3), (0.0, c.r * 1.5 + 0.2));
            let u = perp(c.axis, rng);
            (o, scale(u, rng.f(-2.0 * c.r - 1.0, 2.0 * c.r + 1.0)))
        }
        4 => {
            // nearly perpendicular to the axis
            let hs = if rng.chance(0.5) { c.hh } else { -c.hh };
            let o = cyl_point(rng, &c, (hs * 0.5, hs * 1.2), (0.0, c.r * 1.5 + 0.2));
            let u = perp(c.axis, rng);
            let tilt = rng.f(-0.012, 0.012);
            let d = add(u, scale(c.axis, tilt));
            (o, scale(d, rng.f(0.2, 2.0 * c.r + 1.0)))
        }
        5 => {
            // through a cap / rim
            let hs = if rng.chance(0.5) { c.hh } else { -c.hh };
            let a = cyl_point(rng, &c, (hs - 0.5, hs + 0.5), (0.0, c.r * 1.2));
            let b = cyl_point(rng, &c, (hs - 0.5, hs + 0.5), (c.r * 0.8, c.r * 1.2));
            (a, Vec3::new(b.x - a.x, b.y - a.y, b.z - a.z))
        }
        6 => {
            // zero length
            let o = cyl_point(rng, &c, (-c.hh - 0.5, c.hh + 0.5), (0.0, c.r + 0.5));
            (o, Vec3::ZERO)
        }
        7 => {
            // entirely inside
            let a = cyl_point(rng, &c, (-c.hh, c.hh), (0.0, c.r));
            let b = cyl_point(rng, &c, (-c.hh, c.hh), (0.0, c.r));
            (a, Vec3::new(b.x - a.x, b.y - a.y, b.z - a.z))
        }
        _ => {
            // separated
            let o = cyl_point(rng, &c, (-c.hh - 3.0, c.hh + 3.0), (c.r + 0.2, c.r + 3.0));
            (o, scale(rng.unit(), rng.f(0.1, 2.0)))
        }
    };
    let ray = RayPrim { origin, dir };
    let mut p = UnprojParams {
        dir: gen_dir(rng, &c, dir),
        ..UnprojParams::default()
    };
    p.tmax = match rng.below(4) {
        0 => rng.f(0.0, 1.0),
        1 => rng.f(0.0, 10.0),
        2 => 1e10,
        _ => rng.f(0.5, 5.0),
    };
    p.tmin = if rng.chance(0.8) {
        0.0
    } else {
        rng.f(-1.0, 1.0)
    };
    p.min_pt_dist = rng.f(0.001, 0.05);
    p.max_cos = if rng.chance(0.5) { 0.1 } else { 0.9998 };
    p.flag = i32::from(rng.chance(0.5));
    let fb = match rng.below(10) {
        0 | 1 => 0x43,
        2 => 0x41,
        3 => 0x42,
        4 => 0x8000_0043u32 as i32,
        5 => 0x40,
        _ => -1,
    };
    (ray, c, p, fb)
}

fn gen_ray_cyl(rng: &mut Rng) -> Case {
    let (ray, c, p, fb) = gen_ray_cyl_parts(rng);
    let fa = if rng.chance(0.8) { -1 } else { 0xA0 };
    let case =
        Case::new(0, &Primitive::Ray(ray), &Primitive::Cylinder(c), &p).with_features(fa, fb);
    if rng.chance(0.2) {
        case.with_area(16, 0.01)
    } else {
        case
    }
}

fn gen_cyl_ray(rng: &mut Rng) -> Case {
    let (ray, c, mut p, fb) = gen_ray_cyl_parts(rng);
    p.dir = neg(p.dir);
    let fr = if rng.chance(0.8) { -1 } else { 0xA0 };
    let case =
        Case::new(0, &Primitive::Cylinder(c), &Primitive::Ray(ray), &p).with_features(fb, fr);
    if rng.chance(0.2) {
        case.with_area(16, 0.01)
    } else {
        case
    }
}

#[test]
fn ray_cyl_linear_matches_dll() {
    check("p2b_ray_cyl_ray_cyl", 300, &gen_ray_cyl);
}

#[test]
fn cyl_ray_linear_matches_dll() {
    check("p2b_ray_cyl_cyl_ray", 200, &gen_cyl_ray);
}

fn gen_cyl_sph_parts(rng: &mut Rng) -> (CylinderPrim, SpherePrim, UnprojParams, i32) {
    let c = gen_cyl(rng);
    let sr = if rng.chance(0.03) {
        0.0
    } else if rng.chance(0.3) {
        rng.quant(0.25, 2.0, 0.25)
    } else {
        rng.f(0.05, 2.5)
    };
    let center = match rng.below(8) {
        0 => add(
            c.center,
            rng.vec(-(c.hh + c.r + sr + 1.0), c.hh + c.r + sr + 1.0),
        ),
        1 => {
            // near the side
            let rho = c.r + sr * rng.f(-1.0, 1.3);
            cyl_point(rng, &c, (-c.hh, c.hh), (rho, rho))
        }
        2 => {
            // near a cap
            let hs = if rng.chance(0.5) { 1.0 } else { -1.0 };
            let h = hs * (c.hh + sr * rng.f(-1.0, 1.3));
            cyl_point(rng, &c, (h, h), (0.0, c.r))
        }
        3 => {
            // near a rim
            let hs = if rng.chance(0.5) { 1.0 } else { -1.0 };
            let h = hs * (c.hh + sr * rng.f(-0.8, 0.9));
            cyl_point(rng, &c, (h, h), (c.r, c.r + sr * 0.9))
        }
        4 => {
            // on the axis (degenerate radial direction)
            let h = if rng.chance(0.3) {
                0.0
            } else {
                rng.f(-c.hh - sr, c.hh + sr)
            };
            add(c.center, scale(c.axis, h))
        }
        5 => {
            // deep inside
            cyl_point(rng, &c, (-c.hh, c.hh), (0.0, c.r))
        }
        6 => {
            // quantised, near
            let q = 0.25;
            Vec3::new(
                c.center.x + rng.quant(-2.0, 2.0, q),
                c.center.y + rng.quant(-2.0, 2.0, q),
                c.center.z + rng.quant(-2.0, 2.0, q),
            )
        }
        _ => {
            // separated
            cyl_point(
                rng,
                &c,
                (-c.hh - 3.0, c.hh + 3.0),
                (c.r + sr + 0.1, c.r + sr + 3.0),
            )
        }
    };
    let s = SpherePrim { center, r: sr };
    let dir = match rng.below(8) {
        0 | 1 => Vec3::ZERO,
        2 | 3 => rng.unit_or_axis(0.3),
        4 => c.axis,
        5 => neg(c.axis),
        6 => perp(c.axis, rng),
        _ => scale(rng.unit(), rng.f(0.1, 4.0)),
    };
    let mut p = UnprojParams {
        dir,
        ..UnprojParams::default()
    };
    p.tmax = match rng.below(4) {
        0 => rng.f(0.0, 1.0),
        1 => rng.f(0.0, 10.0),
        2 => 1e10,
        _ => rng.f(0.5, 5.0),
    };
    p.tmin = if rng.chance(0.8) {
        0.0
    } else {
        rng.f(-1.0, 1.0)
    };
    p.min_pt_dist = rng.f(0.001, 0.05);
    p.max_cos = if rng.chance(0.5) { 0.1 } else { 0.9998 };
    p.flag = i32::from(rng.chance(0.5));
    let fc = match rng.below(10) {
        0 | 1 => 0x43,
        2 => 0x41,
        3 => 0x8000_0043u32 as i32,
        _ => -1,
    };
    (c, s, p, fc)
}

fn gen_cyl_sph(rng: &mut Rng) -> Case {
    let (c, s, p, fc) = gen_cyl_sph_parts(rng);
    let case =
        Case::new(0, &Primitive::Cylinder(c), &Primitive::Sphere(s), &p).with_features(fc, -1);
    if rng.chance(0.2) {
        case.with_area(16, 0.01)
    } else {
        case
    }
}

fn gen_sph_cyl(rng: &mut Rng) -> Case {
    let (c, s, mut p, fc) = gen_cyl_sph_parts(rng);
    p.dir = neg(p.dir);
    let case =
        Case::new(0, &Primitive::Sphere(s), &Primitive::Cylinder(c), &p).with_features(-1, fc);
    if rng.chance(0.2) {
        case.with_area(16, 0.01)
    } else {
        case
    }
}

#[test]
fn cyl_sph_linear_matches_dll() {
    check("p2b_ray_cyl_cyl_sph", 300, &gen_cyl_sph);
}

#[test]
fn sph_cyl_linear_matches_dll() {
    check("p2b_ray_cyl_sph_cyl", 200, &gen_sph_cyl);
}

/// Distribution of (ret, iFeature) over many generated cases, through the port (the port is
/// checked against the DLL by the tests above; this one asserts the generators reach every
/// result class).
fn distribution(
    gen: &dyn Fn(&mut Rng) -> Case,
    n: usize,
    seed: u64,
) -> BTreeMap<(i32, i32, i32), usize> {
    let tables = UnprojTables::build();
    let mut rng = Rng::new(seed);
    let mut m = BTreeMap::new();
    for _ in 0..n {
        let c = gen(&mut rng);
        let ans = c.run_port(&tables);
        let w: Vec<&str> = ans.split_whitespace().collect();
        let ret = i32::from_str_radix(w[1], 16).unwrap();
        let fa = u32::from_str_radix(w[27], 16).unwrap() as i32;
        let fb = u32::from_str_radix(w[28], 16).unwrap() as i32;
        let key = if ret == 0 { (0, 0, 0) } else { (ret, fa, fb) };
        *m.entry(key).or_insert(0) += 1;
    }
    m
}

#[test]
fn ray_cyl_generator_coverage() {
    let m = distribution(&gen_ray_cyl, 20000, 1);
    println!("{m:x?}");
    for key in [
        (0, 0, 0),
        (1, 0x80, 0x40),
        (1, 0x81, 0x40),
        (1, 0xA0, 0x40),
        (1, 0x80, 0x41),
        (1, 0x81, 0x42),
        (1, 0xA0, 0x20),
        (1, 0xA0, 0x21),
        (1, 0xA0, 0x41),
        (1, 0xA0, 0x42),
    ] {
        assert!(m.contains_key(&key), "{key:x?} not reached");
    }
}

#[test]
fn cyl_sph_generator_coverage() {
    // dir = 0 and dir != 0 separately (the feature ids overlap)
    let zero = |rng: &mut Rng| {
        let (c, s, mut p, fc) = gen_cyl_sph_parts(rng);
        p.dir = Vec3::ZERO;
        Case::new(0, &Primitive::Cylinder(c), &Primitive::Sphere(s), &p).with_features(fc, -1)
    };
    let moving = |rng: &mut Rng| {
        let (c, s, mut p, fc) = gen_cyl_sph_parts(rng);
        if p.dir == Vec3::ZERO {
            p.dir = rng.unit();
        }
        Case::new(0, &Primitive::Cylinder(c), &Primitive::Sphere(s), &p).with_features(fc, -1)
    };
    let mz = distribution(&zero, 20000, 2);
    let mm = distribution(&moving, 20000, 3);
    println!("dir = 0: {mz:x?}\ndir != 0: {mm:x?}");
    for key in [
        (1, 0x40, 0x40),
        (1, 0x41, 0x40),
        (1, 0x42, 0x40),
        (1, 0x20, 0x40),
        (1, 0x21, 0x40),
    ] {
        assert!(mz.contains_key(&key), "dir = 0: {key:x?} not reached");
    }
    for key in [
        (0, 0, 0),
        (1, 0x40, 0x40),
        (1, 0x41, 0x40),
        (1, 0x42, 0x40),
        (1, 0x20, 0x40),
    ] {
        assert!(mm.contains_key(&key), "dir != 0: {key:x?} not reached");
    }
}
