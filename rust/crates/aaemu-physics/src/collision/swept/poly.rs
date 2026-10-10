//! P2b work group `poly`: the polynomial root finders shared by several unprojection functions
//! (and by `CCylinderGeom` vf11), plus the rotational ray–sphere unprojection.
//!
//! # Polynomial API (stable — other groups call it)
//!
//! The engine stores polynomials as `polynomial_tpl<double, N>`: one `double` *denominator*
//! followed by `N + 1` coefficients in ascending order (`data[k]` multiplies `x^k`). There is no
//! degree field; every routine takes the degree as an argument. [`Poly4`] mirrors the `N = 4`
//! layout (48 bytes); the internal `N = 3` instantiation uses the same struct with `data[4]`
//! unused. All callers in the binary build the quartic, then call
//!
//! ```text
//! if sub_3514A2E0(&poly, lo, hi) != 0 {               // count_roots(&poly, lo, hi)
//!     n = sub_35149170(&poly, lo, hi, roots, 20, 4, 0) // find_roots(&poly, lo, hi, &mut roots, 20)
//! }
//! ```
//!
//! * [`find_roots`] — sub_35149170 (x64, `findroots<4>`, also `CCylinderGeom` vf11) called as
//!   `(poly, lo, hi, roots, maxiter, 4, false)`; [`find_roots_deg`] exposes the degree and
//!   "degree known" arguments. Returns the number of roots in `[lo, hi]`, stored ascending at the
//!   front of `roots` (see the function for the exact filtering semantics).
//! * [`count_roots`] — sub_3514A2E0: Sturm-sequence root count of the quartic in `(lo, hi]`.
//! * [`derivative`] — sub_35148830, [`divide`] — sub_351489E0 (helpers of the Sturm chain).
//!
//! # Algorithms
//!
//! `findroots` (sub_35149170 for N = 4, sub_35050630 for N = 3 — byte-for-byte the same code
//! except for the template constant in the general branch, so one port serves both):
//! 1. unless the degree is "known": `eps = 1e-10 · max|c_i|` (max via the `((m−c)+|…|)/2`
//!    idiom), and the degree drops while `|c_deg| ≤ eps` (or NaN), down to 0;
//! 2. closed forms: linear (bug kept: the root is `c0/c1`, not `−c0/c1`), quadratic (root
//!    selection against `[lo, hi]` through float-rounded sign bits), cubic (Cardano: cube roots as
//!    `exp(log|v|/3)`, the three-real-root case through `atan2`, `cos`, `sin`, then a 3-sort),
//!    quartic (Ferrari: the resolvent cubic is solved by the N = 3 instantiation on
//!    `[−1e20, 1e20]`, its first root is used; ±1e-9 tolerances; a 4-sort);
//! 3. degree 0 (or negative) gives no roots; degrees above 4 (general bisection on the
//!    derivative's roots, `maxiter` steps) are unreachable: every caller passes degree 4 and the
//!    storage holds 5 coefficients (this port asserts `degree <= 4`);
//! 4. roots below `lo` (ordered `lo > r`) are skipped from the front, roots with `!(hi >= r)` are
//!    dropped from the back, the rest is moved to the front; returns the count.
//!
//! The CRT calls (`exp`, `log`, `sin`, `cos`, `atan2`) go to MSVCR100; [`crt`] ports them bit
//! for bit (the shared `math::crt` doubles are not exact).
//!
//! `count_roots` (sub_3514A2E0): Sturm chain `p, p', −rem(p, p'), −rem(s1, s2), −rem(s2, s3)`
//! with fixed degrees 4, 3, 2, 1, 0 (the division trims internally but the chain does not);
//! after each of the last two remainders, a denominator above 1e10 in magnitude scales that
//! polynomial by 1e-10. Each `s_i(x)` is evaluated by Horner (`p` starting from `x·0 + c4`)
//! times its denominator; a sign change is the sign bit of the float-rounded product of
//! neighbours (so `−0` counts). Returns `|changes(lo) − changes(hi)|`.

pub mod crt;
pub mod rot_ray_sph;

pub use rot_ray_sph::unproject_rot_ray_sph;

