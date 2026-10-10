//! Spline areas: the closest point on the area's quadratic B-spline (`sub_35043350`) and the
//! polynomial root finder it uses (`sub_35050630`, degrees 0–3).
//!
//! The spline through the area points `p[0..n)` has one quadratic segment per point `i`
//! (`P0 = p[max(i−1, 0)]`, `P1 = p[i]`, `P2 = p[min(i+1, n−1)]`):
//! `B(t) = A·t² + B·t + C`, `A = (P2+P0)/2 − P1`, `B = P1 − P0`, `C = (P1+P0)/2`, `t ∈ [0, 1]`.
//! The squared distance is minimised over the segments whose bbox (inflated by the tube
//! radius) contains the point by solving `d/dt |B(t) − p|² / 2 = 0` (a cubic, coefficients in
//! single precision, solved in double) and over the two end points.

use super::super::{EntIdx, World};
use crate::math::crt;
use crate::math::Vec3;

const ABS: fn(f64) -> f64 = f64::abs;

/// `+12582912`-free sign helper of the solver: `((bits(f32(x)) >> 30) & ~1) + 1` = ±1 by the
/// sign of `x` rounded to single precision.
#[inline]
fn fsign(x: f64) -> f64 {
    let b = (x as f32).to_bits() as i32;
    f64::from(((b >> 30) & !1) + 1)
}

/// Sign bit of `x` rounded to single precision (`cvtsd2ss` + `shr 31`).
#[inline]
fn fneg(x: f64) -> usize {
    ((x as f32).to_bits() >> 31) as usize
}

