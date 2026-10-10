//! cyl–sph linear unprojection (x64 0x35187780, x86 0x351918E0) and its sph–cyl swap wrapper
//! (x64 0x35189260, x86 0x35193720).
//!
//! **`dir = 0`** (squared length compared with `ucomiss`/`jne`, so NaN takes this branch too):
//! least-penetration direction. With `h = axis·(cS − cC)` and `ρ` the distance of the sphere
//! centre from the axis (`ρ < 1e-8` → `ρ = r/2` and the centre is moved off the axis along a
//! perpendicular built with `inc_mod3`/`dec_mod3`), compare the radial depth `r − ρ` and the
//! axial depth `hh − |h|` (sign bits, then their difference when both are non-negative):
//! * cap (`0x41 + cap` in `iFeature[0]`): `dir = −sgn(h)·axis`, `t = (hh − |h|) + rS`;
//! * side (0x40): `dir` = the radial direction towards the axis, `t = (r − ρ) + rS`;
//! * rim (`0x20 | cap`): `dir` = normalised `sgn(h)·(hh − |h|)·axis + (r − ρ)/ρ·radial`,
//!   `t = rS − |dir|`.
//!
//! The direction is written back to `params.dir`, `n = −dir`, `pt = cS − n·rS`.
//!
//! **`dir ≠ 0`**: the largest `t` (as a fraction `num/den`, starting from `tmin/1`) at which the
//! moved cylinder still touches the sphere, over three features with branch-free sign-bit
//! selection (like ray–cyl):
//! * side (id 0x10): `|(D − dir·t) × axis|² = (r + rS)²` (larger root);
//! * the cap facing the sphere (id `cap` = 0/1): plane distance `rS`, inside-disk check;
//! * both rims (id 0x20): the quartic `(r² + rS² − |E − dir·t|²)² = 4r²(rS² − (axis·(E −
//!   dir·t))²)` with `E` the sphere centre relative to the rim centre, solved by the
//!   polynomial root finder sub_35149170 on `[0, tmax]`; a root counts if
//!   `|E − dir·t|² > r² + rS²` (the outer touching configuration).
//!
//! `iFeature[1] = 0x40` is written first, also on failure. With no feature found the function
//! returns 0 (after the `iFeature[1]` write); otherwise `t = num/den` and per class: cap
//! `n = ±axis` (`0x41 + cap`), side `n = normalize(radial part of D − dir·t)` (0x40), rim
//! `n = normalize(E' − r·normalize(radial part of E'))` (`0x20`), `pt = cS − n·rS`.

use super::super::common::{
    as_cyl, as_sph, le_or_nan, lt_or_nan_d, normalize_in_place, swap_linear, ucomisd_eq,
    ucomiss_eq, DEC_MOD3, INC_MOD3,
};
use super::super::poly::{find_roots, Poly4};
use super::{sb, select, sgn_pm1};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{CylinderPrim, Primitive, SpherePrim};
use crate::math::Vec3;

/// 0.80999994f (0x3F4F5C28): the "axis is mostly x" threshold of the degenerate perpendicular.
const AXIS_X_THRESH: f32 = f32::from_bits(0x3F4F_5C28);
/// 1e-8 (double at 0x351EDA98).
const RHO_EPS: f64 = 1e-8;
/// 1e-20f (0x1E3CE508), also used widened to double (0x351F1CB8).
const LEN2_EPS_F: f32 = f32::from_bits(0x1E3C_E508);

