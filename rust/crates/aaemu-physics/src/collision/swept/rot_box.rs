//! Rotational ray–box unprojection (mode 1) and its swap wrapper.
//!
//! * **ray–box** sub_3519EF90 (x64, 6823 B; x86 0x351AAF30), cell `[1][ray][box]`
//!   ([`unproject_ray_box_rot`]).
//! * **box–ray** 0x351A0A40 (x64, 447 B; x86 0x351ACE40), cell `[1][box][ray]`: the rotational
//!   swap wrapper, identical to [`swap_rot`] (negate the axis, call ray–box with the operands
//!   exchanged, rotate `pt` with sub_3519A670 and `n` with sub_35020EA0 by (cos, −sin), negate
//!   `n`, swap the feature ids, restore the axis).
//!
//! Inputs: `params.dir` = unit rotation axis `a`, `params.center` = rotation centre,
//! `params.min_pt_dist`. `tmax`, `tmin`, `vel`, `max_cos` and the box's `oriented` flag are **not
//! read** (the basis is always applied); the angle is bounded to (0, π/2) only by the root tests.
//! Neither the params nor the contact area (7th argument) are touched.
//!
//! Algorithm (everything after the float frame change is double precision): the ray origin `O`,
//! direction `D`, the rotation centre `C` and the axis `A` are transformed into the box frame
//! (`basis · (x − box.center)`, float, then widened). The best candidate is kept as a fraction
//! `num/den` = sin φ (start 0/1) and a feature code (start −1), updated branch-free: a candidate
//! is accepted when three float-rounded tests are all negative (sign bits ANDed), the last one
//! being `num·L − den·root < 0` (smaller sine than the best so far).
//!
//! 1. **Ray end points vs face planes.** Each end point `k` (end `O + D` first, then the origin;
//!    the origin is skipped when `|O − C|² < minPtDist²` in world space, float) is split relative
//!    to `C` into the axial part `hA = ((p·A)·A)`, the radial part `r` and `A × r`; it moves along
//!    `C + hA + r·cos φ + (A×r)·sin φ`. For each face `i` (axis `j = i/2`, side
//!    `σ = 2(i&1) − 1`): `e + r_j cos φ + (A×r)_j sin φ = 0` with `e = hA_j − σ·size_j + C_j` is
//!    solved for `L·sin φ` (`L = (A×r)_j² + r_j²`), both roots, accepting `0 < sin φ < 1` and
//!    `cos φ > 0` (sign tests of float-rounded `|2x − L| − L` and `(x·(A×r)_j + L·e)·r_j`) whose
//!    rotated point lies inside the two other slabs (`|P_m| − L·size_m < 0`). Feature `2i | k`.
//! 2. **Box edges vs the ray segment.** For each of the 12 edges (`e`: axis `ax = e/4`, side bits
//!    `e&1` → `inc(ax)`, `e&2` → `dec(ax)`), the rotating ray line and the edge line are coplanar
//!    with the centre when `v118 + v117·cos φ + v119·sin φ = 0` (built from the moment
//!    `m = (O−C) × D` and the normal `E = W × e_ax` of the plane through the centre and the edge,
//!    `W` = edge midpoint − C, both split into axial/radial parts); same root handling. A root
//!    is accepted when the intersection lies strictly inside the rotated segment
//!    (`|G·G − 2·G·H| − |−G·G| < 0`) and within the edge's half length
//!    (`|(R × Q)·G| − |G·G·L|·size_ax < 0`). Feature `e | 0x80`.
//!
//! Result (only on success; on failure nothing is written and 0 is returned):
//! `t = num/den` (0 when `den` compares equal to 0, NaN included), `cos_t = √(1 − t²)`.
//! * end point `k` against face `i`: `pt` = the rotated end point, `n` = the face normal
//!   `−σ·e_j` (float, rotated to world), features `(0x80 | k, 0x40 | i)`.
//! * edge `e`: `pt` = the point of the rotated ray line closest to the edge line, `n = ±(Q × e_ax)`
//!   (not normalised; `Q` = rotated ray vector) oriented so that `n·pt_local < 0` (the sign bit of
//!   the float-rounded dot selects +), features `(0xA0, 0x20 | e)`.
//!
//! The 0/1 blends (`(1−acc)·best + acc·cand` in double) are mirrored literally (they turn
//! `−0`/`inf` operands into the same values as the binary).

use super::common::{
    as_box, as_ray, lt_or_nan, lt_or_nan_d, swap_rot, ucomisd_eq, DEC_MOD3, INC_MOD3,
};
use super::{UnprojTables, MODE_ROTATIONAL};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{BoxPrim, Primitive, RayPrim};
use crate::ids::GeomType;
use crate::math::Vec3;

