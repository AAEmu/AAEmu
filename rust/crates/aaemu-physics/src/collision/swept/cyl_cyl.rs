//! cyl–cyl linear unprojection (x64 0x351893C0, 29 091 B; x86 0x35193860), cell `[0][cyl][cyl]`.
//!
//! [`unproject_cyl_cyl`] moves cylinder A along `dir` (or, with `|dir|² < 0.5`, along the
//! least-penetration direction it chooses and writes back) until it stops overlapping cylinder B.
//! It is a separating-axis style search over five families of feature pairs; every candidate
//! gives a depth as a ratio `num / den` (`t_num` / `t_den`, initially `tmax / 1` in
//! least-penetration mode, `0 / 1` with a direction) and the best candidate is kept branch-free
//! (`x·(1−sel) + y·sel`). The winning family is encoded in `best` (initially −1):
//!
//! | phase | candidates | `best` | result (`switch (best & 0xC0)`) |
//! |---|---|---|---|
//! | 1 | cap face of cylinder *k* vs the other's rim (needs both features ≠ 0x43) | `k` | 0: pt on the rim of *k*, n = ±axis of the other, features `0x20|·` / `0x40|·` |
//! | 2 | cap rim of *i* (j = ±1) vs the side of the other; a 1024-step quarter-wave table bisection (and in directed mode a cubic discriminant bisection) finds the rim point | `2i | 0x40 | (j+1)/2` | 0x40 |
//! | 3 | side vs side along the axis cross product `n0` (two passes, the second shifted by `2(rA+rB)`) | `0xC0` | 0xC0: pt on B's axis line, n = ±n0, features 0x40/0x40 |
//! | 4 | rim vs rim: a quartic in t (built from inline polynomial products) solved with the `poly` group's root finders 0x3514A2E0 / 0x35149170 | `0x80 | (jA+1) | (jB+1)/2` | 0x80 |
//!
//! Failure (`ret 0`): no candidate (`best == −1`), or in least-penetration mode the "deep" flag
//! is set and `t > 0.1·min(rA, rB)`. On success the result `t` is `t_num / t_den` (0 if
//! `t_den == 0` or NaN), written before the case code; least-penetration mode writes the
//! normalised accumulated direction back to `params.dir` first.
//!
//! The 7th argument (contact area) **is written** when present (`npt` is reset to 0 first): for
//! near-parallel caps (`|axA×axB|² < 2(1−maxcos)`) up to 16 cap-rim samples (45° steps, kept
//! when inside the other rim ×1.01) plus 2 rim–rim intersection points, else for parallel sides
//! up to 4 cap-edge points; otherwise, for a cap against a side (`cos² < 2(1−maxcos)`), up to 4
//! points from the rim/side and axis-end tests. The routine never checks `nmaxpt` (bug kept: it
//! writes up to 18 points into the caller's pool, which holds 256); the port grows the buffers.
//!
//! Static memory: `dword_3537B450[1025]` = `sinf(i·π/2048)`, built once by sub_351E5900 (a DLL
//! static initialiser, run at load; no state survives between calls). `unk_3537C450 − i` is
//! `table[1024 − i]` (the cosine). Ported as [`sin_table`].
//!
//! Original oddities mirrored (`Bug kept` at the sites): phase 1 always uses B's radius; in
//! directed mode the phase-2 outputs `v917/v918` are cleared by every later (i, j) iteration that
//! is not selected; the lp phase-2 table search keeps the last probed index, the directed one the
//! low bound; the directed phase-4 branch accepts the largest root without the geometric checks;
//! case 0x40 rounds several double products to float.

use super::common::{
    as_cyl, le_or_nan_d, lt_or_nan, lt_or_nan_d, normalized, ucomisd_eq, DEC_MOD3, INC_MOD3,
};
use super::{UnprojTables, MODE_LINEAR};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{CylinderPrim, Primitive};
use crate::ids::GeomType;
use crate::math::{crt, isneg, min_idiom, sgnnz, Vec3};
use std::sync::OnceLock;

#[inline]
fn d(x: f32) -> f64 {
    f64::from(x)
}

/// `(i32)(1 − 2·sign)` style flag → double, as `cvtdq2pd` of a 0/1 integer.
#[inline]
fn fl(x: u32) -> f64 {
    f64::from(x)
}

/// `1/3`, `1/6`, `1/27` (doubles at 0x351EF330 / 0x351EF700 / 0x351F2DC0).
const THIRD: f64 = f64::from_bits(0x3FD5_5555_5555_5555);
const SIXTH: f64 = f64::from_bits(0x3FC5_5555_5555_5555);
const TWENTYSEVENTH: f64 = f64::from_bits(0x3FA2_F684_BDA1_2F68);

/// `dword_3537B450`: `sinf(i · 0.0015339808)` for `i = 0..=1024` (sub_351E5900, run once at DLL
/// load). Entry `1024 − i` is the matching cosine (`unk_3537C450 − i`).
pub fn sin_table() -> &'static [f32; 1025] {
    static TAB: OnceLock<[f32; 1025]> = OnceLock::new();
    TAB.get_or_init(|| {
        let step = f32::from_bits(0x3AC9_0FDB); // π/2048
        let mut t = [0.0f32; 1025];
        for (i, v) in t.iter_mut().enumerate() {
            *v = crt::sinf(i as f32 * step);
        }
        t
    })
}

/// `!(sign((a3 − 0x43) ^ (a3 − 0x44)))`: 0 only for feature 0x43 (wrapping, as the binary).
fn feature_ok(f: i32) -> u32 {
    let e = f.wrapping_sub(0x43);
    u32::from((e.wrapping_sub(1) ^ e) >= 0)
}

/// Sign with zero: `((bits − 1) >> 31) + (bits >> 31) + 1` → 1, 0 (±0) or −1.
fn sign0(x: f32) -> i32 {
    let b = x.to_bits() as i32;
    (b.wrapping_sub(1) >> 31) + (b >> 31) + 1
}

/// Write area entry `i` (the routine never checks `nmaxpt`; grow the caller's buffers).
fn area_slot(a: &mut ContactArea, i: usize) {
    if i >= a.pt.len() {
        a.pt.resize(i + 1, Vec3::ZERO);
        for k in 0..2 {
            a.feature[k].resize(i + 1, 0);
            a.prim[k].resize(i + 1, 0);
        }
    }
}

/// Root finders of the `poly` group (sub_3514A2E0 / sub_35149170) on the 6-double polynomial
/// `[denominator, c0, c1, c2, c3, c4]`.
mod solver {
    // TEMPORARY until the poly group's solvers are merged.
    pub fn has_roots(_p: &[f64; 6], _lo: f64, _hi: f64) -> i32 {
        panic!("cyl_cyl: poly solver not merged yet")
    }
    pub fn find_roots(_p: &[f64; 6], _lo: f64, _hi: f64, _roots: &mut [f64; 4]) -> i32 {
        panic!("cyl_cyl: poly solver not merged yet")
    }
}

/// The mutable search state shared by the phases.
struct St<'a> {
    c: [&'a CylinderPrim; 2],
    lp: bool,
    feat_ok: [u32; 2],
    both_ok: u32,
    /// v874 / v19: candidate depth numerator.
    t_num: f64,
    /// v860 / v14: denominator.
    t_den: f64,
    /// v862, v878, v876: least-penetration direction accumulator.
    nrm: [f64; 3],
    /// v858: the chosen feature pair still overlaps ("deep").
    deep: u32,
    /// v11: winning family / features.
    best: i32,
    /// v882, v859, v861: unit `axA × axB` (or a perpendicular to A when parallel).
    n0: [f64; 3],
    /// v880: `axA · axB` (float value).
    cos_ab: f64,
    /// v933: `|axA × axB|`.
    sin_ab: f64,
    /// v919: `1 / v933` (0 when parallel).
    inv_sin: f64,
    /// v872: B's axis copy, then per phase-1 cylinder the rim offset length.
    v872: [f32; 3],
    /// v964..: phase-1 rim offset directions (float), 3 per cylinder.
    rim_dir: [f32; 6],
    /// v917 / v918: phase-2 rim point (circle cos / sin offsets).
    v917: f64,
    v918: f64,
    /// Phase-4 frame (v358.., v364.., B axis / sin), needed by case 0x80.
    p4: P4,
}

#[derive(Clone, Copy, Default)]
struct P4 {
    u: [f64; 3],   // v899 (v358), v911 (v361), v913 (v360)
    w: [f64; 3],   // v909 (v364), v895, v883 (v365)
    bs: [f64; 3],  // v916, v914, v900
    ar: f64,       // v896 = A.r
}

impl St<'_> {
    #[inline]
    fn take(&mut self, sel: u32, num: f64, den: f64) {
        let fs = fl(sel);
        let fk = fl(sel ^ 1);
        self.t_num = self.t_num * fk + num * fs;
        self.t_den = self.t_den * fk + den * fs;
    }
    #[inline]
    fn set_best(&mut self, sel: u32, code: i32) {
        let s = sel as i32;
        self.best = (self.best & (s - 1)) | (code & -s);
    }
}

