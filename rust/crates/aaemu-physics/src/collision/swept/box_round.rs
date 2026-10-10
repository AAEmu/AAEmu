//! P2b work group `box_round`: linear unprojection of boxes and rays against round / box
//! primitives (box–sph, ray–box, ray–sph) and their 335-byte swap wrappers.
//!
//! | cell | x64 | x86 (1.2) | port |
//! |---|---|---|---|
//! | `[0][box][sph]` | 0x35185B80 (5088 B) | 0x3518A9A0 | [`unproject_box_sph`] |
//! | `[0][sph][box]` | 0x35186F70 (335 B wrapper) | 0x3518BF90 | [`swap_linear`] over box–sph |
//! | `[0][ray][box]` | 0x35178540 (3997 B) | 0x3518CAA0 | [`unproject_ray_box`] |
//! | `[0][box][ray]` | 0x351794F0 (335 B wrapper) | 0x3518DAE0 | [`swap_linear`] over ray–box |
//! | `[0][ray][sph]` | 0x3517BDA0 (2776 B) | 0x35190A00 | [`unproject_ray_sph`] |
//! | `[0][sph][ray]` | 0x3517C880 (335 B wrapper) | 0x35191490 | [`swap_linear`] over ray–sph |
//!
//! The three wrappers were compared instruction by instruction with the reference wrapper
//! 0x35171570; they differ only in the callee, so [`swap_linear`] is used unchanged.
//!
//! **Common scheme.** All three solvers keep the best candidate as a *rational* value
//! `tnum / tden` (both `f64`) and a candidate feature code. Each candidate is accepted
//! branch-free: a set of `f32`-rounded test values is computed, their *sign bits* are AND-ed
//! into `ok ∈ {0, 1}`, and then `feat = (cand & −ok) | ((ok − 1) & feat)`,
//! `tden = (ok^1)·tden + ok·den`, `tnum = (ok^1)·tnum + ok·num` (products with 0.0 / 1.0 in
//! double, so an infinite rejected candidate still turns the running value into NaN — kept).
//! A candidate wins when it is *larger* than the running value (`tnum·den − tden·num < 0`).
//! A returned `t` is `tnum / tden` (0 if `tden` compares equal to 0 or unordered); `tmax` is
//! never read by any of these routines.
//!
//! Signs `sgn(x)` are taken from the sign bit of `(float)x` (`((bits >> 30) & ~1) + 1`), so
//! −0 counts as negative.

use super::common::{as_box, as_ray, as_sph, swap_linear, ucomisd_eq, ucomiss_eq};
use super::common::{DEC_MOD3, INC_MOD3};
use super::{UnprojTables, MODE_LINEAR};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{BoxPrim, Primitive, RayPrim, SpherePrim};
use crate::ids::GeomType;
use crate::math::Vec3;

// ------------------------------------------------------------------------------------ helpers

/// Sign bit of the float-rounded value (`cvtsd2ss` + `shr 31`).
#[inline]
fn sb(x: f64) -> i32 {
    ((x as f32).to_bits() >> 31) as i32
}

/// `((bits(x) sar 30) & ~1) + 1`: ±1 from the sign bit of the float.
#[inline]
fn sgnf(x: f32) -> i32 {
    (((x.to_bits() as i32) >> 30) & !1) + 1
}

/// [`sgnf`] of the float-rounded double.
#[inline]
fn sgn(x: f64) -> i32 {
    sgnf(x as f32)
}

/// Branch-free feature select: `(cand & −ok) | ((ok − 1) & feat)`.
#[inline]
fn sel(ok: i32, cand: i32, feat: i32) -> i32 {
    (cand & ok.wrapping_neg()) | (ok.wrapping_sub(1) & feat)
}

/// Branch-free value select: `(ok^1)·old + ok·new` in double.
#[inline]
fn blend(ok: i32, old: f64, new: f64) -> f64 {
    f64::from(ok ^ 1) * old + f64::from(ok) * new
}

/// Box basis entry `R[i]` (row-major, as the binary indexes `a2[i]`).
#[inline]
fn rm(b: &BoxPrim, i: usize) -> f32 {
    b.basis.m[i / 3][i % 3]
}

#[inline]
fn arr(v: Vec3) -> [f32; 3] {
    [v.x, v.y, v.z]
}

/// The double-precision `1e-20f` (9.999999682655225e-21) of the normalisation guards.
const EPS20: f64 = 9.999_999_682_655_225e-21;

/// `R^T · n` in double, rounded to float: `x = (R0·n0 + R3·n1) + R6·n2`,
/// `y = (R1·n0 + R4·n1) + R7·n2`, `z = (R2·n0 + R5·n1) + R8·n2`.
fn to_world(b: &BoxPrim, n: [f64; 3]) -> Vec3 {
    let r = |i: usize| f64::from(rm(b, i));
    let x = (r(0) * n[0] + r(3) * n[1]) + r(6) * n[2];
    let y = (r(1) * n[0] + r(4) * n[1]) + r(7) * n[2];
    let z = (r(2) * n[0] + r(5) * n[1]) + r(8) * n[2];
    Vec3::new(x as f32, y as f32, z as f32)
}