type V3 = [f64; 3];

/// Sign bit of a float (0 or 1).
#[inline]
fn sbit(f: f32) -> u32 {
    f.to_bits() >> 31
}

/// Axial part `(p·A)·A`, radial part `r = p − (p·A)·A` and `A × r` of a point relative to the
/// rotation centre (box frame, double).
fn split(p: V3, a: V3) -> (V3, V3, V3) {
    let h = (p[0] * a[0] + p[1] * a[1]) + p[2] * a[2];
    let ha = [h * a[0], h * a[1], h * a[2]];
    let r = [p[0] - ha[0], p[1] - ha[1], p[2] - ha[2]];
    let ar = [
        r[2] * a[1] - r[1] * a[2],
        r[0] * a[2] - a[0] * r[2],
        a[0] * r[1] - r[0] * a[1],
    ];
    (ha, r, ar)
}

/// The branch-free best-candidate update shared by both phases.
#[inline]
fn blend(acc: u32, feat: i32, best: &mut i32, num: &mut f64, den: &mut f64, x: f64, l: f64) {
    let a = acc as i32;
    *best = (feat & a.wrapping_neg()) | ((a - 1) & *best);
    let fa = f64::from(acc);
    let fna = f64::from(acc ^ 1);
    *num = fna * *num + fa * x;
    *den = fna * *den + fa * l;
}

