//! The engine's small polynomial template (`polynomial_tpl<double, N>`, inferred name), as far
//! as `CCylinderGeom::FindClosestPoint` (0x35145CC0) uses it: a quartic in the segment
//! parameter whose roots in `[0, 1]` are the candidate closest points between the segment and
//! a cap rim.
//!
//! Layout in the binary: `double denom; double data[N+1]` (ascending coefficients). The value
//! of the polynomial is `denom · Σ data[i]·xⁱ`; the "pseudo-division" below never divides, it
//! scales the remainder and its denominator by the divisor's leading coefficient instead.
//!
//! Functions (all double precision, operation order from the disassembly; Hex-Rays mis-prints
//! the association of the max idiom, `(|m − c| + (c + m))·0.5`, and the NaN behaviour of the
//! degree trimming loops):
//! * [`derivative`] — `sub_35148830`,
//! * [`divide`] — `sub_351489E0` (pseudo-division, remainder only),
//! * [`sturm_nroots`] — `sub_3514A2E0` (number of roots in an interval via a Sturm sequence),
//! * [`findroots`] — `sub_35149170` (degree 4 instance) and `sub_35050630` (degree 3 instance,
//!   the same code): closed forms for degrees 1–4, then the interval filter.

// `!(a > b)`-style tests are deliberate: they reproduce the binary's branch on unordered
// (NaN) operands (`comiss`/`comisd` + `jbe`/`jb`/`jae`).
#![allow(clippy::neg_cmp_op_on_partial_ord)]

use super::crt64;
use crate::math::crt;
use crate::math::sgnnz;

/// `1e-10` (double 0x3DDB7CDFD9D7BDBB), the relative coefficient threshold.
const REL_EPS: f64 = f64::from_bits(0x3DDB_7CDF_D9D7_BDBB);
/// `1e10` (double 0x4202A05F20000000), the Sturm rescaling threshold.
const STURM_BIG: f64 = f64::from_bits(0x4202_A05F_2000_0000);
/// `1/3` (double 0x3FD5555555555555).
const THIRD: f64 = f64::from_bits(0x3FD5_5555_5555_5555);
/// `1/6` (double 0x3FC5555555555555).
const SIXTH: f64 = f64::from_bits(0x3FC5_5555_5555_5555);
/// `√3` (double 0x3FFBB67AE8584CAA).
const SQRT3: f64 = f64::from_bits(0x3FFB_B67A_E858_4CAA);
/// `1e-20` (double 0x3BC79CA10C924223), the cube-root guard of the cubic.
const CBRT_EPS: f64 = f64::from_bits(0x3BC7_9CA1_0C92_4223);
/// `1e-9` (double 0x3E112E0BE826D695), the quartic's tolerance.
const QUARTIC_EPS: f64 = f64::from_bits(0x3E11_2E0B_E826_D695);
/// `1e20` (double 0x4415AF1D78B58C40), the resolvent cubic's search interval.
const RESOLVENT_RANGE: f64 = f64::from_bits(0x4415_AF1D_78B5_8C40);

/// A polynomial of degree ≤ 4: `denom · Σ c[i]·xⁱ`.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Poly {
    pub denom: f64,
    pub c: [f64; 5],
}

impl Poly {
    /// A polynomial object as the engine's constructor leaves it: `denom = 1`, coefficients
    /// unset (zero here; the binary never reads the unset ones).
    #[must_use]
    pub const fn one() -> Self {
        Self {
            denom: 1.0,
            c: [0.0; 5],
        }
    }
}

/// Sign bit of a double as the binary tests it (`cvtsd2ss` + `shr 31`: the conversion keeps the
/// sign of every value, NaN included).
#[inline]
fn sb(x: f64) -> usize {
    (x.to_bits() >> 63) as usize
}

/// `max(m, c)` written `(|m − c| + (c + m))·0.5` (double; note `c` is **signed**: the
/// "magnitude" of a polynomial is `max(|c0|, c1, …, cn)` in the binary).
#[inline]
fn max_d(m: f64, c: f64) -> f64 {
    ((m - c).abs() + (c + m)) * 0.5
}

/// `max(|c0|, c1, …, c_deg)·1e-10` (the trimming threshold of [`divide`] and [`findroots`]).
fn trim_eps(p: &Poly, deg: i32) -> f64 {
    let mut m = p.c[0].abs();
    for i in 1..=deg {
        m = max_d(m, p.c[i as usize]);
    }
    m * REL_EPS
}

