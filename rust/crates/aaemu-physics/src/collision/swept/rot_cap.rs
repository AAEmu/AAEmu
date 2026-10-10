//! P2b work group `rot_cap`: rotational (mode 1) unprojection of a ray against a capsule.
//!
//! * **ray–capsule rotational** — x64 `sub_351A4630` (13 487 B), x86 `0x351B5480`;
//!   cell `[1][ray][cap]`, [`unproject_ray_cap_rot`].
//! * **capsule–ray rotational** — the 447-byte swap wrapper x64 `sub_351A7AF0`, x86 `0x351BCF20`;
//!   cell `[1][cap][ray]`. Its disassembly is the shared 447-byte rotational wrapper instruction
//!   for instruction (only the callee differs), so the cell is [`common::swap_rot`] around
//!   [`unproject_ray_cap_rot`].
//!
//! # Algorithm of `sub_351A4630`
//!
//! The ray (segment `origin → origin + dir`) is rotated about the line (`params.center`,
//! `params.dir` = unit axis `a`) by the angle φ; the routine looks for the **largest** `sin φ` in
//! (0, 1) at which the rotated segment touches the capsule surface (the capsule centre `c`, axis
//! `k`, radius `R` and half-height `hh` are taken relative to the rotation centre). `params.tmax`
//! (sin of the maximum angle), `tmin`, `max_cos`, the feature ids and the contact area are never
//! read. Almost everything is evaluated in double precision.
//!
//! Every point `p` (relative to the centre) is decomposed into an axial part `h = (p·a)a`, a
//! radial part `r = p − h` and `w = a × r`; it moves on `h + r cos φ + w sin φ`. The best root so
//! far is kept as a fraction `num / den` (start `0 / 1`), and every candidate updates it
//! branch-free: `sel` = sign bit of `(float)(num·den' − den·num')` (i.e. the candidate is larger)
//! AND-ed with the sign bits of the candidate's validity tests (each `(float)` of a double
//! expression that is negative when the test passes); `den = keep·den + sel·den'`,
//! `num = keep·num + sel·num'` with `keep = 1 − sel` as doubles (so `0·∞` gives NaN as in the
//! original), and the feature code is replaced when `sel` is set. The feature code starts at
//! −1 (no contact, the function returns 0).
//!
//! 1. For both ray ends `e` (the end point `e = 1` first, then the origin `e = 0`; the origin is
//!    skipped when it is within `params.min_pt_dist` of the rotation centre, `|o − centre|² <
//!    mpd²` in float, NaN counting as "within"):
//!    * against the two cap spheres `c ± k·hh` (`j = −1, +1`): `C + A cos φ + B sin φ = 0` is
//!      turned into the quadratic `(A² + B²) s² + 2BC s + C² − A² = 0` in `s = sin φ` and both
//!      roots `(−BC ∓ √disc) / (A² + B²)` (the second one computed as `first + 2√disc`) are kept
//!      as fractions when `0 < s < 1` and `cos φ > 0`. Feature `e | (j + 1)` (0..3).
//!    * against the cylinder side: `|((p(φ) − c) × k)|² = R²` with `cos² = 1 − sin²` gives the
//!      quartic `A(s)² − 4·B(s)²·(1 − s²)` (built with the engine's inline polynomial template
//!      arithmetic, including its `0.0 +` accumulators and the `1 − s²` factor stored as
//!      `{1, −0, −1}`), which is passed to the polynomial routines `sub_3514A2E0` (any root in
//!      [0, 1]) and `sub_35149170` (find up to 20 roots in [0, 1]). Each root `s` (last first) is
//!      accepted when the point is within the cylinder length (`|(p − c)·k| < hh`) and its
//!      distance to the axis is within 1 % of `R` (`|0.9801R² + 1.0201R² − 2d²| <
//!      |0.9801R² − 1.0201R²|`, which rejects the roots introduced by squaring). Fraction `s / 1`,
//!      feature `e | 0x10`.
//! 2. The rotating ray line against the capsule axis line (in Plücker form, with the direction
//!    and the moment `o × dir` decomposed like the points): the line distance equals `R` gives
//!    another quartic `A(s)² − B(s)²(1 − s²)`. Roots are accepted when the closest point on the
//!    capsule axis is within `hh`, the closest point on the ray is within the segment and the
//!    distance is within 1 % of `R`. Fraction `s / 1`, feature 0x20.
//! 3. The rotating ray line against the cap sphere centres `c ± k·hh`: distance from the line =
//!    `R` (quartic built like 1., with `R²·|dir|²`). Accepted when the closest point is within the
//!    segment and the distance is within 1 %. Feature `((j + 1) | 0x80) >> 1` (0x40 / 0x41).
//!
//! Result (only on success, the function returns 1): `t = sin φ`, `cos_t = √(1 − sin²φ)`; the
//! contact point is computed on the rotated ray (the closest point to the hit feature), `n`
//! points from the contact towards the capsule:
//!
//! | feature | `t` | `pt` | `n` | `i_feature` |
//! |---|---|---|---|---|
//! | 0..3 (end vs cap sphere) | `num/den` (0 if `den` is 0 or NaN) | rotated end | normalised (in float) `cap centre_j − pt` | `0x80 \| e`, `0x41 + (j+1)/2` |
//! | 0x10 (end vs side) | `num` | rotated end | `c − pt` minus its axial part, [`normalize_in_place`] | `0x80 \| e`, 0x40 |
//! | 0x20 (ray vs side) | `num` | closest point of the two lines | `±(D × k)` normalised, sign so that it points to the axis | 0x20, 0x40 |
//! | 0x40/0x41 (ray vs cap sphere) | `num` | closest point of the line to the sphere centre | normalised (in double) | 0x20, 0x20 / 0x21 |
//!
//! (For 0x10 / 0x20 / 0x40 the fraction's denominator is 1 by construction, so the original uses
//! `num` directly.)
//!
//! Static state: none. Reads beyond the documented params: none (`min_pt_dist` at +40 is read,
//! `tmax` is not).

