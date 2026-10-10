//! Bit-exact ports of the MSVCR100 (x64) double-precision `exp`, `sin` and `cos` that the
//! polynomial root finder calls (cube roots `exp(log|x|/3)` of Cardano's formula and the
//! trigonometric form of the cubic). `crate::math::crt` only approximates these (on the inputs
//! this code produces ≈0.5 % of `exp` and 2–3 % of `sin`/`cos` results differ by an ulp),
//! which made the cylinder's rim branch differ from the DLL in a few cases per 100 000.
//!
//! Derived from the disassembly of the `MSVCR100.dll` shipped with the game (exports `exp`
//! 0x797F2010, `cos` 0x797F22B0, `sin` 0x797F4AB0 at its preferred base 0x79730000); the
//! tables are copied from the DLL as raw bits.
//!
//! Scope: every finite argument of `exp`; finite `|x| < 500000` for `sin`/`cos` (beyond that
//! the CRT switches to a Payne–Hanek reduction, `sub_797BA668`, which is not ported: those
//! arguments fall back to `f64::sin`/`cos`; the closest-point code only passes `|x| ≤ π/3`).
//! Infinite and NaN arguments go through the CRT's error handler in the original; the port
//! returns the IEEE results (`exp(+inf) = +inf`, `exp(−inf) = 0`, NaN otherwise).

