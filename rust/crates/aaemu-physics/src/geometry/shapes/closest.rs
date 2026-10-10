//! Slot 11 `FindClosestPoint(gwd, iPrim&, iFeature&, ptdst0, ptdst1, ptres[2], nMaxIters)`.
//!
//! Closest points between a primitive placed by `gwd` and the segment `ptdst0..ptdst1` (world
//! frame). On return `ptres[0]` is the point on the geometry and `ptres[1]` the point on the
//! segment, both in the world frame; every class returns 1. A segment counts as a single
//! point when its squared length is small against the primitive's size (sign bit of
//! `size·1e-5 − |Δ|²` for the box, of `r²·1e-6 − |Δ|²` in double for cylinder and capsule;
//! the sphere only treats a zero (or NaN) length that way).
//!
//! **Inputs/outputs other than the points.** `nMaxIters` is ignored by all four classes and
//! so is the incoming value of `iPrim`/`iFeature`:
//! * box (0x3513D020): writes `iPrim = 0` and a feature code of the box, `0x00 | v` vertex
//!   (`v` bit 0/1/2 = +x/+y/+z corner), `0x20 | axis<<2 | e` edge parallel to `axis`
//!   (`e` bit 0 = + side of axis `inc(axis)`, bit 1 = + side of axis `dec(axis)`; the point
//!   path builds the same code from the two face axes), `0x40 | axis<<1 | (side > 0)` face;
//! * sphere (0x351AF660): leaves `iPrim` and writes a feature of the **segment** instead: 0x20
//!   when the closest point is interior to the segment, else the index (0/1) of the chosen
//!   end point;
//! * cylinder (0x35145CC0) and capsule (0x3513FCA0): write neither.
//!
//! Every function mirrors the binary's precision (the box and sphere are all single precision;
//! cylinder and capsule widen to double and round back at fixed points) and association.
//!
//! Submodules: [`poly`] (`polynomial_tpl` helpers of the cylinder's rim quartic: derivative,
//! pseudo-division, Sturm count, closed-form roots) and [`crt64`] (bit-exact MSVCR100 double
//! `exp`/`sin`/`cos` used by the root finder).

// `!(a > b)`-style tests are deliberate: they reproduce the binary's branch on unordered
// (NaN) operands (`comiss`/`comisd` + `jbe`/`jb`/`jae`).
#![allow(clippy::neg_cmp_op_on_partial_ord)]

pub mod crt64;
pub mod poly;

use super::cylinder::CylinderData;
use super::{BoxGeom, SphereGeom, DEC_MOD3, INC_MOD3};
use crate::geometry::GeomWorldData;
use crate::math::{max_idiom, min_idiom, sgnnz, Mat33, Vec3, Vec3d};
pub use poly::{findroots, sturm_nroots, Poly};

/// `1e10` (0x501502F9), the "no point yet" value `ptres[0]` starts with (box).
const FAR: f32 = f32::from_bits(0x5015_02F9);
/// `1e-5` (0x3727C5AC), box degenerate-segment factor (multiplies `size.x·scale`).
const BOX_SEG_EPS: f32 = f32::from_bits(0x3727_C5AC);
/// `1e20` (0x60AD78EC), initial best edge distance of the box edge scan.
const BOX_BIG: f32 = f32::from_bits(0x60AD_78EC);
/// `1e9` (0x4E6E6B28).
const BOX_1E9: f32 = f32::from_bits(0x4E6E_6B28);
/// `1e-4` (0x38D1B717), the box point-path "on the surface" tolerance.
const BOX_TOL: f32 = f32::from_bits(0x38D1_B717);
/// `1e-20` (0x1E3CE508), the normalisation guard.
const NORM_EPS: f32 = f32::from_bits(0x1E3C_E508);
/// `(double)1e-6f` (0x3EB0C6F7A0000000), cylinder/capsule degenerate-segment factor.
const CYL_SEG_EPS: f64 = f64::from_bits(0x3EB0_C6F7_A000_0000);
/// `(double)1e-20f` (0x3BC79CA100000000), the double normalisation guard.
const NORM_EPS_D: f64 = f64::from_bits(0x3BC7_9CA1_0000_0000);

/// Sign bit of a float.
#[inline]
fn sbf(x: f32) -> u32 {
    x.to_bits() >> 31
}

/// Sign bit of a double (equal to the sign bit of its `cvtsd2ss`, which the binary tests).
#[inline]
fn sbd(x: f64) -> u32 {
    (x.to_bits() >> 63) as u32
}

/// `Mᵀ·v` with each component `(v.x·m[0][k] + v.y·m[1][k]) + v.z·m[2][k]` (single precision).
#[inline]
fn mul_t(m: &Mat33, v: Vec3) -> Vec3 {
    let a = &m.m;
    Vec3::new(
        (v.x * a[0][0] + v.y * a[1][0]) + v.z * a[2][0],
        (v.x * a[0][1] + v.y * a[1][1]) + v.z * a[2][1],
        (v.x * a[0][2] + v.y * a[1][2]) + v.z * a[2][2],
    )
}

/// Widen a vector to double (`cvtps2pd`).
#[inline]
fn wide(v: Vec3) -> Vec3d {
    v.to_f64()
}

