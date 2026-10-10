//! **P2a group 2 — triangle pairs.** Ported bit for bit from the x64 10.x
//! `cryphysics_dedicate.dll` (disassembly; every `double` step of the binary is evaluated in
//! `f64` here, every `float` step in `f32`, with the binary's association).
//!
//! | cell | address | here |
//! |---|---|---|
//! | INTERSECT tri–tri | 0x35159410 | [`intersect_tri_tri`] |
//! | INTERSECT ray–tri | 0x3515AB20 | [`intersect_ray_tri`] |
//! | INTERSECT tri–ray | 0x3515B1D0 | [`intersect_tri_ray`] (wrapper: feat + n) |
//! | INTERSECT tri–sph | 0x35159DE0 | [`intersect_tri_sph`] |
//! | INTERSECT sph–tri | 0x3515AAA0 | [`intersect_sph_tri`] (wrapper: pts + feat) |
//! | INTERSECT tri–box | 0x35162AA0 | [`intersect_tri_box`] |
//! | INTERSECT box–tri | 0x35164780 | [`intersect_box_tri`] (wrapper: pts + feat + n) |
//! | OVERLAP tri–sph / sph–tri | 0x35197530 / 0x35197590 | [`overlap_tri_sph`], [`overlap_sph_tri`] |
//! | (closest-point quotient) | 0x35196E70 | [`tri_point_dist2`] |
//! | OVERLAP box–tri / tri–box | 0x351968F0 / 0x35196CC0 | [`overlap_box_tri`], [`overlap_tri_box`] |
//! | OVERLAP hf–sph / sph–hf | 0x351975E0 / 0x35197860 | [`overlap_hf_sph`], [`overlap_sph_hf`] |
//! | OVERLAP box–hf / hf–box | 0x351986E0 / 0x35199120 | [`overlap_box_hf`], [`overlap_hf_box`] |
//! | (matrix product) | 0x35022380 | `Mat33::mul_mat` (same association) |
//!
//! Wrapper cells call the mirrored kernel with swapped operands and then fix up the output
//! **whether or not the kernel found a contact** (the binary does the swaps unconditionally;
//! the feature swap is bytes 36↔39 and 37↔38).
//!
//! Verified against the DLL itself through `physoracle --p2a2` (goldens
//! `tests/golden/p2a2_*.trace`, live fuzzing with `AAEMU_PHYS_ORACLE`); the comparison covers
//! the return value and every word of `prim_inters` including the unwritten ones and the
//! border buffer.

// `!(a > b)` and friends mirror `comiss`/`comisd` + jcc exactly, including the NaN (unordered)
// outcome; rewriting them with `partial_cmp` would hide that.
#![allow(clippy::neg_cmp_op_on_partial_ord)]

use super::PairTables;
use crate::collision::contact::PrimInters;
use crate::geometry::primitives::{
    BoxPrim, HeightfieldPrim, Primitive, RayPrim, SpherePrim, Triangle,
};
use crate::math::{sgnnz, Vec3};

/// Sign bit of `(float)x` (`cvtsd2ss` + `shr 31`).
#[inline]
fn signbit_f(x: f64) -> usize {
    ((x as f32).to_bits() >> 31) as usize
}

/// `sgnnz((float)x)` as a double (`cvtsd2ss`, `sar 30 / and ~1 / inc`, `cvtdq2pd`).
#[inline]
fn sgnnz_d(x: f64) -> f64 {
    f64::from(sgnnz(x as f32))
}

#[inline]
fn vd(v: Vec3) -> [f64; 3] {
    [f64::from(v.x), f64::from(v.y), f64::from(v.z)]
}

// ---------------------------------------------------------------------------------------------
// tri–tri 0x35159410
// ---------------------------------------------------------------------------------------------

/// `dword_35209F60` / `dword_35209F50`: the two other vertices of a triangle.
const DEC3: [usize; 3] = [2, 0, 1];
const INC3: [usize; 3] = [1, 2, 0];