/// sub_351893C0 (x64) / 0x35193860 (x86): linear cylinder–cylinder unprojection. See the module
/// docs for the algorithm, outputs and conventions.
#[allow(clippy::too_many_lines)]
pub fn unproject_cyl_cyl(
    p: &mut UnprojParams,
    a: &CylinderPrim,
    fa: i32,
    b: &CylinderPrim,
    fb: i32,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) -> i32 {
    let dl2 = (p.dir.x * p.dir.x + p.dir.y * p.dir.y) + p.dir.z * p.dir.z;
    let feat_ok = [feature_ok(fa), feature_ok(fb)];
    let lp = !(dl2 >= 0.5);
    let ax = a.axis;
    let bx = b.axis;
    let cos_ab = d((ax.x * bx.x + ax.y * bx.y) + ax.z * bx.z);
    let cx = d(bx.z * ax.y - bx.y * ax.z);
    let cy = d(bx.x * ax.z - bx.z * ax.x);
    let cz = d(bx.y * ax.x - bx.x * ax.y);
    let sin_ab = ((cx * cx + cy * cy) + cz * cz).sqrt();
    let mut v872 = [bx.x, bx.y, bx.z];
    let mut inv_sin = 0.0;
    let n0;
    if sin_ab > d(0.001) {
        let inv = 1.0 / sin_ab;
        inv_sin = inv;
        n0 = [cx * inv, cy * inv, cz * inv];
    } else {
        // a vector perpendicular to A's axis (drop the component picked by the 0.81 test)
        let t = ((ax.y * ax.y + ax.x * ax.x) + ax.z * ax.z) * f32::from_bits(0x3F4F_5C28)
            - ax.x * ax.x;
        let idx = (t.to_bits() >> 31) as usize;
        let i1 = INC_MOD3[idx] as usize;
        let i2 = DEC_MOD3[idx] as usize;
        v872[idx] = 0.0;
        v872[i1] = ax[i2];
        v872[i2] = -ax[i1];
        let l2 = (v872[0] * v872[0] + v872[1] * v872[1]) + v872[2] * v872[2];
        let (x, y, z) = if l2 > 1e-20 {
            let inv = 1.0 / l2.sqrt();
            (v872[0] * inv, v872[1] * inv, v872[2] * inv)
        } else {
            (0.0, 0.0, 1.0)
        };
        n0 = [d(x), d(y), d(z)];
    }
    let mut s = St {
        c: [a, b],
        lp,
        feat_ok,
        both_ok: feat_ok[0] & feat_ok[1],
        t_num: if lp { d(p.tmax) } else { 0.0 },
        t_den: 1.0,
        nrm: [0.0; 3],
        deep: 0,
        best: -1,
        n0,
        cos_ab,
        sin_ab,
        inv_sin,
        v872,
        rim_dir: [0.0; 6],
        v917: 0.0,
        v918: 0.0,
        p4: P4::default(),
    };
    if s.both_ok != 0 {
        phase_caps(&mut s, p);
    }
    phase_rims(&mut s, p);
    phase_sides(&mut s, p);
    if f64::from(s.both_ok) * s.sin_ab > d(0.001) {
        phase_rim_rim(&mut s, p);
    }

    if s.best == -1 {
        return 0;
    }
    if s.deep != 0 && s.t_num - d(min_idiom(a.r, b.r) * 0.1) * s.t_den > 0.0 {
        return 0;
    }
    if s.lp {
        let n = s.nrm;
        let l2 = (n[0] * n[0] + n[1] * n[1]) + n[2] * n[2];
        let (x, y, z) = if l2 > d(1e-20) {
            let inv = 1.0 / l2.sqrt();
            (inv * n[0], n[1] * inv, n[2] * inv)
        } else {
            (0.0, 0.0, 1.0)
        };
        p.dir = Vec3::new(x as f32, y as f32, z as f32);
    }
    let t = if ucomisd_eq(s.t_den, 0.0) {
        0.0
    } else {
        s.t_num / s.t_den
    };
    match s.best & 0xC0 {
        0 => result_cap(&s, p, t, out),
        0x40 => result_rim_side(&s, p, t, out),
        0x80 => result_rim_rim(&s, p, t, out),
        _ => result_side_side(&s, p, t, out),
    }
    let Some(ar) = area else { return 1 };
    contact_area(&s, p, out, ar)
}

/// Phase 1 (both features ≠ 0x43): the cap face of cylinder `k` against the rim of the other.
fn phase_caps(s: &mut St, p: &UnprojParams) {
    let b_r = s.c[1].r;
    for k in 0..2usize {
        let sgn_k: i32 = 1 - 2 * k as i32; // v50
        let c = s.c[k];
        let o = s.c[k ^ 1];
        // u (v58, v59, v62): the other's axis oriented towards c (lp) or against dir
        let (u, dv) = if s.lp {
            let h = ((c.center.y - o.center.y) * o.axis.y + (c.center.x - o.center.x) * o.axis.x)
                + (c.center.z - o.center.z) * o.axis.z;
            let sf = sgnnz(h) as f32;
            let u = [d(o.axis.x * sf), d(o.axis.y * sf), d(o.axis.z * sf)];
            (u, u)
        } else {
            let h = (p.dir.y * o.axis.y + p.dir.x * o.axis.x) + p.dir.z * o.axis.z;
            let sf = (sgn_k * sgnnz(h)) as f32;
            let u = [d(o.axis.x * sf), d(o.axis.y * sf), d(o.axis.z * sf)];
            (u, [d(p.dir.x), d(p.dir.y), d(p.dir.z)])
        };
        let fk = f64::from(sgn_k); // v917
        let w_y = dv[1] * fk; // v879
        let w_x = dv[0] * fk; // v883
        let w_z = dv[2] * fk; // v881
        let mut h = d(((u[0] as f32) * c.axis.x + (u[1] as f32) * c.axis.y)
            + (u[2] as f32) * c.axis.z);
        if lt_or_nan_d(h.abs(), d(0.001)) {
            h = d(((c.center.x - o.center.x) * c.axis.x + (c.center.y - o.center.y) * c.axis.y)
                + (c.center.z - o.center.z) * c.axis.z);
        }
        let sc = (-sgnnz(h as f32)) as f32;
        let cn = [d(c.axis.x * sc), d(c.axis.y * sc), d(c.axis.z * sc)]; // v940..
        let hh = d(c.hh);
        let pc = [
            d(c.center.x) + cn[0] * hh,
            d(c.center.y) + cn[1] * hh,
            d(c.center.z) + cn[2] * hh,
        ];
        let ohh = d(o.hh);
        let q = [
            d(o.center.x) + ohh * u[0],
            d(o.center.y) + ohh * u[1],
            d(o.center.z) + ohh * u[2],
        ];
        let dcu = (cn[1] * u[1] + cn[0] * u[0]) + cn[2] * u[2]; // v89
        let mut wz = cn[2] * dcu - u[2];
        let mut wy = cn[1] * dcu - u[1];
        let mut wx = dcu * cn[0] - u[0];
        let mut l2 = (wy * wy + wx * wx) + wz * wz;
        let len;
        if l2 > 0.0001 {
            len = l2.sqrt();
        } else {
            let dx = q[0] - pc[0];
            let dy = q[1] - pc[1];
            let dz = q[2] - pc[2];
            let hc = ((d(c.axis.y) * dy + d(c.axis.x) * dx) + d(c.axis.z) * dz) as f32;
            wy = dy - d(c.axis.y * hc);
            wx = dx - d(c.axis.x * hc);
            wz = dz - d(c.axis.z * hc);
            l2 = (wy * wy + wx * wx) + wz * wz;
            if l2 > d(0.0001) {
                len = l2.sqrt();
            } else {
                wx = 0.0;
                wy = 0.0;
                wz = 0.0;
                len = 1.0;
            }
        }
        s.rim_dir[3 * k] = wx as f32;
        s.rim_dir[3 * k + 1] = wy as f32;
        s.v872[k] = len as f32;
        s.rim_dir[3 * k + 2] = wz as f32;
        let r = d(c.r);
        let rz = pc[2] * len + wz * r;
        let rx = pc[0] * len + r * wx;
        let ry = pc[1] * len + wy * r;
        let v863 = ((len * q[1] - ry) * u[1] + (len * q[0] - rx) * u[0]) + (len * q[2] - rz) * u[2];
        let v107 = (((w_y * u[1] + w_x * u[0]) + w_z * u[2]) * fk) * len;
        let sg = f64::from(sgnnz(v107 as f32));
        let den = v107 * sg; // v878
        let num = v863 * sg; // v864
        let v109 = rx * den;
        let v110 = ry * den;
        let v111 = rz * den;
        let v917 = (fk * num) * len;
        // Bug kept: B's radius for both k (the "other" cylinder is A when k = 1).
        let br = d(b_r);
        let dl = den * len;
        let ex = (v109 + v917 * w_x) - dl * q[0];
        let ey = (v110 + w_y * v917) - dl * q[1];
        let ez = (v111 + w_z * v917) - dl * q[2];
        let rr = (br * len) * den;
        let v113 = isneg((((ex * ex + ey * ey) + ez * ez) - rr * rr) as f32);
        let sel;
        if s.lp {
            let a1 = (s.t_den * num - den * s.t_num) as f32;
            let a2 = (d(min_idiom(s.c[0].r, s.c[1].r) * f32::from_bits(0xBB03_126F)) - num) as f32;
            sel = isneg(a1) & isneg(a2);
            let keep = sel ^ 1;
            s.deep = (sel & (v113 ^ 1)) | (s.deep & keep);
            let (fs, fk_) = (fl(sel), fl(keep));
            s.nrm[2] = s.nrm[2] * fk_ + w_z * fs;
            s.nrm[0] = s.nrm[0] * fk_ + w_x * fs;
            s.nrm[1] = s.nrm[1] * fk_ + w_y * fs;
        } else {
            let a1 = (den * s.t_num - s.t_den * num) as f32;
            sel = v113 & isneg(a1);
        }
        s.set_best(sel, k as i32);
        s.take(sel, num, den);
    }
}