/// `(float)(a − b)` per component, computed in double (the cylinder/capsule `d0` idiom).
#[inline]
fn sub_round(a: Vec3d, b: Vec3d) -> Vec3d {
    Vec3::new((a.x - b.x) as f32, (a.y - b.y) as f32, (a.z - b.z) as f32).to_f64()
}

// ------------------------------------------------------------------------------------------
// Box
// ------------------------------------------------------------------------------------------

/// The box frame used by 0x3513D020: `l = Rᵀ·(p − offset) − center·scale`, then `Basis·l` when
/// the box is oriented. The frame is scaled by `scale` (half sizes are `size·scale`), not
/// divided by it.
fn box_local(g: &BoxGeom, gwd: &GeomWorldData, p: Vec3) -> Vec3 {
    let c = g.prim.center * gwd.scale;
    let l = mul_t(&gwd.r, p - gwd.offset) - c;
    if g.prim.oriented != 0 {
        g.prim.basis.mul_vec(l)
    } else {
        l
    }
}

/// Back to the world: `R·(Basisᵀ·l + center·scale) + offset`. The edge and vertex paths apply
/// `Basisᵀ` **unconditionally** (even for a non-oriented box, whose basis is the identity up
/// to `±0`/NaN entries, see [`super::is_identity`]); the point path only when oriented.
fn box_world(g: &BoxGeom, gwd: &GeomWorldData, l: Vec3, use_basis: bool) -> Vec3 {
    let c = g.prim.center * gwd.scale;
    let b = if use_basis {
        mul_t(&g.prim.basis, l)
    } else {
        l
    };
    gwd.r.mul_vec(b + c) + gwd.offset
}