/// INTERSECT tri–tri (0x35159410): the segment where the two triangles' planes cross, clipped
/// to both triangles.
///
/// The line direction is `d = nA × nB` (f32). Every vertex is projected (in a frame scaled by
/// `|d|²`) onto the line (`t`) and across it inside its own triangle's plane (`u`); each
/// triangle's two crossing edges give an interval on the line, kept as a pair of rational
/// end points. The intervals must overlap; the inner end points become `pt[0]`/`pt[1]`.
///
/// Writes `pt[0]`, `pt[1]` and the four feature bytes only (never `n`):
/// `i_feature[0][k0] = 0xA0 | edge`, `i_feature[0][k0^1] = 0x40`, likewise for B with `k1`.
///
/// Binary quirks kept: the edge reported for A is "slot 0 of triangle `k0`" and the edge for
/// B is "slot 1 of triangle `k1`" (`k0`/`k1` pick the end points), i.e. the feature index of
/// A can come from B's edge table and vice versa; a triangle whose vertices are all on one side
/// of the other plane fails, but coplanar triangles (`d = 0`) fail only through the final
/// `den == 0` test.
pub fn intersect_tri_tri(a: &Triangle, b: &Triangle, out: &mut PrimInters) -> bool {
    let na = a.n;
    let nb = b.n;
    // d = nA × nB, e = nB × d, f = nA × d (all f32).
    let d = Vec3::new(
        nb.z * na.y - nb.y * na.z,
        nb.x * na.z - na.x * nb.z,
        na.x * nb.y - nb.x * na.y,
    );
    let e = Vec3::new(
        nb.y * d.z - nb.z * d.y,
        nb.z * d.x - nb.x * d.z,
        nb.x * d.y - nb.y * d.x,
    );
    let f = Vec3::new(
        na.y * d.z - na.z * d.y,
        na.z * d.x - d.z * na.x,
        d.y * na.x - na.y * d.x,
    );
    let [dx, dy, dz] = vd(d);
    let [ex, ey, ez] = vd(e);
    let [fx, fy, fz] = vd(f);
    let len2 = (dy * dy + dx * dx) + dz * dz;

    let a0 = a.pt[0];
    let b0 = b.pt[0];
    let da = f64::from((a0.x * na.x + a0.y * na.y) + a0.z * na.z);
    let db = f64::from(((-b0.y) * nb.y + nb.x * (-b0.x)) + (-b0.z) * nb.z);
    let dd = f64::from((a0.x * d.x + a0.y * d.y) + a0.z * d.z);
    // A point of the line, scaled by |d|².
    let px = (db * fx + da * ex) + dd * dx;
    let py = (ey * da + db * fy) + dd * dy;
    let pz = (ez * da + db * fz) + dd * dz;
    let l = len2 as f32;

    // (u, t) per vertex: 0..3 = A, 3..6 = B.
    let mut eu = [0.0f64; 6];
    let mut et = [0.0f64; 6];
    let mut s = [0i32; 6];
    for i in 0..3 {
        let v = a.pt[i];
        let qx = f64::from((f64::from(l * v.x) - px) as f32);
        let qy = f64::from((f64::from(l * v.y) - py) as f32);
        let qz = f64::from((f64::from(l * v.z) - pz) as f32);
        et[i] = (qx * dx + qy * dy) + qz * dz;
        eu[i] = (qx * fx + qy * fy) + qz * fz;
        let w = b.pt[i];
        let qx = f64::from((f64::from(l * w.x) - px) as f32);
        let qy = f64::from((f64::from(l * w.y) - py) as f32);
        let qz = f64::from((f64::from(l * w.z) - pz) as f32);
        eu[3 + i] = (qx * ex + qy * ey) + qz * ez;
        et[3 + i] = (qx * dx + qy * dy) + qz * dz;
        s[i] = sgnnz(eu[i] as f32);
        s[3 + i] = sgnnz(eu[3 + i] as f32);
    }

    // Per triangle: interval end points as (A, B) rationals.
    let mut p = [0.0f64; 2];
    let mut abs_p = [0.0f64; 2];
    let mut h = [0.0f64; 2];
    let mut m = [0.0f64; 2];
    let mut x = [[0.0f64; 2]; 2]; // [tri][A, B]
    let mut y = [[0.0f64; 2]; 2];
    let mut vtx = [0usize; 4];
    for j in 0..2 {
        let b3 = 3 * j;
        let (s0, s1, s2) = (s[b3], s[b3 + 1], s[b3 + 2]);
        let ca = (s2.wrapping_mul(s0) - 1) >> 1;
        let cb = (s2.wrapping_mul(s1) - 1) >> 1;
        let cc = (s0.wrapping_mul(s1) - 1) >> 1;
        if -(cc + cb) - ca != 2 {
            return false;
        }
        let idx = ((!ca & 2) | ((!cb & 1) & ca)) as usize;
        let i1 = DEC3[idx];
        let i2 = INC3[idx];
        let (u0, t0) = (eu[b3 + idx], et[b3 + idx]);
        let (u1, t1) = (eu[b3 + i1], et[b3 + i1]);
        let (u2, t2) = (eu[b3 + i2], et[b3 + i2]);
        let sa = sgnnz_d(u1 - u2);
        let a1 = (u1 * t2 - t1 * u2) * sa;
        let bb1 = (u1 - u2) * sa;
        let sb = sgnnz_d(u0 - u1);
        let a2 = (t1 * u0 - u1 * t0) * sb;
        let bb2 = (u0 - u1) * sb;
        let dd = a2 * bb1 - bb2 * a1;
        let ad = dd.abs();
        let au0 = u0.abs();
        let au2 = u2.abs();
        let c1 = signbit_f(ad - au0);
        let mx = if c1 == 1 { au0 } else { ad };
        let c2 = signbit_f(mx - au2);
        let mm = c1 | (c2 << 1);
        let sel = (((mm ^ 2) | 4) >> 1) & mm;
        let hsel = signbit_f([dd, u0, u2][sel]);
        p[j] = bb2 * bb1;
        abs_p[j] = p[j].abs();
        h[j] = (bb2 * a1 - a2 * bb1).abs() * 0.5;
        m[j] = (bb2 * a1 + a2 * bb1) * 0.5;
        vtx[2 * j + hsel] = i2;
        vtx[2 * j + (hsel ^ 1)] = i1;
        let ab = [[a1, bb1], [a2, bb2]];
        x[j] = ab[hsel];
        y[j] = ab[hsel ^ 1];
    }
    // Interval overlap (NaN passes, as `comisd`/`ja` does).
    if (p[1] * m[0] - m[1] * p[0]).abs() > h[1] * abs_p[0] + abs_p[1] * h[0] {
        return false;
    }
    let k0 = signbit_f(x[1][1] * x[0][0] - x[1][0] * x[0][1]);
    let k1 = signbit_f(y[1][0] * y[0][1] - y[1][1] * y[0][0]);
    let xb = x[k0][1];
    let yb = y[k1][1];
    let den = ((len2 * len2) * xb) * yb;
    // `ucomisd` + `jz`: zero and NaN both fail.
    if den == 0.0 || den.is_nan() {
        return false;
    }
    let inv = 1.0 / den;
    let w = (xb * len2) * yb;
    let g0 = yb * x[k0][0];
    let g1 = xb * y[k1][0];
    let (wx, wy, wz) = (px * w, py * w, pz * w);
    out.pt[0] = Vec3::new(
        ((g0 * dx + wx) * inv) as f32,
        ((dy * g0 + wy) * inv) as f32,
        ((dz * g0 + wz) * inv) as f32,
    );
    out.pt[1] = Vec3::new(
        ((g1 * dx + wx) * inv) as f32,
        ((dy * g1 + wy) * inv) as f32,
        ((dz * g1 + wz) * inv) as f32,
    );
    out.i_feature[0][k0] = vtx[2 * k0] as u8 | 0xA0;
    out.i_feature[0][k0 ^ 1] = 0x40;
    out.i_feature[1][k1] = vtx[2 * k1 + 1] as u8 | 0xA0;
    out.i_feature[1][k1 ^ 1] = 0x40;
    true
}

// ---------------------------------------------------------------------------------------------
// ray–tri 0x3515AB20, tri–ray 0x3515B1D0
// ---------------------------------------------------------------------------------------------

/// `9.99999905e-9` (dword_351F1D5C).
const RAY_TRI_EPS: f32 = f32::from_bits(0x322B_CC76);

/// One edge test of ray–tri: `|s|·s + |e|²·tol·W ≥ 0` with `s = n · (e × q)`.
#[inline]
fn ray_tri_edge(e: Vec3, p: [f64; 3], v: Vec3, den: f32, n: Vec3, tol: f32, w: f64) -> bool {
    let qx = (p[0] - f64::from(v.x * den)) as f32;
    let qy = (p[1] - f64::from(v.y * den)) as f32;
    let qz = (p[2] - f64::from(v.z * den)) as f32;
    let cy = e.z * qx - qz * e.x;
    let cx = qz * e.y - qy * e.z;
    let cz = qy * e.x - e.y * qx;
    let s = (cy * n.y + n.x * cx) + cz * n.z;
    let e2 = ((e.x * e.x + e.y * e.y) + e.z * e.z) * tol;
    // `comisd` + `jb`: NaN fails.
    f64::from(s.abs() * s) + f64::from(e2) * w >= 0.0
}

