//! P2b: rotational ray–cylinder unprojection (0x351A0C10) and its cyl–ray swap wrapper
//! (0x351A4460) against the DLL (`physoracle --p2b`).

use aaemu_physics::collision::contact::UnprojParams;
use aaemu_physics::collision::swept::probe::{check, Case, Rng};
use aaemu_physics::collision::swept::UnprojTables;
use aaemu_physics::geometry::primitives::{CylinderPrim, Primitive, RayPrim};
use aaemu_physics::math::Vec3;

type D3 = [f64; 3];

fn d3(v: Vec3) -> D3 {
    [f64::from(v.x), f64::from(v.y), f64::from(v.z)]
}
fn v3(v: D3) -> Vec3 {
    Vec3::new(v[0] as f32, v[1] as f32, v[2] as f32)
}
fn add(a: D3, b: D3) -> D3 {
    [a[0] + b[0], a[1] + b[1], a[2] + b[2]]
}
fn sub(a: D3, b: D3) -> D3 {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}
fn mul(a: D3, s: f64) -> D3 {
    [a[0] * s, a[1] * s, a[2] * s]
}
fn dot(a: D3, b: D3) -> f64 {
    a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
}
fn cross(a: D3, b: D3) -> D3 {
    [
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    ]
}
fn norm(a: D3) -> D3 {
    let l = dot(a, a).sqrt();
    if l > 0.0 {
        mul(a, 1.0 / l)
    } else {
        [0.0, 0.0, 1.0]
    }
}

/// Rotate a point about (c, unit axis k) by angle `phi`.
fn rot(pt: D3, c: D3, k: D3, phi: f64) -> D3 {
    let v = sub(pt, c);
    let h = mul(k, dot(v, k));
    let r = sub(v, h);
    add(
        c,
        add(add(h, mul(r, phi.cos())), mul(cross(k, r), phi.sin())),
    )
}

/// A unit vector perpendicular to `n` (axis-aligned when `n` is).
fn perp(rng: &mut Rng, n: D3) -> D3 {
    let w = if rng.chance(0.4) {
        // an axis direction, exactly perpendicular if n is axis-aligned
        let i = rng.below(3) as usize;
        let mut e = [0.0; 3];
        e[i] = 1.0;
        e
    } else {
        d3(rng.unit())
    };
    let p = sub(w, mul(n, dot(w, n)));
    if dot(p, p) < 1e-6 {
        norm(cross(n, [0.3, 0.5, 0.8]))
    } else {
        norm(p)
    }
}

fn quantise(rng: &mut Rng, v: D3) -> D3 {
    if rng.chance(0.25) {
        [
            (v[0] * 8.0).round() / 8.0,
            (v[1] * 8.0).round() / 8.0,
            (v[2] * 8.0).round() / 8.0,
        ]
    } else {
        v
    }
}