/// Phase 2: the cap rims of each cylinder (feature ≠ 0x43) against the other's side.
#[allow(clippy::too_many_lines)]
fn phase_rims(s: &mut St, p: &UnprojParams) {
    let tab = sin_table();
    let n0 = s.n0;
    let nf = [n0[0] as f32, n0[1] as f32, n0[2] as f32]; // v897, v849, v1040
    for i in 0..2usize {
        if s.feat_ok[i] == 0 {
            continue;
        }
        let v123 = 1 - 2 * i as i32;
        let v124 = 2 * i as i32;
        let c = s.c[i];
        let o = s.c[i ^ 1];
        let e = [
            d(nf[2] * o.axis.y - nf[1] * o.axis.z),
            d(nf[0] * o.axis.z - nf[2] * o.axis.x),
            d(nf[1] * o.axis.x - nf[0] * o.axis.y),
        ]; // v925, v974, v975
        let cr = d(c.r); // v896
        let orr = d(o.r); // v910
        let si = (v124 - 1) as f32;
        let dd = [d(p.dir.x * si), d(p.dir.y * si), d(p.dir.z * si)]; // v839, v907, v893
        for j in [-1i32, 1] {
            let jh = j as f32 * c.hh; // v144
            let dx = d((o.center.x - c.center.x) - c.axis.x * jh); // v883
            let dy = d((o.center.y - c.center.y) - c.axis.y * jh); // v895
            let dz = d((o.center.z - c.center.z) - c.axis.z * jh); // v879
            let v146 = (dy * n0[1] + dx * n0[0]) + dz * n0[2];
            let v147 = (dx * e[0] + dy * e[1]) + dz * e[2];
            let code = v124 | (((j + 1) | 0x80) >> 1);
            if s.lp {
                if s.sin_ab > d(0.005) {
                    if lt_or_nan_d((f64::from(j) * v147) * f64::from(v123), 0.0) {
                        continue;
                    }
                } else {
                    let jh2 = jh * 2.0;
                    let p1 = d(c.axis.x * jh2);
                    let p2 = d(c.axis.y * jh2);
                    let p3 = d(c.axis.z * jh2);
                    let lhs = (dy * dy + dx * dx) + dz * dz;
                    let rhs = ((p2 + dy) * (p2 + dy) + (p1 + dx) * (p1 + dx)) + (p3 + dz) * (p3 + dz);
                    if lhs > rhs {
                        continue;
                    }
                }
                let s147 = sgnnz(v147 as f32);
                let s146 = sgnnz(v146 as f32);
                let f147 = f64::from(s147);
                let f146 = f64::from(s146);
                let acos = s.cos_ab.abs();
                let v159 = (cr * f147) * acos;
                let flip = ((1 - s147 * s146) >> 1) as u32;
                let v162 = acos * f146;
                let v163 = cr * f146;
                let (mut lo, mut hi) = (0i32, 1024i32);
                let (mut mid, mut v167, mut v168);
                loop {
                    mid = (hi + lo) >> 1;
                    let sn = d(tab[mid as usize]);
                    let cs = d(tab[(1024 - mid) as usize]);
                    v167 = sn * v159;
                    v168 = cs * v163;
                    let v169 = ((v147 - v167) * (cs * v162) - (v146 - v168) * (sn * f147)) as f32;
                    if flip ^ isneg(v169) == 0 {
                        lo = mid;
                    } else {
                        hi = mid;
                    }
                    if hi - lo <= 1 {
                        break;
                    }
                }
                // Bug kept: the point of the last probe (`mid`), not of a bracket end.
                let dv = v168 - v146;
                let dw = v167 - v147;
                let dist2 = dw * dw + dv * dv;
                if dv * v168 + dw * v167 >= 0.0 || dist2 >= orr * orr {
                    continue;
                }
                let v171 = orr - dist2.sqrt();
                if s.t_den * v171 - s.t_num >= 0.0 {
                    continue;
                }
                let v173 = s147 as f32 * tab[mid as usize];
                let v174 = nf[2] * c.axis.y;
                let v175 = nf[0] * c.axis.z;
                let v917 = d(v173) * cr;
                let v178 = (f64::from(sgnnz(s.cos_ab as f32)) * v917) as f32;
                let v179 = (v174 - nf[1] * c.axis.z) * v178;
                let v180 = (v175 - nf[2] * c.axis.x) * v178;
                let v181 = (nf[1] * c.axis.x - nf[0] * c.axis.y) * v178;
                let gy = (dy - n0[1] * v168) - d(v180);
                let gx = (dx - v168 * n0[0]) - d(v179);
                let gz = (dz - n0[2] * v168) - d(v181);
                let proj = (d(o.axis.y) * gy + d(o.axis.x) * gx) + d(o.axis.z) * gz;
                s.deep = isneg((d(o.hh) - proj.abs()) as f32);
                let pf = proj as f32;
                let gy2 = gy - d(o.axis.y * pf);
                let gx2 = gx - d(o.axis.x * pf);
                let gz2 = gz - d(o.axis.z * pf);
                let l2 = (gy2 * gy2 + gx2 * gx2) + gz2 * gz2;
                let (nx, ny, nz) = if l2 > d(1e-20) {
                    let inv = 1.0 / l2.sqrt();
                    (inv * gx2, gy2 * inv, gz2 * inv)
                } else {
                    (0.0, 0.0, 1.0)
                };
                s.best = code;
                let f = f64::from(v124 - 1);
                s.nrm = [nx * f, ny * f, nz * f];
                s.t_num = v171;
                s.t_den = 1.0;
                s.v917 = v917;
                s.v918 = v168;
            } else {
                rim_directed(s, p, i, code, [dx, dy, dz], dd, e, v146, v147);
            }
        }
    }
}

