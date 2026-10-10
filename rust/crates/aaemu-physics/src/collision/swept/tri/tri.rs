//! tri–tri linear unprojection (x64 0x3516EAE0, x86 0x35176460).

use super::{d, sel, sel_feat, sgn, sign_pm1};
use crate::collision::contact::{UnprojParams, UnprojResult};
use crate::collision::swept::common::{lt_or_nan_d, ucomisd_eq, INC_MOD3};
use crate::geometry::primitives::Triangle;
use crate::math::Vec3;

/// `((|a − b| + (a + b))·0.5` in double (the binary's max idiom).
#[inline]
fn max_idiom_d(a: f64, b: f64) -> f64 {
    ((a - b).abs() + (a + b)) * 0.5
}

/// sub_3516EAE0: move triangle A along `dir` until it stops intersecting triangle B.
///
/// The best `t` is a rational `num/den` (doubles), starting at `0/1`; a candidate replaces it
/// when `(float)(num·den_c − den·num_c)` has its sign bit set (the candidate is larger).
///
/// 1. **Vertex–face**, twice: with A's face (`k = 0`, sign `s0 = −1`) and with B's face
///    (`k = 1`, `s0 = +1`). The vertex `m` of the other triangle that is deepest along the face
///    normal (`n·p` times `s0·sign(n·dir)`, min idiom, ties → lower index) is moved along
///    `dir` onto the face plane and tested against the three face edges (double cross products
///    dotted with `n`; inside when the max idiom of the three is negative). Internal feature
///    `k<<2 | m`.
/// 2. **Edge–edge**, all 3×3 pairs whose cross product `c = eA × eB` is not near-parallel
///    (`|c|² ≥ |eA|²·|eB|²·1e-8`, also skipped on NaN): the time `(w·c)/(dir·c)` (sign of the
///    float `dir·c` folded in, `c` rounded to float for that dot) at which the edge lines meet,
///    accepted if the meeting point lies strictly inside both edges. Internal feature
///    `0x80 | i<<2 | j`.
///
/// Returns 0 (nothing written) if nothing was found or `|den| < 1e-20` (or NaN). Vertex–face →
/// `t = num/den` (0 if `den` is 0/NaN), `pt` = other's vertex `+ dir·(k·t)`, `n = ±n_face`
/// (`1 − 2k`), features: face side 0x40, vertex side `0x80|m`. Edge–edge → `t =
/// (num/(|c|²·den))·|c|²`, `pt` = the meeting point on A's edge `+ dir·t`, `n = c` oriented by
/// the sign of `c·(nB × eB)`, features `0xA0|i` / `0xA0|j`. Input feature ids, `tmin`/`tmax`
/// and the area are not used.
pub fn unproject_tri_tri(
    p: &mut UnprojParams,
    a: &Triangle,
    _fa: i32,
    b: &Triangle,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let tris = [a, b];
    let dir = p.dir;
    let mut feat: i32 = -1;
    let mut num = 0.0f64;
    let mut den = 1.0f64;

    for k in 0..2usize {
        let s0: i32 = if k == 0 { -1 } else { 1 };
        let cur = tris[k];
        let oth = tris[k ^ 1];
        let n = cur.n;
        let cp = &cur.pt;
        let op = &oth.pt;
        let dn0 = (n.y * dir.y + n.x * dir.x) + n.z * dir.z;
        let f = f64::from(sign_pm1(dn0) * s0);
        let ndot = |v: Vec3| (n.y * v.y + n.x * v.x) + n.z * v.z;
        let d0 = d(ndot(op[0])) * f;
        let d1 = d(ndot(op[1])) * f;
        let diff = d1 - d0;
        let lo = ((d1 + d0) - diff.abs()) * 0.5;
        let c = sgn((d(ndot(op[2])) * f - lo) as f32);
        let m = ((sgn(diff as f32) & (c ^ 1)) | (c << 1)) as usize;
        let om = op[m];
        let s0f = s0 as f32;
        let sd0 =
            ((n.y * (cp[0].y - om.y) + n.x * (cp[0].x - om.x)) + n.z * (cp[0].z - om.z)) * s0f;
        let dn = (dir.x * n.x + dir.y * n.y) + dir.z * n.z;
        let s2 = f64::from(sign_pm1(dn));
        let sd = d(sd0) * s2;
        let dnp = d(dn) * s2;
        let dnf = dnp as f32;
        let qx = d((d(dnf * om.x) + d(s0f * dir.x) * sd) as f32);
        let qy = d((d(dnf * om.y) + sd * d(s0f * dir.y)) as f32);
        let qz = d((d(dnf * om.z) + sd * d(s0f * dir.z)) as f32);
        let (nx, ny, nz) = (d(n.x), d(n.y), d(n.z));
        // edge p2 → p0
        let (gx, gy, gz) = (
            d(cp[0].x - cp[2].x),
            d(cp[0].y - cp[2].y),
            d(cp[0].z - cp[2].z),
        );
        let ax = qx - d(dnf * cp[2].x);
        let ay = qy - d(dnf * cp[2].y);
        let az = qz - d(dnf * cp[2].z);
        let c2 = (nx * (gz * ay - gy * az) + ny * (gx * az - ax * gz)) + nz * (ax * gy - gx * ay);
        // edge p1 → p2
        let (fx, fy, fz) = (
            d(cp[2].x - cp[1].x),
            d(cp[2].y - cp[1].y),
            d(cp[2].z - cp[1].z),
        );
        let bx = qx - d(dnf * cp[1].x);
        let by = qy - d(dnf * cp[1].y);
        let bz = qz - d(dnf * cp[1].z);
        let c1 = (nx * (fz * by - fy * bz) + ny * (fx * bz - bx * fz)) + nz * (bx * fy - fx * by);
        // edge p0 → p1
        let (hx, hy, hz) = (
            d(cp[1].x - cp[0].x),
            d(cp[1].y - cp[0].y),
            d(cp[1].z - cp[0].z),
        );
        let cx = qx - d(dnf * cp[0].x);
        let cy = qy - d(dnf * cp[0].y);
        let cz = qz - d(dnf * cp[0].z);
        let c0 = (nx * (hz * cy - hy * cz) + ny * (hx * cz - cx * hz)) + nz * (cx * hy - hx * cy);
        let mx = max_idiom_d(max_idiom_d(c0, c1), c2);
        let x = sgn(mx as f32) & sgn((num * dnp - den * sd) as f32);
        feat = sel_feat(x, feat, (k as i32) << 2 | m as i32);
        den = sel(x, den, dnp);
        num = sel(x, num, sd);
    }

    let (pa, pb) = (&a.pt, &b.pt);
    for i in 0..3 {
        let i1 = INC_MOD3[i] as usize;
        let eax = d(pa[i1].x - pa[i].x);
        let eay = d(pa[i1].y - pa[i].y);
        let eaz = d(pa[i1].z - pa[i].z);
        let la = ((eay * eay + eax * eax) + eaz * eaz) * 1e-8;
        for j in 0..3 {
            let j1 = INC_MOD3[j] as usize;
            let ebx = d(pb[j1].x - pb[j].x);
            let eby = d(pb[j1].y - pb[j].y);
            let ebz = d(pb[j1].z - pb[j].z);
            let wx = d(pb[j].x - pa[i].x);
            let wy = d(pb[j].y - pa[i].y);
            let wz = d(pb[j].z - pa[i].z);
            let cx = ebz * eay - eby * eaz;
            let cy = ebx * eaz - eax * ebz;
            let cz = eax * eby - ebx * eay;
            let c2 = (cy * cy + cx * cx) + cz * cz;
            if lt_or_nan_d(c2, ((eby * eby + ebx * ebx) + ebz * ebz) * la) {
                continue;
            }
            let sd0 = (cy * wy + cx * wx) + cz * wz;
            let dn0 = (dir.x * (cx as f32) + dir.y * (cy as f32)) + dir.z * (cz as f32);
            let s = f64::from(sign_pm1(dn0));
            let dn = d(dn0) * s;
            let sd = sd0 * s;
            let sdf = sd as f32;
            let c2d = c2 * dn;
            let ux = wx * dn - d(dir.x * sdf);
            let uy = dn * wy - d(dir.y * sdf);
            let uz = dn * wz - d(dir.z * sdf);
            let p1 = ((ebx * uz - ux * ebz) * cy + (uy * ebz - uz * eby) * cx)
                + (ux * eby - ebx * uy) * cz;
            let p2 = ((eax * uz - ux * eaz) * cy + (uy * eaz - uz * eay) * cx)
                + (ux * eay - eax * uy) * cz;
            let in1 = sgn(((p1 * 2.0 - c2d).abs() - c2d) as f32);
            let in2 = sgn(((p2 * 2.0 - c2d).abs() - c2d) as f32);
            let x = sgn((num * dn - den * sd) as f32) & in2 & in1;
            feat = sel_feat(x, feat, ((i as i32 | 0x20) << 2) | j as i32);
            den = sel(x, den, dn);
            num = sel(x, num, sd);
        }
    }

    if feat == -1 || lt_or_nan_d(den.abs(), 1e-20) {
        return 0;
    }
    let hi = feat >> 2;
    if feat & 0x80 == 0 {
        let k = hi as usize;
        let m = (feat & 3) as usize;
        let t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
        out.t = t;
        let kt = (f64::from(hi) * t) as f32;
        let v = tris[k ^ 1].pt[m];
        out.pt = Vec3::new(dir.x * kt + v.x, dir.y * kt + v.y, dir.z * kt + v.z);
        let sf = (1 - 2 * hi) as f32;
        let fnrm = tris[k].n;
        out.n = Vec3::new(fnrm.x * sf, fnrm.y * sf, fnrm.z * sf);
        out.i_feature[k] = 0x40;
        out.i_feature[k ^ 1] = m as i32 | 0x80;
        return 1;
    }
    let i = (hi & 3) as usize;
    let j = (feat & 3) as usize;
    let i1 = INC_MOD3[i] as usize;
    let j1 = INC_MOD3[j] as usize;
    let eax = d(pa[i1].x - pa[i].x);
    let eay = d(pa[i1].y - pa[i].y);
    let eaz = d(pa[i1].z - pa[i].z);
    let ebxf = pb[j1].x - pb[j].x;
    let ebyf = pb[j1].y - pb[j].y;
    let ebzf = pb[j1].z - pb[j].z;
    let (ebx, eby, ebz) = (d(ebxf), d(ebyf), d(ebzf));
    let wx = d(pb[j].x - pa[i].x);
    let wy = d(pb[j].y - pa[i].y);
    let wz = d(pb[j].z - pa[i].z);
    let cx = ebz * eay - eby * eaz;
    let cy = ebx * eaz - eax * ebz;
    let cz = eax * eby - ebx * eay;
    let numf = num as f32;
    let ux = wx * den - d(dir.x * numf);
    let uy = den * wy - d(dir.y * numf);
    let uz = den * wz - d(dir.z * numf);
    let pr = ((uy * ebz - uz * eby) * cx + (ebx * uz - ux * ebz) * cy) + (ux * eby - ebx * uy) * cz;
    let c2 = (cx * cx + cy * cy) + cz * cz;
    // Unlike the other cells, `num` enters u as a float but `den` stays double here.
    let inv = 1.0 / (c2 * den);
    let k = inv * pr;
    let t = (inv * num) * c2;
    out.t = t;
    let tf = t as f32;
    out.pt = Vec3::new(
        (d(pa[i].x) + k * eax) as f32 + dir.x * tf,
        (d(pa[i].y) + k * eay) as f32 + dir.y * tf,
        (d(pa[i].z) + k * eaz) as f32 + dir.z * tf,
    );
    let nb = b.n;
    let dot = ((cy as f32) * (nb.z * ebxf - nb.x * ebzf)
        + (nb.y * ebzf - nb.z * ebyf) * (cx as f32))
        + (cz as f32) * (nb.x * ebyf - nb.y * ebxf);
    let s = f64::from(sign_pm1(dot));
    out.n = Vec3::new((cx * s) as f32, (cy * s) as f32, (cz * s) as f32);
    out.i_feature = [i as i32 | 0xA0, j as i32 | 0xA0];
    1
}
