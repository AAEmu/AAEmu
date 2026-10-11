//! tri–cyl linear unprojection (x64 0x351716D0, 15 443 B; x86 0x35179410) and its swap wrapper
//! cyl–tri (x64 0x35175330, 335 B; x86 0x3517DA30).
//!
//! # sub_351716D0 — `[0][tri][cyl]`
//!
//! Moves the triangle A along `dir` by the largest `t ≤ tmax` at which some feature pair of the
//! triangle and the cylinder B still touches, i.e. the distance that separates them. Candidates
//! are kept as a fraction `num / den` (`den > 0`); a candidate replaces the current best when
//! `num/den` is larger (`best.num·den − best.den·num < 0`), when its `t` is below `tmax`
//! (`num − tmax·den < 0`) and when its contact lies on both features — every test is the sign bit
//! of a float-rounded double, combined branch-free into a `feature mask` and a 0/1 blend of
//! `num`/`den` (`den = (1−e)·den + e·den'`, so ∞/NaN propagate exactly like the binary).
//! The best starts as `0 / 1`, the mask as −1.
//!
//! Phases (in binary order):
//! 1. **vertex – side** (mask `0x40|i`): the triangle vertices against the infinite cylinder
//!    (quadratic in double with `dir × axis`), clipped to `|h| ≤ hh`.
//! 2. **edge – side** (`0x60|i`): each edge line against the cylinder surface (axis × edge),
//!    the contact must lie inside the edge and inside the height.
//! 3. Only if the cylinder feature id `fb ≠ 0x43` (`(fb−0x43) ^ (fb−0x44)` sign test, which also
//!    excludes `fb = i32::MIN + 0x43`):
//!    * **cap face – triangle face** (mask = sign bit of `axis·n`, i.e. 0 or 1): the cap rim point
//!      deepest towards the triangle plane must project inside the triangle;
//!    * **vertex – cap** (`0x20 | v<<1 | s`): the triangle vertex lowest along `±axis` (sign of
//!      `dir·axis`) against the cap plane, inside the cap radius;
//!    * **edge – cap rim** (`0x80 | i<<1 | s`, `s` = cap −1/+1): each edge against both rim
//!      circles (quadratic), contact inside the edge;
//!    * then, if `||axis·n| − 1| < 1e-4` (float, `comiss`/`jae`, NaN enters): the cap is parallel
//!      to the triangle. If the cap facing the motion is reached later than the best and before
//!      `tmax`, the result is written right away (`n` = triangle normal, `feat[1]` = cap face
//!      0x41/0x42) — vertex 0 inside the cap (0x80), cap centre inside the triangle (0x40),
//!      or the first edge crossing the rim circle (0xA0|i); if no edge crosses, `n`/`feat[1]` stay
//!      written and the function continues to the final step.
//!    * otherwise, each edge nearly perpendicular to the axis (`(axis·e)² < 1e-4·|e|²`) against
//!      the cap facing the motion: the first edge whose plane intersection is later than the best
//!      and before `tmax` and lands inside the cap disc (projection, end vertex or start vertex)
//!      returns `n = −axis·s`, features `0xA0|i` / cap face. `t` and `pt` are written before the
//!      disc checks, so rejected edges leave them behind.
//! 4. **Final**: fail if the mask is −1 or `|den| < 1e-20`; otherwise build `t = num/den`
//!    (`0` if `den == 0`), the contact point and normal for the winning feature pair:
//!    `0x40|i` vertex–side (`n` = radial from the point to the axis, features `0x80|i`/0x40),
//!    `0x60|i` edge–side (closest point on the edge, `0xA0|i`/0x40), `0x20…` vertex–cap
//!    (`n = ±axis`, `0x80|v`/`0x41+s`), `0x80…` edge–rim (`n` = rim tangent × edge, `0xA0|i`/
//!    `0x20|s`), `0/1` cap face (`n` = triangle normal, 0x40/`0x20|mask`).
//!
//! Inputs read: `dir` and `tmax` only (`tmin`, `min_pt_dist`, `max_cos` are ignored; `dir` is
//! never written back, a zero `dir` just makes every quadratic degenerate), the cylinder feature
//! id `fb`. The triangle feature id and the contact area (7th argument) are never read.
//! `out.cos_t` is never written. Static tables: `dword_3520A3A0` = `inc_mod3` (the edge end
//! vertex); `dword_3520A3AC` (entry 3 of [`TAB_3520A3A0`](super::common::TAB_3520A3A0)) is only
//! the loop-end address, never read.
//!
//! Kept oddities: the parallel-cap branch writes `n`/`feat[1]` before its rim loop and keeps
//! them when no edge crosses the rim (the final step may then still return 0); the non-parallel
//! cap branch writes `t`/`pt` of each edge that passes the `t` test before checking the disc, so a
//! rejected edge leaves them behind; the cap tests are skipped only for the cylinder feature 0x43
//! (the triangle feature is ignored).
//!
//! NaN inputs: comparisons follow the binary (`comisd` unordered semantics, sign-bit tests), but
//! when *both* operands of a commutative `+`/`*` are NaNs of different sign, SSE returns the first
//! one and LLVM may commute the operands, so the NaN sign (and through the sign-bit tests the
//! result) can differ for NaN coordinates (≈0.4 % of cases with one NaN input word). Finite inputs
//! — including `tmax = ∞` and the internal 0·∞ NaNs it produces — match bit for bit.
//!
//! # sub_35175330 — `[0][cyl][tri]`
//!
//! The 335-byte linear swap wrapper ([`swap_linear`]): it calls sub_351716D0 with
//! `(b, fb, a, fa)` and passes the area pointer through (the callee ignores it).