/// INTERSECT ray–tri (0x3515AB20): segment `origin + t·dir`, `t ∈ [0, 1]`, against the
/// triangle, with the edge tolerance read from `out.tolerance` (`prim_inters +40`).
///
/// Fails when `(n·dir)² ≤ tol·1e-8` (so a negative tolerance accepts even `n·dir = 0`). On a hit
/// writes `pt[0] = pt[1] = origin + dir·t`, `n` = the triangle normal and features
/// `{0x40, 0x20, 0x40, 0x20}`. The edge test compares `|s|·s` against `−|e|²·tol·W`, i.e. a
/// point outside an edge by less than about `sqrt(tol)·|e|` still hits.
pub fn intersect_ray_tri(ray: &RayPrim, tri: &Triangle, out: &mut PrimInters) -> bool {
    let tol = out.tolerance;
    let n = tri.n;
    let o = ray.origin;
    let dir = ray.dir;
    let dn = (n.y * dir.y + n.x * dir.x) + n.z * dir.z;
    // `comiss` + `jbe`: NaN fails.
    if !(dn * dn > tol * RAY_TRI_EPS) {
        return false;
    }
    let sg: f32 = if dn >= 0.0 { 1.0 } else { -1.0 };
    let p0 = tri.pt[0];
    let dist = ((p0.y - o.y) * n.y + n.x * (p0.x - o.x)) + (p0.z - o.z) * n.z;
    let tnum = dist * sg;
    let den = sg * dn;
    let p = [
        f64::from(o.x * den + dir.x * tnum),
        f64::from(o.y * den + dir.y * tnum),
        f64::from(o.z * den + dir.z * tnum),
    ];
    let w = f64::from(((n.x * n.x + n.y * n.y) + n.z * n.z) * den);
    if !(den >= (tnum * 2.0 - den).abs()) {
        return false;
    }
    let [v0, v1, v2] = tri.pt;
    if !ray_tri_edge(v1 - v0, p, v0, den, n, tol, w)
        || !ray_tri_edge(v2 - v1, p, v1, den, n, tol, w)
        || !ray_tri_edge(v0 - v2, p, v2, den, n, tol, w)
    {
        return false;
    }
    let t = tnum / den;
    let pt = Vec3::new(dir.x * t + o.x, dir.y * t + o.y, dir.z * t + o.z);
    out.pt = [pt, pt];
    out.n = n;
    out.i_feature = [[0x40, 0x20], [0x40, 0x20]];
    true
}

/// Swap fix-up of the wrappers: `iFeature` bytes 36↔39 and 37↔38.
#[inline]
fn swap_features(out: &mut PrimInters) {
    let [[a, b], [c, d]] = out.i_feature;
    out.i_feature = [[d, c], [b, a]];
}

/// `n = −n` (`xorps` with the sign mask: NaNs keep their payload, only the sign flips).
#[inline]
fn negate_n(out: &mut PrimInters) {
    out.n = Vec3::new(-out.n.x, -out.n.y, -out.n.z);
}

/// INTERSECT tri–ray (0x3515B1D0): [`intersect_ray_tri`] with swapped operands, then (always,
/// even without a contact) features swapped and `n` negated.
pub fn intersect_tri_ray(tri: &Triangle, ray: &RayPrim, out: &mut PrimInters) -> bool {
    let r = intersect_ray_tri(ray, tri, out);
    swap_features(out);
    negate_n(out);
    r
}

// ---------------------------------------------------------------------------------------------
// tri–sph 0x35159DE0, sph–tri 0x3515AAA0
// ---------------------------------------------------------------------------------------------

/// `1e-80` (qword_351F2CF0): the "two distinct roots" test of tri–sph.
const TRI_SPH_ROOT_EPS: f64 = 1e-80;

/// `n · (e × q)` as tri–sph evaluates it (sign = which side of the edge `q` lies on).
#[inline]
fn edge_side(e: Vec3, q: Vec3, n: Vec3) -> f32 {
    let cx = e.y * q.z - e.z * q.y;
    let cy = e.z * q.x - q.z * e.x;
    let cz = q.y * e.x - e.y * q.x;
    (cy * n.y + cx * n.x) + cz * n.z
}

#[inline]
fn signbit32(x: f32) -> i32 {
    (x.to_bits() >> 31) as i32
}

