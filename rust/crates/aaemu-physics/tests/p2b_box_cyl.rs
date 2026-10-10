//! P2b: box–cylinder linear unprojection (0x351801A0) and its cyl–box swap wrapper (0x35185AA0)
//! against the DLL (`physoracle --p2b`).

use aaemu_physics::collision::contact::UnprojParams;
use aaemu_physics::collision::swept::probe::{check, Case, Rng};
use aaemu_physics::geometry::primitives::{BoxPrim, CylinderPrim, Primitive};
use aaemu_physics::math::{Mat33, Vec3};

fn add(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(a.x + b.x, a.y + b.y, a.z + b.z)
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
    if l > 1e-6 {
        scale(a, 1.0 / l)
    } else {
        Vec3::new(0.0, 0.0, 1.0)
    }
}

/// A signed axis permutation (exact 90° rotation).
fn perm_basis(rng: &mut Rng) -> Mat33 {
    let perms = [
        [0, 1, 2],
        [1, 2, 0],
        [2, 0, 1],
        [0, 2, 1],
        [2, 1, 0],
        [1, 0, 2],
    ];
    let p = perms[rng.below(6) as usize];
    let mut m = [[0.0f32; 3]; 3];
    for (row, &col) in p.iter().enumerate() {
        m[row][col] = if rng.chance(0.5) { 1.0 } else { -1.0 };
    }
    Mat33 { m }
}

fn row(m: &Mat33, i: usize) -> Vec3 {
    Vec3::new(m.m[i][0], m.m[i][1], m.m[i][2])
}

fn size(rng: &mut Rng) -> f32 {
    match rng.below(10) {
        0 => rng.f(0.001, 0.05),
        1 => rng.quant(0.25, 2.0, 0.25),
        _ => rng.f(0.05, 3.0),
    }
}

