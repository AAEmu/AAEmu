//! **P2a group 4 — round and ray pairs.** The INTERSECT cells of the sphere / cylinder /
//! capsule / ray block and the OVERLAP cells of the sphere row that are not box or triangle
//! tests (`geometry.md` §4.1, §4.5). Ported from the x64 10.x `cryphysics_dedicate.dll`
//! (decompile + disassembly); every operation keeps the binary's precision (single, or double
//! where the code widens with `cvtps2pd`) and association.
//!
//! | cell | x64 | port |
//! |---|---|---|
//! | sph–sph | 0x35160CB0 | [`sph_sph`] |
//! | sph–ray | 0x35160EA0 | [`sph_ray`] |
//! | ray–sph | 0x35161180 (wrapper) | [`ray_sph`] |
//! | cyl–sph | 0x3515FA20 | [`cyl_sph`] |
//! | sph–cyl | 0x3515FDF0 (wrapper) | [`sph_cyl`] |
//! | cyl–ray | 0x3515FE40 | [`cyl_ray`] |
//! | ray–cyl | 0x35160970 (wrapper) | [`ray_cyl`] |
//! | cyl–cyl | 0x3516B7F0 | [`cyl_cyl`] |
//! | cyl–cap | 0x3516D2E0 | [`cyl_cap`] |
//! | cap–cyl | 0x3516E320 (wrapper) | [`cap_cyl`] |
//! | cap–sph | 0x35161C80 | [`cap_sph`] |
//! | sph–cap | 0x35161EA0 (wrapper) | [`sph_cap`] |
//! | cap–ray | 0x35161EF0 | [`cap_ray`] |
//! | ray–cap | 0x35162A50 (wrapper) | [`ray_cap`] |
//! | cap–cap | 0x35161550 | [`cap_cap`] |
//! | OVERLAP sph–sph | 0x35196030 | [`overlap_sph_sph`] |
//! | OVERLAP hf–sph | 0x351975E0 | [`overlap_hf_sph`] |
//! | OVERLAP sph–hf | 0x35197860 (wrapper) | [`overlap_sph_hf`] |
//!
//! The OVERLAP cells sph–box (0x35196E50 → box–sph 0x35196CE0) and sph–tri (0x35197590 →
//! tri–sph helper 0x35196E70) are swap wrappers around the box and triangle groups' tests and are
//! filled by those groups. ray–ray is `ret0` in the binary (left at the default).
//!
//! **Swap wrappers.** All six wrappers of this group call the mirrored pair with `(b, a, out)`
//! and then reverse the four feature bytes (`+36↔+39`, `+37↔+38`); points and normal are left
//! as the callee wrote them ([`swap_features`]).
//!
//! **Output contract.** The functions write exactly the `prim_inters` fields the binary writes,
//! in the same cases (some write points and normal even when they report no contact, e.g.
//! cyl–sph); untouched fields keep their previous values. The border polyline
//! (`+56`/`+64`) is [`PrimInters::border`]: functions that emit border points clear it first
//! (the binary zeroes `nborderpt`) and push.
//!
//! **Constants.** `1e-20` normalisation threshold (bits 0x1E3CE508), the `0.003` parallel-axis
//! threshold (0x3B449BA6), the sine table of 1025 entries over `[0, π/2]` that the cylinder pairs
//! search (built at DLL init by 0x351E5900 as `sinf(i · 0.0015339808)`, see [`sin_table`]).

// `!(a >= b)` is deliberate throughout: it is the binary's `comiss`/`jae` (NaN takes the
// "less" path). Operand order of `x = y + x` mirrors the disassembly.
#![allow(
    clippy::neg_cmp_op_on_partial_ord,
    clippy::assign_op_pattern,
    clippy::explicit_counter_loop
)]

use super::{IntersectFn, OverlapFn, PairTables};
use crate::collision::contact::PrimInters;
use crate::geometry::primitives::{
    CapsulePrim, CylinderPrim, HeightfieldPrim, Primitive, RayPrim, SpherePrim,
};
use crate::ids::GeomType;
use crate::math::{isneg, sgnnz, Vec3};

// ------------------------------------------------------------------------------------ helpers

/// `1e-20f` (0x1E3CE508): the "zero length" threshold of every normalisation here.
const EPS_LEN2: f32 = f32::from_bits(0x1E3C_E508);

/// Feature code of a face (`0x40`).
const FACE: u8 = 0x40;
/// Feature code of a ray "edge" (`0x20`).
const EDGE: u8 = 0x20;

#[inline]
fn d(x: f32) -> f64 {
    f64::from(x)
}

/// `float(int)` of a 0/1 flag (`cvtdq2ps`).
#[inline]
fn flag_f32(b: u32) -> f32 {
    b as f32
}

/// `double(int)` of a 0/1 flag (`cvtdq2pd`).
#[inline]
fn flag_f64(b: u32) -> f64 {
    f64::from(b)
}

/// `sub_35021020` and its inlined copies: `l2 = (x² + y²) + z²`; `v · (1/√l2)` when
/// `l2 > 1e-20`, else `(0, 0, 1)` (NaN also takes the fallback, as `comiss`/`jbe` does).
#[inline]
fn norm_or_z(v: Vec3) -> Vec3 {
    let l2 = (v.x * v.x + v.y * v.y) + v.z * v.z;
    if l2 > EPS_LEN2 {
        let inv = 1.0 / l2.sqrt();
        Vec3::new(v.x * inv, v.y * inv, v.z * inv)
    } else {
        Vec3::new(0.0, 0.0, 1.0)
    }
}

/// `prim_inters` feature bytes `[A.f0, A.f1, B.f0, B.f1]`.
#[inline]
fn set_features(out: &mut PrimInters, b: [u8; 4]) {
    out.i_feature = [[b[0], b[1]], [b[2], b[3]]];
}

/// The swap wrappers' fix-up: bytes `+36↔+39` and `+37↔+38` (the four bytes reversed).
#[inline]
pub fn swap_features(out: &mut PrimInters) {
    let f = out.i_feature;
    out.i_feature = [[f[1][1], f[1][0]], [f[0][1], f[0][0]]];
}

/// Write the single contact point to `pt[1]` and copy it to `pt[0]` (the binary's store order).
#[inline]
fn set_points(out: &mut PrimInters, p: Vec3) {
    out.pt[1] = p;
    out.pt[0] = p;
}

// ------------------------------------------------------------------------------------ sphere

/// sph–sph 0x35160CB0.
///
/// `|c1−c2|²` is evaluated in double (from single differences) against the single-precision
/// `(r1+r2)²`; the unit normal and the midpoint `(c2 + (c1 + n·(r1−r2)))·0.5` are double, the
/// stored normal is the **unnormalised** single `c2 − c1`. All four features are faces.
pub fn sph_sph(a: &SpherePrim, b: &SpherePrim, out: &mut PrimInters) -> bool {
    let (c1, c2) = (a.center, b.center);
    let dx = d(c1.x - c2.x);
    let dz = d(c1.z - c2.z);
    let dy = d(c1.y - c2.y);
    let l2 = (dy * dy + dx * dx) + dz * dz;
    let rs = b.r + a.r;
    if l2 > d(rs * rs) {
        return false;
    }
    let (nx, ny, nz) = if l2 > d(EPS_LEN2) {
        let inv = 1.0 / l2.sqrt();
        (dx * inv, dy * inv, dz * inv)
    } else {
        (0.0, 0.0, 1.0)
    };
    let dr = d(a.r - b.r);
    let px = (d(c2.x) + (d(c1.x) + dr * nx)) * 0.5;
    let py = (d(c2.y) + (d(c1.y) + ny * dr)) * 0.5;
    let pz = (d(c2.z) + (d(c1.z) + nz * dr)) * 0.5;
    set_points(out, Vec3::new(px as f32, py as f32, pz as f32));
    out.n = Vec3::new(c2.x - c1.x, c2.y - c1.y, c2.z - c1.z);
    set_features(out, [FACE; 4]);
    true
}

