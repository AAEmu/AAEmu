//! P2b work group `ray_cyl`: linear unprojection of a ray against a cylinder and of a cylinder
//! against a sphere (`geometry.md` §4.6, §5).
//!
//! | cell | x64 | x86 | port |
//! |---|---|---|---|
//! | `[0][ray][cyl]` | 0x35179650 (9706 B) | 0x3518DC20 | [`unproject_ray_cyl`] |
//! | `[0][cyl][ray]` | 0x3517BC40 (335 B swap wrapper) | 0x351908C0 | [`unproject_cyl_ray`] |
//! | `[0][cyl][sph]` | 0x35187780 (6862 B) | 0x351918E0 | [`unproject_cyl_sph`] |
//! | `[0][sph][cyl]` | 0x35189260 (335 B swap wrapper) | 0x35193720 | [`unproject_sph_cyl`] |
//!
//! **ray–cyl** (sub_35179650). The ray segment `o + rd·u`, `u ∈ [0, 1]`, is moved by `dir·t`.
//! The function evaluates a fixed list of feature pairs, each giving a candidate `t` as a
//! fraction `num/den` (doubles), and keeps the **largest** candidate below `tmax` with a
//! branch-free selection: the candidate is accepted when the sign bits of three or four
//! float-rounded test values are all set (`num_best·den − den_best·num < 0`,
//! `num − tmax·den < 0`, and the containment tests below), and then `best = cand`,
//! `feature = id`. The pairs, in order:
//! 1. ray endpoint `i` (0, 1) against the infinite side (`|(p + dir·t) × axis|² = r²`, larger
//!    root), height check `|axis·(p + dir·t)| < hh` — id `0x40 | i`;
//! 2. the ray line against the side (line–line distance `= r`, larger root), checks that the
//!    closest point lies inside the segment and the height range — id `0x60`;
//! 3. unless `iFeatB` is 0x43 (or 0x80000043): the ray endpoint nearer the cap facing `dir`
//!    against that cap plane, inside-disk check — id `0x20 | end << 1 | cap`;
//!    then the ray line against both cap rims (circle–line distance; quadratic in `t`) — id
//!    `0x80 | cap`; then, if the ray is nearly perpendicular to the axis (`(rd·axis)² <
//!    1e-4·|rd|²`), the ray line sliding along the cap plane facing `dir`: here the function
//!    writes `t` and `pt` immediately and, if the contact lies on the cap disk (or the far
//!    endpoint is inside the cap circle while the near one is not), returns with the cap
//!    normal (`pt` projected or moved to the ray end, features 0xA0 / `0x41 + cap`).
//!
//! Afterwards (unless that early return happened) nothing found or `|den| < 1e-20` returns 0;
//! otherwise `t = num/den` (0 if `den == 0`) and per feature class:
//! * `0x20`: `pt` = ray endpoint moved by `dir·t`, `n = ±axis` (cap normal), features
//!   `0x80 | end` / `0x41 + cap`;
//! * `0x40`: `pt` = ray endpoint moved, `n` = radial part of `center − pt` (not normalised),
//!   features `0x80 | end` / 0x40;
//! * `0x60`: `pt` = closest point of the ray line to the cylinder axis line moved by `dir·t`,
//!   `n` = radial part of `center − pt` (not normalised), features 0xA0 / 0x40;
//! * `0x80`: `pt` = the point of the ray line on the rim circle moved by `dir·t`,
//!   `n = ±(radial × axis) × rd` oriented towards the centre, features 0xA0 / `0x20 | cap`.
//!
//! `dir = 0` is not special-cased (the engine never asks for least-penetration unprojection of
//! a ray), the params are never written and `tmin` is not read.

use super::common::{as_cyl, as_ray, le_or_nan_d, lt_or_nan_d, swap_linear, ucomisd_eq};
use super::{UnprojTables, MODE_LINEAR};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{CylinderPrim, Primitive, RayPrim};
use crate::ids::GeomType;
use crate::math::Vec3;

