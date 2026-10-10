//! P2b work group `rot_tri`: rotational (mode 1) unprojection of triangles and rays.
//!
//! | cell | x64 | x86 | |
//! |---|---|---|---|
//! | `[1][tri][tri]` | 0x3519A730 (11 259 B) | 0x351A59A0 | [`unproject_tri_tri_rot`] |
//! | `[1][ray][tri]` | 0x3519D340 (6 770 B) | 0x351A8CC0 | [`unproject_ray_tri_rot`] |
//! | `[1][tri][ray]` | 0x3519EDC0 (447 B) | 0x351AABD0 | 447-byte rotational swap wrapper of ray–tri ([`common::swap_rot`]; its disassembly is instruction-for-instruction the one of 0x351A0A40 apart from the callee) |
//!
//! The mesh / heightfield ray path dispatches `[mode][tri][ray]` (index `64·mode + 11`).
//!
//! **Rotational unprojection** (`geometry.md` §5.2): `params.dir` is the unit rotation axis `a`,
//! `params.center` the centre `c`, `params.tmax` = sin of the largest angle (≤ π/2). A is rotated
//! about (c, a) by φ; the routines look for the smallest φ (kept as a rational `num/den` =
//! sin φ, starting at `tmax/1`) at which a feature of the moving primitive touches a feature of the
//! other one, and return `t = sin φ`, `cos_t = √(1 − t²)` (doubles), the contact point, the normal
//! and the feature ids. Nothing is written on failure (return 0), `params` is never written.
//!
//! Every point `p` of the moving primitive is decomposed relative to the centre (in double, from
//! the float difference `p − c`): axial part `h = ((p−c)·a)·a`, radial part `r = (p−c) − h`, and
//! `a × r`. Rotated by φ it is `c + h + r·cos φ + (a×r)·sin φ`. A vertex touching a plane `n·x = d`
//! gives `C + A·cos φ + B·sin φ = 0` with `A = n·r`, `B = n·(a×r)`, `C = n·(c + h − p0)`; it is
//! solved for `sin φ` as the quadratic with `D = A² + B²`, `x = −CB ∓ √((CB)² − (C² − A²)·D)`
//! (`sin φ = x/D`, `cos φ = √(D² − x²)/D`). A root is accepted when (all from float sign bits)
//! `0 < x < D` (`|2x − D| − D < 0`), `cos φ > 0` (`(xB + DC)·A < 0`), the touch point lies inside
//! the face (edge cross products against the face normal, all scaled by D), and `x/D` is below the
//! running best (`den·x − num·D < 0`). Edge–edge contacts use the same quadratic with
//! coefficients built from the edge directions. The best is updated branch-free:
//! `den = (1−m)·den + m·D`, `num = (1−m)·num + m·x` (`m` = 0/1 as double).
//!
//! Feature ids: vertex `0x80|i`, face `0x40`, triangle edge / ray `0xA0|i` (the ray is 0xA0).
//! The input feature ids **are read**: a pair equal to `featA | featB<<8` marks the contact that
//! is being unprojected from (see the functions).
//!
//! No static state, no reads beyond the documented primitive / parameter fields, no area output
//! (the 7th argument is ignored by both pair functions).

use super::common::{
    as_ray, as_tri, lt_or_nan, lt_or_nan_d, swap_rot, ucomisd_eq, DEC_MOD3, INC_MOD3,
};
use super::{UnprojTables, MODE_ROTATIONAL};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{Primitive, RayPrim, Triangle};
use crate::ids::GeomType;
use crate::math::Vec3;

type V3 = [f64; 3];

#[inline]
fn d(x: f32) -> f64 {
    f64::from(x)
}

/// Sign bit of a float (as the binary's `shr 31`).
#[inline]
fn sb(x: f32) -> u32 {
    x.to_bits() >> 31
}

#[inline]
fn vf(v: Vec3) -> [f32; 3] {
    [v.x, v.y, v.z]
}

/// `abs` of a double (`andpd` with 0x7FFF…).
#[inline]
fn fabs(x: f64) -> f64 {
    f64::from_bits(x.to_bits() & 0x7FFF_FFFF_FFFF_FFFF)
}

/// Axial / radial decomposition of the double vector `dv` about the (double) axis `a`, with the
/// dot product summed `(dv.y·a.y + dv.x·a.x) + dv.z·a.z` (both orders of the first pair are
/// bit-identical). Returns `(h, r, a × r)`, `(a×r) = (r.z·a.y − r.y·a.z, r.x·a.z − r.z·a.x,
/// r.y·a.x − r.x·a.y)`.
#[inline]
fn decompose(dv: V3, a: V3) -> (V3, V3, V3) {
    let dot = (dv[1] * a[1] + dv[0] * a[0]) + dv[2] * a[2];
    let h = [dot * a[0], dot * a[1], dot * a[2]];
    let r = [dv[0] - h[0], dv[1] - h[1], dv[2] - h[2]];
    let cr = [
        r[2] * a[1] - r[1] * a[2],
        r[0] * a[2] - r[2] * a[0],
        r[1] * a[0] - r[0] * a[1],
    ];
    (h, r, cr)
}