use super::common::{le_or_nan_d, lt_or_nan_d};
use super::UnprojTables;

/// 1e-10 (degree trimming).
const EPS_TRIM: f64 = 1e-10;
/// 1/3 as in the binary.
const THIRD: f64 = f64::from_bits(0x3FD5_5555_5555_5555);
/// 1/6 as in the binary.
const SIXTH: f64 = f64::from_bits(0x3FC5_5555_5555_5555);
/// √3 as in the binary.
const SQRT3: f64 = f64::from_bits(0x3FFB_B67A_E858_4CAA);

/// `polynomial_tpl<double, 4>`: `denom` then the coefficients `data[k]` of `x^k` (48 bytes, the
/// binary layout). For `N = 3` instances `data[4]` is unused.
#[derive(Clone, Copy, Debug, PartialEq)]
#[repr(C)]
pub struct Poly4 {
    /// Denominator (`+0`); only the Sturm evaluation multiplies by it, the root finder ignores it.
    pub denom: f64,
    /// Coefficients in ascending order (`+8`): `data[k]` multiplies `x^k`.
    pub data: [f64; 5],
}

impl Poly4 {
    /// Denominator 1 and the given coefficients (ascending).
    #[must_use]
    pub fn new(data: [f64; 5]) -> Self {
        Poly4 { denom: 1.0, data }
    }
}

impl Default for Poly4 {
    fn default() -> Self {
        Poly4 {
            denom: 1.0,
            data: [0.0; 5],
        }
    }
}

/// Sign bit of the float-rounded value (`cvtsd2ss` + `shr 31`).
#[inline]
fn sbit(x: f64) -> usize {
    ((x as f32).to_bits() >> 31) as usize
}

/// `((bits >> 30) & ~1) + 1` of the float-rounded value: −1.0 if its sign bit is set, else 1.0.
#[inline]
fn sgn1(x: f64) -> f64 {
    if sbit(x) != 0 {
        -1.0
    } else {
        1.0
    }
}

/// Running maximum idiom `m = (|m − c| + (c + m)) · 0.5` over `data[0..=deg]` (starting from
/// `|data[0]|`), times 1e-10.
fn trim_eps(data: &[f64], deg: i32) -> f64 {
    let mut m = data[0].abs();
    for &c in data.iter().take(deg.max(0) as usize + 1).skip(1) {
        m = ((m - c).abs() + (c + m)) * 0.5;
    }
    m * EPS_TRIM
}

/// sub_35148830: derivative of the degree-`n` polynomial `src` into `dst`:
/// `dst.data[j] = (j+1)·src.data[j+1]` for `j < n`, then `dst.denom = src.denom`
/// (`dst.data[n..]` untouched).
pub fn derivative(src: &Poly4, dst: &mut Poly4, n: i32) {
    for j in 0..n.max(0) as usize {
        dst.data[j] = (j as f64 + 1.0) * src.data[j + 1];
    }
    dst.denom = src.denom;
}

