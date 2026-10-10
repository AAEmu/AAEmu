//! The C runtime math the engine imports from `MSVCR100.dll` (x64).
//!
//! The physics DLL calls `sinf`/`cosf` (≈55/51 call sites, including the exact-rotation branch
//! of rigid-body integration), `asinf`, `atan2f`, `acosf`, `tanf`, `atanf`, `expf`, `logf`, `powf`
//! and the double `sin`, `cos`, `exp`, `log`, `asin`, `atan2`. MSVCR100's x64 implementations are
//! **not** correctly rounded: measured against the real DLL (oracle `--math`, 20,000 samples
//! each), `(x as f64).sin() as f32` differs from MSVCR100 `sinf` in 0.12 % of the cases,
//! `cosf` 0.13 %, `tanf` 0.02 %, `asinf` 6–7 %, `acosf` 2 %, `expf`/`logf` < 0.01 %; the double
//! `sin`/`cos`/`exp`/`asin` differ from a correctly rounded libm in ≈1 % of the cases.
//!
//! * [`sinf`] and [`cosf`] are bit-exact ports of MSVCR100's algorithm for |x| < 500000
//!   (single argument widened to double, Cody–Waite reduction by π/2 in one or two stages,
//!   Taylor/minimax polynomials, one rounding to single at the end). Larger arguments (the CRT
//!   switches to a Payne–Hanek reduction) fall back to the double library function.
//! * The other functions are **not yet bit-exact**: they evaluate in double and round once.
//!   Their known mismatch rates are asserted by `tests/math_oracle.rs`; a port that needs
//!   exactness for one of them must replace it here (open item, see the crate README).

/// `|x| <= π/4` (bits of the double 0.7853981633974483).
const PIO4_BITS: u64 = 0x3FE9_21FB_5444_2D18;
/// 500000.0: above this the CRT uses its large-argument reduction.
const BIG_ARG_BITS: u64 = 0x411E_8480_0000_0000;
const TWO_BY_PI: f64 = f64::from_bits(0x3FE4_5F30_6DC9_C883);
const PIO2_1: f64 = f64::from_bits(0x3FF9_21FB_5440_0000);
const PIO2_1T: f64 = f64::from_bits(0x3DD0_B461_1A62_6331);
const PIO2_2: f64 = f64::from_bits(0x3DD0_B461_1A60_0000);
const PIO2_2T: f64 = f64::from_bits(0x3BA3_198A_2E03_7073);
const SIXTH: f64 = f64::from_bits(0x3FC5_5555_5555_5555);

// sinf: Taylor coefficients (both the |x| <= π/4 and the reduced-argument sine polynomial).
const SINF_S1: f64 = f64::from_bits(0xBFC5_5555_5555_5555); // -1/6
const SINF_S2: f64 = f64::from_bits(0x3F81_1111_1111_1111); // 1/120
const SINF_S3: f64 = f64::from_bits(0xBF2A_01A0_1A01_A01A); // -1/5040
const SINF_S4: f64 = f64::from_bits(0x3EC7_1DE3_A556_C734); // 1/362880
                                                            // sinf: cosine polynomial for odd quadrants.
const SINF_C1: f64 = f64::from_bits(0x3FA5_5555_5555_5555); // 1/24
const SINF_C2: f64 = f64::from_bits(0xBF56_C16C_16C1_6C16); // -1/720
const SINF_C3: f64 = f64::from_bits(0x3EFA_01A0_1A01_A019); // 1/40320
const SINF_C4: f64 = f64::from_bits(0xBE92_7E4F_B778_9F5C); // -1/3628800
                                                            // cosf: its own (minimax-tuned) coefficients.
const COSF_S1: f64 = f64::from_bits(0xBFC5_5555_5555_5555);
const COSF_S2: f64 = f64::from_bits(0x3F81_1111_1111_0BB3);
const COSF_S3: f64 = f64::from_bits(0xBF2A_01A0_19E8_3E5C);
const COSF_S4: f64 = f64::from_bits(0x3EC7_1DE3_796C_DE01);
const COSF_C1: f64 = f64::from_bits(0x3FA5_5555_5555_5555);
const COSF_C2: f64 = f64::from_bits(0xBF56_C16C_16C1_6967);
const COSF_C3: f64 = f64::from_bits(0x3EFA_01A0_19F4_EC90);
const COSF_C4: f64 = f64::from_bits(0xBE92_7E4F_A17F_65F6);

