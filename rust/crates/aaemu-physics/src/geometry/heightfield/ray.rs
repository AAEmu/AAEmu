//! The ray branch of `CHeightfield::Intersect` (slot 10, 0x35156E20): the ray is brought into
//! the heightfield frame, clipped to the grid rectangle and marched cell by cell
//! (sub_35158BA0); each cell (sub_35158080) is culled by its maximum height and holes, then its
//! two triangles are tested with INTERSECT ray–tri (0x3515AB20). The first hit becomes one
//! `geom_contact`.
//!
//! The march's DrawRayOnGrid limit (ArcheAge): unless `gEnv` byte `+289` is set, a ray whose
//! local `|dir.x|` or `|dir.y|` exceeds the integer config value `gEnv+112 → slot 20 → +248` is
//! rejected (and logged). The port takes the limit as a parameter (`None` = byte 289 set, no
//! limit; [`DEFAULT_DRAW_RAY_LIMIT`] = the oracle's 1000).

// Negated float comparisons mirror the binary's NaN behaviour (comiss + jb/jae/ja).
#![allow(clippy::neg_cmp_op_on_partial_ord)]

use crate::collision::contact::{
    GeomContact, IntersectionParams, PrimInters, UnprojParams, UnprojResult,
};
use crate::collision::pairs::tri_pairs::intersect_ray_tri;
use crate::collision::swept::UnprojTables;
use crate::geometry::primitives::{HeightfieldPrim, Primitive, RayPrim, Triangle};
use crate::geometry::shapes::RayGeom;
use crate::geometry::GeomWorldData;
use crate::math::{Mat33, Vec3};

use super::{height, surf_or_hole, surf_type, Heightfield};

/// The oracle's (and the fake `gEnv`'s) DrawRayOnGrid limit.
pub const DEFAULT_DRAW_RAY_LIMIT: i32 = 1000;

const EPS20: f32 = 1e-20;

/// The march context (a stack block of slot 10, passed to sub_35158BA0 / sub_35158080).
/// Offsets are those of the block.
#[derive(Clone, Debug, Default)]
pub struct HfRayContext {
    /// `+0` `|(d.x·stepr.x, d.y·stepr.y)|` (the local direction in cell units).
    pub len: f32,
    /// `+4` `(|d.z|·len)·√½` (double product).
    pub zext: f32,
    /// `+8` `((len + √2)·len + 1e-4)` in double: cells whose centre parameter (×len²) exceeds it
    /// end the march.
    pub tmax: f32,
    /// `+12` the hit cell's decoded surface type (−2 = no hit).
    pub result: i32,
    /// `+16` facing limit: a triangle counts only if `dir·n < limit` (2, or 0 for rotational
    /// unprojection with a zero axis).
    pub facing: i32,
    /// `+20` the last tested triangle (unnormalised normal).
    pub tri: Triangle,
    /// `+68` the local, unclipped ray.
    pub ray: RayPrim,
    /// `+104` INTERSECT output (`tolerance` = `minVtxDist²`).
    pub inters: PrimInters,
    /// `+192`/`+196` `0.5 − o·stepr`.
    pub org_c: [f32; 2],
    /// `+200`/`+204` `d·stepr`.
    pub dir_c: [f32; 2],
}

/// The ray geometry as the heightfield reads it (`CRayGeom +48/+60/+72`, priority `+20`).
pub type RayGeomData = RayGeom;

/// What [`march_grid`] returns.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RayMarchResult {
    /// 0: rejected (DrawRayOnGrid, misses the grid rectangle, degenerate major axis).
    Miss,
    /// 1: march finished (a cell test returned 1: hit, past the ray end, or off the grid; or the
    /// end cell was reached). `ctx.result ≠ −2` tells a hit.
    Done,
}

/// Inputs of [`Heightfield::intersect_ray`].
#[derive(Clone, Copy, Debug)]
pub struct RayIntersectInput<'a> {
    pub ray: &'a RayGeom,
    /// `gwd1`: the heightfield's placement (never null in the original).
    pub gwd_hf: &'a GeomWorldData,
    /// `gwd2`: the ray's placement (`None`: identity, unit scale).
    pub gwd_ray: Option<&'a GeomWorldData>,
    pub ip: Option<&'a IntersectionParams>,
    /// DrawRayOnGrid limit (`None` = `gEnv` byte 289 set).
    pub draw_ray_limit: Option<i32>,
}