/// sub_3519EF90: rotate the ray about (`params.center`, `params.dir`) by the smallest angle that
/// takes it out of the box (see the module docs). Returns 1 with `t = sin φ`, `cos_t = cos φ`,
/// `pt`, `n` and the features, or 0 without writing anything.
#[allow(clippy::too_many_lines, clippy::similar_names)]
pub fn unproject_ray_box_rot(
    p: &UnprojParams,
    ray: &RayPrim,
    _fa: i32,
    bx: &BoxPrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let m = &bx.basis.m;
    let (b0, b1, b2) = (m[0][0], m[0][1], m[0][2]);
    let (b3, b4, b5) = (m[1][0], m[1][1], m[1][2]);
    let (b6, b7, b8) = (m[2][0], m[2][1], m[2][2]);
    let bc = bx.center;
    // float frame changes, widened to double
    let to_local = |v: Vec3| -> V3 {
        [
            f64::from((v.x * b0 + v.y * b1) + v.z * b2),
            f64::from((v.x * b3 + v.y * b4) + v.z * b5),
            f64::from((v.x * b6 + v.y * b7) + v.z * b8),
        ]
    };
    let ol = to_local(Vec3::new(
        ray.origin.x - bc.x,
        ray.origin.y - bc.y,
        ray.origin.z - bc.z,
    ));
    let d = to_local(ray.dir);
    let c = to_local(Vec3::new(
        p.center.x - bc.x,
        p.center.y - bc.y,
        p.center.z - bc.z,
    ));
    let a = to_local(p.dir);
    let size = [
        f64::from(bx.size.x),
        f64::from(bx.size.y),
        f64::from(bx.size.z),
    ];

    // end points relative to the centre: [0] origin, [1] origin + dir
    let o = [ol[0] - c[0], ol[1] - c[1], ol[2] - c[2]];
    let o1 = [o[0] + d[0], o[1] + d[1], o[2] + d[2]];
    let (ha0, r0, ar0) = split(o, a);
    let (ha1, r1, ar1) = split(o1, a);
    let ends = [(ha0, r0, ar0), (ha1, r1, ar1)];

    let dist2 = {
        let ex = ray.origin.x - p.center.x;
        let ey = ray.origin.y - p.center.y;
        let ez = ray.origin.z - p.center.z;
        (ey * ey + ex * ex) + ez * ez
    };
    let mpd2 = p.min_pt_dist * p.min_pt_dist;

    let mut best: i32 = -1;
    let mut num = 0.0f64;
    let mut den = 1.0f64;

    // ---- phase 1: ray end points (end first) against the six face planes
    for k in [1usize, 0] {
        let (ha, r, ar) = ends[k];
        for i in 0..6usize {
            let j = i >> 1;
            let rj = r[j];
            let arj = ar[j];
            let l = arj * arj + rj * rj;
            let sg = f64::from(2 * (i as i32 & 1) - 1);
            let e = (ha[j] - sg * size[j]) + c[j];
            let er = e * arj;
            let disc = er * er - (e * e - rj * rj) * l;
            // comisd + jb: negative or unordered skips
            if lt_or_nan_d(disc, 0.0) {
                continue;
            }
            let i1 = INC_MOD3[j] as usize;
            let i2 = DEC_MOD3[j] as usize;
            let sq = disc.sqrt();
            let le = l * e;
            let mut x = -er - sq;
            let step = sq * 2.0;
            for _ in 0..2 {
                let ta = ((x * 2.0 - l).abs() - l) as f32;
                let tb = ((x * arj + le) * rj) as f32;
                if (ta.to_bits() & tb.to_bits()) >> 31 != 0 {
                    let cs = (l * l - x * x).sqrt();
                    let pv = [
                        (r[0] * cs + ar[0] * x) + (c[0] + ha[0]) * l,
                        (r[1] * cs + ar[1] * x) + (c[1] + ha[1]) * l,
                        (r[2] * cs + ar[2] * x) + (c[2] + ha[2]) * l,
                    ];
                    let e1 = (pv[i1].abs() - l * size[i1]) as f32;
                    let e2 = (pv[i2].abs() - l * size[i2]) as f32;
                    let e3 = (num * l - den * x) as f32;
                    let acc = sbit(e3) & sbit(e2) & sbit(e1);
                    blend(
                        acc,
                        (2 * i as i32) | k as i32,
                        &mut best,
                        &mut num,
                        &mut den,
                        x,
                        l,
                    );
                }
                x += step;
            }
        }
        // comiss + jb: the origin is skipped when it is (within minPtDist of) the centre
        if lt_or_nan(dist2, mpd2) {
            break;
        }
    }

    // ---- phase 2: the twelve box edges against the ray segment
    let mo = [
        o[1] * d[2] - o[2] * d[1],
        d[0] * o[2] - o[0] * d[2],
        o[0] * d[1] - d[0] * o[1],
    ];
    let hm = (mo[1] * a[1] + mo[0] * a[0]) + mo[2] * a[2];
    let hma = [hm * a[0], hm * a[1], hm * a[2]];
    let mp = [mo[0] - hma[0], mo[1] - hma[1], mo[2] - hma[2]];
    let amp = [
        mp[2] * a[1] - mp[1] * a[2],
        mp[0] * a[2] - a[0] * mp[2],
        a[0] * mp[1] - mp[0] * a[1],
    ];
    for e in 0..12usize {
        let ax = e >> 2;
        let i1 = INC_MOD3[ax] as usize;
        let i2 = DEC_MOD3[ax] as usize;
        let mut v = [0.0f64; 3];
        v[i1] = f64::from(2 * (e as i32 & 1) - 1) * size[i1];
        v[i2] = f64::from((e as i32 & 2) - 1) * size[i2];
        let w = [v[0] - c[0], v[1] - c[1], v[2] - c[2]];
        let mut ev = [0.0f64; 3];
        ev[i1] = w[i2];
        ev[i2] = -w[i1];
        let he = (ev[1] * a[1] + ev[0] * a[0]) + ev[2] * a[2];
        let hea = [he * a[0], he * a[1], he * a[2]];
        let ep = [ev[0] - hea[0], ev[1] - hea[1], ev[2] - hea[2]];
        let aep = [
            ep[2] * a[1] - ep[1] * a[2],
            ep[0] * a[2] - a[0] * ep[2],
            a[0] * ep[1] - ep[0] * a[1],
        ];
        let qs = (aep[1] * d[1] + aep[0] * d[0]) + aep[2] * d[2];
        let qc = (ep[1] * d[1] + ep[0] * d[0]) + ep[2] * d[2];
        let qh = (hea[1] * d[1] + hea[0] * d[0]) + hea[2] * d[2];
        let kc = qc + mp[ax];
        let kh = qh + hma[ax];
        let ks = amp[ax] - qs;
        let l = ks * ks + kc * kc;
        let hs = kh * ks;
        let disc = hs * hs - (kh * kh - kc * kc) * l;
        if lt_or_nan_d(disc, 0.0) {
            continue;
        }
        let sq = disc.sqrt();
        let mut x = -hs - sq;
        let lh = l * kh;
        let step = sq * 2.0;
        for _ in 0..2 {
            let ta = ((x * 2.0 - l).abs() - l) as f32;
            let tb = ((x * ks + lh) * kc) as f32;
            if (ta.to_bits() & tb.to_bits()) >> 31 != 0 {
                let cs = (l * l - x * x).sqrt();
                // rotated end points, scaled by l, relative to the centre
                let p0 = [
                    ha0[0] * l + (r0[0] * cs + ar0[0] * x),
                    (r0[1] * cs + ar0[1] * x) + ha0[1] * l,
                    (r0[2] * cs + ar0[2] * x) + ha0[2] * l,
                ];
                let p1 = [
                    (r1[0] * cs + ar1[0] * x) + ha1[0] * l,
                    (r1[1] * cs + ar1[1] * x) + ha1[1] * l,
                    (r1[2] * cs + ar1[2] * x) + ha1[2] * l,
                ];
                let q = [p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]];
                let rr = [w[0] * l - p0[0], w[1] * l - p0[1], w[2] * l - p0[2]];
                let mut g = [0.0f64; 3];
                g[i1] = q[i2];
                g[i2] = -q[i1];
                let mut h = [0.0f64; 3];
                h[i1] = rr[i2];
                h[i2] = -rr[i1];
                let gg = (g[1] * g[1] + g[0] * g[0]) + g[2] * g[2];
                let gh = ((h[1] * g[1] + h[0] * g[0]) + h[2] * g[2]) * 2.0;
                let rq = [
                    rr[1] * q[2] - rr[2] * q[1],
                    rr[2] * q[0] - q[2] * rr[0],
                    q[1] * rr[0] - rr[1] * q[0],
                ];
                let trip = (rq[1] * g[1] + rq[0] * g[0]) + rq[2] * g[2];
                let e1 = ((gg - gh).abs() - (-gg).abs()) as f32;
                let e2 = (trip.abs() - (gg * l).abs() * size[ax]) as f32;
                let e3 = (num * l - den * x) as f32;
                let acc = sbit(e3) & sbit(e2) & sbit(e1);
                blend(acc, e as i32 | 0x80, &mut best, &mut num, &mut den, x, l);
            }
            x += step;
        }
    }

    if best < 0 {
        return 0;
    }
    let s = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
    let cs = (1.0 - s * s).sqrt();
    out.t = s;
    out.cos_t = cs;
    let (fb0, fb1, fb2) = (f64::from(b0), f64::from(b1), f64::from(b2));
    let (fb3, fb4, fb5) = (f64::from(b3), f64::from(b4), f64::from(b5));
    let (fb6, fb7, fb8) = (f64::from(b6), f64::from(b7), f64::from(b8));
    if best & 0x80 == 0 {
        // end point k against face i
        let k = (best & 1) as usize;
        let i = best >> 1;
        let j = (i >> 1) as usize;
        let (ha, r, ar) = ends[k];
        let px = c[0] + ((r[0] * cs + ha[0]) + ar[0] * s);
        let py = ((r[1] * cs + ha[1]) + ar[1] * s) + c[1];
        let pz = ((r[2] * cs + ha[2]) + ar[2] * s) + c[2];
        out.pt = Vec3::new(
            (f64::from(bc.x) + ((fb0 * px + fb3 * py) + fb6 * pz)) as f32,
            (f64::from(bc.y) + ((fb1 * px + fb4 * py) + fb7 * pz)) as f32,
            (f64::from(bc.z) + ((fb5 * py + fb2 * px) + fb8 * pz)) as f32,
        );
        let mut nl = [0.0f32; 3];
        nl[j] = (1 - 2 * (i & 1)) as f32;
        let (nx, ny, nz) = (nl[0], nl[1], nl[2]);
        out.n = Vec3::new(
            (nx * b0 + ny * b3) + nz * b6,
            (ny * b4 + nx * b1) + nz * b7,
            (nx * b2 + ny * b5) + nz * b8,
        );
        out.i_feature = [k as i32 | 0x80, i | 0x40];
    } else {
        // box edge e against the ray segment
        let e = best & 0x7F;
        let ax = (e >> 2) as usize;
        let i1 = INC_MOD3[ax] as usize;
        let i2 = DEC_MOD3[ax] as usize;
        let p0 = [
            c[0] + (ha0[0] + (r0[0] * cs + ar0[0] * s)),
            ((r0[1] * cs + ar0[1] * s) + ha0[1]) + c[1],
            ((r0[2] * cs + ar0[2] * s) + ha0[2]) + c[2],
        ];
        let p1 = [
            c[0] + (ha1[0] + (r1[0] * cs + ar1[0] * s)),
            ((r1[1] * cs + ar1[1] * s) + ha1[1]) + c[1],
            ((r1[2] * cs + ar1[2] * s) + ha1[2]) + c[2],
        ];
        let q = [p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]];
        let mut tv = [0.0f64; 3];
        tv[i1] = f64::from(2 * (e & 1) - 1) * size[i1];
        tv[i2] = f64::from((e & 2) - 1) * size[i2];
        let rr = [tv[0] - p0[0], tv[1] - p0[1], tv[2] - p0[2]];
        let mut u = [0.0f64; 3];
        u[i1] = q[i2];
        u[i2] = -q[i1];
        let mut v2 = [0.0f64; 3];
        v2[i1] = rr[i2];
        v2[i2] = -rr[i1];
        let dot = (v2[0] * u[0] + v2[1] * u[1]) + v2[2] * u[2];
        let uu = (u[0] * u[0] + u[1] * u[1]) + u[2] * u[2];
        let lam = if ucomisd_eq(uu, 0.0) { 0.0 } else { dot / uu };
        let x = [lam * q[0] + p0[0], q[1] * lam + p0[1], q[2] * lam + p0[2]];
        let sd = ((x[1] * u[1] + x[0] * u[0]) + x[2] * u[2]) as f32;
        // -1 - ((bits >> 30) & ~1): +1 when the sign bit is set, else -1
        let sg = f64::from(-1 - (((sd.to_bits() as i32) >> 30) & !1));
        let nu = [sg * u[0], u[1] * sg, u[2] * sg];
        out.n = Vec3::new(
            ((fb3 * nu[1] + fb0 * nu[0]) + fb6 * nu[2]) as f32,
            ((fb4 * nu[1] + fb1 * nu[0]) + fb7 * nu[2]) as f32,
            ((fb5 * nu[1] + fb2 * nu[0]) + fb8 * nu[2]) as f32,
        );
        out.pt = Vec3::new(
            (f64::from(bc.x) + ((fb3 * x[1] + fb0 * x[0]) + fb6 * x[2])) as f32,
            (f64::from(bc.y) + ((fb4 * x[1] + fb1 * x[0]) + fb7 * x[2])) as f32,
            (f64::from(bc.z) + ((fb5 * x[1] + fb2 * x[0]) + fb8 * x[2])) as f32,
        );
        out.i_feature = [0xA0, e | 0x20];
    }
    1
}

