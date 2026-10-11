//! Rotational ray–cylinder unprojection (P2b work group `rot_cyl`).
//!
//! | cell | x64 | x86 (1.2) | function |
//! |---|---|---|---|
//! | `[1][ray][cyl]` | 0x351A0C10 (14 408 B) | 0x351AD1A0 | [`unproject_ray_cyl_rot`] |
//! | `[1][cyl][ray]` | 0x351A4460 (447 B) | 0x351B5120 | 447-byte rotational swap wrapper → [`common::swap_rot`](super::common::swap_rot) |
//!
//! The wrapper's disassembly is identical (apart from the callee) to the shared 447-byte wrapper
//! 0x351A0A40 that `common::swap_rot` ports.
//!
//! # sub_351A0C10
//!
//! The ray (segment `origin … origin + dir`) is rotated about the line (`params.center`,
//! `params.dir` = unit axis `a`) by φ; with `s = sin φ`, `c = √(1 − s²)` a point `p` moves on
//! `center + h + r·c + (a × r)·s` (`h` = axial part, `r` = radial part of `p − center`). The
//! routine looks for the **largest** `s ∈ [0, 1]` at which some feature pair touches (the best
//! value is kept as the fraction `num / den`, initially `0 / 1`; a candidate wins when the float
//! rounding of `num·den' − den·num'` has its sign bit set). `params.tmax`, `tmin` and `maxcos` are
//! not read; `min_pt_dist` is only used to skip the ray origin when it lies within
//! `min_pt_dist` of the rotation centre. All work is done in double precision (inputs widened
//! from float), the acceptance tests are sign-bit tests of float-rounded expressions.
//!
//! Phases (feature code `v12`, −1 = none):
//! 1. For each ray end (end point first, then the origin unless it is within `min_pt_dist` of the
//!    centre):
//!    * against the two cap planes (`j = ∓1`): `n·(P(φ) − capc) = 0` is solved as a quadratic
//!      in `s` (two roots, accepted when `0 < s < 1` in the right half-circle and the point lies
//!      inside the cap disc) → `v12 = end | (j + 1)` (0..3);
//!    * against the side: `|(P(φ) − cylc) × n|² = r²` gives `A(s)·c + B(s) = 0`, squared to the
//!      quartic `B² − (1 − s²)·A²·4` (built with the inlined `polynomial_tpl` arithmetic,
//!      including its `0.0 +` accumulators), roots in `[0, 1]` from sub_3514A2E0 /
//!      sub_35149170; a root is accepted when the point is between the caps and on the cylinder
//!      (±1 % radius tolerance) → `v12 = end | 0x10`.
//! 2. The ray line against the cylinder side line (the rotating line touches the cylinder
//!    surface): quartic from the line's moment, accepted when the closest point lies on the ray
//!    segment and between the caps → `v12 = 0x20`.
//! 3. The ray line against the two cap rims (`j = ∓1`): quartic, accepted when the touching
//!    point is on the segment → `v12 = 0x40 | (j + 1)/2`.
//!
//! Output (only when `v12 ≥ 0`; return 1): `t = sin φ`, `cos_t = √(1 − t²)` and
//! * cap vertex (`v12 < 0x10`): `t = num/den` (0 when `den == 0`), `pt` = rotated ray end,
//!   `n = ±axis` (+ for the bottom cap), features `0x80|end`, `0x41`/`0x42`;
//! * side vertex (0x10): `pt` = rotated end, `n` = radial direction from `pt` to the axis,
//!   normalised with sub_35021020, features `0x80|end`, `0x40`;
//! * side edge (0x20): `pt` = closest point of the rotated line to the cylinder axis, `n` = the
//!   normalised common perpendicular, flipped to point towards the cylinder centre; features
//!   `0x20`, `0x40`;
//! * rim (0x40/0x41): `pt` = rotated line ∩ cap plane, `n` = normalised
//!   `(line dir) × ((pt − capc) × axis)`, flipped towards the cylinder centre; features
//!   `0x20`, `0x20 | (j + 1)/2`.
//!
//! Nothing is written on failure (return 0); `params` is never written.
//!
//! The root buffer (`double[20]` at `rbp+0x398`) is shared by all solver calls; the solver only
//! writes it before the entries are read, so its initial (stack) contents do not matter.

use super::common::{
    as_cyl, as_ray, le_or_nan_d, lt_or_nan, lt_or_nan_d, normalize_in_place, swap_rot, ucomisd_eq,
};
use super::poly::{count_roots, find_roots, Poly4};
use super::{UnprojTables, MODE_ROTATIONAL};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{CylinderPrim, Primitive, RayPrim};
use crate::ids::GeomType;
use crate::math::Vec3;

/// Root buffer of the solver calls (`double[20]` at `rbp+0x398` in the binary; the solver writes
/// at most 4 entries).
type Roots = [f64; 4];