/// `bits(v) − 0x4B400000` (the value already had `+12582912` added).
#[inline]
fn rb(v: f32) -> i32 {
    (v.to_bits() as i32).wrapping_sub(0x4B40_0000)
}

/// `sgnnz`: `((bits >> 30) & ~1) + 1`.
#[inline]
fn sgn(v: f32) -> i32 {
    ((v.to_bits() as i32 >> 30) & !1) + 1
}

/// sub_35158BA0: clip the ray `(org, dir)` (heightfield frame, both modified in place) to the
/// grid rectangle `[0, nx·step.x] × [0, ny·step.y]` with the engine's quotient comparisons, then
/// walk the cells along it (major axis by `|d|·stepr`), calling the cell test on each. `ctx`
/// must be prepared as slot 10 does ([`HfRayContext`]).
pub fn march_grid(
    p: &HeightfieldPrim,
    org: &mut Vec3,
    dir: &mut Vec3,
    ctx: &mut HfRayContext,
    draw_ray_limit: Option<i32>,
) -> RayMarchResult {
    if let Some(lim) = draw_ray_limit {
        let l = lim as f32;
        if dir.x.abs() > l || dir.y.abs() > l {
            // X2Log(2, "DrawRayOnGrid, dir.x(%.1f), dir.y(%.1f)", …)
            return RayMarchResult::Miss;
        }
    }
    let sx = sgn(dir.x);
    let sy = sgn(dir.y);
    let (fsx, fsy) = (sx as f32, sy as f32);
    // (num, den) pairs; index 0 is the entry, 1 the exit.
    let mut qx = [(0f32, 0f32); 2];
    let mut qy = [(0f32, 0f32); 2];
    qx[((1 - sx) >> 1) as usize] = (-(fsx * org.x), fsx * dir.x);
    qx[((sx + 1) >> 1) as usize] = (((p.size[0] as f32 * p.step[0]) - org.x) * fsx, fsx * dir.x);
    qy[((1 - sy) >> 1) as usize] = (-(fsy * org.y), fsy * dir.y);
    qy[((sy + 1) >> 1) as usize] = (((p.size[1] as f32 * p.step[1]) - org.y) * fsy, fsy * dir.y);
    // t_in = max(entry x, entry y)
    let (a, b) = (qx[0], qy[0]);
    let g = (b.1 * a.0 - a.1 * b.0) + (a.0 - b.0) * EPS20;
    let t = if g > 0.0 { a } else { b };
    // max(t_in, 0/1)
    let g = (-t.0) * EPS20 + (t.1 * 0.0 - t.0);
    let (num_in, den_in) = if g > 0.0 { (0.0, 1.0) } else { t };
    // t_out = min(exit x, exit y)
    let (a, b) = (qx[1], qy[1]);
    let g = (a.0 * b.1 - b.0 * a.1) + (a.0 - b.0) * EPS20;
    let t = if g < 0.0 || g.is_nan() { a } else { b };
    // min(t_out, 1/1)
    let g = (1.0 - t.0) * EPS20 + (t.1 - t.0);
    let (num_out, den_out) = if g < 0.0 || g.is_nan() { (1.0, 1.0) } else { t };
    let c_io = den_in * num_out;
    let c_oi = den_out * num_in;
    if (num_in - num_out) * EPS20 + (c_oi - c_io) >= 0.0 {
        return RayMarchResult::Miss;
    }
    let lead = num_in - den_in * 0.0;
    let rescale = if lead > 0.0 {
        let t = if den_in == 0.0 || den_in.is_nan() {
            0.0
        } else {
            num_in / den_in
        };
        *org = Vec3::new(t * dir.x + org.x, t * dir.y + org.y, t * dir.z + org.z);
        true
    } else {
        !(num_out - den_out >= 0.0)
    };
    if rescale {
        let den = den_out * den_in;
        let s = if den == 0.0 || den.is_nan() {
            0.0
        } else {
            (c_io - c_oi) / den
        };
        *dir = Vec3::new(s * dir.x, s * dir.y, s * dir.z);
    }

    let o = [org.x, org.y];
    let d = [dir.x, dir.y];
    let nx1 = p.size[0].wrapping_sub(1);
    let ny1 = p.size[1].wrapping_sub(1);
    let v74 = d[0].abs() * p.stepr[0] - d[1].abs() * p.stepr[1];
    let ia = (v74.to_bits() >> 31) as usize;
    let ib = ia ^ 1;
    let s = [sx, sy];
    let dmaj = d[ia].abs() * p.stepr[ia];
    let dmin = d[ib].abs() * p.stepr[ib];
    let magic = 12_582_912.0f32;
    let clamp_lo = |v: f32, n1: i32| -> i32 {
        let n = rb(v).wrapping_neg();
        let e = ((n >> 31) & n).wrapping_neg();
        let a = n1.wrapping_sub(e);
        ((a >> 31) & a).wrapping_add(e)
    };
    let ix0 = clamp_lo((p.stepr[0] * o[0] - 0.5) + magic, nx1);
    let iy0 = clamp_lo((p.stepr[1] * o[1] - 0.5) + magic, ny1);
    let ey = {
        let n = rb(((o[1] + d[1]) * p.stepr[1] - 0.5) + magic).wrapping_neg();
        let m = ((n >> 31) & n).wrapping_neg();
        let r = ny1.wrapping_sub(m);
        ((r >> 15) & (r << 16) & (0xFFFF_0000u32 as i32)).wrapping_add(m << 16)
    };
    let ex = {
        let n = rb(((o[0] + d[0]) * p.stepr[0] - 0.5) + magic).wrapping_neg();
        let m = ((n >> 31) & n).wrapping_neg();
        let r = nx1.wrapping_sub(m);
        ((r >> 31) & r).wrapping_add(m)
    };
    let end = ey | ex;
    let mut cur = [ix0, iy0];
    let packed = |c: &[i32; 2]| (c[1] << 16) | c[0];
    if cell_test(p, ctx, cur) != 0 || packed(&cur) == end {
        return RayMarchResult::Done;
    }
    if !(d[ia].abs() * p.stepr[ia] >= 1e-4) {
        return RayMarchResult::Miss;
    }
    let s_ia = s[ia];
    let s_ib = s[ib];
    let e = ((s_ia + 1) >> 1).wrapping_add(cur[ia]);
    let mut t = e as f32 * p.step[ia] - o_full(org, ia);
    t = if d[ia] == 0.0 || d[ia].is_nan() {
        0.0
    } else {
        t / d[ia]
    };
    let mut f = ((t * d[ib] + o_full(org, ib)) * p.stepr[ib] - cur[ib] as f32) * s_ib as f32
        + ((1 - s_ib) >> 1) as f32;
    if f > 1.0 {
        cur[ib] = cur[ib].wrapping_add(s_ib);
        if cell_test(p, ctx, cur) != 0 || packed(&cur) == end {
            return RayMarchResult::Done;
        }
        f += -1.0;
    }
    cur[ia] = cur[ia].wrapping_add(s_ia);
    let mut i = f * dmaj;
    let mut ebx = 0i32;
    if cell_test(p, ctx, cur) != 0 {
        return RayMarchResult::Done;
    }
    loop {
        if packed(&cur) == end {
            return RayMarchResult::Done;
        }
        i += (ebx ^ 1) as f32 * dmin;
        ebx = ((dmaj - i).to_bits() >> 31) as i32;
        cur[ib] = cur[ib].wrapping_add(s_ib.wrapping_mul(ebx));
        cur[ia] = cur[ia].wrapping_add((ebx - 1) & s_ia);
        i -= ebx as f32 * dmaj;
        if cell_test(p, ctx, cur) != 0 {
            return RayMarchResult::Done;
        }
    }
}

