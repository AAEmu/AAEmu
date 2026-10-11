//! Scalar idioms of the original engine.
//!
//! The x64 build is SSE scalar code. Several "helpers" are written as branch-free arithmetic, and
//! the result of that arithmetic differs (in the last ulp, or for `-0.0`) from the obvious Rust
//! function. Port each call site with the idiom the binary uses, never with `f32::max`/`min`.
//!
//! **Precision.** MSVC x64 evaluates `float * 0.5` in single precision when the literal is a
//! `float`, but promotes to double (`cvtps2pd … mulsd … cvtsd2ss`) when the source used a double
//! literal or a double-typed helper. Hex-Rays shows the latter as `double` locals. The binary has
//! about 8,600 `mulsd`, so this is common: check the disassembly of every site that shows a
//! `double` and use the `f64` path ([`f64`] arithmetic followed by `as f32`) where the binary does.

/// `max(a, b)` written as `(|a - b| + (a + b)) * 0.5` (all single precision).
///
/// Seen e.g. in `CGeomManager::RegisterGeometry` (0x3514AE40). Not bit-identical to
/// [`f32::max`]: the sum can round.
#[inline]
#[must_use]
pub fn max_idiom(a: f32, b: f32) -> f32 {
    ((a - b).abs() + (a + b)) * 0.5
}

/// `min(a, b)` written as `((a + b) - |a - b|) * 0.5` (all single precision).
#[inline]
#[must_use]
pub fn min_idiom(a: f32, b: f32) -> f32 {
    ((a + b) - (a - b).abs()) * 0.5
}

/// `max(x, 0)` written as `(|x| + x) * 0.5`.
#[inline]
#[must_use]
pub fn max0_idiom(x: f32) -> f32 {
    (x.abs() + x) * 0.5
}

/// `min(x, 0)` written as `(x - |x|) * 0.5`.
#[inline]
#[must_use]
pub fn min0_idiom(x: f32) -> f32 {
    (x - x.abs()) * 0.5
}

/// Sign bit of a float as an integer: 1 for negative numbers **and `-0.0`**, else 0
/// (`COERCE_UNSIGNED_INT(x) >> 31`).
#[inline]
#[must_use]
pub fn isneg(x: f32) -> u32 {
    x.to_bits() >> 31
}

/// `1 - isneg(x)`.
#[inline]
#[must_use]
pub fn isnonneg(x: f32) -> u32 {
    1 - isneg(x)
}

/// Sign of an integer as the engine computes it: `x >> 31` (arithmetic), i.e. -1 or 0.
#[inline]
#[must_use]
pub fn isneg_i(x: i32) -> i32 {
    x >> 31
}

/// Non-zero sign of a float: -1 for negative numbers and `-0.0`, else +1.
///
/// Binary form: `((bits >> 30) & ~1) + 1` on the signed integer bits (grid ray traversal
/// 0x351AD6A0 and many others).
#[inline]
#[must_use]
pub fn sgnnz(x: f32) -> i32 {
    ((x.to_bits() as i32 >> 30) & !1) + 1
}

/// Non-zero sign of an integer: `((x >> 31) << 1) + 1`.
#[inline]
#[must_use]
pub fn sgnnz_i(x: i32) -> i32 {
    ((x >> 31) << 1) + 1
}

/// Branch-free integer `min(a, b)`: `a + ((b - a) & ((b - a) >> 31))` (wrapping, as in the binary).
#[inline]
#[must_use]
pub fn min_i(a: i32, b: i32) -> i32 {
    let d = b.wrapping_sub(a);
    a.wrapping_add(d & (d >> 31))
}

/// Branch-free integer `max(a, b)`: `a - ((a - b) & ((a - b) >> 31))` (wrapping, as in the binary).
#[inline]
#[must_use]
pub fn max_i(a: i32, b: i32) -> i32 {
    let d = a.wrapping_sub(b);
    a.wrapping_sub(d & (d >> 31))
}