/// Phase 2, directed mode: find the travel at which the rim point (circle offset found by
/// bisections) leaves the other's side, for one rim (`code`).
#[allow(clippy::too_many_arguments, clippy::too_many_lines)]
fn rim_directed(
    s: &mut St,
    p: &UnprojParams,
    i: usize,
    code: i32,
    dxyz: [f64; 3],
    dd: [f64; 3],
    e: [f64; 3],
    v146: f64,
    v147: f64,
) {
    let tab = sin_table();
    let n0 = s.n0;
    let nf = [n0[0] as f32, n0[1] as f32, n0[2] as f32];
    let c = s.c[i];
    let o = s.c[i ^ 1];
    let cr = d(c.r);
    let orr = d(o.r);
    let [dx, dy, dz] = dxyz;
    let tm = d(p.tmax);
    let mut found = false; // v196
    // Bug kept: cleared on every (i, j) of the directed search, also after a selection.
    s.v917 = 0.0;
    s.v918 = 0.0;
    let v198 = (dd[1] * n0[1] + dd[0] * n0[0]) + dd[2] * n0[2];
    let v199 = (dd[0] * e[0] + dd[1] * e[1]) + dd[2] * e[2];
    let acos = s.cos_ab.abs();
    let mut lim = [0.0f64; 2]; // v921 (lo), v922 (hi)
    let mut direct = lt_or_nan(p.tmax, min_idiom(s.c[0].r, s.c[1].r)) && acos > d(0.003);
    let v930 = v198 * v198;
    let v201 = v930 + v199 * v199;
    if !direct && lt_or_nan_d(v201, d(2.5e-5)) {
        direct = true;
    }
    if direct {
        lim = [0.0, tm];
    } else {
        let mut x = [0.0f64; 8]; // v956..v963
        let k0;
        if !(acos > d(0.003)) {
            x[5] = 0.0;
            x[7] = 0.0;
            k0 = 1;
            x[6] = cr;
            x[4] = -cr;
        } else {
            let cos2 = s.cos_ab * s.cos_ab;
            let a0 = v198 * s.cos_ab;
            let v205 = (1.0 / (a0 * a0 + v199 * v199).sqrt()) * cr;
            let v206 = -(v198 * cos2) * v205;
            let v208 = v205 * acos;
            let v209 = v205 * v199;
            let v210 = ((v199 * v209 - v206 * v198) * (v199 * v146 - v198 * v147)) as f32;
            let s210 = f64::from(sgnnz(v210));
            x[0] = v209 * s210;
            x[1] = v206 * s210;
            x[2] = v208 * v198;
            x[3] = v208 * v199;
            let b0 = v199 * s.cos_ab;
            let q = (1.0 / (b0 * b0 + v930).sqrt()) * cr;
            let v213 = (v199 * cos2) * q;
            let v214 = q * v198;
            let sq = f64::from(sgnnz((v198 * v214 + v199 * v213) as f32));
            let v215 = (v198 * (v214 * sq) + v199 * (v213 * sq)) * (1.0 / v201);
            k0 = 0;
            x[4] = v215 * v198;
            x[5] = v215 * v199;
            let v217 = (x[1] * -v198 + v199 * x[0]) * (1.0 / v201);
            x[7] = v217 * -v198 + v215 * v199;
            x[6] = v217 * v199 + v215 * v198;
        }
        let orr2 = orr * orr;
        for k in k0..2 {
            let b4 = 4 * k;
            let v220 = x[b4 + 3] - x[b4 + 1];
            let v221 = v147 - x[b4 + 1];
            let v223 = (v146 - x[b4]) * v220;
            let v224 = x[b4 + 2] - x[b4];
            let mut v222 = 1.0f64;
            let mut v225 = 0.0f64;
            let v226 = v220 * v198 - v224 * v199;
            let v228 = v226.abs();
            let v229 = (v146 - x[b4]) * v228;
            let v230 = v224 * v224 + v220 * v220;
            let v231 = v230.sqrt() * orr - (v223 - v224 * v221) * f64::from(sign0(v226 as f32));
            let v232 = ((v230 * v228
                - ((v229 + v198 * v231) * v224 + (v221 * v228 + v199 * v231) * v220) * 2.0)
                .abs()
                - (-(v230 * v228)).abs()) as f32;
            if isneg(v232) != 0 && (v231 - v228 * 0.0) + v231 * 1e-80 > 0.0 {
                if v231 - tm * v228 >= 0.0 {
                    v222 = 1.0;
                    v225 = tm;
                } else {
                    v225 = v231;
                    v222 = v228;
                    found = true;
                }
            }
            let mut skip_div = false;
            for (m, last) in [(b4, false), (b4 + 2, true)] {
                let ea = v146 - x[m];
                let eb = v147 - x[m + 1];
                let v236 = ea * v198 + eb * v199;
                let v237 = v222 * v236 + v225 * v201;
                let v238 = v236 * v236 - ((ea * ea + eb * eb) - orr2) * v201;
                if v238 > 0.0 && (lt_or_nan_d(v237, 0.0) || (v222 * v222) * v238 > v237 * v237) {
                    let v240 = tm * v201 + v236;
                    if le_or_nan_d(v240, 0.0) || v238 >= v240.abs() * v240 {
                        v225 = tm;
                        if last {
                            // the third end jumps past the division (v222 is left as it is)
                            skip_div = true;
                        } else {
                            v222 = 1.0;
                        }
                    } else {
                        v222 = v201;
                        found = true;
                        v225 = v238.sqrt() - v236;
                    }
                }
            }
            if !skip_div && !ucomisd_eq(v222, 1.0) {
                v225 /= v222;
            }
            lim[k] = v225;
        }
    }
    let mid_t;
    if !(acos > d(0.003)) {
        if !found {
            return;
        }
        mid_t = (lim[1] + lim[1]) * 0.5;
    } else {
        let cos2 = s.cos_ab * s.cos_ab;
        let v249 = cos2 + 1.0;
        let cr2 = cr * cr;
        let v937 = cr2 * v249 + orr * orr;
        let oc = orr * cr;
        let c2c = cr2 * s.cos_ab;
        let v250 = (oc * oc) * v249 + c2c * c2c;
        let v252b = (oc * cr) * s.cos_ab;
        let v252 = (v252b * v252b) * 0.5;
        let f = |t: f64| -> f64 {
            let ra = v146 + v198 * t;
            let rb = v147 + v199 * t;
            let a2 = ra * ra;
            let b2 = rb * rb;
            let v254 = (v937 - a2) - b2;
            let v255 = v250 - (a2 * cos2 + b2) * cr2;
            let sq = v254 * v254;
            let v256 = v255 - sq * THIRD;
            let v257 = (v255 * SIXTH - sq * TWENTYSEVENTH) * v254 - v252;
            ((v256 * v256) * v256) * TWENTYSEVENTH + v257 * v257
        };
        if f(lim[1]) * f(lim[0]) >= 0.0 {
            return;
        }
        for _ in 0..16 {
            let m = (lim[1] + lim[0]) * 0.5;
            lim[isneg(f(m) as f32) as usize] = m;
        }
        mid_t = (lim[1] + lim[0]) * 0.5;
    }
    // L83: the rim angle at mid_t by table bisection
    let v276 = v147 + v199 * mid_t;
    let v277 = v146 + v198 * mid_t;
    let s281 = sgnnz(v276 as f32);
    let s282 = sgnnz(v277 as f32);
    let flip = ((1 - s282 * s281) >> 1) as u32;
    let v283 = f64::from(s281);
    let v284 = (s.cos_ab * s.cos_ab) * f64::from(s282);
    let v285 = f64::from(s282) * cr;
    let v286 = (v283 * cr) * acos;
    let (mut lo, mut hi) = (0i32, 1024i32);
    loop {
        let mid = (lo + hi) >> 1;
        let sn = d(tab[mid as usize]);
        let cs = d(tab[(1024 - mid) as usize]);
        let v290 = ((v276 - sn * v286) * (cs * v284) - (v277 - cs * v285) * (sn * v283)) as f32;
        if flip ^ isneg(v290) == 0 {
            lo = mid;
        } else {
            hi = mid;
        }
        if hi - lo <= 1 {
            break;
        }
    }
    // Bug kept (vs. the lp search): the low bound's angle.
    let v956 = d(s282 as f32 * tab[(1024 - lo) as usize]) * cr;
    let v957 = d(s281 as f32 * tab[lo as usize]) * cr;
    let v292 = (f64::from(sgnnz(s.cos_ab as f32)) * v957) as f32;
    let px = ((dx + dd[0] * mid_t) - n0[0] * v956) - d((nf[2] * c.axis.y - nf[1] * c.axis.z) * v292);
    let py = ((dd[1] * mid_t + dy) - n0[1] * v956) - d((c.axis.z * nf[0] - nf[2] * c.axis.x) * v292);
    let pz = ((dd[2] * mid_t + dz) - n0[2] * v956) - d((nf[1] * c.axis.x - c.axis.y * nf[0]) * v292);
    let v293 = (((d(o.axis.y) * py + d(o.axis.x) * px) + d(o.axis.z) * pz).abs() - d(o.hh)) as f32;
    let v294 = (s.t_num - s.t_den * mid_t) as f32;
    let sel = isneg(v293) & isneg(v294);
    s.set_best(sel, code);
    s.take(sel, mid_t, 1.0);
    let z = fl(sel ^ 1) * 0.0;
    s.v918 = z + v956 * fl(sel);
    s.v917 = z + v957 * fl(sel);
}

/// Phase 3: the sides against each other along `n0` (two passes, the second shifted by
/// `2(rA + rB)`).
fn phase_sides(s: &mut St, p: &UnprojParams) {
    let (a, b) = (s.c[0], s.c[1]);
    let n0 = s.n0;
    let dx = d(b.center.x - a.center.x);
    let dy = d(b.center.y - a.center.y);
    let dz = d(b.center.z - a.center.z);
    let (v306, v307, v308) = if s.lp {
        let sg = f64::from(-sgnnz(((n0[0] * dx + dy * n0[1]) + dz * n0[2]) as f32));
        (n0[0] * sg, n0[1] * sg, n0[2] * sg)
    } else {
        (d(p.dir.x), d(p.dir.y), d(p.dir.z))
    };
    let v312 = (v306 * n0[0] + v307 * n0[1]) + v308 * n0[2];
    let s312 = f64::from(sgnnz(v312 as f32));
    let v316 = v312.abs();
    let v317 = dz * v316;
    let v319 = dy * v316;
    let v925 = v316 * dx;
    let mut v321 = (((n0[0] * dx + dy * n0[1]) + dz * n0[2]) * s312 - d(a.r)) - d(b.r);
    let v866 = (a.r + b.r) * 2.0;
    let (bx, by, bz) = (d(b.axis.x), d(b.axis.y), d(b.axis.z));
    let (ax, ay, az) = (d(a.axis.x), d(a.axis.y), d(a.axis.z));
    let v930 = (d(b.hh) * v316) * s.sin_ab;
    let ah = (d(a.hh) * v316) * s.sin_ab;
    for _ in 0..2 {
        let v324 = v319 - v307 * v321;
        let v325 = v317 - v308 * v321;
        let v326 = v925 - v306 * v321;
        let v327 = ((bz * v324 - by * v325) * n0[0] + (v325 * bx - bz * v326) * n0[1])
            + (by * v326 - v324 * bx) * n0[2];
        let v331 = ((az * v324 - ay * v325) * n0[0] + (v325 * ax - az * v326) * n0[1])
            + (ay * v326 - v324 * ax) * n0[2];
        let v332 = isneg((v327.abs() - ah) as f32) & isneg((v331.abs() - v930) as f32);
        let sel;
        if s.lp {
            let a1 = (-v321) as f32;
            let a2 = (s.t_den * v321 - v316 * s.t_num) as f32;
            sel = isneg(a1) & isneg(a2);
            let keep = sel ^ 1;
            s.deep = (sel & (v332 ^ 1)) | (s.deep & keep);
            let (fs, fk) = (fl(sel), fl(keep));
            s.nrm[2] = s.nrm[2] * fk + v308 * fs;
            s.nrm[0] = s.nrm[0] * fk + v306 * fs;
            s.nrm[1] = s.nrm[1] * fk + v307 * fs;
        } else {
            sel = v332 & isneg((v316 * s.t_num - s.t_den * v321) as f32);
        }
        s.set_best(sel, 0xC0);
        s.take(sel, v321, v316);
        v321 += d(v866);
    }
}