#[inline]
fn o_full(o: &Vec3, k: usize) -> f32 {
    if k == 0 {
        o.x
    } else {
        o.y
    }
}

/// sub_35158080: test cell `c = (ix, iy)`. Returns 1 to stop the march (hit, centre parameter
/// past `tmax`, or cell off the grid), 0 to go on.
///
/// The cell is skipped (0) when the ray's height at the cell centre, minus `zext`, is above
/// the cell's maximum corner height (all scaled by `len²`), or when it is a hole. Its triangles
/// are `(x0,y0,h00) (x1,y0,h10) (x0,y1,h01)` and `(x0,y1,h01) (x1,y0,h10) (x1,y1,h11)` with the
/// unnormalised normal `(p1 − p0) × (p2 − p0)`; a triangle hits when INTERSECT ray–tri succeeds
/// and `dir·n < facing`.
pub fn cell_test(p: &HeightfieldPrim, ctx: &mut HfRayContext, c: [i32; 2]) -> i32 {
    let (ix, iy) = (c[0], c[1]);
    let len2 = ctx.len * ctx.len;
    let tc = (iy as f32 + ctx.org_c[1]) * ctx.dir_c[1] + (ix as f32 + ctx.org_c[0]) * ctx.dir_c[0];
    if tc > ctx.tmax {
        return 1;
    }
    let inside = ((-1i32).wrapping_sub(iy) >> 31)
        & ((-1i32).wrapping_sub(ix) >> 31)
        & (iy.wrapping_sub(p.size[1]) >> 31)
        & (ix.wrapping_sub(p.size[0]) >> 31);
    if inside == 0 {
        return 1;
    }
    let zlow = (len2 * ctx.ray.origin.z + tc * ctx.ray.dir.z) - ctx.zext;
    let surf = surf_or_hole(p, surf_type(p, ix, iy));
    let hs = p.height_scale;
    let h00 = height(p, ix, iy) * hs;
    let h10 = height(p, ix + 1, iy) * hs;
    let h01 = height(p, ix, iy + 1) * hs;
    let h11 = height(p, ix + 1, iy + 1) * hs;
    let m1 = ((h00 - h10).abs() + (h10 + h00)) * 0.5;
    let m2 = ((m1 - h01).abs() + (m1 + h01)) * 0.5;
    let m3 = ((m2 - h11).abs() + (m2 + h11)) * 0.5;
    if zlow > m3 * len2 || surf < 0 {
        return 0;
    }
    let x0 = ix as f32 * p.step[0];
    let x1 = x0 + p.step[0];
    let y0 = iy as f32 * p.step[1];
    let y1 = y0 + p.step[1];
    let pts = [
        Vec3::new(x0, y0, h00),
        Vec3::new(x1, y0, h10),
        Vec3::new(x0, y1, h01),
    ];
    if tri_hit(ctx, pts) {
        ctx.result = surf;
        return 1;
    }
    let x1b = p.step[0] + x0;
    let pts = [
        Vec3::new(x0, y1, h01),
        Vec3::new(x1, y0, h10),
        Vec3::new(x1b, y1, h11),
    ];
    if tri_hit(ctx, pts) {
        ctx.result = surf;
        return 1;
    }
    0
}