use super::common::{as_cyl, as_tri, le_or_nan_d, lt_or_nan, lt_or_nan_d, swap_linear, ucomisd_eq};
use super::{UnprojTables, MODE_LINEAR};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{CylinderPrim, Primitive, Triangle};
use crate::ids::GeomType;
use crate::math::Vec3;

/// `dword_3520A3A0` = `inc_mod3`: end vertex of edge `i`.
const INC: [usize; 3] = [1, 2, 0];

#[inline]
fn d(x: f32) -> f64 {
    f64::from(x)
}

/// Sign bit of a float (1 for negative, including −0 and negative NaNs).
#[inline]
fn sbit(x: f32) -> i32 {
    (x.to_bits() >> 31) as i32
}

/// `((bits >> 30) & ~1) + 1`: −1 if the sign bit is set, else +1.
#[inline]
fn pm1(x: f32) -> i32 {
    (((x.to_bits() as i32) >> 30) & !1) + 1
}

/// `ucomisd x, 0; je` guarded division: `0` if `den` is zero or NaN.
#[inline]
fn div0(num: f64, den: f64) -> f64 {
    if ucomisd_eq(den, 0.0) {
        0.0
    } else {
        num / den
    }
}

/// The running best: feature mask and `t = num / den` (binary: `esi`, `xmm15`, `xmm14`).
struct Best {
    mask: i32,
    den: f64,
    num: f64,
}

impl Best {
    /// Branch-free replace: `mask = (feat & −e) | ((e−1) & mask)`,
    /// `den = (e^1)·den + e·den'`, `num = (e^1)·num + e·num'`.
    #[inline]
    fn take(&mut self, e: i32, feat: i32, den: f64, num: f64) {
        self.mask = (feat & e.wrapping_neg()) | ((e - 1) & self.mask);
        let k = f64::from(e ^ 1);
        let m = f64::from(e);
        self.den = k * self.den + m * den;
        self.num = k * self.num + m * num;
    }
}

/// The three-signed-area test of the cap-plane point against a triangle edge `a → b` (phases 3
/// and the parallel-cap face test): `q` is the (float-rounded) scaled point relative to `a`.
#[inline]
fn edge_side(q: [f32; 3], a: Vec3, b: Vec3, n: Vec3) -> f32 {
    let ex = b.x - a.x;
    let ey = b.y - a.y;
    let ez = b.z - a.z;
    let [qx, qy, qz] = q;
    ((qz * ey - qy * ez) * n.x + n.y * (qx * ez - ex * qz)) + n.z * (ex * qy - qx * ey)
}