/// `if (sub_3514A2E0(&poly, 0.0, 1.0)) n = sub_35149170(&poly, 0.0, 1.0, roots, 20, 4, 0);`
/// — the number of roots in `[0, 1]` written to `roots` (0 when the Sturm count says none).
fn roots_01(poly: &Poly4, roots: &mut Roots) -> i32 {
    if count_roots(poly, 0.0, 1.0) == 0 {
        return 0;
    }
    find_roots(poly, 0.0, 1.0, roots, 20)
}

/// Sign bit of a float (the binary's `shr eax, 31` on stored floats).
#[inline]
fn sgn(x: f32) -> i32 {
    (x.to_bits() >> 31) as i32
}

/// The best-candidate update `den = !e·den + e·den'`, `num = !e·num + e·num'` (with the 0/1
/// flags converted to double, so a non-finite operand propagates like in the binary).
#[inline]
fn take(e: i32, num: &mut f64, den: &mut f64, num_new: f64, den_new: f64) {
    let ne = f64::from(e ^ 1);
    let ed = f64::from(e);
    *den = ne * *den + ed * den_new;
    *num = ne * *num + ed * num_new;
}

/// `v12 = (v12 & (e − 1)) | (−e & feat)`.
#[inline]
fn pick(v12: i32, e: i32, feat: i32) -> i32 {
    (v12 & (e - 1)) | (e.wrapping_neg() & feat)
}