/// Point-in-face edge term of the vertex roots: `Q = P − (double)(pt_i·Df)` (float product),
/// `e = pt_j − pt_i` (float), result
/// `(float)((n.x·(e.z·Q.y − e.y·Q.z) + n.y·(e.x·Q.z − Q.x·e.z)) + n.z·(Q.x·e.y − e.x·Q.y))`.
#[inline]
fn edge_side(pp: V3, df: f32, pi: [f32; 3], pj: [f32; 3], n: V3) -> f32 {
    let q = [
        pp[0] - d(pi[0] * df),
        pp[1] - d(pi[1] * df),
        pp[2] - d(pi[2] * df),
    ];
    let e = [d(pj[0] - pi[0]), d(pj[1] - pi[1]), d(pj[2] - pi[2])];
    let x = e[2] * q[1] - e[1] * q[2];
    let y = e[0] * q[2] - q[0] * e[2];
    let z = q[0] * e[1] - e[0] * q[1];
    ((n[0] * x + n[1] * y) + n[2] * z) as f32
}

/// Branch-free best update shared by all root checks: `m` = 0/1.
#[inline]
fn blend(m: u32, num: &mut f64, den: &mut f64, x: f64, dd: f64) {
    let mf = f64::from(m as i32);
    let nm = f64::from((m ^ 1) as i32);
    *den = nm * *den + mf * dd;
    *num = nm * *num + mf * x;
}

/// `(int)(bits >> 30 (arith) & ~1) + 1`: −1 for a negative sign bit, +1 otherwise.
#[inline]
fn sign_pm1(x: f32) -> f64 {
    f64::from(((x.to_bits() as i32) >> 30 & !1) + 1)
}

// =============================================================================== ray – tri