/// sph–ray 0x35160EA0.
///
/// Quadratic in the segment parameter: `A = |d|²`, `b = d·(o−c)`, `C = |o−c|² − r²` in single,
/// discriminant and roots in double. The first root `(−b−√D)` is taken when
/// `| 2t − A | − A` is negative (inside `[0, A]`), else the second; the second must pass the same
/// test (the sign bit decides: `-0.0` counts as inside). `t/A` is narrowed to single, the hit point
/// is `o + d·t`, the normal `normalize(pt − c)`. Features `{0x40, 0x20, 0x40, 0x20}`.
///
/// Note: a ray that starts inside the sphere hits the far wall (the first root is negative).
pub fn sph_ray(s: &SpherePrim, ray: &RayPrim, out: &mut PrimInters) -> bool {
    let c = s.center;
    let (o, dv) = (ray.origin, ray.dir);
    let a = d((dv.x * dv.x + dv.y * dv.y) + dv.z * dv.z);
    let lx = o.x - c.x;
    let ly = o.y - c.y;
    let lz = o.z - c.z;
    let b = d((dv.x * lx + ly * dv.y) + lz * dv.z);
    let cc = d(((ly * ly + lx * lx) + lz * lz) - s.r * s.r);
    let disc = b * b - cc * a;
    if !(disc >= 0.0) {
        return false;
    }
    let sq = disc.sqrt();
    let mut t = -b - sq;
    let k1 = (((t * 2.0) - a).abs() - a) as f32;
    t += (flag_f64(1 - isneg(k1)) * sq) * 2.0;
    let k2 = (((t * 2.0) - a).abs() - a) as f32;
    if isneg(k2) == 0 {
        return false;
    }
    // `ucomisd`/`je`: equal or unordered gives 0.
    let tq = if a == 0.0 || a.is_nan() { 0.0 } else { t / a };
    let tf = tq as f32;
    let p = Vec3::new(o.x + dv.x * tf, o.y + dv.y * tf, o.z + dv.z * tf);
    set_points(out, p);
    out.n = norm_or_z(Vec3::new(p.x - c.x, p.y - c.y, p.z - c.z));
    set_features(out, [FACE, EDGE, FACE, EDGE]);
    true
}

/// ray–sph 0x35161180: [`sph_ray`]`(b, a)` + [`swap_features`].
pub fn ray_sph(ray: &RayPrim, s: &SpherePrim, out: &mut PrimInters) -> bool {
    let r = sph_ray(s, ray, out);
    swap_features(out);
    r
}

// ------------------------------------------------------------------------------------ cylinder

/// cyl–sph 0x3515FA20 (all single precision).
///
/// The offset `v` from the sphere centre to the closest point of the cylinder is built from an
/// axial part (only when the centre is beyond a cap, `hh − |h| < 0`) and, when the centre is
/// outside the infinite radius (`r² − |a×(s−c)|² < 0`, sign bit), a radial part. The point is
/// `s + v` and the normal `s − pt` (unnormalised, = −v); both are **written even when there is
/// no contact**. The result is the sign bit of `|v|² − R²`. Features all faces.
///
/// Quirks kept: when the centre is inside the cylinder `v = 0` (point = sphere centre, zero
/// normal, contact); a centre inside the radius but beyond a cap uses the axial offset only.
pub fn cyl_sph(cyl: &CylinderPrim, s: &SpherePrim, out: &mut PrimInters) -> bool {
    let (c, a) = (cyl.center, cyl.axis);
    let sc = s.center;
    let h = ((sc.y - c.y) * a.y + a.x * (sc.x - c.x)) + (sc.z - c.z) * a.z;
    let e = cyl.hh - h.abs();
    let fbeyond = flag_f32(isneg(e));
    let sg = sgnnz(h) as f32;
    let mut vx = ((a.x * e) * fbeyond) * sg;
    let mut vy = ((a.y * e) * fbeyond) * sg;
    let mut vz = ((a.z * e) * fbeyond) * sg;
    let dx = sc.x - c.x;
    let dy = sc.y - c.y;
    let dz = sc.z - c.z;
    let k5 = a.y * dx - a.x * dy;
    let k4 = a.z * dy - a.y * dz;
    let k3 = a.x * dz - a.z * dx;
    let k = cyl.r * cyl.r - ((k3 * k3 + k4 * k4) + k5 * k5);
    if isneg(k) != 0 {
        let h2 = (dy * a.y + a.x * dx) + dz * a.z;
        let px = dx - a.x * h2;
        let py = dy - a.y * h2;
        let pz = dz - a.z * h2;
        let n = norm_or_z(Vec3::new(px, py, pz));
        vy = (py - n.y * cyl.r) + vy;
        vz = (pz - n.z * cyl.r) + vz;
        vx += px - cyl.r * n.x;
    }
    let p = Vec3::new(sc.x + vx, sc.y + vy, sc.z + vz);
    set_points(out, p);
    out.n = Vec3::new(sc.x - p.x, sc.y - p.y, sc.z - p.z);
    set_features(out, [FACE; 4]);
    isneg(((vy * vy + vx * vx) + vz * vz) - s.r * s.r) != 0
}

/// sph–cyl 0x3515FDF0: [`cyl_sph`]`(b, a)` + [`swap_features`].
pub fn sph_cyl(s: &SpherePrim, cyl: &CylinderPrim, out: &mut PrimInters) -> bool {
    let r = cyl_sph(cyl, s, out);
    swap_features(out);
    r
}

/// The hit point / normal tail shared by both cylinder–ray branches (0x351606F9…).
fn cyl_ray_finish(
    cyl: &CylinderPrim,
    ray: &RayPrim,
    tn: f64,
    td: f64,
    side: bool,
    out: &mut PrimInters,
) -> bool {
    if !(td * tn >= 0.0) {
        return false;
    }
    let t = if td == 0.0 || td.is_nan() {
        0.0
    } else {
        tn / td
    };
    let tf = t as f32;
    let (c, a) = (cyl.center, cyl.axis);
    let (o, dv) = (ray.origin, ray.dir);
    let p = Vec3::new(dv.x * tf + o.x, dv.y * tf + o.y, dv.z * tf + o.z);
    set_points(out, p);
    let nx = p.x - c.x;
    let nz = p.z - c.z;
    if side {
        let ny = p.y - c.y;
        let h = (ny * a.y + nx * a.x) + nz * a.z;
        out.n = norm_or_z(Vec3::new(nx - a.x * h, ny - a.y * h, nz - a.z * h));
        set_features(out, [FACE, EDGE, FACE, EDGE]);
    } else {
        let h = (a.x * nx + (p.y - c.y) * a.y) + nz * a.z;
        let sg = sgnnz(h);
        let sgf = sg as f32;
        out.n = Vec3::new(a.x * sgf, a.y * sgf, a.z * sgf);
        let fb = (((sg + 1) >> 1) + 0x41) as u8;
        set_features(out, [fb, EDGE, fb, EDGE]);
    }
    true
}

/// Cap-disc test of cyl–ray: the ray parameter (scaled by `den = |d·a|`) at the cap plane
/// `e`, and whether that point is inside the segment and the disc.
/// Returns `(q, den, inside)`; `q` and `den` are the double values the caller keeps.
fn cyl_ray_cap(cyl: &CylinderPrim, ray: &RayPrim, s: f32, e: f32) -> (f64, f64, u32) {
    let (c, a, r) = (cyl.center, cyl.axis, cyl.r);
    let (o, dv) = (ray.origin, ray.dir);
    let ex = a.x * e;
    let ey = a.y * e;
    let ez = a.z * e;
    let q = ((s * (c.x - o.x) - ex) * a.x + ((c.y - o.y) * s - ey) * a.y)
        + ((c.z - o.z) * s - ez) * a.z;
    let qd = d(q);
    let den = d((dv.x * a.x + dv.y * a.y) + dv.z * a.z).abs();
    let rden = d(r) * den;
    let rden2 = rden * rden;
    let chk1 = (((qd * 2.0) - den).abs() - den) as f32;
    let denf = den as f32;
    let px = denf * (o.x - c.x) + q * dv.x;
    let py = (o.y - c.y) * denf + q * dv.y;
    let pz = (o.z - c.z) * denf + q * dv.z;
    let k2 = py * a.z - pz * a.y;
    let k6 = pz * a.x - a.z * px;
    let k3 = a.y * px - py * a.x;
    let sum = (k2 * k2 + k6 * k6) + k3 * k3;
    let chk2 = (d(sum) - rden2) as f32;
    (qd, den, isneg(chk2) & isneg(chk1))
}

/// Side-surface test of cyl–ray for one root: `(|2t − A| − A, |h(t)| − hh·A)` as singles.
fn cyl_ray_side_check(cyl: &CylinderPrim, ray: &RayPrim, t: f64, a2: f64, hha: f64) -> u32 {
    let (c, a) = (cyl.center, cyl.axis);
    let (o, dv) = (ray.origin, ray.dir);
    let af = a2 as f32;
    let tf = t as f32;
    let k_range = (((t * 2.0) - a2).abs() - a2) as f32;
    let px = (o.x - c.x) * af + tf * dv.x;
    let py = (o.y - c.y) * af + tf * dv.y;
    let pz = (o.z - c.z) * af + tf * dv.z;
    let h = (py * a.y + a.x * px) + pz * a.z;
    let k_h = (d(h.abs()) - hha) as f32;
    isneg(k_h) & isneg(k_range)
}