/// Box slot 11 (0x3513D020).
///
/// 1. `ptres[0] = (1e10, 1e10, 1e10)`, `ptres[1] = ptdst0`, `iPrim = 0`; both points go to
///    the box frame (`box_local`, half sizes `hs = size·scale`).
/// 2. If `sign(hs.x·1e-5 − |p0 − p1|²)` is set (a real segment, `-0.0` included):
///    * **edges**: for each axis `i` (`j = inc i`, `k = dec i`), the four edges parallel to
///      `i` are classified by the 2-D cross product `c = (corner_j − l0_j)·Δ_k −
///      (corner_k − l0_k)·Δ_j` (corners in the order (−,−), (+,−), (−,+), (+,+)). If all four
///      `sgnnz(c)` agree (the projected line misses the box section) the edge with the
///      smallest `|c|` (first one on ties; NaN always replaces) is tested: with
///      `n = (0 at i, Δ_k at j, −Δ_j at k)`, `d = corner − l0`, `t = (d_k at j, −d_j at k)·n`
///      and `s = (d × Δ)·n`, the edge is taken when `|n² − 2t| < n²` (segment interior),
///      `|s| < n²·hs_i` (edge interior) and `|d·n|/n² < best` (as
///      `max(|s| − n²·hs_i, |d·n|·den − num·n²) < 0`, best = num/den, initially 1/0). Then
///      `iFeature = 0x20 | i<<2 | e`, `ptres[0]` = the edge point (`s/n²` along `i`),
///      `ptres[1] = l0 + (t/n²)·Δ`, best = `|(edge point − l0)·n|/n²`.
///    * if no edge was taken (`num − den·1e9 ≥ 0`), **vertices** `0..8` are tried in order: a
///      vertex whose projection `q` onto the segment is interior writes `iFeature`, `ptres[0]`
///      and the **local** `q` into `ptres[1]`, and is accepted if
///      `min(q.x − hs.x, q.y − hs.y, q.z − hs.z) > 0`. **Original bugs kept**: that test is
///      signed (only projections beyond the box in its (+,+,+) octant pass) and it looks at
///      the segment point, not at the vertex, so the first qualifying vertex wins even when it
///      is the far corner (a segment beyond (+,+,+) typically reports vertex 0, (−,−,−)). A
///      rejected candidate leaves its values behind, but the point path below overwrites them.
///    * then the point path runs on both end points.
/// 3. **Point path** (one or two points): per axis, `δ_a = max(|l_a| − hs_a, 0)·(−sgnnz(l_a))`
///    moves the point onto the box; the axes with `|δ_a| > 1e-4` are the point's feature axes.
///    A point with none is inside: it is pushed out through the face of least depth
///    (`hs − |l|`, index chosen with the sign-bit argmin idiom). **Original bug kept**: the
///    choice between the two end points reads the second point's feature count from a slot
///    the loop writes, but compares `|δ1|² − |δ0|²`, so it does pick the closer end; the
///    feature code is built from the chosen point's axes and signs (face/edge/vertex, see the
///    module doc). `ptres[0] = world(l + δ)`, `ptres[1]` = the chosen original end point.
///
/// `ebx` quirk: the edge index register starts as the low 32 bits of the `ptres` pointer and
/// keeps the previous axis' value when no `|c|` is below `1e20` (huge or NaN coordinates). The
/// port starts it at 0, which equals the binary for 4-byte aligned `ptres` whenever only bits
/// 0–1 are used; the feature code would include the pointer's other bits in that case.
#[allow(clippy::too_many_arguments)]
pub fn box_find_closest_point(
    g: &BoxGeom,
    gwd: &GeomWorldData,
    i_prim: &mut i32,
    i_feature: &mut i32,
    ptdst0: Vec3,
    ptdst1: Vec3,
    ptres: &mut [Vec3; 2],
    _n_max_iters: i32,
) -> i32 {
    let pts = [ptdst0, ptdst1];
    ptres[0] = Vec3::splat(FAR);
    ptres[1] = ptdst0;
    let l0 = box_local(g, gwd, ptdst0);
    *i_prim = 0;
    let hs = g.prim.size * gwd.scale;
    let dd = ptdst0 - ptdst1;
    let seg = sbf(hs.x * BOX_SEG_EPS - ((dd.x * dd.x + dd.y * dd.y) + dd.z * dd.z)) as usize;
    let mut lpt = [l0, Vec3::ZERO];
    if seg != 0 {
        let l1 = box_local(g, gwd, ptdst1);
        lpt[1] = l1;
        let dl = l1 - l0;
        let dl2 = (dl.x * dl.x + dl.y * dl.y) + dl.z * dl.z;
        let mut num = 1.0f32;
        let mut den = 0.0f32;
        let mut ebx: i32 = 0;
        let mut pt = Vec3::ZERO;
        for i in 0..3 {
            let (j, k) = (INC_MOD3[i], DEC_MOD3[i]);
            let mut best = BOX_BIG;
            let mut n = Vec3::ZERO;
            n[j] = dl[k];
            n[k] = -dl[j];
            pt[i] = 0.0;
            let n2 = (n.x * n.x + n.y * n.y) + n.z * n.z;
            let mut sum: i32 = 0;
            let corners = [
                (-hs[j], -hs[k]),
                (hs[j], -hs[k]),
                (-hs[j], hs[k]),
                (hs[j], hs[k]),
            ];
            for (e, &(cj, ck)) in corners.iter().enumerate() {
                let c = (cj - l0[j]) * dl[k] - (ck - l0[k]) * dl[j];
                let ac = c.abs();
                if !(ac >= best) {
                    best = ac;
                    ebx = e as i32;
                }
                sum += sgnnz(c);
            }
            pt[j] = hs[j];
            pt[k] = hs[k];
            if sum.wrapping_abs() < 4 {
                continue;
            }
            pt[j] = (2 * (ebx & 1) - 1) as f32 * hs[j];
            pt[k] = ((ebx & 2) - 1) as f32 * hs[k];
            let d = pt - l0;
            let mut tmp = Vec3::ZERO;
            tmp[j] = d[k];
            tmp[k] = -d[j];
            let t = (tmp.x * n.x + tmp.y * n.y) + tmp.z * n.z;
            let s = ((d.y * dl.z - d.z * dl.y) * n.x + (dl.x * d.z - d.x * dl.z) * n.y)
                + (d.x * dl.y - dl.x * d.y) * n.z;
            let interior = (n2 - t * 2.0).abs() - (-n2).abs();
            let dn = (d.x * n.x + d.y * n.y) + d.z * n.z;
            let a = s.abs() - n2 * hs[i];
            let b = dn.abs() * den - num * n2;
            let m = max_idiom(a, b);
            if (m.to_bits() & interior.to_bits()) >> 31 != 0 {
                *i_feature = (((i as i32) | 8) << 2) | ebx;
                let inv = 1.0 / n2;
                pt[i] = inv * s;
                ptres[0] = box_world(g, gwd, pt, true);
                let f = inv * t;
                let q = Vec3::new(f * dl.x + l0.x, f * dl.y + l0.y, f * dl.z + l0.z);
                ptres[1] = box_world(g, gwd, q, true);
                let e = pt - l0;
                num = ((e.y * n.y + e.x * n.x) + e.z * n.z).abs() * inv;
                den = 1.0;
                pt[i] = 0.0;
            }
        }
        if !(num - den * BOX_1E9 >= 0.0) {
            return 1;
        }
        let ndl2 = (-dl2).abs();
        for c in 0..8i32 {
            let v = Vec3::new(
                (2 * (c & 1) - 1) as f32 * hs.x,
                ((c & 2) - 1) as f32 * hs.y,
                (((c >> 1) & 2) - 1) as f32 * hs.z,
            );
            let t = ((v.x - l0.x) * dl.x + (v.y - l0.y) * dl.y) + (v.z - l0.z) * dl.z;
            if sbf((dl2 - t * 2.0).abs() - ndl2) == 0 {
                continue;
            }
            *i_feature = c;
            ptres[0] = box_world(g, gwd, v, true);
            let f = t / dl2;
            let q = Vec3::new(f * dl.x + l0.x, f * dl.y + l0.y, f * dl.z + l0.z);
            ptres[1] = q;
            let m = min_idiom(min_idiom(q.x - hs.x, q.y - hs.y), q.z - hs.z);
            if m > 0.0 {
                ptres[1] = box_world(g, gwd, q, true);
                return 1;
            }
        }
    }

    // Point path.
    let mut delta = [Vec3::ZERO; 2];
    let mut nfeat = [0i32; 2];
    let mut signs = [[0i32; 3]; 2];
    let mut axes = [[0i32; 3]; 2];
    let mut ax_list = [0i32; 3];
    let mut sg_list = [0i32; 3];
    for p in 0..=seg {
        let lp = lpt[p];
        let mut cnt = 0usize;
        let mut tmp = Vec3::ZERO;
        for a in 0..3 {
            ax_list[cnt] = a as i32;
            let x = lp[a];
            let sg = sgnnz(x);
            sg_list[cnt] = sg;
            let d = x.abs() - hs[a];
            let v = (((-d).abs() + d) * 0.5) * (-sg) as f32;
            tmp[a] = v;
            cnt += sbf(BOX_TOL - v.abs()) as usize;
        }
        signs[p] = sg_list;
        delta[p] = tmp;
        nfeat[p] = cnt as i32;
        axes[p] = ax_list;
        if cnt == 0 {
            delta[p] = Vec3::ZERO;
            nfeat[p] = 1;
            let depth = [hs.x - lp.x.abs(), hs.y - lp.y.abs(), hs.z - lp.z.abs()];
            let e1 = sbf(depth[1] - depth[0]) as usize;
            let e2 = sbf(depth[2] - depth[e1]) as usize;
            let kk = e1 | (e2 << 1);
            let ax = (((kk ^ 2) | 4) >> 1) & kk;
            axes[p][0] = ax as i32;
            let x = lp[ax];
            let sg = sgnnz(x);
            signs[p][0] = sg;
            delta[p][ax] = sg as f32 * hs[ax] - x;
        }
    }
    let d0 = delta[0];
    let d1 = delta[1];
    let sel = usize::from(nfeat[1] > 0)
        & sbf(((d1.x * d1.x + d1.y * d1.y) + d1.z * d1.z)
            - ((d0.x * d0.x + d0.y * d0.y) + d0.z * d0.z)) as usize;
    let (sg, ax) = (signs[sel], axes[sel]);
    *i_feature = match nfeat[sel] {
        1 => ((ax[0] | 0x20) * 2) | ((sg[0] + 1) >> 1),
        2 => {
            let e = 3 - ax[1] - ax[0];
            let cl = e & 1;
            (((e | 8) << 2) | (((sg[0] + 1) >> 1) << cl)) | ((sg[1] + 1) >> cl)
        }
        _ => ((sg[2] * 2 + 2) | ((sg[0] + 1) >> 1)) | (sg[1] + 1),
    };
    let lp = lpt[sel] + delta[sel];
    ptres[0] = box_world(g, gwd, lp, g.prim.oriented != 0);
    ptres[1] = pts[sel];
    1
}