/// sub_3519D340 (x64; x86 0x351A8CC0): rotate the ray segment (A, origin `o`, end `o + dir`)
/// about (c, a) until it touches triangle B.
///
/// 1. **Ray ends against the face plane** (end first, then origin; the origin is skipped when
///    `|o − c|² < min_pt_dist²` in float — `comiss`/`jb`, so NaN also skips): the vertex
///    quadratic with the triangle normal, root accepted if the rotated point is inside the
///    triangle (three edge terms `< 0`, no inflation) and better than the best. Result:
///    `pt` = rotated end `c + ((r·cos + h) + (a×r)·sin)`, `n = −tri.n`, features
///    `(0x80|end, 0x40)` (end 0 = origin, 1 = origin + dir).
/// 2. Only if no end hit: **triangle edges against the rotating ray line** (edge `i` is skipped
///    when `featA | featB<<8 == 0xA0 | (0xA0|i)<<8`): with `m = (o−c)×dir` (float), `k = (pt_i −
///    c)×e_i` decomposed about the axis, the coefficients `G` (cos), `H` (sin), `K` are mixed
///    float/double dot products; a root is accepted when the edge parameter of the closest point
///    of the rotated ray line and the ray parameter both lie in (0, 1) (`|2s − uu| − uu < 0`
///    forms) and it beats the best. Result: `pt` = closest point on the rotated ray line to the
///    edge line, `n = ±(L × e)` (L = rotated ray vector) oriented by the sign of
///    `(L×e)·(n × e)` (floats; −1 if negative), features `(0xA0, 0xA0|i)`.
///
/// Returns 0 (nothing written) when nothing was found. `t = 0` if the final `den` is 0 (or NaN:
/// `ucomisd`/`je`).
pub fn unproject_ray_tri_rot(
    p: &mut UnprojParams,
    ray: &RayPrim,
    fa: i32,
    tri: &Triangle,
    fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let feat_in = fa | fb.wrapping_shl(8);
    let ax = [d(p.dir.x), d(p.dir.y), d(p.dir.z)];
    let c = vf(p.center);
    let o = vf(ray.origin);
    let rd = vf(ray.dir);
    let pt = [vf(tri.pt[0]), vf(tri.pt[1]), vf(tri.pt[2])];
    let nf = vf(tri.n);
    let n = [d(nf[0]), d(nf[1]), d(nf[2])];

    let mut num = d(p.tmax);
    let mut den = 1.0f64;
    let mut best: i32 = -1;

    // end points: 0 = origin, 1 = origin + dir
    let d0 = [d(o[0] - c[0]), d(o[1] - c[1]), d(o[2] - c[2])];
    let (h0, r0, cr0) = decompose(d0, ax);
    let d1 = [d(rd[0]) + d0[0], d(rd[1]) + d0[1], d(rd[2]) + d0[2]];
    let (h1, r1, cr1) = decompose(d1, ax);
    let hh = [h0, h1];
    let rr = [r0, r1];
    let cc = [cr0, cr1];

    let dist2 = ((o[1] - c[1]) * (o[1] - c[1]) + (o[0] - c[0]) * (o[0] - c[0]))
        + (o[2] - c[2]) * (o[2] - c[2]);
    let mpd2 = p.min_pt_dist * p.min_pt_dist;

    let mut v = 1i32;
    loop {
        let vi = v as usize;
        let (h, r, cr) = (hh[vi], rr[vi], cc[vi]);
        let a_ = (n[1] * r[1] + n[0] * r[0]) + n[2] * r[2];
        let b_ = (n[1] * cr[1] + n[0] * cr[0]) + n[2] * cr[2];
        let c_ = (n[0] * (d(c[0]) + (h[0] - d(pt[0][0])))
            + n[1] * (d(c[1]) + (h[1] - d(pt[0][1]))))
            + n[2] * (d(c[2]) + (h[2] - d(pt[0][2])));
        let dd = b_ * b_ + a_ * a_;
        let cb = c_ * b_;
        let disc = cb * cb - (c_ * c_ - a_ * a_) * dd;
        if !lt_or_nan_d(disc, 0.0) {
            let sq = disc.sqrt();
            let mut x = -cb - sq;
            let two_sq = sq * 2.0;
            let dc = dd * c_;
            for _ in 0..2 {
                let f1 = (fabs(x * 2.0 - dd) - dd) as f32;
                let f2 = ((x * b_ + dc) * a_) as f32;
                if ((f1.to_bits() & f2.to_bits()) as i32) < 0 {
                    let df = dd as f32;
                    let cn = (dd * dd - x * x).sqrt();
                    let pp = [
                        (r[0] * cn + cr[0] * x) + (d(c[0]) + h[0]) * dd,
                        (r[1] * cn + cr[1] * x) + (d(c[1]) + h[1]) * dd,
                        (r[2] * cn + cr[2] * x) + (d(c[2]) + h[2]) * dd,
                    ];
                    let s0 = edge_side(pp, df, pt[0], pt[1], n);
                    let s1 = edge_side(pp, df, pt[1], pt[2], n);
                    let s2 = edge_side(pp, df, pt[2], pt[0], n);
                    let s3 = (den * x - num * dd) as f32;
                    let m = sb(s3) & sb(s2) & sb(s1) & sb(s0);
                    best = (-(m as i32) & v) | ((m as i32 - 1) & best);
                    blend(m, &mut num, &mut den, x, dd);
                }
                x += two_sq;
            }
        }
        // comiss dist2, mpd2 / jb: skip the origin when it is (nearly) the centre
        if lt_or_nan(dist2, mpd2) {
            break;
        }
        v -= 1;
        if v < 0 {
            break;
        }
    }

    if best >= 0 {
        let e = (best & 1) as usize;
        let t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
        out.t = t;
        let cs = (1.0 - t * t).sqrt();
        out.cos_t = cs;
        let (h, r, cr) = (hh[e], rr[e], cc[e]);
        out.pt = Vec3::new(
            (d(c[0]) + ((r[0] * cs + h[0]) + cr[0] * t)) as f32,
            (d(c[1]) + ((r[1] * cs + h[1]) + cr[1] * t)) as f32,
            (d(c[2]) + ((r[2] * cs + h[2]) + cr[2] * t)) as f32,
        );
        out.n = Vec3::new(-nf[0], -nf[1], -nf[2]);
        out.i_feature = [e as i32 | 0x80, 0x40];
        return 1;
    }

    // ---- triangle edges against the rotating ray line
    let dxo = o[0] - c[0];
    let dyo = o[1] - c[1];
    let dzo = o[2] - c[2];
    let m = [
        d(rd[2] * dyo - rd[1] * dzo),
        d(rd[0] * dzo - dxo * rd[2]),
        d(dxo * rd[1] - rd[0] * dyo),
    ];
    let dm = (m[0] * ax[0] + m[1] * ax[1]) + m[2] * ax[2];
    let hm = [dm * ax[0], dm * ax[1], dm * ax[2]];
    let rm = [m[0] - hm[0], m[1] - hm[1], m[2] - hm[2]];
    let crm = [
        rm[2] * ax[1] - rm[1] * ax[2],
        ax[2] * rm[0] - rm[2] * ax[0],
        rm[1] * ax[0] - ax[1] * rm[0],
    ];
    for i in 0..3usize {
        if ((i as i32) << 8 | 0xA0A0) == feat_in {
            continue;
        }
        let pi = pt[i];
        let pj = pt[INC_MOD3[i] as usize];
        let dp = [d(pi[0] - c[0]), d(pi[1] - c[1]), d(pi[2] - c[2])];
        let e = [d(pj[0] - pi[0]), d(pj[1] - pi[1]), d(pj[2] - pi[2])];
        let k = [
            e[2] * dp[1] - e[1] * dp[2],
            dp[2] * e[0] - e[2] * dp[0],
            e[1] * dp[0] - dp[1] * e[0],
        ];
        let (hk, rk, crk) = decompose(k, ax);
        let f1 = ((rk[1] as f32) * rd[1] + rd[0] * (rk[0] as f32)) + (rk[2] as f32) * rd[2];
        let g_ = d(f1) + ((e[1] * rm[1] + e[0] * rm[0]) + e[2] * rm[2]);
        let f2 = ((crk[1] as f32) * rd[1] + rd[0] * (crk[0] as f32)) + (crk[2] as f32) * rd[2];
        let h_ = ((e[1] * crm[1] + e[0] * crm[0]) + e[2] * crm[2]) - d(f2);
        let f3 = ((hk[1] as f32) * rd[1] + rd[0] * (hk[0] as f32)) + (hk[2] as f32) * rd[2];
        let k_ = d(f3) + ((e[1] * hm[1] + hm[0] * e[0]) + e[2] * hm[2]);
        let dd = h_ * h_ + g_ * g_;
        let kh = k_ * h_;
        let disc = kh * kh - (k_ * k_ - g_ * g_) * dd;
        if lt_or_nan_d(disc, 0.0) {
            continue;
        }
        let sq = disc.sqrt();
        let mut x = -kh - sq;
        let two_sq = sq * 2.0;
        let dk = dd * k_;
        for _ in 0..2 {
            let fa_ = (fabs(x * 2.0 - dd) - dd) as f32;
            let fb_ = ((x * h_ + dk) * g_) as f32;
            if ((fa_.to_bits() & fb_.to_bits()) as i32) < 0 {
                let cn = (dd * dd - x * x).sqrt();
                let p0 = [
                    (r0[0] * cn + cr0[0] * x) + h0[0] * dd,
                    (r0[1] * cn + cr0[1] * x) + h0[1] * dd,
                    (r0[2] * cn + cr0[2] * x) + h0[2] * dd,
                ];
                let p1 = [
                    (r1[0] * cn + cr1[0] * x) + h1[0] * dd,
                    (r1[1] * cn + cr1[1] * x) + h1[1] * dd,
                    (r1[2] * cn + cr1[2] * x) + h1[2] * dd,
                ];
                let l = [p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]];
                let (s1, s2, uu) = line_terms(l, e, dp, p0, dd);
                let ga = (fabs(s1 * 2.0 - uu) - uu) as f32;
                let ud = uu * dd;
                let gb = (fabs(s2 * 2.0 - ud) - ud) as f32;
                let gc = (den * x - num * dd) as f32;
                let mk = sb(ga) & sb(gb) & sb(gc);
                best = (-(mk as i32) & (i as i32 | 0x80)) | ((mk as i32 - 1) & best);
                blend(mk, &mut num, &mut den, x, dd);
            }
            x += two_sq;
        }
    }
    if best < 0 {
        return 0;
    }
    let ie = (best & 3) as usize;
    let t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
    out.t = t;
    let cs = (1.0 - t * t).sqrt();
    out.cos_t = cs;
    let q0 = [
        d(c[0]) + (h0[0] + (r0[0] * cs + cr0[0] * t)),
        d(c[1]) + ((r0[1] * cs + cr0[1] * t) + h0[1]),
        d(c[2]) + ((r0[2] * cs + cr0[2] * t) + h0[2]),
    ];
    let q1 = [
        d(c[0]) + (h1[0] + (r1[0] * cs + cr1[0] * t)),
        d(c[1]) + ((r1[1] * cs + cr1[1] * t) + h1[1]),
        d(c[2]) + ((r1[2] * cs + cr1[2] * t) + h1[2]),
    ];
    let pi = pt[ie];
    let pj = pt[INC_MOD3[ie] as usize];
    edge_edge_result(out, q0, q1, pi, pj, nf);
    out.i_feature = [0xA0, ie as i32 | 0xA0];
    1
}