/// Cody–Waite reduction shared by `sinf`/`cosf`: returns `(n, r, exponent field of the
/// first-stage r)` for a non-negative `x` below 500000.
#[inline]
fn reduce(x: f64, ax_bits: u64) -> (i32, f64, u64) {
    let n = (x * TWO_BY_PI + 0.5) as i32; // cvttpd2dq: truncation
    let dn = f64::from(n);
    let mut r1 = x - PIO2_1 * dn;
    let mut r = r1 - PIO2_1T * dn;
    let rexp = (r.to_bits() << 1) >> 53;
    let diff = (ax_bits >> 52) as i64 - rexp as i64;
    if diff > 15 {
        let t = PIO2_2 * dn;
        let tt = PIO2_2T * dn;
        let r0 = r1;
        r1 -= t;
        let lost = (r0 - r1) - t;
        let tail = tt - lost;
        r = r1 - tail;
    }
    (n, r, rexp)
}

/// MSVCR100 `sinf` (x64), bit-exact for |x| < 500000.
#[must_use]
pub fn sinf(x: f32) -> f32 {
    if x.to_bits() & 0x7F80_0000 == 0x7F80_0000 {
        #[allow(clippy::eq_op)] // inf - inf / NaN - NaN: the x86 default NaN, like the CRT
        return x - x; // NaN / ±inf -> NaN (the CRT also sets errno)
    }
    let xd = f64::from(x);
    let ax = xd.to_bits() & 0x7FFF_FFFF_FFFF_FFFF;
    if ax <= PIO4_BITS {
        if ax < 0x3F80_0000_0000_0000 {
            if ax < 0x3F20_0000_0000_0000 {
                return x;
            }
            return (xd - ((xd * xd) * xd) * SIXTH) as f32;
        }
        let x2 = xd * xd;
        let p = (SINF_S4 * x2 + SINF_S3) * (x2 * x2) + (SINF_S2 * x2 + SINF_S1);
        return (xd + p * (xd * x2)) as f32;
    }
    let neg = xd.to_bits() != ax;
    let xa = if neg { 0.0 - xd } else { xd };
    if ax >= BIG_ARG_BITS {
        return xd.sin() as f32;
    }
    let (n, r, rexp) = reduce(xa, ax);
    let odd = n & 1 != 0;
    let mut res = if rexp >= 0x3F2 {
        let x2 = r * r;
        if odd {
            let x4 = x2 * x2;
            let p = ((SINF_C4 * x2 + SINF_C3) * x4 + (SINF_C2 * x2 + SINF_C1)) * x4;
            (-0.5 * x2 + 1.0) + p
        } else {
            let p = (SINF_S4 * x2 + SINF_S3) * (x2 * x2) + (SINF_S2 * x2 + SINF_S1);
            r + p * (r * x2)
        }
    } else if rexp > 0x3DE {
        let x2 = r * r;
        if odd {
            1.0 - x2 * 0.5
        } else {
            r - (SIXTH * r) * x2
        }
    } else if odd {
        1.0
    } else {
        r
    };
    let half = ((n as u32) >> 1) & 1;
    if half != u32::from(neg) {
        res = 0.0 - res;
    }
    res as f32
}

