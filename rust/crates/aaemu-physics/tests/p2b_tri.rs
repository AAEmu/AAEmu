//! P2b group `tri`: linear unprojection with a triangle as A (tri–box 0x3516FDB0, tri–tri
//! 0x3516EAE0, tri–ray 0x35176B40, tri–sph 0x35175490) and the swap wrappers, against the DLL
//! (`physoracle --p2b`).

use aaemu_physics::collision::contact::UnprojParams;
use aaemu_physics::collision::swept::probe::{check, Case, Rng};
use aaemu_physics::geometry::primitives::{Primitive, RayPrim, SpherePrim, Triangle};
use aaemu_physics::math::Vec3;

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
fn norm(a: Vec3) -> Vec3 {
    let l = (a.x * a.x + a.y * a.y + a.z * a.z).sqrt();
    if l > 0.0 {
        scale(a, 1.0 / l)
    } else {
        Vec3::new(0.0, 0.0, 1.0)
    }
}

/// A triangle around the origin of size ~`s`: random, sliver, right-angled with quantised
/// coordinates, axis-aligned, or degenerate.
fn tri_shape(rng: &mut Rng, s: f32) -> [Vec3; 3] {
    match rng.below(7) {
        0 | 1 => [rng.vec(-s, s), rng.vec(-s, s), rng.vec(-s, s)],
        2 => {
            // sliver: third point close to the line of the first two
            let p0 = rng.vec(-s, s);
            let p1 = rng.vec(-s, s);
            let t = rng.f(-0.2, 1.2);
            let off = scale(rng.unit(), s * rng.pick(&[0.0, 1e-4, 1e-3, 0.02]));
            [p0, p1, add(add(p0, scale(sub(p1, p0), t)), off)]
        }
        3 => {
            // right angle, quantised, in a coordinate plane
            let q = rng.pick(&[0.25f32, 0.125, 0.5]);
            let a = rng.quant(0.25, s, q).max(q);
            let b = rng.quant(0.25, s, q).max(q);
            let o = Vec3::new(rng.quant(-s, s, q), rng.quant(-s, s, q), rng.quant(-s, s, q));
            let (u, v) = match rng.below(3) {
                0 => (Vec3::new(a, 0.0, 0.0), Vec3::new(0.0, b, 0.0)),
                1 => (Vec3::new(0.0, a, 0.0), Vec3::new(0.0, 0.0, b)),
                _ => (Vec3::new(0.0, 0.0, a), Vec3::new(b, 0.0, 0.0)),
            };
            if rng.chance(0.5) {
                [o, add(o, u), add(o, v)]
            } else {
                [o, add(o, v), add(o, u)]
            }
        }
        4 => {
            // right angle in a random orientation
            let o = rng.vec(-s, s);
            let u = rng.unit();
            let v = norm(cross(u, rng.unit()));
            [
                o,
                add(o, scale(u, rng.f(0.2, s))),
                add(o, scale(v, rng.f(0.2, s))),
            ]
        }
        5 => {
            // equilateral-ish
            let o = rng.vec(-s * 0.5, s * 0.5);
            let u = rng.unit();
            let v = norm(cross(u, rng.unit()));
            let r = rng.f(0.3, s);
            let k = 0.866_025_4f32;
            [
                add(o, scale(u, r)),
                add(o, add(scale(u, -0.5 * r), scale(v, k * r))),
                add(o, add(scale(u, -0.5 * r), scale(v, -k * r))),
            ]
        }
        _ => {
            // degenerate: two equal points or all collinear
            let p0 = rng.vec(-s, s);
            let p1 = rng.vec(-s, s);
            if rng.chance(0.5) {
                [p0, p1, p1]
            } else {
                [p0, p1, add(p0, scale(sub(p1, p0), 2.0))]
            }
        }
    }
}

/// The stored normal: usually the unit winding normal, sometimes slightly off, flipped, or an
/// unrelated unit vector.
fn tri_normal(rng: &mut Rng, pt: &[Vec3; 3]) -> Vec3 {
    let n = norm(cross(sub(pt[1], pt[0]), sub(pt[2], pt[0])));
    let r = rng.f(0.0, 1.0);
    if r < 0.75 {
        n
    } else if r < 0.88 {
        norm(add(n, scale(rng.unit(), rng.pick(&[1e-4, 1e-2, 0.1]))))
    } else if r < 0.94 {
        scale(n, -1.0)
    } else {
        rng.unit_or_axis(0.5)
    }
}