/// `sub_35148830`: `d.c[i] = (i+1)·p.c[i+1]` for `i < n`, `d.denom = p.denom`; the other
/// coefficients of `d` are left untouched.
pub fn derivative(p: &Poly, d: &mut Poly, n: usize) {
    for i in 0..n {
        d.c[i] = (i + 1) as f64 * p.c[i + 1];
    }
    d.denom = p.denom;
}

/// `sub_351489E0`: pseudo-division of `num` (nominal degree `dn`) by `den` (nominal degree
/// `dd`); writes the remainder into `rem`. The quotient (a scratch buffer the only caller never
/// reads) is not computed.
///
/// * `rem.c[0..=dn] = num.c[0..=dn]`, `rem.denom = num.denom`;
/// * the effective degrees drop while `!(|c[n]| ≥ eps)` (down to −1; NaN coefficients count as
///   zero), `eps` from `trim_eps`;
/// * for each top index `i` from `n1` down to `n2` (`lead = den.c[n2]`, `qi = i − n2`):
///   `rem.c[qi+j] = lead·rem.c[qi+j] − rem.c[i]·den.c[j]` (`j = n2−1 … 0`),
///   `rem.c[k] *= lead` (`k = qi−1 … 0`), `rem.denom *= lead`. `rem.c[i]` itself is left
///   stale (the caller reads the remainder with its nominal degree).
pub fn divide(num: &Poly, den: &Poly, dn: i32, dd: i32, rem: &mut Poly) {
    for i in 0..=dn {
        rem.c[i as usize] = num.c[i as usize];
    }
    let mut n1 = dn;
    let eps1 = trim_eps(num, dn);
    while n1 >= 0 && !(num.c[n1 as usize].abs() >= eps1) {
        n1 -= 1;
    }
    let mut n2 = dd;
    let eps2 = trim_eps(den, dd);
    while n2 >= 0 && !(den.c[n2 as usize].abs() >= eps2) {
        n2 -= 1;
    }
    rem.denom = num.denom;
    if n1 < 0 || n2 < 0 || n1 < n2 {
        return;
    }
    let (n1, n2) = (n1 as usize, n2 as usize);
    let lead = den.c[n2];
    for i in (n2..=n1).rev() {
        let qi = i - n2;
        for j in (0..n2).rev() {
            rem.c[qi + j] = lead * rem.c[qi + j] - rem.c[i] * den.c[j];
        }
        for k in (0..qi).rev() {
            rem.c[k] *= lead;
        }
        rem.denom *= lead;
    }
}

/// `sub_3514A2E0`: the number of distinct real roots of the quartic `p` in `(a, b]`, from the
/// Sturm sequence `s0 = p'`, `s1 = −rem(p / s0)`, `s2 = −rem(s0 / s1)`, `s3 = −rem(s1 / s2)`
/// (negation through the denominator; a sequence member whose `|denom|` exceeds `1e10` is
/// rescaled by `1e-10`). Each member is evaluated with its nominal degree (3, 2, 1, 0) by
/// Horner and multiplied by its denominator; `p` itself is evaluated as
/// `denom·((((x·0 + c4)·x + c3)·x + c2)·x + c1)·x + c0`. A sign change is the sign bit of the
/// product of consecutive values (so an exact zero next to a negative value counts). Returns
/// `|V(a) − V(b)|`.
#[must_use]
pub fn sturm_nroots(p: &Poly, a: f64, b: f64) -> i32 {
    let mut s = [Poly::one(); 4];
    derivative(p, &mut s[0], 4);
    let s0 = s[0];
    divide(p, &s0, 4, 3, &mut s[1]);
    s[1].denom = -s[1].denom;
    for (k, deg) in [(0usize, 1i32), (1, 0)] {
        let (num, den) = (s[k], s[k + 1]);
        divide(&num, &den, deg + 2, deg + 1, &mut s[k + 2]);
        let d = -s[k + 2].denom;
        s[k + 2].denom = d;
        if d.abs() > STURM_BIG {
            for i in 0..=deg as usize {
                s[k + 2].c[i] *= REL_EPS;
            }
            s[k + 2].denom *= REL_EPS;
        }
    }
    let changes = |x: f64| -> i32 {
        let c = &p.c;
        let mut prev =
            p.denom * (((((x * 0.0 + c[4]) * x + c[3]) * x + c[2]) * x + c[1]) * x + c[0]);
        let mut n = 0i32;
        for (k, q) in s.iter().enumerate() {
            let deg = 3 - k;
            let mut v = q.c[deg];
            for j in (0..deg).rev() {
                v = v * x + q.c[j];
            }
            v *= q.denom;
            n += sb(v * prev) as i32;
            prev = v;
        }
        n
    };
    let d = changes(a).wrapping_sub(changes(b));
    let m = d >> 31;
    (m.wrapping_add(d)) ^ m
}