/// sub_351489E0: polynomial division `num (degree dn) / den (degree dd)` →
/// quotient `quot` (`data[0..=dn−dd]`), remainder `rem`.
///
/// `rem.data[0..=dn]` = `num.data`, `quot.data[0..=dn−dd]` = 0 (untrimmed degrees); both degrees
/// are then trimmed (`|c_deg| < 1e-10·max|c|` or NaN, down to −1). `rem.denom = num.denom`,
/// `quot.denom = 1`. Then for each leading term (fraction-free, nothing is divided):
/// `quot[k] = den.denom·rem[cur]`, `quot.denom ·= lc`, `quot[k+1..] ·= lc`,
/// `rem[cur−dd+j] = lc·rem[cur−dd+j] − den[j]·rem[cur]` (`j < dd`), `rem[..cur−dd] ·= lc`,
/// `rem.denom ·= lc` with `lc = den.data[dd]`. The leading `rem[cur]` is not cleared.
///
/// # Panics
/// If `dn > 4` or `dd` is not in `0..=4` (the storage has 5 coefficients).
pub fn divide(num: &Poly4, den: &Poly4, quot: &mut Poly4, rem: &mut Poly4, dn: i32, dd: i32) {
    assert!(
        dn <= 4 && (0..=4).contains(&dd),
        "divide: degrees {dn}/{dd}"
    );
    if dn >= 0 {
        let n = dn as usize + 1;
        rem.data[..n].copy_from_slice(&num.data[..n]);
    }
    if dn - dd >= 0 {
        for q in &mut quot.data[..=(dn - dd) as usize] {
            *q = 0.0;
        }
    }
    let eps_n = trim_eps(&num.data, dn);
    let mut tn = dn;
    // comisd |c|, eps; jae → stop
    while tn >= 0 && lt_or_nan_d(num.data[tn as usize].abs(), eps_n) {
        tn -= 1;
    }
    let eps_d = trim_eps(&den.data, dd);
    let mut td = dd;
    while td >= 0 && lt_or_nan_d(den.data[td as usize].abs(), eps_d) {
        td -= 1;
    }
    rem.denom = num.denom;
    quot.denom = 1.0;
    if tn < 0 || td < 0 || tn < td {
        return;
    }
    let ddu = td as usize;
    let lc = den.data[ddu];
    let top = (tn - td) as usize;
    let mut k = tn - td;
    let mut cur = tn as usize;
    for _ in 0..=top {
        let ku = k as usize;
        quot.data[ku] = den.denom * rem.data[cur];
        quot.denom *= lc;
        for j in (ku + 1..=top).rev() {
            quot.data[j] *= lc;
        }
        let lead = rem.data[cur];
        for j in (0..ddu).rev() {
            let i = cur - ddu + j;
            rem.data[i] = lc * rem.data[i] - den.data[j] * lead;
        }
        for j in (0..cur - ddu).rev() {
            rem.data[j] *= lc;
        }
        rem.denom *= lc;
        k -= 1;
        cur = cur.wrapping_sub(1);
    }
}

/// sub_3514A2E0: number of real roots of the quartic `p` between `lo` and `hi` by a Sturm
/// sequence (see the module docs). Returns `|V(lo) − V(hi)|`.
#[must_use]
pub fn count_roots(p: &Poly4, lo: f64, hi: f64) -> i32 {
    // s[0] = p', s[1] = −rem(p, p'), s[2], s[3] (degrees 3, 2, 1, 0)
    let mut s = [Poly4::default(); 4];
    let mut q = Poly4::default();
    derivative(p, &mut s[0], 4);
    {
        let (a, b) = s.split_at_mut(1);
        divide(p, &a[0], &mut q, &mut b[0], 4, 3);
    }
    s[1].denom = -s[1].denom;
    for (i, deg) in [(0usize, 1i32), (1, 0)] {
        let (a, b) = s.split_at_mut(i + 2);
        divide(&a[i], &a[i + 1], &mut q, &mut b[0], deg + 2, deg + 1);
        let r = &mut b[0];
        r.denom = -r.denom;
        if r.denom.abs() > 1e10 {
            for c in &mut r.data[..=deg as usize] {
                *c *= EPS_TRIM;
            }
            r.denom *= EPS_TRIM;
        }
    }
    let changes = |x: f64| -> i32 {
        let d = &p.data;
        let mut prev =
            p.denom * ((((((x * 0.0 + d[4]) * x + d[3]) * x + d[2]) * x + d[1]) * x) + d[0]);
        let mut n = 0i32;
        for (i, si) in s.iter().enumerate() {
            let deg = 3 - i;
            let mut v = si.data[deg];
            for j in (0..deg).rev() {
                v = v * x + si.data[j];
            }
            v *= si.denom;
            n += sbit(v * prev) as i32;
            prev = v;
        }
        n
    };
    (changes(lo) - changes(hi)).wrapping_abs()
}

/// sub_35149170 as every caller uses it: `findroots(p, lo, hi, roots, maxiter, 4, false)`.
/// Writes up to 4 entries of `roots`; returns the number of roots in `[lo, hi]` (ascending at
/// the front of `roots`).
pub fn find_roots(p: &Poly4, lo: f64, hi: f64, roots: &mut [f64; 4], maxiter: i32) -> i32 {
    find_roots_deg(p, lo, hi, roots, maxiter, 4, false)
}

