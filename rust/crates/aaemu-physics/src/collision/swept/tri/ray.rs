//! tri–ray linear unprojection (x64 0x35176B40, x86 0x3517F190).

use super::{d, sel, sel_feat, sgn};
use crate::collision::contact::{UnprojParams, UnprojResult};
use crate::collision::swept::common::{ucomisd_eq, DEC_MOD3, INC_MOD3};
use crate::geometry::primitives::{RayPrim, Triangle};
use crate::math::Vec3;

/// sub_35176B40: move triangle A along `dir` until the ray segment B no longer crosses it.
///
/// The best `t` is a rational `num/den` (doubles), starting at `0/1`; a candidate replaces it
/// when `(float)(num·den_c − den·num_c)` has its sign bit set (the candidate is larger).
///
/// 1. **Ray endpoints** `o` and `o + D` (the second accumulated in double as `o + D`): the time
///    `((o − p0)·n) / (n·dir)` at which the endpoint lies in the moved triangle plane, accepted
///    if the endpoint projected along `dir` is inside all three edges (cross products dotted
///    with `n`, sign bits; nothing is flipped by the sign of `n·dir`). Internal feature `k`.
/// 2. **Triangle edges** against the ray line: with `c = e × D`, the time at which the moved
///    edge meets the ray line (`(w·c)/(dir·c)`, both multiplied by the sign of the latter);
///    accepted if the meeting point lies strictly inside both the edge and the ray segment
///    (`|2·proj − |c|²·dn| − |c|²·dn < 0` twice). Internal feature `0x80|i`.
///
/// Output (internal −1 → return 0, nothing written): endpoint → `t = num/den` (0 if `den` is
/// 0 or NaN), `pt = o + D·k`, `n = tri.n`, features 0x40/`0x80|k`; edge → `t = (num/(|c|²·den))
/// ·|c|²`, `pt` = the meeting point on the edge `+ dir·t`, `n = D × e` oriented away from the
/// third vertex (sign of `n·(p_i − p_{i−1})`), not normalised, features `0xA0|i`/0xA0.
/// `tmin`, `tmax`, the input feature ids and the area are not used.
pub fn unproject_tri_ray(
    p: &mut UnprojParams,
    a: &Triangle,
    _fa: i32,
    b: &RayPrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let pt = &a.pt;
    let n = a.n;
    let dir = p.dir;
    let o = b.origin;
    let dv = b.dir;
    let (nx, ny, nz) = (d(n.x), d(n.y), d(n.z));
    let mut feat: i32 = -1;
    let mut num = 0.0f64;
    let mut den = 1.0f64;
    let dn = (dir.y * n.y + n.x * dir.x) + n.z * dir.z;
    let dn_d = d(dn);
    let dnf = dn;
    let ptd: [[f32; 3]; 3] = [
        [pt[0].x * dnf, pt[0].y * dnf, pt[0].z * dnf],
        [pt[1].x * dnf, pt[1].y * dnf, pt[1].z * dnf],
        [pt[2].x * dnf, pt[2].y * dnf, pt[2].z * dnf],
    ];
    let mut ox = d(o.x);
    let mut oy = d(o.y);
    let mut oz = d(o.z);
    for k in 0..2 {
        let sd = (nx * (ox - d(pt[0].x)) + ny * (oy - d(pt[0].y))) + nz * (oz - d(pt[0].z));
        let sdf = sd as f32;
        let qx = ox * dn_d - d(dir.x * sdf);
        let qy = oy * dn_d - d(dir.y * sdf);
        let qz = oz * dn_d - d(dir.z * sdf);
        let side = |i: usize, j: usize| -> u32 {
            let ex = pt[j].x - pt[i].x;
            let ey = pt[j].y - pt[i].y;
            let ez = pt[j].z - pt[i].z;
            let ax = (qx - d(ptd[i][0])) as f32;
            let ay = (qy - d(ptd[i][1])) as f32;
            let az = (qz - d(ptd[i][2])) as f32;
            let v =
                (n.y * (ez * ax - az * ex) + n.x * (az * ey - ay * ez)) + n.z * (ay * ex - ey * ax);
            v.to_bits()
        };
        let c0 = side(0, 1);
        let c1 = side(1, 2);
        let c2 = side(2, 0);
        let h = (!(c1 | c2 | c0) >> 31) & sgn((num * dn_d - den * sd) as f32);
        feat = sel_feat(h, feat, k);
        den = sel(h, den, dn_d);
        num = sel(h, num, sd);
        // The second endpoint is `o + D` accumulated in double (never rounded to float).
        ox += d(dv.x);
        oy += d(dv.y);
        oz += d(dv.z);
    }

    let (dirx, diry, dirz) = (d(dir.x), d(dir.y), d(dir.z));
    let (ddx, ddy, ddz) = (d(dv.x), d(dv.y), d(dv.z));
    for i in 0..3 {
        let j = INC_MOD3[i] as usize;
        let p0 = pt[i];
        let ex = d(pt[j].x - p0.x);
        let ey = d(pt[j].y - p0.y);
        let ez = d(pt[j].z - p0.z);
        let cx = ddz * ey - ddy * ez;
        let cy = ez * ddx - ddz * ex;
        let cz = ddy * ex - ey * ddx;
        let c2 = (cy * cy + cx * cx) + cz * cz;
        let wx = d(o.x - p0.x);
        let wy = d(o.y - p0.y);
        let wz = d(o.z - p0.z);
        let sd0 = (wy * cy + wx * cx) + wz * cz;
        let dn0 = (diry * cy + dirx * cx) + dirz * cz;
        let s = f64::from((((dn0 as f32).to_bits() as i32 >> 30) & !1) + 1);
        let sd = sd0 * s;
        let dn = dn0 * s;
        let sdf = sd as f32;
        let ux = wx * dn - d(dir.x * sdf);
        let uy = wy * dn - d(dir.y * sdf);
        let uz = wz * dn - d(dir.z * sdf);
        let c2d = c2 * dn;
        let proj1 =
            ((ddz * uy - ddy * uz) * cx + (uz * ddx - ddz * ux) * cy) + (ddy * ux - uy * ddx) * cz;
        let in1 = sgn((((proj1 * 2.0) - c2d).abs() - c2d) as f32);
        let proj2 =
            ((uy * ez - uz * ey) * cx + (uz * ex - ez * ux) * cy) + (ey * ux - uy * ex) * cz;
        let in2 = sgn((((proj2 * 2.0) - c2d).abs() - c2d) as f32);
        let x = sgn((num * dn - den * sd) as f32) & in2 & in1;
        feat = sel_feat(x, feat, i as i32 | 0x80);
        den = sel(x, den, dn);
        num = sel(x, num, sd);
    }

    if feat == -1 {
        return 0;
    }
    if feat & 0x80 == 0 {
        out.t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
        let k = feat as f32;
        out.pt = Vec3::new(dv.x * k + o.x, dv.y * k + o.y, dv.z * k + o.z);
        out.n = n;
        out.i_feature = [0x40, feat | 0x80];
        return 1;
    }
    let i = (feat & 3) as usize;
    let j = INC_MOD3[i] as usize;
    let p0 = pt[i];
    let exf = pt[j].x - p0.x;
    let eyf = pt[j].y - p0.y;
    let ezf = pt[j].z - p0.z;
    let (ex, ey, ez) = (d(exf), d(eyf), d(ezf));
    let cx = ddz * ey - ddy * ez;
    let cy = ez * ddx - ddz * ex;
    let cz = ddy * ex - ey * ddx;
    let c2 = (cy * cy + cx * cx) + cz * cz;
    let numf = num as f32;
    let denf = den as f32;
    let ux = d((o.x - p0.x) * denf - dir.x * numf);
    let uy = d((o.y - p0.y) * denf - dir.y * numf);
    let uz = d((o.z - p0.z) * denf - dir.z * numf);
    // No zero check on den (the binary divides unconditionally here, unlike the endpoint case).
    let inv = 1.0 / (c2 * den);
    let pr = ((uz * ddx - ddz * ux) * cy + (ddz * uy - ddy * uz) * cx) + (ddy * ux - uy * ddx) * cz;
    let t = (inv * num) * c2;
    out.t = t;
    let tf = t as f32;
    out.pt = Vec3::new(
        (d(p0.x) + (ex * pr) * inv) as f32 + dir.x * tf,
        (d(p0.y) + (ey * pr) * inv) as f32 + dir.y * tf,
        (d(p0.z) + (ez * pr) * inv) as f32 + dir.z * tf,
    );
    let nn = Vec3::new(
        ezf * dv.y - eyf * dv.z,
        exf * dv.z - ezf * dv.x,
        eyf * dv.x - exf * dv.y,
    );
    let k = DEC_MOD3[i] as usize;
    let qx = p0.x - pt[k].x;
    let qy = p0.y - pt[k].y;
    let qz = p0.z - pt[k].z;
    let dot = (qy * nn.y + nn.x * qx) + qz * nn.z;
    let s = ((((dot.to_bits() as i32) >> 30) & !1) + 1) as f32;
    out.n = Vec3::new(s * nn.x, s * nn.y, s * nn.z);
    out.i_feature = [i as i32 | 0xA0, 0xA0];
    1
}