/// Sorting step of the closed forms: the index of the largest of `r[0..3]` by sign bits
/// (`i = sb(r0 − r1)`, `j = sb(r[i] − r2)`, `k = i | 2j`, `((k ^ 2) | 4) >> 1 & k`).
#[inline]
fn argmax3(r: &[f64; 4]) -> usize {
    let i = sb(r[0] - r[1]);
    let j = sb(r[i] - r[2]);
    let k = i | (j << 1);
    (((k ^ 2) | 4) >> 1) & k
}

/// `sub_35149170` / `sub_35050630` (`findroots(start, end, roots, nIters, degree, bForce)`):
/// the real roots of `p` (nominal degree `deg` ≤ 4) in `[start, end]`, ascending; returns their
/// number. `nIters` only matters for degrees above 4 (bisection on the derivative's roots),
/// which the closest-point code never reaches, so it is not a parameter here.
///
/// * Unless `force`, the degree drops while `deg > 0 && !(|c[deg]| > eps)`.
/// * Degree 1: `roots[0] = c0 / c1` — **original bug kept**: the sign is wrong (`−c0/c1`).
/// * Degree 2: with `s = sgnnz(c2)`, `a = c2·s`, `b = s·c1`, `disc = b² − ((s·c0)·a)·4`; the
///   roots `(−b ∓ √disc)·(0.5/a)` are reported when `disc ≥ 0` and the derivative values at the
///   interval ends allow them (the binary's sign-bit expression); each root lands in the next
///   slot only if it lies inside `[start, end]`.
/// * Degree 3: Cardano with `exp(log|x|/3)` cube roots when the discriminant is positive, else
///   the trigonometric form (`atan2`, `cos`, `sin`), then a 3-element sort. MSVCR100's `exp`,
///   `sin` and `cos` are ported bit-exactly in [`super::crt64`]; `log` and `atan2` come from
///   `math::crt` (`log` matched MSVCR100 on every sample, `atan2` differs in ≈0.015 % of
///   random inputs).
/// * Degree 4: Ferrari through the resolvent cubic, solved by the degree-3 instance on
///   `[−1e20, 1e20]` (no roots there → return 0); tolerances `±1e-9`; 4 roots are sorted.
/// * Finally roots `< start` are dropped from the front (ordered compare) and roots with
///   `!(end ≥ root)` (NaN included) from the back.
pub fn findroots(
    p: &Poly,
    start: f64,
    end: f64,
    roots: &mut [f64; 4],
    nominal: i32,
    force: bool,
) -> i32 {
    let mut deg = nominal;
    if !force {
        let mut m = p.c[0].abs();
        for i in 1..=nominal {
            m = max_d(m, p.c[i as usize]);
        }
        let eps = m * REL_EPS;
        while deg > 0 && !(p.c[deg as usize].abs() > eps) {
            deg -= 1;
        }
    }
    let mut n: usize = 0;
    match deg {
        1 => {
            roots[0] = p.c[0] / p.c[1];
            n = 1;
        }
        2 => n = quadratic(p, start, end, roots),
        3 => n = cubic(p, roots),
        4 => match quartic(p, roots) {
            Some(k) => n = k,
            None => return 0,
        },
        _ => {}
    }
    filter(roots, n, start, end)
}

/// The interval filter at the end of `findroots`.
fn filter(roots: &mut [f64; 4], n: usize, start: f64, end: f64) -> i32 {
    let mut lead = 0usize;
    while lead < n && start > roots[lead] {
        lead += 1;
    }
    let mut m = n;
    while m > lead && !(end >= roots[m - 1]) {
        m -= 1;
    }
    for k in lead..m {
        roots[k - lead] = roots[k];
    }
    (m - lead) as i32
}