mod cyl_sph;
pub use cyl_sph::{unproject_cyl_sph, unproject_sph_cyl};

/// Sign bit of a float as 0/1 (`shr 31` on the stored bits).
#[inline]
fn sb(x: f32) -> i32 {
    (x.to_bits() >> 31) as i32
}

/// `((bits(x) sar 30) & ~1) + 1`: +1 for a clear sign bit, −1 for a set one (−0 → −1).
#[inline]
fn sgn_pm1(x: f32) -> i32 {
    ((x.to_bits() as i32 >> 30) & !1) + 1
}

/// The branch-free candidate update shared by every feature test:
/// `feat = sel ? id : feat`, `den = !sel·den + sel·d`, `num = !sel·num + sel·n` (all doubles,
/// the 0/1 factors converted with `cvtdq2pd`).
#[inline]
fn select(e: i32, id: i32, d: f64, n: f64, feat: &mut i32, den: &mut f64, num: &mut f64) {
    *feat = (id & e.wrapping_neg()) | ((e - 1) & *feat);
    let ef = f64::from(e);
    let nf = f64::from(e ^ 1);
    *den = nf * *den + ef * d;
    *num = nf * *num + ef * n;
}

/// 1e-20 (double at 0x351EF338).
const DEN_EPS: f64 = 1e-20;