/// cyl–ray 0x3515FE40.
///
/// 1. Near cap (the cap the ray enters first, `sgnnz(d·a)`): the hit parameter `q/|d·a|` on the
///    cap plane, accepted when inside the segment and inside the disc (`|a × p|² < (r·den)²`,
///    double).
/// 2. On a near-cap hit the same cap is re-evaluated (identical values) and used. Otherwise the
///    side surface is solved as a quadratic in double (`u = d×a`, `w = (o−c)×a`): first root
///    `−B−√D`, else `−B+√D` (chosen by the sign bits of the range and height checks); a valid side
///    root wins, else the **far** cap is tried.
/// 3. The point is `o + d·t` (`t` = double quotient narrowed to single). Side normal: the
///    radial direction normalised (`sub_35021020`), features `{0x40,0x20,0x40,0x20}`; cap normal:
///    `±axis` (not normalised), features `{0x41|0x42, 0x20, …}` (`0x42` for the `+axis` cap).
///
/// The near-cap check is the only place the ray direction sign matters; a ray starting inside
/// the cylinder finds the far cap or side, as in the binary.
pub fn cyl_ray(cyl: &CylinderPrim, ray: &RayPrim, out: &mut PrimInters) -> bool {
    let (c, a, hh) = (cyl.center, cyl.axis, cyl.hh);
    let (o, dv) = (ray.origin, ray.dir);
    let dn = (dv.x * a.x + dv.y * a.y) + dv.z * a.z;
    let s = sgnnz(dn) as f32;
    let (_, _, hit_near) = cyl_ray_cap(cyl, ray, s, hh);
    // Part 2: e = (2·hit − 1)·hh: the near cap again on a hit, else the far cap.
    let e = ((2 * hit_near as i32 - 1) as f32) * hh;
    let (q2, den2, hit_cap2) = cyl_ray_cap(cyl, ray, s, e);
    if hit_near == 0 {
        let lx = o.x - c.x;
        let ly = o.y - c.y;
        let lz = o.z - c.z;
        let wx = d(ly * a.z - lz * a.y);
        let wy = d(lz * a.x - a.z * lx);
        let wz = d(a.y * lx - ly * a.x);
        let ux = d(a.z * dv.y - a.y * dv.z);
        let uy = d(dv.z * a.x - a.z * dv.x);
        let uz = d(a.y * dv.x - dv.y * a.x);
        let aa = (ux * ux + uy * uy) + uz * uz;
        let bb = (ux * wx + uy * wy) + uz * wz;
        let cc = ((wx * wx + wy * wy) + wz * wz) - d(cyl.r * cyl.r);
        let disc = bb * bb - cc * aa;
        if disc >= 0.0 {
            let sq = disc.sqrt();
            let hha = d(hh) * aa;
            let t1 = -bb - sq;
            let ok1 = cyl_ray_side_check(cyl, ray, t1, aa, hha);
            let t2 = t1 + (flag_f64(1 - ok1) * sq) * 2.0;
            if cyl_ray_side_check(cyl, ray, t2, aa, hha) != 0 {
                return cyl_ray_finish(cyl, ray, t2, aa, true, out);
            }
        }
    }
    if hit_cap2 == 0 {
        return false;
    }
    cyl_ray_finish(cyl, ray, q2, den2, false, out)
}

/// ray–cyl 0x35160970: [`cyl_ray`]`(b, a)` + [`swap_features`].
pub fn ray_cyl(ray: &RayPrim, cyl: &CylinderPrim, out: &mut PrimInters) -> bool {
    let r = cyl_ray(cyl, ray, out);
    swap_features(out);
    r
}

// ------------------------------------------------------------------------------------ capsule

/// cap–sph 0x35161C80 (single precision).
///
/// Closest segment point via the idioms `m = min(hh, h)` (`((hh+h) − |hh−h|)·0.5`) and
/// `t = max(−hh, m)`, where `h = a·(c − s)`; `v = (c − s) − a·t`. Contact when
/// `|v|² < (R + r)²` (NaN counts as contact). The point is `s + R·n̂` and the normal `−n̂`
/// (`n̂ = normalize(v)`, `(0,0,1)` for zero length). Features all faces.
///
/// Quirk kept: the segment point is `c − a·t` with `t` clamped from `a·(c − s)` — i.e. the
/// projection is mirrored; for a symmetric segment `[−hh, hh]` this is the usual closest point.
pub fn cap_sph(cap: &CapsulePrim, s: &SpherePrim, out: &mut PrimInters) -> bool {
    let (c, a, hh) = (cap.center, cap.axis, cap.hh);
    let sc = s.center;
    let dx = c.x - sc.x;
    let dy = c.y - sc.y;
    let dz = c.z - sc.z;
    let h = (dy * a.y + a.x * dx) + dz * a.z;
    let m = ((hh + h) - (hh - h).abs()) * 0.5;
    let nhh = -hh;
    let t = ((nhh - m).abs() + (nhh + m)) * 0.5;
    let vz = dz - a.z * t;
    let vy = dy - a.y * t;
    let vx = dx - a.x * t;
    let l2 = (vy * vy + vx * vx) + vz * vz;
    let rs = s.r + cap.r;
    if l2 >= rs * rs {
        return false;
    }
    let n = norm_or_z(Vec3::new(vx, vy, vz));
    let rr = s.r;
    let p = Vec3::new(rr * n.x + sc.x, n.y * rr + sc.y, n.z * rr + sc.z);
    set_points(out, p);
    out.n = Vec3::new(-n.x, -n.y, -n.z);
    set_features(out, [FACE; 4]);
    true
}

/// sph–cap 0x35161EA0: [`cap_sph`]`(b, a)` + [`swap_features`].
pub fn sph_cap(s: &SpherePrim, cap: &CapsulePrim, out: &mut PrimInters) -> bool {
    let r = cap_sph(cap, s, out);
    swap_features(out);
    r
}

/// One root of cap–ray: the range test against the running best `tn/td`
/// (`|tn·A − td·t·2| − |−(tn·A)|`) and the height test `ok_h`, returning the selection flag.
#[inline]
fn cap_ray_range(tn: f64, td: f64, t: f64, a: f64) -> f32 {
    let x = tn * a;
    ((x - (td * t) * 2.0).abs() - (-x).abs()) as f32
}