/// sub_35149170 / sub_35050630 `findroots(p, lo, hi, roots, maxiter, degree, degree_known)`.
///
/// `maxiter` only matters for degrees above 4 (unreachable, see the module docs).
///
/// # Panics
/// If `degree > 4`.
pub fn find_roots_deg(
    p: &Poly4,
    lo: f64,
    hi: f64,
    roots: &mut [f64; 4],
    maxiter: i32,
    degree: i32,
    degree_known: bool,
) -> i32 {
    let _ = maxiter;
    assert!(degree <= 4, "findroots: degree {degree} > 4");
    let d = &p.data;
    let mut deg = degree;
    if !degree_known {
        let eps = trim_eps(d, degree);
        // comisd |c|, eps; ja → stop
        while deg > 0 && le_or_nan_d(d[deg as usize].abs(), eps) {
            deg -= 1;
        }
    }
    let n: usize = match deg {
        1 => {
            // Bug kept: the root of c0 + c1·x is −c0/c1; the binary stores c0/c1.
            roots[0] = d[0] / d[1];
            1
        }
        2 => solve_quadratic(d, lo, hi, roots),
        3 => solve_cubic(d, roots),
        4 => match solve_quartic(d, roots) {
            Some(n) => n,
            None => return 0,
        },
        _ => 0,
    };
    filter_roots(roots, n, lo, hi)
}

/// Common tail of findroots: skip leading roots with `lo > r` (ordered), drop trailing roots
/// with `!(hi >= r)`, move the rest to the front.
fn filter_roots(roots: &mut [f64; 4], n: usize, lo: f64, hi: f64) -> i32 {
    let mut first = 0usize;
    while first < n && lo > roots[first] {
        first += 1;
    }
    let mut last = n;
    while last > first && lt_or_nan_d(hi, roots[last - 1]) {
        last -= 1;
    }
    for i in first..last {
        roots[i - first] = roots[i];
    }
    (last - first) as i32
}

/// Degree-2 case of findroots.
fn solve_quadratic(d: &[f64; 5], lo: f64, hi: f64, roots: &mut [f64; 4]) -> usize {
    let s = sgn1(d[2]);
    let a = d[2] * s;
    let b = s * d[1];
    let disc = b * b - ((s * d[0]) * a) * 4.0;
    let bh = (a * hi) * 2.0 + b;
    let bl = (a * lo) * 2.0 + b;
    let sg = sgn1(bh * bl);
    let mut sq = [bl * bl, bh * bh];
    let i = sbit(hi.abs() - lo.abs());
    sq[i] *= sg;
    let t = ((sq[1] + sq[0]) - disc * 2.0).abs() - (sq[0] - sq[1]).abs();
    // roots iff the float sign of t is set and that of disc is clear
    if !(sbit(t) == 1 && sbit(disc) == 0) {
        return 0;
    }
    let sd = disc.sqrt();
    let k = 0.5 / a;
    let r1 = (-b - sd) * k;
    let r2 = (sd - b) * k;
    let w = (lo - hi).abs();
    let mid = lo + hi;
    roots[0] = r1;
    let i = sbit((mid - r1 * 2.0).abs() - w);
    roots[i] = r2;
    i + sbit((mid - r2 * 2.0).abs() - w)
}

/// `exp(log|v|·k)·sign(v)` if `|v| > 1e-20`, else `v`.
fn signed_root(v: f64, k: f64) -> f64 {
    if v.abs() > 1e-20 {
        let s = sgn1(v);
        crt::exp(crt::log(v.abs()) * k) * s
    } else {
        v
    }
}

/// The 3-element sorting network of findroots (largest to slot 2, then order slots 0/1).
fn sort3(r: &mut [f64; 4]) {
    let c = sbit(r[0] - r[1]);
    let e = sbit(r[c] - r[2]);
    let c2 = c | (e << 1);
    let idx = (((c2 ^ 2) | 4) >> 1) & c2;
    r.swap(idx, 2);
    let i = sbit(r[0] - r[1]);
    r.swap(i, 1);
}