/// The two in-range terms of an edge–edge root (shared by ray–tri and tri–tri, identical
/// operation order): `u = L × e`, `W = d·D − P0`;
/// `S1 = ((W×e).y·u.y + (W×e).x·u.x) + (W×e).z·u.z`, `S2` the same with `W×L`,
/// `uu = (u.y² + u.x²) + u.z²`.
#[inline]
fn line_terms(l: V3, e: V3, dv: V3, p0: V3, dd: f64) -> (f64, f64, f64) {
    let u = [
        l[1] * e[2] - l[2] * e[1],
        e[0] * l[2] - l[0] * e[2],
        l[0] * e[1] - e[0] * l[1],
    ];
    let w = [dv[0] * dd - p0[0], dv[1] * dd - p0[1], dv[2] * dd - p0[2]];
    let x = w[1] * e[2] - w[2] * e[1];
    let y = e[0] * w[2] - w[0] * e[2];
    let z = w[0] * e[1] - e[0] * w[1];
    let s1 = (y * u[1] + x * u[0]) + z * u[2];
    let uu = (u[1] * u[1] + u[0] * u[0]) + u[2] * u[2];
    let x2 = w[1] * l[2] - w[2] * l[1];
    let y2 = l[0] * w[2] - w[0] * l[2];
    let z2 = w[0] * l[1] - l[0] * w[1];
    let s2 = (y2 * u[1] + x2 * u[0]) + z2 * u[2];
    (s1, s2, uu)
}

/// Final contact of an edge–edge root (identical in ray–tri and tri–tri): the rotated moving
/// segment `q0 → q1` (doubles, world space) against the static edge `pi → pj` (floats) with face
/// normal `nf`. `c = L × e`, `V = (float)(pi − q0)`, `s = ((V×e)·c)/|c|²` (0 if `|c|² == 0` or
/// NaN), `pt = q0 + L·s`, `n = c·(±1)` with the sign of `(c·(n×e))` in floats.
fn edge_edge_result(
    out: &mut UnprojResult,
    q0: V3,
    q1: V3,
    pi: [f32; 3],
    pj: [f32; 3],
    nf: [f32; 3],
) {
    let l = [q1[0] - q0[0], q1[1] - q0[1], q1[2] - q0[2]];
    let ef = [pj[0] - pi[0], pj[1] - pi[1], pj[2] - pi[2]];
    let e = [d(ef[0]), d(ef[1]), d(ef[2])];
    let vv = [
        d((d(pi[0]) - q0[0]) as f32),
        d((d(pi[1]) - q0[1]) as f32),
        d((d(pi[2]) - q0[2]) as f32),
    ];
    let cx = e[2] * l[1] - e[1] * l[2];
    let cy = l[2] * e[0] - e[2] * l[0];
    let cz = e[1] * l[0] - l[1] * e[0];
    let x = vv[1] * e[2] - vv[2] * e[1];
    let y = vv[2] * e[0] - e[2] * vv[0];
    let z = e[1] * vv[0] - vv[1] * e[0];
    let num = (y * cy + x * cx) + z * cz;
    let den = (cy * cy + cx * cx) + cz * cz;
    let s = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
    out.pt = Vec3::new(
        (s * l[0] + q0[0]) as f32,
        (l[1] * s + q0[1]) as f32,
        (l[2] * s + q0[2]) as f32,
    );
    let (exf, eyf, ezf) = (e[0] as f32, e[1] as f32, e[2] as f32);
    let gx = nf[1] * ezf - nf[2] * eyf;
    let gy = nf[2] * exf - ezf * nf[0];
    let gz = eyf * nf[0] - nf[1] * exf;
    let sg = ((cy as f32) * gy + gx * (cx as f32)) + (cz as f32) * gz;
    let k = sign_pm1(sg);
    out.n = Vec3::new((k * cx) as f32, (cy * k) as f32, (cz * k) as f32);
}

