//! P2b `rot_tri`: rotational tri–tri (0x3519A730), ray–tri (0x3519D340) and the tri–ray swap
//! wrapper (0x3519EDC0) against the DLL (`physoracle --p2b`).

use aaemu_physics::collision::contact::{UnprojParams, UnprojResult};
use aaemu_physics::collision::swept::probe::{check, Case, Rng};
use aaemu_physics::collision::swept::rot_tri::{unproject_ray_tri_rot, unproject_tri_tri_rot};
use aaemu_physics::collision::swept::UnprojTables;
use aaemu_physics::geometry::primitives::{Primitive, RayPrim, Triangle};
use aaemu_physics::math::Vec3;
use std::collections::BTreeMap;

fn add(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(a.x + b.x, a.y + b.y, a.z + b.z)
}
fn scale(a: Vec3, s: f32) -> Vec3 {
    Vec3::new(a.x * s, a.y * s, a.z * s)
}

/// A random point, sometimes on a coarse grid (exact coincidences / parallels).
fn point(rng: &mut Rng, around: Vec3, r: f32, quant: bool) -> Vec3 {
    if quant {
        add(
            around,
            Vec3::new(
                rng.quant(-r, r, 0.25),
                rng.quant(-r, r, 0.25),
                rng.quant(-r, r, 0.25),
            ),
        )
    } else {
        add(around, rng.vec(-r, r))
    }
}

/// A triangle around `q` of size ~`s` with its normal (float-normalised cross product, sometimes
/// flipped, rarely degenerate / arbitrary).
fn triangle(rng: &mut Rng, q: Vec3, s: f32) -> Triangle {
    let quant = rng.chance(0.25);
    let mut pt = [
        point(rng, q, s, quant),
        point(rng, q, s, quant),
        point(rng, q, s, quant),
    ];
    if rng.chance(0.03) {
        pt[2] = pt[1]; // degenerate
    }
    let e1 = Vec3::new(pt[1].x - pt[0].x, pt[1].y - pt[0].y, pt[1].z - pt[0].z);
    let e2 = Vec3::new(pt[2].x - pt[0].x, pt[2].y - pt[0].y, pt[2].z - pt[0].z);
    let c = Vec3::new(
        e1.y * e2.z - e1.z * e2.y,
        e1.z * e2.x - e1.x * e2.z,
        e1.x * e2.y - e1.y * e2.x,
    );
    let l2 = c.x * c.x + c.y * c.y + c.z * c.z;
    let mut n = if l2 > 1e-12 {
        scale(c, 1.0 / l2.sqrt())
    } else {
        Vec3::new(0.0, 0.0, 1.0)
    };
    if rng.chance(0.3) {
        n = scale(n, -1.0);
    }
    if rng.chance(0.02) {
        n = rng.unit();
    }
    Triangle { pt, n }
}

fn rot_params(rng: &mut Rng, center: Vec3) -> UnprojParams {
    let dir = rng.unit_or_axis(0.3);
    let ang = match rng.below(4) {
        0 => rng.f(0.001, 0.1),
        1 => rng.f(0.1, 1.0),
        2 => std::f32::consts::FRAC_PI_2,
        _ => rng.f(0.0, std::f32::consts::FRAC_PI_2),
    };
    UnprojParams {
        dir,
        center,
        tmax: ang.sin(),
        vel: rng.f(0.0, 5.0),
        min_pt_dist: rng.pick(&[0.001, 0.01, 0.02, 0.05]),
        max_cos: 0.1,
        flag: 1,
        ..UnprojParams::default()
    }
}