/// sub_351716D0: linear unprojection of triangle A from cylinder B (see the module docs).
/// `_ftri` is not read; `fcyl == 0x43` skips the cap tests.
#[allow(
    clippy::too_many_lines,
    clippy::similar_names,
    clippy::many_single_char_names
)]
pub fn unproject_tri_cyl(
    p: &mut UnprojParams,
    tri: &Triangle,
    _ftri: i32,
    cyl: &CylinderPrim,
    fcyl: i32,
    out: &mut UnprojResult,
) -> i32 {
    let dir = p.dir;
    let tmax = d(p.tmax);
    let c = cyl.center;
    let (ax, ay, az) = (cyl.axis.x, cyl.axis.y, cyl.axis.z);
    let (axd, ayd, azd) = (d(ax), d(ay), d(az));
    let hh = cyl.hh;
    let pt = &tri.pt;

    let r2 = d(cyl.r * cyl.r);
    let mut best = Best {
        mask: -1,
        den: 1.0,
        num: 0.0,
    };
    // [rbp] — only set by the cap block, read by the cap-face result
    let mut nlen = 0.0f64;

    // ---- phase 1: vertices vs the cylinder side
    let cx = d(dir.y * az - dir.z * ay);
    let cy = d(dir.z * ax - dir.x * az);
    let cz = d(dir.x * ay - dir.y * ax);
    let l2 = (cy * cy + cx * cx) + cz * cz;
    for (i, q) in pt.iter().enumerate() {
        let dx = d(q.x - c.x);
        let dz = d(q.z - c.z);
        let dy = d(q.y - c.y);
        let v27 = azd * dy - ayd * dz;
        let v28 = dz * axd - azd * dx;
        let v29 = ayd * dx - dy * axd;
        let b = (cy * v28 + cx * v27) + cz * v29;
        let disc = b * b - (((v28 * v28 + v27 * v27) + v29 * v29) - r2) * l2;
        if disc >= 0.0 {
            let num = disc.sqrt() - b;
            let nf = num as f32;
            let h = ((ayd * (d(nf * dir.y) + dy * l2) + axd * (d(nf * dir.x) + dx * l2))
                + azd * (d(nf * dir.z) + dz * l2))
                .abs()
                - d(hh) * l2;
            let h = h as f32;
            let cmp = (best.num * l2 - best.den * num) as f32;
            let tm = (num - tmax * l2) as f32;
            let e = sbit(cmp) & sbit(tm) & sbit(h);
            best.take(e, i as i32 | 0x40, l2, num);
        }
    }

    // ---- phase 2: edges vs the cylinder side
    for i in 0..3 {
        let a = pt[i];
        let b = pt[INC[i]];
        let ex = d(b.x - a.x);
        let ey = d(b.y - a.y);
        let ez = d(b.z - a.z);
        let wy = d(a.y - c.y);
        let wz = d(a.z - c.z);
        let wx = d(a.x - c.x);
        let mx = azd * ey - ayd * ez;
        let my = ez * axd - azd * ex;
        let mz = ayd * ex - ey * axd;
        let l = (my * my + mx * mx) + mz * mz;
        let w = (my * wy + mx * wx) + mz * wz;
        let v50 = (dir.y * (my as f32) + dir.x * (mx as f32)) + dir.z * (mz as f32);
        let bq = w * d(v50);
        let aq = d(v50) * d(v50);
        let disc = bq * bq - (w * w - l * r2) * aq;
        if disc >= 0.0 {
            let af = aq as f32;
            let num = disc.sqrt() - bq;
            let nf = num as f32;
            let v56 = d((c.z - a.z) * af - nf * dir.z);
            let v57 = d((c.y - a.y) * af - nf * dir.y);
            let v58 = d((c.x - a.x) * af - nf * dir.x);
            let la = l * aq;
            let v59 = (((axd * v56 - v58 * azd) * my + (azd * v57 - ayd * v56) * mx)
                + (v58 * ayd - axd * v57) * mz)
                * 2.0
                - la;
            let v60 = ((v56 * ex - v58 * ez) * my + (v57 * ez - v56 * ey) * mx)
                + (v58 * ey - v57 * ex) * mz;
            let s1 = (v59.abs() - la) as f32;
            let s2 = (v60.abs() - d(hh) * la) as f32;
            let cmp = (best.num * aq - best.den * num) as f32;
            let tm = (num - tmax * aq) as f32;
            let e = sbit(cmp) & sbit(tm) & sbit(s2) & sbit(s1);
            best.take(e, i as i32 | 0x60, aq, num);
        }
    }

    // `(fb − 0x43) ^ (fb − 0x44)` negative ⇒ skip the cap tests
    let fe = fcyl.wrapping_sub(0x43);
    let skip_caps = (fe.wrapping_sub(1) ^ fe) < 0;
    if !skip_caps {
        if let Some(r) = caps(p, tri, cyl, out, &mut best, &mut nlen, r2) {
            return r;
        }
    }
    finish(p, tri, cyl, out, &best, nlen)
}