// ------------------------------------------------------------------------------------------
// Sphere
// ------------------------------------------------------------------------------------------

/// Sphere slot 11 (0x351AF660), all single precision.
///
/// `c = R·center·scale + offset`, `r' = scale·r`. With `Δ = p1 − p0`: if `|Δ|² ≤ 0` the
/// segment point is `p0` (`iFeature = 0`); else `t = (c − p0)·Δ` and when
/// `|Δ|² − 2t` and `|Δ|²` give `sign(| |Δ|² − 2t | − |Δ|²)` set (projection interior,
/// `-0.0` counts) the point is `Δ·t·(1/|Δ|²) + p0` (`iFeature = 0x20`), otherwise the closer
/// end (sign bit of `|p1 − c|² − |p0 − c|²`, written to `iFeature`) as `Δ·k + p0`, `k ∈ {0, 1}`
/// (so the far end is recomputed, not copied). `ptres[0] = c + r'·normalize(ptres[1] − c)`
/// (direction `(0,0,1)` when `|·|² ≤ 1e-20`). `iPrim` is not written.
#[allow(clippy::too_many_arguments)]
pub fn sphere_find_closest_point(
    g: &SphereGeom,
    gwd: &GeomWorldData,
    _i_prim: &mut i32,
    i_feature: &mut i32,
    ptdst0: Vec3,
    ptdst1: Vec3,
    ptres: &mut [Vec3; 2],
    _n_max_iters: i32,
) -> i32 {
    let sc = gwd.scale;
    let c = gwd.r.mul_vec(g.prim.center) * sc + gwd.offset;
    let r = sc * g.prim.r;
    let p0 = ptdst0;
    let d = ptdst1 - p0;
    let d2 = (d.y * d.y + d.x * d.x) + d.z * d.z;
    if !(d2 > 0.0) {
        ptres[1] = p0;
        *i_feature = 0;
    } else {
        let t = ((c.y - p0.y) * d.y + (c.x - p0.x) * d.x) + (c.z - p0.z) * d.z;
        if sbf((d2 - t * 2.0).abs() - (-d2).abs()) != 0 {
            *i_feature = 0x20;
            let inv = 1.0 / d2;
            ptres[1] = Vec3::new(
                (d.x * t) * inv + p0.x,
                (d.y * t) * inv + p0.y,
                (d.z * t) * inv + p0.z,
            );
        } else {
            let e1 = ptdst1 - c;
            let e0 = p0 - c;
            let dd = ((e1.y * e1.y + e1.x * e1.x) + e1.z * e1.z)
                - ((e0.y * e0.y + e0.x * e0.x) + e0.z * e0.z);
            let k = sbf(dd);
            *i_feature = k as i32;
            let kf = k as f32;
            ptres[1] = Vec3::new(kf * d.x + p0.x, d.y * kf + p0.y, d.z * kf + p0.z);
        }
    }
    let v = ptres[1] - c;
    let l2 = (v.y * v.y + v.x * v.x) + v.z * v.z;
    let n = if l2 > NORM_EPS {
        let inv = 1.0 / l2.sqrt();
        Vec3::new(inv * v.x, v.y * inv, v.z * inv)
    } else {
        Vec3::Z
    };
    ptres[0] = Vec3::new(n.x * r + c.x, n.y * r + c.y, n.z * r + c.z);
    1
}