/// cap–ray 0x35161EF0.
///
/// Keeps the best hit as a double quotient `tn/td` (initially `1/1`, i.e. the segment end) and a
/// feature code (initially −1 = none):
/// 1. the two end spheres (`s = −1` → 0x41, `s = +1` → 0x42) in that order, each root accepted
///    when it lies before the current best and its projection on the axis is **beyond** the
///    cylindrical part (`|hd·t + h0·A| > hh·A`);
/// 2. the side surface (quadratic of `d×a`, `(o−c)×a` in double; feature 0x40), roots accepted
///    when before the best and **within** the cylindrical part.
///
/// Selections are branch-free (`x += (y − x)·flag`). The point is `o + d·(tn/td)` and the normal
/// the radial direction (side) or `pt − (c + a·e)` (end spheres), normalised; features
/// `{f, 0x20, f, 0x20}`. Nothing is written when there is no hit.
pub fn cap_ray(cap: &CapsulePrim, ray: &RayPrim, out: &mut PrimInters) -> bool {
    let (c, a, hh) = (cap.center, cap.axis, cap.hh);
    let (o, dv) = (ray.origin, ray.dir);
    let aa = d((dv.x * dv.x + dv.y * dv.y) + dv.z * dv.z);
    let h0 = ((o.y - c.y) * a.y + a.x * (o.x - c.x)) + (o.z - c.z) * a.z;
    let hd = (a.y * dv.y + a.x * dv.x) + a.z * dv.z;
    let r2 = cap.r * cap.r;
    let mut tn = 1.0f64;
    let mut td = 1.0f64;
    let mut feat: i32 = -1;
    let mut code: i32 = 0x41;
    let mut sgn: i32 = -1;
    for _ in 0..2 {
        let e = (sgn as f32) * cap.hh;
        let ccx = c.x + a.x * e;
        let ccy = a.y * e + c.y;
        let ccz = a.z * e + c.z;
        let lx = o.x - ccx;
        let ly = o.y - ccy;
        let lz = o.z - ccz;
        let b = (ly * dv.y + lx * dv.x) + lz * dv.z;
        let cc = ((ly * ly + lx * lx) + lz * lz) - r2;
        let bd = d(b);
        let disc = bd * bd - d(cc) * aa;
        if disc >= 0.0 {
            let sq = disc.sqrt();
            let t = -bd - sq;
            let hha = d(hh) * aa;
            let h0a = d(h0) * aa;
            let k1 = cap_ray_range(tn, td, t, aa);
            let k2 = (hha - (d(hd) * t + h0a).abs()) as f32;
            let f1 = isneg(k2) & isneg(k1);
            let fsel = (feat & (f1 as i32 - 1)) | (code & (f1 as i32).wrapping_neg());
            let t2 = t + sq * 2.0;
            let f1d = flag_f64(f1);
            td += (aa - td) * f1d;
            tn += (t - tn) * f1d;
            let k3 = cap_ray_range(tn, td, t2, aa);
            let k4 = (hha - (d(hd) * t2 + h0a).abs()) as f32;
            let f2 = isneg(k4) & isneg(k3);
            feat = (fsel & (f2 as i32 - 1)) | (code & (f2 as i32).wrapping_neg());
            let f2d = flag_f64(f2);
            tn += (t2 - tn) * f2d;
            td += (aa - td) * f2d;
        }
        sgn += 2;
        code += 1;
    }
    // Side surface.
    let lx = o.x - c.x;
    let ly = o.y - c.y;
    let lz = o.z - c.z;
    let wx = d(ly * a.z - lz * a.y);
    let wy = d(lz * a.x - lx * a.z);
    let wz = d(lx * a.y - ly * a.x);
    let ux = d(a.z * dv.y - a.y * dv.z);
    let uy = d(dv.z * a.x - a.z * dv.x);
    let uz = d(a.y * dv.x - dv.y * a.x);
    let a2 = (uy * uy + ux * ux) + uz * uz;
    let b2 = (uy * wy + ux * wx) + uz * wz;
    let c2 = ((wy * wy + wx * wx) + wz * wz) - d(r2);
    let disc = b2 * b2 - c2 * a2;
    if disc >= 0.0 {
        let a2f = a2 as f32;
        let hha = d(hh) * a2;
        let sq = disc.sqrt();
        let t = -b2 - sq;
        let side_h = |t: f64| -> f32 {
            let tf = t as f32;
            let px = (o.x - c.x) * a2f + tf * dv.x;
            let py = (o.y - c.y) * a2f + tf * dv.y;
            let pz = (o.z - c.z) * a2f + tf * dv.z;
            let h = (py * a.y + a.x * px) + pz * a.z;
            (d(h.abs()) - hha) as f32
        };
        let k1 = cap_ray_range(tn, td, t, a2);
        let k2 = side_h(t);
        let f1 = isneg(k2) & isneg(k1);
        let f1d = flag_f64(f1);
        tn += (t - tn) * f1d;
        let t2 = t + sq * 2.0;
        td += (a2 - td) * f1d;
        let fsel = ((f1 as i32 - 1) & feat) | ((f1 as i32).wrapping_neg() & 0x40);
        let k3 = cap_ray_range(tn, td, t2, a2);
        let k4 = side_h(t2);
        let f2 = isneg(k4) & isneg(k3);
        feat = ((f2 as i32 - 1) & fsel) | ((f2 as i32).wrapping_neg() & 0x40);
        let f2d = flag_f64(f2);
        tn += (t2 - tn) * f2d;
        td += (a2 - td) * f2d;
    }
    if feat < 0 {
        return false;
    }
    let t = if td == 0.0 || td.is_nan() {
        0.0
    } else {
        tn / td
    };
    let tf = t as f32;
    let p = Vec3::new(dv.x * tf + o.x, dv.y * tf + o.y, dv.z * tf + o.z);
    set_points(out, p);
    let n = if feat == 0x40 {
        let nx = p.x - c.x;
        let ny = p.y - c.y;
        let nz = p.z - c.z;
        let h = (ny * a.y + nx * a.x) + nz * a.z;
        Vec3::new(nx - a.x * h, ny - a.y * h, nz - a.z * h)
    } else {
        let e = ((2 * feat - 0x83) as f32) * hh;
        Vec3::new(
            (p.x - c.x) - a.x * e,
            (p.y - c.y) - a.y * e,
            (p.z - c.z) - a.z * e,
        )
    };
    out.n = norm_or_z(n);
    let fb = feat as u8;
    set_features(out, [fb, EDGE, fb, EDGE]);
    true
}

/// ray–cap 0x35162A50: [`cap_ray`]`(b, a)` + [`swap_features`].
pub fn ray_cap(ray: &RayPrim, cap: &CapsulePrim, out: &mut PrimInters) -> bool {
    let r = cap_ray(cap, ray, out);
    swap_features(out);
    r
}

/// cap–cap 0x35161550.
///
/// Emits border points (the binary resets `nborderpt` and appends; there is no capacity
/// check):
/// 1. each end sphere of each capsule against the other's segment (A's ends with normal sign
///    +1, then B's with −1): closest segment point by the min/max idioms, contact when
///    `|v|² < (rA + rB)²`; the point is `end + r·n̂` on the end's capsule, `n = ±n̂`;
/// 2. the two cylindrical parts: `n = aA × aB`, accepted when the signed distance along `n`
///    is below `rA + rB` and both foot points are within the half-lengths (all three tested
///    by the sign bit of their AND); `n̂ = n·sgnnz(D)/|n|` (double `√(1/|n|²)`).
///
/// `pt[0] = pt[1] =` the **first** border point, `n` = the **last** written normal, features all
/// faces. Returns whether any point was emitted. Parallel axes skip step 2 (`|n|² = 0` makes
/// every check NaN).
pub fn cap_cap(a: &CapsulePrim, b: &CapsulePrim, out: &mut PrimInters) -> bool {
    out.border.clear();
    let caps = [a, b];
    let mut sgn_n: i32 = 1;
    for (k, p) in caps.iter().enumerate() {
        let q = caps[k ^ 1];
        let mut s: i32 = -1;
        while s <= 1 {
            let sf = s as f32;
            let e = sf * p.hh;
            let vx = (q.center.x - p.center.x) - p.axis.x * e;
            let vy = (q.center.y - p.center.y) - p.axis.y * e;
            let vz = (q.center.z - p.center.z) - p.axis.z * e;
            let h = (q.axis.x * vx + vy * q.axis.y) + vz * q.axis.z;
            let m = ((q.hh + h) - (q.hh - h).abs()) * 0.5;
            let nhh = -q.hh;
            let t = ((nhh - m).abs() + (nhh + m)) * 0.5;
            let wx = vx - q.axis.x * t;
            let wy = vy - q.axis.y * t;
            let wz = vz - q.axis.z * t;
            let l2 = (wx * wx + wy * wy) + wz * wz;
            let rs = b.r + a.r;
            if !(l2 >= rs * rs) {
                let n = norm_or_z(Vec3::new(wx, wy, wz));
                let e2 = sf * p.hh;
                let r = p.r;
                out.border.push(Vec3::new(
                    (p.axis.x * e2 + p.center.x) + r * n.x,
                    (p.axis.y * e2 + p.center.y) + n.y * r,
                    (p.axis.z * e2 + p.center.z) + n.z * r,
                ));
                let g = sgn_n as f32;
                out.n = Vec3::new(g * n.x, n.y * g, n.z * g);
            }
            s += 2;
        }
        sgn_n -= 2;
    }
    // Cylindrical parts.
    let (aa, ba) = (a.axis, b.axis);
    let nx = aa.y * ba.z - aa.z * ba.y;
    let ny = ba.x * aa.z - aa.x * ba.z;
    let nz = aa.x * ba.y - ba.x * aa.y;
    let dx = b.center.x - a.center.x;
    let dy = b.center.y - a.center.y;
    let dz = b.center.z - a.center.z;
    let n2 = d((ny * ny + nx * nx) + nz * nz);
    let dd = d((ny * dy + nx * dx) + nz * dz);
    let rs = b.r + a.r;
    // (d × aB)·n and (d × aA)·n
    let wbx = dy * ba.z - dz * ba.y;
    let wby = dz * ba.x - dx * ba.z;
    let wbz = dx * ba.y - dy * ba.x;
    let sa = d((wby * ny + wbx * nx) + wbz * nz);
    let wax = dy * aa.z - dz * aa.y;
    let way = dz * aa.x - dx * aa.z;
    let waz = dx * aa.y - dy * aa.x;
    let sb = (way * ny + wax * nx) + waz * nz;
    let chk_b = (d(sb.abs()) - d(b.hh) * n2) as f32;
    let chk_a = (sa.abs() - d(a.hh) * n2) as f32;
    let chk_r = (dd * dd - d(rs * rs) * n2) as f32;
    if isneg(f32::from_bits(
        chk_r.to_bits() & chk_a.to_bits() & chk_b.to_bits(),
    )) != 0
    {
        let inv = 1.0 / n2;
        let sg = sgnnz(dd as f32);
        let s = (inv.sqrt() * f64::from(sg)) as f32;
        let nnx = s * nx;
        let nny = ny * s;
        let nnz = nz * s;
        let ta = (sa * inv) as f32;
        let r = a.r;
        out.border.push(Vec3::new(
            (aa.x * ta + a.center.x) + r * nnx,
            (aa.y * ta + a.center.y) + nny * r,
            (aa.z * ta + a.center.z) + nnz * r,
        ));
        out.n = Vec3::new(nnx, nny, nnz);
    }
    if out.border.is_empty() {
        return false;
    }
    set_points(out, out.border[0]);
    set_features(out, [FACE; 4]);
    true
}