/// INTERSECT tri–sph (0x35159DE0).
///
/// 1. Each vertex is classified inside/outside the sphere (`r² − |v−c|²`, `r²` rounded to
///    float). **All three inside → no contact** (a triangle swallowed by the sphere is ignored).
/// 2. Face: if the centre projects inside the triangle and `|n·(c−v0)| ≤ r`, it writes
///    `unk_84 = 1000`, `n_alt = c − n·r` (the deepest sphere point, stored at `+72`) and
///    `n = tri.n`, and remembers a face contact.
/// 3. Edges, walked `i → i−1` starting at the highest-index outside vertex: each edge's two
///    sphere crossings (`t = (−b ∓ √disc)/|e|²`) that lie strictly inside the edge alternate
///    between "entering" and "leaving" by a parity bit that runs **across** edges. Among the
///    entering points the one nearest to `out.pt[1]` wins; the leaving point recorded just
///    before it (or the last leaving point) is the second.
/// 4. With an entering point: `pt[0]`/`pt[1]` are the two edge points, features
///    `{0xA0|prev(i0), 0x40, 0xA0|prev(i1), 0x40}` (where `prev(i) = (i+2)%3` is the edge's
///    other vertex). Without one: the face contact if any (`pt[0] = pt[1] = n_alt`, features
///    `0x40` ×4), else none.
///
/// Binary quirks kept: the "nearest entering point" distance is measured from `out.pt[1]` as
/// found in the output buffer **before** the call (the binary reads `prim_inters +12` without
/// writing it first); a face contact plus an entering point without any leaving point returns
/// 0 although `n`, `n_alt` and `unk_84` were already written; a zero `|e|²` edge gives `t = 0`.
pub fn intersect_tri_sph(tri: &Triangle, sph: &SpherePrim, out: &mut PrimInters) -> bool {
    let c = sph.center;
    let r = sph.r;
    let r2d = f64::from(r * r);
    let p = tri.pt;
    let n = tri.n;
    let outside = |v: Vec3| -> i32 {
        let d = v - c;
        signbit32((r2d - f64::from((d.x * d.x + d.y * d.y) + d.z * d.z)) as f32)
    };
    let s0 = outside(p[0]);
    let s1 = outside(p[1]);
    let s2 = outside(p[2]);
    let m01 = ((s1 - 1) & (s0 - 1)) | (s1.wrapping_neg() & 1);
    let start = ((s2 - 1) & m01) | (s2.wrapping_neg() & 2);
    if start < 0 {
        return false;
    }

    // Face contact.
    let q0 = c - p[0];
    let dplane = (n.x * q0.x + q0.y * n.y) + q0.z * n.z;
    let mut far = signbit32(r - dplane.abs());
    for i in 0..3 {
        let e = p[INC3[i]] - p[i];
        far |= signbit32(edge_side(e, c - p[i], n));
    }
    let face = far == 0;
    if face {
        out.unk_84 = 1000;
        out.n_alt = Vec3::new(c.x - n.x * r, c.y - n.y * r, c.z - n.z * r);
        out.n = n;
    }

    // Edge crossings. rec[i] = [t0num, den, t1num, den].
    let mut rec = [[0.0f64; 4]; 3];
    let mut best_den2 = 0.0f64;
    let mut best_dist2 = 1.0f64;
    let mut exit_prev: i32 = -1; // esi
    let mut entry: i32 = -1; // r13
    let mut exit: i32 = -1; // edi
    let mut parity: i32 = 0; // r14
    let ref_pt = out.pt[1];
    let mut i = start as usize;
    loop {
        let j = DEC3[i];
        let vi = p[i];
        let vj = p[j];
        let ex = f64::from(vj.x - vi.x);
        let ey = f64::from(vj.y - vi.y);
        let ez = f64::from(vj.z - vi.z);
        let wx = f64::from(vi.x - c.x);
        let wy = f64::from(vi.y - c.y);
        let wz = f64::from(vi.z - c.z);
        let e2 = (ey * ey + ex * ex) + ez * ez;
        let b = (wy * ey + wx * ex) + wz * ez;
        let w2 = (wy * wy + wx * wx) + wz * wz;
        let disc = b * b - (w2 - r2d) * e2;
        // `comisd` + `jb`: NaN skips the edge.
        if disc >= 0.0 {
            let sq = disc.sqrt();
            let t0 = -b - sq;
            let t1 = sq - b;
            rec[i] = [t0, e2, t1, e2];
            // `comisd` + `jnb`: NaN does not skip.
            if !((t0 * e2 - t1 * e2) + (t0 - t1) * TRI_SPH_ROOT_EPS >= 0.0) {
                let e = vj - vi;
                let dx = f64::from(vi.x - ref_pt.x);
                let dy = f64::from(vi.y - ref_pt.y);
                let dz = f64::from(vi.z - ref_pt.z);
                for root in 0..2 {
                    let tnum = rec[i][2 * root];
                    let den = rec[i][2 * root + 1];
                    let id = (2 * i + root) as i32;
                    let inside = signbit32(((tnum * 2.0 - den).abs() - den) as f32);
                    let tnf = tnum as f32;
                    let a = inside & parity;
                    exit = (a.wrapping_neg() & id) | ((a - 1) & exit);
                    let x = f64::from(e.x * tnf) + dx * den;
                    let y = f64::from(e.y * tnf) + dy * den;
                    let z = f64::from(e.z * tnf) + dz * den;
                    let den2 = den * den;
                    let dist2 = (y * y + x * x) + z * z;
                    let cmp = signbit32((best_den2 * dist2 - den2 * best_dist2) as f32);
                    let better = cmp & (parity ^ 1) & inside;
                    let cb = f64::from(better);
                    let nb = f64::from(1 - better);
                    best_den2 = nb * best_den2 + cb * den2;
                    best_dist2 = nb * best_dist2 + cb * dist2;
                    let mask = better.wrapping_neg();
                    entry = (entry & (better - 1)) | (id & mask);
                    exit_prev = (exit_prev & (better - 1)) | (mask & exit);
                    parity ^= inside;
                }
            }
        }
        i = j;
        if i as i32 == start {
            break;
        }
    }

    if entry < 0 {
        if !face {
            return false;
        }
        out.pt = [out.n_alt, out.n_alt];
        out.i_feature = [[0x40, 0x40], [0x40, 0x40]];
        return true;
    }
    let lo = exit_prev >> 31;
    let leave = (lo & exit) | (exit_prev - lo);
    if leave < 0 {
        return false;
    }
    let edge_point = |id: i32| -> (Vec3, u8) {
        let e = (id >> 1) as usize;
        let k = (id & 1) as usize;
        let den = rec[e][2 * k + 1];
        // `ucomisd` + `jz`: zero or NaN → t = 0.
        let t = if den == 0.0 || den.is_nan() {
            0.0
        } else {
            rec[e][2 * k] / den
        };
        let t = t as f32;
        let vi = p[e];
        let vj = p[DEC3[e]];
        (
            Vec3::new(
                (vj.x - vi.x) * t + vi.x,
                (vj.y - vi.y) * t + vi.y,
                (vj.z - vi.z) * t + vi.z,
            ),
            DEC3[e] as u8 | 0xA0,
        )
    };
    let (p0, f0) = edge_point(entry);
    out.pt[0] = p0;
    out.i_feature[1][1] = 0x40;
    out.i_feature[0][1] = 0x40;
    out.i_feature[0][0] = f0;
    let (p1, f1) = edge_point(leave);
    out.pt[1] = p1;
    out.i_feature[1][0] = f1;
    true
}

/// INTERSECT sph–tri (0x3515AAA0): [`intersect_tri_sph`] with swapped operands, then (always)
/// `pt[0] ↔ pt[1]` and the feature swap; `n` keeps the triangle's orientation.
pub fn intersect_sph_tri(sph: &SpherePrim, tri: &Triangle, out: &mut PrimInters) -> bool {
    let r = intersect_tri_sph(tri, sph, out);
    out.pt.swap(0, 1);
    swap_features(out);
    r
}

// ---------------------------------------------------------------------------------------------
// tri–box 0x35162AA0, box–tri 0x35164780
// ---------------------------------------------------------------------------------------------

/// `0.98f` as stored in double (qword_351F2D00): the "box face nearly parallel" threshold.
const TRI_BOX_FACE_COS: f64 = 0.980_000_019_073_486_3;

/// The `sel = (((m ^ 2) | 4) >> 1) & m` idiom with `m = c1 | 2·c2`: index of the extreme of
/// three values from two sign bits.
#[inline]
fn pick3(c1: usize, c2: usize) -> usize {
    let m = c1 | (c2 << 1);
    (((m ^ 2) | 4) >> 1) & m
}

/// `n · (q × e)` in double, as tri–box evaluates it for its edge tests.
#[inline]
fn side_d(e: [f64; 3], q: [f64; 3], n: [f64; 3]) -> f64 {
    let wx = q[1] * e[2] - q[2] * e[1];
    let wy = q[2] * e[0] - q[0] * e[2];
    let wz = q[0] * e[1] - q[1] * e[0];
    (n[0] * wx + n[1] * wy) + n[2] * wz
}

/// `q = p − l·den` (component-wise, double).
#[inline]
fn sub_scaled(p: [f64; 3], l: [f64; 3], den: f64) -> [f64; 3] {
    [p[0] - l[0] * den, p[1] - l[1] * den, p[2] - l[2] * den]
}

#[inline]
fn sub3(a: [f64; 3], b: [f64; 3]) -> [f64; 3] {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}