// ------------------------------------------------------------------------------------------
// Cylinder and capsule (shared parts)
// ------------------------------------------------------------------------------------------

/// The placed cylinder/capsule and the segment, as both functions set them up (identical
/// code at the top of 0x35145CC0 and 0x3513FCA0).
struct CylSetup {
    /// World axis `R·axis` (single precision, widened).
    a: Vec3d,
    /// World centre `R·center·scale + offset` (single, widened).
    c: Vec3d,
    /// `scale·r`, `scale·hh` (single products, widened).
    r: f64,
    hh: f64,
    /// The end points widened.
    pd: [Vec3d; 2],
    /// `(float)(p0 − c)` (double subtraction rounded to single), widened.
    d0: Vec3d,
    /// `p1 − p0` (single), widened.
    dp: Vec3d,
    /// `(dp.y² + dp.x²) + dp.z²` (double).
    len2: f64,
    /// 1 when the segment is not degenerate: sign of `(float)(r²·1e-6 − len2)`.
    seg: usize,
}

fn cyl_setup(d: &CylinderData, gwd: &GeomWorldData, p0: Vec3, p1: Vec3) -> CylSetup {
    let p = &d.prim;
    let sc = gwd.scale;
    let a = wide(gwd.r.mul_vec(p.axis));
    let c = wide(gwd.r.mul_vec(p.center) * sc + gwd.offset);
    let r = f64::from(sc * p.r);
    let hh = f64::from(sc * p.hh);
    let pd = [wide(p0), wide(p1)];
    let d0 = sub_round(pd[0], c);
    let dp = wide(p1 - p0);
    let len2 = (dp.y * dp.y + dp.x * dp.x) + dp.z * dp.z;
    let seg = sbd(r * r * CYL_SEG_EPS - len2) as usize;
    CylSetup {
        a,
        c,
        r,
        hh,
        pd,
        d0,
        dp,
        len2,
        seg,
    }
}

/// The line–line test shared by both classes: `n = Δ × a`, `nn = |n|²`, `nd = n·d0`,
/// `t = (e × a)·n`, `s = (e × Δ)·n` with `e = −d0`. Returns `(accept, t, nn, s)`: accept when
/// `|s| < nn·hh` (as `|((−nnh) + nnh) − 2s| − |(−nnh) − nnh|`), `|nn − 2t| < nn` and
/// `nn·r² < nd²` (the infinite lines pass outside the radius), all as sign bits.
fn line_line(s: &CylSetup) -> (bool, f64, f64, f64) {
    let (a, dp, d0) = (s.a, s.dp, s.d0);
    let n = Vec3d::new(
        dp.y * a.z - dp.z * a.y,
        dp.z * a.x - a.z * dp.x,
        a.y * dp.x - dp.y * a.x,
    );
    let nn = (n.y * n.y + n.x * n.x) + n.z * n.z;
    let nd = (n.y * d0.y + n.x * d0.x) + n.z * d0.z;
    let e = -d0;
    let t = ((e.z * a.x - a.z * e.x) * n.y + (e.y * a.z - e.z * a.y) * n.x)
        + (a.y * e.x - e.y * a.x) * n.z;
    let sa = ((e.z * dp.x - dp.z * e.x) * n.y + (e.y * dp.z - e.z * dp.y) * n.x)
        + (dp.y * e.x - e.y * dp.x) * n.z;
    let nnh = nn * s.hh;
    let ta = ((-nnh + nnh) - sa * 2.0).abs() - (-nnh - nnh).abs();
    let tb = (nn - t * 2.0).abs() - (-nn).abs();
    let tc = nn * (s.r * s.r) - nd * nd;
    (sbd(ta) & sbd(tb) & sbd(tc) != 0, t, nn, sa)
}

/// `ptres[1] = (float)(p0 + tt·Δ)` (double sums, `tt·Δx` but `Δy·tt`, `Δz·tt`).
fn seg_point(s: &CylSetup, tt: f64) -> Vec3 {
    let (p, dp) = (s.pd[0], s.dp);
    Vec3::new(
        (p.x + tt * dp.x) as f32,
        (p.y + dp.y * tt) as f32,
        (p.z + dp.z * tt) as f32,
    )
}

/// Tail shared by the segment branches: the axis point `P = (float)(a·h + c)`, then
/// `ptres[0] = P + r·normalize(ptres[1] − P)` in single precision (`(0,0,1)` when
/// `|·|² ≤ 1e-20`).
fn axis_tail(s: &CylSetup, h: f64, ptres: &mut [Vec3; 2]) {
    let (a, c) = (s.a, s.c);
    let pp = Vec3::new(
        (h * a.x + c.x) as f32,
        (a.y * h + c.y) as f32,
        (a.z * h + c.z) as f32,
    );
    let v = ptres[1] - pp;
    let l2 = (v.y * v.y + v.x * v.x) + v.z * v.z;
    let n = if l2 > NORM_EPS {
        let inv = 1.0 / l2.sqrt();
        Vec3::new(inv * v.x, v.y * inv, v.z * inv)
    } else {
        Vec3::Z
    };
    let r = s.r as f32;
    ptres[0] = Vec3::new(n.x * r + pp.x, n.y * r + pp.y, n.z * r + pp.z);
}