/// Rotational ray–cylinder unprojection, sub_351A0C10 (x64), x86 0x351AD1A0. See the module
/// docs.
#[allow(
    clippy::too_many_lines,
    clippy::similar_names,
    clippy::many_single_char_names
)]
pub fn unproject_ray_cyl_rot(
    p: &mut UnprojParams,
    a: &RayPrim,
    _fa: i32,
    b: &CylinderPrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let hh = f64::from(b.hh);
    let r2 = f64::from(b.r * b.r);
    let (ax, ay, az) = (f64::from(p.dir.x), f64::from(p.dir.y), f64::from(p.dir.z));
    let (nx, ny, nz) = (
        f64::from(b.axis.x),
        f64::from(b.axis.y),
        f64::from(b.axis.z),
    );
    let dx = f64::from(b.center.x - p.center.x);
    let dy = f64::from(b.center.y - p.center.y);
    let dz = f64::from(b.center.z - p.center.z);

    // ---- ray ends relative to the centre: axial h, radial r, a × r
    let o0x = f64::from(a.origin.x - p.center.x);
    let o0y = f64::from(a.origin.y - p.center.y);
    let o0z = f64::from(a.origin.z - p.center.z);
    let t0 = (o0x * ax + o0y * ay) + o0z * az;
    let mut h = [[0.0f64; 3]; 2];
    let mut r = [[0.0f64; 3]; 2];
    let mut x = [[0.0f64; 3]; 2];
    h[0] = [t0 * ax, t0 * ay, t0 * az];
    r[0] = [o0x - h[0][0], o0y - h[0][1], o0z - h[0][2]];
    x[0] = [
        r[0][2] * ay - r[0][1] * az,
        r[0][0] * az - ax * r[0][2],
        ax * r[0][1] - r[0][0] * ay,
    ];
    let e1x = f64::from(a.dir.x) + o0x;
    let e1y = f64::from(a.dir.y) + o0y;
    let e1z = f64::from(a.dir.z) + o0z;
    let t1 = (e1x * ax + e1y * ay) + e1z * az;
    h[1] = [t1 * ax, t1 * ay, t1 * az];
    r[1] = [e1x - h[1][0], e1y - h[1][1], e1z - h[1][2]];
    x[1] = [
        r[1][2] * ay - r[1][1] * az,
        az * r[1][0] - ax * r[1][2],
        ax * r[1][1] - ay * r[1][0],
    ];

    let mut v12: i32 = -1;
    let mut num = 0.0f64; // v21 / v384
    let mut den = 1.0f64; // v390
    let mut roots: Roots = [0.0; 4];

    // ---- phase 1: the ray ends (end point first)
    for i in (0..2usize).rev() {
        let (hi, ri, xi) = (h[i], r[i], x[i]);
        let end = i as i32;
        let v41 = (ri[1] * ny + nx * ri[0]) + ri[2] * nz;
        let v44 = (nx * xi[0] + xi[1] * ny) + xi[2] * nz;
        let v41sq = v41 * v41;
        let v45 = v44 * v44 + v41sq;
        // caps
        for j in [-1i32, 1] {
            let v46 = f64::from(j) * hh;
            let v51 = (((hi[1] - dy) - ny * v46) * ny + nx * ((hi[0] - dx) - nx * v46))
                + ((hi[2] - dz) - nz * v46) * nz;
            let v52 = v51 * v44;
            let v53 = v52 * v52 - (v51 * v51 - v41sq) * v45;
            // comisd + jb: negative or NaN discriminant skips
            if lt_or_nan_d(v53, 0.0) {
                continue;
            }
            let v54 = v53.sqrt();
            let mut v56 = -v52 - v54;
            let v379 = v54 * 2.0;
            let v57 = v45 * v51;
            for _ in 0..2 {
                let v58 = ((v56 * 2.0 - v45).abs() - v45) as f32;
                let v59 = ((v56 * v44 + v57) * v41) as f32;
                if (v58.to_bits() & v59.to_bits()) >> 31 != 0 {
                    let v60 = (v45 * v45 - v56 * v56).sqrt();
                    let v61 = ((ri[1] * v60 + xi[1] * v56) + hi[1] * v45) - (ny * v46 + dy) * v45;
                    let v62 = ((ri[2] * v60 + xi[2] * v56) + hi[2] * v45) - (nz * v46 + dz) * v45;
                    let v63 = ((ri[0] * v60 + xi[0] * v56) + hi[0] * v45) - (dx + nx * v46) * v45;
                    let v64 = (((v61 * v61 + v63 * v63) + v62 * v62) - (v45 * v45) * r2) as f32;
                    let v66 = (num * v45 - den * v56) as f32;
                    let e = sgn(v64) & sgn(v66);
                    v12 = pick(v12, e, end | (j + 1));
                    take(e, &mut num, &mut den, v56, v45);
                }
                v56 += v379;
            }
        }
        // side
        let wx = (hi[1] - dy) * nz - (hi[2] - dz) * ny;
        let wy = nx * (hi[2] - dz) - nz * (hi[0] - dx);
        let wz = ny * (hi[0] - dx) - nx * (hi[1] - dy);
        let ux = ri[1] * nz - ri[2] * ny;
        let uy = nx * ri[2] - ri[0] * nz;
        let uz = ri[0] * ny - nx * ri[1];
        let vx = xi[1] * nz - xi[2] * ny;
        let vy = nx * xi[2] - xi[0] * nz;
        let vz = xi[0] * ny - nx * xi[1];
        let poly = side_vertex_poly([wx, wy, wz], [ux, uy, uz], [vx, vy, vz], r2);
        let n = roots_01(&poly, &mut roots);
        let lim_hi = r2 * 0.9801 + r2 * 1.0201;
        let lim_lo = (r2 * 0.9801 - r2 * 1.0201).abs();
        let mut k = n - 1;
        while k >= 0 {
            let s = roots[k as usize];
            let c = (1.0 - s * s).sqrt();
            let px = ((hi[0] + ri[0] * c) + xi[0] * s) - dx;
            let py = ((hi[1] + ri[1] * c) + xi[1] * s) - dy;
            let pz = ((hi[2] + ri[2] * c) + xi[2] * s) - dz;
            let f1 = (((py * ny + nx * px) + pz * nz).abs() - hh) as f32;
            let ca = nx * pz - px * nz;
            let cb = py * nz - pz * ny;
            let cc = px * ny - nx * py;
            let f2 = ((lim_hi - ((ca * ca + cb * cb) + cc * cc) * 2.0).abs() - lim_lo) as f32;
            let f3 = (num - den * s) as f32;
            let e = sgn(f1) & sgn(f2) & sgn(f3);
            v12 = pick(v12, e, end | 0x10);
            take(e, &mut num, &mut den, s, 1.0);
            k -= 1;
        }
        // comiss + jb: the origin is skipped when it is closer than min_pt_dist (or NaN)
        let ox = a.origin.x - p.center.x;
        let oy = a.origin.y - p.center.y;
        let oz = a.origin.z - p.center.z;
        let d2 = (oy * oy + ox * ox) + oz * oz;
        if lt_or_nan(d2, p.min_pt_dist * p.min_pt_dist) {
            break;
        }
    }

    // ---- phase 2: the ray line against the cylinder side
    let dir = a.dir;
    let tdf = (p.dir.y * dir.y + p.dir.x * dir.x) + p.dir.z * dir.z;
    let td = f64::from(tdf);
    // axial part of the ray direction (double) and radial part (rounded to float)
    let par = [ax * td, td * ay, td * az]; // v383, v373, v401
    let q = [
        f64::from((f64::from(dir.x) - par[0]) as f32), // v397
        f64::from((f64::from(dir.y) - par[1]) as f32), // v391
        f64::from((f64::from(dir.z) - par[2]) as f32), // v389
    ];
    let aq = [
        q[2] * ay - q[1] * az, // v381
        q[0] * az - ax * q[2], // v394
        ax * q[1] - q[0] * ay, // v395
    ];
    // moment of the ray line about the centre (float)
    let oxf = a.origin.x - p.center.x;
    let oyf = a.origin.y - p.center.y;
    let ozf = a.origin.z - p.center.z;
    let mx = f64::from(oyf * dir.z - ozf * dir.y);
    let my = f64::from(ozf * dir.x - oxf * dir.z);
    let mz = f64::from(oxf * dir.y - oyf * dir.x);
    let tm = (my * ay + ax * mx) + mz * az;
    let mpar = [ax * tm, tm * ay, tm * az]; // v382, v377, v400
    let mperp = [mx - mpar[0], my - mpar[1], mz - mpar[2]]; // v398, v417, v408
    let am = [
        mperp[2] * ay - mperp[1] * az, // v416
        mperp[0] * az - ax * mperp[2], // v393
        ax * mperp[1] - mperp[0] * ay, // v399
    ];
    let v134 = dz * ny - dy * nz;
    let v137 = dx * nz - nx * dz;
    let v135 = nx * dy - dx * ny;
    let v136 = ((mperp[1] * ny + nx * mperp[0]) + mperp[2] * nz)
        - ((v137 * q[1] + v134 * q[0]) + v135 * q[2]);
    let v138 = (am[1] * ny + nx * am[0]) + am[2] * nz;
    let v139 = (v137 * par[1] + v134 * par[0]) + v135 * par[2];
    let v511 = ((mpar[1] * ny + nx * mpar[0]) + mpar[2] * nz) - v139;
    let v517 = v138 - ((v137 * aq[1] + v134 * aq[0]) + v135 * aq[2]);
    let v140 = q[1] * nz - q[2] * ny;
    let v141 = nx * q[2] - q[0] * nz;
    let v142 = q[0] * ny - nx * q[1];
    let v143 = aq[1] * nz - aq[2] * ny;
    let v144 = nx * aq[2] - aq[0] * nz;
    let v145 = aq[0] * ny - nx * aq[1];
    let v146 = par[1] * nz - par[2] * ny;
    let v147 = nx * par[2] - par[0] * nz;
    let v148 = par[0] * ny - nx * par[1];
    let v149 = (v517 * v136 - ((v143 * v140 + v144 * v141) + v145 * v142) * r2) * 2.0;
    let v150 = (v511 * v136 - ((v146 * v140 + v147 * v141) + v148 * v142) * r2) * 2.0;
    let v151 = (v140 * v140 + v141 * v141) + v142 * v142;
    let v152 =
        (v136 * v136 + v511 * v511) - (((v146 * v146 + v147 * v147) + v148 * v148) + v151) * r2;
    let v153 = (v511 * v517 - ((v146 * v143 + v147 * v144) + v148 * v145) * r2) * 2.0;
    let v154 =
        (v517 * v517 - v136 * v136) - (((v143 * v143 + v144 * v144) + v145 * v145) - v151) * r2;
    let poly = Poly4::new([
        v152 * v152 - v150 * v150,
        (v153 * v152 + v153 * v152) - (v149 * v150 + v149 * v150),
        ((v154 * v152 + v153 * v153) + v154 * v152) - v149 * v149,
        v154 * v153 + v154 * v153,
        v154 * v154,
    ]);
    let n = roots_01(&poly, &mut roots);
    let mut k = n - 1;
    while k >= 0 {
        let s = roots[k as usize];
        let c = (1.0 - s * s).sqrt();
        let v164 = ((r[0][1] * c + h[0][1]) + x[0][1] * s) - dy;
        let v165 = ((r[0][2] * c + h[0][2]) + x[0][2] * s) - dz;
        let v168 = ((h[0][0] + r[0][0] * c) + x[0][0] * s) - dx;
        let v166 = (par[0] + q[0] * c) + aq[0] * s;
        let v167 = (q[2] * c + par[2]) + aq[2] * s;
        let v169 = (q[1] * c + par[1]) + aq[1] * s;
        let v170 = v169 * nz - v167 * ny;
        let v171 = nx * v167 - nz * v166;
        let v172 = ny * v166 - nx * v169;
        let v173 = (v170 * v170 + v171 * v171) + v172 * v172;
        let v174 = (v171 * v164 + v170 * v168) + v172 * v165;
        let g = v173 * r2;
        let g1 = g * 1.0201;
        let g0 = g * 0.9801;
        let f1 = (((g1 + g0) - (v174 * v174) * 2.0).abs() - (g0 - g1).abs()) as f32;
        let (m165, m164, m168) = (-v165, -v164, -v168);
        let v180 = ((m164 * nz - m165 * ny) * v170 + (nx * m165 - nz * m168) * v171)
            + (ny * m168 - nx * m164) * v172;
        let f2 = ((v173 - v180 * 2.0).abs() - (-v173).abs()) as f32;
        let v183 = ((v173 * v168 + v180 * v166) * nx + (v164 * v173 + v169 * v180) * ny)
            + (v165 * v173 + v167 * v180) * nz;
        let f3 = (v183.abs() - v173 * hh) as f32;
        let f4 = (num - den * s) as f32;
        let e = sgn(f1) & sgn(f2) & sgn(f3) & sgn(f4);
        v12 = pick(v12, e, 0x20);
        take(e, &mut num, &mut den, s, 1.0);
        k -= 1;
    }

    // ---- phase 3: the ray line against the cap rims
    let v417c = mperp[2] * ny - mperp[1] * nz;
    let v398c = mperp[0] * nz - nx * mperp[2];
    let v408c = nx * mperp[1] - mperp[0] * ny;
    let v489 = am[2] * ny - am[1] * nz;
    let v416c = am[0] * nz - nx * am[2];
    let v505 = nx * am[1] - am[0] * ny;
    let v400c = mpar[2] * ny - mpar[1] * nz;
    let v504 = mpar[0] * nz - nx * mpar[2];
    let v497 = nx * mpar[1] - mpar[0] * ny;
    let k1 = (q[1] * ny + nx * q[0]) + q[2] * nz; // v382
    let k2 = (aq[1] * ny + nx * aq[0]) + aq[2] * nz; // v358
    let k3 = (par[1] * ny + nx * par[0]) + par[2] * nz; // v393
    let v490 = (k2 * k1) * r2;
    for j in [-1i32, 1] {
        let v194 = f64::from(j) * hh;
        let v195 = ny * v194 + dy;
        let v196 = nz * v194 + dz;
        let v197 = dx + nx * v194;
        let v198 = v195 * q[2] - v196 * q[1];
        let v199 = q[0] * v196 - q[2] * v197;
        let v200 = q[1] * v197 - q[0] * v195;
        let v201 = v398c - (nz * v198 - nx * v200);
        let v202 = v408c - (nx * v199 - ny * v198);
        let v500 = v417c - (v200 * ny - v199 * nz);
        let v203 = v195 * aq[2] - v196 * aq[1];
        let cp = aq[0] * v196 - aq[2] * v197;
        let cq = aq[1] * v197 - aq[0] * v195;
        let v204 = cq * ny - cp * nz;
        let v207 = v416c - (nz * v203 - nx * cq);
        let v208 = v505 - (nx * cp - ny * v203);
        let g = v489 - v204;
        let v209 = v195 * par[2] - v196 * par[1];
        let v211 = par[0] * v196 - par[2] * v197;
        let cr = par[1] * v197 - par[0] * v195;
        let v212 = v400c - (cr * ny - v211 * nz);
        let v214 = v504 - (nz * v209 - nx * cr);
        let v215 = v497 - (nx * v211 - ny * v209);
        let poly = rim_poly(
            [v212, v214, v215],
            [g, v207, v208],
            [v500, v201, v202],
            [k1, k2, k3],
            v490,
            r2,
        );
        let n = roots_01(&poly, &mut roots);
        let feat = ((j + 1) | 0x80) >> 1;
        let mut k = n - 1;
        while k >= 0 {
            let s = roots[k as usize];
            let c = (1.0 - s * s).sqrt();
            let v231 = (r[0][1] * c + h[0][1]) + x[0][1] * s;
            let v232 = (h[0][0] + r[0][0] * c) + x[0][0] * s;
            let v233 = aq[0] * s + (par[0] + q[0] * c);
            let v234 = (r[0][2] * c + h[0][2]) + x[0][2] * s;
            let v236 = (q[1] * c + par[1]) + aq[1] * s;
            let v237 = (q[2] * c + par[2]) + aq[2] * s;
            let v235 = (nx * (v197 - v232) + (v195 - v231) * ny) + (v196 - v234) * nz;
            let v238 = (nx * v233 + v236 * ny) + v237 * nz;
            let t1 = (v231 - v195) * v238 + v236 * v235;
            let t2 = v238 * (v232 - v197) + v235 * v233;
            let t3 = (v234 - v196) * v238 + v237 * v235;
            let g104 = ((v238 * 1.04) * (v238 * 1.04)) * r2;
            let g096 = ((v238 * 0.96) * (v238 * 0.96)) * r2;
            let f1 = (((g104 + g096) - ((t1 * t1 + t2 * t2) + t3 * t3) * 2.0).abs()
                - (g096 - g104).abs()) as f32;
            let f2 = ((v238 - v235 * 2.0).abs() - (-v238).abs()) as f32;
            let f3 = (num - den * s) as f32;
            let e = sgn(f1) & sgn(f2) & sgn(f3);
            v12 = pick(v12, e, feat);
            take(e, &mut num, &mut den, s, 1.0);
            k -= 1;
        }
    }

    if v12 < 0 {
        return 0;
    }
    let cx = f64::from(p.center.x);
    let cy = f64::from(p.center.y);
    let cz = f64::from(p.center.z);
    match v12 & 0xF0 {
        0 => {
            // cap vertex
            let idx = (v12 & 1) as usize;
            let side = v12 & 2;
            let t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
            out.t = t;
            let c = (1.0 - t * t).sqrt();
            out.cos_t = c;
            let (hv, rv, xv) = (h[idx], r[idx], x[idx]);
            out.pt = Vec3::new(
                (cx + ((rv[0] * c + hv[0]) + xv[0] * t)) as f32,
                (cy + ((rv[1] * c + hv[1]) + xv[1] * t)) as f32,
                (cz + ((rv[2] * c + hv[2]) + xv[2] * t)) as f32,
            );
            let sg = f64::from(1 - side);
            out.n = Vec3::new((nx * sg) as f32, (ny * sg) as f32, (nz * sg) as f32);
            out.i_feature = [idx as i32 | 0x80, (side >> 1) + 0x41];
        }
        0x10 => {
            // side vertex
            let idx = (v12 & 1) as usize;
            let t = num;
            out.t = t;
            let c = (1.0 - t * t).sqrt();
            out.cos_t = c;
            let (hv, rv, xv) = (h[idx], r[idx], x[idx]);
            out.pt = Vec3::new(
                (cx + ((rv[0] * c + hv[0]) + t * xv[0])) as f32,
                (cy + ((rv[1] * c + hv[1]) + t * xv[1])) as f32,
                (cz + ((rv[2] * c + hv[2]) + t * xv[2])) as f32,
            );
            let mut nn = Vec3::new(
                b.center.x - out.pt.x,
                b.center.y - out.pt.y,
                b.center.z - out.pt.z,
            );
            let d = (nn.y * b.axis.y + nn.x * b.axis.x) + nn.z * b.axis.z;
            nn.x -= b.axis.x * d;
            nn.y -= b.axis.y * d;
            nn.z -= b.axis.z * d;
            normalize_in_place(&mut nn);
            out.n = nn;
            out.i_feature = [idx as i32 | 0x80, 0x40];
        }
        0x20 => {
            // the ray line touches the cylinder side
            let t = num;
            out.t = t;
            let c = (1.0 - t * t).sqrt();
            out.cos_t = c;
            let v359 = cx + ((h[0][0] + r[0][0] * c) + x[0][0] * t);
            let v512 = cy + ((r[0][1] * c + h[0][1]) + x[0][1] * t);
            let v516 = cz + ((r[0][2] * c + h[0][2]) + x[0][2] * t);
            let v305 = (q[1] * c + par[1]) + aq[1] * t;
            let v306 = (q[2] * c + par[2]) + aq[2] * t;
            let v307 = (par[0] + q[0] * c) + aq[0] * t;
            let v308 = v305 * nz - v306 * ny;
            let v309 = nx * v306 - nz * v307;
            let v310 = ny * v307 - nx * v305;
            let ddx = (f64::from(b.center.x) - v359) as f32;
            let ddy = (f64::from(b.center.y) - v512) as f32;
            let ddz = (f64::from(b.center.z) - v516) as f32;
            let (nxf, nyf, nzf) = (b.axis.x, b.axis.y, b.axis.z);
            let v311 = nyf * ddx - ddy * nxf;
            let bz = ddz * nxf - nzf * ddx;
            let az_ = nzf * ddy - nyf * ddz;
            let sumf = (az_ * (v308 as f32) + (v309 as f32) * bz) + (v310 as f32) * v311;
            let v314 = (v308 * v308 + v309 * v309) + v310 * v310;
            let v315 = if ucomisd_eq(v314, 0.0) {
                0.0
            } else {
                f64::from(sumf) / v314
            };
            out.pt = Vec3::new(
                (v315 * v307 + v359) as f32,
                (v305 * v315 + v512) as f32,
                (v306 * v315 + v516) as f32,
            );
            let l2 = (v309 * v309 + v308 * v308) + v310 * v310;
            out.n = normalized_d(v308, v309, v310, l2);
            flip_towards(out, b);
            out.i_feature = [0x20, 0x40];
        }
        0x40 => {
            // the ray line touches a cap rim
            let t = num;
            out.t = t;
            let c = (1.0 - t * t).sqrt();
            out.cos_t = c;
            let p0z = (r[0][2] * c + h[0][2]) + x[0][2] * t;
            let p0y = (r[0][1] * c + h[0][1]) + x[0][1] * t;
            let p0x = (h[0][0] + r[0][0] * c) + x[0][0] * t;
            let v257 = (q[2] * c + par[2]) + aq[2] * t;
            let v258 = (par[0] + q[0] * c) + aq[0] * t;
            let v259 = (q[1] * c + par[1]) + aq[1] * t;
            let sj = 2 * (v12 & 1) - 1;
            let v260 = f64::from(sj) * hh;
            let v261 = ny * v260 + dy;
            let v262 = dx + nx * v260;
            let v263 = nz * v260 + dz;
            let v264 = (v259 * ny + nx * v258) + v257 * nz;
            let v265 = if ucomisd_eq(v264, 0.0) {
                0.0
            } else {
                (((v261 - p0y) * ny + nx * (v262 - p0x)) + (v263 - p0z) * nz) / v264
            };
            let ptx = (v265 * v258 + p0x) as f32;
            let pty = (v265 * v259 + p0y) as f32;
            let ptz = (v265 * v257 + p0z) as f32;
            let w = [
                f64::from((f64::from(ptx) - v262) as f32),
                f64::from((f64::from(pty) - v261) as f32),
                f64::from((f64::from(ptz) - v263) as f32),
            ];
            out.pt = Vec3::new(p.center.x + ptx, p.center.y + pty, p.center.z + ptz);
            let v276 = w[2] * ny - w[1] * nz;
            let v277 = nz * w[0] - nx * w[2];
            let v278 = nx * w[1] - ny * w[0];
            let v279 = v277 * v257 - v278 * v259;
            let v280 = v278 * v258 - v257 * v276;
            let v281 = v259 * v276 - v277 * v258;
            let l2 = (v279 * v279 + v280 * v280) + v281 * v281;
            out.n = normalized_d(v279, v280, v281, l2);
            flip_towards(out, b);
            out.i_feature = [0x20, ((sj + 1) | 0x40) >> 1];
        }
        // not reachable: v12 is one of the codes above
        _ => {}
    }
    1
}