/// MSVCR100 `cosf` (x64), bit-exact for |x| < 500000.
#[must_use]
pub fn cosf(x: f32) -> f32 {
    if x.to_bits() & 0x7F80_0000 == 0x7F80_0000 {
        #[allow(clippy::eq_op)]
        return x - x;
    }
    let xd = f64::from(x);
    let ax = xd.to_bits() & 0x7FFF_FFFF_FFFF_FFFF;
    let cos_poly = |x2: f64| {
        let x4 = x2 * x2;
        let p = ((COSF_C4 * x2 + COSF_C3) * x4 + (COSF_C2 * x2 + COSF_C1)) * x4;
        p - (0.5 * x2 - 1.0)
    };
    if ax <= PIO4_BITS {
        return cos_poly(xd * xd) as f32;
    }
    let xa = if xd.to_bits() != ax { 0.0 - xd } else { xd };
    if ax >= BIG_ARG_BITS {
        return xd.cos() as f32;
    }
    let (n, r, rexp) = reduce(xa, ax);
    let odd = n & 1 != 0;
    let x2 = r * r;
    let mut res = if rexp >= 0x3F2 {
        if odd {
            let x4 = x2 * x2;
            let p = (COSF_S4 * x2 + COSF_S3) * x4 + (COSF_S2 * x2 + COSF_S1);
            p * (x2 * r) + r
        } else {
            cos_poly(x2)
        }
    } else if rexp > 0x3DE {
        if odd {
            r - (SIXTH * r) * x2
        } else {
            1.0 - x2 * 0.5
        }
    } else if odd {
        r
    } else {
        1.0
    };
    if (n.wrapping_add(1)) & 2 != 0 {
        res = 0.0 - res;
    }
    res as f32
}

/// `tanf` — double evaluation, **not bit-exact** (0.02 % mismatch vs MSVCR100).
#[must_use]
pub fn tanf(x: f32) -> f32 {
    f64::from(x).tan() as f32
}

/// `atanf` — double evaluation (matched MSVCR100 on all 20,000 samples).
#[must_use]
pub fn atanf(x: f32) -> f32 {
    f64::from(x).atan() as f32
}

/// MSVCR100 `asinf` (x64, at 0x797B2D6C in the shipped DLL), bit-exact port (added by P1a:
/// the cylinder buoyancy calls it on its main path).
///
/// Single-precision fdlibm-style evaluation: `|x| < 2^-14` returns `x`; `|x| >= 1` returns
/// `±π/2` for `±1` and NaN (0xFFC00000) otherwise; NaN input returns the quieted NaN. For
/// `|x| < 0.5`: `z = x²`, `x + x·R(z)`; else `z = (1 − |x|)·0.5`, `s = sqrtss(z)`, and the
/// split-`s` reconstruction around π/4. `R(z) = P(z)/Q(z)` with
/// `P = (((−0.013381929 − 0.0039613745·z)·z − 0.056529868)·z + 0.1841616)·z`,
/// `Q = 1.1049696 − 0.8364113·z` (all single precision, constants as raw bits).
#[must_use]
pub fn asinf(x: f32) -> f32 {
    const PIO2: f32 = f32::from_bits(0x3FC9_0FDB);
    const PIO4_HI: f32 = f32::from_bits(0x3F49_0FDA);
    const PIO4_LO: f32 = f32::from_bits(0x33A2_2168);
    const C0: f32 = f32::from_bits(0x3B81_CE6B);
    const C1: f32 = f32::from_bits(0xBC5B_3FE1);
    const C2: f32 = f32::from_bits(0x3D67_8BDD);
    const C3: f32 = f32::from_bits(0x3E3C_94DC);
    const Q0: f32 = f32::from_bits(0x3F8D_6FA5);
    const Q1: f32 = f32::from_bits(0x3F56_1F0D);
    let bits = x.to_bits();
    let neg = bits & 0x8000_0000 != 0;
    let e = ((bits >> 23) & 0xFF) as i32 - 127;
    if bits & 0x7FFF_FFFF > 0x7F80_0000 {
        return f32::from_bits(bits | 0x0040_0000);
    }
    if e < -14 {
        return x;
    }
    if e >= 0 {
        return if x == 1.0 {
            PIO2
        } else if x == -1.0 {
            -PIO2
        } else {
            f32::from_bits(0xFFC0_0000)
        };
    }
    let xa = x.abs();
    let big = e >= -1;
    let (z, s) = if big {
        let z = (1.0 - xa) * 0.5;
        (z, z.sqrt())
    } else {
        (xa * xa, 0.0)
    };
    let p = ((((C1 - z * C0) * z) - C2) * z + C3) * z;
    let q = Q0 - z * Q1;
    let r = p / q;
    let res = if big {
        let df = f32::from_bits(s.to_bits() & 0xFFFF_0000);
        let c = (z - df * df) / (df + s);
        let p2 = ((s * 2.0) * r) - (PIO4_LO - c * 2.0);
        let q2 = PIO4_HI - df * 2.0;
        PIO4_HI - (p2 - q2)
    } else {
        r * xa + xa
    };
    if neg {
        -res
    } else {
        res
    }
}