/// Phase 4 (both features ≠ 0x43, axes not parallel): rim against rim. The squared distance of
/// the moving rim point of A from B's rim plane / axis gives a quartic in t.
#[allow(clippy::too_many_lines)]
fn phase_rim_rim(s: &mut St, p: &UnprojParams) {
    let (a, b) = (s.c[0], s.c[1]);
    let dvec = if s.lp {
        if s.deep != 0 {
            let v = [
                a.center.x - b.center.x,
                a.center.y - b.center.y,
                a.center.z - b.center.z,
            ];
            let l2 = (v[0] * v[0] + v[1] * v[1]) + v[2] * v[2];
            let (x, y, z) = if l2 > 1e-20 {
                let inv = 1.0 / l2.sqrt();
                (v[0] * inv, v[1] * inv, v[2] * inv)
            } else {
                (0.0, 0.0, 1.0)
            };
            [d(x), d(y), d(z)]
        } else {
            s.nrm
        }
    } else {
        [d(p.dir.x), d(p.dir.y), d(p.dir.z)]
    };
    let [x, y, z] = dvec; // v902, v867, v886
    let isf = s.inv_sin as f32;
    let cf = s.cos_ab as f32;
    let v358 = (a.axis.x * cf - b.axis.x) * isf;
    let v361 = (a.axis.y * cf - b.axis.y) * isf;
    let v360 = (a.axis.z * cf - b.axis.z) * isf;
    let v364 = d(v360 * a.axis.y - v361 * a.axis.z);
    let v365 = d(v361 * a.axis.x - v358 * a.axis.y);
    let v895 = d(v358 * a.axis.z - v360 * a.axis.x);
    let v916 = d(b.axis.x * isf);
    let v914 = d(b.axis.y * isf);
    let v900 = d(b.axis.z * isf);
    let (u0, u1, u2) = (d(v358), d(v361), d(v360));
    s.p4 = P4 {
        u: [u0, u1, u2],
        w: [v364, v895, v365],
        bs: [v916, v914, v900],
        ar: d(a.r),
    };
    let v925 = (v916 * x + v914 * y) + v900 * z;
    let v904 = (u0 * x + u1 * y) + u2 * z;
    let v884 = (v364 * x + v895 * y) + v365 * z;
    let v885 = (x * x + y * y) + z * z;
    let br2 = d(b.r) * d(b.r); // v898
    let ar2 = d(a.r) * d(a.r); // v919
    let tm = d(p.tmax);
    for ja in [-1i32, 1] {
        let sa = ja as f32;
        let an = [d(a.axis.x * sa), d(a.axis.y * sa), d(a.axis.z * sa)];
        let ahh = d(a.hh);
        let pa = [
            d((d(a.center.x) + an[0] * ahh) as f32),
            d((d(a.center.y) + an[1] * ahh) as f32),
            d((d(a.center.z) + an[2] * ahh) as f32),
        ]; // v887..v889
        for jb in [-1i32, 1] {
            let sb = jb as f32;
            let bn = [d(b.axis.x * sb), d(b.axis.y * sb), d(b.axis.z * sb)];
            let bhh = d(b.hh);
            let qb = [
                d((d(b.center.x) + bhh * bn[0]) as f32),
                d((d(b.center.y) + bhh * bn[1]) as f32),
                d((d(b.center.z) + bhh * bn[2]) as f32),
            ]; // v890..v892
            let v388 = pa[0] - qb[0];
            let v912 = pa[1] - qb[1];
            let v901 = pa[2] - qb[2];
            // inline polynomial products (the 0.0 + … of the template code turn −0 into +0)
            let v389 = 0.0 + ((v912 * u1 + v388 * u0) + v901 * u2);
            let v390 = 0.0 + ((v912 * v914 + v388 * v916) + v901 * v900);
            let v391 = 0.0 + ((v912 * v895 + v388 * v364) + v901 * v365);
            let v1023 = 0.0 + 0.0 * 2.0;
            let v1024 = 0.0 + ((v912 * y + v388 * x) + v901 * z) * 2.0;
            let dd2 = (v912 * v912 + v388 * v388) + v901 * v901;
            let v1030 = (0.0 + v390 * v389) * 2.0 + (v1023 + dd2);
            let v1031 = v1024 + ((0.0 + v904 * v390) + v925 * v389) * 2.0;
            let v1032 = v885 + (0.0 + v925 * v904) * 2.0;
            let v988 = v1030 + ar2;
            let v392 = v988 - br2;
            let v907 = 0.0 + v1032 * v1032;
            let v395 = (0.0 + v1031 * v392) + v1031 * v392;
            let v396 = ((0.0 + v1032 * v392) + v1031 * v1031) + v1032 * v392;
            let v893 = (0.0 + v1031 * v1032) + v1031 * v1032;
            let v397 = 0.0 + v884 * v884;
            let v398 = (0.0 + v884 * v391) + v884 * v391;
            let v979 = 0.0 + v925 * v925;
            let v399 = 0.0 + v392 * v392;
            let v978 = (0.0 + v925 * v390) + v925 * v390;
            let v977 = 0.0 + v390 * v390;
            let v400 = 0.0 + v391 * v391;
            let v401 = -(v977 - ar2);
            let (m978, m979) = (-v978, -v979);
            let coef = [
                1.0,
                (0.0 + v401 * v400) * 4.0 - v399,
                ((0.0 + m978 * v400) + v401 * v398) * 4.0 - v395,
                (((0.0 + m979 * v400) + m978 * v398) + v401 * v397) * 4.0 - v396,
                ((0.0 + m979 * v398) + m978 * v397) * 4.0 - v893,
                (0.0 + m979 * v397) * 4.0 - v907,
            ];
            if solver::has_roots(&coef, 0.0, tm) == 0 {
                continue;
            }
            let mut roots = [0.0f64; 4];
            let n = solver::find_roots(&coef, 0.0, tm, &mut roots);
            if n == 0 {
                continue;
            }
            let code = (ja + 1) | (((jb + 1) | 0x100) >> 1);
            if s.lp {
                let mut hit = 0u32;
                let mut t = 0.0;
                let bhh999 = d(b.hh * f32::from_bits(0x3F7F_BE77));
                for k in (0..n.max(0) as usize).rev() {
                    t = roots[k];
                    let v435 = v388 + x * t;
                    let yy = y * t + v912;
                    let zz = z * t + v901;
                    let v436 = (v916 * v435 + yy * v914) + zz * v900;
                    let ey = u1 * v436 + yy;
                    let ex = u0 * v436 + v435;
                    let ez = u2 * v436 + zz;
                    let v437 = (br2 - ((ey * ey + ex * ex) + ez * ez)) as f32;
                    let py = (y * t + pa[1]) - d(b.center.y);
                    let px = (pa[0] + x * t) - d(b.center.x);
                    let pz = (z * t + pa[2]) - d(b.center.z);
                    let h = (d(b.axis.y) * py + d(b.axis.x) * px) + d(b.axis.z) * pz;
                    let v439 = (bhh999 - h.abs()) as f32;
                    hit = isneg(v437) & isneg(v439);
                    if hit != 0 {
                        break;
                    }
                }
                let v440 = (s.t_den * t - s.t_num) as f32;
                let sel = hit & (s.deep | isneg(v440));
                s.deep &= sel ^ 1;
                s.set_best(sel, code);
                let (fs, fk) = (fl(sel), fl(sel ^ 1));
                s.nrm[0] = s.nrm[0] * fk + x * fs;
                s.nrm[1] = s.nrm[1] * fk + y * fs;
                s.nrm[2] = s.nrm[2] * fk + z * fs;
                s.take(sel, t, 1.0);
            } else {
                // Bug kept: the largest root is taken without the rim / axis checks.
                let t = roots[(n - 1) as usize];
                let sel = isneg((s.t_num - s.t_den * t) as f32);
                s.set_best(sel, code);
                s.take(sel, t, 1.0);
            }
        }
    }
}

/// Case 0: cap face of cylinder `best` against the other's rim.
fn result_cap(s: &St, p: &UnprojParams, t: f64, out: &mut UnprojResult) {
    let k = s.best as usize;
    let c = s.c[k];
    let o = s.c[k ^ 1];
    let sg = (1 - 2 * s.best)
        * sgnnz((p.dir.y * o.axis.y + p.dir.x * o.axis.x) + p.dir.z * o.axis.z);
    let sf = sg as f32;
    let u = [o.axis.x * sf, o.axis.y * sf, o.axis.z * sf]; // v943..
    let mut h = (u[1] * c.axis.y + c.axis.x * u[0]) + u[2] * c.axis.z;
    if lt_or_nan_d(d(h).abs(), d(0.01)) {
        h = ((c.center.y - o.center.y) * c.axis.y + c.axis.x * (c.center.x - o.center.x))
            + (c.center.z - o.center.z) * c.axis.z;
    }
    let sc = (-sgnnz(h)) as f32;
    let cn = [c.axis.x * sc, c.axis.y * sc, c.axis.z * sc]; // v940..
    let hh = d(c.hh);
    let pc = [
        d(c.center.x) + d(cn[0]) * hh,
        d(c.center.y) + d(cn[1]) * hh,
        d(c.center.z) + d(cn[2]) * hh,
    ];
    out.t = t;
    let v559 = c.r / s.v872[k];
    let v560 = (f64::from((k ^ 1) as i32) * t) as f32;
    let f = &s.rim_dir[3 * k..3 * k + 3];
    out.pt = Vec3::new(
        (d(p.dir.x * v560) + (d(f[0] * v559) + pc[0])) as f32,
        (d(p.dir.y * v560) + (d(f[1] * v559) + pc[1])) as f32,
        (d(p.dir.z * v560) + (d(f[2] * v559) + pc[2])) as f32,
    );
    let g = f64::from(2 * s.best - 1);
    out.n = Vec3::new(
        (g * d(u[0])) as f32,
        (g * d(u[1])) as f32,
        (g * d(u[2])) as f32,
    );
    let hc = (cn[1] * c.axis.y + cn[0] * c.axis.x) + cn[2] * c.axis.z;
    out.i_feature[k] = (isneg(hc) ^ 1) as i32 | 0x20;
    let ho = (u[1] * o.axis.y + u[0] * o.axis.x) + u[2] * o.axis.z;
    out.i_feature[k ^ 1] = ((isneg(ho) ^ 1) as i32 + 1) | 0x40;
}