/// The quartic of a ray end against the cylinder side (phase 1): with `w = (h − d) × n`,
/// `u = r × n`, `v = (a × r) × n`, `A = u·w + (u·v)·s` and
/// `B = (w·w + u·u − r²) + 2(v·w)·s + (v·v − u·u)·s²`, the polynomial is
/// `B² − 4·(1 − s²)·A²`, evaluated with the inlined `polynomial_tpl` operations (products
/// accumulate on `0.0`, `(1 − s²)` is `[1.0, −0.0, −1.0]`, scalar factors `1.0` are exact and
/// omitted).
fn side_vertex_poly(w: [f64; 3], u: [f64; 3], v: [f64; 3], r2: f64) -> Poly4 {
    let uw = (u[1] * w[1] + u[0] * w[0]) + u[2] * w[2];
    let uv = (v[1] * u[1] + v[0] * u[0]) + v[2] * u[2];
    // A = [a0, a1]
    let a0 = 0.0 + uw;
    let a1 = uv;
    // A²
    let q0 = 0.0 + a0 * a0;
    let q1 = (0.0 + a1 * a0) + a1 * a0;
    let q2 = 0.0 + a1 * a1;
    // (1 − s²) = −([0, 0, 1] − 1)
    let p0 = -(0.0 - 1.0f64);
    let p1 = -0.0f64;
    let p2 = -1.0f64;
    let m0 = (0.0 + q0 * p0) * 4.0;
    let m1 = ((0.0 + q1 * p0) + q0 * p1) * 4.0;
    let m2 = (((0.0 + q2 * p0) + q1 * p1) + q0 * p2) * 4.0;
    let m3 = ((0.0 + q2 * p1) + q1 * p2) * 4.0;
    let m4 = (0.0 + q2 * p2) * 4.0;
    // B
    let ww = (w[1] * w[1] + w[0] * w[0]) + w[2] * w[2];
    let vv = (v[1] * v[1] + v[0] * v[0]) + v[2] * v[2];
    let uu = (u[1] * u[1] + u[0] * u[0]) + u[2] * u[2];
    let vw = (v[1] * w[1] + v[0] * w[0]) + v[2] * w[2];
    let b2 = vv - uu;
    let b1 = vw * 2.0 + 0.0;
    let b0 = (((0.0 + 0.0) + ww) + uu) - r2;
    // B²
    let s0 = 0.0 + b0 * b0;
    let s1 = (0.0 + b1 * b0) + b1 * b0;
    let s2 = ((0.0 + b2 * b0) + b1 * b1) + b2 * b0;
    let s3 = (0.0 + b1 * b2) + b1 * b2;
    let s4 = 0.0 + b2 * b2;
    Poly4::new([s0 - m0, s1 - m1, s2 - m2, s3 - m3, s4 - m4])
}