/// Phase 3 of sub_351716D0 (cap tests). `Some(1)` when it returned directly; `None` continues
/// to the final step.
#[allow(
    clippy::too_many_lines,
    clippy::similar_names,
    clippy::many_single_char_names
)]
fn caps(
    p: &UnprojParams,
    tri: &Triangle,
    cyl: &CylinderPrim,
    out: &mut UnprojResult,
    best: &mut Best,
    nlen: &mut f64,
    r2: f64,
) -> Option<i32> {
    let dir = p.dir;
    let tmax = d(p.tmax);
    let c = cyl.center;
    let (ax, ay, az) = (cyl.axis.x, cyl.axis.y, cyl.axis.z);
    let (axd, ayd, azd) = (d(ax), d(ay), d(az));
    let hh = cyl.hh;
    let pt = &tri.pt;
    let nn = tri.n;
    let p0 = pt[0];

    // ---- 3a: cap face vs triangle face
    let v67 = (ay * nn.y + ax * nn.x) + az * nn.z;
    let v68 = d(ax * v67 - nn.x);
    let v69 = d(ay * v67 - nn.y);
    let v70 = d(az * v67 - nn.z);
    *nlen = ((v69 * v69 + v68 * v68) + v70 * v70).sqrt();
    let nlen = *nlen;
    let v71 = sbit(v67);
    let v72 = ((2 * v71 - 1) as f32) * hh;
    let v74 = d(c.x + ax * v72);
    let v75 = d(c.y + ay * v72);
    let v76 = d(c.z + az * v72);
    let rd = d(cyl.r);
    let dn = (dir.y * nn.y + nn.x * dir.x) + dir.z * nn.z;
    let s77 = f64::from(pm1(dn));
    let wsum = (d(nn.y) * ((v75 - d(p0.y)) * nlen + rd * v69)
        + d(nn.x) * ((v74 - d(p0.x)) * nlen + rd * v68))
        + d(nn.z) * ((v76 - d(p0.z)) * nlen + rd * v70);
    let v394 = wsum * s77;
    let v79 = v394 as f32;
    let v78 = d(dn) * s77;
    let v80 = rd * v78;
    let v416 = v78 * nlen;
    let v85 = (v416 * v74 + v80 * v68) - d(v79 * dir.x);
    let v86 = (v416 * v75 + v80 * v69) - d(v79 * dir.y);
    let v87 = (v416 * v76 + v80 * v70) - d(v79 * dir.z);
    let lf = v416 as f32;
    let q3 = |a: Vec3| {
        [
            (v85 - d(lf * a.x)) as f32,
            (v86 - d(lf * a.y)) as f32,
            (v87 - d(lf * a.z)) as f32,
        ]
    };
    let s0 = edge_side(q3(pt[0]), pt[0], pt[1], nn);
    let s1 = edge_side(q3(pt[1]), pt[1], pt[2], nn);
    let s2 = edge_side(q3(pt[2]), pt[2], pt[0], nn);
    let cmp = (best.num * v416 - best.den * v394) as f32;
    let tm = (v394 - tmax * v416) as f32;
    let inside = (!(s1.to_bits() | s2.to_bits() | s0.to_bits()) >> 31) as i32;
    let e = inside & sbit(cmp) & sbit(tm);
    best.take(e, v71, v416, v394);

    // ---- 3b: lowest vertex vs the cap plane
    let dax = (dir.y * ay + dir.x * ax) + dir.z * az;
    let r8 = 2 * (sbit(dax) ^ 1) - 1;
    let v106 = r8 as f32;
    let proj = |q: Vec3| d(((ay * q.y + ax * q.x) + az * q.z) * v106);
    let v107 = proj(pt[0]);
    let v108 = proj(pt[1]);
    let v411 = (v108 - v107) as f32;
    let mn = ((v108 + v107) - (v107 - v108).abs()) * 0.5;
    let v97 = (proj(pt[2]) - mn) as f32;
    let k = sbit(v97);
    let v109 = (2 * k) | ((k ^ 1) & sbit(v411));
    let hs = hh * v106;
    let ccx = c.x + ax * hs;
    let ccy = c.y + ay * hs;
    let ccz = c.z + az * hs;
    let pv = pt[v109 as usize];
    let pre = (axd * (d(ccx) - d(pv.x)) + ayd * (d(ccy) - d(pv.y))) + azd * (d(ccz) - d(pv.z));
    let dax2 = (ax * dir.x + dir.y * ay) + dir.z * az;
    let s113 = f64::from(pm1(dax2));
    let v114 = d(dax2) * s113;
    let v115 = pre * s113;
    let v115f = v115 as f32;
    let v116 = v114 as f32;
    let x = (d(v116 * pv.x + dir.x * v115f) - d(ccx) * v114) as f32;
    let y = (d(v116 * pv.y + dir.y * v115f) - d(ccy) * v114) as f32;
    let z = (d(v116 * pv.z + dir.z * v115f) - d(ccz) * v114) as f32;
    let v441 = (d((x * x + y * y) + z * z) - (v114 * r2) * v114) as f32;
    let v453 = (best.num * v114 - best.den * v115) as f32;
    let tm = (v115 - tmax * v114) as f32;
    let e = sbit(v441) & sbit(tm) & sbit(v453);
    best.take(e, (2 * (v109 | 0x10)) | ((r8 + 1) >> 1), v114, v115);

    // ---- 3c: edges vs the two cap rims
    let hx = ax * hh;
    let hy = ay * hh;
    let hz = az * hh;
    for i in 0..3 {
        let a = pt[i];
        let b = pt[INC[i]];
        let exf = b.x - a.x;
        let eyf = b.y - a.y;
        let ezf = b.z - a.z;
        let (ex, ey, ez) = (d(exf), d(eyf), d(ezf));
        let kx = dir.y * ezf - dir.z * eyf;
        let ky = exf * dir.z - dir.x * ezf;
        let kz = dir.x * eyf - exf * dir.y;
        let gx = d(ay * kz - az * ky);
        let gy = d(kx * az - ax * kz);
        let gz = d(ax * ky - kx * ay);
        let gl = (gx * gx + gy * gy) + gz * gz;
        for j in [-1i32, 1] {
            let jf = j as f32;
            let rcx = c.x + hx * jf;
            let rcy = c.y + hy * jf;
            let rcz = c.z + hz * jf;
            let ux = d((d(a.x) - d(rcx)) as f32);
            let uy = d((d(a.y) - d(rcy)) as f32);
            let uz = d((d(a.z) - d(rcz)) as f32);
            let h148 = (uy * ez - uz * ey) as f32;
            let h149 = (ex * uz - ux * ez) as f32;
            let h150 = (ux * ey - ex * uy) as f32;
            let v151 = d(h148 * az - ax * h150);
            let v152 = d(ay * h150 - az * h149);
            let v153 = d(ax * h149 - h148 * ay);
            let bq = (gx * v152 + gy * v151) + gz * v153;
            let v155 = (axd * ex + ayd * ey) + azd * ez;
            let disc =
                bq * bq - (((v152 * v152 + v151 * v151) + v153 * v153) - (v155 * v155) * r2) * gl;
            if disc >= 0.0 {
                let num = disc.sqrt() - bq;
                let nf = num as f32;
                let v160 = (axd * ex + ayd * ey) + azd * ez;
                let sx = d(dir.x * nf) + ux * gl;
                let sy = d(dir.y * nf) + uy * gl;
                let sz = d(dir.z * nf) + uz * gl;
                let s = (axd * sx + ayd * sy) + azd * sz;
                let vl = v160 * gl;
                let h = ((-s * 2.0 - vl).abs() - vl.abs()) as f32;
                let cmp = (best.num * gl - best.den * num) as f32;
                let tm = (num - tmax * gl) as f32;
                let e = sbit(cmp) & sbit(tm) & sbit(h);
                best.take(e, ((j + 1) >> 1) | (2 * (i as i32 | 0x40)), gl, num);
            }
        }
    }

    let ndot = (nn.y * ay + ax * nn.x) + nn.z * az;
    if lt_or_nan((ndot.abs() - 1.0).abs(), f32::from_bits(0x38D1_B717)) {
        cap_parallel(p, tri, cyl, out, best, r2)
    } else {
        cap_edges(p, tri, cyl, out, best, r2)
    }
}

