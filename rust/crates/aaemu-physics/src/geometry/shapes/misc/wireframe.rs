//! Slot 15 `DrawWireframe(IPhysRenderer*, gwd, iLevel, idxColor)`.
//!
//! * `CGeometry` base (0x350377B0, used by the ray): always
//!   `pRenderer->DrawGeometry(this, gwd, idxColor, 0, Vec3(0))` (renderer slot 1).
//! * box (0x3513A440), sphere (0x351AEFF0), cylinder and capsule (0x35141E70): the same call
//!   for `iLevel == 0`; for any other level the embedded `CSingleBoxTree` is placed in the world
//!   (tree slot 9, 0x351AE3F0) and `sub_35151B90` draws its box as 12 lines (renderer slot 2,
//!   `DrawLine(p0, p1, idxColor, 0)`). The recursion of `sub_35151B90` only splits nodes whose
//!   split priority (tree slot 12) is positive, which is never the case for a single box, so
//!   every level ≥ 1 (and negative levels) draws exactly the one box.
//!
//! The renderer is the [`PhysRenderer`] trait (the two slots the primitives call).

use crate::geometry::primitives::BoxPrim;
use crate::geometry::shapes::gwd_point;
use crate::geometry::{GeomWorldData, Geometry};
use crate::math::Vec3;

/// The part of `IPhysRenderer` the geometry code calls.
pub trait PhysRenderer {
    /// Slot 1 (`+8`) `DrawGeometry(pGeom, gwd, idxColor, bSlowFadein, sweepDir)`.
    fn draw_geometry(
        &mut self,
        geom: &dyn Geometry,
        gwd: &GeomWorldData,
        idx_color: i32,
        slow_fadein: i32,
        sweep_dir: Vec3,
    );
    /// Slot 2 (`+16`) `DrawLine(pt0, pt1, idxColor, bSlowFadein)`.
    fn draw_line(&mut self, pt0: Vec3, pt1: Vec3, idx_color: i32, slow_fadein: i32);
}

/// `CSingleBoxTree` slot 9 (0x351AE3F0): the tree box in the world — basis
/// `Basis·Rᵀ` (`sub_35022380`, applied whatever `bOriented` says), centre
/// `(R·center)·s + offset`, size `size·s`; the BV record is flagged oriented.
#[must_use]
pub fn single_box_world(b: &BoxPrim, gwd: &GeomWorldData) -> BoxPrim {
    let s = gwd.scale;
    BoxPrim {
        basis: b.basis.mul_mat(&gwd.r.transpose()),
        oriented: 1,
        center: gwd_point(gwd, b.center),
        size: Vec3::new(b.size.x * s, b.size.y * s, b.size.z * s),
    }
}

/// `sub_35151B90` at a leaf: corner `k` is `center + Mᵀ·(±sx, ±sy, ±sz)` (bit 0/1/2 of `k`
/// set = `+`), then for `k = 0..7` and `bit = 1, 2, 4` with `k & bit`, a line from corner `k`
/// to corner `k ^ bit`.
pub fn draw_box_lines(b: &BoxPrim, r: &mut dyn PhysRenderer, idx_color: i32) {
    let mut c = [Vec3::ZERO; 8];
    for (k, p) in c.iter_mut().enumerate() {
        let k = k as i32;
        let s = Vec3::new(
            (2 * (k & 1) - 1) as f32 * b.size.x,
            (2 * ((k >> 1) & 1) - 1) as f32 * b.size.y,
            (2 * ((k >> 2) & 1) - 1) as f32 * b.size.z,
        );
        let t = b.basis.t_mul_vec(s);
        *p = Vec3::new(b.center.x + t.x, b.center.y + t.y, b.center.z + t.z);
    }
    for k in 0..8usize {
        for bit in [1usize, 2, 4] {
            if k & bit != 0 {
                r.draw_line(c[k], c[k ^ bit], idx_color, 0);
            }
        }
    }
}

/// `CGeometry` slot 15 (0x350377B0): `DrawGeometry(this, gwd, idxColor, 0, 0)`.
pub fn base_draw_wireframe(
    g: &dyn Geometry,
    r: &mut dyn PhysRenderer,
    gwd: &GeomWorldData,
    idx_color: i32,
) {
    r.draw_geometry(g, gwd, idx_color, 0, Vec3::ZERO);
}

/// Slot 15 of the box (0x3513A440), sphere (0x351AEFF0) and cylinder/capsule (0x35141E70):
/// `tree_box` is the geometry's embedded `CSingleBoxTree` box (`bv.box_`).
pub fn primitive_draw_wireframe(
    g: &dyn Geometry,
    tree_box: &BoxPrim,
    r: &mut dyn PhysRenderer,
    gwd: &GeomWorldData,
    level: i32,
    idx_color: i32,
) {
    if level == 0 {
        base_draw_wireframe(g, r, gwd, idx_color);
    } else {
        draw_box_lines(&single_box_world(tree_box, gwd), r, idx_color);
    }
}