/// `h = (e·a)` with `e = (float)(q − c)` (double), the axis coordinate of a segment point.
fn axis_coord(s: &CylSetup, q: Vec3) -> f64 {
    let e = sub_round(wide(q), s.c);
    let a = s.a;
    (e.x * a.x + e.y * a.y) + e.z * a.z
}

/// `n·r` from `v` normalised in double (`1/sqrtsd`), `(0,0,1)` when `|v|² ≤ (double)1e-20f`;
/// `|v|² = (v.y² + v.x²) + v.z²`.
fn dnormal(v: Vec3d) -> Vec3d {
    let l2 = (v.y * v.y + v.x * v.x) + v.z * v.z;
    if l2 > NORM_EPS_D {
        let inv = 1.0 / l2.sqrt();
        Vec3d::new(inv * v.x, v.y * inv, v.z * inv)
    } else {
        Vec3d::new(0.0, 0.0, 1.0)
    }
}

/// Final selection of the point paths: the end point whose surface point is closer
/// (sign bit of `|res1 − p1|² − |res0 − p0|²`, only when the segment is real), rounded to
/// single.
fn pick_end(s: &CylSetup, res: &[Vec3d; 2], ptres: &mut [Vec3; 2]) {
    let (p0, p1) = (s.pd[0], s.pd[1]);
    let e0 = res[0] - p0;
    let e1 = res[1] - p1;
    let d0 = (e0.y * e0.y + e0.x * e0.x) + e0.z * e0.z;
    let d1 = (e1.y * e1.y + e1.x * e1.x) + e1.z * e1.z;
    let k = (sbd(d1 - d0) as usize) & s.seg;
    ptres[0] = res[k].to_f32();
    ptres[1] = s.pd[k].to_f32();
}

/// Capsule slot 11 (0x3513FCA0). Neither `iPrim` nor `iFeature` is written.
///
/// Set-up in `cyl_setup` (`ptres[1] = ptdst0` first). For a real segment:
/// 1. `line_line`: when the closest points of the infinite lines are interior to both the
///    segment and the axis and the lines pass outside the radius, `ptres[1] = (float)(p0 +
///    (t/nn)·Δ)` and the axis coordinate `h` is recomputed from that rounded point
///    (`axis_coord`); `axis_tail` finishes.
/// 2. Else for each cap `s = −1, +1`: the projection `t` of the cap centre `c + a·s·hh` onto
///    the segment; if interior (`| len2 − 2t | < len2`) and
///    `(h0·len2 + (Δ·a)·t)·s > len2·hh` (`h0 = a·(p0 − c)` in **single** precision) the segment
///    point `p0 + (t/len2)·Δ` is used with `h = s·hh` (`axis_tail`).
/// 3. Else (and for a degenerate segment) the point path: each end point is projected onto the
///    capsule surface in double (side: radial push from the axis point; beyond `|h| ≥ hh`,
///    tested on `|(float)h|`: from the cap sphere centre `c + a·sgnnz(h)·hh`), and
///    `pick_end` chooses.
#[allow(clippy::too_many_arguments)]
pub fn capsule_find_closest_point(
    d: &CylinderData,
    gwd: &GeomWorldData,
    _i_prim: &mut i32,
    _i_feature: &mut i32,
    ptdst0: Vec3,
    ptdst1: Vec3,
    ptres: &mut [Vec3; 2],
    _n_max_iters: i32,
) -> i32 {
    let s = cyl_setup(d, gwd, ptdst0, ptdst1);
    ptres[1] = ptdst0;
    let (a, c, hh, len2, dp) = (s.a, s.c, s.hh, s.len2, s.dp);
    if s.seg != 0 {
        let (ok, t, nn, _) = line_line(&s);
        if ok {
            ptres[1] = seg_point(&s, t / nn);
            let h = axis_coord(&s, ptres[1]);
            axis_tail(&s, h, ptres);
            return 1;
        }
        let p0 = s.pd[0];
        for side in [-1i32, 1] {
            let sd = f64::from(side);
            let sh = sd * hh;
            let t = (((a.y * sh + c.y) - p0.y) * dp.y + ((sh * a.x + c.x) - p0.x) * dp.x)
                + ((a.z * sh + c.z) - p0.z) * dp.z;
            if sbd((len2 - t * 2.0).abs() - (-len2).abs()) == 0 {
                continue;
            }
            let e = sub_round(p0, c).to_f32();
            let af = a.to_f32();
            let h0 = (af.x * e.x + af.y * e.y) + af.z * e.z;
            let ad = (dp.x * a.x + dp.y * a.y) + dp.z * a.z;
            if (f64::from(h0) * len2 + ad * t) * sd > len2 * hh {
                ptres[1] = seg_point(&s, t / len2);
                axis_tail(&s, sd * hh, ptres);
                return 1;
            }
        }
    }
    let mut res = [Vec3d::ZERO; 2];
    for (p, out) in res.iter_mut().enumerate().take(s.seg + 1) {
        let pt = s.pd[p];
        let dd = pt - c;
        let h = (dd.y * a.y + dd.x * a.x) + dd.z * a.z;
        if f64::from((h as f32).abs()) >= hh {
            let hs = f64::from(sgnnz(h as f32)) * hh;
            let cc = Vec3d::new(hs * a.x + c.x, a.y * hs + c.y, a.z * hs + c.z);
            let n = dnormal(pt - cc);
            *out = Vec3d::new(n.x * s.r + cc.x, n.y * s.r + cc.y, n.z * s.r + cc.z);
        } else {
            let u = Vec3d::new(dd.x - h * a.x, dd.y - a.y * h, dd.z - a.z * h);
            let n = dnormal(u);
            *out = Vec3d::new(
                (pt.x - u.x) + n.x * s.r,
                (pt.y - u.y) + n.y * s.r,
                (pt.z - u.z) + n.z * s.r,
            );
        }
    }
    pick_end(&s, &res, ptres);
    1
}