/// The quartic of the ray line against a cap rim (phase 3): `B² − A²` with
/// `A = A0 + A1·s`, `B = B0 + B1·s + B2·s²` built from the cross products of the loop body
/// (`sq` = (v212, v214, v215), `gv` = (g, v207, v208), `pv` = (v500, v201, v202),
/// `k` = (K1, K2, K3)), with the same `polynomial_tpl` accumulators.
fn rim_poly(sq: [f64; 3], gv: [f64; 3], pv: [f64; 3], k: [f64; 3], v490: f64, r2: f64) -> Poly4 {
    let [v212, v214, v215] = sq;
    let [g, v207, v208] = gv;
    let [v500, v201, v202] = pv;
    let [k1, k2, k3] = k;
    let a1 = (((g * v500 + v207 * v201) + v208 * v202) - v490) * 2.0;
    let a0 = 0.0 + (((v212 * v500 + v214 * v201) + v215 * v202) - (k3 * k1) * r2) * 2.0;
    let q0 = 0.0 + a0 * a0;
    let q1 = (0.0 + a1 * a0) + a1 * a0;
    let q2 = 0.0 + a1 * a1;
    let ssq = (v212 * v212 + v214 * v214) + v215 * v215;
    let b1 = (((v212 * g + v214 * v207) + v215 * v208) - (k3 * k2) * r2) * 2.0 + 0.0;
    let v218 = (v500 * v500 + v201 * v201) + v202 * v202;
    let b2 = (((g * g + v207 * v207) + v208 * v208) - v218) - (k2 * k2 - k1 * k1) * r2;
    let b0 = (((0.0 + 0.0) + v218) + ssq) - (k3 * k3 + k1 * k1) * r2;
    let s0 = 0.0 + b0 * b0;
    let s1 = (0.0 + b1 * b0) + b1 * b0;
    let s2 = ((0.0 + b2 * b0) + b1 * b1) + b2 * b0;
    let s3 = (0.0 + b1 * b2) + b1 * b2;
    let s4 = 0.0 + b2 * b2;
    Poly4::new([s0 - q0, s1 - q1, s2 - q2, s3, s4])
}