fn gen_tri_tri(rng: &mut Rng) -> Case {
    let q = rng.vec(-10.0, 10.0);
    let s = rng.pick(&[0.3f32, 1.0, 2.0]);
    let b = triangle(rng, q, s);
    let a = if rng.chance(0.15) {
        // shares a vertex / edge with B
        let mut t = triangle(rng, q, s);
        t.pt[0] = b.pt[rng.below(3) as usize];
        if rng.chance(0.5) {
            t.pt[1] = b.pt[rng.below(3) as usize];
        }
        t
    } else {
        let off = rng.vec(-s, s);
        triangle(rng, add(q, off), s)
    };
    let off = scale(rng.vec(-1.0, 1.0), s * rng.pick(&[0.5f32, 2.0, 4.0]));
    let center = add(q, off);
    let p = rot_params(rng, center);
    let mut c = Case::new(1, &Primitive::Triangle(a), &Primitive::Triangle(b), &p);
    match rng.below(10) {
        0 => {
            let j = rng.below(3) as i32;
            c = c.with_features(0x40, 0x80 | j);
        }
        1 => {
            let j = rng.below(3) as i32;
            c = c.with_features(0x80 | j, 0x40);
        }
        2 => {
            let i = rng.below(3) as i32;
            let j = rng.below(3) as i32;
            c = c.with_features(0xA0 | i, 0xA0 | j);
        }
        3 => {
            let fa = rng.pick(&[0, 0x40, 0x80, 0x81, 0xA0, 0xA2, 0x20]);
            let fb = rng.pick(&[0, 0x40, 0x82, 0xA1, -1]);
            c = c.with_features(fa, fb);
        }
        _ => {}
    }
    if rng.chance(0.2) {
        c = c.with_area(16, 0.01);
    }
    c
}

fn ray_tri_pair(rng: &mut Rng) -> (RayPrim, Triangle, UnprojParams, i32, i32) {
    let q = rng.vec(-10.0, 10.0);
    let s = rng.pick(&[0.3f32, 1.0, 2.0]);
    let tri = triangle(rng, q, s);
    let origin = if rng.chance(0.1) {
        tri.pt[rng.below(3) as usize]
    } else {
        let qz = rng.chance(0.2);
        point(rng, q, s * 1.5, qz)
    };
    let len = rng.f(0.2, 3.0) * s;
    let dir = if rng.chance(0.2) {
        // towards / across the triangle
        let t = add(
            scale(tri.pt[0], 0.4),
            add(scale(tri.pt[1], 0.3), scale(tri.pt[2], 0.3)),
        );
        let v = Vec3::new(t.x - origin.x, t.y - origin.y, t.z - origin.z);
        scale(v, rng.f(0.5, 2.0))
    } else {
        scale(rng.unit_or_axis(0.2), len)
    };
    let ray = RayPrim { origin, dir };
    let center = match rng.below(4) {
        0 => origin, // the rope case: centre = ray origin
        1 => add(origin, rng.vec(-0.01, 0.01)),
        _ => {
            let off = scale(rng.vec(-1.0, 1.0), s * 2.0);
            add(origin, off)
        }
    };
    let p = rot_params(rng, center);
    let (fa, fb) = match rng.below(8) {
        0 => (0xA0, 0xA0 | rng.below(3) as i32),
        1 => (
            rng.pick(&[0, 0x80, 0xA0, 0x40]),
            rng.pick(&[0xA0, 0xA1, 0x40, 0x81]),
        ),
        _ => (-1, -1),
    };
    (ray, tri, p, fa, fb)
}

fn gen_ray_tri(rng: &mut Rng) -> Case {
    let (ray, tri, p, fa, fb) = ray_tri_pair(rng);
    let mut c =
        Case::new(1, &Primitive::Ray(ray), &Primitive::Triangle(tri), &p).with_features(fa, fb);
    if rng.chance(0.2) {
        c = c.with_area(4, 0.01);
    }
    c
}

fn gen_tri_ray(rng: &mut Rng) -> Case {
    let (ray, tri, p, fa, fb) = ray_tri_pair(rng);
    let mut c =
        Case::new(1, &Primitive::Triangle(tri), &Primitive::Ray(ray), &p).with_features(fb, fa);
    if rng.chance(0.2) {
        c = c.with_area(4, 0.01);
    }
    c
}

#[test]
fn tri_tri_rot_matches_dll() {
    check("p2b_rot_tri_tri_tri", 250, &gen_tri_tri);
}

#[test]
fn ray_tri_rot_matches_dll() {
    check("p2b_rot_tri_ray_tri", 300, &gen_ray_tri);
}

#[test]
fn tri_ray_rot_matches_dll() {
    check("p2b_rot_tri_tri_ray", 300, &gen_tri_ray);
}

fn word(w: &[&str], i: usize) -> u32 {
    u32::from_str_radix(w[i], 16).unwrap()
}