/// Normalise a double vector, `(0,0,1)` when `|v|² ≤ 1e-20f` (or NaN); `|v|²` summed
/// `(y² + x²) + z²`, scaled by `1/√|v|²`.
fn norm_d(v: [f64; 3]) -> [f64; 3] {
    let l2 = (v[1] * v[1] + v[0] * v[0]) + v[2] * v[2];
    if l2 > EPS20 {
        let inv = 1.0 / l2.sqrt();
        [v[0] * inv, v[1] * inv, v[2] * inv]
    } else {
        [0.0, 0.0, 1.0]
    }
}

// ------------------------------------------------------------------------------------ box–sph

/// sub_35185B80 (x86 0x3518A9A0): move box A along `dir` until it stops touching sphere B.
///
/// The sphere centre `l` and `dir` (`ld`) are taken into the box frame (`R·(c − c_box)`,
/// float), then everything runs in double. The box's `oriented` flag is not read (the basis is
/// always applied). `out.i_feature[1] = 0x40` is written first, also on failure.
///
/// * **`dir = 0`** (`ucomiss`/`je`: a NaN length also takes it): `d² = Σ(min(s−|l|, 0))²`
///   (`((e − |e|)·0.5)²`); fails if `d² > r²`. The number of axes with `s − |l|` negative
///   (sign bit) picks the closest feature; centre inside the box → the face of least
///   penetration. Face `k`: `t = (r + s_k) − sgn·l_k`, `n_local = −sgn·e_k`, feature
///   `0x40 | 2k | (sgn > 0)`. Edge along `k`: closest point of that edge, feature
///   `0x20 | 4k | (sgn_inc > 0) | 2(sgn_dec > 0)`. Vertex: feature `(sz>0) | 2(sy>0) | 4(sz>0)`
///   (**bug kept:** bit 0 uses the z sign instead of x). Edge/vertex: `t = r − |v − l|`,
///   `n_local = (v − l)/|v − l|` (no zero guard). `dir = R^T·n_local` is written back,
///   `n = −dir`, `pt = c + dir·r`.
/// * **directed**: candidates are the 6 faces (`t = d/a`, accepted when the sphere centre
///   projected at that `t` is inside the face's two slabs), the 12 edges (sphere hitting the
///   edge line: larger root of a quadratic, accepted when within the edge's extent) and the
///   8 vertices (larger root, no extent check); the largest `t > tmin` wins (running value
///   starts at `tmin / 1`). No candidate → 0. Face: `n = ±row_k` (float), feature
///   `0x40 | 2k | bit`; edge / vertex: `n = R^T·normalize(centre(t) − closest)` (double,
///   `(0,0,1)` below 1e-20), features `0x20 | 4k | bits` / `bits` (`x | 2y | 4z`).
///   `pt = c − n·r`.
pub fn unproject_box_sph(
    p: &mut UnprojParams,
    a: &BoxPrim,
    _fa: i32,
    b: &SpherePrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let s = [
        f64::from(a.size.x),
        f64::from(a.size.y),
        f64::from(a.size.z),
    ];
    let r = f64::from(b.r);
    let dx = b.center.x - a.center.x;
    let dy = b.center.y - a.center.y;
    let dz = b.center.z - a.center.z;
    let tmin = f64::from(p.tmin);
    let lx = (dy * rm(a, 1) + dx * rm(a, 0)) + dz * rm(a, 2);
    let lz = (dy * rm(a, 7) + dx * rm(a, 6)) + dz * rm(a, 8);
    let ly = (dy * rm(a, 4) + dx * rm(a, 3)) + dz * rm(a, 5);
    let l = [f64::from(lx), f64::from(ly), f64::from(lz)];
    let d = p.dir;
    let ldx = (d.x * rm(a, 0) + d.y * rm(a, 1)) + d.z * rm(a, 2);
    let ldy = (d.y * rm(a, 4) + d.x * rm(a, 3)) + d.z * rm(a, 5);
    let ldz = (d.y * rm(a, 7) + d.x * rm(a, 6)) + d.z * rm(a, 8);
    out.i_feature[1] = 0x40;
    let ld = [f64::from(ldx), f64::from(ldy), f64::from(ldz)];
    let dl2 = (d.y * d.y + d.x * d.x) + d.z * d.z;
    if ucomiss_eq(dl2, 0.0) {
        return box_sph_min(p, a, b, s, r, l, out);
    }

    // ---- directed: faces
    let mut tnum = tmin;
    let mut tden = 1.0f64;
    let mut feat: i32 = -1;
    for i in 0..3 {
        let a_ = ld[i].abs();
        let m = -1 - (((ld[i] as f32).to_bits() as i32 >> 30) & !1);
        let j = INC_MOD3[i] as usize;
        let k = DEC_MOD3[i] as usize;
        let dd = (r + s[i]) - f64::from(m) * l[i];
        let c1 = (a_ * l[j] - dd * ld[j]).abs() - a_ * s[j];
        let c2 = (a_ * l[k] - dd * ld[k]).abs() - a_ * s[k];
        let c3 = tnum * a_ - tden * dd;
        let ok = sb(c3) & sb(c2) & sb(c1);
        let cand = ((m + 1) >> 1) | (2 * i as i32);
        feat = sel(ok, cand, feat);
        tden = blend(ok, tden, a_);
        tnum = blend(ok, tnum, dd);
    }
    // ---- edges
    let nd = [-ld[0], -ld[1], -ld[2]];
    let r2 = r * r;
    for i in 0..3usize {
        let j = INC_MOD3[i] as usize;
        let k = DEC_MOD3[i] as usize;
        for q in 0..4i32 {
            let sx = 2 * (q & 1) - 1;
            let sy = (q & 2) - 1;
            let mut v = [0.0f64; 3];
            v[j] = f64::from(sx) * s[j];
            v[k] = f64::from(sy) * s[k];
            v[i] = 0.0;
            let pp = [l[0] - v[0], l[1] - v[1], l[2] - v[2]];
            let mut u = [0.0f64; 3];
            u[j] = pp[k];
            u[k] = -pp[j];
            let mut w = [0.0f64; 3];
            w[j] = nd[k];
            w[k] = -nd[j];
            let qa = (w[1] * w[1] + w[0] * w[0]) + w[2] * w[2];
            let qb = (w[1] * u[1] + w[0] * u[0]) + w[2] * u[2];
            let qc = ((u[1] * u[1] + u[0] * u[0]) + u[2] * u[2]) - r2;
            let disc = qb * qb - qc * qa;
            if root_ok(qb, disc) {
                let root = disc.sqrt() - qb;
                let c1 = (qa * l[i] - root * ld[i]).abs() - qa * s[i];
                let c2 = tnum * qa - tden * root;
                let ok = sb(c1) & sb(c2);
                let cand = ((sx + 1) >> 1) | (((i as i32) | 4) << 2) | (sy + 1);
                feat = sel(ok, cand, feat);
                tden = blend(ok, tden, qa);
                tnum = blend(ok, tnum, root);
            }
        }
    }
    // ---- vertices
    let qa = (nd[1] * nd[1] + nd[0] * nd[0]) + nd[2] * nd[2];
    for q in 0..8i32 {
        let sx = 2 * (q & 1) - 1;
        let sy = (q & 2) - 1;
        let sz = ((q >> 1) & 2) - 1;
        let px = l[0] - f64::from(sx) * s[0];
        let py = l[1] - f64::from(sy) * s[1];
        let pz = l[2] - f64::from(sz) * s[2];
        let qb = (nd[1] * py + nd[0] * px) + nd[2] * pz;
        let qc = ((py * py + px * px) + pz * pz) - r2;
        let disc = qb * qb - qc * qa;
        if root_ok(qb, disc) {
            let root = disc.sqrt() - qb;
            let ok = sb(tnum * qa - tden * root);
            let cand = (((sx + 1) | 0x40) >> 1) | (2 * sz + 2) | (sy + 1);
            feat = sel(ok, cand, feat);
            tden = blend(ok, tden, qa);
            tnum = blend(ok, tnum, root);
        }
    }
    if feat == -1 {
        return 0;
    }
    let t = if ucomisd_eq(tden, 0.0) {
        0.0
    } else {
        tnum / tden
    };
    out.t = t;
    if feat & 0xF0 == 0 {
        let k = (feat >> 1) as usize;
        let bit = feat & 1;
        let sgi = 2 * bit - 1;
        let sf = sgi as f32;
        out.n = Vec3::new(
            rm(a, 3 * k) * sf,
            rm(a, 3 * k + 1) * sf,
            rm(a, 3 * k + 2) * sf,
        );
        out.i_feature[0] = (((k as i32) | 0x20) * 2) | ((2 * bit) >> 1);
    } else {
        let y = l[1] - ld[1] * t;
        let z = l[2] - ld[2] * t;
        let x = l[0] - t * ld[0];
        let v = if feat & 0xF0 == 0x10 {
            let i = ((feat >> 2) & 3) as usize;
            let j = INC_MOD3[i] as usize;
            let k = DEC_MOD3[i] as usize;
            let mut v = [0.0f64; 3];
            v[j] = f64::from(2 * (feat & 1) - 1) * s[j];
            v[k] = f64::from((feat & 2) - 1) * s[k];
            v[i] = l[i] - t * ld[i];
            v
        } else {
            [
                f64::from(2 * (feat & 1) - 1) * s[0],
                f64::from((feat & 2) - 1) * s[1],
                f64::from(((feat >> 1) & 2) - 1) * s[2],
            ]
        };
        let n = norm_d([x - v[0], y - v[1], z - v[2]]);
        out.n = to_world(a, n);
        out.i_feature[0] = if feat & 0xF0 == 0x10 {
            (feat & 0xF) | 0x20
        } else {
            feat & 0xF
        };
    }
    let rf = r as f32;
    out.pt = Vec3::new(
        b.center.x - out.n.x * rf,
        b.center.y - out.n.y * rf,
        b.center.z - out.n.z * rf,
    );
    1
}

