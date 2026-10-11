//! P2b `box_round`: box–sph (0x35185B80), ray–box (0x35178540), ray–sph (0x3517BDA0) and
//! their 335-byte swap wrappers (0x35186F70, 0x351794F0, 0x3517C880) against the DLL.

use aaemu_physics::collision::contact::UnprojParams;
use aaemu_physics::collision::swept::probe::{check, result_from_words, Case, Rng};
use aaemu_physics::collision::swept::UnprojTables;
use aaemu_physics::geometry::primitives::{BoxPrim, Primitive, RayPrim, SpherePrim};
use aaemu_physics::math::{Mat33, Vec3};
use std::collections::BTreeMap;

fn add(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(a.x + b.x, a.y + b.y, a.z + b.z)
}

fn scale(a: Vec3, s: f32) -> Vec3 {
    Vec3::new(a.x * s, a.y * s, a.z * s)
}

/// Random box: axis-aligned (identity basis, `oriented = 0`) or rotated; quantised sometimes.
fn gen_box(rng: &mut Rng) -> BoxPrim {
    let q = rng.chance(0.3);
    let size = if q {
        Vec3::new(
            rng.quant(0.25, 3.0, 0.25),
            rng.quant(0.25, 3.0, 0.25),
            rng.quant(0.25, 3.0, 0.25),
        )
    } else if rng.chance(0.05) {
        Vec3::new(rng.f(0.0, 0.05), rng.f(0.05, 3.0), rng.f(0.05, 3.0))
    } else {
        rng.vec(0.05, 3.0)
    };
    let center = if q {
        Vec3::new(
            rng.quant(-10.0, 10.0, 0.5),
            rng.quant(-10.0, 10.0, 0.5),
            rng.quant(-10.0, 10.0, 0.5),
        )
    } else {
        rng.vec(-20.0, 20.0)
    };
    let (basis, oriented) = if rng.chance(0.4) {
        (Mat33::IDENTITY, 0)
    } else {
        (rng.rotation(), 1)
    };
    BoxPrim {
        basis,
        oriented,
        center,
        size,
    }
}

/// Box axis `k` (row `k` of the basis).
fn axis(b: &BoxPrim, k: usize) -> Vec3 {
    Vec3::new(b.basis.m[k][0], b.basis.m[k][1], b.basis.m[k][2])
}

/// World point from box-local coordinates (`c + R^T·l`).
fn to_world(b: &BoxPrim, l: Vec3) -> Vec3 {
    let r = &b.basis.m;
    add(
        b.center,
        Vec3::new(
            r[0][0] * l.x + r[1][0] * l.y + r[2][0] * l.z,
            r[0][1] * l.x + r[1][1] * l.y + r[2][1] * l.z,
            r[0][2] * l.x + r[1][2] * l.y + r[2][2] * l.z,
        ),
    )
}

/// A box-local point near a random face / edge / vertex, inside, or far away.
fn local_point(rng: &mut Rng, b: &BoxPrim, reach: f32) -> Vec3 {
    let s = [b.size.x, b.size.y, b.size.z];
    let mut l = [0.0f32; 3];
    let kind = rng.below(5);
    for (i, li) in l.iter_mut().enumerate() {
        let sign = if rng.chance(0.5) { 1.0 } else { -1.0 };
        let out_axis = match kind {
            0 => i == 0, // face region
            1 => i != 2, // edge region
            2 => true,   // vertex region
            3 => false,  // inside
            _ => true,
        };
        *li = if out_axis {
            sign * (s[i] + rng.f(-0.5, 1.0) * reach)
        } else {
            rng.f(-1.0, 1.0) * s[i]
        };
        if kind == 4 {
            *li = rng.f(-3.0, 3.0) * (s[i] + reach);
        }
    }
    // shuffle which axis plays which role
    let rot = rng.below(3) as usize;
    let v = [l[rot], l[(rot + 1) % 3], l[(rot + 2) % 3]];
    Vec3::new(v[0], v[1], v[2])
}