fn tri_hit(ctx: &mut HfRayContext, pts: [Vec3; 3]) -> bool {
    ctx.tri = Triangle {
        pt: pts,
        n: super::region::cross_ba(pts[0], pts[1], pts[2]),
    };
    if !intersect_ray_tri(&ctx.ray, &ctx.tri, &mut ctx.inters) {
        return false;
    }
    let d = ctx.ray.dir;
    let n = ctx.tri.n;
    // `comiss; jb`: less than the limit, or NaN.
    !((d.x * n.x + d.y * n.y) + d.z * n.z >= ctx.facing as f32)
}

/// `(R·v)·s + off` with each row summed `(r0·x + r1·y) + r2·z`.
#[inline]
fn xf(r: &Mat33, v: Vec3, s: f32, off: Vec3) -> Vec3 {
    let m = &r.m;
    Vec3::new(
        ((m[0][0] * v.x + m[0][1] * v.y) + m[0][2] * v.z) * s + off.x,
        ((m[1][0] * v.x + m[1][1] * v.y) + m[1][2] * v.z) * s + off.y,
        ((m[2][0] * v.x + m[2][1] * v.y) + m[2][2] * v.z) * s + off.z,
    )
}

/// `R·v` (rows summed `(r0·x + r1·y) + r2·z`).
#[inline]
fn rot(r: &Mat33, v: Vec3) -> Vec3 {
    let m = &r.m;
    Vec3::new(
        (m[0][0] * v.x + m[0][1] * v.y) + m[0][2] * v.z,
        (m[1][0] * v.x + m[1][1] * v.y) + m[1][2] * v.z,
        (m[2][0] * v.x + m[2][1] * v.y) + m[2][2] * v.z,
    )
}