/// sub_35187780: linear unprojection of a cylinder (A) out of a sphere (B). See the module docs.
#[allow(
    clippy::too_many_lines,
    clippy::similar_names,
    clippy::many_single_char_names
)]
pub fn unproject_cyl_sph(
    p: &mut UnprojParams,
    cyl: &CylinderPrim,
    fa: i32,
    sph: &SpherePrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let sr = f64::from(sph.r);
    let (ax, ay, az) = (
        f64::from(cyl.axis.x),
        f64::from(cyl.axis.y),
        f64::from(cyl.axis.z),
    );
    let (dx, dy, dz) = (f64::from(p.dir.x), f64::from(p.dir.y), f64::from(p.dir.z));
    let r = f64::from(cyl.r);
    let hh = f64::from(cyl.hh);
    let tmin = f64::from(p.tmin);
    let caps = {
        let e = fa.wrapping_sub(0x43);
        (e.wrapping_sub(1) ^ e) >= 0
    };
    out.i_feature[1] = 0x40;
    let mut ddx = f64::from(sph.center.x - cyl.center.x);
    let mut ddy = f64::from(sph.center.y - cyl.center.y);
    let mut ddz = f64::from(sph.center.z - cyl.center.z);
    let l2 = (p.dir.x * p.dir.x + p.dir.y * p.dir.y) + p.dir.z * p.dir.z;
    if ucomiss_eq(l2, 0.0) {
        least_penetration(p, cyl, sph, &mut ddx, &mut ddy, &mut ddz, out);
        return 1;
    }

    let mut den = 1.0f64;
    let mut num = tmin;
    let mut feat: i32 = -1;

    // ---- side: |(D − dir·t) × axis|² = (r + rS)²
    let v94 = -dy * az - -dz * ay;
    let v95 = ax * -dz - -dx * az;
    let v96 = -dx * ay - ax * -dy;
    let wx = ddy * az - ddz * ay;
    let wy = ax * ddz - ddx * az;
    let wz = ddx * ay - ax * ddy;
    let aa = (v95 * v95 + v94 * v94) + v96 * v96;
    let bb = (wy * v95 + wx * v94) + wz * v96;
    let rr = r + sr;
    let disc = bb * bb - (((wy * wy + wx * wx) + wz * wz) - rr * rr) * aa;
    let disc_f = disc as f32;
    let b_f = bb as f32;
    let x_f = (bb * bb - disc) as f32;
    if ((!disc_f.to_bits() & (x_f.to_bits() | b_f.to_bits())) as i32) < 0 {
        let s = disc.sqrt() - bb;
        // Bug kept: the axial extent of the (A-scaled) contact point is compared with the
        // unscaled hh.
        let c1 = ((((ddy * aa - dy * s) * ay + (ddx * aa - dx * s) * ax)
            + (ddz * aa - dz * s) * az)
            .abs()
            - hh) as f32;
        let c2 = (s - f64::from(p.tmax) * aa) as f32;
        let c3 = (num * aa - den * s) as f32;
        let e = sb(c3) & sb(c2) & sb(c1);
        select(e, 0x10, aa, s, &mut feat, &mut den, &mut num);
    }

    if caps {
        // ---- the cap facing the sphere
        let da = (dy * ay + dx * ax) + dz * az;
        let v117 = -1 - ((((da as f32).to_bits() as i32) >> 30) & !1);
        let ada = da.abs();
        let dh = (ddy * ay + ddx * ax) + ddz * az;
        let v118 = (hh + sr) - dh * f64::from(v117);
        let px = ddx * ada - dx * v118;
        let py = ddy * ada - dy * v118;
        let pz = ddz * ada - dz * v118;
        let q = ada * r;
        let c1 = (((py * az - pz * ay) * (py * az - pz * ay)
            + (ax * pz - px * az) * (ax * pz - px * az))
            + (px * ay - ax * py) * (px * ay - ax * py)
            - q * q) as f32;
        let c2 = (v118 - f64::from(p.tmax) * ada) as f32;
        let c3 = (num * ada - den * v118) as f32;
        let e = sb(c3) & sb(c2) & sb(c1);
        select(e, (v117 + 1) >> 1, ada, v118, &mut feat, &mut den, &mut num);

        // ---- the two rims (quartic)
        let v234 = (r * 4.0) * r;
        let r2 = r * r;
        let sr2 = sr * sr;
        let dd = (dx * dx + dy * dy) + dz * dz;
        let tmax = f64::from(p.tmax);
        let mut v9 = -1i32;
        while v9 <= 1 {
            let vh = f64::from(v9) * hh;
            let ex = ddx - vh * ax;
            let ey = ddy - vh * ay;
            let ez = ddz - vh * az;
            let eh = (ey * ay + ex * ax) + ez * az;
            // P = 4r²·((0 + 0) + rS² − Eh², 0 + 2·Eh·da, −da²)
            let mut p0 = [0.0 + 0.0, 0.0 + (eh * 2.0) * da, -(da * da)];
            p0[0] += 1.0 * sr2;
            p0[0] -= 1.0 * (eh * eh);
            let p1 = [v234 * p0[0], p0[1] * v234, p0[2] * v234];
            // Q = r² + rS² − |E − dir·t|²
            let ee = (ex * ex + ey * ey) + ez * ez;
            let mut q = [0.0 + 0.0, 0.0 + ((ex * dx + ey * dy) + ez * dz) * 2.0, -dd];
            q[0] += 1.0 * r2;
            q[0] += 1.0 * sr2;
            q[0] -= 1.0 * ee;
            // Q² (accumulated into zeros, in the binary's order)
            let s4 = 0.0 + q[2] * q[2];
            let s0 = 0.0 + q[0] * q[0];
            let q10 = q[1] * q[0];
            let s1 = (0.0 + q10) + q10;
            let q20 = q[2] * q[0];
            let s2 = ((0.0 + q20) + q[1] * q[1]) + q20;
            let q12 = q[1] * q[2];
            let s3 = (0.0 + q12) + q12;
            let one = 1.0 * 1.0;
            let poly = [
                1.0 * one,
                1.0 * s0 - p1[0] * one,
                1.0 * s1 - p1[1] * one,
                1.0 * s2 - p1[2] * one,
                1.0 * s3,
                1.0 * s4,
            ];
            let mut roots = [0.0f64; 4];
            let n = rim_roots(&poly, 0.0, tmax, &mut roots);
            let lim = r2 + sr2;
            let mut i = n - 1;
            while i >= 0 {
                let t = roots[i as usize];
                let f = (lim
                    - (((ey - dy * t) * (ey - dy * t) + (ex - dx * t) * (ex - dx * t))
                        + (ez - dz * t) * (ez - dz * t))) as f32;
                let g = (num - den * t) as f32;
                let e = sb(f) & sb(g);
                select(e, 0x20, 1.0, t, &mut feat, &mut den, &mut num);
                i -= 1;
            }
            v9 += 2;
        }
    }

    if feat < 0 {
        return 0;
    }
    let t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
    out.t = t;
    let cap = feat & 1;
    let sgn = 2 * cap - 1;
    match feat & 0xF0 {
        0 => {
            let s = sgn as f32;
            out.n = Vec3::new(cyl.axis.x * s, cyl.axis.y * s, cyl.axis.z * s);
            out.i_feature[0] = (((sgn + 1) >> 1) + 1) | 0x40;
        }
        0x10 => {
            out.n = Vec3::new(
                (ddx - dx * t) as f32,
                (ddy - dy * t) as f32,
                (ddz - dz * t) as f32,
            );
            let n = out.n;
            let d = (f64::from(n.y) * ay + ax * f64::from(n.x)) + f64::from(n.z) * az;
            out.n = Vec3::new(
                (f64::from(n.x) - ax * d) as f32,
                (f64::from(n.y) - ay * d) as f32,
                (f64::from(n.z) - az * d) as f32,
            );
            normalize_in_place(&mut out.n);
            out.i_feature[0] = 0x40;
        }
        0x20 => {
            // Bug kept: the rim id carries no cap bit, so this is always the −hh rim.
            let v167 = f64::from(sgn) * hh;
            let mut x = (ddx - ax * v167) - dx * t;
            let mut y = (ddy - ay * v167) - dy * t;
            let mut z = (ddz - az * v167) - dz * t;
            let h2 = (y * ay + ax * x) + z * az;
            let (mut ux, mut uy, mut uz) = (x - ax * h2, y - ay * h2, z - az * h2);
            let l2 = (ux * ux + uy * uy) + uz * uz;
            let eps = f64::from(LEN2_EPS_F);
            if l2 > eps {
                let inv = 1.0 / l2.sqrt();
                ux *= inv;
                uy *= inv;
                uz *= inv;
            } else {
                ux = 0.0;
                uy = 0.0;
                uz = 1.0;
            }
            x -= ux * r;
            y -= uy * r;
            z -= uz * r;
            let l2 = (x * x + y * y) + z * z;
            if l2 > eps {
                let inv = 1.0 / l2.sqrt();
                x *= inv;
                y *= inv;
                z *= inv;
            } else {
                x = 0.0;
                y = 0.0;
                z = 1.0;
            }
            out.n = Vec3::new(x as f32, y as f32, z as f32);
            out.i_feature[0] = ((sgn + 1) | 0x40) >> 1;
        }
        _ => {}
    }
    out.pt = sphere_point(sph, out.n);
    1
}