/// The root test of the quadratics: `(sign(b² − disc) | sign(b)) & !sign(disc)` on the
/// float-rounded values (a real root exists and the larger one is positive).
#[inline]
fn root_ok(qb: f64, disc: f64) -> bool {
    let x = ((qb * qb - disc) as f32).to_bits() | (qb as f32).to_bits();
    (!(disc as f32).to_bits() & x) >> 31 != 0
}

/// The `dir = 0` branch of sub_35185B80 (least-penetration unprojection).
fn box_sph_min(
    p: &mut UnprojParams,
    a: &BoxPrim,
    b: &SpherePrim,
    s: [f64; 3],
    r: f64,
    l: [f64; 3],
    out: &mut UnprojResult,
) -> i32 {
    let al = [l[0].abs(), l[1].abs(), l[2].abs()];
    let e = [s[0] - al[0], s[1] - al[1], s[2] - al[2]];
    let o = [sb(e[0]), sb(e[1]), sb(e[2])];
    let h = |x: f64| {
        let v = (x - x.abs()) * 0.5;
        v * v
    };
    let d2 = (h(e[0]) + h(e[1])) + h(e[2]);
    if d2 > r * r {
        return 0;
    }
    let mut cnt = o[0] + o[1] + o[2];
    let mut idx = o[1] + 2 * o[2];
    if cnt == 0 {
        cnt = 1;
        let t = [al[0] - s[0], al[1] - s[1], al[2] - s[2]];
        let c0 = sb(t[0] - t[1]);
        let c = c0 | (sb(t[c0 as usize] - t[2]) << 1);
        idx = c & (((c ^ 2) | 4) >> 1);
    }
    let n: [f64; 3];
    match cnt {
        1 => {
            let k = idx as usize;
            let sg = sgn(l[k]);
            let mut v = [0.0f64; 3];
            out.i_feature[0] = (((k as i32) | 0x20) * 2) | ((sg + 1) >> 1);
            v[k] = f64::from(-sg);
            n = v;
            out.t = (r + s[k]) - f64::from(sg) * l[k];
        }
        2 => {
            let k = (3 - idx) as usize;
            let i = INC_MOD3[k] as usize;
            let j = DEC_MOD3[k] as usize;
            let si = sgn(l[i]);
            let sj = sgn(l[j]);
            let mut v = [0.0f64; 3];
            v[i] = f64::from(si) * s[i];
            v[j] = f64::from(sj) * s[j];
            v[k] = l[k];
            out.i_feature[0] = ((si + 1) >> 1) | (((k as i32) | 8) << 2) | (sj + 1);
            n = edge_vertex_normal(r, [v[0] - l[0], v[1] - l[1], v[2] - l[2]], out);
        }
        _ => {
            let sx = sgn(l[0]);
            let sy = sgn(l[1]);
            let sz = sgn(l[2]);
            let ex = f64::from(sx) * s[0] - l[0];
            let ey = f64::from(sy) * s[1] - l[1];
            let ez = f64::from(sz) * s[2] - l[2];
            // Bug kept: bit 0 of the vertex id is taken from the z sign, not the x sign.
            out.i_feature[0] = (sy + 1) | (2 * sz + 2) | ((sz + 1) >> 1);
            n = edge_vertex_normal(r, [ex, ey, ez], out);
        }
    }
    p.dir = to_world(a, n);
    out.n = Vec3::new(-p.dir.x, -p.dir.y, -p.dir.z);
    let rf = r as f32;
    out.pt = Vec3::new(
        rf * p.dir.x + b.center.x,
        rf * p.dir.y + b.center.y,
        rf * p.dir.z + b.center.z,
    );
    1
}