use super::common::{as_cap, as_ray, swap_rot};
use super::{UnprojTables, MODE_ROTATIONAL};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{CapsulePrim, Primitive, RayPrim};
use crate::ids::GeomType;
use crate::math::Vec3;

/// The two polynomial routines the cell calls on its quartics. A polynomial is the engine's
/// 6-double block `{scale, c0, c1, c2, c3, c4}` (`scale · Σ cᵢ xⁱ`, always `scale = 1` here).
pub trait QuarticSolver {
    /// `sub_3514A2E0(p, 0.0, 1.0)`: non-zero when the polynomial has a root in [0, 1].
    fn has_root(&mut self, p: &[f64; 6]) -> i32;
    /// `sub_35149170(p, 0.0, 1.0, roots, 20, 4, 0)`: the roots in [0, 1] (at most 20) written to
    /// `roots`, returns their count.
    fn roots(&mut self, p: &[f64; 6], roots: &mut [f64; 20]) -> i32;
}

/// The engine's solvers (the `poly` group's ports of `sub_3514A2E0` / `sub_35149170`).
pub struct EngineSolver;

impl QuarticSolver for EngineSolver {
    fn has_root(&mut self, _p: &[f64; 6]) -> i32 {
        unimplemented!("waiting for the poly group's solver")
    }
    fn roots(&mut self, _p: &[f64; 6], _roots: &mut [f64; 20]) -> i32 {
        unimplemented!("waiting for the poly group's solver")
    }
}

/// Sign bit of a float (`shr 31` on its bits) as 0/1.
#[inline]
fn sgn(x: f32) -> u32 {
    x.to_bits() >> 31
}

/// The branch-free "take the candidate" update shared by all candidate loops: returns
/// `(keep, sel)` as doubles from the 0/1 selector.
#[inline]
fn keep_sel(sel: u32) -> (f64, f64) {
    (f64::from((sel ^ 1) as i32), f64::from(sel as i32))
}

/// `feat = sel ? new : feat` the way the binary computes it.
#[inline]
fn pick_feat(sel: u32, new: i32, feat: i32) -> i32 {
    (new & (sel as i32).wrapping_neg()) | ((sel as i32 - 1) & feat)
}

/// sub_35021020 with its exact NaN behaviour: the binary tests `|v|² ≤ 1e-20` with
/// `comiss`/`jbe`, so a NaN length also yields (0, 0, 1). (`common::normalize_in_place` takes
/// the other branch for NaN; this private copy keeps the cell exact.)
fn normalize_in_place(v: &mut Vec3) {
    let l2 = (v.x * v.x + v.y * v.y) + v.z * v.z;
    if super::common::le_or_nan(l2, f32::from_bits(0x1E3C_E508)) {
        *v = Vec3::new(0.0, 0.0, 1.0);
    } else {
        let inv = 1.0 / l2.sqrt();
        *v = Vec3::new(v.x * inv, v.y * inv, v.z * inv);
    }
}

/// A ray end relative to the rotation centre, decomposed as `h + r cos φ + w sin φ`.
#[derive(Clone, Copy, Default)]
struct Circ {
    h: [f64; 3],
    r: [f64; 3],
    w: [f64; 3],
}

impl Circ {
    fn new(p: [f64; 3], a: [f64; 3]) -> Self {
        let dot = (p[0] * a[0] + p[1] * a[1]) + p[2] * a[2];
        let h = [dot * a[0], dot * a[1], dot * a[2]];
        let r = [p[0] - h[0], p[1] - h[1], p[2] - h[2]];
        let w = [
            r[2] * a[1] - r[1] * a[2],
            r[0] * a[2] - a[0] * r[2],
            a[0] * r[1] - r[0] * a[1],
        ];
        Circ { h, r, w }
    }

