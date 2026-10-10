//! tri–sph linear unprojection (x64 0x35175490, x86 0x3517DB70).

use super::{d, sel, sel_feat, sgn};
use crate::collision::contact::{UnprojParams, UnprojResult};
use crate::collision::swept::common::{lt_or_nan_d, ucomisd_eq, INC_MOD3};
use crate::geometry::primitives::{SpherePrim, Triangle};
use crate::math::Vec3;

/// sub_35175490: move triangle A along `dir` until it stops touching sphere B.
///
/// The best `t` is kept as a rational `num/den` (doubles) and every candidate replaces it when
/// `(float)(den_c·num − den·num_c)` has its sign bit set (i.e. the candidate is *larger*);
/// selections are branch-free blends `(1−s)·old + s·new` with `s ∈ {0, 1}` in double.
///
/// 1. **Face:** `sd = (cB − p0)·n − r`, `dn = n·dir`, both multiplied by the sign (bit) of `dn`.
///    The sphere centre is moved along `dir` onto the plane (everything scaled by `dn`) and
///    tested against the three edges (cross products dotted with `n`, sign bits); if it is
///    inside and `(float)(tmin·dn − sd) < 0` the start value is `sd/dn` with feature 0,
///    otherwise `tmin/1` with feature −1.
/// 2. **Vertices:** `|p_i + dir·t − c|² = r²`, both roots (`−b ∓ √disc`, over `a = |dir|²`),
///    internal feature `0x40|i`.
/// 3. **Edges:** distance of the swept centre to the edge line `= r`, both roots
///    (`r2 = r1 + 2√disc`), each accepted only if the foot lies strictly inside the edge
///    (`|2·proj − L²A| − L²A < 0`); internal feature `0x80|i`.
///
/// Output (`−1` → return 0, nothing written): face → `t = num/den` (0 if `den` is 0 or NaN),
/// `pt = c − n·r`, `n = tri.n`, features 0x40/0x40; vertex → `pt = p_i + dir·t`, `n = c − pt`
/// (not normalised), features `0x80|i`/0x40; edge → `t = (num/(L²·den))·L²`, `pt` = foot on
/// the edge `+ dir·t`, `n = c − pt`, features `0xA0|i`/0x40. The input feature ids, `tmax` and
/// the area are not used.
pub fn unproject_tri_sph(
    p: &mut UnprojParams,
    a: &Triangle,
    _fa: i32,
    b: &SpherePrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let pt = &a.pt;
    let n = a.n;
    let c = b.center;
    let dir = p.dir;
    let r2 = d(b.r * b.r);
    let tmin = d(p.tmin);
    let sd = ((n.x * (c.x - pt[0].x) + (c.y - pt[0].y) * n.y) + (c.z - pt[0].z) * n.z) - b.r;
    let dn = (n.x * dir.x + n.y * dir.y) + n.z * dir.z;
    let s = f64::from(((dn.to_bits() as i32 >> 30) & !1) + 1);
    let sd_d = d(sd) * s;
    let dn_d = d(dn) * s;
    let sdf = sd_d as f32;
    let dnf = dn_d as f32;
    let q = [
        c.x * dnf - dir.x * sdf,
        c.y * dnf - dir.y * sdf,
        c.z * dnf - dir.z * sdf,
    ];
    let edge_side = |i: usize, j: usize| -> u32 {
        let e = [pt[j].x - pt[i].x, pt[j].y - pt[i].y, pt[j].z - pt[i].z];
        let w = [
            q[0] - pt[i].x * dnf,
            q[1] - pt[i].y * dnf,
            q[2] - pt[i].z * dnf,
        ];
        let v = (n.y * (w[0] * e[2] - e[0] * w[2]) + n.x * (w[2] * e[1] - w[1] * e[2]))
            + n.z * (e[0] * w[1] - w[0] * e[1]);
        v.to_bits()
    };
    let c0 = edge_side(0, 1);
    let c1 = edge_side(1, 2);
    let c2 = edge_side(2, 0);
    let inside = !(c1 | c2 | c0) >> 31;
    let h = inside & sgn((tmin * dn_d - sd_d) as f32);
    let mut feat = h as i32 - 1;
    let mut num = f64::from(h ^ 1) * tmin + f64::from(h) * sd_d;
    let mut den = f64::from(h) * dn_d + f64::from(h ^ 1);

    let (dx, dy, dz) = (d(dir.x), d(dir.y), d(dir.z));
    let dd = (dx * dx + dy * dy) + dz * dz;
    for (i, v) in pt.iter().enumerate() {
        let vx = d(v.x - c.x);
        let vy = d(v.y - c.y);
        let vz = d(v.z - c.z);
        let bb = (dx * vx + dy * vy) + dz * vz;
        let disc = bb * bb - (((vx * vx + vy * vy) + vz * vz) - r2) * dd;
        if lt_or_nan_d(disc, 0.0) {
            continue;
        }
        let sq = disc.sqrt();
        let t1 = -bb - sq;
        let t2 = sq - bb;
        let fid = i as i32 | 0x40;
        let x = sgn((dd * num - den * t1) as f32);
        feat = sel_feat(x, feat, fid);
        den = sel(x, den, dd);
        num = sel(x, num, t1);
        let x = sgn((dd * num - den * t2) as f32);
        feat = sel_feat(x, feat, fid);
        den = sel(x, den, dd);
        num = sel(x, num, t2);
    }

    for i in 0..3 {
        let j = INC_MOD3[i] as usize;
        let p0 = pt[i];
        let ex = d(pt[j].x - p0.x);
        let ey = d(pt[j].y - p0.y);
        let ez = d(pt[j].z - p0.z);
        let wx = d(p0.x - c.x);
        let wy = d(p0.y - c.y);
        let wz = d(p0.z - c.z);
        let v70 = wz * ey - wy * ez;
        let v71 = ex * wy - wx * ey;
        let v72 = wx * ez - ex * wz;
        let v73 = dz * ey - dy * ez;
        let v74 = dx * ez - ex * dz;
        let v75 = ex * dy - dx * ey;
        let aa = (v73 * v73 + v74 * v74) + v75 * v75;
        let bb = (v74 * v72 + v73 * v70) + v75 * v71;
        let l2 = (ex * ex + ey * ey) + ez * ez;
        let disc = bb * bb - ((((v72 * v72 + v70 * v70) + v71 * v71) - l2 * r2) * aa);
        if lt_or_nan_d(disc, 0.0) {
            continue;
        }
        let sq = disc.sqrt();
        let af = aa as f32;
        let (exf, eyf, ezf) = (ex as f32, ey as f32, ez as f32);
        let l2a = l2 * aa;
        let inside_edge = |t: f64| -> u32 {
            let tf = t as f32;
            let ux = (c.x - p0.x) * af - dir.x * tf;
            let uy = af * (c.y - p0.y) - dir.y * tf;
            let uz = af * (c.z - p0.z) - dir.z * tf;
            let proj = (exf * ux + eyf * uy) + ezf * uz;
            sgn(((d(proj) * 2.0 - l2a).abs() - l2a) as f32)
        };
        let fid = i as i32 | 0x80;
        let t1 = -bb - sq;
        let ins = inside_edge(t1);
        let x = sgn((aa * num - den * t1) as f32) & ins;
        feat = sel_feat(x, feat, fid);
        let t2 = t1 + sq * 2.0;
        den = sel(x, den, aa);
        num = sel(x, num, t1);
        let ins = inside_edge(t2);
        let x = sgn((aa * num - den * t2) as f32) & ins;
        feat = sel_feat(x, feat, fid);
        den = sel(x, den, aa);
        num = sel(x, num, t2);
    }

    if feat == -1 {
        return 0;
    }
    match feat & 0xC0 {
        0 => {
            out.t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
            let r = b.r;
            out.pt = Vec3::new(c.x - n.x * r, c.y - n.y * r, c.z - n.z * r);
            out.n = n;
            out.i_feature = [0x40, 0x40];
        }
        0x40 => {
            let v = (feat & 3) as usize;
            let t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
            out.t = t;
            let tf = t as f32;
            out.pt = Vec3::new(
                dir.x * tf + pt[v].x,
                dir.y * tf + pt[v].y,
                dir.z * tf + pt[v].z,
            );
            out.n = Vec3::new(c.x - out.pt.x, c.y - out.pt.y, c.z - out.pt.z);
            out.i_feature = [v as i32 | 0x80, 0x40];
        }
        0x80 => {
            let i = (feat & 3) as usize;
            let j = INC_MOD3[i] as usize;
            let p0 = pt[i];
            let denf = den as f32;
            let numf = num as f32;
            let exf = pt[j].x - p0.x;
            let eyf = pt[j].y - p0.y;
            let ezf = pt[j].z - p0.z;
            let ux = (c.x - p0.x) * denf - dir.x * numf;
            let uy = denf * (c.y - p0.y) - dir.y * numf;
            let uz = denf * (c.z - p0.z) - dir.z * numf;
            let proj = (exf * ux + eyf * uy) + ezf * uz;
            let (ex, ey, ez) = (d(exf), d(eyf), d(ezf));
            let l2 = (ex * ex + ey * ey) + ez * ez;
            let inv = 1.0 / (l2 * den);
            let t = (inv * num) * l2;
            out.t = t;
            let tf = t as f32;
            let k = inv * d(proj);
            out.pt = Vec3::new(
                (d(p0.x) + k * ex) as f32 + dir.x * tf,
                (d(p0.y) + k * ey) as f32 + dir.y * tf,
                (d(p0.z) + k * ez) as f32 + dir.z * tf,
            );
            out.n = Vec3::new(c.x - out.pt.x, c.y - out.pt.y, c.z - out.pt.z);
            out.i_feature = [i as i32 | 0xA0, 0x40];
        }
        _ => {}
    }
    1
}