/// `Rᵀ·v` (columns summed `(r0k·x + r1k·y) + r2k·z`).
#[inline]
fn rot_t(r: &Mat33, v: Vec3) -> Vec3 {
    let m = &r.m;
    Vec3::new(
        (m[0][0] * v.x + m[1][0] * v.y) + m[2][0] * v.z,
        (m[0][1] * v.x + m[1][1] * v.y) + m[2][1] * v.z,
        (m[0][2] * v.x + m[1][2] * v.y) + m[2][2] * v.z,
    )
}

/// sub_35021020: normalise in place (`|v|² ≤ 1e-20` → `(0, 0, 1)`), `v·(1/√|v|²)`.
fn normalize(v: Vec3) -> Vec3 {
    let l2 = (v.x * v.x + v.y * v.y) + v.z * v.z;
    if l2 <= 1e-20 {
        Vec3::new(0.0, 0.0, 1.0)
    } else {
        let k = 1.0 / l2.sqrt();
        Vec3::new(v.x * k, v.y * k, v.z * k)
    }
}

impl Heightfield {
    /// Slot 10 (0x35156E20) when the other geometry is a ray (`GetType() == 3`). Returns the
    /// number of contacts (0 or 1); the contact is appended to `pool` (the per-caller contact
    /// pool, arena `+0xC7B8`, 64 × 136 bytes, count at `+0xE9B8`).
    ///
    /// * `ip == None` or `!ip.keep_prev_contacts`: the pool counters are reset first — the
    ///   contact count (`pool.clear()` here) and the area / area-point / border-point counters
    ///   (`+0xF2C0`, `+0x10EC4`, `+0x16EC8`), which the **caller** must reset as well.
    /// * The ray in world space (the ray geometry's slot 53 / sub_3519A190 under `gwd_ray`) is
    ///   brought into the heightfield frame (`Rᵀ·(p − off)/s`), `ctx` is prepared and
    ///   [`march_grid`] runs on a copy. No hit → 0.
    /// * Contact `center` = border point = the hit in world space (`(R·p)·s + off`).
    /// * If the ray's priority ≥ the heightfield's and `ip` is given (never with the engine's
    ///   priorities): `UNPROJECT[mode][tri][ray]` of the world triangle against the world ray
    ///   (mode 0: `dir = normalize?(v_ray − v_hf)` (normalised only when `||d|² − 1| > 0.001`),
    ///   `tmax = time_interval`; mode 1: `dir = ip.axis`, or when zero `n_world × raydir`
    ///   (fixed up when `|·|² < 0.002`, double compare) normalised, `center = ip.center`,
    ///   `tmax = π/2`; `tmin 0`, `minPtDist = minVtxDist`, `maxcos 0.1`, `flag 0`, `vel` stale on
    ///   the original's stack (the port passes 0)). On success: `t` (mode 1: `atan2(sin, cos)`),
    ///   `pt`, `n` from the result, `dir` from the params, mode, features from the result.
    /// * Otherwise (and when the unprojection fails): `t = ((R·(hit − o_local))·ndir_world)·s`
    ///   (a distance along the ray, as double), `pt = center`, `n = R·normalize(tri normal)`,
    ///   `dir = 0`, `iUnprojMode = −1`, features `(0x40, 0x20)`.
    /// * Always: `vel = 0`, `id = (surface type, −1)`, one border point. `iPrim`, `iNode` and
    ///   `idxBorder` are not written (stale pool data in the original; default here).
    #[must_use]
    pub fn intersect_ray(
        &self,
        input: &RayIntersectInput<'_>,
        tables: &UnprojTables,
        pool: &mut Vec<GeomContact>,
    ) -> i32 {
        let ray = input.ray;
        let ip = input.ip;
        let keep = ip.is_some_and(|ip| ip.keep_prev_contacts);
        let mut facing = 2;
        if let Some(ip) = ip {
            let a = ip.axis_of_rotation;
            if ip.i_unprojection_mode == 1 && (a.x * a.x + a.y * a.y) + a.z * a.z == 0.0 {
                facing = 0;
            }
        }
        let (r2, off2, s2) = match input.gwd_ray {
            Some(g) => (g.r, g.offset, g.scale),
            None => (Mat33::IDENTITY, Vec3::ZERO, 1.0),
        };
        let wray = RayPrim {
            origin: xf(&r2, ray.prim.origin, s2, off2),
            dir: {
                let d = rot(&r2, ray.prim.dir);
                Vec3::new(d.x * s2, d.y * s2, d.z * s2)
            },
        };
        let ndw = rot(&r2, ray.ndir);
        if !keep {
            pool.clear();
        }
        let g1 = input.gwd_hf;
        let p = &self.bv.prim;
        let inv = 1.0 / g1.scale;
        let d = wray.origin - g1.offset;
        let lo = rot_t(&g1.r, d);
        let lo = Vec3::new(lo.x * inv, lo.y * inv, lo.z * inv);
        let ld = rot_t(&g1.r, wray.dir);
        let ld = Vec3::new(ld.x * inv, ld.y * inv, ld.z * inv);
        let mut ctx = HfRayContext {
            result: -2,
            facing,
            ray: RayPrim {
                origin: lo,
                dir: ld,
            },
            ..HfRayContext::default()
        };
        ctx.inters.tolerance = self.mesh.min_vtx_dist * self.mesh.min_vtx_dist;
        let dcx = ld.x * p.stepr[0];
        let dcy = ld.y * p.stepr[1];
        ctx.dir_c = [dcx, dcy];
        ctx.org_c = [0.5 - lo.x * p.stepr[0], 0.5 - lo.y * p.stepr[1]];
        let len = (dcx * dcx + dcy * dcy).sqrt();
        ctx.len = len;
        ctx.zext = (f64::from(ld.z.abs() * len) * std::f64::consts::FRAC_1_SQRT_2) as f32;
        ctx.tmax = ((f64::from(len) + std::f64::consts::SQRT_2) * f64::from(len)
            + f64::from(1e-4f32)) as f32;
        let (mut mo, mut md) = (lo, ld);
        if march_grid(p, &mut mo, &mut md, &mut ctx, input.draw_ray_limit) == RayMarchResult::Miss
            || ctx.result == -2
        {
            return 0;
        }
        let hit = ctx.inters.pt[0];
        let center = xf(&g1.r, hit, g1.scale, g1.offset);
        let mut c = GeomContact {
            center,
            border: vec![center],
            ..GeomContact::default()
        };
        let mut done = false;
        if let Some(ip) = ip.filter(|_| ray.hdr.priority >= self.mesh.priority) {
            let mode = ip.i_unprojection_mode;
            let mut up = UnprojParams {
                mode,
                ..UnprojParams::default()
            };
            if mode != 0 {
                let mut a = ip.axis_of_rotation;
                if (a.x * a.x + a.y * a.y) + a.z * a.z == 0.0 {
                    let n = ctx.inters.n;
                    let wn = Vec3::new(
                        (n.y * g1.r.m[0][1] + n.x * g1.r.m[0][0]) + n.z * g1.r.m[0][2],
                        (n.x * g1.r.m[1][0] + n.y * g1.r.m[1][1]) + n.z * g1.r.m[1][2],
                        (n.y * g1.r.m[2][1] + n.x * g1.r.m[2][0]) + n.z * g1.r.m[2][2],
                    );
                    let rd = wray.dir;
                    a = Vec3::new(
                        wn.y * rd.z - wn.z * rd.y,
                        wn.z * rd.x - wn.x * rd.z,
                        wn.x * rd.y - wn.y * rd.x,
                    );
                    let l2 = (a.x * a.x + a.y * a.y) + a.z * a.z;
                    if !(f64::from(l2) >= 0.002) {
                        let q = ((rd.y * rd.y + rd.x * rd.x) + rd.z * rd.z)
                            * f32::from_bits(0x3F4F_5C28)
                            - rd.x * rd.x;
                        let i = (q.to_bits() >> 31) as usize;
                        let inc = [1usize, 2, 0][i];
                        let dec = [2usize, 0, 1][i];
                        let rda = rd.to_array();
                        let mut aa = a.to_array();
                        aa[i] = 0.0;
                        aa[inc] = rda[dec];
                        aa[dec] = -rda[inc];
                        a = Vec3::from_array(aa);
                    }
                    a = normalize(a);
                }
                up.dir = a;
                up.center = ip.center_of_rotation;
                up.tmax = f32::from_bits(0x3FC9_0FDB);
            } else {
                let v2 = input.gwd_ray.map_or(Vec3::ZERO, |g| g.v);
                let mut dd = v2 - g1.v;
                if (((dd.x * dd.x + dd.y * dd.y) + dd.z * dd.z) - 1.0).abs() > 0.001 {
                    dd = normalize(dd);
                }
                up.dir = dd;
                up.tmax = ip.time_interval;
            }
            up.tmin = 0.0;
            up.min_pt_dist = self.mesh.min_vtx_dist;
            up.max_cos = 0.1;
            up.flag = 0;
            let t = &ctx.tri;
            let wt = Triangle {
                pt: [
                    xf(&g1.r, t.pt[0], g1.scale, g1.offset),
                    xf(&g1.r, t.pt[1], g1.scale, g1.offset),
                    xf(&g1.r, t.pt[2], g1.scale, g1.offset),
                ],
                n: {
                    let n = normalize_tri(t.n);
                    Vec3::new(
                        (n.y * g1.r.m[0][1] + g1.r.m[0][0] * n.x) + n.z * g1.r.m[0][2],
                        (n.y * g1.r.m[1][1] + g1.r.m[1][0] * n.x) + n.z * g1.r.m[1][2],
                        (n.y * g1.r.m[2][1] + g1.r.m[2][0] * n.x) + n.z * g1.r.m[2][2],
                    )
                },
            };
            if mode >= 0 {
                let mut res = UnprojResult::default();
                let ok = tables.unproject(
                    (mode as usize).min(1),
                    &mut up,
                    &Primitive::Triangle(wt),
                    -1,
                    &Primitive::Ray(wray),
                    -1,
                    &mut res,
                    None,
                );
                if mode <= 1 && ok != 0 {
                    c.t = if mode != 0 {
                        crate::math::crt::atan2(res.t, res.cos_t)
                    } else {
                        res.t
                    };
                    c.pt = res.pt;
                    c.n = res.n;
                    c.dir = up.dir;
                    c.i_unproj_mode = mode;
                    c.i_feature = res.i_feature;
                    done = true;
                }
            }
        }
        if !done {
            let hd = hit - lo;
            let w = Vec3::new(
                (hd.y * g1.r.m[0][1] + hd.x * g1.r.m[0][0]) + hd.z * g1.r.m[0][2],
                (hd.x * g1.r.m[1][0] + hd.y * g1.r.m[1][1]) + hd.z * g1.r.m[1][2],
                (hd.y * g1.r.m[2][1] + hd.x * g1.r.m[2][0]) + hd.z * g1.r.m[2][2],
            );
            c.t = f64::from(((w.x * ndw.x + w.y * ndw.y) + w.z * ndw.z) * g1.scale);
            c.pt = center;
            let n = ctx.inters.n;
            let l2 = (n.x * n.x + n.y * n.y) + n.z * n.z;
            let nn = if l2 > 1e-20 {
                let k = 1.0 / l2.sqrt();
                Vec3::new(k * n.x, n.y * k, n.z * k)
            } else {
                Vec3::new(0.0, 0.0, 1.0)
            };
            c.n = Vec3::new(
                (nn.y * g1.r.m[0][1] + nn.x * g1.r.m[0][0]) + nn.z * g1.r.m[0][2],
                (nn.x * g1.r.m[1][0] + nn.y * g1.r.m[1][1]) + nn.z * g1.r.m[1][2],
                (nn.y * g1.r.m[2][1] + nn.x * g1.r.m[2][0]) + nn.z * g1.r.m[2][2],
            );
            c.dir = Vec3::ZERO;
            c.i_unproj_mode = -1;
            c.i_feature = [0x40, 0x20];
        }
        c.vel = 0.0;
        c.id = [ctx.result, -1];
        pool.push(c);
        1
    }
}

/// The triangle-normal normalisation of the unprojection path:
/// `|n|² = (n.y² + n.x²) + n.z²`, `> 1e-20` → `n·(1/√)`, else `(0, 0, 1)`.
fn normalize_tri(n: Vec3) -> Vec3 {
    let l2 = (n.y * n.y + n.x * n.x) + n.z * n.z;
    if l2 > 1e-20 {
        let k = 1.0 / l2.sqrt();
        Vec3::new(n.x * k, n.y * k, n.z * k)
    } else {
        Vec3::new(0.0, 0.0, 1.0)
    }
}