// ------------------------------------------------------------------------------------ sine table

/// `0.0015339808f` (0x3AC90FDB, π/2048): the step of the engine's sine table.
const SIN_TABLE_STEP: f32 = f32::from_bits(0x3AC9_0FDB);

/// The engine's quarter-wave sine table (`0x3537B450`, 1025 floats): entry `i` is
/// `sinf(i · 0.0015339808)` (MSVCR100 `sinf`, ported bit-exactly in [`crate::math::crt::sinf`]),
/// filled by the DLL's static initialiser 0x351E5900. `cos(i)` is read as entry `1024 − i`
/// (the binary's `unk_3537C450 − i`).
#[must_use]
pub fn sin_table() -> &'static [f32; 1025] {
    static TABLE: std::sync::OnceLock<[f32; 1025]> = std::sync::OnceLock::new();
    TABLE.get_or_init(|| {
        let mut t = [0.0f32; 1025];
        for (i, v) in t.iter_mut().enumerate() {
            *v = crate::math::crt::sinf(i as f32 * SIN_TABLE_STEP);
        }
        t
    })
}

/// `|cross(axisA, axisB)|` (or `|dot|`) below which two axes count as parallel (perpendicular).
const AXIS_EPS: f32 = f32::from_bits(0x3B44_9BA6);

/// `0.80999994` (0x3F4F5C28): picks the helper axis for a vector perpendicular to an axis.
const PERP_PICK: f32 = f32::from_bits(0x3F4F_5C28);

/// The `{1,2,0}` / `{2,0,1}` index tables at 0x35209F50 / 0x35209F60.
const IDX_NEXT: [usize; 2] = [1, 2];
const IDX_PREV: [usize; 2] = [2, 0];

/// A unit vector perpendicular to `a` as the cylinder pairs build it: `i = signbit(0.81·|a|² −
/// ax²)`; `v[i] = 0`, `v[next(i)] = a[prev(i)]`, `v[prev(i)] = −a[next(i)]`, normalised by
/// [`norm_or_z`].
fn perp_axis(a: Vec3) -> Vec3 {
    let ax2 = a.x * a.x;
    let i = isneg(((a.y * a.y + ax2) + a.z * a.z) * PERP_PICK - ax2) as usize;
    let (nx, pv) = (IDX_NEXT[i], IDX_PREV[i]);
    let mut v = [0.0f32; 3];
    v[i] = 0.0;
    v[nx] = a[pv];
    v[pv] = -a[nx];
    norm_or_z(Vec3::new(v[0], v[1], v[2]))
}

/// One step of the refinement bisection of cyl–cap / cyl–cyl on a 4096-step circle: quadrant
/// `(mid >> 10) & 3`, mirrored table index and the sine/cosine signs.
/// Returns `(sin_entry, cos_entry, sin_sign, cos_sign, quadrant)`.
#[inline]
fn circle_lookup(mid: i32) -> (f32, f32, i32, i32, i32) {
    let tab = sin_table();
    let q = (mid >> 10) & 3;
    let b0 = q & 1;
    let idx = ((mid & 0x3FF) * (1 - 2 * b0) + ((-b0) & 0x3FF)) as usize;
    let s = tab[idx];
    let c = tab[1024 - idx];
    let cos_sign = 1 - ((q ^ (q << 1)) & 2);
    let sin_sign = 1 - (q & 2);
    (s, c, sin_sign, cos_sign, q)
}

/// Frame shared by the cap-circle tests of cyl–cap / cyl–cyl.
struct CircleGeo {
    /// `n̂` (normalised `aA × aB`, or a perpendicular of `aA` for parallel axes).
    n: Vec3,
    /// `aQ × n̂` as the binary associates it.
    e: Vec3,
    /// `aA · aB`.
    dot: f32,
    /// `|aA × aB|` (unnormalised).
    len: f32,
    /// Sign applied to the height correction: −1 for cyl–cap, `−1 / +1` for the two passes of
    /// cyl–cyl (`h + x·outer`; with −1 this is bit-identical to cyl–cap's `h − x`).
    outer: f32,
}

/// The cap circle of `p` (offset `v` = circle centre − `q.center`, in the caller's rounding)
/// against the round segment `q` (inlined twice in cyl–cyl 0x3516B7F0 and once in cyl–cap
/// 0x3516D2E0). Returns the contact flag; border points go to `out.border`.
///
/// `p = −v·n̂`, `q = −v·e` are the circle centre's coordinates in the plane spanned by `n̂, e`.
/// * `|aA·aB| < 0.003`: analytic disc test, flag only (`|q| < rQ` and
///   `| |pa−rP|(pa−rP) + |pa+rP|(pa+rP) − 2(rQ²−q²) | − | … − … |` negative, then
///   `|h| < hhQ`).
/// * else the cubic discriminant `((c − b²/3)³/27 + ((c/6 − b²/27)·b − t)²)` must be `> 0`; a
///   bisection over the sine table (`lo=0, hi=1024`, side chosen by the sign bit of
///   `(q − k6·s)(c·k7) − (sgq·s)(p − c·k13)` XOR `sgp ≠ sgq`) gives the angle; its height check
///   (with the **last probed** sine) sets the flag, then two refinement bisections over
///   half-circles (`|lo − hi| > 2`, quadrant-folded table lookups) emit the points whose height
///   is within `hhQ` (and, for a cylinder `q`, whose distance to `q`'s axis is below `rQ`).
fn cap_circle(
    p_cyl: &CylinderPrim,
    q_prim: &CylinderPrim,
    q_is_cylinder: bool,
    g: &CircleGeo,
    v: Vec3,
    out: &mut PrimInters,
) -> bool {
    let (n, ev, dot, len) = (g.n, g.e, g.dot, g.len);
    let rp = p_cyl.r;
    let (qa, rq, hhq) = (q_prim.axis, q_prim.r, q_prim.hh);
    let rp2 = rp * rp;
    let rq2 = rq * rq;
    let adot = dot.abs();
    let p = -((n.x * v.x + v.y * n.y) + v.z * n.z);
    let q = -((ev.x * v.x + v.y * ev.y) + v.z * ev.z);
    let h_seg = (v.y * qa.y + qa.x * v.x) + v.z * qa.z;
    if !(adot >= AXIS_EPS) {
        if q.abs() >= rq {
            return false;
        }
        let pa = p.abs();
        let am = pa - rp;
        let ap = pa + rp;
        let x = am.abs() * am;
        let y = ap.abs() * ap;
        let chk = ((x + y) - (rq2 - q * q) * 2.0).abs() - (x - y).abs();
        return isneg(chk) != 0 && !(h_seg.abs() >= hhq);
    }
    let dot2 = dot * dot;
    let k81 = dot2 + 1.0;
    let b = ((rp2 * k81 + rq2) - p * p) - q * q;
    let rqp = rq * rp;
    let c = ((rqp * rqp) * k81 + (rp2 * dot) * (rp2 * dot)) - (p * p * dot2 + q * q) * rp2;
    let t84 = (((rqp * rp) * dot) * ((rqp * rp) * dot)) * 0.5;
    let b2 = b * b;
    let u = c - b2 * f32::from_bits(0x3EAA_AAAB);
    let w = (c * f32::from_bits(0x3E2A_AAAB) - b2 * f32::from_bits(0x3D17_B426)) * b - t84;
    let disc = ((u * u) * u) * f32::from_bits(0x3D17_B426) + w * w;
    if !(disc > 0.0) {
        return false;
    }
    let sgq = sgnnz(q);
    let sgp = sgnnz(p);
    let flip = ((1 - sgp * sgq) >> 1) as usize;
    let sgqf = sgq as f32;
    let sgpf = sgp as f32;
    let k13 = sgpf * rp;
    let k7 = sgpf * adot;
    let k6 = (sgqf * rp) * adot;
    let tab = sin_table();
    let mut lohi = [0i32, 1024i32];
    let mut sn;
    loop {
        let mid = (lohi[1] + lohi[0]) >> 1;
        sn = tab[mid as usize];
        let cs = tab[(1024 - mid) as usize];
        let val = (q - k6 * sn) * (cs * k7) - (sgqf * sn) * (p - cs * k13);
        lohi[(isneg(val) as usize) ^ flip] = mid;
        if lohi[1] - lohi[0] <= 1 {
            break;
        }
    }
    let sgdot = sgnnz(dot) as f32;
    let hcheck = h_seg + ((((sgdot * len) * sn) * sgqf) * rp) * g.outer;
    if hcheck.abs() >= hhq {
        return false;
    }
    // Refinement: two half-circle bisections for the points at distance rQ from q's axis.
    let start = ((lohi[0] * sgq - 1024) * sgp + 1024) & 0xFFF;
    let mut hi0 = start - 1024;
    let k13b = adot * rp;
    let pa = p_cyl.axis;
    for _ in 0..2 {
        let mut lh = [start, hi0];
        let (mut s2, mut c_term, mut quad);
        loop {
            let mid = (lh[0] + lh[1]) >> 1;
            let (sn2, cs2, ss, cs_s, qd) = circle_lookup(mid);
            s2 = sn2;
            quad = qd;
            c_term = ((cs_s as f32) * cs2) * rp;
            let s_term = ((ss as f32) * sn2) * k13b;
            let val = rq2 - ((c_term - p) * (c_term - p) + (s_term - q) * (s_term - q));
            lh[isneg(val) as usize] = mid;
            let dd = lh[0] - lh[1];
            let m = dd >> 31;
            if (dd.wrapping_add(m) ^ m) <= 2 {
                break;
            }
        }
        let s7 = ((1 - (quad & 2)) as f32 * (sgdot * s2)) * rp;
        if lh[1] != hi0 && !((h_seg + (s7 * len) * g.outer).abs() >= hhq) {
            let t1 = pa.y * n.z - pa.z * n.y;
            let t2 = n.x * pa.z - n.z * pa.x;
            let t3 = n.y * pa.x - n.x * pa.y;
            let x = (n.x * c_term + v.x) + s7 * t1;
            let y = (n.y * c_term + v.y) + t2 * s7;
            let z = (n.z * c_term + v.z) + t3 * s7;
            let inside = if q_is_cylinder {
                let k5 = qa.z * y - qa.y * z;
                let k4 = qa.y * x - qa.x * y;
                let k3 = qa.x * z - qa.z * x;
                !((k5 * k5 + k3 * k3) + k4 * k4 >= rq2)
            } else {
                true
            };
            if inside {
                let qc = q_prim.center;
                out.border.push(Vec3::new(x + qc.x, y + qc.y, z + qc.z));
            }
        }
        hi0 += 0x800;
    }
    true
}