/// Cylinder slot 11 (0x35145CC0). Neither `iPrim` nor `iFeature` is written.
///
/// Set-up as the capsule (`cyl_setup`, `ptres[1] = ptdst0`). For a real segment:
/// 1. `line_line` exactly as the capsule (side wall vs. segment interior).
/// 2. Else the rim of the cap on the side `sg = sgnnz((float)s)` of the axis parameter `s`:
///    with `cc = c + a·sg·hh`, `w = (float)(p0 − cc)`, `Δ⊥ = Δ − a(Δ·a)`, `w⊥ = w − a(w·a)`,
///    the squared distance from the rim circle along the segment gives the quartic
///    `c4 t⁴ + … + c0` (coefficients below, built in double); if the Sturm count on `[0, 1]`
///    ([`sturm_nroots`]) is non-zero its roots ([`findroots`], degree 4, `[0, 1]`) are tried
///    from the **largest** down: a root is accepted when `(t·|Δ⊥|² + w⊥·Δ)·(t·len2 + w·Δ) > 0`
///    (strictly: in an exactly symmetric configuration the first factor is 0 at the true
///    minimum and the root is rejected, quirk kept) and the point `u = w + tΔ` lies outside the
///    radius and beyond the cap plane (sign bits of `r² − |u × a|²` and `(u·a)·(−sg)`). Then `ptres[1] = (float)(u + cc)`,
///    `ptres[0] = (float)(cc + r·normalize(u − a(u·a)))` in double.
/// 3. Else the point path: per end point in double, inside the slab (`|(float)h| < hh`) a
///    radial push onto the side wall; beyond it, a projection onto the cap plane when inside
///    the radius (`|d × a|² < r²`, sign of `h` as `sgn` with 0 for `±0`) or onto the rim;
///    `pick_end` chooses.
#[allow(clippy::too_many_arguments)]
pub fn cylinder_find_closest_point(
    d: &CylinderData,
    gwd: &GeomWorldData,
    _i_prim: &mut i32,
    _i_feature: &mut i32,
    ptdst0: Vec3,
    ptdst1: Vec3,
    ptres: &mut [Vec3; 2],
    _n_max_iters: i32,
) -> i32 {
    let s = cyl_setup(d, gwd, ptdst0, ptdst1);
    ptres[1] = ptdst0;
    let (a, c, hh, r) = (s.a, s.c, s.hh, s.r);
    let r2 = r * r;
    if s.seg != 0 {
        let (ok, t, nn, sa) = line_line(&s);
        if ok {
            ptres[1] = seg_point(&s, t / nn);
            let h = axis_coord(&s, ptres[1]);
            axis_tail(&s, h, ptres);
            return 1;
        }
        if rim(&s, sa, r2, ptres) {
            return 1;
        }
    }
    let mut res = [Vec3d::ZERO; 2];
    for (p, out) in res.iter_mut().enumerate().take(s.seg + 1) {
        let pt = s.pd[p];
        let dd = pt - c;
        let h = (a.x * dd.x + dd.y * a.y) + dd.z * a.z;
        if f64::from((h as f32).abs()) >= hh {
            let cx = dd.y * a.z - dd.z * a.y;
            let cy = dd.z * a.x - a.z * dd.x;
            let cz = a.y * dd.x - dd.y * a.x;
            let sgn = f64::from(sgn3(h as f32));
            if (cy * cy + cx * cx) + cz * cz >= r2 {
                let u = Vec3d::new(dd.x - h * a.x, dd.y - a.y * h, dd.z - a.z * h);
                let n = dnormal(u);
                let hs = sgn * hh;
                *out = Vec3d::new(
                    (hs * a.x + c.x) + n.x * r,
                    (a.y * hs + c.y) + n.y * r,
                    (a.z * hs + c.z) + n.z * r,
                );
            } else {
                let k = sgn * hh - h;
                *out = Vec3d::new(k * a.x + pt.x, a.y * k + pt.y, a.z * k + pt.z);
            }
        } else {
            let u = Vec3d::new(dd.x - a.x * h, dd.y - a.y * h, dd.z - a.z * h);
            let n = dnormal(u);
            *out = Vec3d::new(
                (pt.x - u.x) + n.x * r,
                (pt.y - u.y) + n.y * r,
                (pt.z - u.z) + n.z * r,
            );
        }
    }
    pick_end(&s, &res, ptres);
    1
}