fn triangle(rng: &mut Rng, s: f32) -> Triangle {
    let pt = tri_shape(rng, s);
    let n = tri_normal(rng, &pt);
    Triangle { pt, n }
}

/// A point on the triangle: a vertex, on an edge, or inside.
fn on_tri(rng: &mut Rng, t: &Triangle) -> Vec3 {
    match rng.below(4) {
        0 => t.pt[rng.below(3) as usize],
        1 => {
            let i = rng.below(3) as usize;
            let j = (i + 1) % 3;
            add(t.pt[i], scale(sub(t.pt[j], t.pt[i]), rng.f(-0.1, 1.1)))
        }
        _ => {
            let mut u = rng.f(0.0, 1.0);
            let mut v = rng.f(0.0, 1.0);
            if u + v > 1.0 {
                u = 1.0 - u;
                v = 1.0 - v;
            }
            add(
                t.pt[0],
                add(scale(sub(t.pt[1], t.pt[0]), u), scale(sub(t.pt[2], t.pt[0]), v)),
            )
        }
    }
}

/// Linear unprojection params: dir 0, unit (sometimes axis or ±hint), or non-unit.
fn lin_params(rng: &mut Rng, hint: Vec3) -> UnprojParams {
    let mut p = UnprojParams::default();
    match rng.below(6) {
        0 => {}
        1 | 2 => p.dir = rng.unit_or_axis(0.3),
        3 => p.dir = hint,
        4 => p.dir = scale(hint, -1.0),
        _ => p.dir = scale(rng.unit(), rng.f(0.1, 5.0)),
    }
    p.tmax = rng.f(0.0, 10.0);
    p.tmin = if rng.chance(0.7) {
        0.0
    } else {
        rng.f(-1.0, 1.0)
    };
    p.min_pt_dist = rng.pick(&[0.001, 0.01, 0.05]);
    p.max_cos = rng.pick(&[0.1, 0.9998]);
    p
}

fn maybe_area(rng: &mut Rng, c: Case) -> Case {
    if rng.chance(0.3) {
        let n = rng.pick(&[1u32, 2, 3, 4, 256]);
        c.with_area(n, 0.01)
    } else {
        c
    }
}

fn maybe_feat(rng: &mut Rng, c: Case) -> Case {
    if rng.chance(0.3) {
        let fa = rng.pick(&[-1, 0x40, 0x80, 0x81, 0xA0, 0xA2]);
        let fb = rng.pick(&[-1, 0x40, 0x41, 0x80, 0x20, 0xA1]);
        c.with_features(fa, fb)
    } else {
        c
    }
}

fn tri_sph_parts(rng: &mut Rng) -> (Triangle, SpherePrim, UnprojParams) {
    let sz = rng.f(0.5, 4.0);
    let t = triangle(rng, sz);
    let r = rng.f(0.05, 2.0);
    let base = on_tri(rng, &t);
    let off = if rng.chance(0.6) {
        scale(t.n, rng.f(-1.5, 1.5) * r)
    } else {
        scale(rng.unit(), rng.f(0.0, 2.0) * r)
    };
    let c = add(base, off);
    let p = lin_params(rng, t.n);
    (t, SpherePrim { center: c, r }, p)
}

fn gen_tri_sph(rng: &mut Rng) -> Case {
    let (t, s, p) = tri_sph_parts(rng);
    let c = Case::new(0, &Primitive::Triangle(t), &Primitive::Sphere(s), &p);
    let c = maybe_feat(rng, c);
    maybe_area(rng, c)
}

fn gen_sph_tri(rng: &mut Rng) -> Case {
    let (t, s, mut p) = tri_sph_parts(rng);
    p.dir = scale(p.dir, -1.0);
    let c = Case::new(0, &Primitive::Sphere(s), &Primitive::Triangle(t), &p);
    let c = maybe_feat(rng, c);
    maybe_area(rng, c)
}

