//! Bit-exact ports of the MSVCR100 (x64) **double** math functions the polynomial root finder
//! calls: `exp`, `log`, `sin`, `cos`, `atan2` (the closed-form cubic, see [`super`]).
//!
//! `crate::math::crt` evaluates these with the Rust/UCRT library, which differs from MSVCR100
//! in ≈1 % of the results (README "Same CRT"); the cubic's cube roots (`exp(log(|v|)/3)`) and its
//! trigonometric branch reach those differences directly, so this module ports the five
//! functions from `MSVCR100.dll` itself (AMD-libm–style table methods; disassembly at the
//! addresses given per function, tables copied as raw bits). They are private to the `poly`
//! group because `math/crt.rs` is owned elsewhere — the crate owner may move them there.
//!
//! Scope: every path is ported except the large-argument reduction of `sin`/`cos`
//! (|x| ≥ 500000, which calls `__remainder_piby2`); those arguments fall back to the library
//! functions. The root finder only calls `sin`/`cos` on `atan2(·)/3 ∈ [0, π/3]` (or NaN).
//! Error-handler paths return what the oracle measured: NaN in → the same NaN quieted, invalid
//! operation → the default NaN `0xFFF8000000000000`, overflow → `+inf`, underflow → `+0`.

#![allow(clippy::unreadable_literal, clippy::excessive_precision)]

use super::super::common::le_or_nan_d;

include!("crt_tables.rs");

const EXP_MASK: u64 = 0x7FF0_0000_0000_0000;
const ABS_MASK: u64 = 0x7FFF_FFFF_FFFF_FFFF;
const SIGN_MASK: u64 = 0x8000_0000_0000_0000;
const MANT_MASK: u64 = 0x000F_FFFF_FFFF_FFFF;
const QUIET_BIT: u64 = 0x0008_0000_0000_0000;
const DEFAULT_NAN: u64 = 0xFFF8_0000_0000_0000;

#[inline]
fn d(bits: u64) -> f64 {
    f64::from_bits(bits)
}

// ------------------------------------------------------------------------------------- exp

/// MSVCR100 `exp` (x64, 0x797F2010 in the reference image): `x = (64·m + j)·ln2/64 + r`,
/// `exp(x) = 2^m · 2^(j/64) · (1 + p(r))` with a 64-entry table split in full / head / tail.
#[must_use]
pub fn exp(x: f64) -> f64 {
    let ux = x.to_bits();
    if ux & EXP_MASK == EXP_MASK {
        if ux == 0x7FF0_0000_0000_0000 {
            return x;
        }
        if ux == 0xFFF0_0000_0000_0000 {
            return 0.0;
        }
        return d(ux | QUIET_BIT);
    }
    // signed 64-bit compare of |x| bits with 2^-63
    if ((ux & ABS_MASK) as i64) < 0x3C00_0000_0000_0000 {
        return 1.0;
    }
    let t = d(0x4057_1547_652B_82FE) * x; // 64/ln2
    if t > 65536.0 {
        return f64::INFINITY; // overflow (_handle_error returns +inf)
    }
    if le_or_nan_d(t, -68800.0) {
        return 0.0; // underflow
    }
    let n = t.round_ties_even() as i32; // cvtpd2dq (round to nearest even)
    let dn = f64::from(n);
    let r1 = x - d(0x3F86_2E42_FEFA_0000) * dn;
    let r2 = dn * d(0xBD1C_F79A_BC9E_3B39);
    let j = (n & 0x3F) as usize;
    let m = (n - j as i32) >> 6;
    let r = r1 + r2;
    let rr = r * r;
    let mut a0 = 0.5 * r;
    let mut a3 = d(0x3FA5_5555_5555_5555) * r; // 1/24
    let mut a4 = d(0x3F56_C16C_16C1_6C17) * r; // 1/720
    let r3 = rr * r;
    a0 += 1.0;
    a3 += d(0x3FC5_5555_5555_5555); // 1/6
    let r5 = rr * r3;
    a4 += d(0x3F81_1111_1111_1111); // 1/120
    a0 *= r;
    a3 *= r3;
    a4 *= r5;
    let q = a0 + (a3 + a4);
    // rcx = (zero-extended m + 0x3FF) << 52
    let scale = (u64::from(m as u32).wrapping_add(0x3FF)) << 52;
    let mut z = q * d(EXP_TWO_TO_JBY64[j]);
    z += d(EXP_TWO_TO_JBY64_TAIL[j]);
    z += d(EXP_TWO_TO_JBY64_HEAD[j]);
    if scale == EXP_MASK {
        return d(z.to_bits() | 0x7FE0_0000_0000_0000);
    }
    if m > -1022 {
        return z * d(scale);
    }
    // denormal result
    let r11 = if z >= 1.0 { m } else { 0 };
    if r11 == -1022 {
        return z * d(scale);
    }
    if (ux as i64) > (0xC087_4046_DFEF_D9D0u64 as i64) {
        return d(1);
    }
    let mut c = m.wrapping_add(0x432);
    if c < 0 {
        c = 0;
    }
    z * d(1u64 << (c as u32 & 63))
}