/// A box and a cylinder in a configuration chosen to hit the different branches.
fn scene(rng: &mut Rng) -> (BoxPrim, CylinderPrim, UnprojParams, i32) {
    let (basis, oriented) = match rng.below(4) {
        0 => (Mat33::IDENTITY, 0),
        1 => (perm_basis(rng), 1),
        _ => (rng.rotation(), 1),
    };
    let center = if rng.chance(0.3) {
        Vec3::new(
            rng.quant(-5.0, 5.0, 0.5),
            rng.quant(-5.0, 5.0, 0.5),
            rng.quant(-5.0, 5.0, 0.5),
        )
    } else {
        rng.vec(-20.0, 20.0)
    };
    let half = Vec3::new(size(rng), size(rng), size(rng));
    let bx = BoxPrim {
        basis,
        oriented,
        center,
        size: half,
    };
    // cylinder axis: parallel / perpendicular to a box axis, or random
    let axis = match rng.below(6) {
        0 | 1 => scale(
            row(&basis, rng.below(3) as usize),
            if rng.chance(0.5) { 1.0 } else { -1.0 },
        ),
        2 => {
            // perpendicular to box axis k
            let k = rng.below(3) as usize;
            let u = row(&basis, (k + 1) % 3);
            let v = row(&basis, (k + 2) % 3);
            let t = rng.f(0.0, std::f32::consts::TAU);
            if rng.chance(0.4) {
                u
            } else {
                norm(add(scale(u, t.cos()), scale(v, t.sin())))
            }
        }
        3 => {
            // nearly parallel
            let k = rng.below(3) as usize;
            let e = rng.unit();
            norm(add(row(&basis, k), scale(e, rng.f(0.0, 0.08))))
        }
        _ => rng.unit_or_axis(0.3),
    };
    let r = match rng.below(12) {
        0 => rng.f(0.001, 0.02),
        1 => rng.quant(0.25, 1.5, 0.25),
        _ => rng.f(0.05, 2.0),
    };
    let hh = match rng.below(12) {
        0 => rng.f(0.001, 0.02),
        1 => rng.quant(0.25, 2.0, 0.25),
        _ => rng.f(0.05, 3.0),
    };
    // place the cylinder centre relative to the box: inside, near a face/edge/vertex, or apart
    let local = match rng.below(5) {
        0 => Vec3::new(
            rng.f(-1.0, 1.0) * half.x,
            rng.f(-1.0, 1.0) * half.y,
            rng.f(-1.0, 1.0) * half.z,
        ),
        1 => {
            // beyond a face by about r or hh
            let k = rng.below(3) as usize;
            let mut q = [
                rng.f(-1.0, 1.0) * half.x,
                rng.f(-1.0, 1.0) * half.y,
                rng.f(-1.0, 1.0) * half.z,
            ];
            let h = [half.x, half.y, half.z][k];
            let reach = if rng.chance(0.5) { r } else { hh };
            q[k] = (h + reach * rng.f(0.0, 1.5)) * if rng.chance(0.5) { 1.0 } else { -1.0 };
            Vec3::new(q[0], q[1], q[2])
        }
        2 => {
            // near a vertex
            let sx = if rng.chance(0.5) { 1.0 } else { -1.0 };
            let sy = if rng.chance(0.5) { 1.0 } else { -1.0 };
            let sz = if rng.chance(0.5) { 1.0 } else { -1.0 };
            add(
                Vec3::new(sx * half.x, sy * half.y, sz * half.z),
                scale(rng.unit(), (r + hh) * rng.f(0.0, 1.2)),
            )
        }
        3 => {
            // near an edge
            let k = rng.below(3) as usize;
            let mut q = [
                if rng.chance(0.5) { half.x } else { -half.x },
                if rng.chance(0.5) { half.y } else { -half.y },
                if rng.chance(0.5) { half.z } else { -half.z },
            ];
            q[k] *= rng.f(-1.0, 1.0);
            add(
                Vec3::new(q[0], q[1], q[2]),
                scale(rng.unit(), (r + hh) * rng.f(0.0, 1.0)),
            )
        }
        _ => scale(
            rng.unit(),
            rng.f(0.0, 2.0) * (half.x + half.y + half.z + r + hh),
        ),
    };
    let world = add(
        center,
        add(
            add(
                scale(row(&basis, 0), local.x),
                scale(row(&basis, 1), local.y),
            ),
            scale(row(&basis, 2), local.z),
        ),
    );
    let cyl = CylinderPrim {
        center: world,
        axis,
        r,
        hh,
    };
    let mut p = UnprojParams::default();
    match rng.below(6) {
        0 | 1 => {} // least-penetration direction
        2 => p.dir = rng.unit_or_axis(0.3),
        3 => {
            // along a box axis or the cylinder axis (exactly parallel features)
            let v = if rng.chance(0.5) {
                row(&basis, rng.below(3) as usize)
            } else {
                axis
            };
            p.dir = scale(v, if rng.chance(0.5) { 1.0 } else { -1.0 });
        }
        4 => {
            // perpendicular to the axis
            p.dir = norm(cross(axis, rng.unit()));
        }
        _ => {
            let u = rng.unit();
            let s = rng.f(0.1, 5.0);
            p.dir = scale(u, s);
        }
    }
    if rng.chance(0.03) {
        p.dir = Vec3::new(-0.0, 0.0, -0.0);
    }
    p.tmax = match rng.below(4) {
        0 => 1e10,
        1 => rng.f(0.0, 0.5),
        _ => rng.f(0.0, 10.0),
    };
    p.tmin = if rng.chance(0.8) {
        0.0
    } else {
        rng.f(-1.0, 1.0)
    };
    p.min_pt_dist = rng.pick(&[0.001, 0.01, 0.05, 0.02]);
    p.max_cos = match rng.below(4) {
        0 => 0.1,
        1 => 0.999_847_7,
        2 => 0.9998,
        _ => rng.f(0.0, 1.0),
    };
    p.flag = match rng.below(10) {
        0..=4 => 1,
        5..=8 => 0,
        _ => rng.pick(&[2, 3, -1]),
    };
    let fb = match rng.below(8) {
        0 | 1 => 0x43,
        2 => rng.pick(&[0x41, 0x42, 0x40, 0x20, 0x80000043u32 as i32]),
        _ => -1,
    };
    (bx, cyl, p, fb)
}

fn with_area(rng: &mut Rng, c: Case) -> Case {
    match rng.below(5) {
        0 | 1 => c,
        2 | 3 => c.with_area(256, 0.01),
        _ => c.with_area(1 + rng.below(4), 0.01),
    }
}

fn gen_box_cyl(rng: &mut Rng) -> Case {
    let (bx, cyl, p, fb) = scene(rng);
    let c = Case::new(0, &Primitive::Box(bx), &Primitive::Cylinder(cyl), &p).with_features(-1, fb);
    with_area(rng, c)
}

fn gen_cyl_box(rng: &mut Rng) -> Case {
    let (bx, cyl, p, fb) = scene(rng);
    let c = Case::new(0, &Primitive::Cylinder(cyl), &Primitive::Box(bx), &p).with_features(fb, -1);
    with_area(rng, c)
}

#[test]
fn box_cyl_linear_matches_dll() {
    check("p2b_box_cyl_box_cyl", 300, &gen_box_cyl);
}

#[test]
fn cyl_box_wrapper_matches_dll() {
    check("p2b_box_cyl_cyl_box", 200, &gen_cyl_box);
}
