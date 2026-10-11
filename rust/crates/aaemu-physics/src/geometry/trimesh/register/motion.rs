//! sub_351C4360 (+ sub_3514D500): moves the registering side's record by the accumulated
//! unprojection `t` from the transform saved at the top of the loop. Mode 0:
//! `offset = dir·(float)t + saved`; mode 1: `R = rot(axis, (float)cos t, (float)sin t) · R_saved`
//! (sub_35020320) and `offset = rot·(saved − c) + c`. Both set `+160` (re-transform primitives).

use super::MotionBase;
use crate::collision::contact::UnprojParams;
use crate::collision::swept::poly::crt::{cos, sin};
use crate::geometry::shapes::protocol::GeomUnderTest;
use crate::geometry::trimesh::intersect::mat_mul_20320;
use crate::math::{Mat33, Vec3};

/// sub_3514D500: the rotation about the unit `a` with `c = cos`, `s = sin`.
#[must_use]
pub fn rot_matrix(c: f32, s: f32, a: Vec3) -> Mat33 {
    let k = 1.0 - c;
    Mat33 {
        m: [
            [
                (a.x * k) * a.x + c,
                (k * a.x) * a.y - s * a.z,
                (k * a.x) * a.z + s * a.y,
            ],
            [
                (k * a.y) * a.x + s * a.z,
                (a.y * k) * a.y + c,
                (k * a.y) * a.z - s * a.x,
            ],
            [
                (k * a.z) * a.x - s * a.y,
                (k * a.z) * a.y + s * a.x,
                (a.z * k) * a.z + c,
            ],
        ],
    }
}

/// sub_351C4360.
pub fn apply(t: f64, p: &UnprojParams, saved: &MotionBase, gt: &mut GeomUnderTest) {
    if p.mode != 0 {
        let s = sin(t) as f32;
        let c = cos(t) as f32;
        let r = rot_matrix(c, s, p.dir);
        gt.r = mat_mul_20320(&r, &saved.r);
        let o = saved.offset;
        let ce = p.center;
        let d = Vec3::new(o.x - ce.x, o.y - ce.y, o.z - ce.z);
        let m = &r.m;
        gt.offset = Vec3::new(
            ((d.y * m[0][1] + d.x * m[0][0]) + d.z * m[0][2]) + ce.x,
            ((d.y * m[1][1] + d.x * m[1][0]) + d.z * m[1][2]) + ce.y,
            ((d.y * m[2][1] + d.x * m[2][0]) + d.z * m[2][2]) + ce.z,
        );
    } else {
        let tf = t as f32;
        let o = saved.offset;
        gt.offset = Vec3::new(p.dir.x * tf + o.x, p.dir.y * tf + o.y, p.dir.z * tf + o.z);
    }
    gt.transform_prims = 1;
}