// =============================================================================== tri – tri

/// sub_3519A730 (x64; x86 0x351A59A0): rotate triangle A about (c, a) until it touches B.
///
/// Every vertex of both triangles is decomposed about the centre; B's with the **negated** axis
/// (B moves by −φ relative to A).
///
/// 1. **Vertex–face**, for face triangle F = A then B and each vertex `j` of the other triangle
///    V: if `featA | featB<<8` equals the pair (face 0x40 of F, vertex 0x80|j of V) — the contact
///    being unprojected from — the vertex is accepted at angle 0 (`num = 0`, `den = 1`, with no
///    "better" test) when both neighbouring vertices of V are on the outer side,
///    `n·(v − F.pt0) + min_pt_dist ≥ 0` (sign bits of the float sums). Otherwise the vertex quadratic
///    with F's normal; a root needs the rotated vertex inside F (three edge terms), both rotated
///    neighbours on the same side of F's plane inflated by `min_pt_dist`
///    (`(n·(P' − p0·D) + mpd·D)·(…) ≥ 0`, positions as `((r·cn + (a×r)·x) + ((c + h) − p0)·D)`),
///    and a better angle. Feature code `4·F | j`.
/// 2. **Edge–edge**, edge `i` of A (moving, +axis) against edge `j` of B: if `featA | featB<<8 ==
///    (0xA0|i) | (0xA0|j)<<8` the pair is accepted at angle 0 when the edges' face normals
///    (`(nA×eA)`, `(nB×eB)`) agree in sign along `eA×eB` and B's edge lies on the outer side of
///    A's rotated edge (float tests). Otherwise the edge quadratic (G cos, H sin, K) with root
///    checks like ray–tri plus the orientation test `((N×L)·u)·((nB×eB)·u) < 0` with A's rotated
///    normal N (`n_A` decomposed, its radial part rounded to float). Code `0x80 | i<<2 | j`.
///
/// Result (if any code was recorded): vertex–face: `pt` = the vertex rotated by its triangle's
/// angle (B's vertex unrotated: the cos factor is `max(cos, 1)` and the sin factor `0·t`),
/// `n` = A's rotated normal (face of A) or `−nB` (face of B), features `[F] = 0x40`,
/// `[V] = 0x80|j`. Edge–edge: as [`unproject_ray_tri_rot`]'s edge result with A's rotated edge,
/// features `(0xA0|i, 0xA0|j)`.
pub fn unproject_tri_tri_rot(
    p: &mut UnprojParams,
    a: &Triangle,
    fa: i32,
    b: &Triangle,
    fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let feat_in = fa | fb.wrapping_shl(8);
    let c = vf(p.center);
    let pts = [
        [vf(a.pt[0]), vf(a.pt[1]), vf(a.pt[2])],
        [vf(b.pt[0]), vf(b.pt[1]), vf(b.pt[2])],
    ];
    let ns = [vf(a.n), vf(b.n)];
    let mpd = p.min_pt_dist;

    // ---- decomposition of all six vertices (B with the axis multiplied by −1.0)
    let mut hh = [[[0.0f64; 3]; 3]; 2];
    let mut rr = [[[0.0f64; 3]; 3]; 2];
    let mut cc = [[[0.0f64; 3]; 3]; 2];
    let mut ax = [d(p.dir.x), d(p.dir.y), d(p.dir.z)];
    for t in 0..2 {
        for j in 0..3 {
            let q = pts[t][j];
            let dv = [d(q[0] - c[0]), d(q[1] - c[1]), d(q[2] - c[2])];
            // (dx·ax + dy·ay) + dz·az: same bits as `decompose`'s pair order
            let (h, r, cr) = decompose(dv, ax);
            hh[t][j] = h;
            rr[t][j] = r;
            cc[t][j] = cr;
        }
        // `mulsd` by −1.0, not a sign flip (differs for NaN: the NaN keeps its sign)
        #[allow(clippy::neg_multiply)]
        {
            ax = [ax[0] * -1.0, ax[1] * -1.0, ax[2] * -1.0];
        }
    }

    let mut best: i32 = -1;
    let mut num = d(p.tmax);
    let mut den = 1.0f64;

    // ---- vertex of V against the face of F
    for k in 0..2usize {
        let fp = pts[k];
        let nf = ns[k];
        let n = [d(nf[0]), d(nf[1]), d(nf[2])];
        let vt = k ^ 1;
        for j in 0..3usize {
            let code = (0x40i32 << (8 * k)) | ((j as i32 | 0x80) << (8 * vt));
            let cand = (4 * k + j) as i32;
            if code == feat_in {
                // Oddity kept: the angle-0 acceptance skips the "better than best" test and
                // inflates by min_pt_dist unscaled (the root test below uses mpd·D).
                let q1 = pts[vt][INC_MOD3[j] as usize];
                let e1 = [q1[0] - fp[0][0], q1[1] - fp[0][1], q1[2] - fp[0][2]];
                let g1 = ((e1[1] * nf[1] + nf[0] * e1[0]) + e1[2] * nf[2]) + mpd;
                let q2 = pts[vt][DEC_MOD3[j] as usize];
                let e2 = [q2[0] - fp[0][0], q2[1] - fp[0][1], q2[2] - fp[0][2]];
                let g2 = ((e2[1] * nf[1] + nf[0] * e2[0]) + e2[2] * nf[2]) + mpd;
                let m = (sb(g1) ^ 1) & (sb(g2) ^ 1);
                best = (-(m as i32) & cand) | ((m as i32 - 1) & best);
                blend(m, &mut num, &mut den, 0.0, 1.0);
                continue;
            }
            let (h, r, cr) = (hh[vt][j], rr[vt][j], cc[vt][j]);
            let a_ = (n[1] * r[1] + n[0] * r[0]) + n[2] * r[2];
            let b_ = (n[1] * cr[1] + n[0] * cr[0]) + n[2] * cr[2];
            let c_ = (n[0] * (d(c[0]) + (h[0] - d(fp[0][0])))
                + n[1] * (d(c[1]) + (h[1] - d(fp[0][1]))))
                + n[2] * (d(c[2]) + (h[2] - d(fp[0][2])));
            let dd = b_ * b_ + a_ * a_;
            let cb = c_ * b_;
            let disc = cb * cb - (c_ * c_ - a_ * a_) * dd;
            if lt_or_nan_d(disc, 0.0) {
                continue;
            }
            let sq = disc.sqrt();
            let mut x = -cb - sq;
            let two_sq = sq * 2.0;
            let dc = dd * c_;
            for _ in 0..2 {
                let f1 = (fabs(x * 2.0 - dd) - dd) as f32;
                let f2 = ((x * b_ + dc) * a_) as f32;
                if ((f1.to_bits() & f2.to_bits()) as i32) < 0 {
                    let df = dd as f32;
                    let cn = (dd * dd - x * x).sqrt();
                    let pp = [
                        (cn * r[0] + x * cr[0]) + dd * (d(c[0]) + h[0]),
                        (cn * r[1] + x * cr[1]) + (d(c[1]) + h[1]) * dd,
                        (cn * r[2] + x * cr[2]) + (d(c[2]) + h[2]) * dd,
                    ];
                    let s0 = edge_side(pp, df, fp[0], fp[1], n);
                    let s1 = edge_side(pp, df, fp[1], fp[2], n);
                    let s2 = edge_side(pp, df, fp[2], fp[0], n);
                    let mpd_d = d(mpd) * dd;
                    let side = |jj: usize| -> f64 {
                        let (h, r, cr) = (hh[vt][jj], rr[vt][jj], cc[vt][jj]);
                        let q = [
                            (r[0] * cn + cr[0] * x) + ((d(c[0]) + h[0]) - d(fp[0][0])) * dd,
                            (r[1] * cn + cr[1] * x) + ((d(c[1]) + h[1]) - d(fp[0][1])) * dd,
                            (r[2] * cn + cr[2] * x) + ((d(c[2]) + h[2]) - d(fp[0][2])) * dd,
                        ];
                        ((n[0] * q[0] + n[1] * q[1]) + n[2] * q[2]) + mpd_d
                    };
                    let g1 = side(INC_MOD3[j] as usize);
                    let g2 = side(DEC_MOD3[j] as usize);
                    let g = (g2 * g1) as f32;
                    let s3 = (den * x - num * dd) as f32;
                    let m = (sb(g) ^ 1) & sb(s3) & sb(s2) & sb(s1) & sb(s0);
                    best = (-(m as i32) & cand) | ((m as i32 - 1) & best);
                    blend(m, &mut num, &mut den, x, dd);
                }
                x += two_sq;
            }
        }
    }

    // ---- edge of A (moving, +axis) against edge of B
    let a0 = [d(p.dir.x), d(p.dir.y), d(p.dir.z)];
    let na = ns[0];
    let nb = ns[1];
    let af = [a0[0] as f32, a0[1] as f32, a0[2] as f32];
    let dn = d((af[1] * na[1] + af[0] * na[0]) + af[2] * na[2]);
    let hn = [dn * a0[0], a0[1] * dn, a0[2] * dn];
    // Oddity kept: the radial part of A's normal is rounded to float (`cvtsd2ss`) and widened
    // again, while its axial part stays double.
    let rn = [
        d((d(na[0]) - hn[0]) as f32),
        d((d(na[1]) - hn[1]) as f32),
        d((d(na[2]) - hn[2]) as f32),
    ];
    let crn = [
        rn[2] * a0[1] - rn[1] * a0[2],
        a0[2] * rn[0] - rn[2] * a0[0],
        rn[1] * a0[0] - a0[1] * rn[0],
    ];
    let pa = pts[0];
    let pb = pts[1];
    for i in 0..3usize {
        let ii = INC_MOD3[i] as usize;
        let da = [d(pa[i][0] - c[0]), d(pa[i][1] - c[1]), d(pa[i][2] - c[2])];
        let ea = [
            d(pa[ii][0] - pa[i][0]),
            d(pa[ii][1] - pa[i][1]),
            d(pa[ii][2] - pa[i][2]),
        ];
        let k = [
            ea[2] * da[1] - ea[1] * da[2],
            ea[0] * da[2] - da[0] * ea[2],
            da[0] * ea[1] - ea[0] * da[1],
        ];
        let (hk, rk, crk) = decompose(k, a0);
        for j in 0..3usize {
            let jj = INC_MOD3[j] as usize;
            let cand = (((i as i32) | 0x20) << 2) | j as i32;
            let ebf = [
                pb[jj][0] - pb[j][0],
                pb[jj][1] - pb[j][1],
                pb[jj][2] - pb[j][2],
            ];
            let eb = [d(ebf[0]), d(ebf[1]), d(ebf[2])];
            if ((j as i32) << 8 | i as i32 | 0xA0A0) == feat_in {
                // contact being unprojected from: accept at angle 0 if the configuration agrees
                // (oddity kept: no "better than best" test, like the vertex–face special case)
                let w = [
                    eb[2] * ea[1] - eb[1] * ea[2],
                    eb[0] * ea[2] - eb[2] * ea[0],
                    eb[1] * ea[0] - eb[0] * ea[1],
                ];
                let eaf = [ea[0] as f32, ea[1] as f32, ea[2] as f32];
                let qa = [
                    na[1] * eaf[2] - na[2] * eaf[1],
                    eaf[0] * na[2] - na[0] * eaf[2],
                    na[0] * eaf[1] - eaf[0] * na[1],
                ];
                let qb = [
                    nb[1] * ebf[2] - nb[2] * ebf[1],
                    ebf[0] * nb[2] - nb[0] * ebf[2],
                    nb[0] * ebf[1] - ebf[0] * nb[1],
                ];
                let sa = (d(qa[0]) * w[0] + d(qa[1]) * w[1]) + d(qa[2]) * w[2];
                let sbv = (d(qb[0]) * w[0] + d(qb[1]) * w[1]) + d(qb[2]) * w[2];
                let f1 = (sbv * sa) as f32;
                let dd_ = [
                    pb[j][0] - pa[i][0],
                    pb[j][1] - pa[i][1],
                    pb[j][2] - pa[i][2],
                ];
                let v = [
                    ebf[2] * dd_[1] - ebf[1] * dd_[2],
                    ebf[0] * dd_[2] - dd_[0] * ebf[2],
                    dd_[0] * ebf[1] - ebf[0] * dd_[1],
                ];
                let tw = ((w[0] as f32) * v[0] + (w[1] as f32) * v[1]) + (w[2] as f32) * v[2];
                let ww = (w[0] * w[0] + w[1] * w[1]) + w[2] * w[2];
                let wwf = ww as f32;
                let daf = [pa[i][0] - c[0], pa[i][1] - c[1], pa[i][2] - c[2]];
                let twd = d(tw);
                let px = (d(daf[0] * wwf) + twd * ea[0]) as f32;
                let py = (d(daf[1] * wwf) + twd * ea[1]) as f32;
                let pz = (d(daf[2] * wwf) + twd * ea[2]) as f32;
                let (axf, ayf, azf) = (p.dir.x, p.dir.y, p.dir.z);
                let xx = ayf * pz - azf * py;
                let yy = azf * px - pz * axf;
                let zz = py * axf - ayf * px;
                let f2 = ((d(qb[0]) * d(xx) + d(yy) * d(qb[1])) + d(zz) * d(qb[2])) as f32;
                let m = sb(f1) & sb(f2);
                best = (-(m as i32) & cand) | ((m as i32 - 1) & best);
                blend(m, &mut num, &mut den, 0.0, 1.0);
                continue;
            }
            let db = [d(pb[j][0] - c[0]), d(pb[j][1] - c[1]), d(pb[j][2] - c[2])];
            let kb = [
                eb[2] * db[1] - eb[1] * db[2],
                eb[0] * db[2] - db[0] * eb[2],
                db[0] * eb[1] - eb[0] * db[1],
            ];
            let (hb, rb, crb) = decompose(kb, a0);
            let g_ = ((eb[0] * rk[0] + eb[1] * rk[1]) + eb[2] * rk[2])
                + ((rb[0] * ea[0] + rb[1] * ea[1]) + rb[2] * ea[2]);
            let h_ = ((eb[0] * crk[0] + eb[1] * crk[1]) + eb[2] * crk[2])
                - ((crb[0] * ea[0] + crb[1] * ea[1]) + crb[2] * ea[2]);
            let k_ = ((eb[0] * hk[0] + eb[1] * hk[1]) + eb[2] * hk[2])
                + ((hb[0] * ea[0] + hb[1] * ea[1]) + hb[2] * ea[2]);
            let dd = h_ * h_ + g_ * g_;
            let kh = k_ * h_;
            let disc = kh * kh - (k_ * k_ - g_ * g_) * dd;
            if lt_or_nan_d(disc, 0.0) {
                continue;
            }
            let sq = disc.sqrt();
            let mut x = -kh - sq;
            let two_sq = sq * 2.0;
            let dk = dd * k_;
            for _ in 0..2 {
                let fa_ = (fabs(x * 2.0 - dd) - dd) as f32;
                let fb_ = ((x * h_ + dk) * g_) as f32;
                if ((fa_.to_bits() & fb_.to_bits()) as i32) < 0 {
                    let cn = (dd * dd - x * x).sqrt();
                    let (h0, r0, c0) = (hh[0][i], rr[0][i], cc[0][i]);
                    let (h1, r1, c1) = (hh[0][ii], rr[0][ii], cc[0][ii]);
                    let p0 = [
                        (r0[0] * cn + c0[0] * x) + h0[0] * dd,
                        (r0[1] * cn + c0[1] * x) + h0[1] * dd,
                        (r0[2] * cn + c0[2] * x) + h0[2] * dd,
                    ];
                    let p1 = [
                        (r1[0] * cn + c1[0] * x) + h1[0] * dd,
                        (r1[1] * cn + c1[1] * x) + h1[1] * dd,
                        (r1[2] * cn + c1[2] * x) + h1[2] * dd,
                    ];
                    let l = [p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]];
                    let u = [
                        l[1] * eb[2] - l[2] * eb[1],
                        eb[0] * l[2] - l[0] * eb[2],
                        l[0] * eb[1] - eb[0] * l[1],
                    ];
                    let (s1, s2, uu) = line_terms(l, eb, db, p0, dd);
                    let ga = (fabs(s1 * 2.0 - uu) - uu) as f32;
                    let ud = uu * dd;
                    let gb = (fabs(s2 * 2.0 - ud) - ud) as f32;
                    // A's rotated face normal (scaled by D)
                    let nn = [
                        (hn[0] * dd + rn[0] * cn) + crn[0] * x,
                        (hn[1] * dd + rn[1] * cn) + crn[1] * x,
                        (hn[2] * dd + rn[2] * cn) + crn[2] * x,
                    ];
                    let cv = [
                        nn[1] * l[2] - nn[2] * l[1],
                        l[0] * nn[2] - nn[0] * l[2],
                        nn[0] * l[1] - l[0] * nn[1],
                    ];
                    let part1 = (cv[1] * u[1] + cv[0] * u[0]) + cv[2] * u[2];
                    let gx = nb[1] * ebf[2] - nb[2] * ebf[1];
                    let gy = ebf[0] * nb[2] - nb[0] * ebf[2];
                    let gz = nb[0] * ebf[1] - ebf[0] * nb[1];
                    let part2 = (d(gy) * u[1] + d(gx) * u[0]) + d(gz) * u[2];
                    let gc = (part2 * part1) as f32;
                    let gd = (den * x - num * dd) as f32;
                    let m = sb(gd) & sb(gc) & sb(gb) & sb(ga);
                    best = (-(m as i32) & cand) | ((m as i32 - 1) & best);
                    blend(m, &mut num, &mut den, x, dd);
                }
                x += two_sq;
            }
        }
    }

    if best == -1 {
        return 0;
    }
    if best & 0x80 != 0 {
        // edge–edge
        let ia = ((best >> 2) & 3) as usize;
        let jb = (best & 3) as usize;
        let t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
        out.t = t;
        let cs = (1.0 - t * t).sqrt();
        out.cos_t = cs;
        let ia1 = INC_MOD3[ia] as usize;
        let q = |v: usize| -> V3 {
            let (h, r, cr) = (hh[0][v], rr[0][v], cc[0][v]);
            [
                d(c[0]) + ((r[0] * cs + cr[0] * t) + h[0]),
                d(c[1]) + ((r[1] * cs + cr[1] * t) + h[1]),
                d(c[2]) + ((r[2] * cs + cr[2] * t) + h[2]),
            ]
        };
        let q0 = q(ia);
        let q1 = q(ia1);
        edge_edge_result(out, q0, q1, pb[jb], pb[INC_MOD3[jb] as usize], nb);
        out.i_feature = [ia as i32 | 0xA0, jb as i32 | 0xA0];
        return 1;
    }
    // vertex–face
    let kf = (best >> 2) as usize; // face triangle
    let jv = (best & 3) as usize;
    let t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
    out.t = t;
    let cs = (1.0 - t * t).sqrt();
    out.cos_t = cs;
    let vt = kf ^ 1;
    let kt = f64::from(kf as i32) * t;
    let vtd = f64::from(vt as i32);
    // ((|cos − v| + (v + cos))·0.5 = max(cos, v): 1 for B's vertex (unrotated), cos for A's
    let cm = (fabs(cs - vtd) + (vtd + cs)) * 0.5;
    // Oddity kept: B's vertex is "unrotated" through the same formula (cos factor 1, sin factor
    // `0.0·t`), so it gets the rounding of `c + ((r + h) + 0·(a×r))` (and NaN for infinite
    // inputs), not its original coordinates.
    let (h, r, cr) = (hh[vt][jv], rr[vt][jv], cc[vt][jv]);
    out.pt = Vec3::new(
        (d(c[0]) + ((r[0] * cm + h[0]) + cr[0] * kt)) as f32,
        (d(c[1]) + ((r[1] * cm + h[1]) + cr[1] * kt)) as f32,
        (d(c[2]) + ((r[2] * cm + h[2]) + cr[2] * kt)) as f32,
    );
    let (t2, c2) = (out.t, out.cos_t);
    let nrm: [V3; 2] = [
        [
            (hn[0] + rn[0] * c2) + crn[0] * t2,
            (rn[1] * c2 + hn[1]) + crn[1] * t2,
            (rn[2] * c2 + hn[2]) + crn[2] * t2,
        ],
        [d(-nb[0]), d(-nb[1]), d(-nb[2])],
    ];
    let nv = nrm[kf];
    out.n = Vec3::new(nv[0] as f32, nv[1] as f32, nv[2] as f32);
    out.i_feature[kf] = 0x40;
    out.i_feature[vt] = jv as i32 | 0x80;
    1
}