/// The common tail: `pt = cS − n·rS` (float).
///
/// `n` goes through `black_box`: in the `dir = 0` branch `n = −dir`, and LLVM would otherwise
/// fold `c − (−d)·r` into `c + d·r`, which differs from the binary in the sign of a NaN result
/// (degenerate inputs such as `r = 0` with the sphere centre on the axis).
#[inline]
fn sphere_point(sph: &SpherePrim, n: Vec3) -> Vec3 {
    let n = std::hint::black_box(n);
    let r = sph.r;
    Vec3::new(
        sph.center.x - n.x * r,
        sph.center.y - n.y * r,
        sph.center.z - n.z * r,
    )
}

/// The `dir = 0` branch of sub_35187780 (inline in the binary).
#[allow(clippy::similar_names, clippy::too_many_arguments)]
fn least_penetration(
    p: &mut UnprojParams,
    cyl: &CylinderPrim,
    sph: &SpherePrim,
    ddx: &mut f64,
    ddy: &mut f64,
    ddz: &mut f64,
    out: &mut UnprojResult,
) {
    let sr = f64::from(sph.r);
    let (ax, ay, az) = (
        f64::from(cyl.axis.x),
        f64::from(cyl.axis.y),
        f64::from(cyl.axis.z),
    );
    let h = (*ddy * ay + *ddx * ax) + *ddz * az;
    let hz = az * h;
    let hy = ay * h;
    let hx = ax * h;
    let rx = *ddx - hx;
    let ry = *ddy - hy;
    let rz = *ddz - hz;
    let mut rho = ((rx * rx + ry * ry) + rz * rz).sqrt();
    if lt_or_nan_d(rho, RHO_EPS) {
        // the sphere centre is on the axis: move it off along a perpendicular, by r/2
        let a = cyl.axis;
        let rf = cyl.r * 0.5;
        rho = f64::from(rf);
        let ax2 = a.x * a.x;
        let idx =
            (((((a.y * a.y + ax2) + a.z * a.z) * AXIS_X_THRESH) - ax2).to_bits() >> 31) as usize;
        // the binary builds the vector in the stack slot that held the cylinder centre; all
        // three components are overwritten (idx, inc_mod3[idx], dec_mod3[idx])
        let mut v = [cyl.center.x, cyl.center.y, cyl.center.z];
        v[idx] = 0.0;
        let inc = INC_MOD3[idx] as usize;
        let dec = DEC_MOD3[idx] as usize;
        v[inc] = a[dec];
        v[dec] = -a[inc];
        let l2 = (v[0] * v[0] + v[1] * v[1]) + v[2] * v[2];
        let (vx, vy, vz) = if le_or_nan(l2, LEN2_EPS_F) {
            (0.0, 0.0, 1.0)
        } else {
            let s = 1.0f32 / l2.sqrt();
            (v[0] * s, s * v[1], s * v[2])
        };
        *ddx = f64::from((f64::from(rf * vx) + hx) as f32);
        *ddy = f64::from((f64::from(rf * vy) + hy) as f32);
        *ddz = f64::from((f64::from(rf * vz) + hz) as f32);
    }
    let r = f64::from(cyl.r);
    let v57 = r - rho;
    let v58 = f64::from(cyl.hh) - h.abs();
    let mut edx = sb(v58 as f32);
    let mut ecx = sb(v57 as f32);
    if ecx + edx == 0 {
        edx = sb((v58 - v57) as f32);
        ecx = edx ^ 1;
    }
    match (ecx << 1) | edx {
        1 => {
            // cap
            let hf = h as f32;
            let s = f64::from(-sgn_pm1(hf));
            p.dir = Vec3::new((s * ax) as f32, (s * ay) as f32, (s * az) as f32);
            out.t = v58 + sr;
            out.i_feature[0] = (((((hf.to_bits() as i32) >> 30) & !1) + 2) >> 1) + 0x41;
        }
        2 => {
            // side
            let inv = 1.0 / rho;
            p.dir = Vec3::new(
                ((hx + -*ddx) * inv) as f32,
                (inv * (-*ddy + hy)) as f32,
                (inv * (-*ddz + hz)) as f32,
            );
            out.i_feature[0] = 0x40;
            out.t = v57 + sr;
        }
        3 => {
            // rim
            let k = v57 / rho;
            let sgn = sgn_pm1(h as f32);
            let s = f64::from(sgn);
            let x = (ax * v58) * s + (*ddx - hx) * k;
            let z = s * (az * v58) + k * (*ddz - hz);
            let y = s * (ay * v58) + k * (*ddy - hy);
            let (xf, yf, zf) = (x as f32, y as f32, z as f32);
            let l = ((xf * xf + yf * yf) + zf * zf).sqrt();
            let lt = f64::from(l);
            let inv = 1.0f32 / (lt as f32);
            p.dir = Vec3::new(inv * xf, inv * yf, inv * zf);
            out.t = sr - lt;
            out.i_feature[0] = ((sgn + 1) | 0x40) >> 1;
        }
        _ => {}
    }
    out.n = Vec3::new(-p.dir.x, -p.dir.y, -p.dir.z);
    out.pt = sphere_point(sph, out.n);
}