// ------------------------------------------------------------------------------------ cyl–cyl

/// The four cap-circle pairs of cyl–cyl (0x3516CC48…), only for non-parallel axes. For each cap
/// `eA` of A and `eB` of B, with `w = (aA·dot − aB)/|n|` and `g = aA × w`, the intersection
/// points of the two circles' planes are solved as quadratics (no square root of the
/// discriminant-like `v91` is ever taken for the side test above; here proper `sqrt`s are used).
fn cyl_cyl_circles(a: &CylinderPrim, b: &CylinderPrim, dot: f32, len: f32, out: &mut PrimInters) {
    let inv = 1.0 / len;
    let (aa, ab) = (a.axis, b.axis);
    let wx = (aa.x * dot - ab.x) * inv;
    let wy = (aa.y * dot - ab.y) * inv;
    let wz = (aa.z * dot - ab.z) * inv;
    let bix = ab.x * inv;
    let biy = ab.y * inv;
    let biz = ab.z * inv;
    let g165 = wz * aa.y - wy * aa.z;
    let g166 = wx * aa.z - wz * aa.x;
    let g167 = wy * aa.x - wx * aa.y;
    let mut sa: i32 = -1;
    while sa <= 1 {
        let mut sb: i32 = -1;
        while sb <= 1 {
            let ea = (sa as f32) * a.hh;
            let eb = (sb as f32) * b.hh;
            let (bex, bey, bez) = (ab.x * eb, ab.y * eb, ab.z * eb);
            let v177 = ((aa.x * ea + a.center.x) - b.center.x) - bex;
            let v178 = ((aa.y * ea + a.center.y) - b.center.y) - bey;
            let v180 = ((aa.z * ea + a.center.z) - b.center.z) - bez;
            let v181 = (v177 * bix + biy * v178) + biz * v180;
            let v182 = v177 + v181 * wx;
            let v183 = v178 + v181 * wy;
            let v184 = v180 + v181 * wz;
            let v185 = (g166 * v183 + v182 * g165) + g167 * v184;
            let v186 = (v182 * v182 + v183 * v183) + v184 * v184;
            let v187 = v185 * v185;
            let rb2 = b.r * b.r;
            let v189 = v187 - (v186 - rb2);
            let v190 = a.r * a.r - v181 * v181;
            let v249 = v189 - v187;
            let v246 = v189 - v190;
            let v243 = v246 - v187;
            let sum = v190 + v189;
            if ((sum - (v190 - v189).abs()) * 0.5) > 0.0 {
                let v244 = (sum - v187 * 2.0).abs() - v246.abs();
                let solve = isneg(v244) != 0
                    || isneg(((v243.abs() * v243) * (sgnnz(v249) as f32)) - (v187 * v190) * 4.0)
                        != 0;
                if solve {
                    let s = v189.sqrt();
                    let mut sg: i32 = -1;
                    while sg <= 1 {
                        let t = (sg as f32) * s - v185;
                        if !(t * t >= v190) {
                            out.border.push(Vec3::new(
                                ((v182 + t * g165) + bex) + b.center.x,
                                ((g166 * t + v183) + bey) + b.center.y,
                                ((g167 * t + v184) + bez) + b.center.z,
                            ));
                        }
                        sg += 2;
                    }
                }
                let v200 = ((v182 * 2.0) * g165 + (v183 * 2.0) * g166) + (v184 * 2.0) * g167;
                let v201 = (v186 + v190) - b.r * b.r;
                if (v200 * v200) * v190 > ((-v201).abs() + v201) * 0.5 {
                    let s2 = v190.sqrt();
                    let mut sg: i32 = -1;
                    while sg <= 1 {
                        let u = (sg as f32) * s2;
                        let py = g166 * u + v183;
                        let px = v182 + u * g165;
                        let pz = g167 * u + v184;
                        if !((py * py + px * px) + pz * pz >= b.r * b.r) {
                            out.border.push(Vec3::new(
                                (px + bex) + b.center.x,
                                (py + bey) + b.center.y,
                                (pz + bez) + b.center.z,
                            ));
                        }
                        sg += 2;
                    }
                }
            }
            sb += 2;
        }
        sa += 2;
    }
}

/// The side-surface test of one cyl–cyl pass (P against Q; the binary's 0x3516C034…).
/// Returns whether it reports contact. Mirrors the original exactly, including that the
/// "discriminant" `v91` is used without a square root in the root sign tests.
fn cyl_cyl_side(pc: &CylinderPrim, qc: &CylinderPrim, dvec: Vec3, dot: f32, k: f32) -> bool {
    let (pa, qa) = (pc.axis, qc.axis);
    let (dx, dy, dz) = (dvec.x, dvec.y, dvec.z);
    let hp = (dy * pa.y + dx * pa.x) + dz * pa.z;
    let hq = (qa.y * dy + qa.x * dx) + qa.z * dz;
    let v89 = hp - hq * dot;
    let v90 = (dy * qa.y + dx * qa.x) + dz * qa.z;
    let rq2 = qc.r * qc.r;
    let v91 = v89 * v89 - ((((dx * dx + dy * dy) + dz * dz) - v90 * v90) - rq2) * k;
    if !(v91 > 0.0) {
        return false;
    }
    let v92 = pc.hh * k;
    let adot = dot.abs();
    let qhk = qc.hh * k;
    // root test for one sign of the "root" (±v91)
    let root = |r: f32| -> bool {
        let s1 = sgnnz(r - v89) as f32;
        let a1 = s1 * v89 + v92;
        let chk1 = (r * 1.0) * s1 - a1.abs() * a1;
        if isneg(chk1) == 0 {
            return false;
        }
        let m = -(((qa.y * dy + qa.x * dx) + qa.z * dz) * k);
        let s2 = sgnnz((r - v89) * dot + m) as f32;
        let v95 = (v89 * dot - m) * s2 + qhk;
        let chk2 = (((adot * dot) * r) * s2) - v95.abs() * v95;
        isneg(chk2) != 0
    };
    root(v91) || root(-v91)
}