/// Inline double normalisation of the 0x20 / 0x40 outputs: `|v|² ≤ (double)1e-20f` (or NaN,
/// `comisd` + `jbe`) → (0,0,1), else `v·(1/√|v|²)`; rounded to float.
fn normalized_d(x: f64, y: f64, z: f64, l2: f64) -> Vec3 {
    if le_or_nan_d(l2, f64::from(1e-20f32)) {
        Vec3::new(0.0, 0.0, 1.0)
    } else {
        let inv = 1.0 / l2.sqrt();
        Vec3::new((x * inv) as f32, (y * inv) as f32, (z * inv) as f32)
    }
}

/// `n *= (float)(((bits(d) >> 30) & ~1) + 1)` with `d = (cylc − pt)·n` (float): flip the normal
/// to point towards the cylinder centre (−0 counts as negative).
fn flip_towards(out: &mut UnprojResult, b: &CylinderPrim) {
    let n = out.n;
    let d = ((b.center.y - out.pt.y) * n.y + (b.center.x - out.pt.x) * n.x)
        + (b.center.z - out.pt.z) * n.z;
    let sgn = ((((d.to_bits() as i32) >> 30) & !1) + 1) as f32;
    out.n = Vec3::new(sgn * n.x, sgn * n.y, sgn * n.z);
}