/// Degree-2 branch of `findroots` (0x351493BA).
fn quadratic(p: &Poly, start: f64, end: f64, roots: &mut [f64; 4]) -> usize {
    let c2 = p.c[2];
    let s = f64::from(sgnnz(c2 as f32));
    let a = c2 * s;
    let b = s * p.c[1];
    let disc = b * b - ((s * p.c[0]) * a) * 4.0;
    let fe = (a * end) * 2.0 + b;
    let fs = (a * start) * 2.0 + b;
    let s2 = f64::from(sgnnz((fe * fs) as f32));
    let mut arr = [fs * fs, fe * fe];
    let idx = sb(end.abs() - start.abs());
    arr[idx] *= s2;
    let sum = arr[1] + arr[0];
    let dif = (arr[0] - arr[1]).abs();
    let ok = (1 - sb(disc)) & sb((sum - disc * 2.0).abs() - dif);
    if ok == 0 {
        return 0;
    }
    let sq = disc.sqrt();
    let ia = 0.5 / a;
    let se = start + end;
    let sd = (start - end).abs();
    let r0 = (-b - sq) * ia;
    let r1 = (sq - b) * ia;
    roots[0] = r0;
    let i0 = sb((se - r0 * 2.0).abs() - sd);
    roots[i0] = r1;
    let i1 = sb((se - r1 * 2.0).abs() - sd);
    i0 + i1
}

/// `exp(log|x|·k)·sgnnz(x)` when `|x| > 1e-20`, else `x` (the cube/sixth-root idiom).
fn signed_root(x: f64, k: f64) -> f64 {
    let ax = x.abs();
    if ax > CBRT_EPS {
        crt64::exp(crt::log(ax) * k) * f64::from(sgnnz(x as f32))
    } else {
        x
    }
}

/// Degree-3 branch of `findroots` (0x351495BD).
fn cubic(p: &Poly, roots: &mut [f64; 4]) -> usize {
    let inv = 1.0 / p.c[3];
    let a0 = inv * p.c[0];
    let a2 = inv * p.c[2];
    let a1 = inv * p.c[1];
    let a23 = a2 * THIRD;
    let q = (a23 * a1 - a0) * 0.5 - (a23 * a23) * a23;
    let pp = (a1 - a23 * a2) * THIRD;
    let d = (pp * pp) * pp + q * q;
    let sq = d.abs().sqrt();
    if d > 0.0 {
        let s1 = signed_root(sq + q, THIRD);
        let s2 = signed_root(q - sq, THIRD);
        roots[0] = (s2 + s1) - a23;
        return 1;
    }
    let ang = crt::atan2(sq, q);
    let rr = signed_root(sq * sq + q * q, SIXTH);
    let ang3 = ang * THIRD;
    let cs = crt64::cos(ang3) * rr;
    let sn = (crt64::sin(ang3) * rr) * SQRT3;
    roots[0] = cs * 2.0 - a23;
    roots[1] = (sn - cs) - a23;
    roots[2] = (-cs - sn) - a23;
    let idx = argmax3(roots);
    roots.swap(idx, 2);
    let i = sb(roots[0] - roots[1]);
    roots.swap(i, 1);
    3
}

/// `(|−x| + x)·0.5` then `sqrt` (the quartic's clamped square root).
#[inline]
fn sqrt_pos(x: f64) -> f64 {
    (((-x).abs() + x) * 0.5).sqrt()
}