// ------------------------------------------------------------------------------------- log

/// MSVCR100 `log` (x64, 0x797F2BC0): `x = 2^e·2y`, `y ∈ [0.5,1)`, `F` = `y` rounded to 8
/// mantissa bits, `log x = e·ln2 + log(2F) + log1p(−(F−y)/F)` (257-entry tables); a separate
/// rational approximation for `|x − 1| < 1/16`.
#[must_use]
pub fn log(x: f64) -> f64 {
    let ux = x.to_bits();
    // psrlq 52 keeps the sign bit; the low dword is converted
    let mut e = ((ux >> 52) as i64 - 0x3FF) as i32;
    let mut ef = f64::from(e);
    if ux & EXP_MASK == EXP_MASK {
        if ux == 0x7FF0_0000_0000_0000 {
            return x;
        }
        if ux == 0xFFF0_0000_0000_0000 {
            return d(DEFAULT_NAN);
        }
        return d(ux | QUIET_BIT);
    }
    let mut mant = ux & MANT_MASK;
    let xm1 = x - 1.0;
    let mut rax = ux;
    if ef == -1023.0 {
        // zero / positive denormal: renormalise
        let t = d(mant | 0x3FF0_0000_0000_0000) - 1.0;
        mant = t.to_bits() & MANT_MASK;
        rax = mant;
        e = ((t.to_bits() >> 52) as u32).wrapping_sub(0x7FD) as i32;
        ef = f64::from(e);
    }
    let fbits = (rax & 0x000F_F000_0000_0000) + ((rax & 0x0000_0800_0000_0000) << 1);
    if xm1.abs() < 0.0625 {
        let f = x - 1.0;
        let u0 = f / (2.0 + f);
        let fu = f * u0;
        let u = u0 + u0;
        let v = u * u;
        let mut p1 = d(0x3F89_9999_99BA_C6D4) * v;
        let mut p2 = d(0x3F3C_8034_C85D_FFF0) * v;
        p1 += d(0x3FB5_5555_5555_54E6);
        p2 += d(0x3F62_4923_07F1_519F);
        let u3 = v * u;
        p1 *= u3;
        let u6 = u3 * u3;
        let u7 = u6 * u;
        p2 *= u7;
        let s = (p1 + p2) - fu;
        return f + s;
    }
    let idx = (fbits >> 44) as usize;
    let y = d(mant | 0x3FE0_0000_0000_0000);
    let fv = d(fbits | 0x3FE0_0000_0000_0000);
    if le_or_nan_d(x, 0.0) {
        if x == 0.0 {
            return f64::NEG_INFINITY;
        }
        return d(DEFAULT_NAN);
    }
    let r = (fv - y) * d(LOG_INV_F[idx]);
    let r2 = r * r;
    let mut a = d(0x3FC5_5555_5555_5555) * r; // 1/6
    let mut b = d(0x3FD5_5555_5555_5555) * r; // 1/3
    let r4 = r2 * r2;
    a += d(0x3FC9_9999_9999_999A); // 0.2
    b += 0.5;
    a *= r;
    b *= r2;
    a += 0.25;
    b += r;
    a *= r4;
    let poly = b + a;
    let t = d(0x3E6E_FA39_EF35_793C) * ef - poly;
    let z = d(LOG_F_TAIL[idx]) + t;
    let h = d(LOG_F_HEAD[idx]) + d(0x3FE6_2E42_E000_0000) * ef;
    h + z
}