/// `acosf` — double evaluation, **not bit-exact** (2.0 % mismatch vs MSVCR100).
#[must_use]
pub fn acosf(x: f32) -> f32 {
    f64::from(x).acos() as f32
}

/// `expf` — double evaluation, **not bit-exact** (< 0.01 % mismatch).
#[must_use]
pub fn expf(x: f32) -> f32 {
    f64::from(x).exp() as f32
}

/// `logf` — double evaluation, **not bit-exact** (< 0.01 % mismatch).
#[must_use]
pub fn logf(x: f32) -> f32 {
    f64::from(x).ln() as f32
}

/// `powf` — double evaluation (matched MSVCR100 on all 20,000 samples).
#[must_use]
pub fn powf(x: f32, y: f32) -> f32 {
    f64::from(x).powf(f64::from(y)) as f32
}

/// `atan2f(y, x)` — double evaluation (matched MSVCR100 on all 20,000 samples).
#[must_use]
pub fn atan2f(y: f32, x: f32) -> f32 {
    f64::from(y).atan2(f64::from(x)) as f32
}

/// `sqrtf` is the `sqrtss` instruction (IEEE correctly rounded); the engine inlines it.
#[inline]
#[must_use]
pub fn sqrtf(x: f32) -> f32 {
    x.sqrt()
}

/// Double `sin` — **not bit-exact** (≈1 % of results differ by 1 ulp from MSVCR100).
#[must_use]
pub fn sin(x: f64) -> f64 {
    x.sin()
}

/// Double `cos` — **not bit-exact** (≈1 %).
#[must_use]
pub fn cos(x: f64) -> f64 {
    x.cos()
}

/// Double `exp` — **not bit-exact** (≈0.4 %).
#[must_use]
pub fn exp(x: f64) -> f64 {
    x.exp()
}

/// Double `log` (matched MSVCR100 on all 20,000 samples).
#[must_use]
pub fn log(x: f64) -> f64 {
    x.ln()
}

/// Double `asin` — **not bit-exact** (≈1.2 %).
#[must_use]
pub fn asin(x: f64) -> f64 {
    x.asin()
}

/// Double `atan2(y, x)`.
#[must_use]
pub fn atan2(y: f64, x: f64) -> f64 {
    y.atan2(x)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sinf_cosf_sanity() {
        for i in -2000..2000 {
            let x = i as f32 * 0.01;
            assert!((sinf(x) - x.sin()).abs() < 2e-6, "sinf({x})");
            assert!((cosf(x) - x.cos()).abs() < 2e-6, "cosf({x})");
        }
        assert_eq!(sinf(0.0), 0.0);
        assert_eq!(cosf(0.0), 1.0);
        assert_eq!(sinf(1e-5), 1e-5);
        assert!(sinf(f32::INFINITY).is_nan());
        assert!(cosf(f32::NAN).is_nan());
    }

    #[test]
    fn known_msvcr100_values() {
        // Cases where MSVCR100 and the correctly rounded result differ (from the oracle).
        assert_eq!(sinf(f32::from_bits(0x3F3E_FF88)).to_bits(), 0x3F2D_C3E2);
        assert_eq!(sinf(f32::from_bits(0x407A_2FBC)).to_bits(), 0xBF31_C37F);
        assert_eq!(cosf(f32::from_bits(0x3F54_A370)).to_bits(), 0x3F2C_A6B9);
        assert_eq!(cosf(f32::from_bits(0x4015_BC3C)).to_bits(), 0xBF31_FE42);
    }
}