/// Case 0x40: rim of cylinder `(best >> 1) & 1` against the other's side.
fn result_rim_side(s: &St, p: &UnprojParams, t: f64, out: &mut UnprojResult) {
    let k = ((s.best >> 1) & 1) as usize;
    let v515 = ((-2 * s.best) & 2) - 1;
    out.t = t;
    let c = s.c[k];
    let n0 = s.n0;
    let nf = [n0[0] as f32, n0[1] as f32, n0[2] as f32];
    let sgc = f64::from(sgnnz(s.cos_ab as f32));
    let v524 = (sgc * s.v917) as f32;
    let v527 = (nf[2] * c.axis.y - nf[1] * c.axis.z) * v524;
    let v528 = (c.axis.z * nf[0] - nf[2] * c.axis.x) * v524;
    let v529 = (nf[1] * c.axis.x - c.axis.y * nf[0]) * v524;
    let v525 = n0[1] * s.v918;
    let v526 = n0[2] * s.v918;
    let v918x = s.v918 * n0[0];
    let v530 = (f64::from(1 - k as i32) * t) as f32;
    let v531 = v515 as f32 * c.hh;
    out.pt = Vec3::new(
        ((d((c.axis.x * v531 + c.center.x) + p.dir.x * v530) + v918x) as f32) + v527,
        ((d((c.axis.y * v531 + c.center.y) + p.dir.y * v530) + v525) as f32) + v528,
        ((d((c.axis.z * v531 + c.center.z) + p.dir.z * v530) + v526) as f32) + v529,
    );
    let o = s.c[k ^ 1];
    let v539 = (f64::from(k as i32) * out.t) as f32;
    let sg = (1 - 2 * k as i32) as f32;
    let nx = sg * ((p.dir.x * v539 + o.center.x) - out.pt.x);
    let ny = ((p.dir.y * v539 + o.center.y) - out.pt.y) * sg;
    let nz = ((p.dir.z * v539 + o.center.z) - out.pt.z) * sg;
    let h = (nx * o.axis.x + o.axis.y * ny) + nz * o.axis.z;
    out.n = Vec3::new(nx - o.axis.x * h, ny - o.axis.y * h, nz - o.axis.z * h);
    out.i_feature[k] = ((v515 + 1) | 0x40) >> 1;
    out.i_feature[k ^ 1] = 0x40;
}

/// Case 0x80: rim against rim.
fn result_rim_rim(s: &St, p: &UnprojParams, t: f64, out: &mut UnprojResult) {
    let (a, b) = (s.c[0], s.c[1]);
    let sa = (s.best & 2) - 1;
    let sb = ((-2 * s.best) & 2) - 1;
    out.t = t;
    let tf = t as f32;
    let ah = sa as f32 * a.hh;
    let pa = [
        d((a.axis.x * ah + a.center.x) + p.dir.x * tf),
        d((a.axis.y * ah + a.center.y) + p.dir.y * tf),
        d((a.axis.z * ah + a.center.z) + p.dir.z * tf),
    ];
    let bh = sb as f32 * b.hh;
    let qb = [
        d(b.axis.x * bh + b.center.x),
        d(b.axis.y * bh + b.center.y),
        d(b.axis.z * bh + b.center.z),
    ];
    let p4 = s.p4;
    let v489 = ((pa[1] - qb[1]) * p4.bs[1] + (pa[0] - qb[0]) * p4.bs[0]) + (pa[2] - qb[2]) * p4.bs[2];
    let mut pt = Vec3::new(
        (p4.u[0] * v489 + pa[0]) as f32,
        (p4.u[1] * v489 + pa[1]) as f32,
        (p4.u[2] * v489 + pa[2]) as f32,
    );
    let v497 = (((qb[1] - d(pt.y)) * p4.w[1] + (qb[0] - d(pt.x)) * p4.w[0])
        + (qb[2] - d(pt.z)) * p4.w[2]) as f32;
    let v498 = (p4.ar * p4.ar - v489 * v489).abs().sqrt() * f64::from(sgnnz(v497));
    pt.x = (d(pt.x) + v498 * p4.w[0]) as f32;
    pt.y = (d(pt.y) + p4.w[1] * v498) as f32;
    pt.z = (d(pt.z) + p4.w[2] * v498) as f32;
    out.pt = pt;
    let ax_ = (d(pt.x) - qb[0]) as f32;
    let ay_ = (d(pt.y) - qb[1]) as f32;
    let az_ = (d(pt.z) - qb[2]) as f32;
    let bx_ = (d(pt.x) - pa[0]) as f32;
    let by_ = (d(pt.y) - pa[1]) as f32;
    let bz_ = (d(pt.z) - pa[2]) as f32;
    let v506 = ay_ * b.axis.x - ax_ * b.axis.y;
    let v507 = ax_ * b.axis.z - az_ * b.axis.x;
    let v493 = az_ * b.axis.y - ay_ * b.axis.z;
    let v491 = by_ * a.axis.x - bx_ * a.axis.y;
    let v489f = bz_ * a.axis.y - by_ * a.axis.z;
    let w = bx_ * a.axis.z - bz_ * a.axis.x;
    let mut n = Vec3::new(
        w * v506 - v491 * v507,
        v491 * v493 - v506 * v489f,
        v507 * v489f - w * v493,
    );
    let tf2 = out.t as f32;
    let h = (((b.center.y - a.center.y) - p.dir.y * tf2) * n.y
        + ((b.center.x - a.center.x) - p.dir.x * tf2) * n.x)
        + ((b.center.z - a.center.z) - p.dir.z * tf2) * n.z;
    let sg = sgnnz(h) as f32;
    n = Vec3::new(sg * n.x, sg * n.y, sg * n.z);
    out.n = n;
    out.i_feature[0] = ((sa + 1) | 0x40) >> 1;
    out.i_feature[1] = ((sb + 1) | 0x40) >> 1;
}

/// Case 0xC0: side against side.
fn result_side_side(s: &St, p: &UnprojParams, t: f64, out: &mut UnprojResult) {
    let (a, b) = (s.c[0], s.c[1]);
    let n0 = s.n0;
    out.t = t;
    let tf = t as f32;
    let x = d((b.center.x - a.center.x) - p.dir.x * tf);
    let y = d((b.center.y - a.center.y) - p.dir.y * tf);
    let z = d((b.center.z - a.center.z) - p.dir.z * tf);
    let (ax, ay, az) = (d(a.axis.x), d(a.axis.y), d(a.axis.z));
    let cxp = az * y - ay * z;
    let cyp = z * ax - az * x;
    let czp = ay * x - y * ax;
    let v466 = ((cyp * n0[1] + cxp * n0[0]) + n0[2] * czp) as f32;
    let mut pt = Vec3::new(
        b.center.x + b.axis.x * v466,
        b.center.y + b.axis.y * v466,
        b.center.z + b.axis.z * v466,
    );
    let h = (n0[1] * d(b.center.y - a.center.y) + d(b.center.x - a.center.x) * n0[0])
        + n0[2] * d(b.center.z - a.center.z);
    let sg = f64::from(sgnnz(h as f32));
    let n = Vec3::new((sg * n0[0]) as f32, (sg * n0[1]) as f32, (sg * n0[2]) as f32);
    let r = b.r;
    pt.x -= n.x * r;
    pt.y -= n.y * r;
    pt.z -= n.z * r;
    out.pt = pt;
    out.n = n;
    out.i_feature = [0x40, 0x40];
}