#[test]
fn tri_sph_linear_matches_dll() {
    check("p2b_tri_sph", 300, &gen_tri_sph);
}

#[test]
fn sph_tri_linear_matches_dll() {
    check("p2b_tri_sph_tri", 200, &gen_sph_tri);
}

fn tri_ray_parts(rng: &mut Rng) -> (Triangle, RayPrim, UnprojParams) {
    let sz = rng.f(0.5, 4.0);
    let t = triangle(rng, sz);
    let hit = on_tri(rng, &t);
    let u = match rng.below(5) {
        0 => scale(t.n, if rng.chance(0.5) { 1.0 } else { -1.0 }),
        1 => {
            // parallel to the plane
            let e = sub(t.pt[1], t.pt[0]);
            norm(add(e, scale(cross(t.n, e), rng.f(-1.0, 1.0))))
        }
        _ => rng.unit_or_axis(0.3),
    };
    let l0 = rng.f(0.1, 6.0);
    let len = rng.pick(&[l0, 1.0, sz]);
    let dv = scale(u, len);
    let k0 = rng.f(-0.3, 1.3);
    let k = rng.pick(&[k0, 0.0, 1.0, 0.5]);
    let mut o = sub(hit, scale(dv, k));
    if rng.chance(0.15) {
        o = add(o, scale(t.n, rng.f(-0.2, 0.2)));
    }
    let mut p = lin_params(rng, t.n);
    if rng.chance(0.25) {
        // the mesh ray path pushes sideways: n × ray dir
        p.dir = norm(cross(t.n, u));
    }
    (t, RayPrim { origin: o, dir: dv }, p)
}

fn gen_tri_ray(rng: &mut Rng) -> Case {
    let (t, r, p) = tri_ray_parts(rng);
    let c = Case::new(0, &Primitive::Triangle(t), &Primitive::Ray(r), &p);
    let c = maybe_feat(rng, c);
    maybe_area(rng, c)
}

fn gen_ray_tri(rng: &mut Rng) -> Case {
    let (t, r, mut p) = tri_ray_parts(rng);
    p.dir = scale(p.dir, -1.0);
    let c = Case::new(0, &Primitive::Ray(r), &Primitive::Triangle(t), &p);
    let c = maybe_feat(rng, c);
    maybe_area(rng, c)
}

#[test]
fn tri_ray_linear_matches_dll() {
    check("p2b_tri_ray", 300, &gen_tri_ray);
}

#[test]
fn ray_tri_linear_matches_dll() {
    check("p2b_tri_ray_tri", 200, &gen_ray_tri);
}

fn gen_tri_tri(rng: &mut Rng) -> Case {
    let sa = rng.f(0.5, 4.0);
    let a = triangle(rng, sa);
    let sb = rng.f(0.3, 4.0);
    let mut b = triangle(rng, sb);
    let mode = rng.below(4);
    if mode == 3 {
        // coplanar / parallel copy of A, shifted in-plane and along n
        let sh = scale(rng.unit(), rng.f(0.0, sa));
        let up = scale(a.n, rng.pick(&[0.0, 0.0, 0.01, -0.05]));
        let off = add(sh, up);
        b = Triangle {
            pt: [add(a.pt[0], off), add(a.pt[2], off), add(a.pt[1], off)],
            n: if rng.chance(0.5) { scale(a.n, -1.0) } else { a.n },
        };
    } else {
        // move B so that one of its points sits near a point of A
        let pa = on_tri(rng, &a);
        let pb = on_tri(rng, &b);
        let k = rng.f(-0.5, 0.5) * sb;
        let off = add(sub(pa, pb), scale(a.n, k));
        for q in &mut b.pt {
            *q = add(*q, off);
        }
    }
    let p = lin_params(rng, a.n);
    let c = Case::new(0, &Primitive::Triangle(a), &Primitive::Triangle(b), &p);
    let c = maybe_feat(rng, c);
    maybe_area(rng, c)
}

#[test]
fn tri_tri_linear_matches_dll() {
    check("p2b_tri_tri", 300, &gen_tri_tri);
}