/// cyl–cyl 0x3516B7F0.
///
/// Resets the border count (the normal and the features are **never written**).
/// 1. Non-parallel axes (`|aA × aB| > 0.003`): the same three-sign-bit overlap test of the
///    cylindrical parts as cyl–cap (sets the flag). Parallel axes: `n̂` = [`perp_axis`]`(aA)`.
/// 2. Two passes, (P, Q) = (A, B) with `outer = −1`, then (B, A) with `+1`:
///    * cap-end vs side checks whose sign test ANDs a float's bits with a 0/1 flag (bit 31 of
///      the result is always clear) — dead in the binary, so not evaluated here;
///    * when `1 − dot² > 1e-4`, the side-surface test [`cyl_cyl_side`] (flag only);
///    * both caps of P against Q ([`cap_circle`], points within Q's radius emitted).
/// 3. Non-parallel axes only: the cap circles against each other ([`cyl_cyl_circles`]).
///
/// `pt[0] = pt[1] =` the first border point and the result is 1 when any point was emitted,
/// else the accumulated flag.
pub fn cyl_cyl(a: &CylinderPrim, b: &CylinderPrim, out: &mut PrimInters) -> bool {
    out.border.clear();
    let (aa, ab) = (a.axis, b.axis);
    let mut res = false;
    let dot = (ab.y * aa.y + ab.x * aa.x) + ab.z * aa.z;
    let cx_ = ab.z * aa.y - ab.y * aa.z;
    let cz_ = aa.x * ab.y - ab.x * aa.y;
    let cy_ = ab.x * aa.z - aa.x * ab.z;
    let len = ((cx_ * cx_ + cy_ * cy_) + cz_ * cz_).sqrt();
    let n = if len > AXIS_EPS {
        let dx = b.center.x - a.center.x;
        let dy = b.center.y - a.center.y;
        let dz = b.center.z - a.center.z;
        let inv = 1.0 / len;
        let (nx, ny, nz) = (cx_ * inv, cy_ * inv, cz_ * inv);
        let chk_r = (((dx * nx + dy * ny) + dz * nz).abs() - a.r) - b.r;
        let wx = dy * ab.z - dz * ab.y;
        let wy = dz * ab.x - dx * ab.z;
        let wz = dx * ab.y - dy * ab.x;
        let chk_a = ((wx * nx + wy * ny) + wz * nz).abs() - len * a.hh;
        let ux = dy * aa.z - dz * aa.y;
        let uy = dz * aa.x - dx * aa.z;
        let uz = dx * aa.y - dy * aa.x;
        let chk_b = ((ux * nx + uy * ny) + uz * nz).abs() - len * b.hh;
        if (chk_b.to_bits() & chk_a.to_bits() & chk_r.to_bits()) >> 31 != 0 {
            res = true;
        }
        Vec3::new(nx, ny, nz)
    } else {
        perp_axis(aa)
    };
    let k = 1.0 - dot * dot;
    let pairs = [(a, b, -1.0f32), (b, a, 1.0f32)];
    for (p, q, outer) in pairs {
        let qa = q.axis;
        let e = Vec3::new(
            n.z * qa.y - n.y * qa.z,
            n.x * qa.z - n.z * qa.x,
            n.y * qa.x - n.x * qa.y,
        );
        let dvec = Vec3::new(
            p.center.x - q.center.x,
            p.center.y - q.center.y,
            p.center.z - q.center.z,
        );
        if k > f32::from_bits(0x38D1_B717) && cyl_cyl_side(p, q, dvec, dot, k) {
            res = true;
        }
        let geo = CircleGeo {
            n,
            e,
            dot,
            len,
            outer,
        };
        let mut s: i32 = -1;
        while s <= 1 {
            let es = (s as f32) * p.hh;
            let v = Vec3::new(
                dvec.x + p.axis.x * es,
                p.axis.y * es + dvec.y,
                p.axis.z * es + dvec.z,
            );
            res |= cap_circle(p, q, true, &geo, v, out);
            s += 2;
        }
    }
    if len > AXIS_EPS {
        cyl_cyl_circles(a, b, dot, len, out);
    }
    if out.border.is_empty() {
        return res;
    }
    set_points(out, out.border[0]);
    true
}

// ------------------------------------------------------------------------------------ cyl–cap

/// cyl–cap 0x3516D2E0.
///
/// Starts by zeroing `n` and the border count. `v3` (the result when no point is emitted)
/// becomes true on any of the overlap tests below.
/// 1. Axes not parallel (`|aA × aB| > 0.003`): `n̂ = normalize(aA × aB)`; the cylindrical parts
///    overlap when `|D| − rA − rB`, `|(d × aB)·n̂| − |n|·hhA` and `|(d × aA)·n̂| − |n|·hhB` are
///    all negative (sign bits ANDed). Parallel axes: `n̂` = a vector perpendicular to `aA`
///    ([`perp_axis`]).
/// 2. Each capsule end point `P` against the cylinder: closest-point offset `f` (axial part
///    beyond the caps, radial part outside the radius — the radial part has the **opposite
///    orientation** of the axial one, as in cyl–sph); `|f|² < rB²` emits the border point
///    `P + f` with `n = −f`.
/// 3. Each cylinder cap circle against the capsule's segment, with `e = aB × n̂`,
///    `p = −(PA − cB)·n̂`, `q = −(PA − cB)·e`:
///    * axes nearly perpendicular (`|aA·aB| < 0.003`): an analytic disc test, contact flag only;
///    * else the discriminant of the cubic for the closest circle point decides (`> 0`); the
///      circle angle is found by bisection over the 1025-entry sine table, and when the point's
///      height along `aB` is within `hhB`, a second bisection over the full circle
///      (two half-circles, `|lo−hi| > 2`) finds the points at distance `rB` from the segment,
///      each emitted when its height is within `hhB`.
///
/// `pt[0] = pt[1] =` the first border point. **No features are written** and step 3 never
/// writes `n` (the last normal comes from step 2, or stays zero).
pub fn cyl_cap(cyl: &CylinderPrim, cap: &CapsulePrim, out: &mut PrimInters) -> bool {
    let (ca, aa, r1, hh1) = (cyl.center, cyl.axis, cyl.r, cyl.hh);
    let (cb, ab, r2, hh2) = (cap.center, cap.axis, cap.r, cap.hh);
    let mut res = false;
    out.border.clear();
    out.n = Vec3::ZERO;
    let dot = (ab.y * aa.y + aa.x * ab.x) + ab.z * aa.z;
    let cy_ = ab.x * aa.z - ab.z * aa.x;
    let cx_ = ab.z * aa.y - ab.y * aa.z;
    let cz_ = ab.y * aa.x - ab.x * aa.y;
    let len = ((cy_ * cy_ + cx_ * cx_) + cz_ * cz_).sqrt();
    let n = if len > AXIS_EPS {
        let dx = cb.x - ca.x;
        let dy = cb.y - ca.y;
        let dz = cb.z - ca.z;
        let inv = 1.0 / len;
        let (ny, nz, nx) = (cy_ * inv, cz_ * inv, cx_ * inv);
        let chk_r = (((dy * ny + dx * nx) + dz * nz).abs() - r1) - r2;
        let wx = dy * ab.z - dz * ab.y;
        let wy = ab.x * dz - dx * ab.z;
        let wz = dx * ab.y - ab.x * dy;
        let chk_a = ((wy * ny + wx * nx) + wz * nz).abs() - len * hh1;
        let ux = dy * aa.z - dz * aa.y;
        let uy = aa.x * dz - dx * aa.z;
        let uz = dx * aa.y - aa.x * dy;
        let chk_b = ((uy * ny + ux * nx) + uz * nz).abs() - len * hh2;
        if (chk_b.to_bits() & chk_a.to_bits() & chk_r.to_bits()) >> 31 != 0 {
            res = true;
        }
        Vec3::new(nx, ny, nz)
    } else {
        perp_axis(aa)
    };
    let e1 = n.z * ab.y - n.y * ab.z;
    let e2 = n.x * ab.z - ab.x * n.z;
    let e3 = ab.x * n.y - n.x * ab.y;
    let r1sq = r1 * r1;
    let r2sq = r2 * r2;
    // 2. capsule end points against the cylinder.
    let mut s: i32 = -1;
    while s <= 1 {
        let sf = s as f32;
        let e = sf * hh2;
        let mut vy = (ab.y * e + cb.y) - ca.y;
        let mut vx = (ab.x * e + cb.x) - ca.x;
        let mut vz = (ab.z * e + cb.z) - ca.z;
        let h = (aa.x * vx + vy * aa.y) + vz * aa.z;
        let k = hh1 - h.abs();
        let f = (flag_f32(isneg(k)) * k) * (sgnnz(h) as f32);
        let mut f6 = aa.x * f;
        let mut f7 = aa.y * f;
        let mut f8 = aa.z * f;
        let rx = vy * aa.z - vz * aa.y;
        let ry = aa.x * vz - vx * aa.z;
        let rz = vx * aa.y - aa.x * vy;
        let k2 = r1sq - ((rx * rx + ry * ry) + rz * rz);
        if isneg(k2) != 0 {
            let h2 = (aa.x * vx + vy * aa.y) + vz * aa.z;
            vx -= aa.x * h2;
            vy -= aa.y * h2;
            vz -= aa.z * h2;
            let nn = norm_or_z(Vec3::new(vx, vy, vz));
            vx -= nn.x * r1;
            vy -= nn.y * r1;
            vz -= nn.z * r1;
            f6 += vx;
            f7 += vy;
            f8 += vz;
        }
        let dist2 = (f6 * f6 + f7 * f7) + f8 * f8;
        if !(dist2 >= r2sq) {
            res = true;
            let e2_ = sf * hh2;
            out.border.push(Vec3::new(
                (ab.x * e2_ + cb.x) + f6,
                (ab.y * e2_ + cb.y) + f7,
                (ab.z * e2_ + cb.z) + f8,
            ));
            out.n = Vec3::new(-f6, -f7, -f8);
        }
        s += 2;
    }
    // 3. cylinder cap circles against the capsule segment.
    let geo = CircleGeo {
        n,
        e: Vec3::new(e1, e2, e3),
        dot,
        len,
        outer: -1.0,
    };
    let mut s: i32 = -1;
    while s <= 1 {
        let e = (s as f32) * hh1;
        let v = Vec3::new(
            (aa.x * e + ca.x) - cb.x,
            (aa.y * e + ca.y) - cb.y,
            (aa.z * e + ca.z) - cb.z,
        );
        res |= cap_circle(cyl, cap, false, &geo, v, out);
        s += 2;
    }
    if out.border.is_empty() {
        return res;
    }
    set_points(out, out.border[0]);
    true
}