/// Linear unprojection params: 0 / unit / box-axis / non-unit direction, tmin ≠ 0 sometimes.
fn gen_params(rng: &mut Rng, b: Option<&BoxPrim>, toward: Vec3) -> UnprojParams {
    let mut p = UnprojParams::default();
    match rng.below(6) {
        0 => {}
        1 => p.dir = rng.unit_or_axis(0.3),
        2 => {
            if let Some(b) = b {
                let k = rng.below(3) as usize;
                let s = if rng.chance(0.5) { 1.0 } else { -1.0 };
                p.dir = scale(axis(b, k), s);
            } else {
                p.dir = rng.unit_or_axis(1.0);
            }
        }
        3 => {
            let l = (toward.x * toward.x + toward.y * toward.y + toward.z * toward.z).sqrt();
            if l > 1e-3 {
                p.dir = scale(toward, 1.0 / l);
            } else {
                p.dir = rng.unit();
            }
        }
        4 => {
            let u = rng.unit();
            p.dir = scale(u, rng.f(0.1, 5.0));
        }
        _ => {
            p.dir = Vec3::new(
                rng.quant(-1.0, 1.0, 0.5),
                rng.quant(-1.0, 1.0, 0.5),
                rng.quant(-1.0, 1.0, 0.5),
            );
        }
    }
    p.tmax = rng.f(0.0, 10.0);
    p.tmin = if rng.chance(0.75) {
        0.0
    } else {
        rng.f(-1.0, 1.0)
    };
    p.min_pt_dist = rng.f(0.001, 0.05);
    p.max_cos = if rng.chance(0.5) { 0.1 } else { 0.9998 };
    p.flag = rng.below(2) as i32;
    p
}

/// A case generator.
type Gen = dyn Fn(&mut Rng) -> Case;

fn finish(rng: &mut Rng, c: Case) -> Case {
    if rng.chance(0.2) {
        c.with_area(rng.pick(&[1, 4, 256]), 0.01)
    } else {
        c
    }
}

// ------------------------------------------------------------------------------------ box–sph

fn box_sph_parts(rng: &mut Rng) -> (Primitive, Primitive, UnprojParams) {
    let b = gen_box(rng);
    let r = if rng.chance(0.1) {
        rng.f(0.001, 0.05)
    } else {
        rng.f(0.05, 3.0)
    };
    let l = if rng.chance(0.05) {
        Vec3::ZERO
    } else {
        local_point(rng, &b, r)
    };
    let c = to_world(&b, l);
    let away = Vec3::new(b.center.x - c.x, b.center.y - c.y, b.center.z - c.z);
    let p = gen_params(rng, Some(&b), away);
    (
        Primitive::Box(b),
        Primitive::Sphere(SpherePrim { center: c, r }),
        p,
    )
}

fn gen_box_sph(rng: &mut Rng) -> Case {
    let (b, s, p) = box_sph_parts(rng);
    let c = Case::new(0, &b, &s, &p);
    finish(rng, c)
}

fn gen_sph_box(rng: &mut Rng) -> Case {
    let (b, s, p) = box_sph_parts(rng);
    let c = Case::new(0, &s, &b, &p);
    finish(rng, c)
}

// ------------------------------------------------------------------------------------ ray–box

fn ray_box_parts(rng: &mut Rng) -> (Primitive, Primitive, UnprojParams) {
    let b = gen_box(rng);
    let reach = rng.f(0.1, 2.0);
    let l0 = local_point(rng, &b, reach);
    let l1 = match rng.below(6) {
        0 => l0, // zero-length ray
        1 => {
            // parallel to a box axis
            let k = rng.below(3);
            let len = rng.f(-4.0, 4.0);
            match k {
                0 => Vec3::new(l0.x + len, l0.y, l0.z),
                1 => Vec3::new(l0.x, l0.y + len, l0.z),
                _ => Vec3::new(l0.x, l0.y, l0.z + len),
            }
        }
        2 => Vec3::new(-l0.x, -l0.y, -l0.z), // through the centre
        _ => local_point(rng, &b, reach),
    };
    let o = to_world(&b, l0);
    let e = to_world(&b, l1);
    let rd = Vec3::new(e.x - o.x, e.y - o.y, e.z - o.z);
    let mid = add(o, scale(rd, 0.5));
    let away = Vec3::new(mid.x - b.center.x, mid.y - b.center.y, mid.z - b.center.z);
    let p = gen_params(rng, Some(&b), away);
    (
        Primitive::Ray(RayPrim { origin: o, dir: rd }),
        Primitive::Box(b),
        p,
    )
}

fn gen_ray_box(rng: &mut Rng) -> Case {
    let (r, b, p) = ray_box_parts(rng);
    finish(rng, Case::new(0, &r, &b, &p))
}

fn gen_box_ray(rng: &mut Rng) -> Case {
    let (r, b, p) = ray_box_parts(rng);
    finish(rng, Case::new(0, &b, &r, &p))
}

// ------------------------------------------------------------------------------------ ray–sph