/// sub_35179650: linear unprojection of a ray (A) out of a cylinder (B). See the module docs.
#[allow(
    clippy::too_many_lines,
    clippy::similar_names,
    clippy::many_single_char_names
)]
pub fn unproject_ray_cyl(
    p: &mut UnprojParams,
    ray: &RayPrim,
    _fa: i32,
    cyl: &CylinderPrim,
    fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let (dx, dy, dz) = (p.dir.x, p.dir.y, p.dir.z);
    let tmax = f64::from(p.tmax);
    let o = ray.origin;
    let rd = ray.dir;
    let c = cyl.center;
    let (ax, ay, az) = (cyl.axis.x, cyl.axis.y, cyl.axis.z);
    let (axd, ayd, azd) = (f64::from(ax), f64::from(ay), f64::from(az));
    let hh = cyl.hh;
    let r2 = f64::from(cyl.r * cyl.r);

    let mut den = 1.0f64;
    let mut num = 0.0f64;
    let mut feat: i32 = -1;

    // r12d: caps are skipped only for iFeatB == 0x43 (or 0x80000043: the sign test wraps)
    let caps = {
        let e = fb.wrapping_sub(0x43);
        (e.wrapping_sub(1) ^ e) >= 0
    };

    // ---- 1. ray endpoints against the side
    let cx = f64::from(dy * az - dz * ay);
    let cy = f64::from(dz * ax - dx * az);
    let cz = f64::from(dx * ay - dy * ax);
    let a = (cy * cy + cx * cx) + cz * cz;
    for i in 0..2i32 {
        let fi = i as f32;
        let px = f64::from((o.x + fi * rd.x) - c.x);
        let py = f64::from((o.y + fi * rd.y) - c.y);
        let pz = f64::from((o.z + fi * rd.z) - c.z);
        let qx = azd * py - ayd * pz;
        let qy = axd * pz - px * azd;
        let qz = px * ayd - axd * py;
        let b = (cy * qy + cx * qx) + cz * qz;
        let disc = b * b - (((qy * qy + qx * qx) + qz * qz) - r2) * a;
        if lt_or_nan_d(disc, 0.0) {
            continue;
        }
        let tn = disc.sqrt() - b;
        let tf = tn as f32;
        let sx = f64::from(tf * dx) + px * a;
        let sy = f64::from(tf * dy) + py * a;
        let sz = f64::from(tf * dz) + pz * a;
        let h = (((axd * sx + ayd * sy) + azd * sz).abs() - f64::from(hh) * a) as f32;
        let cmp = (num * a - den * tn) as f32;
        let tm = (tn - tmax * a) as f32;
        let e = sb(cmp) & sb(tm) & sb(h);
        select(e, i | 0x40, a, tn, &mut feat, &mut den, &mut num);
    }

    // ---- 2. ray line against the side
    let wx = f64::from(o.x - c.x);
    let wy = f64::from(o.y - c.y);
    let wz = f64::from(o.z - c.z);
    let (rdx, rdy, rdz) = (f64::from(rd.x), f64::from(rd.y), f64::from(rd.z));
    let ex = azd * rdy - ayd * rdz;
    let ey = axd * rdz - rdx * azd;
    let ez = rdx * ayd - axd * rdy;
    let ee = (ex * ex + ey * ey) + ez * ez;
    let ew = (ex * wx + ey * wy) + ez * wz;
    let ed = f64::from((dy * (ey as f32) + (ex as f32) * dx) + dz * (ez as f32));
    let ewd = ew * ed;
    let ed2 = ed * ed;
    let disc = ewd * ewd - (ew * ew - ee * r2) * ed2;
    if !lt_or_nan_d(disc, 0.0) {
        let s = disc.sqrt() - ewd;
        let ed2f = ed2 as f32;
        let sf = s as f32;
        let v52 = f64::from((c.x - o.x) * ed2f - sf * dx);
        let v51 = f64::from(ed2f * (c.y - o.y) - sf * dy);
        let v53 = f64::from(ed2f * (c.z - o.z) - sf * dz);
        let t1 = ((azd * v51 - ayd * v53) * ex + (axd * v53 - v52 * azd) * ey)
            + (v52 * ayd - axd * v51) * ez;
        let eeed = ee * ed2;
        let t2 = ((v51 * rdz - v53 * rdy) * ex + (rdx * v53 - v52 * rdz) * ey)
            + (v52 * rdy - rdx * v51) * ez;
        let c1 = ((t1 * 2.0 - eeed).abs() - eeed) as f32;
        let c2 = (t2.abs() - f64::from(hh) * eeed) as f32;
        let c3 = (num * ed2 - den * s) as f32;
        let c4 = (s - tmax * ed2) as f32;
        let e = sb(c3) & sb(c4) & sb(c2) & sb(c1);
        select(e, 0x60, ed2, s, &mut feat, &mut den, &mut num);
    }

    if caps {
        // ---- 3. the nearer ray endpoint against the cap facing dir
        let da = (dy * ay + dx * ax) + dz * az;
        let sgn = 2 * (1 - sb(da)) - 1;
        let sgnf = sgn as f32;
        let e1 = (ax * (o.x + rd.x) + ay * (o.y + rd.y)) + az * (o.z + rd.z);
        let e0 = (ax * o.x + ay * o.y) + az * o.z;
        let hs = hh * sgnf;
        let capz = az * hs + c.z;
        let capy = ay * hs + c.y;
        let capx = ax * hs + c.x;
        let end = sb(e1 * sgnf - e0 * sgnf);
        let endf = end as f32;
        let (capxd, capyd, capzd) = (f64::from(capx), f64::from(capy), f64::from(capz));
        let xx = (capxd - f64::from(o.x)) - f64::from(endf * rd.x);
        let yy = (capyd - f64::from(o.y)) - f64::from(endf * rd.y);
        let zz = (capzd - f64::from(o.z)) - f64::from(endf * rd.z);
        let v69 = (ayd * yy + axd * xx) + azd * zz;
        let dd = f64::from((ay * dy + ax * dx) + dz * az);
        let sg2 = f64::from(sgn_pm1(dd as f32));
        let v71 = dd * sg2;
        let v72 = v69 * sg2;
        let v73 = v72 as f32;
        let v71f = v71 as f32;
        let vx = (f64::from((o.x + endf * rd.x) * v71f + v73 * dx) - capxd * v71) as f32;
        let vy = (f64::from((o.y + endf * rd.y) * v71f + v73 * dy) - capyd * v71) as f32;
        let vz = (f64::from((o.z + endf * rd.z) * v71f + v73 * dz) - capzd * v71) as f32;
        let sum = (vy * vy + vx * vx) + vz * vz;
        let h = (f64::from(sum) - (v71 * r2) * v71) as f32;
        let cmp = (num * v71 - den * v72) as f32;
        let tm = (v72 - tmax * v71) as f32;
        let e = sb(cmp) & sb(tm) & sb(h);
        let id = ((end | 0x10) << 1) | ((sgn + 1) >> 1);
        select(e, id, v71, v72, &mut feat, &mut den, &mut num);

        // ---- 4. the ray line against the two cap rims
        let axhh = ax * hh;
        let ayhh = ay * hh;
        let azhh = az * hh;
        let (rxf, ryf, rzf) = (rd.x, rd.y, rd.z);
        let w_x = dy * rzf - dz * ryf;
        let w_y = dz * rxf - dx * rzf;
        let w_z = dx * ryf - dy * rxf;
        let ux = f64::from(ay * w_z - az * w_y);
        let uy = f64::from(az * w_x - ax * w_z);
        let uz = f64::from(ax * w_y - ay * w_x);
        let uu = (ux * ux + uy * uy) + uz * uz;
        let mut s = -1i32;
        while s <= 1 {
            let sf = s as f32;
            let kcz = sf * azhh + c.z;
            let kcy = sf * ayhh + c.y;
            let kcx = sf * axhh + c.x;
            let gx = f64::from((f64::from(o.x) - f64::from(kcx)) as f32);
            let gy = f64::from((f64::from(o.y) - f64::from(kcy)) as f32);
            let gz = f64::from((f64::from(o.z) - f64::from(kcz)) as f32);
            let hx = (gy * rdz - gz * rdy) as f32;
            let hy = (rdx * gz - gx * rdz) as f32;
            let hz = (gx * rdy - rdx * gy) as f32;
            let kx = f64::from(ay * hz - az * hy);
            let ky = f64::from(az * hx - ax * hz);
            let kz = f64::from(ax * hy - ay * hx);
            let v104 = (ux * kx + uy * ky) + uz * kz;
            let ard = (ayd * rdy + rdx * axd) + azd * rdz;
            let disc = v104 * v104 - (((kx * kx + ky * ky) + kz * kz) - (ard * ard) * r2) * uu;
            if !lt_or_nan_d(disc, 0.0) {
                let q = disc.sqrt() - v104;
                let qf = q as f32;
                let ssx = f64::from(qf * dx) + gx * uu;
                let ssy = f64::from(qf * dy) + gy * uu;
                let ssz = f64::from(qf * dz) + gz * uu;
                let ss = (axd * ssx + ayd * ssy) + azd * ssz;
                let ard2 = (rdx * axd + ayd * rdy) + azd * rdz;
                let v110 = ard2 * uu;
                let c1 = ((-ss * 2.0 - v110).abs() - v110.abs()) as f32;
                let c2 = (num * uu - den * q) as f32;
                let c3 = (q - tmax * uu) as f32;
                let e = sb(c2) & sb(c3) & sb(c1);
                let id = ((s + 1) | 0x100) >> 1;
                select(e, id, uu, q, &mut feat, &mut den, &mut num);
            }
            s += 2;
        }

        // ---- 5. ray (nearly) perpendicular to the axis: slide along the cap plane
        let ard = (rdx * axd + ayd * rdy) + azd * rdz;
        let rr = (rdy * rdy + rdx * rdx) + rdz * rdz;
        if lt_or_nan_d(ard * ard, rr * 1e-4) {
            if let Some(r) = ray_cyl_cap_slide(p, ray, cyl, r2, den, num, out) {
                return r;
            }
        }
    }

    // ---- final: build the contact for the selected feature
    if feat == -1 || lt_or_nan_d(den.abs(), DEN_EPS) {
        return 0;
    }
    match feat & 0xE0 {
        0x20 => {
            let end = (feat >> 1) & 3;
            let cap = feat & 1;
            let t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
            out.t = t;
            let tf = t as f32;
            let ef = end as f32;
            out.pt = Vec3::new(
                (ef * rd.x + o.x) + dx * tf,
                (ef * rd.y + o.y) + dy * tf,
                (ef * rd.z + o.z) + dz * tf,
            );
            let s = (1 - 2 * cap) as f32;
            out.n = Vec3::new(ax * s, ay * s, az * s);
            out.i_feature = [end | 0x80, (cap + 1) | 0x40];
        }
        0x40 => {
            let end = feat & 3;
            let t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
            out.t = t;
            let tf = t as f32;
            let ef = end as f32;
            out.pt = Vec3::new(
                (ef * rd.x + o.x) + dx * tf,
                (ef * rd.y + o.y) + dy * tf,
                (ef * rd.z + o.z) + dz * tf,
            );
            out.n = radial(c, out.pt, cyl.axis);
            out.i_feature = [end | 0x80, 0x40];
        }
        0x60 => {
            let wx = f64::from(c.x - o.x) * den;
            let wy = f64::from(c.y - o.y) * den;
            let wz = f64::from(c.z - o.z) * den;
            let v198 = azd * rdy - ayd * rdz;
            let v200 = rdx * ayd - axd * rdy;
            let v201 = axd * rdz - rdx * azd;
            let l = (v201 * v201 + v198 * v198) + v200 * v200;
            let nf = num as f32;
            let px = wx - f64::from(nf * dx);
            let py = wy - f64::from(nf * dy);
            let pz = wz - f64::from(nf * dz);
            let v202 = ((axd * pz - px * azd) * v201 + (azd * py - ayd * pz) * v198)
                + (px * ayd - axd * py) * v200;
            let v203 = 1.0 / (l * den);
            let t = (v203 * num) * l;
            out.t = t;
            let tf = t as f32;
            out.pt = Vec3::new(
                (f64::from(o.x) + (rdx * v202) * v203) as f32 + dx * tf,
                (f64::from(o.y) + (v202 * rdy) * v203) as f32 + dy * tf,
                (f64::from(o.z) + (v202 * rdz) * v203) as f32 + dz * tf,
            );
            out.n = radial(c, out.pt, cyl.axis);
            out.i_feature = [0xA0, 0x40];
        }
        _ => {
            let cap = feat & 1;
            let s = (2 * cap - 1) as f32;
            let kcx = (ax * hh) * s + c.x;
            let kcz = s * (az * hh) + c.z;
            let kcy = s * (ay * hh) + c.y;
            let gx = f64::from((f64::from(o.x) - f64::from(kcx)) as f32) * den;
            let gy = f64::from((f64::from(o.y) - f64::from(kcy)) as f32) * den;
            let gz = f64::from((f64::from(o.z) - f64::from(kcz)) as f32) * den;
            let nf = num as f32;
            let sy = f64::from(dy * nf) + gy;
            let sx = f64::from(dx * nf) + gx;
            let sz = f64::from(dz * nf) + gz;
            let ard = (axd * rdx + ayd * rdy) + azd * rdz;
            let v178 = -((axd * sx + ayd * sy) + azd * sz);
            let v179 = 1.0 / (ard * den);
            let ard2 = (axd * rdx + ayd * rdy) + azd * rdz;
            let t = ard2 * (v179 * num);
            out.t = t;
            let tf = t as f32;
            let pt = Vec3::new(
                (f64::from(o.x) + (rdx * v178) * v179) as f32 + dx * tf,
                (f64::from(o.y) + (v178 * rdy) * v179) as f32 + dy * tf,
                (f64::from(o.z) + (v178 * rdz) * v179) as f32 + dz * tf,
            );
            out.pt = pt;
            // radial part of pt − c
            let n0 = Vec3::new(pt.x - c.x, pt.y - c.y, pt.z - c.z);
            let d = (n0.y * ay + n0.x * ax) + n0.z * az;
            let nx = n0.x - ax * d;
            let ny = n0.y - ay * d;
            let nz = n0.z - az * d;
            // m = radial × axis, n = m × rd
            let mx = f64::from(nz * ay - ny * az);
            let my = f64::from(nx * az - nz * ax);
            let mz = f64::from(ny * ax - nx * ay);
            let n1x = (mz * rdy - my * rdz) as f32;
            let n1y = (rdz * mx - rdx * mz) as f32;
            let n1z = (rdx * my - rdy * mx) as f32;
            let side = ((c.y - pt.y) * n1y + n1x * (c.x - pt.x)) + (c.z - pt.z) * n1z;
            let sg = sgn_pm1(side) as f32;
            out.n = Vec3::new(sg * n1x, sg * n1y, sg * n1z);
            out.i_feature = [0xA0, cap | 0x20];
        }
    }
    1
}