/// Ray, cylinder and params of one rotational case.
fn gen_parts(rng: &mut Rng) -> (RayPrim, CylinderPrim, UnprojParams) {
    let k = d3(rng.unit_or_axis(0.4)); // rotation axis
    let center = d3(rng.vec(-4.0, 4.0));
    let center = quantise(rng, center);
    // cylinder axis: parallel / perpendicular to the rotation axis or random
    let n = match rng.below(5) {
        0 => k,
        1 => mul(k, -1.0),
        2 => perp(rng, k),
        _ => d3(rng.unit_or_axis(0.4)),
    };
    let rad = if rng.chance(0.1) {
        rng.f(0.001, 0.05)
    } else {
        rng.f(0.05, 2.0)
    };
    let hh = if rng.chance(0.1) {
        rng.f(0.001, 0.05)
    } else {
        rng.f(0.05, 3.0)
    };
    let (rad, hh) = (f64::from(rad), f64::from(hh));
    // contact angle
    let phi = match rng.below(6) {
        0 => std::f64::consts::FRAC_PI_2,
        1 => f64::from(rng.f(0.0, 0.05)),
        _ => f64::from(rng.f(0.0, 1.0)) * std::f64::consts::FRAC_PI_2,
    };
    // cylinder centre: near the rotation centre at some distance
    let cc = if rng.chance(0.15) {
        add(center, mul(k, f64::from(rng.f(-3.0, 3.0))))
    } else {
        add(center, d3(rng.vec(-4.0, 4.0)))
    };
    let cc = quantise(rng, cc);
    let u = perp(rng, n);
    let w = cross(n, u);
    let ang = f64::from(rng.f(0.0, 1.0)) * std::f64::consts::TAU;
    let radial = add(mul(u, ang.cos()), mul(w, ang.sin()));
    let len = f64::from(rng.f(0.05, 6.0));
    // rotated (contact) ray: origin' and dir'
    let (o2, d2) = match rng.below(9) {
        0 | 1 => {
            // ray end on the side
            let q = add(
                add(cc, mul(radial, rad)),
                mul(n, hh * f64::from(rng.f(-1.0, 1.0))),
            );
            let dir = norm(add(
                mul(radial, -f64::from(rng.f(0.0, 1.0))),
                d3(rng.unit()),
            ));
            if rng.chance(0.5) {
                (q, mul(dir, len))
            } else {
                (sub(q, mul(dir, len)), mul(dir, len))
            }
        }
        2 | 3 => {
            // ray end on a cap
            let s = if rng.chance(0.5) { 1.0 } else { -1.0 };
            let q = add(
                add(cc, mul(n, s * hh)),
                mul(radial, rad * f64::from(rng.f(0.0, 1.0))),
            );
            let dir = norm(add(mul(n, -s * f64::from(rng.f(0.0, 1.0))), d3(rng.unit())));
            if rng.chance(0.5) {
                (q, mul(dir, len))
            } else {
                (sub(q, mul(dir, len)), mul(dir, len))
            }
        }
        4 | 5 => {
            // ray line tangent to the side
            let q = add(
                add(cc, mul(radial, rad)),
                mul(n, hh * f64::from(rng.f(-1.0, 1.0))),
            );
            let tdir = norm(add(
                mul(cross(n, radial), f64::from(rng.f(-1.0, 1.0))),
                mul(n, f64::from(rng.f(-1.0, 1.0))),
            ));
            let f = f64::from(rng.f(0.0, 1.0));
            (sub(q, mul(tdir, len * f)), mul(tdir, len))
        }
        6 | 7 => {
            // ray line through a rim
            let s = if rng.chance(0.5) { 1.0 } else { -1.0 };
            let q = add(add(cc, mul(n, s * hh)), mul(radial, rad));
            let dir = if rng.chance(0.5) {
                norm(cross(radial, n))
            } else {
                d3(rng.unit())
            };
            let f = f64::from(rng.f(0.0, 1.0));
            (sub(q, mul(dir, len * f)), mul(dir, len))
        }
        _ => {
            // random ray near the cylinder
            (
                add(cc, d3(rng.vec(-3.0, 3.0))),
                mul(d3(rng.unit_or_axis(0.3)), len),
            )
        }
    };
    // un-rotate the contact ray by phi (the routine rotates it forward)
    let back = if rng.chance(0.1) { phi * 1.3 } else { phi };
    let o = rot(o2, center, k, -back);
    let e = rot(add(o2, d2), center, k, -back);
    let mut origin = o;
    let mut dir = sub(e, o);
    if rng.chance(0.05) {
        origin = center; // origin at the rotation centre (min_pt_dist skip)
    } else if rng.chance(0.05) {
        origin = add(center, mul(d3(rng.unit()), 0.005));
    }
    if rng.chance(0.03) {
        dir = mul(k, f64::from(rng.f(0.1, 3.0))); // ray along the axis
    }
    let ray = RayPrim {
        origin: v3(origin),
        dir: v3(dir),
    };
    let cyl = CylinderPrim {
        center: v3(cc),
        axis: v3(n),
        r: rad as f32,
        hh: hh as f32,
    };
    let tmax = rng.f(0.01, 1.0);
    let min_pt_dist = if rng.chance(0.3) {
        rng.f(0.001, 0.05)
    } else {
        0.01
    };
    let vel = rng.f(0.0, 5.0);
    let p = UnprojParams {
        dir: v3(k),
        center: v3(center),
        tmax,
        min_pt_dist,
        max_cos: 0.1,
        vel,
        ..UnprojParams::default()
    };
    (ray, cyl, p)
}

fn gen_ray_cyl(rng: &mut Rng) -> Case {
    let (ray, cyl, p) = gen_parts(rng);
    Case::new(1, &Primitive::Ray(ray), &Primitive::Cylinder(cyl), &p)
}