/// The three-valued sign the cylinder point path uses for the cap side:
/// `((b − 1) >> 31) + (b >> 31) + 1` on the float's bits (wrapping): 1 for positive, −1 for
/// negative, 0 for `+0.0` **and** `-0.0`.
#[inline]
fn sgn3(x: f32) -> i32 {
    let b = x.to_bits() as i32;
    ((b.wrapping_sub(1)) >> 31) + (b >> 31) + 1
}

/// Step 2 of [`cylinder_find_closest_point`]: the segment against the cap rim through the
/// quartic. Returns true when a root was accepted (`ptres` written).
///
/// Coefficients (all double, association as in the binary):
/// `L = |Δ|²`, `l⊥ = L − (Δ·a)²`, `wd = w·Δ`, `wpd = w⊥·Δ`, `wp2 = |w⊥|²`,
/// `v94 = w⊥·Δ⊥`, `v98 = |Δ⊥|²`, `v99 = wd·L + wd·L`, `A = wpd·r`, `B = l⊥·r`:
/// `c4 = L²·v98`, `c3 = L²·2v94 + v99·v98`, `c2 = ((L²·wp2 + v99·2v94) + wd²·v98) − B²`,
/// `c1 = (v99·wp2 + wd²·2v94) − (AB + AB)`, `c0 = wd²·wp2 − A²`.
fn rim(s: &CylSetup, sa: f64, r2: f64, ptres: &mut [Vec3; 2]) -> bool {
    let (a, c, hh, r, len2, dp) = (s.a, s.c, s.hh, s.r, s.len2, s.dp);
    let sg = sgnnz(sa as f32);
    let da = (dp.y * a.y + dp.x * a.x) + dp.z * a.z;
    let dperp = Vec3d::new(dp.x - da * a.x, dp.y - da * a.y, dp.z - da * a.z);
    let hs = f64::from(sg) * hh;
    let lperp = len2 - da * da;
    let cc = Vec3d::new(hs * a.x + c.x, hs * a.y + c.y, hs * a.z + c.z);
    let w = sub_round(s.pd[0], cc);
    let wa = (w.y * a.y + w.x * a.x) + w.z * a.z;
    let wp = Vec3d::new(w.x - wa * a.x, w.y - wa * a.y, w.z - wa * a.z);
    let wd = (w.x * dp.x + w.y * dp.y) + w.z * dp.z;
    let wpd = (wp.x * dp.x + wp.y * dp.y) + wp.z * dp.z;
    let wp2 = (wp.y * wp.y + wp.x * wp.x) + wp.z * wp.z;
    let v94 = (wp.y * dperp.y + wp.x * dperp.x) + wp.z * dperp.z;
    let v96 = v94 * 2.0;
    let v98 = (dperp.y * dperp.y + dperp.x * dperp.x) + dperp.z * dperp.z;
    let v99 = wd * len2 + wd * len2;
    let ll = len2 * len2;
    let wd2 = wd * wd;
    let am = wpd * r;
    let bm = lperp * r;
    let ab = am * bm;
    let mut p = Poly::one();
    p.c[0] = wd2 * wp2 - am * am;
    p.c[1] = (v99 * wp2 + wd2 * v96) - (ab + ab);
    p.c[2] = ((ll * wp2 + v99 * v96) + wd2 * v98) - bm * bm;
    p.c[3] = ll * v96 + v99 * v98;
    p.c[4] = ll * v98;
    if sturm_nroots(&p, 0.0, 1.0) == 0 {
        return false;
    }
    let mut roots = [0f64; 4];
    let n = findroots(&p, 0.0, 1.0, &mut roots, 4, false);
    let msg = f64::from(-sg);
    for idx in (0..n.max(0) as usize).rev() {
        let t = roots[idx];
        if !((t * lperp + wpd) * (t * len2 + wd) > 0.0) {
            continue;
        }
        let u = Vec3d::new(t * dp.x + w.x, t * dp.y + w.y, t * dp.z + w.z);
        let cx = u.y * a.z - u.z * a.y;
        let cy = u.z * a.x - u.x * a.z;
        let cz = u.x * a.y - u.y * a.x;
        let k = r2 - ((cy * cy + cx * cx) + cz * cz);
        let m = ((u.x * a.x + u.y * a.y) + u.z * a.z) * msg;
        if sbd(k) & sbd(m) == 0 {
            continue;
        }
        ptres[1] = (u + cc).to_f32();
        let ua = (u.x * a.x + u.y * a.y) + u.z * a.z;
        let v = Vec3d::new(u.x - a.x * ua, u.y - a.y * ua, u.z - a.z * ua);
        let l2 = (v.x * v.x + v.y * v.y) + v.z * v.z;
        let nrm = if l2 > NORM_EPS_D {
            let inv = 1.0 / l2.sqrt();
            Vec3d::new(v.x * inv, v.y * inv, v.z * inv)
        } else {
            Vec3d::new(0.0, 0.0, 1.0)
        };
        ptres[0] = Vec3d::new(cc.x + nrm.x * r, nrm.y * r + cc.y, nrm.z * r + cc.z).to_f32();
        return true;
    }
    false
}
