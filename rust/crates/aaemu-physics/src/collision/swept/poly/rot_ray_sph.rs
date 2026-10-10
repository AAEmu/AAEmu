//! Rotational ray–sphere unprojection: x64 0x351A7CC0 (x86 0x351BD280), cell `[1][ray][sph]`,
//! and its swap wrapper 0x351A9170, cell `[1][sph][ray]`.
//!
//! The ray (segment `origin .. origin + dir`) rotates about the axis `params.dir` through
//! `params.center`; for a segment that currently intersects the sphere the result is the
//! largest rotation `t = sin φ ∈ (0, 1)` at which it leaves the sphere (the candidate exit
//! angles of the two end points and of the side are compared; a pair that does not intersect
//! now yields no candidate and returns 0). Everything is in double relative to the centre
//! (`cvtps2pd` of float differences).
//!
//! 1. **End points** (end point first, then the origin; the origin is skipped when it lies within
//!    `min_pt_dist` of the centre, float compare): a point `Q + R cos φ + (a × R) sin φ` (`Q` the
//!    axial projection, `R` the radial part) hits the sphere when `k + A cos φ + B sin φ = 0`,
//!    `A = 2 (Q−S)·R`, `B = 2 (Q−S)·(a×R)`, `k = |R|² + |Q−S|² − r²`. With `c2 = A² + B²` the two
//!    candidates `sin φ = s/c2`, `s = −kB ∓ √(k²B² − (k² − A²) c2)`, are accepted when
//!    `0 < s < c2` and `(s B + c2 k) A < 0` (sign bits of float-rounded values) and kept as a
//!    fraction `num/den` if larger than the current best (start 0/1); feature = end-point index.
//! 2. **Side**: the closest-approach condition between the rotating line and the sphere centre
//!    gives a quartic in `s = sin φ` (built in double from float cross products), counted by the
//!    Sturm routine on `[0, 1]` and solved by findroots (see [`super`]); a root is accepted
//!    when the closest point lies strictly inside the segment, the distance is within
//!    `r·[0.99, 1.01]` and `s` beats the current best (then `den = 1`); feature 0x40.
//! 3. Output: none found → 0. Side: `t = num` (no division: `den` is 1 after a side update),
//!    `cos_t = √(1 − t²)`, `pt` = closest point of the rotated line to the centre (`center`
//!    added back in float), `n = normalize(S − pt)` in double (≤ 1e-20 → (0,0,1)), features
//!    `(0x20, 0)`. End point `i`: `t = num/den` (0 if `den` is 0/NaN), `pt` = the rotated end
//!    point, `n = normalize(sphere.center − pt)` in float, features `(0x80 | i, 0)`.
//!
//! `tmax` is not read: the caller compares `t` itself. Nothing else (params, area) is written.

use super::super::common::{as_ray, as_sph, lt_or_nan, lt_or_nan_d, swap_rot, ucomisd_eq};
use super::super::{UnprojTables, MODE_ROTATIONAL};
use super::{count_roots, find_roots, Poly4};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{Primitive, RayPrim, SpherePrim};
use crate::ids::GeomType;
use crate::math::Vec3;

#[inline]
fn d(x: f32) -> f64 {
    f64::from(x)
}

/// Sign bit of the float-rounded value.
#[inline]
fn sbit(x: f64) -> u32 {
    (x as f32).to_bits() >> 31
}

/// Best candidate so far as the fraction `num / den`, with its feature.
struct Best {
    num: f64,
    den: f64,
    feat: i32,
}

impl Best {
    /// Branch-free update of the binary: `e` = 1 takes `(n, dn)` and `feat`.
    fn take(&mut self, e: u32, n: f64, dn: f64, feat: i32) {
        if e != 0 {
            self.feat = feat;
        }
        let ke = f64::from(e);
        let kn = f64::from(e ^ 1);
        self.den = kn * self.den + ke * dn;
        self.num = kn * self.num + ke * n;
    }
}