fn gen_cyl_ray(rng: &mut Rng) -> Case {
    let (ray, cyl, mut p) = gen_parts(rng);
    // the wrapper negates the axis: mostly pre-negate it so the generated contacts are reached
    if rng.chance(0.7) {
        p.dir = Vec3::new(-p.dir.x, -p.dir.y, -p.dir.z);
    }
    Case::new(1, &Primitive::Cylinder(cyl), &Primitive::Ray(ray), &p)
}

#[test]
fn ray_cyl_rot_matches_dll() {
    check("p2b_rot_cyl_ray_cyl", 300, &gen_ray_cyl);
}

#[test]
fn cyl_ray_rot_matches_dll() {
    check("p2b_rot_cyl_cyl_ray", 200, &gen_cyl_ray);
}

/// Outcome histogram of a generator through the port (`fail` or `featA/featB` low bytes).
fn histogram(
    gen: &dyn Fn(&mut Rng) -> Case,
    n: usize,
) -> std::collections::BTreeMap<String, usize> {
    let tables = UnprojTables::build();
    let mut rng = Rng::new(0x5EED_C0DE);
    let mut hist = std::collections::BTreeMap::<String, usize>::new();
    for _ in 0..n {
        let ans = gen(&mut rng).run_port(&tables);
        let w: Vec<&str> = ans.split_whitespace().collect();
        // ret, P + 13, R + 12: features at R[10], R[11]
        let key = if w[1] == "00000000" {
            "fail".to_string()
        } else {
            format!("{}/{}", &w[27][6..], &w[28][6..])
        };
        *hist.entry(key).or_default() += 1;
    }
    hist
}

/// Branch coverage of the generators (port only): every output class must be reached.
#[test]
fn ray_cyl_rot_coverage() {
    let hist = histogram(&gen_ray_cyl, 20_000);
    eprintln!("ray-cyl outcomes: {hist:?}");
    for k in [
        "fail", "80/41", "81/41", "80/42", "81/42", "80/40", "81/40", "20/40", "20/20", "20/21",
    ] {
        assert!(hist.contains_key(k), "class {k} never reached: {hist:?}");
    }
    let hist = histogram(&gen_cyl_ray, 20_000);
    eprintln!("cyl-ray outcomes: {hist:?}");
    for k in [
        "fail", "41/80", "41/81", "42/80", "42/81", "40/80", "40/81", "40/20", "20/20", "21/20",
    ] {
        assert!(hist.contains_key(k), "class {k} never reached: {hist:?}");
    }
    eprintln!(
        "degenerate outcomes: {:?}",
        histogram(&gen_degenerate, 20_000)
    );
}

/// Degenerate inputs: zero-length rays, rays through the centre, zero radius / height,
/// non-unit and zero rotation axes, coincident centres, quantised coordinates.
fn gen_degenerate(rng: &mut Rng) -> Case {
    let (mut ray, mut cyl, mut p) = gen_parts(rng);
    match rng.below(9) {
        0 => ray.dir = Vec3::ZERO,
        1 => cyl.r = 0.0,
        2 => cyl.hh = 0.0,
        3 => p.dir = Vec3::ZERO,
        4 => {
            let s = rng.f(0.2, 3.0);
            p.dir = Vec3::new(p.dir.x * s, p.dir.y * s, p.dir.z * s);
        }
        5 => {
            cyl.center = p.center;
            ray.origin = p.center;
        }
        6 => cyl.axis = Vec3::ZERO,
        7 => {
            let q = |v: f32| (v * 2.0).round() * 0.5;
            ray.origin = Vec3::new(q(ray.origin.x), q(ray.origin.y), q(ray.origin.z));
            ray.dir = Vec3::new(q(ray.dir.x), q(ray.dir.y), q(ray.dir.z));
            cyl.center = Vec3::new(q(cyl.center.x), q(cyl.center.y), q(cyl.center.z));
            p.center = Vec3::new(q(p.center.x), q(p.center.y), q(p.center.z));
        }
        _ => p.min_pt_dist = rng.f(0.5, 5.0),
    }
    Case::new(1, &Primitive::Ray(ray), &Primitive::Cylinder(cyl), &p)
}

#[test]
fn ray_cyl_rot_degenerate_matches_dll() {
    check("p2b_rot_cyl_degenerate", 200, &gen_degenerate);
}