    /// The rotated point `(r cos + h) + w sin` (x: `(h + r cos) + w sin`, the same value).
    fn at(&self, c: f64, s: f64) -> [f64; 3] {
        [
            (self.r[0] * c + self.h[0]) + self.w[0] * s,
            (self.r[1] * c + self.h[1]) + self.w[1] * s,
            (self.r[2] * c + self.h[2]) + self.w[2] * s,
        ]
    }
}

/// The inline polynomial template arithmetic shared by phases 1 and 3:
/// `A(s)² − 4·B(s)²·(1 − s²)` with `A = a0 + a1 s + a2 s²`, `B = b0 + b1 s`, the factor
/// `1 − s²` stored as `{q0, q1, q2} = {1, −0, −1}` and every product coefficient accumulated
/// from `0.0`. (Multiplications by the template's `1.0` scale factors are exact and omitted.)
fn quartic_ab(a0: f64, a1: f64, a2: f64, b0: f64, b1: f64) -> [f64; 6] {
    let (q0, q1, q2) = (-(0.0 - 1.0), -0.0f64, -1.0f64);
    let bb0 = 0.0 + b0 * b0;
    let b10 = b1 * b0;
    let bb2 = 0.0 + b1 * b1;
    let bb1 = (0.0 + b10) + b10;
    let k0 = (0.0 + bb0 * q0) * 4.0;
    let k1 = ((0.0 + bb1 * q0) + bb0 * q1) * 4.0;
    let k2 = (((0.0 + bb2 * q0) + bb1 * q1) + bb0 * q2) * 4.0;
    let k3 = ((0.0 + bb2 * q1) + bb1 * q2) * 4.0;
    let k4 = (0.0 + bb2 * q2) * 4.0;
    let a10 = a1 * a0;
    let a20 = a2 * a0;
    let a12 = a1 * a2;
    [
        1.0,
        (0.0 + a0 * a0) - k0,
        ((0.0 + a10) + a10) - k1,
        (((0.0 + a20) + a1 * a1) + a20) - k2,
        ((0.0 + a12) + a12) - k3,
        (0.0 + a2 * a2) - k4,
    ]
}

/// Run the two solver calls like the binary: `has_root`, then `roots`; returns the roots found
/// (iterated by the caller from the last to the first).
fn solve(solver: &mut dyn QuarticSolver, p: &[f64; 6], roots: &mut [f64; 20]) -> i32 {
    if solver.has_root(p) == 0 {
        return 0;
    }
    solver.roots(p, roots)
}

/// sub_351A4630 (x86 0x351B5480): rotational unprojection ray–capsule (see the module docs).
pub fn unproject_ray_cap_rot(
    p: &mut UnprojParams,
    ray: &RayPrim,
    _fa: i32,
    cap: &CapsulePrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    unproject_ray_cap_rot_with(p, ray, cap, out, &mut EngineSolver)
}