// ------------------------------------------------------------------------------- sin / cos

const PIO4_BITS: u64 = 0x3FE9_21FB_5444_2D18;
const SIXTH: u64 = 0x3FC5_5555_5555_5555;
const S1: u64 = 0xBFC5_5555_5555_5555;
const S2: u64 = 0x3F81_1111_1111_0BB3;
const S3: u64 = 0xBF2A_01A0_19E8_3E5C;
const S4: u64 = 0x3EC7_1DE3_796C_DE01;
const S5: u64 = 0xBE5A_E600_B42F_DFA7;
const S6: u64 = 0x3DE5_E0B2_F9A4_3BB8;
const C1: u64 = 0x3FA5_5555_5555_5555;
const C2: u64 = 0xBF56_C16C_16C1_6967;
const C3: u64 = 0x3EFA_01A0_19F4_EC91;
const C4: u64 = 0xBE92_7E4F_A17F_667B;
const C5: u64 = 0x3E21_EEB6_9038_2EEC;
const C6: u64 = 0xBDA9_07DB_4725_8AA7;

/// Cody–Waite reduction by π/2 shared by `sin`/`cos` for `0 < x < 500000`: returns
/// `(n, r, rr, exponent field of the first-stage r)`.
fn reduce_piby2(x: f64, ax: u64) -> (i32, f64, f64, u64) {
    let n = (x * d(0x3FE4_5F30_6DC9_C883) + 0.5) as i32; // cvttpd2dq
    let dn = f64::from(n);
    let mut r1 = x - d(0x3FF9_21FB_5440_0000) * dn;
    let mut rt = d(0x3DD0_B461_1A62_6331) * dn;
    let mut r = r1 - rt;
    let rexp = (r.to_bits() << 1) >> 53;
    if (ax >> 52) as i64 - rexp as i64 > 15 {
        let t = d(0x3DD0_B461_1A60_0000) * dn;
        let tt = d(0x3BA3_198A_2E03_7073) * dn;
        let r0 = r1;
        r1 -= t;
        let lost = (r0 - r1) - t;
        rt = tt - lost;
        r = r1 - rt;
    }
    let rr = (r1 - r) - rt;
    (n, r, rr, rexp)
}

/// sine polynomial with the reduction tail `rr`.
fn sin_tail(r: f64, rr: f64) -> f64 {
    let x2 = r * r;
    let mut a = d(S6) * x2;
    let x4 = x2 * x2;
    let mut b = d(S3) * x2;
    a += d(S5);
    let x6 = x4 * x2;
    a *= x2;
    let r3 = r * x2;
    b += d(S2);
    b *= x2;
    a += d(S4);
    let h = (x2 * 0.5) * rr;
    a *= x6;
    b += d(S1);
    a += b;
    let t = r3 * a - h;
    (rr + t) + r
}