/// `sub_35050630(poly, start, end, roots[], nIters, degree, bNoReduce)` for the degrees the
/// spline query produces (`degree <= 3`, `bNoReduce = 0`): the real roots of
/// `c[0] + c[1]·t + c[2]·t² + c[3]·t³` inside `[start, end]`, ascending. Returns the count.
///
/// * Degree reduction: `m = max(|c0|, c1, c2, c3)` (the running `max` idiom on the signed
///   coefficients after the first), the leading coefficients with `|c| <= m·1e-10` are dropped.
/// * Degree 1: `t = c0 / c1` (**sign as in the binary**: the root of `c0 − c1·t`; kept).
/// * Degree 2: the closed form with a sign-normalised leading coefficient; no roots unless the
///   discriminant is non-negative and the `[start, end]` interval test passes.
/// * Degree 3: Cardano (one real root: cube roots through `exp(log|x|/3)`; three: the
///   trigonometric form through `atan2`/`cos`/`sin`, then a 3-element sort).
/// * Roots below `start` / above `end` are removed (the list is assumed ascending).
///
/// Degrees >= 4 (Sturm-free bisection on the derivative's roots) are not used by the areas
/// and not ported.
#[must_use]
pub fn find_roots(c: &[f64; 4], start: f64, end: f64, degree: usize, roots: &mut [f64; 4]) -> usize {
    // degree reduction
    let mut m = ABS(c[0]);
    for &ck in c.iter().take(degree + 1).skip(1) {
        m = (ABS(m - ck) + (ck + m)) * 0.5;
    }
    let eps = m * f64::from_bits(0x3DDB_7CDF_D9D7_BDBB); // 1e-10
    let mut deg = degree as i64;
    while deg > 0 {
        if ABS(c[deg as usize]) > eps {
            break;
        }
        deg -= 1;
    }
    let half = f64::from_bits(0x3FE0_0000_0000_0000);
    let third = f64::from_bits(0x3FD5_5555_5555_5555);
    let tiny = f64::from_bits(0x3BC7_9CA1_0C92_4223); // 1e-20
    let mut n = 0usize;
    match deg {
        1 => {
            roots[0] = c[0] / c[1];
            n = 1;
        }
        2 => {
            let s = fsign(c[2]);
            let a = c[2] * s;
            let b = s * c[1];
            let disc = b * b - ((s * c[0]) * a) * 4.0;
            let ue = (a * end) * 2.0 + b;
            let us = (a * start) * 2.0 + b;
            let prod_sign = fsign(ue * us);
            let mut sq = [us * us, ue * ue];
            let k = fneg(ABS(end) - ABS(start));
            sq[k] *= prod_sign;
            let t = ABS((sq[1] + sq[0]) - disc * 2.0) - ABS(sq[0] - sq[1]);
            let ok = (!(disc as f32).to_bits()) & (t as f32).to_bits();
            if ok & 0x8000_0000 != 0 {
                let sd = disc.sqrt();
                let k2 = half / a;
                let r0 = (-b - sd) * k2;
                let r1 = (sd - b) * k2;
                let se = start + end;
                let de = ABS(start - end);
                roots[0] = r0;
                let i0 = fneg(ABS(se - r0 * 2.0) - de);
                roots[i0] = r1;
                n = i0 + fneg(ABS(se - r1 * 2.0) - de);
            }
        }
        3 => {
            let inv = 1.0 / c[3];
            let a = inv * c[2];
            let b = inv * c[1];
            let a3 = a * third;
            let q = (a3 * b - inv * c[0]) * half - (a3 * a3) * a3;
            let p = (b - a3 * a) * third;
            let d = (p * p) * p + q * q;
            let sd = ABS(d).sqrt();
            if d <= 0.0 {
                let ang = crt::atan2(sd, q);
                let mut r = sd * sd + q * q;
                let ang3 = ang * third;
                if ABS(r) > tiny {
                    let s = fsign(r);
                    let l = crt::log(ABS(r));
                    r = crt::exp(l * f64::from_bits(0x3FC5_5555_5555_5555)) * s;
                }
                let cc = crt::cos(ang3) * r;
                let ss = (crt::sin(ang3) * r) * f64::from_bits(0x3FFB_B67A_E858_4CAA);
                roots[0] = cc * 2.0 - a3;
                roots[1] = (ss - cc) - a3;
                roots[2] = (-cc - ss) - a3;
                n = 3;
                let c0 = fneg(roots[0] - roots[1]);
                let d0 = fneg(roots[c0] - roots[2]);
                let v = (2 * d0) | c0;
                let idx = v & (((v ^ 2) | 4) >> 1);
                roots.swap(idx, 2);
                let e = fneg(roots[0] - roots[1]);
                roots.swap(e, 1);
            } else {
                let cbrt = |x: f64| -> f64 {
                    if ABS(x) > tiny {
                        let s = fsign(x);
                        crt::exp(crt::log(ABS(x)) * third) * s
                    } else {
                        x
                    }
                };
                let u = cbrt(sd + q);
                let v = cbrt(q - sd);
                roots[0] = (v + u) - a3;
                n = 1;
            }
        }
        _ => {}
    }
    // drop the roots below `start` and above `end`
    let mut lo = 0usize;
    while lo < n {
        if start <= roots[lo] {
            break;
        }
        lo += 1;
    }
    let mut hi = n;
    while hi > lo {
        if end >= roots[hi - 1] {
            break;
        }
        hi -= 1;
    }
    for k in lo..hi {
        roots[k - lo] = roots[k];
    }
    hi - lo
}