/// `n = c − pt`, minus its component along `axis` (`d = (n.y·a.y + n.x·a.x) + n.z·a.z`),
/// all float — the side normal of the 0x40 / 0x60 results (not normalised).
#[inline]
fn radial(c: Vec3, pt: Vec3, axis: Vec3) -> Vec3 {
    let n = Vec3::new(c.x - pt.x, c.y - pt.y, c.z - pt.z);
    let d = (n.y * axis.y + n.x * axis.x) + n.z * axis.z;
    Vec3::new(n.x - axis.x * d, n.y - axis.y * d, n.z - axis.z * d)
}

/// Part 5 of sub_35179650 (inline in the binary): the ray, nearly perpendicular to the axis,
/// slides along the cap plane facing `dir`. Writes `t` and `pt` once the candidate passes the
/// two bound tests. Returns `Some(1)` for the early cap-normal return, `None` to continue with
/// the final feature dispatch (with `out.t`/`out.pt` possibly already written).
#[allow(clippy::similar_names, clippy::too_many_arguments)]
fn ray_cyl_cap_slide(
    p: &UnprojParams,
    ray: &RayPrim,
    cyl: &CylinderPrim,
    r2: f64,
    den: f64,
    num: f64,
    out: &mut UnprojResult,
) -> Option<i32> {
    let (dx, dy, dz) = (p.dir.x, p.dir.y, p.dir.z);
    let o = ray.origin;
    let (rdx, rdy, rdz) = (
        f64::from(ray.dir.x),
        f64::from(ray.dir.y),
        f64::from(ray.dir.z),
    );
    let c = cyl.center;
    let (ax, ay, az) = (cyl.axis.x, cyl.axis.y, cyl.axis.z);
    let (axd, ayd, azd) = (f64::from(ax), f64::from(ay), f64::from(az));
    let hh = cyl.hh;
    let da = (dy * ay + dx * ax) + dz * az;
    let sg3 = sgn_pm1(da);
    let sg3f = sg3 as f32;
    let capx = f64::from((ax * hh) * sg3f + c.x);
    let capz = f64::from(sg3f * (az * hh) + c.z);
    let capy = f64::from(sg3f * (ay * hh) + c.y);
    let v257 = capx - f64::from(o.x);
    let v261 = capy - f64::from(o.y);
    let v264 = capz - f64::from(o.z);
    let v286 = (rdy * rdy + rdx * rdx) + rdz * rdz;
    let aa = ((ayd * v261 + axd * v257) + azd * v264) * v286;
    let ard3 = (rdx * axd + ayd * rdy) + azd * rdz;
    let rw = (rdx * v257 + v261 * rdy) + v264 * rdz;
    let v121 = aa - ard3 * rw;
    let da2 = (ay * dy + ax * dx) + az * dz;
    let ard4 = (rdx * axd + ayd * rdy) + azd * rdz;
    let rdd = (rdx * f64::from(dx) + f64::from(dy) * rdy) + f64::from(dz) * rdz;
    let v122 = f64::from(da2) * v286 - rdd * ard4;
    let sg4 = f64::from(sgn_pm1(v122 as f32));
    let v125 = v121 * sg4;
    let v126 = v122 * sg4;
    if le_or_nan_d((den * v125 - num * v126) + (v125 - num) * 1e-80, 0.0) {
        return None;
    }
    if v125 - f64::from(p.tmax) * v126 >= 0.0 {
        return None;
    }
    let t = if ucomisd_eq(v126, 0.0) {
        0.0
    } else {
        v125 / v126
    };
    out.t = t;
    let tf = t as f32;
    let v131 = (rdx * (v257 - f64::from(dx * tf)) + (v261 - f64::from(dy * tf)) * rdy)
        + (v264 - f64::from(dz * tf)) * rdz;
    out.pt = Vec3::new(dx * tf + o.x, dy * tf + o.y, dz * tf + o.z);
    let mut far = (v131 * 2.0 - v286).abs() >= v286;
    if !far {
        let pt = out.pt;
        let qx = (f64::from(pt.x) - capx) as f32;
        let qy = (f64::from(pt.y) - capy) as f32;
        let qz = (f64::from(pt.z) - capz) as f32;
        let lf = v286 as f32;
        let xx = f64::from(qx * lf) + rdx * v131;
        let yy = f64::from(lf * qy) + v131 * rdy;
        let zz = f64::from(lf * qz) + v131 * rdz;
        far = (xx * xx + yy * yy) + zz * zz >= (v286 * v286) * r2;
        if !far {
            let v138 = if ucomisd_eq(v286, 0.0) {
                0.0
            } else {
                v131 / v286
            };
            out.pt = Vec3::new(
                (f64::from(pt.x) + rdx * v138) as f32,
                (f64::from(pt.y) + v138 * rdy) as f32,
                (f64::from(pt.z) + v138 * rdz) as f32,
            );
            return None;
        }
    }
    // the far end: inside the cap circle → move pt there and continue
    let pt = out.pt;
    let e1x = (f64::from(pt.x) + rdx) as f32;
    let e1y = (f64::from(pt.y) + rdy) as f32;
    let e1z = (f64::from(pt.z) + rdz) as f32;
    let qx = (f64::from(e1x) - capx) as f32;
    let qy = (f64::from(e1y) - capy) as f32;
    let qz = (f64::from(e1z) - capz) as f32;
    let v150 = (qx * qx + qy * qy) + qz * qz;
    if lt_or_nan_d(f64::from(v150), r2) {
        out.pt = Vec3::new(
            (f64::from(pt.x) + rdx) as f32,
            (f64::from(pt.y) + rdy) as f32,
            (f64::from(pt.z) + rdz) as f32,
        );
        return None;
    }
    let qx = (f64::from(pt.x) - capx) as f32;
    let qy = (f64::from(pt.y) - capy) as f32;
    let qz = (f64::from(pt.z) - capz) as f32;
    let v = (qx * qx + qy * qy) + qz * qz;
    if f64::from(v) > r2 {
        return None;
    }
    let s = (-sg3) as f32;
    out.n = Vec3::new(ax * s, ay * s, az * s);
    out.i_feature = [0xA0, (((sg3 + 1) >> 1) + 1) | 0x40];
    Some(1)
}