/// Degree-3 case of findroots (Cardano).
fn solve_cubic(d: &[f64; 5], roots: &mut [f64; 4]) -> usize {
    let inv = 1.0 / d[3];
    let a = inv * d[2];
    let b = inv * d[1];
    let c = inv * d[0];
    let a3 = a * THIRD;
    let q = (a3 * b - c) * 0.5 - (a3 * a3) * a3;
    let pp = (b - a3 * a) * THIRD;
    let disc = (pp * pp) * pp + q * q;
    let sq = disc.abs().sqrt();
    if disc > 0.0 {
        let u = signed_root(sq + q, THIRD);
        let v = signed_root(q - sq, THIRD);
        roots[0] = (v + u) - a3;
        return 1;
    }
    let ang = crt::atan2(sq, q);
    let r = signed_root(sq * sq + q * q, SIXTH);
    let ang3 = ang * THIRD;
    let cs = crt::cos(ang3) * r;
    let sn = (crt::sin(ang3) * r) * SQRT3;
    roots[0] = cs * 2.0 - a3;
    roots[1] = (sn - cs) - a3;
    roots[2] = (-cs - sn) - a3;
    sort3(roots);
    3
}

/// Degree-4 case of findroots (Ferrari). `None`: the resolvent cubic had no root, or the
/// degenerate branch failed — findroots returns 0 at once (without the final filtering).
fn solve_quartic(d: &[f64; 5], roots: &mut [f64; 4]) -> Option<usize> {
    let inv = 1.0 / d[4];
    let dd = inv * d[0];
    let c = inv * d[1];
    let a = inv * d[3];
    let b = inv * d[2];
    let d4 = dd * 4.0;
    let a2 = a * a;
    let resolvent = Poly4 {
        denom: 1.0,
        data: [((b * 4.0) * dd - c * c) - a2 * dd, c * a - d4, -b, 1.0, 0.0],
    };
    let mut yr = [0.0f64; 4];
    if find_roots_deg(&resolvent, -1e20, 1e20, &mut yr, 20, 3, false) == 0 {
        return None;
    }
    let y = yr[0];
    let r2 = a2 * 0.25 - b + y;
    // comisd r2, −1e-9; jbe → no roots
    if le_or_nan_d(r2, -1e-9) {
        return Some(0);
    }
    let (rr, e, dt);
    if r2 >= 1e-9 {
        rr = (((-r2).abs() + r2) * 0.5).sqrt();
        dt = ((((a * 4.0) * b - c * 8.0) - a2 * a) / rr) * 0.25;
        e = (a2 * 0.75 - rr * rr) - b * 2.0;
    } else {
        // Bug kept (?): R stays the tiny R² value instead of its square root.
        rr = r2;
        e = a2 * 0.75 - b * 2.0;
        let t = y * y - d4;
        // comisd t, −1e-9; jb → findroots returns 0
        if lt_or_nan_d(t, -1e-9) {
            return None;
        }
        dt = (((-t).abs() + t) * 0.5).sqrt() * 2.0;
    }
    let mut n = 0usize;
    let f1 = e + dt;
    if f1 > -1e-9 {
        n = 2;
        let s = (((-f1).abs() + f1) * 0.5).sqrt();
        let a4 = a * 0.25;
        roots[0] = (rr - s) * 0.5 - a4;
        roots[1] = (s + rr) * 0.5 - a4;
    }
    let f2 = e - dt;
    if f2 > -1e-9 {
        let m = a * -0.25;
        let s = (((-f2).abs() + f2) * 0.5).sqrt();
        roots[n] = m - (s + rr) * 0.5;
        roots[n + 1] = m - (rr - s) * 0.5;
        n += 2;
    }
    if n == 4 {
        let c0 = sbit(roots[0] - roots[1]);
        let e0 = sbit(roots[c0] - roots[2]);
        let c2 = c0 | (e0 << 1);
        let idx = (((c2 ^ 2) | 4) >> 1) & c2;
        if roots[idx] > roots[3] {
            roots.swap(idx, 3);
        }
        sort3(roots);
    }
    Some(n)
}

/// Fill the cells this group owns: `[1][ray][sph]` and `[1][sph][ray]`.
pub fn fill(t: &mut UnprojTables) {
    rot_ray_sph::fill(t);
}