/// Branch coverage of the generators (port only): every result kind must occur. Answer words:
/// `ret <w> P <13> R <12>`, so the result starts at index 17 and the features are 27, 28.
#[test]
fn generator_coverage() {
    let tables = UnprojTables::build();
    let mut rng = Rng::new(12345);
    let mut tt: BTreeMap<String, usize> = BTreeMap::new();
    for _ in 0..20000 {
        let c = gen_tri_tri(&mut rng);
        let ans = c.run_port(&tables);
        let w: Vec<&str> = ans.split_whitespace().collect();
        let key = if w[1] == "00000000" {
            "ret0".to_string()
        } else {
            let t = f64::from_bits(u64::from(word(&w, 17)) | (u64::from(word(&w, 18)) << 32));
            format!(
                "{:02x}/{:02x}{}",
                word(&w, 27) & 0xE0,
                word(&w, 28) & 0xE0,
                if t == 0.0 { " t0" } else { "" }
            )
        };
        *tt.entry(key).or_default() += 1;
    }
    eprintln!("tri-tri: {tt:?}");
    for k in [
        "ret0", "40/80", "80/40", "a0/a0", "40/80 t0", "80/40 t0", "a0/a0 t0",
    ] {
        assert!(
            tt.get(k).copied().unwrap_or(0) > 20,
            "tri-tri never hit {k}"
        );
    }
    let mut rt: BTreeMap<String, usize> = BTreeMap::new();
    for _ in 0..20000 {
        let c = gen_ray_tri(&mut rng);
        let ans = c.run_port(&tables);
        let w: Vec<&str> = ans.split_whitespace().collect();
        let key = if w[1] == "00000000" {
            "ret0".to_string()
        } else {
            format!("{}/{}", w[27], w[28])
        };
        *rt.entry(key).or_default() += 1;
    }
    eprintln!("ray-tri: {rt:?}");
    for k in [
        "ret0",
        "00000080/00000040",
        "00000081/00000040",
        "000000a0/000000a0",
        "000000a0/000000a1",
        "000000a0/000000a2",
    ] {
        assert!(
            rt.get(k).copied().unwrap_or(0) > 20,
            "ray-tri never hit {k}"
        );
    }
}

#[test]
fn ray_end_hits_face() {
    // the ray (0.5,0,0.5)-(1.5,0,0.5) swings down about +y through (0,0,0.6) onto the z = 0
    // triangle; the end point arrives first: 0.6 - 0.1 cos(phi) - 1.5 sin(phi) = 0
    let tri = Triangle {
        pt: [
            Vec3::new(0.0, -2.0, 0.0),
            Vec3::new(3.0, -2.0, 0.0),
            Vec3::new(0.0, 2.0, 0.0),
        ],
        n: Vec3::new(0.0, 0.0, 1.0),
    };
    let ray = RayPrim {
        origin: Vec3::new(0.5, 0.0, 0.5),
        dir: Vec3::new(1.0, 0.0, 0.0),
    };
    let mut p = UnprojParams {
        dir: Vec3::new(0.0, 1.0, 0.0),
        center: Vec3::new(0.0, 0.0, 0.6),
        tmax: 1.0,
        min_pt_dist: 0.01,
        ..UnprojParams::default()
    };
    let mut out = UnprojResult::default();
    let r = unproject_ray_tri_rot(&mut p, &ray, -1, &tri, -1, &mut out);
    assert_eq!(r, 1);
    assert_eq!(out.i_feature, [0x81, 0x40]);
    let (s, c) = (out.t, out.cos_t);
    assert!((0.6 - 0.1 * c - 1.5 * s).abs() < 1e-6, "{out:?}");
    assert!(out.pt.z.abs() < 1e-5, "{out:?}");
    assert_eq!(out.n, Vec3::new(-0.0, -0.0, -1.0));

    // the same motion for a thin triangle A along the ray: tri-tri vertex-face, A's vertex 1
    let a = Triangle {
        pt: [
            Vec3::new(0.5, -0.1, 0.5),
            Vec3::new(1.5, 0.0, 0.5),
            Vec3::new(0.5, 0.1, 0.5),
        ],
        n: Vec3::new(0.0, 0.0, 1.0),
    };
    let mut out2 = UnprojResult::default();
    let r = unproject_tri_tri_rot(&mut p, &a, -1, &tri, -1, &mut out2);
    assert_eq!(r, 1);
    assert_eq!(out2.i_feature, [0x81, 0x40]);
    assert_eq!(out2.t, out.t);
}