/// Edge / vertex case of the `dir = 0` branch: `t = r − |e|`, `n = e·(1/|e|)` (no zero guard).
fn edge_vertex_normal(r: f64, e: [f64; 3], out: &mut UnprojResult) -> [f64; 3] {
    let len = ((e[1] * e[1] + e[0] * e[0]) + e[2] * e[2]).sqrt();
    out.t = r - len;
    let inv = 1.0 / len;
    [e[0] * inv, e[1] * inv, e[2] * inv]
}

// ------------------------------------------------------------------------------------ ray–box

/// sub_35178540 (x86 0x3518CAA0): move the ray segment A (`origin`, `origin + dir`) along
/// `dir` until it stops intersecting box B.
///
/// The segment ends `l0`, `l1` and `dir` (`ld`) are taken into the box frame (float), then
/// double. The running value starts at `0 / 1` (**`tmin` is not read**). There is no `dir = 0`
/// special case.
/// * Faces `i` (`sg = sgn(ld_i)`): the segment end that leads along `sg` (`e` = sign bit of
///   `sg·(l1_i − l0_i)`) is pushed to the plane `sg·x_i = s_i`: `t = (s_i − sg·P_e,i)/(sg·ld_i)`,
///   accepted when the moved end lies within the two other slabs. Candidate `0x40 | 4e | i`.
/// * Edges along `i` (skipped if `|axis_i × (l1 − l0)|² ≤ 1e-8·|l1 − l0|²`): the box edge
///   facing the segment, `t` from the line–line distance along the common normal, accepted when
///   the contact lies inside both the edge and the segment. Candidate
///   `0x80 | 4i | (sgn_inc > 0) | 2(sgn_dec > 0)`.
///
/// Result: none → 0 (nothing written). Face: `t = tnum/tden`, `pt` = that segment end moved by
/// `dir·t`, `n = −sg·row_i`, features `0x80 | e` / `0x40 | 2i | (sg > 0)`. Edge:
/// `t = (1/(|u|²·tden))·tnum·|u|²` (no zero check), `pt` = segment point moved by `dir·t`,
/// `n = R^T·u` oriented towards the box centre, features `0xA0 | e₂` / `0x20 | (feat & 0xF)`
/// (**bug kept:** `e₂` is the end bit left over from the *last* face iteration, axis z).
pub fn unproject_ray_box(
    p: &mut UnprojParams,
    a: &RayPrim,
    _fa: i32,
    b: &BoxPrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let o = a.origin;
    let rd = a.dir;
    let c = b.center;
    let r = |i: usize| rm(b, i);
    let p0x = o.x - c.x;
    let p0y = o.y - c.y;
    let p0z = o.z - c.z;
    let l0x = (p0x * r(0) + p0y * r(1)) + p0z * r(2);
    let l0y = (p0y * r(4) + p0x * r(3)) + p0z * r(5);
    let l0z = (p0y * r(7) + p0x * r(6)) + p0z * r(8);
    let p1x = (o.x + rd.x) - c.x;
    let p1y = (o.y + rd.y) - c.y;
    let p1z = (o.z + rd.z) - c.z;
    let l1x = (p1x * r(0) + p1y * r(1)) + p1z * r(2);
    let l1y = (p1x * r(3) + p1y * r(4)) + p1z * r(5);
    let l1z = (p1x * r(6) + p1y * r(7)) + p1z * r(8);
    let d = p.dir;
    let ldxf = (r(1) * d.y + d.x * r(0)) + r(2) * d.z;
    let ldyf = (r(3) * d.x + r(4) * d.y) + r(5) * d.z;
    let ldzf = (r(6) * d.x + r(7) * d.y) + r(8) * d.z;
    let l0 = [f64::from(l0x), f64::from(l0y), f64::from(l0z)];
    let l1 = [f64::from(l1x), f64::from(l1y), f64::from(l1z)];
    let pe = [l0, l1];
    let ld = [f64::from(ldxf), f64::from(ldyf), f64::from(ldzf)];
    let sg = [sgnf(ldxf), sgnf(ldyf), sgnf(ldzf)];
    let size = arr(b.size);

    let mut tnum = 0.0f64;
    let mut tden = 1.0f64;
    let mut feat: i32 = -1;
    let mut end = 0i32;
    for i in 0..3usize {
        let sgf = f64::from(sg[i]);
        let si = f64::from(size[i]);
        end = sb(sgf * l1[i] - sgf * l0[i]);
        let a_ = sgf * ld[i];
        let pp = &pe[end as usize];
        let dd = si - sgf * pp[i];
        let j = INC_MOD3[i] as usize;
        let k = DEC_MOD3[i] as usize;
        let c1 = (a_ * pp[j] + dd * ld[j]).abs() - f64::from(size[j]) * a_;
        let c2 = (a_ * pp[k] + dd * ld[k]).abs() - f64::from(size[k]) * a_;
        let cand = ((end | 0x10) << 2) | i as i32;
        let c3 = tnum * a_ - tden * dd;
        let ok = sb(c3) & sb(c2) & sb(c1);
        feat = sel(ok, cand, feat);
        tnum = blend(ok, tnum, dd);
        tden = blend(ok, tden, a_);
    }

    let rdl = [l1[0] - l0[0], l1[1] - l0[1], l1[2] - l0[2]];
    let thr = ((rdl[1] * rdl[1] + rdl[0] * rdl[0]) + rdl[2] * rdl[2]) * 1e-8;
    for i in 0..3usize {
        let j = INC_MOD3[i] as usize;
        let k = DEC_MOD3[i] as usize;
        let mut u = [0.0f64; 3];
        u[j] = rdl[k];
        u[k] = -rdl[j];
        let uu = (u[1] * u[1] + u[0] * u[0]) + u[2] * u[2];
        if uu <= thr || uu.is_nan() {
            continue;
        }
        let un = (u[1] * ld[1] + u[0] * ld[0]) + u[2] * ld[2];
        let sg1 = sgn(un);
        let sj = sgn(u[j]) * sg1;
        let sk = sgn(u[k]) * sg1;
        let a_ = un * f64::from(sg1);
        let mut v = [0.0f64; 3];
        v[i] = f64::from(-size[i]);
        v[j] = f64::from(sj as f32 * size[j]);
        v[k] = f64::from(sk as f32 * size[k]);
        let q = [v[0] - l0[0], v[1] - l0[1], v[2] - l0[2]];
        let bb = ((q[1] * u[1] + q[0] * u[0]) + q[2] * u[2]) * f64::from(sg1);
        let w = [
            q[0] * a_ - ld[0] * bb,
            q[1] * a_ - ld[1] * bb,
            q[2] * a_ - ld[2] * bb,
        ];
        let mut vv = [0.0f64; 3];
        vv[j] = w[k];
        vv[k] = -w[j];
        let ua = uu * a_;
        let c1 = (((vv[0] * u[0] + vv[1] * u[1]) + vv[2] * u[2]) * 2.0 - ua).abs() - ua.abs();
        let x = ((w[1] * rdl[2] - w[2] * rdl[1]) * u[0] + (w[2] * rdl[0] - rdl[2] * w[0]) * u[1])
            + (rdl[1] * w[0] - w[1] * rdl[0]) * u[2];
        let sua = f64::from(size[i]) * ua;
        let c2 = (x - sua).abs() - sua;
        let c3 = tnum * a_ - tden * bb;
        let ok = sb(c3) & sb(c2) & sb(c1);
        let cand = ((sj + 1) >> 1) | (((i as i32) | 0x20) << 2) | (sk + 1);
        feat = sel(ok, cand, feat);
        tden = blend(ok, tden, a_);
        tnum = blend(ok, tnum, bb);
    }

    if feat == -1 {
        return 0;
    }
    if feat & 0xC0 == 0x40 {
        let e = (feat >> 2) & 3;
        let i = (feat & 3) as usize;
        let t = if ucomisd_eq(tden, 0.0) {
            0.0
        } else {
            tnum / tden
        };
        out.t = t;
        let tf = t as f32;
        let ef = e as f32;
        out.pt = Vec3::new(
            (rd.x * ef + o.x) + d.x * tf,
            (rd.y * ef + o.y) + d.y * tf,
            (rd.z * ef + o.z) + d.z * tf,
        );
        let nf = (-sg[i]) as f32;
        out.n = Vec3::new(r(3 * i) * nf, r(3 * i + 1) * nf, r(3 * i + 2) * nf);
        out.i_feature[0] = e | 0x80;
        out.i_feature[1] = (((i as i32) | 0x20) * 2) | ((sg[i] + 1) >> 1);
    } else {
        let i = ((feat >> 2) & 3) as usize;
        let j = INC_MOD3[i] as usize;
        let k = DEC_MOD3[i] as usize;
        let mut u = [0.0f64; 3];
        u[j] = rdl[k];
        u[k] = -rdl[j];
        let un = (u[1] * ld[1] + u[0] * ld[0]) + u[2] * ld[2];
        let sg1 = sgn(un);
        let sj = sgn(u[j]) * sg1;
        let sk = sgn(u[k]) * sg1;
        let mut v = [0.0f64; 3];
        v[i] = f64::from(-size[i]);
        v[j] = f64::from(sj as f32 * size[j]);
        v[k] = f64::from(sk as f32 * size[k]);
        let w = [
            (v[0] - l0[0]) * tden - ld[0] * tnum,
            (v[1] - l0[1]) * tden - ld[1] * tnum,
            (v[2] - l0[2]) * tden - ld[2] * tnum,
        ];
        let mut vv = [0.0f64; 3];
        vv[j] = w[k];
        vv[k] = -w[j];
        let num = (vv[0] * u[0] + vv[1] * u[1]) + vv[2] * u[2];
        let uu = (u[1] * u[1] + u[0] * u[0]) + u[2] * u[2];
        let inv = 1.0 / (uu * tden);
        let t = (inv * tnum) * uu;
        out.t = t;
        let tf = t as f32;
        let numf = num as f32;
        let invf = inv as f32;
        out.pt = Vec3::new(
            ((rd.x * numf) * invf + o.x) + d.x * tf,
            ((rd.y * numf) * invf + o.y) + d.y * tf,
            ((rd.z * numf) * invf + o.z) + d.z * tf,
        );
        let n = to_world(b, u);
        let dot = ((c.y - out.pt.y) * n.y + n.x * (c.x - out.pt.x)) + (c.z - out.pt.z) * n.z;
        let s = sgnf(dot) as f32;
        out.n = Vec3::new(s * n.x, s * n.y, s * n.z);
        // Bug kept: the A feature uses the end bit of the last face iteration (axis z).
        out.i_feature[0] = end | 0xA0;
        out.i_feature[1] = (feat & 0xF) | 0x20;
    }
    1
}