/// 0x351A0A40: box–ray rotational unprojection, the 447-byte swap wrapper around
/// [`unproject_ray_box_rot`] (its disassembly matches [`swap_rot`] exactly).
pub fn unproject_box_ray_rot(
    p: &mut UnprojParams,
    bx: &BoxPrim,
    fa: i32,
    ray: &RayPrim,
    fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    swap_rot(p, out, |p, out| {
        unproject_ray_box_rot(p, ray, fb, bx, fa, out)
    })
}

fn cell_ray_box_rot(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_ray_box_rot(p, as_ray(a), fa, as_box(b), fb, out)
}

fn cell_box_ray_rot(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_box_ray_rot(p, as_box(a), fa, as_ray(b), fb, out)
}

/// Fill the cells this group owns: `[1][ray][box]` and `[1][box][ray]`.
pub fn fill(t: &mut UnprojTables) {
    t.set(
        MODE_ROTATIONAL,
        GeomType::Ray,
        GeomType::Box,
        cell_ray_box_rot,
    );
    t.set(
        MODE_ROTATIONAL,
        GeomType::Box,
        GeomType::Ray,
        cell_box_ray_rot,
    );
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::math::Mat33;

    /// Unit box at the origin, rotation about +z through (-0.5, -0.2, 0) (= the ray origin, which
    /// is therefore skipped). The end point (0.9, 0, 0) is at (1.4, 0.2) from the centre and
    /// reaches the face y = 1 when `0.2·cos φ + 1.4·sin φ = 1.2` (x ≈ 0.33, inside): end point 1,
    /// face 3 (+y).
    #[test]
    fn end_point_hits_face() {
        let bx = BoxPrim {
            basis: Mat33::IDENTITY,
            oriented: 0,
            center: Vec3::ZERO,
            size: Vec3::new(1.0, 1.0, 1.0),
        };
        let ray = RayPrim {
            origin: Vec3::new(-0.5, -0.2, 0.0),
            dir: Vec3::new(1.4, 0.2, 0.0),
        };
        let p = UnprojParams {
            mode: 1,
            dir: Vec3::Z,
            center: Vec3::new(-0.5, -0.2, 0.0),
            min_pt_dist: 0.01,
            tmax: 1.0,
            ..UnprojParams::default()
        };
        let mut out = UnprojResult::default();
        assert_eq!(unproject_ray_box_rot(&p, &ray, -1, &bx, -1, &mut out), 1);
        assert!(
            (0.2 * out.cos_t + 1.4 * out.t - 1.2).abs() < 1e-6,
            "{}",
            out.t
        );
        assert_eq!(out.i_feature, [0x81, 0x43]);
        assert_eq!(out.n, Vec3::new(0.0, -1.0, 0.0));
        assert!((out.pt.y - 1.0).abs() < 1e-6);
    }
}