/// Phase 3, cap parallel to the triangle (`||axis·n| − 1| < 1e-4`).
#[allow(
    clippy::too_many_lines,
    clippy::similar_names,
    clippy::many_single_char_names
)]
fn cap_parallel(
    p: &UnprojParams,
    tri: &Triangle,
    cyl: &CylinderPrim,
    out: &mut UnprojResult,
    best: &Best,
    r2: f64,
) -> Option<i32> {
    let dir = p.dir;
    let c = cyl.center;
    let (ax, ay, az) = (cyl.axis.x, cyl.axis.y, cyl.axis.z);
    let pt = &tri.pt;
    let nn = tri.n;
    let p0 = pt[0];

    let dax = (dir.y * ay + dir.x * ax) + dir.z * az;
    let v171 = (pm1(dax) as f32) * cyl.hh;
    let capx = c.x + ax * v171;
    let capy = c.y + ay * v171;
    let capz = c.z + az * v171;
    let (v412, v421, v431) = (d(capx), d(capy), d(capz));
    let dn = (dir.y * nn.y + dir.x * nn.x) + dir.z * nn.z;
    let v174 = pm1(dn);
    let v175 = d(dn).abs();
    let v176 = ((d(nn.x) * (v412 - d(p0.x)) + d(nn.y) * (v421 - d(p0.y)))
        + d(nn.z) * (v431 - d(p0.z)))
        * f64::from(v174);
    let tst = (best.den * v176 - best.num * v175) + (v176 - best.num) * 1e-80;
    if le_or_nan_d(tst, 0.0) {
        return None;
    }
    if !lt_or_nan_d(v176 - d(p.tmax) * v175, 0.0) {
        return None;
    }
    out.n = nn;
    out.i_feature[1] = (((v174 + 1) >> 1) + 1) | 0x40;
    let v454 = v176 as f32;
    let v461 = v175 as f32;
    let v178 = (d(p0.x) - v412) as f32;
    let v180 = (d(p0.y) - v421) as f32;
    let v181 = (d(p0.z) - v431) as f32;
    let x = v461 * v178 + v454 * dir.x;
    let y = v461 * v180 + v454 * dir.y;
    let z = v461 * v181 + v454 * dir.z;
    if lt_or_nan_d(d((x * x + y * y) + z * z), (v175 * r2) * v175) {
        // vertex 0 inside the cap
        let t = div0(v176, v175);
        out.t = t;
        let tf = t as f32;
        out.pt = Vec3::new(dir.x * tf + p0.x, dir.y * tf + p0.y, dir.z * tf + p0.z);
        out.i_feature[0] = 0x80;
        return Some(1);
    }
    let v187 = v175 * v412 - d(v454 * dir.x);
    let v188 = v421 * v175 - d(v454 * dir.y);
    let v189 = v431 * v175 - d(v454 * dir.z);
    let q5 = |a: Vec3| {
        [
            (v187 - d(v461 * a.x)) as f32,
            (v188 - d(v461 * a.y)) as f32,
            (v189 - d(v461 * a.z)) as f32,
        ]
    };
    let s0 = edge_side(q5(pt[0]), pt[0], pt[1], nn);
    let s1 = edge_side(q5(pt[1]), pt[1], pt[2], nn);
    let s2 = edge_side(q5(pt[2]), pt[2], pt[0], nn);
    if (!(s2.to_bits() | s1.to_bits() | s0.to_bits()) as i32) < 0 {
        // the cap centre projects inside the triangle
        out.t = div0(v176, v175);
        out.pt = Vec3::new(capx, capy, capz);
        out.i_feature[0] = 0x40;
        return Some(1);
    }
    // first edge crossing the rim circle
    let v395 = v454 * dir.x;
    let v410 = v454 * dir.y;
    let v219 = v454 * dir.z;
    let v216 = (v175 * v175) * r2;
    for i in 0..3 {
        let a = pt[i];
        let b = pt[INC[i]];
        let ez = d(b.z - a.z);
        let ey = d(b.y - a.y);
        let ex = d(b.x - a.x);
        let v220 = (d(a.x) - v412) as f32;
        let v221 = (d(a.y) - v421) as f32;
        let v224 = (d(a.z) - v431) as f32;
        let v222 = d(v461 * v221 + v410);
        let v223 = d(v461 * v220 + v395);
        let v225 = d(v461 * v224 + v219);
        let e_x = ex * v175;
        let e_y = ey * v175;
        let e_z = ez * v175;
        let v226 = (e_y * v222 + e_x * v223) + e_z * v225;
        let v227 = (e_y * e_y + e_x * e_x) + e_z * e_z;
        let disc = v226 * v226 - (((v222 * v222 + v223 * v223) + v225 * v225) - v216) * v227;
        if disc >= 0.0 {
            let sq = disc.sqrt();
            let mut root = -v226 - sq;
            let mut found = false;
            for _ in 0..2 {
                let h = (((root * 2.0 - v227).abs()) - v227) as f32;
                if sbit(h) != 0 {
                    found = true;
                    break;
                }
                root += sq * 2.0;
            }
            if found {
                let t = div0(v176, v175);
                out.t = t;
                let u = div0(root, v227);
                let tf = t as f32;
                out.pt = Vec3::new(
                    (d(dir.x * tf + a.x) + u * ex) as f32,
                    (d(dir.y * tf + a.y) + u * ey) as f32,
                    (d(dir.z * tf + a.z) + u * ez) as f32,
                );
                out.i_feature[0] = i as i32 | 0xA0;
                return Some(1);
            }
        }
    }
    None
}