/// Box-local (scaled) point back to world: `center + Basisᵀ·(p·inv)`, rounded to float.
#[inline]
fn box_to_world(bx: &BoxPrim, p: [f64; 3], inv: f64) -> Vec3 {
    let m = &bx.basis.m;
    let lx = inv * p[0];
    let ly = p[1] * inv;
    let lz = p[2] * inv;
    let md = |r: usize, c: usize| f64::from(m[r][c]);
    let wx = (md(0, 0) * lx + md(1, 0) * ly) + md(2, 0) * lz;
    let wy = (md(0, 1) * lx + md(1, 1) * ly) + md(2, 1) * lz;
    let wz = (md(0, 2) * lx + md(1, 2) * ly) + md(2, 2) * lz;
    Vec3::new(
        (f64::from(bx.center.x) + wx) as f32,
        (f64::from(bx.center.y) + wy) as f32,
        (f64::from(bx.center.z) + wz) as f32,
    )
}

/// Rational parameter comparison of the slab clipper: `a.n·b.d − b.n·a.d + (a.n − b.n)·1e-80`.
#[inline]
fn qcmp(a: (f64, f64), b: (f64, f64)) -> f64 {
    (a.0 * b.1 - b.0 * a.1) + (a.0 - b.0) * TRI_SPH_ROOT_EPS
}