// Tables of `exp` (j = 0..63): 2^(j/64), its leading bits and the tail.
const EXP_T: [u64; 64] = [
    0x3FF0_0000_0000_0000,
    0x3FF0_2C9A_3E77_8061,
    0x3FF0_59B0_D315_8574,
    0x3FF0_8745_1875_9BC8,
    0x3FF0_B558_6CF9_890F,
    0x3FF0_E3EC_32D3_D1A2,
    0x3FF1_1301_D012_5B51,
    0x3FF1_429A_AEA9_2DE0,
    0x3FF1_72B8_3C7D_517B,
    0x3FF1_A35B_EB6F_CB75,
    0x3FF1_D487_3168_B9AA,
    0x3FF2_063B_8862_8CD6,
    0x3FF2_387A_6E75_6238,
    0x3FF2_6B45_65E2_7CDD,
    0x3FF2_9E9D_F51F_DEE1,
    0x3FF2_D285_A6E4_030B,
    0x3FF3_06FE_0A31_B715,
    0x3FF3_3C08_B264_16FF,
    0x3FF3_71A7_373A_A9CB,
    0x3FF3_A7DB_34E5_9FF7,
    0x3FF3_DEA6_4C12_3422,
    0x3FF4_160A_21F7_2E2A,
    0x3FF4_4E08_6061_892D,
    0x3FF4_86A2_B5C1_3CD0,
    0x3FF4_BFDA_D536_2A27,
    0x3FF4_F9B2_769D_2CA7,
    0x3FF5_342B_569D_4F82,
    0x3FF5_6F47_36B5_27DA,
    0x3FF5_AB07_DD48_5429,
    0x3FF5_E76F_15AD_2148,
    0x3FF6_247E_B03A_5585,
    0x3FF6_6238_8255_2225,
    0x3FF6_A09E_667F_3BCD,
    0x3FF6_DFB2_3C65_1A2F,
    0x3FF7_1F75_E8EC_5F74,
    0x3FF7_5FEB_5642_67C9,
    0x3FF7_A114_73EB_0187,
    0x3FF7_E2F3_36CF_4E62,
    0x3FF8_2589_994C_CE13,
    0x3FF8_68D9_9B44_92ED,
    0x3FF8_ACE5_422A_A0DB,
    0x3FF8_F1AE_9915_7736,
    0x3FF9_3737_B0CD_C5E5,
    0x3FF9_7D82_9FDE_4E50,
    0x3FF9_C491_82A3_F090,
    0x3FFA_0C66_7B5D_E565,
    0x3FFA_5503_B23E_255D,
    0x3FFA_9E6B_5579_FDBF,
    0x3FFA_E89F_995A_D3AD,
    0x3FFB_33A2_B84F_15FB,
    0x3FFB_7F76_F2FB_5E47,
    0x3FFB_CC1E_904B_C1D2,
    0x3FFC_199B_DD85_529C,
    0x3FFC_67F1_2E57_D14B,
    0x3FFC_B720_DCEF_9069,
    0x3FFD_072D_4A07_897C,
    0x3FFD_5818_DCFB_A487,
    0x3FFD_A9E6_03DB_3285,
    0x3FFD_FC97_337B_9B5F,
    0x3FFE_502E_E78B_3FF6,
    0x3FFE_A4AF_A2A4_90DA,
    0x3FFE_FA1B_EE61_5A27,
    0x3FFF_5076_5B6E_4540,
    0x3FFF_A7C1_819E_90D8,
];
const EXP_LEAD: [u64; 64] = [
    0x3FF0_0000_0000_0000,
    0x3FF0_2C9A_3000_0000,
    0x3FF0_59B0_D000_0000,
    0x3FF0_8745_1000_0000,
    0x3FF0_B558_6000_0000,
    0x3FF0_E3EC_3000_0000,
    0x3FF1_1301_D000_0000,
    0x3FF1_429A_A000_0000,
    0x3FF1_72B8_3000_0000,
    0x3FF1_A35B_E000_0000,
    0x3FF1_D487_3000_0000,
    0x3FF2_063B_8000_0000,
    0x3FF2_387A_6000_0000,
    0x3FF2_6B45_6000_0000,
    0x3FF2_9E9D_F000_0000,
    0x3FF2_D285_A000_0000,
    0x3FF3_06FE_0000_0000,
    0x3FF3_3C08_B000_0000,
    0x3FF3_71A7_3000_0000,
    0x3FF3_A7DB_3000_0000,
    0x3FF3_DEA6_4000_0000,
    0x3FF4_160A_2000_0000,
    0x3FF4_4E08_6000_0000,
    0x3FF4_86A2_B000_0000,
    0x3FF4_BFDA_D000_0000,
    0x3FF4_F9B2_7000_0000,
    0x3FF5_342B_5000_0000,
    0x3FF5_6F47_3000_0000,
    0x3FF5_AB07_D000_0000,
    0x3FF5_E76F_1000_0000,
    0x3FF6_247E_B000_0000,
    0x3FF6_6238_8000_0000,
    0x3FF6_A09E_6000_0000,
    0x3FF6_DFB2_3000_0000,
    0x3FF7_1F75_E000_0000,
    0x3FF7_5FEB_5000_0000,
    0x3FF7_A114_7000_0000,
    0x3FF7_E2F3_3000_0000,
    0x3FF8_2589_9000_0000,
    0x3FF8_68D9_9000_0000,
    0x3FF8_ACE5_4000_0000,
    0x3FF8_F1AE_9000_0000,
    0x3FF9_3737_B000_0000,
    0x3FF9_7D82_9000_0000,
    0x3FF9_C491_8000_0000,
    0x3FFA_0C66_7000_0000,
    0x3FFA_5503_B000_0000,
    0x3FFA_9E6B_5000_0000,
    0x3FFA_E89F_9000_0000,
    0x3FFB_33A2_B000_0000,
    0x3FFB_7F76_F000_0000,
    0x3FFB_CC1E_9000_0000,
    0x3FFC_199B_D000_0000,
    0x3FFC_67F1_2000_0000,
    0x3FFC_B720_D000_0000,
    0x3FFD_072D_4000_0000,
    0x3FFD_5818_D000_0000,
    0x3FFD_A9E6_0000_0000,
    0x3FFD_FC97_3000_0000,
    0x3FFE_502E_E000_0000,
    0x3FFE_A4AF_A000_0000,
    0x3FFE_FA1B_E000_0000,
    0x3FFF_5076_5000_0000,
    0x3FFF_A7C1_8000_0000,
];
const EXP_TAIL: [u64; 64] = [
    0x0000_0000_0000_0000,
    0x3E6C_EF00_C1DC_DEF9,
    0x3E48_AC2B_A1D7_3E2A,
    0x3E60_EB37_9011_86BE,
    0x3E69_F312_1EC5_3172,
    0x3E46_9E8D_1010_3A17,
    0x3DF2_5B50_A4EB_BF1A,
    0x3E6D_525B_BF66_8203,
    0x3E68_FAA2_F5B9_BEF9,
    0x3E66_DF96_EA79_6D31,
    0x3E36_8B9A_A780_5B80,
    0x3E60_C519_AC77_1DD6,
    0x3E6C_EAC4_70CD_83F5,
    0x3E57_89F3_7495_E99C,
    0x3E54_7F7B_84B0_9745,
    0x3E5B_900C_2D00_2475,
    0x3E64_636E_2A5B_D1AB,
    0x3E43_20B7_FA64_E430,
    0x3E5C_EAA7_2A9C_5154,
    0x3E53_967F_DBA8_6F24,
    0x3E68_2468_446B_6824,
    0x3E3F_72E2_9F84_325B,
    0x3E18_624B_40C4_DBD0,
    0x3E57_04F3_404F_068E,
    0x3E54_D8A8_9C75_0E5E,
    0x3E5A_74B2_9AB4_CF62,
    0x3E5A_753E_077C_2A0F,
    0x3E5A_D49F_699B_B2C0,
    0x3E6A_90A8_52B1_9260,
    0x3E56_B485_21BA_6F93,
    0x3E0D_2AC2_58F8_7D03,
    0x3E42_A911_2489_3ECF,
    0x3E59_FCEF_3242_2CBE,
    0x3E68_CA34_5DE4_41C5,
    0x3E61_D8BE_E7BA_46E1,
    0x3E59_099F_22FD_BA6A,
    0x3E4F_580C_36BE_A881,
    0x3E5B_3D39_8841_740A,
    0x3E62_999C_2515_9F11,
    0x3E66_8925_D901_C83B,
    0x3E41_5506_DADD_3E2A,
    0x3E62_2AEE_6C57_304E,
    0x3E29_B8BC_9E8A_0387,
    0x3E6F_BC9C_9F17_3D24,
    0x3E45_1F84_80E3_E235,
    0x3E66_BBCA_C965_35B5,
    0x3E41_F12A_E45A_1224,
    0x3E55_E7F6_FD0F_AC90,
    0x3E62_B5A7_5ABD_0E69,
    0x3E60_9E2B_F5ED_7FA1,
    0x3E47_DAF2_3755_3D84,
    0x3E12_F074_891E_E83D,
    0x3E6B_0AA5_3844_4196,
    0x3E6C_AFA2_9694_426F,
    0x3E69_DF20_D22A_0797,
    0x3E64_0F12_F71A_1E45,
    0x3E69_F749_0E4B_B40B,
    0x3E4E_D994_2B84_600D,
    0x3E4B_DCDA_F5CB_4656,
    0x3E5E_2CFF_D89C_F44C,
    0x3E45_2486_CC2C_7B9D,
    0x3E6C_C2B4_4EEE_3FA4,
    0x3E66_DC8A_80CE_9F09,
    0x3E39_E90D_82E9_0A7E,
];