fn ray_sph_parts(rng: &mut Rng) -> (Primitive, Primitive, UnprojParams) {
    let r = rng.f(0.05, 3.0);
    let c = if rng.chance(0.3) {
        Vec3::new(
            rng.quant(-10.0, 10.0, 0.5),
            rng.quant(-10.0, 10.0, 0.5),
            rng.quant(-10.0, 10.0, 0.5),
        )
    } else {
        rng.vec(-20.0, 20.0)
    };
    let o = add(c, scale(rng.unit_or_axis(0.2), r * rng.f(0.0, 2.0)));
    let rd = match rng.below(6) {
        0 => Vec3::ZERO,
        1 => scale(rng.unit_or_axis(0.5), rng.f(0.1, 5.0)),
        2 => {
            // through / past the sphere
            let target = add(c, scale(rng.unit(), r * rng.f(0.0, 1.5)));
            let d = Vec3::new(target.x - o.x, target.y - o.y, target.z - o.z);
            scale(d, rng.f(0.5, 3.0))
        }
        _ => scale(rng.unit(), rng.f(0.1, 6.0)),
    };
    let mid = add(o, scale(rd, 0.5));
    let away = Vec3::new(mid.x - c.x, mid.y - c.y, mid.z - c.z);
    let p = gen_params(rng, None, away);
    (
        Primitive::Ray(RayPrim { origin: o, dir: rd }),
        Primitive::Sphere(SpherePrim { center: c, r }),
        p,
    )
}

fn gen_ray_sph(rng: &mut Rng) -> Case {
    let (r, s, p) = ray_sph_parts(rng);
    finish(rng, Case::new(0, &r, &s, &p))
}

fn gen_sph_ray(rng: &mut Rng) -> Case {
    let (r, s, p) = ray_sph_parts(rng);
    finish(rng, Case::new(0, &s, &r, &p))
}

// ------------------------------------------------------------------------------------ tests

#[test]
fn box_sph_linear_matches_dll() {
    check("p2b_box_round_box_sph", 300, &gen_box_sph);
}

#[test]
fn sph_box_linear_matches_dll() {
    check("p2b_box_round_sph_box", 200, &gen_sph_box);
}

#[test]
fn ray_box_linear_matches_dll() {
    check("p2b_box_round_ray_box", 300, &gen_ray_box);
}

#[test]
fn box_ray_linear_matches_dll() {
    check("p2b_box_round_box_ray", 200, &gen_box_ray);
}

#[test]
fn ray_sph_linear_matches_dll() {
    check("p2b_box_round_ray_sph", 300, &gen_ray_sph);
}

#[test]
fn sph_ray_linear_matches_dll() {
    check("p2b_box_round_sph_ray", 200, &gen_sph_ray);
}

/// Distribution of return values and feature ids of the generators (run with
/// `--ignored --nocapture`); evidence that the generators reach every branch.
#[test]
#[ignore = "statistics only"]
fn box_round_distribution() {
    let tables = UnprojTables::build();
    let gens: [(&str, &Gen); 6] = [
        ("box_sph", &gen_box_sph),
        ("sph_box", &gen_sph_box),
        ("ray_box", &gen_ray_box),
        ("box_ray", &gen_box_ray),
        ("ray_sph", &gen_ray_sph),
        ("sph_ray", &gen_sph_ray),
    ];
    for (name, g) in gens {
        let mut rng = Rng::new(42);
        let mut hist: BTreeMap<String, usize> = BTreeMap::new();
        let n = 20000;
        for _ in 0..n {
            let c = g(&mut rng);
            let ans = c.run_port(&tables);
            let w: Vec<u32> = ans
                .split_whitespace()
                .filter_map(|t| u32::from_str_radix(t, 16).ok())
                .collect();
            let ret = w[0];
            let dir0 = c.params[1] == 0 && c.params[2] == 0 && c.params[3] == 0;
            // answer words: ret, P[13], R[12] (after the "ret"/"P"/"R" tokens were dropped)
            let r = result_from_words(&w[14..26]);
            let key = if ret == 0 {
                format!("ret0 dir0={dir0}")
            } else {
                format!(
                    "ret1 dir0={dir0} fA={:02x} fB={:02x}",
                    r.i_feature[0] & 0xF0,
                    r.i_feature[1] & 0xE0
                )
            };
            *hist.entry(key).or_default() += 1;
        }
        println!("== {name}");
        for (k, v) in hist {
            println!("  {k}: {v}");
        }
    }
}