/// sub_351A7CC0: rotational unprojection of a ray (segment) from a sphere (see the module docs).
pub fn unproject_rot_ray_sph(
    p: &mut UnprojParams,
    ray: &RayPrim,
    _fa: i32,
    sph: &SpherePrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let r2s = d(sph.r * sph.r);
    let (ax, ay, az) = (d(p.dir.x), d(p.dir.y), d(p.dir.z));
    let c = p.center;
    let sx = d(sph.center.x - c.x);
    let sy = d(sph.center.y - c.y);
    let sz = d(sph.center.z - c.z);
    let ox = d(ray.origin.x - c.x);
    let oy = d(ray.origin.y - c.y);
    let oz = d(ray.origin.z - c.z);

    // [k] = axial projection Q, radial part R, a × R of the origin (0) and the end point (1)
    let mut q = [[0.0f64; 3]; 2];
    let mut r = [[0.0f64; 3]; 2];
    let mut t = [[0.0f64; 3]; 2];
    let dot0 = (oy * ay + ox * ax) + oz * az;
    q[0] = [dot0 * ax, ay * dot0, az * dot0];
    r[0] = [ox - q[0][0], oy - q[0][1], oz - q[0][2]];
    t[0] = [
        r[0][2] * ay - r[0][1] * az,
        az * r[0][0] - r[0][2] * ax,
        r[0][1] * ax - ay * r[0][0],
    ];
    let ex = d(ray.dir.x) + ox;
    let ey = d(ray.dir.y) + oy;
    let ez = d(ray.dir.z) + oz;
    let dot1 = (ey * ay + ex * ax) + ez * az;
    q[1] = [dot1 * ax, dot1 * ay, az * dot1];
    r[1] = [ex - q[1][0], ey - q[1][1], ez - q[1][2]];
    t[1] = [
        r[1][2] * ay - r[1][1] * az,
        az * r[1][0] - r[1][2] * ax,
        r[1][1] * ax - r[1][0] * ay,
    ];
    let oxf = ray.origin.x - c.x;
    let oyf = ray.origin.y - c.y;
    let ozf = ray.origin.z - c.z;
    let dist2 = (oxf * oxf + oyf * oyf) + ozf * ozf;
    let mpd2 = p.min_pt_dist * p.min_pt_dist;

    let mut best = Best {
        num: 0.0,
        den: 1.0,
        feat: -1,
    };
    for k in [1usize, 0] {
        let (qv, rv, tv) = (q[k], r[k], t[k]);
        let dx = qv[0] - sx;
        let dy = qv[1] - sy;
        let dz = qv[2] - sz;
        let b0 = (dy * rv[1] + rv[0] * dx) + dz * rv[2];
        let a0 = (dx * tv[0] + dy * tv[1]) + dz * tv[2];
        let bb = a0 * 2.0;
        let aa = b0 * 2.0;
        let c2 = bb * bb + aa * aa;
        let rlen = (rv[0] * rv[0] + rv[1] * rv[1]) + rv[2] * rv[2];
        let dlen = (dy * dy + dx * dx) + dz * dz;
        let kk = (rlen + dlen) - r2s;
        let kb = kk * bb;
        let disc = kb * kb - (kk * kk - aa * aa) * c2;
        // comisd disc, 0; jb → no candidates (negative or NaN)
        if !lt_or_nan_d(disc, 0.0) {
            let sq = disc.sqrt();
            let c2k = c2 * kk;
            let s1 = -kb - sq;
            let s2 = s1 + sq * 2.0;
            for s in [s1, s2] {
                let w1 = (s * 2.0 - c2).abs() - c2;
                let w2 = (s * bb + c2k) * aa;
                if sbit(w1) & sbit(w2) != 0 {
                    let e = sbit(best.num * c2 - best.den * s);
                    best.take(e, s, c2, k as i32);
                }
            }
        }
        // comiss dist2, mpd2; jb → the origin is not tested
        if lt_or_nan(dist2, mpd2) {
            break;
        }
    }

    // ---- side of the segment: quartic in s = sin φ
    let ddf = (p.dir.y * ray.dir.y + p.dir.x * ray.dir.x) + p.dir.z * ray.dir.z;
    let dd = d(ddf);
    let dpx = dd * ax;
    let dpy = dd * ay;
    let dpz = dd * az;
    // radial part of the direction, rounded to float
    let wx = d((d(ray.dir.x) - dpx) as f32);
    let wy = d((d(ray.dir.y) - dpy) as f32);
    let wz = d((d(ray.dir.z) - dpz) as f32);
    let vx = wz * ay - wy * az;
    let vy = wx * az - wz * ax;
    let vz = wy * ax - wx * ay;
    // origin × dir in float
    let mxf = ray.dir.z * oyf - ray.dir.y * ozf;
    let myf = ray.dir.x * ozf - oxf * ray.dir.z;
    let mzf = oxf * ray.dir.y - ray.dir.x * oyf;
    let (mx, my, mz) = (d(mxf), d(myf), d(mzf));
    let mdot = (my * ay + mx * ax) + mz * az;
    let mpx = mdot * ax;
    let mpy = mdot * ay;
    let mpz = mdot * az;
    let mrx = mx - mpx;
    let mry = my - mpy;
    let mrz = mz - mpz;
    let mtx = mrz * ay - mry * az;
    let mty = mrx * az - mrz * ax;
    let mtz = mry * ax - mrx * ay;
    let g1x = (wy * sz - wz * sy) + mrx;
    let g1y = (wz * sx - wx * sz) + mry;
    let g1z = (wx * sy - wy * sx) + mrz;
    let g2x = (vy * sz - vz * sy) + mtx;
    let g2y = (vz * sx - vx * sz) + mty;
    let g2z = (vx * sy - vy * sx) + mtz;
    let hx = (dpy * sz - dpz * sy) + mpx;
    let hy = (dpz * sx - dpx * sz) + mpy;
    let hz = (dpx * sy - dpy * sx) + mpz;
    let pp = (hx * g1x + hy * g1y) + hz * g1z;
    let qq = (g2x * g1x + g2y * g1y) + g2z * g1z;
    let q2 = qq * qq;
    let qp = qq * pp;
    let p2 = pp * pp;
    let qp2 = qp + qp;
    let e6 = (q2 * 0.0 - qp2) * 4.0;
    // `mulsd` by −1.0 (not a sign flip: a NaN keeps its sign)
    #[allow(clippy::neg_multiply)]
    let e7 = (q2 * -1.0) * 4.0;
    let e8 = (qp2 + p2 * 0.0) * 4.0;
    let e9 = ((q2 + qp2 * 0.0) - p2) * 4.0;
    let e10 = p2 * 4.0;
    let dlen2 = (ray.dir.y * ray.dir.y + ray.dir.x * ray.dir.x) + ray.dir.z * ray.dir.z;
    let g1sq = (g1x * g1x + g1y * g1y) + g1z * g1z;
    let hsq = (hx * hx + hy * hy) + hz * hz;
    let rr = d(dlen2) * r2s;
    let v105 = ((hx * g2x + hy * g2y) + hz * g2z) * 2.0;
    let g2sq = (g2x * g2x + g2y * g2y) + g2z * g2z;
    let v106 = g2sq - g1sq;
    let v107 = (g1sq + hsq) - rr;
    let poly = Poly4 {
        denom: 1.0,
        data: [
            v107 * v107 - e10,
            (v105 * v107 + v105 * v107) - e8,
            ((v106 * v107 + v105 * v105) + v106 * v107) - e9,
            (v106 * v105 + v106 * v105) - e6,
            v106 * v106 - e7,
        ],
    };
    let (p0, r0, t0) = (q[0], r[0], t[0]);
    if count_roots(&poly, 0.0, 1.0) != 0 {
        let mut roots = [0.0f64; 4];
        let n = find_roots(&poly, 0.0, 1.0, &mut roots, 20);
        for i in (0..n.max(0) as usize).rev() {
            let s = roots[i];
            let cs = (1.0 - s * s).sqrt();
            let px = (p0[0] + r0[0] * cs) + t0[0] * s;
            let py = (r0[1] * cs + p0[1]) + t0[1] * s;
            let pz = (r0[2] * cs + p0[2]) + t0[2] * s;
            let qy = (wy * cs + dpy) + vy * s;
            let qz = (wz * cs + dpz) + vz * s;
            let qx = (dpx + wx * cs) + vx * s;
            let tq = ((sx - px) * qx + (sy - py) * qy) + (sz - pz) * qz;
            let ql = (qy * qy + qx * qx) + qz * qz;
            let hi = ql * 1.01;
            let lo = ql * 0.99;
            let a = (hi * hi) * r2s;
            let b = (lo * lo) * r2s;
            let x = (px - sx) * ql + qx * tq;
            let y = (py - sy) * ql + qy * tq;
            let z = (pz - sz) * ql + qz * tq;
            let m = ((x * x + y * y) + z * z) * 2.0;
            let w1 = ((a + b) - m).abs() - (b - a).abs();
            let w2 = (ql - tq * 2.0).abs() - (-ql).abs();
            let w3 = best.num - best.den * s;
            let e = sbit(w1) & sbit(w2) & sbit(w3);
            best.take(e, s, 1.0, 0x40);
        }
    }

    if best.feat < 0 {
        return 0;
    }
    let (num, den) = (best.num, best.den);
    match best.feat & 0xF0 {
        0x40 => {
            out.t = num;
            let cs = (1.0 - num * num).sqrt();
            out.cos_t = cs;
            let px = (r0[0] * cs + p0[0]) + t0[0] * num;
            let py = (r0[1] * cs + p0[1]) + t0[1] * num;
            let pz = (r0[2] * cs + p0[2]) + t0[2] * num;
            let qz = (wz * cs + dpz) + vz * num;
            let qy = (wy * cs + dpy) + vy * num;
            let qx = (dpx + wx * cs) + vx * num;
            let tq = ((sx - px) * qx + (sy - py) * qy) + (sz - pz) * qz;
            let ql = (qx * qx + qy * qy) + qz * qz;
            // ucomisd ql, 0; je → also NaN
            let u = if ucomisd_eq(ql, 0.0) { 0.0 } else { tq / ql };
            out.pt = Vec3::new(
                (qx * u + px) as f32,
                (qy * u + py) as f32,
                (qz * u + pz) as f32,
            );
            let nx = sx - d(out.pt.x);
            let ny = sy - d(out.pt.y);
            let nz = sz - d(out.pt.z);
            let l2 = (nx * nx + ny * ny) + nz * nz;
            let (nx, ny, nz) = if l2 > f64::from(1e-20f32) {
                let inv = 1.0 / l2.sqrt();
                (nx * inv, ny * inv, nz * inv)
            } else {
                (0.0, 0.0, 1.0)
            };
            out.n = Vec3::new(nx as f32, ny as f32, nz as f32);
            out.pt = Vec3::new(c.x + out.pt.x, c.y + out.pt.y, c.z + out.pt.z);
            out.i_feature = [0x20, 0];
        }
        0 => {
            let k = (best.feat & 1) as usize;
            // ucomisd den, 0; je → 0 (also NaN)
            let tt = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
            out.t = tt;
            let cs = (1.0 - tt * tt).sqrt();
            out.cos_t = cs;
            let (qv, rv, tv) = (q[k], r[k], t[k]);
            let px = (rv[0] * cs + qv[0]) + tv[0] * tt;
            let py = (rv[1] * cs + qv[1]) + tv[1] * tt;
            let pz = (rv[2] * cs + qv[2]) + tv[2] * tt;
            out.pt = Vec3::new(
                (d(c.x) + px) as f32,
                (d(c.y) + py) as f32,
                (d(c.z) + pz) as f32,
            );
            let nx = sph.center.x - out.pt.x;
            let ny = sph.center.y - out.pt.y;
            let nz = sph.center.z - out.pt.z;
            let l2 = (nx * nx + ny * ny) + nz * nz;
            out.n = if l2 > 1e-20f32 {
                let inv = 1.0 / l2.sqrt();
                Vec3::new(nx * inv, ny * inv, nz * inv)
            } else {
                Vec3::new(0.0, 0.0, 1.0)
            };
            out.i_feature = [best.feat | 0x80, 0];
        }
        _ => {}
    }
    1
}

/// `[1][ray][sph]`.
fn cell_rot_ray_sph(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_rot_ray_sph(p, as_ray(a), fa, as_sph(b), fb, out)
}

/// `[1][sph][ray]`: sub_351A9170, the rotational swap wrapper (negate the axis, call
/// 0x351A7CC0 with the operands exchanged, rotate `pt` and `n` back by −φ, negate `n`, swap the
/// features, restore the axis) — [`swap_rot`].
fn cell_rot_sph_ray(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    let (s, r) = (as_sph(a), as_ray(b));
    swap_rot(p, out, |p, out| unproject_rot_ray_sph(p, r, fb, s, fa, out))
}

/// Fill `[1][ray][sph]` and `[1][sph][ray]`.
pub fn fill(t: &mut UnprojTables) {
    t.set(
        MODE_ROTATIONAL,
        GeomType::Ray,
        GeomType::Sphere,
        cell_rot_ray_sph,
    );
    t.set(
        MODE_ROTATIONAL,
        GeomType::Sphere,
        GeomType::Ray,
        cell_rot_sph_ray,
    );
}