/// Phase 3, cap not parallel to the triangle: edges (nearly perpendicular to the axis) against
/// the cap facing the motion.
#[allow(
    clippy::too_many_lines,
    clippy::similar_names,
    clippy::many_single_char_names
)]
fn cap_edges(
    p: &UnprojParams,
    tri: &Triangle,
    cyl: &CylinderPrim,
    out: &mut UnprojResult,
    best: &Best,
    r2: f64,
) -> Option<i32> {
    let dir = p.dir;
    let c = cyl.center;
    let (ax, ay, az) = (cyl.axis.x, cyl.axis.y, cyl.axis.z);
    let (axd, ayd, azd) = (d(ax), d(ay), d(az));
    let hh = cyl.hh;
    let pt = &tri.pt;
    for i in 0..3 {
        let a = pt[i];
        let b = pt[INC[i]];
        let ex = d(b.x - a.x);
        let ey = d(b.y - a.y);
        let ez = d(b.z - a.z);
        let v246 = (axd * ex + ayd * ey) + azd * ez;
        let v256 = (ey * ey + ex * ex) + ez * ez;
        if !lt_or_nan_d(v246 * v246, v256 * 0.0001) {
            continue;
        }
        let dax = (dir.y * ay + dir.x * ax) + dir.z * az;
        let v250 = pm1(dax);
        let vf = v250 as f32;
        let v443 = d((ax * hh) * vf + c.x);
        let v450 = d((ay * hh) * vf + c.y);
        let v439 = d((az * hh) * vf + c.z);
        let v432 = v443 - d(a.x);
        let v429 = v450 - d(a.y);
        let v413 = v439 - d(a.z);
        let a1 = (axd * v432 + ayd * v429) + azd * v413;
        let a2 = (axd * ex + ayd * ey) + azd * ez;
        let c1 = (v432 * ex + v429 * ey) + v413 * ez;
        let b1 = (d(dir.x) * ex + d(dir.y) * ey) + d(dir.z) * ez;
        let v257 = d(dax) * v256 - b1 * a2;
        let s = f64::from(pm1(v257 as f32));
        let v260 = (a1 * v256 - a2 * c1) * s;
        let v261 = v257 * s;
        let tst = (best.den * v260 - best.num * v261) + (v260 - best.num) * 1e-80;
        if le_or_nan_d(tst, 0.0) {
            continue;
        }
        if !lt_or_nan_d(v260 - d(p.tmax) * v261, 0.0) {
            continue;
        }
        let t = div0(v260, v261);
        out.t = t;
        let tf = t as f32;
        let v266 = ((v432 - d(dir.x * tf)) * ex + (v429 - d(dir.y * tf)) * ey)
            + (v413 - d(dir.z * tf)) * ez;
        out.pt = Vec3::new(dir.x * tf + a.x, dir.y * tf + a.y, dir.z * tf + a.z);
        let mut hit = false;
        if lt_or_nan_d((v266 * 2.0 - v256).abs(), v256) {
            let q = out.pt;
            let v268 = (d(q.x) - v443) as f32;
            let v269 = (d(q.y) - v450) as f32;
            let v272 = (d(q.z) - v439) as f32;
            let v271 = v256 as f32;
            let x = d(v268 * v271) + ex * v266;
            let y = d(v271 * v269) + ey * v266;
            let z = d(v271 * v272) + ez * v266;
            if lt_or_nan_d((x * x + y * y) + z * z, (v256 * v256) * r2) {
                // the closest point of the edge line lies inside the cap disc
                let u = div0(v266, v256);
                out.pt = Vec3::new(
                    (d(q.x) + u * ex) as f32,
                    (d(q.y) + u * ey) as f32,
                    (d(q.z) + u * ez) as f32,
                );
                hit = true;
            }
        }
        if !hit {
            let q = out.pt;
            let v274 = (d(q.x) + ex) as f32;
            let v275 = (d(q.y) + ey) as f32;
            let v276 = (d(q.z) + ez) as f32;
            let v277 = (d(v274) - v443) as f32;
            let v278 = (d(v275) - v450) as f32;
            let v279 = (d(v276) - v439) as f32;
            if lt_or_nan_d(d((v278 * v278 + v277 * v277) + v279 * v279), r2) {
                // the end vertex is inside the disc
                out.pt = Vec3::new(
                    (d(q.x) + ex) as f32,
                    (d(q.y) + ey) as f32,
                    (d(q.z) + ez) as f32,
                );
                hit = true;
            } else {
                let v281 = (d(q.y) - v450) as f32;
                let v282 = (d(q.x) - v443) as f32;
                let v283 = (d(q.z) - v439) as f32;
                // the start vertex is inside the disc
                hit = le_or_nan_d(d((v281 * v281 + v282 * v282) + v283 * v283), r2);
            }
        }
        if hit {
            let s = (-v250) as f32;
            out.n = Vec3::new(ax * s, ay * s, az * s);
            out.i_feature[1] = (((v250 + 1) >> 1) + 1) | 0x40;
            out.i_feature[0] = i as i32 | 0xA0;
            return Some(1);
        }
        // Bug kept: `t` and `pt` of a rejected edge stay in `out`.
    }
    None
}