/// `64/ln 2`.
const EXP_INV_L: f64 = f64::from_bits(0x4057_1547_652B_82FE);
/// `ln 2/64`, lead and (negative) tail parts.
const EXP_L1: f64 = f64::from_bits(0x3F86_2E42_FEFA_0000);
const EXP_L2: f64 = f64::from_bits(0xBD1C_F79A_BC9E_3B39);
/// 65536 and −68800: the overflow / underflow limits on `x·64/ln 2`.
const EXP_OVF: f64 = f64::from_bits(0x40F0_0000_0000_0000);
const EXP_UNF: f64 = f64::from_bits(0xC0F0_CC00_0000_0000);
const EXP_C2: f64 = f64::from_bits(0x3FE0_0000_0000_0000); // 1/2
const EXP_C3: f64 = f64::from_bits(0x3FC5_5555_5555_5555); // 1/6
const EXP_C4: f64 = f64::from_bits(0x3FA5_5555_5555_5555); // 1/24
const EXP_C5: f64 = f64::from_bits(0x3F81_1111_1111_1111); // 1/120
const EXP_C6: f64 = f64::from_bits(0x3F56_C16C_16C1_6C17); // 1/720
/// Bits of −744.0346068132731 (compared as a signed integer: below it the result is the
/// smallest denormal).
const EXP_TINY_BITS: i64 = 0xC087_4046_DFEF_D9D0_u64 as i64;

