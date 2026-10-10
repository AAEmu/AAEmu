//! P2b: the capsule cells (compositions of the cylinder and sphere cells) against the DLL.

use aaemu_physics::collision::contact::UnprojParams;
use aaemu_physics::collision::swept::probe::{check, check_masked, mask_cos, Case, Rng};
use aaemu_physics::geometry::primitives::{
    BoxPrim, CylinderPrim, Primitive, RayPrim, SpherePrim, Triangle,
};
use aaemu_physics::math::{Mat33, Vec3};

const BOX: i32 = 0;
const TRI: i32 = 1;
const RAY: i32 = 3;
const SPH: i32 = 4;
const CYL: i32 = 5;
const CAP: i32 = 6;

fn cylinder(rng: &mut Rng, c: Vec3) -> CylinderPrim {
    let axis = rng.unit_or_axis(0.4);
    let r = rng.f(0.05, 1.5);
    let hh = if rng.chance(0.1) {
        rng.f(0.0, 0.05)
    } else {
        rng.f(0.05, 2.0)
    };
    CylinderPrim {
        center: c,
        axis,
        r,
        hh,
    }
}

/// A primitive of type `t` around `c`.
fn prim(rng: &mut Rng, t: i32, c: Vec3) -> Primitive {
    match t {
        BOX => {
            let oriented = rng.chance(0.5);
            Primitive::Box(BoxPrim {
                basis: if oriented {
                    rng.rotation()
                } else {
                    Mat33::IDENTITY
                },
                oriented: i32::from(oriented),
                center: c,
                size: Vec3::new(rng.f(0.05, 1.5), rng.f(0.05, 1.5), rng.f(0.05, 1.5)),
            })
        }
        TRI => {
            let p = [
                Vec3::new(
                    c.x + rng.f(-1.5, 1.5),
                    c.y + rng.f(-1.5, 1.5),
                    c.z + rng.f(-0.3, 0.3),
                ),
                Vec3::new(
                    c.x + rng.f(-1.5, 1.5),
                    c.y + rng.f(-1.5, 1.5),
                    c.z + rng.f(-0.3, 0.3),
                ),
                Vec3::new(
                    c.x + rng.f(-1.5, 1.5),
                    c.y + rng.f(-1.5, 1.5),
                    c.z + rng.f(-0.3, 0.3),
                ),
            ];
            let e1 = Vec3::new(p[1].x - p[0].x, p[1].y - p[0].y, p[1].z - p[0].z);
            let e2 = Vec3::new(p[2].x - p[0].x, p[2].y - p[0].y, p[2].z - p[0].z);
            let n = e1.cross(e2);
            let l = n.len();
            let n = if l > 0.0 { n * (1.0 / l) } else { Vec3::Z };
            Primitive::Triangle(Triangle { pt: p, n })
        }
        RAY => {
            let d = rng.unit_or_axis(0.3) * rng.f(0.0, 3.0);
            Primitive::Ray(RayPrim {
                origin: Vec3::new(c.x - d.x * 0.5, c.y - d.y * 0.5, c.z - d.z * 0.5),
                dir: d,
            })
        }
        SPH => Primitive::Sphere(SpherePrim {
            center: c,
            r: rng.f(0.05, 1.5),
        }),
        CYL => Primitive::Cylinder(cylinder(rng, c)),
        CAP => Primitive::Capsule(cylinder(rng, c)),
        _ => unreachable!(),
    }
}

fn gen_pair(rng: &mut Rng, ta: i32, tb: i32) -> Case {
    let ca = rng.vec(-10.0, 10.0);
    let off = rng.unit_or_axis(0.3) * rng.f(0.0, 2.5);
    let cb = Vec3::new(ca.x + off.x, ca.y + off.y, ca.z + off.z);
    let a = prim(rng, ta, ca);
    let b = prim(rng, tb, cb);
    let mut p = UnprojParams::default();
    match rng.below(4) {
        0 => {}
        1 => p.dir = rng.unit_or_axis(0.3),
        2 => p.dir = Vec3::new(-off.x, -off.y, -off.z),
        _ => p.dir = rng.unit() * rng.f(0.2, 4.0),
    }
    p.tmax = rng.f(0.0, 20.0);
    p.tmin = if rng.chance(0.7) {
        0.0
    } else {
        rng.f(-0.5, 0.5)
    };
    p.min_pt_dist = rng.pick(&[0.0f32, 0.001, 0.01]);
    p.max_cos = rng.pick(&[0.1f32, 0.99985]);
    let c = Case::new(0, &a, &b, &p);
    match rng.below(4) {
        0 => c.with_area(256, 0.0),
        1 => c.with_area(rng.pick(&[1u32, 2, 4]), 0.01),
        _ => c,
    }
}

#[test]
fn sph_cap_matches_dll() {
    // sph–cap copies `cos` from an uninitialised temporary (see capsule.rs)
    check_masked(
        "p2b_capsule_sph_cap",
        250,
        &|r| gen_pair(r, SPH, CAP),
        &mask_cos,
    );
}

#[test]
fn cap_sph_matches_dll() {
    check_masked(
        "p2b_capsule_cap_sph",
        200,
        &|r| gen_pair(r, CAP, SPH),
        &mask_cos,
    );
}

#[test]
fn ray_cap_matches_dll() {
    check("p2b_capsule_ray_cap", 250, &|r| gen_pair(r, RAY, CAP));
}

#[test]
fn cap_ray_matches_dll() {
    check("p2b_capsule_cap_ray", 200, &|r| gen_pair(r, CAP, RAY));
}