/// cosine polynomial with the reduction tail `rr`.
fn cos_tail(r: f64, rr: f64) -> f64 {
    let x2 = r * r;
    let t = rr * r;
    let mut c5 = 0.5 * x2;
    let mut p1 = d(C6) * x2;
    let mut p0 = d(C3) * x2;
    c5 -= 1.0;
    let x4 = x2 * x2;
    p1 += d(C5);
    p0 += d(C2);
    c5 += 1.0;
    let x6 = x4 * x2;
    p1 *= x2;
    p0 *= x2;
    let mut h = x2 * 0.5;
    p1 += d(C4);
    p0 += d(C1);
    let x4b = x2 * x2;
    c5 -= h;
    p1 *= x6;
    p0 += p1;
    h -= 1.0;
    c5 -= t;
    p0 *= x4b;
    p0 += c5;
    p0 - h
}

/// MSVCR100 `sin` (x64, 0x797F4AB0).
#[must_use]
pub fn sin(x: f64) -> f64 {
    let ux = x.to_bits();
    if ux & EXP_MASK == EXP_MASK {
        return if ux & MANT_MASK != 0 {
            d(ux | QUIET_BIT)
        } else {
            d(DEFAULT_NAN)
        };
    }
    let ax = ux & ABS_MASK;
    if ax <= PIO4_BITS {
        if ax < 0x3F20_0000_0000_0000 {
            if ax < 0x3E40_0000_0000_0000 {
                return x;
            }
            return x - ((x * x) * x) * d(SIXTH);
        }
        let x2 = x * x;
        let mut a = d(S6) * x2;
        let mut b = d(S3) * x2;
        let x4 = x2 * x2;
        a += d(S5);
        let x6 = x4 * x2;
        a *= x2;
        let x3 = x * x2;
        b += d(S2);
        b *= x2;
        a += d(S4);
        a *= x6;
        b += d(S1);
        a += b;
        return x + x3 * a;
    }
    let neg = ux != ax;
    let xp = if neg { 0.0 - x } else { x };
    if ax >= 0x411E_8480_0000_0000 {
        // __remainder_piby2 path: not ported (unreachable from the root finder)
        return x.sin();
    }
    let (n, r, rr, rexp) = reduce_piby2(xp, ax);
    let odd = n & 1 != 0;
    let v = if rexp >= 0x3F2 {
        if odd {
            cos_tail(r, rr)
        } else {
            sin_tail(r, rr)
        }
    } else if rexp > 0x3DE {
        let x2 = r * r;
        if odd {
            1.0 - x2 * 0.5
        } else {
            r - (d(SIXTH) * r) * x2
        }
    } else if odd {
        1.0
    } else {
        r
    };
    if (n >> 1) & 1 == i32::from(neg) {
        v
    } else {
        0.0 - v
    }
}

/// MSVCR100 `cos` (x64, 0x797F22B0).
#[must_use]
pub fn cos(x: f64) -> f64 {
    let ux = x.to_bits();
    if ux & EXP_MASK == EXP_MASK {
        return if ux & MANT_MASK != 0 {
            d(ux | QUIET_BIT)
        } else {
            d(DEFAULT_NAN)
        };
    }
    let ax = ux & ABS_MASK;
    if ax <= PIO4_BITS {
        if ax < 0x3F20_0000_0000_0000 {
            if ax < 0x3E40_0000_0000_0000 {
                return 1.0;
            }
            return 1.0 - (x * x) * 0.5;
        }
        let x2 = x * x;
        let mut p1 = d(C2) * x2;
        let x4 = x2 * x2;
        let mut p3 = d(C4) * x2;
        let mut p5 = d(C6) * x2;
        let x8 = x4 * x4;
        p1 += d(C1);
        p1 *= x4;
        p3 += d(C3);
        let h = x2 * -0.5;
        p5 += d(C5);
        p3 *= x8;
        let x12 = x4 * x8;
        let p5 = x12 * p5;
        p1 += p3;
        let t = h + 1.0;
        let mut z = 1.0 - t;
        z += h;
        p1 += p5;
        z += p1;
        return z + t;
    }
    let xp = if ux != ax { 0.0 - x } else { x };
    if ax >= 0x411E_8480_0000_0000 {
        // __remainder_piby2 path: not ported (unreachable from the root finder)
        return x.cos();
    }
    let (n, r, rr, rexp) = reduce_piby2(xp, ax);
    let odd = n & 1 != 0;
    let v = if rexp >= 0x3F2 {
        if odd {
            sin_tail(r, rr)
        } else {
            cos_tail(r, rr)
        }
    } else if rexp > 0x3DE {
        let x2 = r * r;
        if odd {
            r - (d(SIXTH) * r) * x2
        } else {
            1.0 - x2 * 0.5
        }
    } else if odd {
        r
    } else {
        1.0
    };
    if n.wrapping_add(1) & 2 != 0 {
        0.0 - v
    } else {
        v
    }
}