/// 0x3517BC40: cyl–ray, the 335-byte swap wrapper around [`unproject_ray_cyl`].
pub fn unproject_cyl_ray(
    p: &mut UnprojParams,
    cyl: &CylinderPrim,
    fa: i32,
    ray: &RayPrim,
    fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    swap_linear(p, out, |p, out| unproject_ray_cyl(p, ray, fb, cyl, fa, out))
}

fn cell_ray_cyl(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_ray_cyl(p, as_ray(a), fa, as_cyl(b), fb, out)
}

fn cell_cyl_ray(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_cyl_ray(p, as_cyl(a), fa, as_ray(b), fb, out)
}

/// Fill the cells this group owns: `[0][ray][cyl]`, `[0][cyl][ray]`, `[0][cyl][sph]`,
/// `[0][sph][cyl]`.
pub fn fill(t: &mut UnprojTables) {
    t.set(MODE_LINEAR, GeomType::Ray, GeomType::Cylinder, cell_ray_cyl);
    t.set(MODE_LINEAR, GeomType::Cylinder, GeomType::Ray, cell_cyl_ray);
    t.set(
        MODE_LINEAR,
        GeomType::Cylinder,
        GeomType::Sphere,
        cyl_sph::cell_cyl_sph,
    );
    t.set(
        MODE_LINEAR,
        GeomType::Sphere,
        GeomType::Cylinder,
        cyl_sph::cell_sph_cyl,
    );
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::geometry::primitives::SpherePrim;

    fn unit_cyl() -> CylinderPrim {
        CylinderPrim {
            center: Vec3::ZERO,
            axis: Vec3::Z,
            r: 1.0,
            hh: 1.0,
        }
    }

    #[test]
    fn ray_pushed_out_through_the_side() {
        let ray = RayPrim {
            origin: Vec3::ZERO,
            dir: Vec3::new(0.5, 0.0, 0.0),
        };
        let mut p = UnprojParams {
            dir: Vec3::X,
            tmax: 10.0,
            ..UnprojParams::default()
        };
        let mut out = UnprojResult::default();
        assert_eq!(
            unproject_ray_cyl(&mut p, &ray, -1, &unit_cyl(), -1, &mut out),
            1
        );
        assert_eq!(out.t, 1.0);
        assert_eq!(out.i_feature, [0x80, 0x40]);
        assert_eq!(out.pt, Vec3::new(1.0, 0.0, 0.0));
        assert_eq!(out.n, Vec3::new(-1.0, 0.0, 0.0));
        // the mirrored cell gives the same depth with the contact on the moved ray
        let mut p = UnprojParams {
            dir: Vec3::new(-1.0, 0.0, 0.0),
            tmax: 10.0,
            ..UnprojParams::default()
        };
        let mut out2 = UnprojResult::default();
        assert_eq!(
            unproject_cyl_ray(&mut p, &unit_cyl(), -1, &ray, -1, &mut out2),
            1
        );
        assert_eq!(out2.t, 1.0);
        assert_eq!(out2.i_feature, [0x40, 0x80]);
        assert_eq!(out2.n, Vec3::new(1.0, 0.0, 0.0));
        assert_eq!(p.dir, Vec3::new(-1.0, 0.0, 0.0));
    }

    #[test]
    fn sphere_least_penetration_side_and_cap() {
        let s = SpherePrim {
            center: Vec3::new(1.5, 0.0, 0.0),
            r: 1.0,
        };
        let mut p = UnprojParams::default();
        let mut out = UnprojResult::default();
        assert_eq!(
            unproject_cyl_sph(&mut p, &unit_cyl(), -1, &s, -1, &mut out),
            1
        );
        assert_eq!(p.dir, Vec3::new(-1.0, 0.0, 0.0));
        assert_eq!(out.t, 0.5);
        assert_eq!(out.n, Vec3::new(1.0, 0.0, 0.0));
        assert_eq!(out.pt, Vec3::new(0.5, 0.0, 0.0));
        assert_eq!(out.i_feature, [0x40, 0x40]);
        let s = SpherePrim {
            center: Vec3::new(0.0, 0.0, 1.25),
            r: 0.5,
        };
        let mut p = UnprojParams::default();
        assert_eq!(
            unproject_cyl_sph(&mut p, &unit_cyl(), -1, &s, -1, &mut out),
            1
        );
        assert_eq!(p.dir, Vec3::new(0.0, 0.0, -1.0));
        assert_eq!(out.t, 0.25);
        assert_eq!(out.i_feature, [0x42, 0x40]);
        assert_eq!(out.pt, Vec3::new(0.0, 0.0, 0.75));
    }
}