/// MSVCR100 `exp` (x64, 0x797F2010).
///
/// `n = round(x·64/ln 2)` (`cvtpd2dq`, ties to even), `j = n & 63`, `m = (n − j) >> 6`,
/// `r = (x − L1·n) + n·L2`, `q = (0.5r + 1)·r + ((r/24 + 1/6)·r³ + (r/720 + 1/120)·r⁵)`,
/// `s = (q·T[j] + Ttail[j]) + Tlead[j]`, result `s·2^m`. Quirks kept: for `m = 1024` the
/// result is `s | 0x7FE0…` (bitwise or, giving inf/NaN bit patterns); for `m ≤ −1022` the scale
/// is a denormal power of two (one rounding) unless `s ≥ 1` and `m = −1022`.
#[must_use]
pub fn exp(x: f64) -> f64 {
    let ux = x.to_bits();
    if ux & 0x7FF0_0000_0000_0000 == 0x7FF0_0000_0000_0000 {
        if x.is_nan() {
            return f64::from_bits(ux | 0x0008_0000_0000_0000);
        }
        return if x > 0.0 { x } else { 0.0 };
    }
    if ux & 0x7FFF_FFFF_FFFF_FFFF < 0x3C00_0000_0000_0000 {
        return 1.0;
    }
    let t = EXP_INV_L * x;
    if t > EXP_OVF {
        return f64::INFINITY;
    }
    if !(t > EXP_UNF) {
        return 0.0;
    }
    let n = t.round_ties_even() as i32;
    let dn = f64::from(n);
    let r1 = x - EXP_L1 * dn;
    let r2 = dn * EXP_L2;
    let j = (n & 0x3F) as usize;
    let m = (n - (n & 0x3F)) >> 6;
    let r = r1 + r2;
    let rr = r * r;
    let r3 = rr * r;
    let r5 = rr * r3;
    let a = (EXP_C2 * r + 1.0) * r;
    let b = (EXP_C4 * r + EXP_C3) * r3;
    let c = (EXP_C6 * r + EXP_C5) * r5;
    let q = a + (b + c);
    let s =
        (q * f64::from_bits(EXP_T[j]) + f64::from_bits(EXP_TAIL[j])) + f64::from_bits(EXP_LEAD[j]);
    let scale_bits = (u64::from(m as u32) + 0x3FF) << 52;
    if scale_bits == 0x7FF0_0000_0000_0000 {
        return f64::from_bits(s.to_bits() | 0x7FE0_0000_0000_0000);
    }
    if m > -1022 {
        return s * f64::from_bits(scale_bits);
    }
    let r11 = if s >= 1.0 { m } else { 0 };
    if r11 == -1022 {
        return s * f64::from_bits(scale_bits);
    }
    if ux as i64 > EXP_TINY_BITS {
        return f64::from_bits(1);
    }
    let sh = (m + 0x432).max(0);
    s * f64::from_bits(1u64 << (sh as u32 & 63))
}