/// cap–cyl 0x3516E320: [`cyl_cap`]`(b, a)` + [`swap_features`] (which here only permutes
/// whatever feature bytes the output already held: cyl–cap writes none).
pub fn cap_cyl(cap: &CapsulePrim, cyl: &CylinderPrim, out: &mut PrimInters) -> bool {
    let r = cyl_cap(cyl, cap, out);
    swap_features(out);
    r
}

// ------------------------------------------------------------------------------------ overlap

/// OVERLAP sph–sph 0x35196030: sign bit of `((dx² + dy²) + dz²) − (r1 + r2)²` (single).
#[must_use]
pub fn overlap_sph_sph(a: &SpherePrim, b: &SpherePrim) -> bool {
    let dx = a.center.x - b.center.x;
    let dy = a.center.y - b.center.y;
    let dz = a.center.z - b.center.z;
    let rs = a.r + b.r;
    isneg(((dx * dx + dy * dy) + dz * dz) - rs * rs) != 0
}

/// The grid-index clamp of 0x351975E0: `bits` is `(x ± 0.5) + 12582912` as raw bits (the
/// magic-number round), the result `min(max(i, 0), n)` computed branch-free as in the binary.
#[inline]
fn hf_clamp_index(bits: u32, n: i32) -> i32 {
    let neg = 0x4B40_0000i32.wrapping_sub(bits as i32);
    let m = (neg & (neg >> 31)).wrapping_neg();
    let e = n.wrapping_sub(m);
    (e & (e >> 31)).wrapping_add(m)
}

/// OVERLAP hf–sph 0x351975E0.
///
/// The sphere centre goes into the heightfield frame (`Basis·(c − origin)`), the covered cell
/// range is `[floor((x−r)·stepr), round((x+r)·stepr + 0.5))` clamped to `[0, n]` (magic-number
/// rounding, exclusive upper bound), and the result is the OR over those **grid vertices** of
/// `z − r − h(ix, iy)·heightscale < 0` (sign bit). Heights are sampled at vertices only (no
/// interpolation), as in the binary.
///
/// Deviation: without a height callback the binary would call a null pointer; the port answers
/// "maybe overlaps" (`true`).
#[must_use]
pub fn overlap_hf_sph(hf: &HeightfieldPrim, s: &SpherePrim) -> bool {
    let Some(get_height) = hf.get_height else {
        return true;
    };
    let m = &hf.basis.m;
    let dx = s.center.x - hf.origin.x;
    let dy = s.center.y - hf.origin.y;
    let dz = s.center.z - hf.origin.z;
    let r = s.r;
    let px = (dy * m[0][1] + dx * m[0][0]) + dz * m[0][2];
    let py = (dy * m[1][1] + dx * m[1][0]) + dz * m[1][2];
    let pz = (dy * m[2][1] + dx * m[2][0]) + dz * m[2][2];
    const MAGIC: f32 = 12_582_912.0;
    let ix0 = hf_clamp_index(
        (((px - r) * hf.stepr[0] - 0.5) + MAGIC).to_bits(),
        hf.size[0],
    );
    let iy0 = hf_clamp_index(
        (((py - r) * hf.stepr[1] - 0.5) + MAGIC).to_bits(),
        hf.size[1],
    );
    let ix1 = hf_clamp_index(
        (((r + px) * hf.stepr[0] + 0.5) + MAGIC).to_bits(),
        hf.size[0],
    );
    let iy1 = hf_clamp_index(
        (((r + py) * hf.stepr[1] + 0.5) + MAGIC).to_bits(),
        hf.size[1],
    );
    let mut res = 0u32;
    for ix in ix0..ix1 {
        for iy in iy0..iy1 {
            let h = get_height(ix, iy);
            res |= isneg((pz - s.r) - h * hf.height_scale);
        }
    }
    res != 0
}

/// OVERLAP sph–hf 0x35197860: [`overlap_hf_sph`]`(b, a)`.
#[must_use]
pub fn overlap_sph_hf(s: &SpherePrim, hf: &HeightfieldPrim) -> bool {
    overlap_hf_sph(hf, s)
}

// ------------------------------------------------------------------------------------ tables

macro_rules! cell {
    ($name:ident, $f:path, $va:ident, $vb:ident) => {
        fn $name(a: &Primitive, b: &Primitive, out: &mut PrimInters) -> bool {
            match (a, b) {
                (Primitive::$va(a), Primitive::$vb(b)) => $f(a, b, out),
                _ => false,
            }
        }
    };
}
macro_rules! ocell {
    ($name:ident, $f:path, $va:ident, $vb:ident) => {
        fn $name(a: &Primitive, b: &Primitive) -> bool {
            match (a, b) {
                (Primitive::$va(a), Primitive::$vb(b)) => $f(a, b),
                _ => true,
            }
        }
    };
}

cell!(t_sph_sph, sph_sph, Sphere, Sphere);
cell!(t_sph_ray, sph_ray, Sphere, Ray);
cell!(t_ray_sph, ray_sph, Ray, Sphere);
cell!(t_cyl_sph, cyl_sph, Cylinder, Sphere);
cell!(t_sph_cyl, sph_cyl, Sphere, Cylinder);
cell!(t_cyl_ray, cyl_ray, Cylinder, Ray);
cell!(t_ray_cyl, ray_cyl, Ray, Cylinder);
cell!(t_cyl_cyl, cyl_cyl, Cylinder, Cylinder);
cell!(t_cyl_cap, cyl_cap, Cylinder, Capsule);
cell!(t_cap_cyl, cap_cyl, Capsule, Cylinder);
cell!(t_cap_sph, cap_sph, Capsule, Sphere);
cell!(t_sph_cap, sph_cap, Sphere, Capsule);
cell!(t_cap_ray, cap_ray, Capsule, Ray);
cell!(t_ray_cap, ray_cap, Ray, Capsule);
cell!(t_cap_cap, cap_cap, Capsule, Capsule);
ocell!(o_sph_sph, overlap_sph_sph, Sphere, Sphere);
ocell!(o_hf_sph, overlap_hf_sph, Heightfield, Sphere);
ocell!(o_sph_hf, overlap_sph_hf, Sphere, Heightfield);

/// Write this group's INTERSECT / OVERLAP cells.
pub fn fill(t: &mut PairTables) {
    use GeomType::{Capsule as Cap, Cylinder as Cyl, Heightfield as Hf, Ray, Sphere as Sph};
    let cells: [(GeomType, GeomType, IntersectFn); 15] = [
        (Sph, Sph, t_sph_sph),
        (Sph, Ray, t_sph_ray),
        (Ray, Sph, t_ray_sph),
        (Cyl, Sph, t_cyl_sph),
        (Sph, Cyl, t_sph_cyl),
        (Cyl, Ray, t_cyl_ray),
        (Ray, Cyl, t_ray_cyl),
        (Cyl, Cyl, t_cyl_cyl),
        (Cyl, Cap, t_cyl_cap),
        (Cap, Cyl, t_cap_cyl),
        (Cap, Sph, t_cap_sph),
        (Sph, Cap, t_sph_cap),
        (Cap, Ray, t_cap_ray),
        (Ray, Cap, t_ray_cap),
        (Cap, Cap, t_cap_cap),
    ];
    for (a, b, f) in cells {
        t.intersect[a.id() as usize][b.id() as usize] = f;
    }
    let ocells: [(GeomType, GeomType, OverlapFn); 3] = [
        (Sph, Sph, o_sph_sph),
        (Hf, Sph, o_hf_sph),
        (Sph, Hf, o_sph_hf),
    ];
    for (a, b, f) in ocells {
        t.overlap[a.id() as usize][b.id() as usize] = f;
    }
}