/// INTERSECT tri–box (0x35162AA0).
///
/// Works in the box frame (`Basis·(v − center)`, every coordinate kept in double):
/// 1. Vertices are classified against the three slabs. **All three inside → no contact.**
///    If at least one vertex is inside, the first inside vertex goes to `n_alt` (`+72`), `n`
///    becomes the box face normal nearest to it (`±Basis` row) and `unk_84 = 6`.
/// 2. Each triangle edge (walked `i → i−1` from the highest-index outside vertex) is clipped
///    by the slabs as rational parameters; the clipped end points that lie strictly inside the
///    edge alternate entering/leaving by a parity bit running across edges, exactly as in
///    [`intersect_tri_sph`] (the nearest entering point to the buffer's `pt[1]` wins).
/// 3. Without an entering point: the box corner deepest along `−n` is swept towards the
///    opposite corner; if that segment pierces the triangle, the piercing point is a face
///    contact (`pt[0] = pt[1] = n_alt`, `n = tri.n`, features `0x40`×4, `unk_84 = 1000`),
///    otherwise no contact.
/// 4. With a contact and a triangle large compared with the box face most parallel to it,
///    the 12 box edges that pierce the triangle append their piercing points to the border
///    buffer (while `border.len() < border_max`); if that box face is within ~11.5° of the
///    triangle (`|n_local| > 0.98`), at least two points were added, `unk_84 < 6`, and the
///    face centre projects into the triangle, `n = tri.n`, `n_alt = first added point` and
///    `unk_84 = 6`.
///
/// Binary quirks kept: `pt[1]` of the buffer is read as the reference point before anything
/// writes it; on an edge contact with no vertex inside the box, `n` is **not written**; in
/// step 4 the face-centre test uses an unscaled point (it is a plain sign test).
pub fn intersect_tri_box(tri: &Triangle, bx: &BoxPrim, out: &mut PrimInters) -> bool {
    let size = [bx.size.x, bx.size.y, bx.size.z];
    let basis = &bx.basis;
    let l: [[f64; 3]; 3] = [
        vd(basis.mul_vec(tri.pt[0] - bx.center)),
        vd(basis.mul_vec(tri.pt[1] - bx.center)),
        vd(basis.mul_vec(tri.pt[2] - bx.center)),
    ];
    let nl = vd(basis.mul_vec(tri.n));

    let outside = |k: usize| -> i32 {
        let mut bits = 0u32;
        for a in 0..3 {
            bits |= ((f64::from(size[a]) - l[k][a].abs()) as f32).to_bits();
        }
        (bits >> 31) as i32
    };
    let o0 = outside(0);
    let o1 = outside(1);
    let o2 = outside(2);
    let m01 = ((o1 - 1) & (o0 - 1)) | (o1.wrapping_neg() & 1);
    let start = ((o2 - 1) & m01) | (o2.wrapping_neg() & 2);
    if start < 0 {
        return false;
    }
    if o0 + o1 + o2 < 3 {
        let k = (o0 + (o1 & o0)) as usize;
        out.n_alt = tri.pt[k];
        let d = [
            (l[k][0].abs() - f64::from(size[0])).abs(),
            (l[k][1].abs() - f64::from(size[1])).abs(),
            (l[k][2].abs() - f64::from(size[2])).abs(),
        ];
        let c1 = signbit_f(d[1] - d[0]);
        let c2 = signbit_f(d[2] - d[c1]);
        let sel = pick3(c1, c2);
        let s = (-sgnnz(l[k][sel] as f32)) as f32;
        let row = basis.m[sel];
        out.n = Vec3::new(row[0] * s, row[1] * s, row[2] * s);
        out.unk_84 = 6;
    }

    // Edge clipping. rec[i] = [lo.n, lo.d, hi.n, hi.d].
    let mut rec = [[0.0f64; 4]; 3];
    let mut best_den2 = 0.0f64;
    let mut best_dist2 = 1.0f64;
    let mut exit_prev: i32 = -1;
    let mut entry: i32 = -1;
    let mut exit: i32 = -1;
    let mut parity: i32 = 0;
    let p = tri.pt;
    let mut i = start as usize;
    loop {
        let j = DEC3[i];
        let e = sub3(l[j], l[i]);
        let mut lo = (0.0f64, 1.0f64);
        let mut hi = (1.0f64, 1.0f64);
        for a in 0..3 {
            let s = f64::from(size[a]);
            let neg = signbit_f(e[a]);
            let sg = f64::from(1 - 2 * neg as i32);
            let pm = (f64::from(-size[a]) - l[i][a]) * sg;
            let pp = (s - l[i][a]) * sg;
            let es = e[a] * sg;
            let mut par = [(0.0, 0.0); 2];
            par[neg] = (pm, es);
            par[neg ^ 1] = (pp, es);
            // `comisd` + `ja` keeps lo (NaN replaces it); `jb` keeps hi (NaN keeps it).
            if !(qcmp(lo, par[0]) > 0.0) {
                lo = par[0];
            }
            if !(qcmp(hi, par[1]) < 0.0) {
                hi = par[1];
            }
        }
        rec[i] = [lo.0, lo.1, hi.0, hi.1];
        if (hi.0 * lo.1 - lo.0 * hi.1) + (hi.0 - lo.0) * TRI_SPH_ROOT_EPS > 0.0 {
            let ref_pt = out.pt[1];
            let ew = p[j] - p[i];
            let dx = f64::from(p[i].x - ref_pt.x);
            let dy = f64::from(p[i].y - ref_pt.y);
            let dz = f64::from(p[i].z - ref_pt.z);
            for root in 0..2 {
                let num = rec[i][2 * root];
                let den = rec[i][2 * root + 1];
                let id = (2 * i + root) as i32;
                let nf = num as f32;
                let inside = signbit32(((num * 2.0 - den).abs() - den.abs()) as f32);
                let a = inside & parity;
                exit = (a.wrapping_neg() & id) | ((a - 1) & exit);
                let x = f64::from(ew.x * nf) + den * dx;
                let y = f64::from(nf * ew.y) + den * dy;
                let z = f64::from(nf * ew.z) + den * dz;
                let den2 = den * den;
                let dist2 = (x * x + y * y) + z * z;
                let cmp = signbit32((best_den2 * dist2 - den2 * best_dist2) as f32);
                let better = cmp & (parity ^ 1) & inside;
                let cb = f64::from(better);
                let nb = f64::from(1 - better);
                best_den2 = nb * best_den2 + cb * den2;
                best_dist2 = nb * best_dist2 + cb * dist2;
                let mask = better.wrapping_neg();
                entry = (id & mask) | (entry & (better - 1));
                exit_prev = (exit_prev & (better - 1)) | (mask & exit);
                parity ^= inside;
            }
        }
        i = j;
        if i as i32 == start {
            break;
        }
    }

    if entry >= 0 {
        let lo = exit_prev >> 31;
        let leave = (exit_prev - lo) | (lo & exit);
        if leave < 0 {
            return false;
        }
        let edge_point = |id: i32| -> (Vec3, u8) {
            let e = (id >> 1) as usize;
            let k = (id & 1) as usize;
            let den = rec[e][2 * k + 1];
            let t = if den == 0.0 || den.is_nan() {
                0.0
            } else {
                rec[e][2 * k] / den
            };
            let t = t as f32;
            let vi = p[e];
            let vj = p[DEC3[e]];
            (
                Vec3::new(
                    (vj.x - vi.x) * t + vi.x,
                    (vj.y - vi.y) * t + vi.y,
                    (vj.z - vi.z) * t + vi.z,
                ),
                DEC3[e] as u8 | 0xA0,
            )
        };
        let (p0, f0) = edge_point(entry);
        out.pt[0] = p0;
        out.i_feature[1][1] = 0x40;
        out.i_feature[0][1] = 0x40;
        out.i_feature[0][0] = f0;
        let (p1, f1) = edge_point(leave);
        out.pt[1] = p1;
        out.i_feature[1][0] = f1;
    } else {
        // The box corner deepest against the normal, swept to the opposite corner.
        let corner = [
            f64::from((-sgnnz(nl[0] as f32)) as f32 * size[0]),
            f64::from((-sgnnz(nl[1] as f32)) as f32 * size[1]),
            f64::from((-sgnnz(nl[2] as f32)) as f32 * size[2]),
        ];
        let k2 = [corner[0] * -2.0, corner[1] * -2.0, corner[2] * -2.0];
        let num = ((l[0][0] - corner[0]) * nl[0] + (l[0][1] - corner[1]) * nl[1])
            + (l[0][2] - corner[2]) * nl[2];
        let den = (k2[0] * nl[0] + k2[1] * nl[1]) + k2[2] * nl[2];
        let mut hit = signbit32(((num * 2.0 - den).abs() - den.abs()) as f32);
        let pp = [
            corner[0] * den + k2[0] * num,
            corner[1] * den + k2[1] * num,
            corner[2] * den + k2[2] * num,
        ];
        for k in 0..3 {
            let e = sub3(l[INC3[k]], l[k]);
            let q = sub_scaled(pp, l[k], den);
            hit &= signbit32(side_d(e, q, nl) as f32);
        }
        if hit == 0 {
            return false;
        }
        let w = box_to_world(bx, pp, 1.0 / den);
        out.pt = [w, w];
        out.i_feature = [[0x40, 0x40], [0x40, 0x40]];
        out.n_alt = w;
        out.n = tri.n;
        out.unk_84 = 1000;
    }

    // Box edges piercing a large triangle → border points.
    let an = [nl[0].abs(), nl[1].abs(), nl[2].abs()];
    let c1 = signbit_f(an[0] - an[1]);
    let c2 = signbit_f(an[c1] - an[2]);
    let sel = pick3(c1, c2);
    let e1 = sub3(l[1], l[0]);
    let e2 = sub3(l[2], l[0]);
    let cx = e1[1] * e2[2] - e1[2] * e2[1];
    let cy = e1[2] * e2[0] - e2[2] * e1[0];
    let cz = e2[1] * e1[0] - e1[1] * e2[0];
    let area2 = (cx * cx + cy * cy) + cz * cz;
    let fa = size[INC3[sel]] * size[DEC3[sel]];
    // `comisd` + `jbe`: NaN skips.
    if !(area2 > f64::from(fa * fa)) {
        return true;
    }
    let n0 = out.border.len() as i32;
    let mut ci = [0i32; 3];
    for edge in 0..12usize {
        let a = edge >> 2;
        ci[a] = -1;
        ci[INC3[a]] = 2 * (edge as i32 & 1) - 1;
        ci[DEC3[a]] = (edge as i32 & 2) - 1;
        let cor = [
            f64::from(ci[0] as f32 * size[0]),
            f64::from(ci[1] as f32 * size[1]),
            f64::from(ci[2] as f32 * size[2]),
        ];
        let num =
            (nl[0] * (l[0][0] - cor[0]) + (l[0][1] - cor[1]) * nl[1]) + (l[0][2] - cor[2]) * nl[2];
        let dd = (f64::from(size[a]) * nl[a]) * 2.0;
        if signbit32(((dd - num * 2.0).abs() - (-dd).abs()) as f32) == 0 {
            continue;
        }
        let sg = f64::from(sgnnz(nl[a] as f32));
        let den = nl[a] * sg;
        let mut pp = [cor[0] * den, cor[1] * den, cor[2] * den];
        pp[a] += num * sg;
        let mut hit = 1;
        for k in 0..3 {
            let e = sub3(l[INC3[k]], l[k]);
            let q = sub_scaled(pp, l[k], den);
            hit &= signbit32(side_d(e, q, nl) as f32);
        }
        let len = out.border.len() as i32;
        if hit != 0 && len.wrapping_sub(out.border_max) < 0 {
            let w = box_to_world(bx, pp, 1.0 / den);
            out.border.push(w);
        }
    }
    if an[sel] > TRI_BOX_FACE_COS && (out.border.len() as i32) > n0 + 1 && out.unk_84 < 6 {
        let mut cc = [0.0f64; 3];
        cc[sel] = f64::from(sgnnz(l[0][sel] as f32) as f32 * size[sel]);
        let s1 = side_d(e1, sub3(cc, l[0]), nl) as f32;
        let s2 = side_d(sub3(l[2], l[1]), sub3(cc, l[1]), nl) as f32;
        let s3 = side_d(sub3(l[0], l[2]), sub3(cc, l[2]), nl) as f32;
        if (s1.to_bits() & s2.to_bits() & s3.to_bits()) >> 31 != 0 {
            out.unk_84 = 6;
            out.n = tri.n;
            out.n_alt = out.border[n0 as usize];
        }
    }
    true
}