/// [`unproject_ray_cap_rot`] with the polynomial solvers supplied by the caller (tests replay the
/// DLL's roots through this).
#[allow(clippy::too_many_lines, clippy::similar_names, clippy::many_single_char_names)]
pub fn unproject_ray_cap_rot_with(
    p: &mut UnprojParams,
    ray: &RayPrim,
    cap: &CapsulePrim,
    out: &mut UnprojResult,
    solver: &mut dyn QuarticSolver,
) -> i32 {
    let cc = p.center;
    let hh = f64::from(cap.hh);
    let a = [f64::from(p.dir.x), f64::from(p.dir.y), f64::from(p.dir.z)];
    let r2 = f64::from(cap.r * cap.r);
    let k = [
        f64::from(cap.axis.x),
        f64::from(cap.axis.y),
        f64::from(cap.axis.z),
    ];
    // capsule centre relative to the rotation centre
    let c = [
        f64::from(cap.center.x - cc.x),
        f64::from(cap.center.y - cc.y),
        f64::from(cap.center.z - cc.z),
    ];
    let o = [
        f64::from(ray.origin.x - cc.x),
        f64::from(ray.origin.y - cc.y),
        f64::from(ray.origin.z - cc.z),
    ];
    let e1 = [
        f64::from(ray.dir.x) + o[0],
        f64::from(ray.dir.y) + o[1],
        f64::from(ray.dir.z) + o[2],
    ];
    let ends = [Circ::new(o, a), Circ::new(e1, a)];

    let mut feat: i32 = -1;
    let mut bnum = 0.0f64;
    let mut bden = 1.0f64;
    let mut roots = [0.0f64; 20];

    // ---- phase 1: the ray ends (end point first)
    let mut ep: i32 = 1;
    loop {
        let e = &ends[ep as usize];
        let (h, r, w) = (e.h, e.r, e.w);
        let rr = (r[0] * r[0] + r[1] * r[1]) + r[2] * r[2];
        // 1a: against the cap spheres
        let mut j: i32 = -1;
        while j <= 1 {
            let jh = f64::from(j) * hh;
            let cjx = c[0] + k[0] * jh;
            let cjy = k[1] * jh + c[1];
            let cjz = k[2] * jh + c[2];
            let dx = h[0] - cjx;
            let dy = h[1] - cjy;
            let dz = h[2] - cjz;
            let b = ((w[0] * dx + dy * w[1]) + dz * w[2]) * 2.0;
            let aa = ((dy * r[1] + r[0] * dx) + dz * r[2]) * 2.0;
            let aa2 = aa * aa;
            let den = b * b + aa2;
            let c0 = (((dy * dy + dx * dx) + dz * dz) + rr) - r2;
            let hb = c0 * b;
            let disc = hb * hb - (c0 * c0 - aa2) * den;
            // comisd / jb: NaN skips
            if disc >= 0.0 {
                let sq = disc.sqrt();
                let dc0 = den * c0;
                let mut num = -hb - sq;
                for pass in 0..2 {
                    if pass == 1 {
                        num += sq * 2.0;
                    }
                    let in01 = ((num * 2.0 - den).abs() - den) as f32;
                    let cos_pos = ((num * b + dc0) * aa) as f32;
                    if ((in01.to_bits() & cos_pos.to_bits()) as i32) < 0 {
                        let better = (bnum * den - bden * num) as f32;
                        let sel = sgn(better);
                        feat = pick_feat(sel, (j + 1) | ep, feat);
                        let (keep, selv) = keep_sel(sel);
                        bden = keep * bden + selv * den;
                        bnum = keep * bnum + selv * num;
                    }
                }
            }
            j += 2;
        }

        // 1b: against the cylinder side
        let dx = h[0] - c[0];
        let dy = h[1] - c[1];
        let dz = h[2] - c[2];
        // u = (h − c) × k, v = r × k, vw = w × k
        let u = [
            dy * k[2] - dz * k[1],
            k[0] * dz - k[2] * dx,
            k[1] * dx - k[0] * dy,
        ];
        let v = [
            k[2] * r[1] - k[1] * r[2],
            k[0] * r[2] - k[2] * r[0],
            k[1] * r[0] - k[0] * r[1],
        ];
        let vw = [
            k[2] * w[1] - k[1] * w[2],
            k[0] * w[2] - k[2] * w[0],
            k[1] * w[0] - k[0] * w[1],
        ];
        let b1 = (vw[1] * v[1] + vw[0] * v[0]) + vw[2] * v[2];
        let b0 = 0.0 + ((v[1] * u[1] + v[0] * u[0]) + v[2] * u[2]);
        let vv = (v[1] * v[1] + v[0] * v[0]) + v[2] * v[2];
        let a1 = ((vw[1] * u[1] + vw[0] * u[0]) + vw[2] * u[2]) * 2.0 + 0.0;
        let a2 = ((vw[1] * vw[1] + vw[0] * vw[0]) + vw[2] * vw[2]) - vv;
        let a0 = (((0.0 + 0.0) + ((u[1] * u[1] + u[0] * u[0]) + u[2] * u[2])) + vv) - r2;
        let poly = quartic_ab(a0, a1, a2, b0, b1);
        let n = solve(solver, &poly, &mut roots);
        let r2sum = r2 * 0.9801 + r2 * 1.0201;
        let r2dif = (r2 * 0.9801 - r2 * 1.0201).abs();
        let mut i = n - 1;
        while i >= 0 {
            let s = roots[i as usize];
            let co = (1.0 - s * s).sqrt();
            let px = ((r[0] * co + h[0]) + w[0] * s) - c[0];
            let py = ((r[1] * co + h[1]) + w[1] * s) - c[1];
            let pz = ((r[2] * co + h[2]) + w[2] * s) - c[2];
            let axial = (((py * k[1] + k[0] * px) + pz * k[2]).abs() - hh) as f32;
            let x0 = py * k[2] - pz * k[1];
            let x1 = k[0] * pz - px * k[2];
            let x2 = px * k[1] - k[0] * py;
            let dist = ((r2sum - ((x1 * x1 + x0 * x0) + x2 * x2) * 2.0).abs() - r2dif) as f32;
            let better = (bnum - bden * s) as f32;
            let sel = sgn(better) & sgn(dist) & sgn(axial);
            feat = pick_feat(sel, ep | 0x10, feat);
            let (keep, selv) = keep_sel(sel);
            bden = keep * bden + selv;
            bnum = keep * bnum + selv * s;
            i -= 1;
        }

        // skip the origin when it lies on the rotation centre (comiss / jb: NaN breaks too)
        let ox = ray.origin.x - cc.x;
        let oy = ray.origin.y - cc.y;
        let oz = ray.origin.z - cc.z;
        let d2 = (oy * oy + ox * ox) + oz * oz;
        if super::common::lt_or_nan(d2, p.min_pt_dist * p.min_pt_dist) {
            break;
        }
        ep -= 1;
        if ep < 0 {
            break;
        }
    }

    // ---- phase 2 set-up: the ray line in Plücker form, decomposed about the axis
    let (ax, ay, az) = (a[0], a[1], a[2]);
    let (kx, ky, kz) = (k[0], k[1], k[2]);
    let (cx, cy, cz) = (c[0], c[1], c[2]);
    let ddot = f64::from((p.dir.y * ray.dir.y + p.dir.x * ray.dir.x) + p.dir.z * ray.dir.z);
    // direction: axial, radial (rounded to float), a × radial
    let dax = [ax * ddot, ddot * ay, ddot * az];
    let dr = [
        f64::from((f64::from(ray.dir.x) - dax[0]) as f32),
        f64::from((f64::from(ray.dir.y) - dax[1]) as f32),
        f64::from((f64::from(ray.dir.z) - dax[2]) as f32),
    ];
    let dw = [
        dr[2] * ay - dr[1] * az,
        dr[0] * az - ax * dr[2],
        ax * dr[1] - dr[0] * ay,
    ];
    // moment o × dir (float), decomposed the same way
    let (ofx, ofy, ofz) = (
        ray.origin.x - cc.x,
        ray.origin.y - cc.y,
        ray.origin.z - cc.z,
    );
    let (rdx, rdy, rdz) = (ray.dir.x, ray.dir.y, ray.dir.z);
    let m = [
        f64::from(rdz * ofy - rdy * ofz),
        f64::from(rdx * ofz - rdz * ofx),
        f64::from(rdy * ofx - rdx * ofy),
    ];
    let mdot = (m[1] * ay + ax * m[0]) + m[2] * az;
    let max = [ax * mdot, mdot * ay, mdot * az];
    let mr = [m[0] - max[0], m[1] - max[1], m[2] - max[2]];
    let mw = [
        mr[2] * ay - mr[1] * az,
        mr[0] * az - ax * mr[2],
        ax * mr[1] - mr[0] * ay,
    ];
    // k × c
    let kc = [cz * ky - cy * kz, cx * kz - kx * cz, kx * cy - cx * ky];
    let g1 = ((mr[1] * ky + kx * mr[0]) + mr[2] * kz)
        - ((kc[1] * dr[1] + kc[0] * dr[0]) + kc[2] * dr[2]);
    let g2 = ((mw[1] * ky + kx * mw[0]) + mw[2] * kz)
        - ((kc[1] * dw[1] + kc[0] * dw[0]) + kc[2] * dw[2]);
    let g0 = ((max[1] * ky + kx * max[0]) + max[2] * kz)
        - ((kc[1] * dax[1] + kc[0] * dax[0]) + kc[2] * dax[2]);
    // dir parts × k
    let vr = [
        dr[1] * kz - dr[2] * ky,
        kx * dr[2] - dr[0] * kz,
        dr[0] * ky - kx * dr[1],
    ];
    let vw = [
        dw[1] * kz - dw[2] * ky,
        kx * dw[2] - dw[0] * kz,
        dw[0] * ky - kx * dw[1],
    ];
    let va = [
        dax[1] * kz - dax[2] * ky,
        kx * dax[2] - dax[0] * kz,
        dax[0] * ky - kx * dax[1],
    ];
    {
        let q_sin = ((g2 * g1) - (((vw[1] * vr[1] + vw[0] * vr[0]) + vw[2] * vr[2]) * r2)) * 2.0;
        let q_cos = ((g0 * g1) - (((va[1] * vr[1] + va[0] * vr[0]) + va[2] * vr[2]) * r2)) * 2.0;
        let g1sq = g1 * g1;
        let vrr = (vr[1] * vr[1] + vr[0] * vr[0]) + vr[2] * vr[2];
        let a0 =
            (g0 * g0 + g1sq) - ((((va[1] * va[1] + va[0] * va[0]) + va[2] * va[2]) + vrr) * r2);
        let va_vw = ((va[1] * vw[1] + va[0] * vw[0]) + va[2] * vw[2]) * r2;
        let a1 = ((g0 * g2) - va_vw) * 2.0;
        let a2 = ((g2 * g2) - g1sq) - ((((vw[1] * vw[1] + vw[0] * vw[0]) + vw[2] * vw[2]) - vrr) * r2);
        let qq = q_sin * q_cos;
        let qq2 = qq + qq;
        let s02 = a0 * a2;
        let s01 = a0 * a1;
        let s12 = a2 * a1;
        let p0 = a0 * a0 - q_cos * q_cos;
        let p1 = (s01 + s01) - qq2;
        let p2 = ((s02 + a1 * a1) + s02) - q_sin * q_sin;
        let p3 = s12 + s12;
        let p4 = a2 * a2;
        let poly = [1.0, p0, p1, p2, p3, p4];
        let n = solve(solver, &poly, &mut roots);
        let e0 = &ends[0];
        let mut i = n - 1;
        while i >= 0 {
            let s = roots[i as usize];
            let co = (1.0 - s * s).sqrt();
            let py = ((e0.r[1] * co + e0.h[1]) + e0.w[1] * s) - cy;
            let pz = ((e0.r[2] * co + e0.h[2]) + e0.w[2] * s) - cz;
            let ddx = (dax[0] + dr[0] * co) + dw[0] * s;
            let ddz = (dr[2] * co + dax[2]) + dw[2] * s;
            let px = ((e0.h[0] + e0.r[0] * co) + e0.w[0] * s) - cx;
            let ddy = (dr[1] * co + dax[1]) + dw[1] * s;
            let nx = ddy * kz - ddz * ky;
            let ny = kx * ddz - kz * ddx;
            let nz = ky * ddx - kx * ddy;
            let nn = (nx * nx + ny * ny) + nz * nz;
            let nr2 = nn * r2;
            let pn = (ny * py + nx * px) + nz * pz;
            let lo = nr2 * 0.9801;
            let hi = nr2 * 1.0201;
            let dist = (((hi + lo) - (pn * pn) * 2.0).abs() - (lo - hi).abs()) as f32;
            let (npx, npy, npz) = (-px, -py, -pz);
            let tq = (((npy * kz - npz * ky) * nx) + ((kx * npz - kz * npx) * ny))
                + ((ky * npx - kx * npy) * nz);
            let seg = ((nn - tq * 2.0).abs() - (-nn).abs()) as f32;
            let axial = (((((nn * px + tq * ddx) * kx) + (py * nn + ddy * tq) * ky)
                + (pz * nn + ddz * tq) * kz)
                .abs()
                - nn * hh) as f32;
            let better = (bnum - bden * s) as f32;
            let sel = sgn(better) & sgn(axial) & sgn(seg) & sgn(dist);
            feat = pick_feat(sel, 0x20, feat);
            let (keep, selv) = keep_sel(sel);
            bnum = keep * bnum + selv * s;
            bden = keep * bden + selv;
            i -= 1;
        }
    }

    // ---- phase 3: the ray line against the cap sphere centres
    let dd = f64::from((ray.dir.x * ray.dir.x + ray.dir.y * ray.dir.y) + ray.dir.z * ray.dir.z);
    let mut j: i32 = -1;
    while j <= 1 {
        let jh = f64::from(j) * hh;
        let cjy = ky * jh + cy;
        let cjz = kz * jh + cz;
        let cjx = cx + kx * jh;
        let x = [
            mr[0] + (cjz * dr[1] - cjy * dr[2]),
            (dr[2] * cjx - dr[0] * cjz) + mr[1],
            (dr[0] * cjy - dr[1] * cjx) + mr[2],
        ];
        let y = [
            mw[0] + (cjz * dw[1] - cjy * dw[2]),
            (dw[2] * cjx - dw[0] * cjz) + mw[1],
            (dw[0] * cjy - dw[1] * cjx) + mw[2],
        ];
        let z = [
            max[0] + (cjz * dax[1] - cjy * dax[2]),
            (dax[2] * cjx - dax[0] * cjz) + max[1],
            (dax[0] * cjy - dax[1] * cjx) + max[2],
        ];
        let b1 = (y[0] * x[0] + y[1] * x[1]) + y[2] * x[2];
        let b0 = 0.0 + ((z[0] * x[0] + z[1] * x[1]) + z[2] * x[2]);
        let xx = (x[0] * x[0] + x[1] * x[1]) + x[2] * x[2];
        let a1 = ((z[0] * y[0] + z[1] * y[1]) + z[2] * y[2]) * 2.0 + 0.0;
        let a2 = ((y[0] * y[0] + y[1] * y[1]) + y[2] * y[2]) - xx;
        let a0 = (((0.0 + 0.0) + ((z[0] * z[0] + z[1] * z[1]) + z[2] * z[2])) + xx) - dd * r2;
        let poly = quartic_ab(a0, a1, a2, b0, b1);
        let n = solve(solver, &poly, &mut roots);
        let e0 = &ends[0];
        let f = ((j + 1) | 0x80) >> 1;
        let mut i = n - 1;
        while i >= 0 {
            let s = roots[i as usize];
            let co = (1.0 - s * s).sqrt();
            let [px, py, pz] = e0.at(co, s);
            let ddy = (dr[1] * co + dax[1]) + dw[1] * s;
            let ddx = (dax[0] + dr[0] * co) + dw[0] * s;
            let ddz = (dr[2] * co + dax[2]) + dw[2] * s;
            let tq = ((cjy - py) * ddy + (cjx - px) * ddx) + (cjz - pz) * ddz;
            let dl2 = (ddy * ddy + ddx * ddx) + ddz * ddz;
            let qy = (py - cjy) * dl2 + ddy * tq;
            let qz = (pz - cjz) * dl2 + ddz * tq;
            let qx = dl2 * (px - cjx) + tq * ddx;
            let hi = dl2 * 1.01;
            let lo = dl2 * 0.99;
            let hi2 = (hi * hi) * r2;
            let lo2 = (lo * lo) * r2;
            let q2 = ((qy * qy + qx * qx) + qz * qz) * 2.0;
            let dist = (((hi2 + lo2) - q2).abs() - (lo2 - hi2).abs()) as f32;
            let seg = ((dl2 - tq * 2.0).abs() - (-dl2).abs()) as f32;
            let better = (bnum - bden * s) as f32;
            let sel = sgn(better) & sgn(seg) & sgn(dist);
            feat = pick_feat(sel, f, feat);
            let (keep, selv) = keep_sel(sel);
            bden = keep * bden + selv;
            bnum = keep * bnum + selv * s;
            i -= 1;
        }
        j += 2;
    }

    if feat < 0 {
        return 0;
    }
    let ccd = [f64::from(cc.x), f64::from(cc.y), f64::from(cc.z)];
    match feat & 0xF0 {
        0 => {
            // ray end against a cap sphere
            let e = (feat & 1) as usize;
            let jj = (feat & 2) - 1;
            // ucomisd / je: zero or NaN denominator gives 0
            let s = if super::common::ucomisd_eq(bden, 0.0) {
                0.0
            } else {
                bnum / bden
            };
            out.t = s;
            let co = (1.0 - s * s).sqrt();
            out.cos_t = co;
            let ce = &ends[e];
            let px = (ce.r[0] * co + ce.h[0]) + s * ce.w[0];
            let py = (ce.r[1] * co + ce.h[1]) + ce.w[1] * s;
            let pz = (ce.r[2] * co + ce.h[2]) + ce.w[2] * s;
            out.pt = Vec3::new(
                (ccd[0] + px) as f32,
                (ccd[1] + py) as f32,
                (ccd[2] + pz) as f32,
            );
            let jhf = jj as f32 * cap.hh;
            let mut nx = (cap.axis.x * jhf + cap.center.x) - out.pt.x;
            let mut ny = (cap.axis.y * jhf + cap.center.y) - out.pt.y;
            let mut nz = (cap.axis.z * jhf + cap.center.z) - out.pt.z;
            let l2 = (nx * nx + ny * ny) + nz * nz;
            if super::common::le_or_nan(l2, f32::from_bits(0x1E3C_E508)) {
                nx = 0.0;
                ny = 0.0;
                nz = 1.0;
            } else {
                let inv = 1.0 / l2.sqrt();
                nx *= inv;
                ny *= inv;
                nz *= inv;
            }
            out.n = Vec3::new(nx, ny, nz);
            out.i_feature = [(e as i32) | 0x80, ((jj + 1) >> 1) + 0x41];
        }
        0x10 => {
            // ray end against the cylinder side
            let e = (feat & 1) as usize;
            let s = bnum;
            out.t = s;
            let co = (1.0 - s * s).sqrt();
            out.cos_t = co;
            let ce = &ends[e];
            let px = (ce.r[0] * co + ce.h[0]) + s * ce.w[0];
            let py = (ce.r[1] * co + ce.h[1]) + s * ce.w[1];
            let pz = (ce.r[2] * co + ce.h[2]) + s * ce.w[2];
            out.pt = Vec3::new(
                (ccd[0] + px) as f32,
                (ccd[1] + py) as f32,
                (ccd[2] + pz) as f32,
            );
            let mut n = Vec3::new(
                cap.center.x - out.pt.x,
                cap.center.y - out.pt.y,
                cap.center.z - out.pt.z,
            );
            let dot = (n.y * cap.axis.y + n.x * cap.axis.x) + n.z * cap.axis.z;
            n = Vec3::new(
                n.x - cap.axis.x * dot,
                n.y - cap.axis.y * dot,
                n.z - cap.axis.z * dot,
            );
            normalize_in_place(&mut n);
            out.n = n;
            out.i_feature = [(e as i32) | 0x80, 0x40];
        }
        0x20 => {
            // ray line against the cylinder side
            let s = bnum;
            out.t = s;
            let co = (1.0 - s * s).sqrt();
            out.cos_t = co;
            let e0 = &ends[0];
            let px = ccd[0] + ((e0.h[0] + e0.r[0] * co) + e0.w[0] * s);
            let py = ccd[1] + ((e0.r[1] * co + e0.h[1]) + e0.w[1] * s);
            let pz = ccd[2] + ((e0.r[2] * co + e0.h[2]) + e0.w[2] * s);
            let ddy = (dr[1] * co + dax[1]) + dw[1] * s;
            let ddz = (dr[2] * co + dax[2]) + dw[2] * s;
            let ddx = (dax[0] + dr[0] * co) + dw[0] * s;
            let mut nx = ddy * kz - ddz * ky;
            let mut ny = kx * ddz - kz * ddx;
            let mut nz = ky * ddx - kx * ddy;
            let ex = (f64::from(cap.center.x) - px) as f32;
            let ey = (f64::from(cap.center.y) - py) as f32;
            let ez = (f64::from(cap.center.z) - pz) as f32;
            let (kxf, kyf, kzf) = (kx as f32, ky as f32, kz as f32);
            let c2 = kyf * ex - ey * kxf;
            let c0 = kzf * ey - kyf * ez;
            let c1 = ez * kxf - kzf * ex;
            let num = (c0 * nx as f32 + ny as f32 * c1) + nz as f32 * c2;
            let nn = (nx * nx + ny * ny) + nz * nz;
            let tt = if super::common::ucomisd_eq(nn, 0.0) {
                0.0
            } else {
                f64::from(num) / nn
            };
            out.pt = Vec3::new(
                (tt * ddx + px) as f32,
                (ddy * tt + py) as f32,
                (ddz * tt + pz) as f32,
            );
            let l2 = (ny * ny + nx * nx) + nz * nz;
            if super::common::le_or_nan_d(l2, f64::from(f32::from_bits(0x1E3C_E508))) {
                nx = 0.0;
                ny = 0.0;
                nz = 1.0;
            } else {
                let inv = 1.0 / l2.sqrt();
                nx *= inv;
                ny *= inv;
                nz *= inv;
            }
            let n = Vec3::new(nx as f32, ny as f32, nz as f32);
            let side = ((cap.center.y - out.pt.y) * n.y + (cap.center.x - out.pt.x) * n.x)
                + (cap.center.z - out.pt.z) * n.z;
            let sg = (((side.to_bits() as i32) >> 30) & !1) + 1;
            let sgf = sg as f32;
            out.n = Vec3::new(sgf * n.x, sgf * n.y, sgf * n.z);
            out.i_feature = [0x20, 0x40];
        }
        0x40 => {
            // ray line against a cap sphere centre
            let jj = 2 * (feat & 1) - 1;
            let s = bnum;
            out.t = s;
            let co = (1.0 - s * s).sqrt();
            out.cos_t = co;
            let e0 = &ends[0];
            let px = (e0.h[0] + e0.r[0] * co) + e0.w[0] * s;
            let py = (e0.r[1] * co + e0.h[1]) + e0.w[1] * s;
            let pz = (e0.r[2] * co + e0.h[2]) + e0.w[2] * s;
            let ddy = (dr[1] * co + dax[1]) + dw[1] * s;
            let ddz = (dr[2] * co + dax[2]) + dw[2] * s;
            let ddx = (dax[0] + dr[0] * co) + dw[0] * s;
            let jh = f64::from(jj) * hh;
            let cjy = ky * jh + cy;
            let cjz = kz * jh + cz;
            let cjx = cx + kx * jh;
            let tq = ((cjx - px) * ddx + (cjy - py) * ddy) + (cjz - pz) * ddz;
            let dl2 = (ddx * ddx + ddy * ddy) + ddz * ddz;
            let tt = if super::common::ucomisd_eq(dl2, 0.0) {
                0.0
            } else {
                tq / dl2
            };
            out.pt = Vec3::new(
                (tt * ddx + px) as f32,
                (tt * ddy + py) as f32,
                (tt * ddz + pz) as f32,
            );
            let mut nx = cjx - f64::from(out.pt.x);
            let mut ny = cjy - f64::from(out.pt.y);
            let mut nz = cjz - f64::from(out.pt.z);
            let l2 = (nx * nx + ny * ny) + nz * nz;
            if super::common::le_or_nan_d(l2, f64::from(f32::from_bits(0x1E3C_E508))) {
                nx = 0.0;
                ny = 0.0;
                nz = 1.0;
            } else {
                let inv = 1.0 / l2.sqrt();
                nx *= inv;
                ny *= inv;
                nz *= inv;
            }
            out.n = Vec3::new(nx as f32, ny as f32, nz as f32);
            out.pt = Vec3::new(cc.x + out.pt.x, cc.y + out.pt.y, cc.z + out.pt.z);
            out.i_feature = [0x20, ((jj + 1) | 0x40) >> 1];
        }
        // unreachable feature codes: the binary returns 1 without writing anything
        _ => {}
    }
    1
}

fn cell_ray_cap_rot(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_ray_cap_rot(p, as_ray(a), fa, as_cap(b), fb, out)
}

/// sub_351A7AF0 (x86 0x351BCF20): capsule–ray rotational, the 447-byte swap wrapper around
/// [`unproject_ray_cap_rot`].
fn cell_cap_ray_rot(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    let (cap, ray) = (as_cap(a), as_ray(b));
    swap_rot(p, out, |p, out| {
        unproject_ray_cap_rot(p, ray, fb, cap, fa, out)
    })
}

/// Fill the cells this group owns: `[1][ray][cap]` and `[1][cap][ray]`.
pub fn fill(t: &mut UnprojTables) {
    t.set(
        MODE_ROTATIONAL,
        GeomType::Ray,
        GeomType::Capsule,
        cell_ray_cap_rot,
    );
    t.set(
        MODE_ROTATIONAL,
        GeomType::Capsule,
        GeomType::Ray,
        cell_cap_ray_rot,
    );
}