// ------------------------------------------------------------------------------------ ray–sph

/// sub_3517BDA0 (x86 0x35190A00): move the ray segment A along `dir` until it stops
/// intersecting sphere B.
///
/// The running value starts at `tmin / 1`; there is no `dir = 0` special case.
/// * Segment ends `e = 0, 1` (`origin + e·dir_ray`): both roots of `|p + dir·t|² = r²`
///   (`(−b ∓ √disc)/|dir|²`, skipped if `disc < 0` or NaN) are offered in turn; candidate
///   `0x40 | e`.
/// * The segment's line (cross products with the ray direction): both roots of the
///   line–centre distance `= r`, each accepted only if the closest point lies inside the
///   segment; candidate `0x82` (the end counter after the loop, `| 0x80`).
///
/// None → 0. End: `t = tnum/tden` (0 if `tden` is 0 or NaN), `pt` = that end moved by
/// `dir·t`, feature A `0x80 | e`. Line: `t = (1/(|R|²·tden))·tnum·|R|²`, `pt` = closest point
/// on the line moved by `dir·t`, feature A `0xA2`. Both: `n = c − pt` (not normalised),
/// feature B `0x40`.
pub fn unproject_ray_sph(
    p: &mut UnprojParams,
    a: &RayPrim,
    _fa: i32,
    b: &SpherePrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let o = a.origin;
    let rd = a.dir;
    let c = b.center;
    let d = p.dir;
    let r2 = f64::from(b.r * b.r);
    let dx = f64::from(d.x);
    let dy = f64::from(d.y);
    let dz = f64::from(d.z);
    let dd = (dy * dy + dx * dx) + dz * dz;
    let mut tnum = f64::from(p.tmin);
    let mut tden = 1.0f64;
    let mut feat: i32 = -1;
    for e in 0..2i32 {
        let ef = e as f32;
        let pz = f64::from((ef * rd.z + o.z) - c.z);
        let px = f64::from((o.x + ef * rd.x) - c.x);
        let py = f64::from((ef * rd.y + o.y) - c.y);
        let qb = (dy * py + dx * px) + dz * pz;
        let qc = ((py * py + px * px) + pz * pz) - r2;
        let disc = qb * qb - qc * dd;
        // comisd + jb: negative or unordered skips
        if disc >= 0.0 {
            let sq = disc.sqrt();
            let cand = e | 0x40;
            let root1 = -qb - sq;
            let root2 = sq - qb;
            let ok1 = sb(tnum * dd - tden * root1);
            let f1 = sel(ok1, cand, feat);
            let den1 = blend(ok1, tden, dd);
            let num1 = blend(ok1, tnum, root1);
            let ok2 = sb(num1 * dd - den1 * root2);
            feat = sel(ok2, cand, f1);
            tden = blend(ok2, den1, dd);
            tnum = blend(ok2, num1, root2);
        }
    }
    let e_end = 2i32;

    let px = f64::from(o.x - c.x);
    let py = f64::from(o.y - c.y);
    let pz = f64::from(o.z - c.z);
    let rx = f64::from(rd.x);
    let ry = f64::from(rd.y);
    let rz = f64::from(rd.z);
    let cx = pz * ry - py * rz;
    let cy = rz * px - pz * rx;
    let cz = py * rx - ry * px;
    let ex = dz * ry - dy * rz;
    let ey = rz * dx - dz * rx;
    let ez = dy * rx - ry * dx;
    let ee = (ex * ex + ey * ey) + ez * ez;
    let rr = (rx * rx + ry * ry) + rz * rz;
    let qb = (ex * cx + ey * cy) + ez * cz;
    let qc = (((cx * cx + cy * cy) + cz * cz) - rr * r2) * ee;
    let disc = qb * qb - qc;
    if disc >= 0.0 {
        let sq = disc.sqrt();
        let cand = e_end | 0x80;
        let rre = rr * ee;
        let (rxf, ryf, rzf) = (rd.x, rd.y, rd.z);
        let eef = ee as f32;
        // within-segment test of a root: |2·(R·((c − o)·ee − root·dir)) − |R|²ee| − |R|²ee
        let inside = |root: f64| {
            let rf = root as f32;
            let x = (c.x - o.x) * eef - rf * d.x;
            let y = (c.y - o.y) * eef - rf * d.y;
            let z = (c.z - o.z) * eef - rf * d.z;
            let s = (rxf * x + ryf * y) + rzf * z;
            (f64::from(s) * 2.0 - rre).abs() - rre
        };
        let root1 = -qb - sq;
        let ok1 = sb(inside(root1)) & sb(tnum * ee - tden * root1);
        let f1 = sel(ok1, cand, feat);
        let den1 = blend(ok1, tden, ee);
        let num1 = blend(ok1, tnum, root1);
        let root2 = root1 + sq * 2.0;
        let ok2 = sb(inside(root2)) & sb(num1 * ee - den1 * root2);
        feat = sel(ok2, cand, f1);
        tden = blend(ok2, den1, ee);
        tnum = blend(ok2, num1, root2);
    }

    if feat == -1 {
        return 0;
    }
    match feat & 0xC0 {
        0x40 => {
            let e = feat & 3;
            let t = if ucomisd_eq(tden, 0.0) {
                0.0
            } else {
                tnum / tden
            };
            out.t = t;
            let tf = t as f32;
            let ef = e as f32;
            out.pt = Vec3::new(
                (rd.x * ef + o.x) + d.x * tf,
                (rd.y * ef + o.y) + d.y * tf,
                (rd.z * ef + o.z) + d.z * tf,
            );
            out.n = Vec3::new(c.x - out.pt.x, c.y - out.pt.y, c.z - out.pt.z);
            out.i_feature[0] = e | 0x80;
        }
        0x80 => {
            let tnf = tnum as f32;
            let tdf = tden as f32;
            let x = (c.x - o.x) * tdf - d.x * tnf;
            let y = (c.y - o.y) * tdf - d.y * tnf;
            let z = (c.z - o.z) * tdf - d.z * tnf;
            let s = f64::from((rd.x * x + rd.y * y) + rd.z * z);
            let inv = 1.0 / (rr * tden);
            let k = inv * s;
            let t = (inv * tnum) * rr;
            out.t = t;
            let tf = t as f32;
            out.pt = Vec3::new(
                (f64::from(o.x) + k * rx) as f32 + d.x * tf,
                (f64::from(o.y) + ry * k) as f32 + d.y * tf,
                (f64::from(o.z) + rz * k) as f32 + d.z * tf,
            );
            out.n = Vec3::new(c.x - out.pt.x, c.y - out.pt.y, c.z - out.pt.z);
            out.i_feature[0] = e_end | 0xA0;
        }
        // unreachable in practice (candidates are 0x40|e and 0x82): returns 1, writes nothing
        _ => return 1,
    }
    out.i_feature[1] = 0x40;
    1
}