/// INTERSECT box–tri (0x35164780): [`intersect_tri_box`] with swapped operands, then (always)
/// `pt[0] ↔ pt[1]`, the feature swap and `n = −n`.
pub fn intersect_box_tri(bx: &BoxPrim, tri: &Triangle, out: &mut PrimInters) -> bool {
    let r = intersect_tri_box(tri, bx, out);
    out.pt.swap(0, 1);
    swap_features(out);
    negate_n(out);
    r
}

// ---------------------------------------------------------------------------------------------
// OVERLAP: closest point 0x35196E70, tri–sph 0x35197530, sph–tri 0x35197590
// ---------------------------------------------------------------------------------------------

/// `sub_35196E70`: squared distance from `p` to the triangle as a quotient `(num, den)`
/// (`dist² = num / den`), plus the flag the binary writes through its 4th argument (1 when the
/// face plane distance was used).
///
/// Picks the nearest vertex `v0`; with `w = p − v0` and the two edges `e1`, `e2` leaving it,
/// `s1 = n·(e1 × w)`, `s2 = n·(e2 × w)`:
/// * not both negative: `(|e|²·|w|² − f·max(e·w, 0)², |e|²)` with `e = s1 < 0 ? e2 : e1` and
///   `f = 1` if either is negative, else 0 (i.e. the vertex distance itself);
/// * both negative and `e1·e2 < 0` and the point is outside the opposite edge: distance to the
///   line of that edge;
/// * otherwise the plane distance `((p − pt[0])·n)², 1` — exact only for a unit normal.
#[must_use]
pub fn tri_point_dist2(tri: &Triangle, p: Vec3) -> (f32, f32, i32) {
    let v = tri.pt;
    let n = tri.n;
    let mut d = [0.0f32; 3];
    for (k, dk) in d.iter_mut().enumerate() {
        let q = v[k] - p;
        *dk = (q.y * q.y + q.x * q.x) + q.z * q.z;
    }
    let c1 = (((d[1] - d[0]).to_bits()) >> 31) as usize;
    let c2 = (((d[2] - d[c1]).to_bits()) >> 31) as usize;
    let i0 = pick3(c1, c2);
    let i1 = INC3[i0];
    let i2 = DEC3[i0];
    let w = p - v[i0];
    let e = [v[i1] - v[i0], v[i2] - v[i0]];
    let l = [
        (e[0].x * e[0].x + e[0].y * e[0].y) + e[0].z * e[0].z,
        (e[1].x * e[1].x + e[1].y * e[1].y) + e[1].z * e[1].z,
    ];
    let (e1, e2) = (e[0], e[1]);
    let s1 = ((w.z * e1.x - e1.z * w.x) * n.y + (e1.z * w.y - e1.y * w.z) * n.x)
        + (e1.y * w.x - w.y * e1.x) * n.z;
    let r9 = (s1.to_bits() >> 31) as usize;
    let s2 = ((e2.z * w.x - w.z * e2.x) * n.y + (e2.y * w.z - e2.z * w.y) * n.x)
        + (w.y * e2.x - e2.y * w.x) * n.z;
    let r8 = (s2.to_bits() >> 31) as usize;
    let ee = e[r9];
    let dot = (w.y * ee.y + ee.x * w.x) + w.z * ee.z;
    let m = ((-dot).abs() + dot) * 0.5;
    d[i0] = l[r9] * d[i0] - ((r8 | r9) as i32 as f32) * (m * m);
    if r9 & r8 == 0 {
        return (d[i0], l[r9], 0);
    }
    let dn = (e2.x * e1.x + e2.y * e1.y) + e2.z * e1.z;
    if !(dn >= 0.0) {
        let f = v[i2] - v[i1];
        let g = p - v[i1];
        let s3 = ((g.y * f.z - g.z * f.y) * n.x + (g.z * f.x - f.z * g.x) * n.y)
            + (f.y * g.x - g.y * f.x) * n.z;
        if s3 > 0.0 {
            let gf = (g.x * f.x + g.y * f.y) + g.z * f.z;
            let lf = (f.x * f.x + f.y * f.y) + f.z * f.z;
            return (lf * d[i1] - gf * gf, lf, 0);
        }
    }
    let q = p - v[0];
    let s = (q.y * n.y + n.x * q.x) + q.z * n.z;
    (s * s, 1.0, 1)
}

/// OVERLAP tri–sph (0x35197530): `num − r²·den < 0` (sign bit, so `−0` overlaps) with the
/// quotient of [`tri_point_dist2`] for the sphere centre.
#[must_use]
pub fn overlap_tri_sph(tri: &Triangle, sph: &SpherePrim) -> bool {
    let r2 = sph.r * sph.r;
    let (num, den, _) = tri_point_dist2(tri, sph.center);
    (num - r2 * den).to_bits() >> 31 != 0
}

/// OVERLAP sph–tri (0x35197590): [`overlap_tri_sph`] with swapped operands.
#[must_use]
pub fn overlap_sph_tri(sph: &SpherePrim, tri: &Triangle) -> bool {
    overlap_tri_sph(tri, sph)
}

// ---------------------------------------------------------------------------------------------
// OVERLAP box–tri 0x351968F0 (SSE4.1 path), tri–box 0x35196CC0
// ---------------------------------------------------------------------------------------------

/// OVERLAP box–tri (0x351968F0): separating-axis test over the box axes, the triangle normal
/// and the three `edge × box axis` axes, in the box frame (all lanes single precision).
///
/// The binary checks a cached CPU-feature byte first (`sub_35199B20`, which always reports
/// SSE4.1 on any machine that runs the game) and otherwise calls a scalar variant
/// (`sub_351960B0`); only the SSE path is ported. Box axes: the triangle's centroid-weighted
/// extent `|½·Σ(vᵢ+vⱼ)·|vᵢ−vⱼ||` against `(¼·Σ|vᵢ−vⱼ| + size)·Σ|vᵢ−vⱼ|` (a conservative
/// interval test, not the exact projection). Edge axes compare `|½(e×v₀ + e×v₂)|` with
/// `|½(e×v₀ − e×v₂)| + |s·e|` terms. Comparisons are `<` on lanes, so NaNs never separate.
#[must_use]
pub fn overlap_box_tri(bx: &BoxPrim, tri: &Triangle) -> bool {
    // Ported once, by the owning group; kept here for the P2a-2 tests and callers.
    super::box_pairs::overlap_box_tri(bx, tri)
}

/// OVERLAP tri–box (0x35196CC0): [`overlap_box_tri`] with swapped operands.
#[must_use]
pub fn overlap_tri_box(tri: &Triangle, bx: &BoxPrim) -> bool {
    // Ported once, by the owning group; kept here for the P2a-2 tests and callers.
    super::box_pairs::overlap_box_tri(bx, tri)
}

// ---------------------------------------------------------------------------------------------
// OVERLAP hf–sph 0x351975E0, sph–hf 0x35197860
// ---------------------------------------------------------------------------------------------

