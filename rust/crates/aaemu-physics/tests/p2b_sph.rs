//! P2b: sph–sph linear unprojection (0x35178110) against the DLL (`physoracle --p2b`).

use aaemu_physics::collision::contact::UnprojParams;
use aaemu_physics::collision::swept::probe::{check, Case, Rng};
use aaemu_physics::geometry::primitives::{Primitive, SpherePrim};
use aaemu_physics::math::Vec3;

fn gen(rng: &mut Rng) -> Case {
    let ra = rng.f(0.05, 3.0);
    let rb = if rng.chance(0.2) {
        ra
    } else {
        rng.f(0.05, 3.0)
    };
    let ca = rng.vec(-20.0, 20.0);
    let reach = (ra + rb) * rng.f(0.0, 1.6);
    let d = rng.unit_or_axis(0.3);
    let cb = if rng.chance(0.05) {
        ca
    } else {
        Vec3::new(ca.x + d.x * reach, ca.y + d.y * reach, ca.z + d.z * reach)
    };
    let mut p = UnprojParams::default();
    match rng.below(4) {
        0 => {} // least-penetration direction
        1 => p.dir = rng.unit_or_axis(0.3),
        2 => p.dir = Vec3::new(-d.x, -d.y, -d.z),
        _ => {
            let u = rng.unit();
            let s = rng.f(0.1, 5.0);
            p.dir = Vec3::new(u.x * s, u.y * s, u.z * s);
        }
    }
    p.tmax = rng.f(0.0, 10.0);
    p.tmin = if rng.chance(0.7) {
        0.0
    } else {
        rng.f(-1.0, 1.0)
    };
    p.max_cos = 0.1;
    let a = Primitive::Sphere(SpherePrim { center: ca, r: ra });
    let b = Primitive::Sphere(SpherePrim { center: cb, r: rb });
    let c = Case::new(0, &a, &b, &p);
    if rng.chance(0.2) {
        c.with_area(16, 0.01)
    } else {
        c
    }
}

#[test]
fn sph_sph_linear_matches_dll() {
    check("p2b_sph_sph", 300, &gen);
}