/// Phase 4 of sub_351716D0: the result for the winning feature pair.
#[allow(
    clippy::too_many_lines,
    clippy::similar_names,
    clippy::many_single_char_names
)]
fn finish(
    p: &UnprojParams,
    tri: &Triangle,
    cyl: &CylinderPrim,
    out: &mut UnprojResult,
    best: &Best,
    nlen: f64,
) -> i32 {
    if best.mask == -1 || lt_or_nan_d(best.den.abs(), 1e-20) {
        return 0;
    }
    let dir = p.dir;
    let c = cyl.center;
    let (ax, ay, az) = (cyl.axis.x, cyl.axis.y, cyl.axis.z);
    let (axd, ayd, azd) = (d(ax), d(ay), d(az));
    let hh = cyl.hh;
    let pt = &tri.pt;
    let nn = tri.n;
    let mask = best.mask;
    // radial part of `v` (the axis component removed), float
    let radial = |v: Vec3| {
        let dot = (v.y * ay + v.x * ax) + v.z * az;
        Vec3::new(v.x - ax * dot, v.y - ay * dot, v.z - az * dot)
    };
    match mask & 0xE0 {
        0 => {
            // cap face vs triangle face (mask = sign bit of axis·n)
            let num = best.num * nlen;
            let s = ((2 * mask - 1) as f32) * hh;
            let ccy = d(ay * s + c.y);
            let ccz = d(az * s + c.z);
            let ccx = d(ax * s + c.x);
            let v375 = (nn.y * ay + nn.x * ax) + nn.z * az;
            let v376 = d(ay * v375 - nn.y);
            let v377 = d(ax * v375 - nn.x);
            let v378 = d(az * v375 - nn.z);
            let inv = 1.0 / (best.den * nlen);
            out.t = num * inv;
            let v380 = (d(cyl.r) * inv) * best.den;
            out.pt = Vec3::new(
                (v377 * v380 + ccx) as f32,
                (v380 * v376 + ccy) as f32,
                (v380 * v378 + ccz) as f32,
            );
            out.n = nn;
            out.i_feature[0] = 0x40;
            out.i_feature[1] = mask | 0x20;
        }
        0x20 => {
            // vertex vs cap plane
            let j = mask & 1;
            let i = ((mask >> 1) & 3) as usize;
            let t = div0(best.num, best.den);
            out.t = t;
            let tf = t as f32;
            let q = pt[i];
            out.pt = Vec3::new(dir.x * tf + q.x, dir.y * tf + q.y, dir.z * tf + q.z);
            let s = (1 - 2 * j) as f32;
            out.n = Vec3::new(ax * s, ay * s, az * s);
            out.i_feature[1] = (j + 1) | 0x40;
            out.i_feature[0] = i as i32 | 0x80;
        }
        0x40 => {
            // vertex vs side
            let i = (mask & 3) as usize;
            let t = div0(best.num, best.den);
            out.t = t;
            let tf = t as f32;
            let q = pt[i];
            out.pt = Vec3::new(dir.x * tf + q.x, dir.y * tf + q.y, dir.z * tf + q.z);
            let n = Vec3::new(c.x - out.pt.x, c.y - out.pt.y, c.z - out.pt.z);
            out.n = radial(n);
            out.i_feature[0] = i as i32 | 0x80;
            out.i_feature[1] = 0x40;
        }
        0x60 => {
            // edge vs side
            let i = (mask & 3) as usize;
            let a = pt[i];
            let b = pt[INC[i]];
            let ex = d(b.x - a.x);
            let ey = d(b.y - a.y);
            let ez = d(b.z - a.z);
            let v332 = azd * ey - ayd * ez;
            let v333 = ex * ayd - axd * ey;
            let v334 = axd * ez - ex * azd;
            let l = (v332 * v332 + v334 * v334) + v333 * v333;
            let nf = best.num as f32;
            let v336 = d(c.x - a.x) * best.den - d(dir.x * nf);
            let v337 = d(c.y - a.y) * best.den - d(dir.y * nf);
            let v338 = d(c.z - a.z) * best.den - d(dir.z * nf);
            let v339 = l * best.den;
            let v340 = ((azd * v337 - ayd * v338) * v332 + (axd * v338 - v336 * azd) * v334)
                + (v336 * ayd - axd * v337) * v333;
            let inv = 1.0 / v339;
            let v341 = (ey * v340) * inv;
            let v342 = (ex * v340) * inv;
            let v344 = (ez * v340) * inv;
            let t = (inv * best.num) * l;
            out.t = t;
            let tf = t as f32;
            out.pt = Vec3::new(
                ((d(a.x) + v342) as f32) + dir.x * tf,
                ((d(a.y) + v341) as f32) + dir.y * tf,
                ((d(a.z) + v344) as f32) + dir.z * tf,
            );
            let n = Vec3::new(c.x - out.pt.x, c.y - out.pt.y, c.z - out.pt.z);
            out.n = radial(n);
            out.i_feature[0] = i as i32 | 0xA0;
            out.i_feature[1] = 0x40;
        }
        _ => {
            // edge vs cap rim (0x80)
            let i = ((mask >> 1) & 3) as usize;
            let j = mask & 1;
            let a = pt[i];
            let b = pt[INC[i]];
            let s = (2 * j - 1) as f32;
            let ccx = (ax * hh) * s + c.x;
            let ccy = (ay * hh) * s + c.y;
            let ccz = (az * hh) * s + c.z;
            let ez = d(b.z - a.z);
            let ey = d(b.y - a.y);
            let ex = d(b.x - a.x);
            let v303 = d((d(a.x) - d(ccx)) as f32);
            let v304 = d((d(a.y) - d(ccy)) as f32);
            let v305 = d((d(a.z) - d(ccz)) as f32);
            let nf = best.num as f32;
            let den = best.den;
            let ae = (ayd * ey + axd * ex) + azd * ez;
            let v307 = ae * den;
            let x = d(nf * dir.x) + v303 * den;
            let y = d(nf * dir.y) + v304 * den;
            let z = d(nf * dir.z) + v305 * den;
            let v308 = -((ayd * y + axd * x) + azd * z);
            let inv = 1.0 / v307;
            let t = ((ayd * ey + axd * ex) + azd * ez) * (inv * best.num);
            out.t = t;
            let tf = t as f32;
            let q = Vec3::new(
                ((d(a.x) + (v308 * ex) * inv) as f32) + dir.x * tf,
                ((d(a.y) + (ey * v308) * inv) as f32) + dir.y * tf,
                ((d(a.z) + (ez * v308) * inv) as f32) + dir.z * tf,
            );
            out.pt = q;
            let r = radial(Vec3::new(q.x - c.x, q.y - c.y, q.z - c.z));
            let q322 = d(r.z * ay - r.y * az);
            let q309 = d(r.x * az - r.z * ax);
            let q323 = d(r.y * ax - r.x * ay);
            let nx = (q323 * ey - q309 * ez) as f32;
            let ny = (ez * q322 - q323 * ex) as f32;
            let nz = (q309 * ex - ey * q322) as f32;
            let sg = pm1((nx * (c.x - q.x) + (c.y - q.y) * ny) + (c.z - q.z) * nz) as f32;
            out.n = Vec3::new(sg * nx, sg * ny, sg * nz);
            out.i_feature[0] = i as i32 | 0xA0;
            out.i_feature[1] = j | 0x20;
        }
    }
    1
}

fn cell_tri_cyl(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_tri_cyl(p, as_tri(a), fa, as_cyl(b), fb, out)
}

/// sub_35175330: cyl–tri, the 335-byte swap wrapper around [`unproject_tri_cyl`] (the area
/// pointer is passed through to the callee, which ignores it).
fn cell_cyl_tri(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    let (cyl, tri) = (as_cyl(a), as_tri(b));
    swap_linear(p, out, |p, out| unproject_tri_cyl(p, tri, fb, cyl, fa, out))
}

/// Fill the cells this group owns: `[0][tri][cyl]` and `[0][cyl][tri]`.
pub fn fill(t: &mut UnprojTables) {
    t.set(
        MODE_LINEAR,
        GeomType::TriMesh,
        GeomType::Cylinder,
        cell_tri_cyl,
    );
    t.set(
        MODE_LINEAR,
        GeomType::Cylinder,
        GeomType::TriMesh,
        cell_cyl_tri,
    );
}