/// OVERLAP hf–sph (0x351975E0): the sphere's centre in the heightfield frame; every grid
/// vertex in the (clamped) cell range under the sphere's square footprint is tested with
/// `c.z − r < h·heightscale` (sign bit). The ranges are half-open (`ix0 ≤ ix < ix1`) after
/// rounding `(x ∓ r)·stepr ∓ 0.5`.
#[must_use]
pub fn overlap_hf_sph(hf: &HeightfieldPrim, sph: &SpherePrim) -> bool {
    // Ported once, by the owning group; kept here for the P2a-2 tests and callers.
    super::round_pairs::overlap_hf_sph(hf, sph)
}

/// OVERLAP sph–hf (0x35197860): [`overlap_hf_sph`] with swapped operands.
#[must_use]
pub fn overlap_sph_hf(sph: &SpherePrim, hf: &HeightfieldPrim) -> bool {
    // Ported once, by the owning group; kept here for the P2a-2 tests and callers.
    super::round_pairs::overlap_sph_hf(sph, hf)
}

// ---------------------------------------------------------------------------------------------
// OVERLAP box–hf 0x351986E0, hf–box 0x35199120
// ---------------------------------------------------------------------------------------------

/// OVERLAP box–hf (0x351986E0).
///
/// The box is brought into the heightfield frame (`R = Basis_box · Basis_hfᵀ` when both are
/// oriented, `Basis_hfᵀ` when only the heightfield is, the raw box basis when the heightfield
/// is not; centre `Basis_hf·(c − origin)`), its rows sign-flipped so that every row's `z` is
/// non-positive. Its footprint gives a clamped cell range (`ix0 = max(·, 0)`, `ix1 = min(·, n)`
/// — each bound clamped on one side only) and the box bottom `zmin`.
/// * More than 6 cells (`(ix1−ix0)·(iy1−iy0) > 6`, wrapping): any grid vertex in the
///   inclusive range with `h·hscale > zmin` overlaps.
/// * Otherwise: if the maximum of `0` and all those heights is below `zmin` (or `ix0 ≥ ix1`),
///   no overlap; else each cell of the half-open range is split into the triangles
///   `(00, 10, 01)` and `(01, 10, 11)` and tested with [`overlap_box_tri`] against the
///   transformed box.
///
/// Binary quirks kept: the "max height" starts at 0 (heights below 0 never lower it); the
/// footprint uses the `max`-idiom half extents of the flipped rows, not the exact AABB.
#[must_use]
pub fn overlap_box_hf(bx: &BoxPrim, hf: &HeightfieldPrim) -> bool {
    // Ported once, by the owning group; kept here for the P2a-2 tests and callers.
    super::box_pairs::overlap_box_heightfield(bx, hf)
}

/// OVERLAP hf–box (0x35199120): [`overlap_box_hf`] with swapped operands.
#[must_use]
pub fn overlap_hf_box(hf: &HeightfieldPrim, bx: &BoxPrim) -> bool {
    // Ported once, by the owning group; kept here for the P2a-2 tests and callers.
    super::box_pairs::overlap_box_heightfield(bx, hf)
}

// ---------------------------------------------------------------------------------------------
// table adapters
// ---------------------------------------------------------------------------------------------

fn as_tri(p: &Primitive) -> &Triangle {
    match p {
        Primitive::Triangle(t) => t,
        _ => panic!("tri_pairs: expected a triangle, got {:?}", p.geom_type()),
    }
}

fn as_ray(p: &Primitive) -> &RayPrim {
    match p {
        Primitive::Ray(r) => r,
        _ => panic!("tri_pairs: expected a ray, got {:?}", p.geom_type()),
    }
}

fn cell_tri_tri(a: &Primitive, b: &Primitive, out: &mut PrimInters) -> bool {
    intersect_tri_tri(as_tri(a), as_tri(b), out)
}
fn cell_ray_tri(a: &Primitive, b: &Primitive, out: &mut PrimInters) -> bool {
    intersect_ray_tri(as_ray(a), as_tri(b), out)
}
fn cell_tri_ray(a: &Primitive, b: &Primitive, out: &mut PrimInters) -> bool {
    intersect_tri_ray(as_tri(a), as_ray(b), out)
}

fn as_box(p: &Primitive) -> &BoxPrim {
    match p {
        Primitive::Box(b) => b,
        _ => panic!("tri_pairs: expected a box, got {:?}", p.geom_type()),
    }
}

fn as_sph(p: &Primitive) -> &SpherePrim {
    match p {
        Primitive::Sphere(s) => s,
        _ => panic!("tri_pairs: expected a sphere, got {:?}", p.geom_type()),
    }
}

fn cell_tri_sph(a: &Primitive, b: &Primitive, out: &mut PrimInters) -> bool {
    intersect_tri_sph(as_tri(a), as_sph(b), out)
}
fn cell_sph_tri(a: &Primitive, b: &Primitive, out: &mut PrimInters) -> bool {
    intersect_sph_tri(as_sph(a), as_tri(b), out)
}
fn cell_tri_box(a: &Primitive, b: &Primitive, out: &mut PrimInters) -> bool {
    intersect_tri_box(as_tri(a), as_box(b), out)
}
fn cell_box_tri(a: &Primitive, b: &Primitive, out: &mut PrimInters) -> bool {
    intersect_box_tri(as_box(a), as_tri(b), out)
}
fn ocell_tri_sph(a: &Primitive, b: &Primitive) -> bool {
    overlap_tri_sph(as_tri(a), as_sph(b))
}
fn ocell_sph_tri(a: &Primitive, b: &Primitive) -> bool {
    overlap_sph_tri(as_sph(a), as_tri(b))
}

/// Write this group's INTERSECT / OVERLAP cells: every INTERSECT cell with a triangle operand
/// except tri x cyl/cap (P2a-3), and the OVERLAP tri and hf rows. The OVERLAP cells box–tri,
/// box–hf, sph–tri and sph–hf (box / sph rows) are filled here as well because their kernels
/// live in this file; the box and round groups reuse them.
pub fn fill(t: &mut PairTables) {
    use crate::ids::GeomType as G;
    let bx = G::Box.id() as usize;
    let tri = G::TriMesh.id() as usize;
    let ray = G::Ray.id() as usize;
    let sph = G::Sphere.id() as usize;
    t.intersect[tri][tri] = cell_tri_tri;
    t.intersect[ray][tri] = cell_ray_tri;
    t.intersect[tri][ray] = cell_tri_ray;
    t.intersect[tri][sph] = cell_tri_sph;
    t.intersect[sph][tri] = cell_sph_tri;
    t.intersect[tri][bx] = cell_tri_box;
    t.intersect[bx][tri] = cell_box_tri;
    t.overlap[tri][sph] = ocell_tri_sph;
    t.overlap[sph][tri] = ocell_sph_tri;
}