const PIO4_BITS: u64 = 0x3FE9_21FB_5444_2D18;
const BIG_ARG_BITS: u64 = 0x411E_8480_0000_0000;
const TWO_BY_PI: f64 = f64::from_bits(0x3FE4_5F30_6DC9_C883);
const PIO2_1: f64 = f64::from_bits(0x3FF9_21FB_5440_0000);
const PIO2_1T: f64 = f64::from_bits(0x3DD0_B461_1A62_6331);
const PIO2_2: f64 = f64::from_bits(0x3DD0_B461_1A60_0000);
const PIO2_2T: f64 = f64::from_bits(0x3BA3_198A_2E03_7073);
const SIXTH: f64 = f64::from_bits(0x3FC5_5555_5555_5555);
// Sine polynomial.
const S1: f64 = f64::from_bits(0xBFC5_5555_5555_5555);
const S2: f64 = f64::from_bits(0x3F81_1111_1111_0BB3);
const S3: f64 = f64::from_bits(0xBF2A_01A0_19E8_3E5C);
const S4: f64 = f64::from_bits(0x3EC7_1DE3_796C_DE01);
const S5: f64 = f64::from_bits(0xBE5A_E600_B42F_DFA7);
const S6: f64 = f64::from_bits(0x3DE5_E0B2_F9A4_3BB8);
// Cosine polynomial.
const C1: f64 = f64::from_bits(0x3FA5_5555_5555_5555);
const C2: f64 = f64::from_bits(0xBF56_C16C_16C1_6967);
const C3: f64 = f64::from_bits(0x3EFA_01A0_19F4_EC91);
const C4: f64 = f64::from_bits(0xBE92_7E4F_A17F_667B);
const C5: f64 = f64::from_bits(0x3E21_EEB6_9038_2EEC);
const C6: f64 = f64::from_bits(0xBDA9_07DB_4725_8AA7);

/// `((S6x² + S5)x² + S4)·x⁶ + ((S3x² + S2)x² + S1)`.
#[inline]
fn sin_p(x2: f64) -> f64 {
    let x6 = (x2 * x2) * x2;
    ((S6 * x2 + S5) * x2 + S4) * x6 + ((S3 * x2 + S2) * x2 + S1)
}

/// The sine of a reduced argument `r + rr`: `(rr + (r³·p − (r²·0.5)·rr)) + r`.
fn sin_tail(r: f64, rr: f64) -> f64 {
    let x2 = r * r;
    let x3 = r * x2;
    (rr + (x3 * sin_p(x2) - (x2 * 0.5) * rr)) + r
}

/// The cosine of a reduced argument `r + rr`:
/// `(p·r⁴ + ((((0.5r² − 1) + 1) − 0.5r²) − rr·r)) − (0.5r² − 1)`.
fn cos_tail(r: f64, rr: f64) -> f64 {
    let x2 = r * r;
    let x4 = x2 * x2;
    let x6 = x4 * x2;
    let p = ((C3 * x2 + C2) * x2 + C1) + ((C6 * x2 + C5) * x2 + C4) * x6;
    let h = x2 * 0.5;
    (p * x4 + ((((0.5 * x2 - 1.0) + 1.0) - h) - rr * r)) - (h - 1.0)
}

/// Cody–Waite reduction by π/2 in one or two stages: `(n, r, rr, exponent field of r)` for
/// `0 ≤ x < 500000` with `ax` the bits of `|x|`.
fn reduce(x: f64, ax: u64) -> (i32, f64, f64, u64) {
    let n = (x * TWO_BY_PI + 0.5) as i32; // cvttpd2dq
    let dn = f64::from(n);
    let mut rhead = x - PIO2_1 * dn;
    let mut rtail = PIO2_1T * dn;
    let mut r = rhead - rtail;
    let expr = (r.to_bits() << 1) >> 53;
    if (ax >> 52) as i64 - expr as i64 > 15 {
        let t = PIO2_2 * dn;
        let tt = PIO2_2T * dn;
        let old = rhead;
        rhead = old - t;
        rtail = tt - ((old - rhead) - t);
        r = rhead - rtail;
    }
    let rr = (rhead - r) - rtail;
    (n, r, rr, expr)
}

/// The reduced-argument evaluation shared by `sin` and `cos`: `use_cos` selects the cosine
/// polynomial (cos of even, sin of odd quadrants).
fn eval_reduced(r: f64, rr: f64, expr: u64, use_cos: bool) -> f64 {
    if expr >= 0x3F2 {
        if use_cos {
            cos_tail(r, rr)
        } else {
            sin_tail(r, rr)
        }
    } else if expr > 0x3DE {
        let x2 = r * r;
        if use_cos {
            1.0 - x2 * 0.5
        } else {
            r - (SIXTH * r) * x2
        }
    } else if use_cos {
        1.0
    } else {
        r
    }
}