/// The contact-area part (7th argument present).
#[allow(clippy::too_many_lines)]
fn contact_area(s: &St, p: &UnprojParams, out: &mut UnprojResult, ar: &mut ContactArea) -> i32 {
    let (a, b) = (s.c[0], s.c[1]);
    ar.npt = 0;
    let tf = out.t as f32;
    let dv = [
        d((p.dir.x * tf + a.center.x) - b.center.x),
        d((p.dir.y * tf + a.center.y) - b.center.y),
        d((p.dir.z * tf + a.center.z) - b.center.z),
    ]; // v884, v885, v877
    let v579 = d((1.0 - p.max_cos) * 2.0);
    if lt_or_nan_d(s.sin_ab * s.sin_ab, v579) {
        ar.n = out.n;
        // parallel caps
        let hd = (((d(a.axis.x) * dv[0] + d(a.axis.y) * dv[1]) + d(a.axis.z) * dv[2]).abs()
            - d(b.hh + a.hh))
            .abs();
        let thr = d((min_idiom(a.hh, b.hh) * 0.05) * s.both_ok as f32);
        if lt_or_nan_d(hd, thr) {
            if area_caps(s, p, out, ar, dv) {
                return 1;
            }
            ar.npt = 0;
        }
        // parallel sides
        let hf = ((dv[0] * d(a.axis.x) + d(a.axis.y) * dv[1]) + d(a.axis.z) * dv[2]) as f32;
        let rs = a.r + b.r;
        let ex = dv[0] - d(a.axis.x * hf);
        let ey = dv[1] - d(a.axis.y * hf);
        let ez = dv[2] - d(a.axis.z * hf);
        let v = (((ex * ex + ey * ey) + ez * ez) - d(rs * rs)).abs();
        if lt_or_nan_d(v, d(min_idiom(a.r, b.r) * 0.05)) {
            area_sides(s, p, out, ar, dv);
            if ar.npt != 0 {
                return 1;
            }
        }
    }
    let cos2 = s.cos_ab * s.cos_ab;
    if !lt_or_nan_d(cos2, v579) {
        return 1;
    }
    // a cap against a side
    let mut nd = [-dv[0], -dv[1], -dv[2]];
    for i in 0..2usize {
        let c = s.c[i];
        let o = s.c[i ^ 1];
        let v731 = d(o.r + c.hh);
        let dc = (nd[1] * d(c.axis.y) + nd[0] * d(c.axis.x)) + nd[2] * d(c.axis.z);
        let lo = v731 * 0.99;
        let hi = v731 * 1.01;
        let v732 = (((lo + hi) - dc.abs() * 2.0).abs() - (lo - hi).abs()) as f32;
        if isneg(v732) & s.feat_ok[i] != 0 {
            let v733 = 1.0 - cos2;
            let doc = (nd[1] * d(o.axis.y) + nd[0] * d(o.axis.x)) + nd[2] * d(o.axis.z);
            let v736 = doc - dc * s.cos_ab;
            let ndf = [nd[0] as f32, nd[1] as f32, nd[2] as f32];
            let dcf = (c.axis.y * ndf[1] + c.axis.x * ndf[0]) + c.axis.z * ndf[2];
            let v738 = v736 * v736
                - ((((nd[1] * nd[1] + nd[0] * nd[0]) + nd[2] * nd[2]) - d(dcf * dcf))
                    - d(c.r * c.r))
                    * v733;
            if v738 > 0.0 {
                let v740 = sgnnz(dc as f32);
                let v736sq = v736 * v736;
                let v742 = f64::from(sgnnz((v738 - v736sq) as f32));
                let v743 = v736.abs() * v742 + d(o.hh) * v733;
                if lt_or_nan_d(v742 * v738, v743.abs() * v743) {
                    let v744 = v738.sqrt();
                    let inv = 1.0 / v733;
                    for j in [-1i32, 1] {
                        let v746 = (f64::from(j) * v744 - v736) * inv;
                        if lt_or_nan_d(v746.abs(), d(o.hh)) {
                            let h = v746 as f32;
                            let k = ar.npt as usize;
                            area_slot(ar, k);
                            ar.pt[k] = Vec3::new(o.axis.x * h, o.axis.y * h, o.axis.z * h);
                            ar.feature[i ^ 1][k] = v740 | 0x20;
                            ar.feature[i][k] = (((j + 1) >> 1) + 1) | 0x40;
                            ar.npt += 1;
                        }
                    }
                }
                for j in [-1i32, 1] {
                    let v752 = j as f32 * o.hh;
                    let x_ = d(o.axis.x * v752) + nd[0];
                    let y_ = d(o.axis.y * v752) + nd[1];
                    let z_ = d(o.axis.z * v752) + nd[2];
                    let (cx, cy, cz) = (d(c.axis.x), d(c.axis.y), d(c.axis.z));
                    let e1 = cz * y_ - cy * z_;
                    let e2 = cx * z_ - x_ * cz;
                    let e3 = x_ * cy - cx * y_;
                    if lt_or_nan_d((e1 * e1 + e2 * e2) + e3 * e3, d(c.r * c.r)) {
                        let k = ar.npt as usize;
                        area_slot(ar, k);
                        ar.pt[k] = Vec3::new(o.axis.x * v752, o.axis.y * v752, o.axis.z * v752);
                        ar.feature[i ^ 1][k] = 0x40;
                        ar.feature[i][k] = ((j + 1) | 0x40) >> 1;
                        ar.npt += 1;
                    }
                }
                if ar.npt > 0 {
                    let v740f = v740 as f32;
                    let v775 = (f64::from(i as i32) * out.t) as f32;
                    for k in 0..ar.npt as usize {
                        let q = ar.pt[k];
                        let qx = (d(q.x) + nd[0]) as f32;
                        let qy = (d(q.y) + nd[1]) as f32;
                        let qz = (d(q.z) + nd[2]) as f32;
                        let v774 = ((qy * c.axis.y + qx * c.axis.x) + qz * c.axis.z) - v740f * c.hh;
                        ar.pt[k] = Vec3::new(
                            ((d(p.dir.x * v775 + o.center.x) - d(c.axis.x * v774)) as f32) + q.x,
                            ((d(p.dir.y * v775 + o.center.y) - d(c.axis.y * v774)) as f32) + q.y,
                            ((d(p.dir.z * v775 + o.center.z) - d(c.axis.z * v774)) as f32) + q.z,
                        );
                    }
                }
                if ar.npt > 1 {
                    area_cap_side_normals(s, p, out, ar, i);
                    return 1;
                }
                ar.npt = 0;
            }
        }
        nd = [-nd[0], -nd[1], -nd[2]];
    }
    1
}

/// Near-parallel caps: rim samples of each cap inside the other rim, plus the two rim–rim
/// intersections. Returns true when it produced an area (`npt > 1`).
#[allow(clippy::too_many_lines)]
fn area_caps(
    s: &St,
    p: &UnprojParams,
    out: &mut UnprojResult,
    ar: &mut ContactArea,
    dv: [f64; 3],
) -> bool {
    let (a, b) = (s.c[0], s.c[1]);
    let n0 = s.n0;
    let (dxf, dyf, dzf) = (dv[0] as f32, dv[1] as f32, dv[2] as f32);
    let v585 = -sgnnz((dyf * a.axis.y + dxf * a.axis.x) + dzf * a.axis.z);
    let v586 = sgnnz((dyf * b.axis.y + dxf * b.axis.x) + dzf * b.axis.z);
    let v869 = v585 as f32;
    let v587 = a.hh * v869;
    let tf = out.t as f32;
    let pa = [
        d((p.dir.x * tf + a.center.x) + a.axis.x * v587),
        d((p.dir.y * tf + a.center.y) + a.axis.y * v587),
        d((p.dir.z * tf + a.center.z) + a.axis.z * v587),
    ]; // v887..
    let v868 = v586 as f32;
    let v592 = v868 * b.hh;
    let qb = [
        d(v592 * b.axis.x + b.center.x),
        d(v592 * b.axis.y + b.center.y),
        d(v592 * b.axis.z + b.center.z),
    ]; // v890..
    let nf = [n0[0] as f32, n0[1] as f32, n0[2] as f32];
    let ctrs = [pa, qb];
    for i in 0..2usize {
        let c = s.c[i];
        let o = s.c[i ^ 1];
        let v595 = i as i32;
        let v597 = -(i as i32);
        let f_other = (((((v597 & v585) | (v586 & (v595 - 1))) + 1) >> 1) + 1) | 0x40;
        let f_self = ((((v585 & (v595 - 1)) | (v597 & v586)) + 1) | 0x40) >> 1;
        let w = [
            d(c.axis.y * nf[2] - c.axis.z * nf[1]),
            d(c.axis.z * nf[0] - c.axis.x * nf[2]),
            d(c.axis.x * nf[1] - c.axis.y * nf[0]),
        ];
        let ctr = ctrs[i];
        let octr = ctrs[i ^ 1];
        let rr = {
            let r = o.r * f32::from_bits(0x3F81_47AE); // 1.01
            r * r
        };
        let (mut cc, mut ss) = (d(c.r), 0.0f64);
        for _ in 0..8 {
            let k = ar.npt as usize;
            area_slot(ar, k);
            ar.pt[k] = Vec3::new(
                ((cc * n0[0] + ctr[0]) + ss * w[0]) as f32,
                ((n0[1] * cc + ctr[1]) + ss * w[1]) as f32,
                ((n0[2] * cc + ctr[2]) + ss * w[2]) as f32,
            );
            ar.feature[i ^ 1][k] = f_other;
            ar.feature[i][k] = f_self;
            if v595 != 0 {
                // project onto A's cap plane
                let q = ar.pt[k];
                let ex = (d(q.x) - pa[0]) as f32;
                let ey = (d(q.y) - pa[1]) as f32;
                let ez = (d(q.z) - pa[2]) as f32;
                let h = (ex * a.axis.x + ey * a.axis.y) + ez * a.axis.z;
                ar.pt[k] = Vec3::new(
                    (d(a.axis.x * h) + pa[0]) as f32,
                    (d(a.axis.y * h) + pa[1]) as f32,
                    (d(a.axis.z * h) + pa[2]) as f32,
                );
            }
            let q = ar.pt[k];
            let e1 = (d(q.x) - octr[0]) as f32;
            let e2 = (d(q.y) - octr[1]) as f32;
            let e3 = (d(q.z) - octr[2]) as f32;
            ar.npt += isneg(((e1 * e1 + e2 * e2) + e3 * e3) - rr) as i32;
            let c2 = (cc - ss) * std::f64::consts::FRAC_1_SQRT_2;
            let s2 = (ss + cc) * std::f64::consts::FRAC_1_SQRT_2;
            cc = c2;
            ss = s2;
        }
    }
    // the two rim circles' intersection points
    let v646 = qb[0] - pa[0];
    let v644 = qb[1] - pa[1];
    let v872 = qb[2] - pa[2];
    let v647 = (v646 * v646 + v644 * v644) + v872 * v872;
    let rs = a.r + b.r;
    let rd = a.r - b.r;
    let v648 = d(rs * rs) * d(0.999);
    let v649 = d(rd * rd) * d(1.001);
    let v650 = (((v649 + v648) - v647 * 2.0).abs() - (v649 - v648).abs()) as f32;
    if isneg(v650) != 0 {
        let v651 = v647.sqrt();
        let f0 = ((v585 + 1) | 0x40) >> 1;
        let f1 = ((v586 + 1) | 0x40) >> 1;
        let v655 = 1.0 / v651;
        let ar2 = a.r * a.r;
        let v1038 = (d(ar2 - b.r * b.r) * v655 + v651) * 0.5;
        let v669 = (d(ar2) - v1038 * v1038).sqrt();
        let uy = v655 * v644;
        let ux = v655 * v646;
        let uz = v655 * v872;
        let (uxf, uyf, uzf) = (ux as f32, uy as f32, uz as f32);
        let v665 = d(a.axis.y * uzf - a.axis.z * uyf);
        let cy_ = d(a.axis.z * uxf - a.axis.x * uzf);
        let cz_ = d(a.axis.x * uyf - a.axis.y * uxf);
        let hx = ux * v1038;
        let v667 = v1038 * uy;
        let v668 = v1038 * uz;
        for j in [-1i32, 1] {
            let v671 = f64::from(j) * v669;
            let k = ar.npt as usize;
            area_slot(ar, k);
            ar.pt[k] = Vec3::new(
                ((pa[0] + hx) + v665 * v671) as f32,
                ((pa[1] + v667) + v671 * cy_) as f32,
                ((pa[2] + v668) + v671 * cz_) as f32,
            );
            ar.feature[0][k] = f0;
            ar.feature[1][k] = f1;
            ar.npt += 1;
        }
    }
    if ar.npt > 1 {
        out.n = Vec3::new(a.axis.x * v869, a.axis.y * v869, a.axis.z * v869);
        ar.n = Vec3::new(b.axis.x * v868, b.axis.y * v868, b.axis.z * v868);
        return true;
    }
    false
}