impl World {
    /// `sub_35043350(area, pt, &iseg, &t)`: squared distance from `pt` (area frame) to the
    /// area spline; `(d², segment, t)`. The last query is cached (`+312 … +332`) and returned
    /// unchanged for the identical point.
    pub fn area_spline_closest(&mut self, a: EntIdx, pt: Vec3) -> (f32, i32, f32) {
        let d = self.area(a);
        let cp = d.cache_pt;
        let dc = ((pt.x - cp.x) * (pt.x - cp.x) + (pt.y - cp.y) * (pt.y - cp.y)) + (pt.z - cp.z) * (pt.z - cp.z);
        if !(dc != 0.0) {
            return (d.cache_d2, d.cache_seg, d.cache_t);
        }
        let p = &d.points;
        let n = d.npoints;
        let rad = d.radius;
        let dist2 = |q: Vec3| ((pt.x - q.x) * (pt.x - q.x) + (pt.y - q.y) * (pt.y - q.y)) + (pt.z - q.z) * (pt.z - q.z);
        let mut best = dist2(p[0]);
        let mut seg = 0i32;
        let mut tb = 0.0f32;
        let dl = dist2(p[(n - 1) as usize]);
        if dl < best {
            best = dl;
            tb = 1.0;
            seg = n - 1;
        }
        for i in 0..n {
            let p0 = p[(i - 1).max(0) as usize];
            let p1 = p[i as usize];
            let p2 = p[(i + 1).min(n - 1) as usize];
            // segment bbox (min/max idioms), inflated by the tube radius
            let sx = p1.x + p0.x;
            let ax = (p0.x - p1.x).abs();
            let ay = (p0.y - p1.y).abs();
            let sy = p1.y + p0.y;
            let az = (p0.z - p1.z).abs();
            let sz = p1.z + p0.z;
            let mn = |s: f32, a: f32, c: f32| ((((s - a) * 0.5) + c) - (((s - a) * 0.5) - c).abs()) * 0.5;
            let mx = |s: f32, a: f32, c: f32| (((a + s) * 0.5 - c).abs() + ((a + s) * 0.5 + c)) * 0.5;
            let bmin = Vec3::new(mn(sx, ax, p2.x) - rad, mn(sy, ay, p2.y) - rad, mn(sz, az, p2.z) - rad);
            let bmax = Vec3::new(rad + mx(sx, ax, p2.x), rad + mx(sy, ay, p2.y), rad + mx(sz, az, p2.z));
            if !point_in_box_4ff30(bmin, bmax, pt) {
                continue;
            }
            // B(t) − pt = A t² + B t + D
            let dx = (sx * 0.5) - pt.x;
            let dy = (sy * 0.5) - pt.y;
            let dz = (sz * 0.5) - pt.z;
            let a_x = ((p2.x + p0.x) * 0.5) - p1.x;
            let a_y = ((p2.y + p0.y) * 0.5) - p1.y;
            let a_z = ((p2.z + p0.z) * 0.5) - p1.z;
            let b_x = p1.x - p0.x;
            let b_y = p1.y - p0.y;
            let b_z = p1.z - p0.z;
            let c1 = ((dy * a_y + dx * a_x) + dz * a_z) * 2.0 + ((b_y * b_y + b_x * b_x) + b_z * b_z);
            let c2 = ((b_y * a_y + b_x * a_x) + b_z * a_z) * 3.0;
            let c3 = ((a_y * a_y + a_x * a_x) + a_z * a_z) * 2.0;
            let c0 = ((dy * b_y) + (dx * b_x)) + dz * b_z;
            // the polynomial sums of the binary: 0 + x (turns -0 into +0)
            let coef = [
                0.0 + f64::from(c0) * 1.0,
                (0.0 + 0.0) + f64::from(c1),
                0.0 + f64::from(c2),
                f64::from(c3),
            ];
            let mut roots = [0.0f64; 4];
            let nr = find_roots(&coef, 0.0, 1.0, 3, &mut roots);
            for k in (0..nr).rev() {
                let t = roots[k] as f32;
                let ey = ((a_y * t) + b_y) * t + dy;
                let ex = ((a_x * t) + b_x) * t + dx;
                let ez = ((a_z * t) + b_z) * t + dz;
                let f = (ey * ey + ex * ex) + ez * ez;
                if f < best {
                    best = f;
                    seg = i;
                    tb = roots[k] as f32;
                }
            }
        }
        let d = self.area_mut(a);
        d.cache_pt = pt;
        d.cache_seg = seg;
        d.cache_t = tb;
        d.cache_d2 = best;
        (best, seg, tb)
    }
}

/// `sub_3504FF30`: `pt` inside the box (`max(|(lo+hi) − 2p| − (hi − lo)) < 0` per axis, the
/// max idiom over x, y, then z).
#[must_use]
pub fn point_in_box_4ff30(lo: Vec3, hi: Vec3, p: Vec3) -> bool {
    let v2 = ((lo.y + hi.y) - p.y * 2.0).abs() - (hi.y - lo.y);
    let v3 = ((lo.x + hi.x) - p.x * 2.0).abs() - (hi.x - lo.x);
    let v4 = ((v3 - v2).abs() + (v3 + v2)) * 0.5;
    let v5 = ((lo.z + hi.z) - p.z * 2.0).abs() - (hi.z - lo.z);
    (((v4 - v5).abs() + (v5 + v4)) * 0.5) < 0.0
}