/// Degree-4 branch of `findroots` (0x351498C6). `None` is the early `return 0` (no resolvent
/// root, or a negative discriminant in the degenerate branch).
fn quartic(p: &Poly, roots: &mut [f64; 4]) -> Option<usize> {
    let inv = 1.0 / p.c[4];
    let b0 = inv * p.c[0];
    let b1 = inv * p.c[1];
    let b3 = inv * p.c[3];
    let b2 = inv * p.c[2];
    let b0x4 = b0 * 4.0;
    let b33 = b3 * b3;
    let mut res = Poly::one();
    res.c[0] = ((b2 * 4.0) * b0 - b1 * b1) - b33 * b0;
    res.c[1] = b1 * b3 - b0x4;
    res.c[2] = -b2;
    res.c[3] = 1.0;
    let mut rr = [0f64; 4];
    if findroots(&res, -RESOLVENT_RANGE, RESOLVENT_RANGE, &mut rr, 3, false) == 0 {
        return None;
    }
    let y = rr[0];
    let mut r = (b33 * 0.25 - b2) + y;
    if !(r > -QUARTIC_EPS) {
        return Some(0);
    }
    let (d, e);
    if r >= QUARTIC_EPS {
        r = sqrt_pos(r);
        d = ((((b3 * 4.0) * b2 - b1 * 8.0) - b33 * b3) / r) * 0.25;
        e = (b33 * 0.75 - r * r) - b2 * 2.0;
    } else {
        // Quirk kept: `r` stays the tiny un-rooted value in this branch.
        let v = y * y - b0x4;
        e = b33 * 0.75 - b2 * 2.0;
        if !(v >= -QUARTIC_EPS) {
            return None;
        }
        d = sqrt_pos(v) * 2.0;
    }
    let mut n = 0usize;
    let ed = e + d;
    if ed > -QUARTIC_EPS {
        n = 2;
        let s = sqrt_pos(ed);
        let b34 = b3 * 0.25;
        roots[0] = (r - s) * 0.5 - b34;
        roots[1] = (s + r) * 0.5 - b34;
    }
    let em = e - d;
    if em > -QUARTIC_EPS {
        let mb = b3 * -0.25;
        let s = sqrt_pos(em);
        roots[n] = mb - (s + r) * 0.5;
        roots[n + 1] = mb - (r - s) * 0.5;
        n += 2;
    }
    if n == 4 {
        let idx = argmax3(roots);
        if roots[idx] > roots[3] {
            roots.swap(idx, 3);
        }
        let idx = argmax3(roots);
        roots.swap(idx, 2);
        let i = sb(roots[0] - roots[1]);
        roots.swap(i, 1);
    }
    Some(n)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn poly(c: [f64; 5]) -> Poly {
        Poly { denom: 1.0, c }
    }

    #[test]
    fn linear_root_has_the_original_sign_bug() {
        let mut r = [0.0; 4];
        // 2x - 1 = 0 has its root at 0.5; the engine reports c0/c1 = -0.5.
        let n = findroots(
            &poly([-1.0, 2.0, 0.0, 0.0, 0.0]),
            -10.0,
            10.0,
            &mut r,
            1,
            false,
        );
        assert_eq!(n, 1);
        assert_eq!(r[0], -0.5);
    }

    #[test]
    fn quadratic_and_quartic_roots() {
        let mut r = [0.0; 4];
        // (x - 0.25)(x - 0.75) = x² - x + 0.1875
        let n = findroots(
            &poly([0.1875, -1.0, 1.0, 0.0, 0.0]),
            0.0,
            1.0,
            &mut r,
            2,
            false,
        );
        assert_eq!(n, 2);
        assert!((r[0] - 0.25).abs() < 1e-12 && (r[1] - 0.75).abs() < 1e-12);
        // (x-0.1)(x-0.3)(x-0.6)(x-0.9)
        let roots = [0.1, 0.3, 0.6, 0.9];
        let mut c = [1.0, 0.0, 0.0, 0.0, 0.0];
        for &z in &roots {
            let mut nc = [0.0; 5];
            for i in 0..4 {
                nc[i + 1] += c[i];
                nc[i] -= z * c[i];
            }
            c = nc;
        }
        let p = poly(c);
        assert_eq!(sturm_nroots(&p, 0.0, 1.0), 4);
        assert_eq!(sturm_nroots(&p, 0.0, 0.5), 2);
        let n = findroots(&p, 0.0, 1.0, &mut r, 4, false);
        assert_eq!(n, 4);
        for k in 0..4 {
            assert!((r[k] - roots[k]).abs() < 1e-6, "{r:?}");
        }
        // Only the roots inside the interval survive the filter.
        let n = findroots(&p, 0.2, 0.7, &mut r, 4, false);
        assert_eq!(n, 2);
        assert!((r[0] - 0.3).abs() < 1e-6 && (r[1] - 0.6).abs() < 1e-6);
    }

    #[test]
    fn division_remainder() {
        // (x² + 1) / (x - 1): pseudo-remainder 2 (lead 1, so no scaling).
        let num = poly([1.0, 0.0, 1.0, 0.0, 0.0]);
        let den = poly([-1.0, 1.0, 0.0, 0.0, 0.0]);
        let mut rem = Poly::one();
        divide(&num, &den, 2, 1, &mut rem);
        assert_eq!(rem.c[0], 2.0);
        assert_eq!(rem.denom, 1.0);
        // Leading coefficient 2: everything below is scaled by 2 per step.
        let den = poly([-1.0, 2.0, 0.0, 0.0, 0.0]);
        divide(&num, &den, 2, 1, &mut rem);
        assert_eq!(rem.denom, 4.0);
        assert_eq!(rem.c[0] / rem.denom, 1.25); // p(0.5) = 1.25
    }
}