// =============================================================================== cells

fn cell_tri_tri(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_tri_tri_rot(p, as_tri(a), fa, as_tri(b), fb, out)
}

fn cell_ray_tri(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_ray_tri_rot(p, as_ray(a), fa, as_tri(b), fb, out)
}

/// 0x3519EDC0: rotational swap wrapper tri–ray → ray–tri (the area pointer is passed through to
/// ray–tri, which ignores it).
fn cell_tri_ray(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    let tri = as_tri(a);
    let ray = as_ray(b);
    swap_rot(p, out, |p, out| {
        unproject_ray_tri_rot(p, ray, fb, tri, fa, out)
    })
}

/// Fill the cells this group owns: `[1][tri][tri]`, `[1][ray][tri]`, `[1][tri][ray]`.
pub fn fill(t: &mut UnprojTables) {
    t.set(
        MODE_ROTATIONAL,
        GeomType::TriMesh,
        GeomType::TriMesh,
        cell_tri_tri,
    );
    t.set(
        MODE_ROTATIONAL,
        GeomType::Ray,
        GeomType::TriMesh,
        cell_ray_tri,
    );
    t.set(
        MODE_ROTATIONAL,
        GeomType::TriMesh,
        GeomType::Ray,
        cell_tri_ray,
    );
}