/// MSVCR100 `cos` (x64, 0x797F22B0), bit-exact for finite `|x| < 500000`.
#[must_use]
pub fn cos(x: f64) -> f64 {
    let ux = x.to_bits();
    if ux & 0x7FF0_0000_0000_0000 == 0x7FF0_0000_0000_0000 {
        return x.cos();
    }
    let ax = ux & 0x7FFF_FFFF_FFFF_FFFF;
    if ax <= PIO4_BITS {
        if ax < 0x3F20_0000_0000_0000 {
            if ax < 0x3E40_0000_0000_0000 {
                return 1.0;
            }
            return 1.0 - (x * x) * 0.5;
        }
        let x2 = x * x;
        let x4 = x2 * x2;
        let x8 = x4 * x4;
        let p = ((C2 * x2 + C1) * x4 + (C4 * x2 + C3) * x8) + (x4 * x8) * (C6 * x2 + C5);
        let r = x2 * -0.5;
        let t = r + 1.0;
        return (((1.0 - t) + r) + p) + t;
    }
    if ax >= BIG_ARG_BITS {
        return x.cos();
    }
    let xa = if ux != ax { 0.0 - x } else { x };
    let (n, r, rr, expr) = reduce(xa, ax);
    let res = eval_reduced(r, rr, expr, n & 1 == 0);
    if n.wrapping_add(1) & 2 != 0 {
        0.0 - res
    } else {
        res
    }
}

/// MSVCR100 `sin` (x64, 0x797F4AB0), bit-exact for finite `|x| < 500000`.
#[must_use]
pub fn sin(x: f64) -> f64 {
    let ux = x.to_bits();
    if ux & 0x7FF0_0000_0000_0000 == 0x7FF0_0000_0000_0000 {
        return x.sin();
    }
    let ax = ux & 0x7FFF_FFFF_FFFF_FFFF;
    if ax <= PIO4_BITS {
        if ax < 0x3F20_0000_0000_0000 {
            if ax < 0x3E40_0000_0000_0000 {
                return x;
            }
            return x - ((x * x) * x) * SIXTH;
        }
        let x2 = x * x;
        return x + (x * x2) * sin_p(x2);
    }
    if ax >= BIG_ARG_BITS {
        return x.sin();
    }
    let neg = ux != ax;
    let xa = if neg { 0.0 - x } else { x };
    let (n, r, rr, expr) = reduce(xa, ax);
    let res = eval_reduced(r, rr, expr, n & 1 != 0);
    if ((n as u32) >> 1) & 1 != u32::from(neg) {
        0.0 - res
    } else {
        res
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sanity() {
        for i in -3000..3000 {
            let x = f64::from(i) * 0.0123;
            let e = (x * 0.1).exp();
            assert!((exp(x * 0.1) - e).abs() <= 4e-16 * e, "exp {x}");
            assert!((sin(x) - x.sin()).abs() < 1e-15, "sin {x}");
            assert!((cos(x) - x.cos()).abs() < 1e-15, "cos {x}");
        }
        assert_eq!(exp(0.0), 1.0);
        assert_eq!(exp(f64::NEG_INFINITY), 0.0);
        assert_eq!(exp(1000.0), f64::INFINITY);
        assert_eq!(exp(-800.0), 0.0);
        assert!(exp(-740.0) > 0.0 && exp(-740.0) < 1e-320);
        assert_eq!(sin(0.0), 0.0);
        assert_eq!(cos(0.0), 1.0);
    }

    #[test]
    fn known_msvcr100_values() {
        // Oracle values (`crtd fn=exp n=3 seed=5 lo=-5 hi=5`).
        assert_eq!(
            exp(f64::from_bits(0xBFF2_1DFB_1819_C9E8)).to_bits(),
            0x3FD4_A050_93CE_6FDD
        );
        assert_eq!(
            exp(f64::from_bits(0x4004_2F3F_683E_1F40)).to_bits(),
            0x4028_EF02_1F0B_5107
        );
        assert_eq!(
            exp(f64::from_bits(0xC005_621D_C4FA_DE54)).to_bits(),
            0x3FB1_AD55_5D7C_4666
        );
    }
}