/// The quartic root finder call of the rim test: sub_35149170(poly, 0.0, tmax, roots, 20, 4,
/// false) → number of roots in `[0, tmax]` (ascending at the front of `roots`). `poly` is the
/// `polynomial_tpl<double, 4>` as built on the stack: denominator, then `c0..c4`.
fn rim_roots(poly: &[f64; 6], lo: f64, hi: f64, roots: &mut [f64; 4]) -> i32 {
    let p = Poly4 {
        denom: poly[0],
        data: [poly[1], poly[2], poly[3], poly[4], poly[5]],
    };
    find_roots(&p, lo, hi, roots, 20)
}

/// 0x35189260: sph–cyl, the 335-byte swap wrapper around [`unproject_cyl_sph`] (the capsule
/// compositions call it directly).
pub fn unproject_sph_cyl(
    p: &mut UnprojParams,
    sph: &SpherePrim,
    fa: i32,
    cyl: &CylinderPrim,
    fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    swap_linear(p, out, |p, out| unproject_cyl_sph(p, cyl, fb, sph, fa, out))
}

pub(super) fn cell_cyl_sph(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_cyl_sph(p, as_cyl(a), fa, as_sph(b), fb, out)
}

pub(super) fn cell_sph_cyl(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_sph_cyl(p, as_sph(a), fa, as_cyl(b), fb, out)
}