fn cell_ray_cyl(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_ray_cyl_rot(p, as_ray(a), fa, as_cyl(b), fb, out)
}

/// 0x351A4460: cyl–ray rotational, the 447-byte swap wrapper around [`unproject_ray_cyl_rot`].
fn cell_cyl_ray(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    let (cyl, ray) = (as_cyl(a), as_ray(b));
    swap_rot(p, out, |p, out| {
        unproject_ray_cyl_rot(p, ray, fb, cyl, fa, out)
    })
}

/// Fill the cells this group owns: `[1][ray][cyl]`, `[1][cyl][ray]`.
pub fn fill(t: &mut UnprojTables) {
    t.set(
        MODE_ROTATIONAL,
        GeomType::Ray,
        GeomType::Cylinder,
        cell_ray_cyl,
    );
    t.set(
        MODE_ROTATIONAL,
        GeomType::Cylinder,
        GeomType::Ray,
        cell_cyl_ray,
    );
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A radial ray (origin at radius 2 from the z axis) swings into a cylinder standing at
    /// (0, 2, 0) with radius 0.5: the origin reaches the side when `8 − 8·sin φ = 0.25`.
    #[test]
    fn origin_swings_into_side() {
        let mut p = UnprojParams {
            dir: Vec3::Z,
            tmax: 1.0,
            min_pt_dist: 0.01,
            ..UnprojParams::default()
        };
        let ray = RayPrim {
            origin: Vec3::new(2.0, 0.0, 0.0),
            dir: Vec3::new(1.0, 0.0, 0.0),
        };
        let cyl = CylinderPrim {
            center: Vec3::new(0.0, 2.0, 0.0),
            axis: Vec3::Z,
            r: 0.5,
            hh: 1.0,
        };
        let mut out = UnprojResult::default();
        assert_eq!(
            unproject_ray_cyl_rot(&mut p, &ray, -1, &cyl, -1, &mut out),
            1
        );
        assert!((out.t - 0.96875).abs() < 1e-9, "{out:?}");
        assert!((out.cos_t - (1.0 - 0.96875f64 * 0.96875).sqrt()).abs() < 1e-9);
        assert_eq!(out.i_feature, [0x80, 0x40]);
        assert!((out.pt.y - 1.9375).abs() < 1e-5);
        assert!((out.n.x + 0.992_156_7).abs() < 1e-4 && (out.n.y - 0.125).abs() < 1e-4);
        assert_eq!(p.dir, Vec3::Z);
    }
}