/// `x * x`.
#[inline]
#[must_use]
pub fn sqr(x: f32) -> f32 {
    x * x
}

/// The `+ 12582912.0` (1.5 * 2^23) rounding trick: rounds to the nearest integer, ties to even,
/// for |x| < 2^22. Result = `bits(x + 12582912.0f) - 0x4B400000`.
#[inline]
#[must_use]
pub fn round_magic(x: f32) -> i32 {
    ((x + 12_582_912.0f32).to_bits() as i32).wrapping_sub(0x4B40_0000)
}

/// `round_magic(x - 0.5)`: the engine's floor for grid coordinates (exact floor except at
/// exact integers + ties, where it follows round-half-even of `x - 0.5`).
#[inline]
#[must_use]
pub fn floor_magic(x: f32) -> i32 {
    round_magic(x - 0.5)
}

/// Round `x` to a multiple of `step` with the magic-number trick, as world `TimeStep` quantises dt.
#[inline]
#[must_use]
pub fn quantise_magic(x: f32, step: f32) -> f32 {
    round_magic(x / step) as f32 * step
}

/// Float clamp constant the engine uses for "infinite" (`9.9999999e14`, bits 0x58635FA9 = 1482907561; the `0x5866D2A9` in entities.md §0 is a typo).
pub const BIG_F32: f32 = f32::from_bits(0x5863_5FA9);

/// `1e10`, the engine's "no hit yet" distance.
pub const LARGE_DIST: f32 = 1e10;

/// The f32 nearest to `1e-20` (`9.9999997e-21`), used by quotient comparisons.
pub const QUOTIENT_EPS: f32 = 1e-20;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn min_max_idioms() {
        assert_eq!(max_idiom(1.0, 2.0), 2.0);
        assert_eq!(min_idiom(1.0, 2.0), 1.0);
        assert_eq!(max0_idiom(-3.0), 0.0);
        assert_eq!(max0_idiom(3.0), 3.0);
        assert_eq!(min0_idiom(-3.0), -3.0);
        // Not bit-identical to f32::max: (|a-b| + (a+b)) * 0.5 rounds (cancellation here).
        let (a, b) = (f32::from_bits(3_291_926_880), f32::from_bits(976_627_652));
        assert_eq!(a.max(b), b);
        assert_ne!(max_idiom(a, b), b);
        assert_eq!(max_idiom(a, b), 0.000_671_386_7);
    }

    #[test]
    fn sign_tricks() {
        assert_eq!(isneg(-0.0), 1);
        assert_eq!(isneg(0.0), 0);
        assert_eq!(isneg(-2.0), 1);
        assert_eq!(isnonneg(-2.0), 0);
        assert_eq!(sgnnz(-0.0), -1);
        assert_eq!(sgnnz(0.0), 1);
        assert_eq!(sgnnz(-5.0), -1);
        assert_eq!(sgnnz(5.0), 1);
        assert_eq!(sgnnz_i(-7), -1);
        assert_eq!(sgnnz_i(0), 1);
        assert_eq!(isneg_i(-7), -1);
        assert_eq!(min_i(3, -4), -4);
        assert_eq!(max_i(3, -4), 3);
        assert_eq!(min_i(i32::MAX, 1), 1);
    }

    #[test]
    fn magic_rounding() {
        assert_eq!(round_magic(2.5), 2);
        assert_eq!(round_magic(3.5), 4);
        assert_eq!(round_magic(-2.5), -2);
        assert_eq!(round_magic(-2.6), -3);
        assert_eq!(floor_magic(2.7), 2);
        assert_eq!(floor_magic(-0.3), -1);
        assert_eq!(floor_magic(3.0), 2); // round-half-even of 2.5: an exact integer floors one low
        assert_eq!(BIG_F32.to_bits(), 0x5863_5FA9);
        assert_eq!(BIG_F32, 1e15); // 9.9999999e14 and 1e15 round to the same f32
        assert_eq!(QUOTIENT_EPS.to_bits(), 0x1E3C_E508);
    }
}