// ------------------------------------------------------------------------------------ cells

fn cell_box_sph(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_box_sph(p, as_box(a), fa, as_sph(b), fb, out)
}

/// 0x35186F70: sph–box = swapped box–sph (335-byte wrapper).
fn cell_sph_box(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    let (s, bx) = (as_sph(a), as_box(b));
    swap_linear(p, out, |p, out| unproject_box_sph(p, bx, fb, s, fa, out))
}

fn cell_ray_box(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_ray_box(p, as_ray(a), fa, as_box(b), fb, out)
}

/// 0x351794F0: box–ray = swapped ray–box (335-byte wrapper).
fn cell_box_ray(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    let (bx, r) = (as_box(a), as_ray(b));
    swap_linear(p, out, |p, out| unproject_ray_box(p, r, fb, bx, fa, out))
}

fn cell_ray_sph(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_ray_sph(p, as_ray(a), fa, as_sph(b), fb, out)
}

/// 0x3517C880: sph–ray = swapped ray–sph (335-byte wrapper).
fn cell_sph_ray(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    let (s, r) = (as_sph(a), as_ray(b));
    swap_linear(p, out, |p, out| unproject_ray_sph(p, r, fb, s, fa, out))
}

/// Fill the cells this group owns: `[0][box][sph]`, `[0][sph][box]`, `[0][ray][box]`,
/// `[0][box][ray]`, `[0][ray][sph]`, `[0][sph][ray]`.
pub fn fill(t: &mut UnprojTables) {
    use GeomType::{Box, Ray, Sphere};
    t.set(MODE_LINEAR, Box, Sphere, cell_box_sph);
    t.set(MODE_LINEAR, Sphere, Box, cell_sph_box);
    t.set(MODE_LINEAR, Ray, Box, cell_ray_box);
    t.set(MODE_LINEAR, Box, Ray, cell_box_ray);
    t.set(MODE_LINEAR, Ray, Sphere, cell_ray_sph);
    t.set(MODE_LINEAR, Sphere, Ray, cell_sph_ray);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn unit_box(c: Vec3) -> BoxPrim {
        BoxPrim {
            center: c,
            size: Vec3::new(1.0, 1.0, 1.0),
            ..BoxPrim::default()
        }
    }

    #[test]
    fn box_sph_min_distance_face() {
        let mut p = UnprojParams::default();
        let mut out = UnprojResult::default();
        let s = SpherePrim {
            center: Vec3::new(1.5, 0.0, 0.0),
            r: 1.0,
        };
        let r = unproject_box_sph(&mut p, &unit_box(Vec3::ZERO), -1, &s, -1, &mut out);
        assert_eq!(r, 1);
        assert_eq!(out.t, 0.5);
        assert_eq!(p.dir, Vec3::new(-1.0, 0.0, 0.0));
        assert_eq!(out.n, Vec3::new(1.0, 0.0, 0.0));
        assert_eq!(out.pt, Vec3::new(0.5, 0.0, 0.0));
        assert_eq!(out.i_feature, [0x41, 0x40]);
    }

    #[test]
    fn box_sph_directed_face() {
        let mut p = UnprojParams {
            dir: Vec3::new(-1.0, 0.0, 0.0),
            ..UnprojParams::default()
        };
        let mut out = UnprojResult::default();
        let s = SpherePrim {
            center: Vec3::new(1.5, 0.0, 0.0),
            r: 1.0,
        };
        let r = unproject_box_sph(&mut p, &unit_box(Vec3::ZERO), -1, &s, -1, &mut out);
        assert_eq!(r, 1);
        assert_eq!(out.t, 0.5);
        assert_eq!(out.n, Vec3::new(1.0, 0.0, 0.0));
        assert_eq!(out.i_feature, [0x41, 0x40]);
    }

    #[test]
    fn ray_sph_end_point() {
        // segment from x=0.5 to x=2.5 inside / through a unit sphere at the origin, pushed +x
        let mut p = UnprojParams {
            dir: Vec3::new(1.0, 0.0, 0.0),
            ..UnprojParams::default()
        };
        let mut out = UnprojResult::default();
        let ray = RayPrim {
            origin: Vec3::new(0.5, 0.0, 0.0),
            dir: Vec3::new(2.0, 0.0, 0.0),
        };
        let s = SpherePrim {
            center: Vec3::ZERO,
            r: 1.0,
        };
        assert_eq!(unproject_ray_sph(&mut p, &ray, -1, &s, -1, &mut out), 1);
        assert_eq!(out.t, 0.5);
        assert_eq!(out.pt, Vec3::new(1.0, 0.0, 0.0));
        assert_eq!(out.i_feature, [0x80, 0x40]);
    }
}
