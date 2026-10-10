//! `CHeightfield` slot 51 (0x35156430): rasterise the cells within `rmax` of the cubemap centre
//! (the world origin) into the occlusion cubemap (the leaf worker of slot 23
//! `DrawToOcclusionCubemap`; see P1a's [`crate::geometry::shapes::misc::cubemap`] for the
//! cubemap and the polygon rasteriser sub_35131AB0).

use crate::collision::pairs::tri_pairs::overlap_tri_sph;
use crate::geometry::primitives::{SpherePrim, Triangle};
use crate::geometry::shapes::misc::cubemap::{rasterize_polygon, Cubemap};
use crate::geometry::GeomWorldData;
use crate::math::Vec3;

use super::region::cross_ba;
use super::{height, is_hole, surf_type, Heightfield};

impl Heightfield {
    /// Slot 51 (0x35156430) `(gwd, iStartPrim, nPrims, iPass, cubemap, res, rmin, rmax, zscale)`;
    /// the prim range is ignored. Returns the number of triangles rasterised.
    ///
    /// 1. The origin in the heightfield frame: `k = 1/s` (exactly 1 when `s == 1` or NaN),
    ///    `o = −off·k`, `c.x = (o.y·R10 + o.x·R00) + o.z·R20`, `c.y` likewise with column 1;
    ///    `r = k·rmax`.
    /// 2. Cells `ix ∈ [clamp(⌊(c.x − r)·stepr.x⌉), clamp(round((c.x + r)·stepr.x + 0.5)))` (both
    ///    clamped to `[0, nx]`; the lower bound by `round(v − 0.5)`), same for `iy` (x outer).
    /// 3. Hole cells are skipped. Triangle 1 = the cell corners `(x0,y0,h00) (x1,y0,h10)
    ///    (x0,y1,h01)` in world space, each `((R·(x, y, h))·s + off` with the row sums
    ///    `(R[k][0]·x + R[k][1]·y) + h·R[k][2]` (`x1 = step.x + x0`, `y1 = step.y + y0`);
    ///    triangle 2 = `(p2, p1, p2 + R·(s·step.x, 0, 0) + R·(0, 0, (h11 − h01)·s))` with the
    ///    binary's association (`(p2 + A) + B`, `A_k = (R[k][0]·s·sx + R[k][1]·0) + R[k][2]·0`,
    ///    `B_k = (R[k][0]·0 + R[k][1]·0) + dh·R[k][2]`). Normal `(p1 − p0) × (p2 − p0)`.
    /// 4. A triangle is drawn ([`rasterize_polygon`] with 3 points) when OVERLAP tri–sph
    ///    (0x35197530) says it touches the sphere `{0, rmax}`.
    #[allow(clippy::too_many_arguments)]
    pub fn draw_to_cubemap(
        &self,
        gwd: &GeomWorldData,
        pass: u32,
        cube: &mut Cubemap,
        rmin: f32,
        rmax: f32,
        zscale: f32,
    ) -> i32 {
        let p = &self.bv.prim;
        let s = gwd.scale;
        let k = if s == 1.0 || s.is_nan() { 1.0 } else { 1.0 / s };
        let r = &gwd.r.m;
        let ox = -gwd.offset.x * k;
        let oy = -gwd.offset.y * k;
        let oz = -gwd.offset.z * k;
        let cx = (oy * r[1][0] + ox * r[0][0]) + oz * r[2][0];
        let cy = (oy * r[1][1] + ox * r[0][1]) + oz * r[2][1];
        let rr = k * rmax;
        let magic = 12_582_912.0f32;
        let rb = |v: f32| (v.to_bits() as i32).wrapping_sub(0x4B40_0000);
        // clamp(v, 0, n) as the binary computes it: min with n, then max with 0.
        let clamp = |v: i32, n: i32| {
            let d = n.wrapping_sub(v);
            let m = (d & (d >> 31)).wrapping_add(v).wrapping_neg();
            ((m >> 31) & m).wrapping_neg()
        };
        let (nx, ny) = (p.size[0], p.size[1]);
        let ix0 = clamp(rb(((cx - rr) * p.stepr[0] - 0.5) + magic), nx);
        let iy0 = clamp(rb(((cy - rr) * p.stepr[1] - 0.5) + magic), ny);
        let ix1 = clamp(rb(((cx + rr) * p.stepr[0] + 0.5) + magic), nx);
        let iy1 = clamp(rb(((cy + rr) * p.stepr[1] + 0.5) + magic), ny);
        let sph = SpherePrim {
            center: Vec3::ZERO,
            r: rmax,
        };
        let hs = p.height_scale;
        let off = gwd.offset;
        let mut n = 0;
        let mut ix = ix0;
        while ix < ix1 {
            let mut iy = iy0;
            while iy < iy1 {
                if is_hole(p, surf_type(p, ix, iy)) {
                    iy += 1;
                    continue;
                }
                let h00 = height(p, ix, iy) * hs;
                let h10 = height(p, ix + 1, iy) * hs;
                let h01 = height(p, ix, iy + 1) * hs;
                let h11 = height(p, ix + 1, iy + 1) * hs;
                let y0 = iy as f32 * p.step[1];
                let y1 = p.step[1] + y0;
                let x0 = ix as f32 * p.step[0];
                let x1 = p.step[0] + x0;
                let place = |x: f32, y: f32, h: f32| {
                    Vec3::new(
                        ((r[0][0] * x + r[0][1] * y) + h * r[0][2]) * s + off.x,
                        ((r[1][0] * x + r[1][1] * y) + h * r[1][2]) * s + off.y,
                        ((r[2][0] * x + r[2][1] * y) + h * r[2][2]) * s + off.z,
                    )
                };
                let p0 = place(x0, y0, h00);
                let p1 = place(x1, y0, h10);
                let p2 = place(x0, y1, h01);
                let t1 = Triangle {
                    pt: [p0, p1, p2],
                    n: cross_ba(p0, p1, p2),
                };
                if overlap_tri_sph(&t1, &sph) {
                    rasterize_polygon(&t1.pt, pass, cube, rmin, rmax, zscale);
                    n += 1;
                }
                let ssx = s * p.step[0];
                let dh = (h11 - h01) * s;
                let a = |k: usize| (r[k][0] * ssx + r[k][1] * 0.0) + r[k][2] * 0.0;
                let b = |k: usize| (r[k][0] * 0.0 + r[k][1] * 0.0) + dh * r[k][2];
                let p3 = Vec3::new(
                    (p2.x + a(0)) + b(0),
                    (p2.y + a(1)) + b(1),
                    (p2.z + a(2)) + b(2),
                );
                let t2 = Triangle {
                    pt: [p2, p1, p3],
                    n: cross_ba(p2, p1, p3),
                };
                if overlap_tri_sph(&t2, &sph) {
                    rasterize_polygon(&t2.pt, pass, cube, rmin, rmax, zscale);
                    n += 1;
                }
                iy += 1;
            }
            ix += 1;
        }
        n
    }
}