// ------------------------------------------------------------------------------------ atan2

const PI: u64 = 0x4009_21FB_5444_2D18;
const PIBY2: u64 = 0x3FF9_21FB_5444_2D18;
const PIBY4: u64 = 0x3FE9_21FB_5444_2D18;
const THREEPIBY4: u64 = 0x4002_D97C_7F33_21D2;

/// MSVCR100 `atan2(y, x)` (x64, 0x797B323C): special cases by exponent difference, then
/// `atan(u)`, `u = min/max ≤ 1`, from a 241-entry table around `c = k/256` (u > 1/16) or a
/// polynomial with a split-precision residual (u ≤ 1/16), recombined with π/2 and π in head +
/// tail form.
#[must_use]
pub fn atan2(y: f64, x: f64) -> f64 {
    let ux = x.to_bits();
    let uy = y.to_bits();
    let ax = ux & ABS_MASK;
    let ay = uy & ABS_MASK;
    let xexp = ((ux >> 52) & 0x7FF) as i32;
    let yexp = ((uy >> 52) & 0x7FF) as i32;
    let xneg = ux & SIGN_MASK != 0;
    let yneg = uy & SIGN_MASK != 0;
    let xinf = ax == EXP_MASK;
    let yinf = ay == EXP_MASK;
    let mut diff = yexp - xexp;
    let signed = |v: u64| if yneg { -d(v) } else { d(v) };
    if ax > EXP_MASK {
        return d(ux | QUIET_BIT);
    }
    if ay > EXP_MASK {
        return d(uy | QUIET_BIT);
    }
    if ay == 0 {
        if !xneg {
            return y;
        }
        return signed(PI);
    }
    if ax == 0 && yneg {
        return -d(PIBY2);
    }
    let mut xv = x;
    let mut yv = y;
    if xexp < 0x3FD && yexp < 0x3FD {
        let scale = |u: u64, neg: bool| -> u64 {
            if u & EXP_MASK == 0 {
                let t = d(u | 0x4010_0000_0000_0000);
                if neg {
                    (t + 4.0).to_bits()
                } else {
                    (t - 4.0).to_bits()
                }
            } else {
                u.wrapping_add(0x4000_0000_0000_0000)
            }
        };
        let nx = scale(ux, xneg);
        let ny = scale(uy, yneg);
        xv = d(nx);
        yv = d(ny);
        diff = ((ny >> 52) & 0x7FF) as i32 - ((nx >> 52) & 0x7FF) as i32;
    }
    if diff > 56 {
        return signed(PIBY2);
    }
    if diff < -28 && !xneg {
        if diff < -1074 {
            return if yneg { -0.0 } else { 0.0 };
        }
        if diff < -1022 {
            // denormal result, rounded by hand
            let q = (d(0x4630_0000_0000_0000) * yv / xv).to_bits();
            let sign = q & SIGN_MASK;
            let qa = q & ABS_MASK;
            let e = ((qa >> 52) & 0x7FF) as i32 - 100;
            let mut rb: u64 = 0;
            if e > 0 {
                rb = ((i64::from(e) as u64) << 52) | (qa & 0x800F_FFFF_FFFF_FFFF);
            } else {
                let m = (qa & 0x801F_FFFF_FFFF_FFFF) | 0x0010_0000_0000_0000;
                let sh = 1 - e;
                if sh <= 0x36 {
                    let v = m >> ((sh - 1) as u32 & 63);
                    rb = (v >> 1) + (v & 1);
                }
            }
            return d(rb | sign);
        }
        return yv / xv;
    }
    if diff < -56 && xneg {
        return signed(PI);
    }
    if yinf && xinf {
        return signed(if xneg { THREEPIBY4 } else { PIBY4 });
    }
    if xneg {
        xv = -xv;
    }
    if yneg {
        yv = -yv;
    }
    let swapped = yv > xv;
    if swapped {
        std::mem::swap(&mut xv, &mut yv);
    }
    let u = yv / xv;
    let mut head = 0.0f64;
    let mut tail;
    if u > 0.0625 {
        let k = (u * 256.0 + 0.5) as i64; // cvttsd2si
        let i = (k - 16) as usize;
        let tab_a = d(ATAN_TAB_A[i]);
        head = d(ATAN_TAB_B[i]);
        let e = -((((xv.to_bits() >> 52) & 0x7FF) as i32) - 0x3FF);
        let c = (k as u32 as f64) * d(0x3F70_0000_0000_0000);
        let h = e / 2;
        let p2h = d(((0x3FF + i64::from(h)) as u64) << 52);
        let p2eh = d(((i64::from(e - h) + 0x3FF) as u64) << 52);
        let xs = (p2h * xv) * p2eh;
        let ys = (p2h * yv) * p2eh;
        let xh = d(xs.to_bits() & 0xFFFF_FFFF_F800_0000);
        let xl = xs - xh;
        let mut num = ys - xh * c;
        num -= xl * c;
        let den = c * ys + xs;
        let r = num / den;
        let r2 = r * r;
        let mut p = d(0x3FD5_5555_5555_0877) - r2 * d(0x3FC9_9992_B980_2D7D);
        p *= r2;
        p *= r;
        tail = (r + tab_a) - p;
    } else if 1e-8 > u {
        tail = u;
    } else {
        let xh = d(xv.to_bits() & 0xFFFF_FFFF_0000_0000);
        let uh = d(u.to_bits() & 0xFFFF_FFFF_0000_0000);
        let u2 = u * u;
        let mut res = yv - uh * xh;
        res -= (xv - xh) * uh;
        res -= (u - uh) * xv;
        res /= xv;
        let mut p = d(0x3FBC_7188_39B8_C4EB) - u2 * d(0x3FB7_0C31_92D7_51C2);
        p = d(0x3FC2_4924_82BD_6BE1) - p * u2;
        p = d(0x3FC9_9999_9996_43A3) - p * u2;
        let mut q = d(0x3FD5_5555_5555_5538) - p * u2;
        q *= u2 * u;
        res -= q;
        tail = res + u;
    }
    if swapped {
        head = d(PIBY2) - head;
        tail = d(0x3C91_A626_3314_5C07) - tail;
    }
    if xneg {
        head = d(0x4009_21FB_5000_0000) - head;
        tail = d(0x3E61_10B4_611A_6263) - tail;
    }
    let res = head + tail;
    if yneg {
        -res
    } else {
        res
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sanity() {
        for i in -200..200 {
            let v = f64::from(i) * 0.037;
            assert!((exp(v) - v.exp()).abs() <= v.exp() * 1e-15, "exp {v}");
            assert!((sin(v) - v.sin()).abs() < 1e-15, "sin {v}");
            assert!((cos(v) - v.cos()).abs() < 1e-15, "cos {v}");
            let w = v.abs() + 1e-3;
            assert!((log(w) - w.ln()).abs() < 1e-15, "log {w}");
            assert!((atan2(v, 0.7) - v.atan2(0.7)).abs() < 1e-15, "atan2 {v}");
            assert!(
                (atan2(0.3, v) - 0.3f64.atan2(v)).abs() < 1e-15,
                "atan2 x {v}"
            );
        }
    }
}