/// Parallel sides: the cap-edge points of each cylinder that lie within the other's height.
fn area_sides(
    s: &St,
    p: &UnprojParams,
    out: &UnprojResult,
    ar: &mut ContactArea,
    dv: [f64; 3],
) {
    let a = s.c[0];
    let hf = ((dv[0] * d(a.axis.x) + d(a.axis.y) * dv[1]) + d(a.axis.z) * dv[2]) as f32;
    let x = (d(a.axis.x * hf) - dv[0]) as f32;
    let y = (d(a.axis.y * hf) - dv[1]) as f32;
    let z = (d(a.axis.z * hf) - dv[2]) as f32;
    let l2 = (x * x + y * y) + z * z;
    let (nx, ny, nz) = if l2 > 1e-20 {
        let inv = 1.0 / l2.sqrt();
        (x * inv, inv * y, inv * z)
    } else {
        (0.0, 0.0, 1.0)
    };
    let (nx, ny, nz) = (d(nx), d(ny), d(nz));
    for i in 0..2usize {
        let sg = 1 - 2 * i as i32; // v699
        let tk = 1 - i as i32; // v697
        let c = s.c[i];
        let o = s.c[i ^ 1];
        let fsg = f64::from(sg);
        let sd = [dv[0] * fsg, dv[1] * fsg, dv[2] * fsg];
        for j in [-1i32, 1] {
            let v705 = j as f32 * c.hh;
            let h = ((d(o.axis.y) * (d(c.axis.y * v705) + sd[1])
                + d(o.axis.x) * (d(c.axis.x * v705) + sd[0]))
                + d(o.axis.z) * (d(c.axis.z * v705) + sd[2]))
                .abs();
            if lt_or_nan_d(h, d(o.hh * f32::from_bits(0x3F80_4189))) {
                let v706 = d(sg as f32 * c.r);
                let v711 = (f64::from(tk) * out.t) as f32;
                let k = ar.npt as usize;
                area_slot(ar, k);
                ar.pt[k] = Vec3::new(
                    (d((p.dir.x * v711 + c.center.x) + c.axis.x * v705) + v706 * nx) as f32,
                    (d((p.dir.y * v711 + c.center.y) + c.axis.y * v705) + v706 * ny) as f32,
                    (d((p.dir.z * v711 + c.center.z) + c.axis.z * v705) + v706 * nz) as f32,
                );
                ar.feature[i ^ 1][k] = 0x40;
                ar.feature[i][k] = ((j + 1) | 0x40) >> 1;
                ar.npt += 1;
            }
        }
    }
}

/// Normals of the cap-against-side area of cylinder `i`'s cap; projects the points for `i = 1`.
fn area_cap_side_normals(
    s: &St,
    p: &UnprojParams,
    out: &mut UnprojResult,
    ar: &mut ContactArea,
    i: usize,
) {
    let c = s.c[i];
    let o = s.c[i ^ 1];
    let p0 = ar.pt[0];
    let mut nn = [Vec3::ZERO; 2];
    let v781 = (f64::from(1 - i as i32) * out.t) as f32;
    let h = (((p0.y - c.center.y) - p.dir.y * v781) * c.axis.y
        + ((p0.x - c.center.x) - p.dir.x * v781) * c.axis.x)
        + ((p0.z - c.center.z) - p.dir.z * v781) * c.axis.z;
    let sg = sgnnz(h) as f32;
    nn[i] = Vec3::new(c.axis.x * sg, c.axis.y * sg, c.axis.z * sg);
    let v786 = (f64::from(i as i32) * out.t) as f32;
    let h2 = (((p0.x - o.center.x) - p.dir.x * v786) * o.axis.x
        + ((p0.y - o.center.y) - p.dir.y * v786) * o.axis.y)
        + ((p0.z - o.center.z) - p.dir.z * v786) * o.axis.z;
    let sg2 = sgnnz(h2) as f32;
    nn[i ^ 1] = Vec3::new(o.axis.x * sg2, o.axis.y * sg2, o.axis.z * sg2);
    let av = nn[i];
    let v795 = av.x * o.axis.z - av.z * o.axis.x;
    let v796 = av.z * o.axis.y - av.y * o.axis.z;
    let v797 = av.y * o.axis.x - av.x * o.axis.y;
    let m = Vec3::new(
        o.axis.y * v797 - o.axis.z * v795,
        v796 * o.axis.z - o.axis.x * v797,
        o.axis.x * v795 - v796 * o.axis.y,
    );
    nn[i ^ 1] = if (m.x * m.x + m.y * m.y) + m.z * m.z > 0.001 {
        normalized(m)
    } else {
        Vec3::new(-av.x, -av.y, -av.z)
    };
    out.n = nn[0];
    ar.n = nn[1];
    if i != 0 {
        let n = out.n;
        for k in 0..ar.npt.max(0) as usize {
            let q = ar.pt[k];
            let dd = ((out.pt.y - q.y) * n.y + (out.pt.x - q.x) * n.x) + (out.pt.z - q.z) * n.z;
            ar.pt[k] = Vec3::new(n.x * dd + q.x, n.y * dd + q.y, n.z * dd + q.z);
        }
    }
}

fn cell_cyl_cyl(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) -> i32 {
    unproject_cyl_cyl(p, as_cyl(a), fa, as_cyl(b), fb, out, area)
}

/// Fill the cells this group owns: `[0][cyl][cyl]`.
pub fn fill(t: &mut UnprojTables) {
    t.set(MODE_LINEAR, GeomType::Cylinder, GeomType::Cylinder, cell_cyl_cyl);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn feature_flag_and_sign0() {
        assert_eq!(feature_ok(-1), 1);
        assert_eq!(feature_ok(0x43), 0);
        assert_eq!(feature_ok(0x44), 1);
        assert_eq!(feature_ok(i32::MIN + 0x43), 0);
        assert_eq!(sign0(0.0), 0);
        assert_eq!(sign0(-0.0), 0);
        assert_eq!(sign0(2.0), 1);
        assert_eq!(sign0(-2.0), -1);
    }

    #[test]
    fn table_ends() {
        let t = sin_table();
        assert_eq!(t[0], 0.0);
        assert_eq!(t[1024], 1.0);
        assert_eq!(t[1].to_bits(), 0x3AC9_0FD6);
    }
}
