//! tri–box linear unprojection (x64 0x3516FDB0, x86 0x35177810).

use super::{d, sel, sel_feat, sgn, sign_pm1};
use crate::collision::contact::{UnprojParams, UnprojResult};
use crate::collision::swept::common::{lt_or_nan_d, ucomisd_eq, DEC_MOD3, INC_MOD3};
use crate::geometry::primitives::{BoxPrim, Triangle};
use crate::math::Vec3;

/// sub_3516FDB0: move triangle A along `dir` until it stops intersecting box B.
///
/// Everything is done in the box frame: the triangle vertices, its stored normal and `dir` are
/// rotated by the box basis (`R·(p − c)`, rows of `R` = box axes; `oriented` is not read) in
/// single precision and then used as doubles. The best `t` is a rational `num/den` (doubles,
/// starting at `0/1` unless the face test hits); a candidate replaces it when
/// `(float)(num·den_c − den·num_c)` has its sign bit set (the candidate is larger).
///
/// 1. **Triangle face – box vertex**: the box corner `h = −sign(n_l)·size` is moved along `dir`
///    onto the triangle plane and tested against the three edges (all sign bits clear); hit if
///    also `(float)(0·dn − sd) < 0`. Internal feature 0.
/// 2. **Box faces – triangle vertices**: for each axis `k`, the triangle vertex deepest along
///    `−sign(dir_k)` (min idiom, ties → lower index); the time it reaches the face `size_k` and
///    the check that it is inside the face rectangle there. Internal feature `0x40 | m<<2 | k`.
/// 3. **Box edges – triangle edges**: triangle edge `i` against the box edge parallel to axis
///    `j` (skipped if `|e × axis|² < |e|²·1e-8` or NaN), the edge chosen by the signs of
///    `c = axis_j × e` (times the sign of `c·dir`); hit if the meeting point is strictly inside
///    both edges. Internal feature `0x80 | i<<4 | j<<2 | edge bits`.
///
/// Output (internal −1 → return 0, nothing written): face → `t = num/den` (0 if `den` is 0/NaN),
/// `pt = c + Rᵀ·h` (double), `n = tri.n`, features 0x40 / box-vertex id (`(1−sx)>>1 |
/// (1−sy) | (2−2sz)`); box face → `pt = p_m + dir·t`, `n = −sign(dir_k)·axis_k`, features
/// `0x80|m` / `0x40 | 2k | (s+1)>>1`; edge–edge → `t = (num/(|c|²·den))·|c|²`, `pt` = meeting
/// point on the world triangle edge (float) `+ dir·t`, `n = ±Rᵀ·c` (sign of `c·(p_i −
/// p_{i−1})`, not normalised), features `0xA0|i` / `0x20 | j<<2 | edge bits`.
/// The input feature ids, `tmin`, `tmax` and the area are not used.
pub fn unproject_tri_box(
    p: &mut UnprojParams,
    a: &Triangle,
    _fa: i32,
    b: &BoxPrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let r = &b.basis.m;
    let bc = b.center;
    let size = [b.size.x, b.size.y, b.size.z];
    let dir = p.dir;
    // R·v with the binary's association ((v.x·R[k][0] + v.y·R[k][1]) + v.z·R[k][2])
    let rot = |v: [f32; 3]| -> [f64; 3] {
        let mut o = [0.0f64; 3];
        for (k, ok) in o.iter_mut().enumerate() {
            *ok = d((v[0] * r[k][0] + v[1] * r[k][1]) + v[2] * r[k][2]);
        }
        o
    };
    let vl: [[f64; 3]; 3] = [0, 1, 2].map(|i| {
        let q = a.pt[i];
        rot([q.x - bc.x, q.y - bc.y, q.z - bc.z])
    });
    let nl = rot([a.n.x, a.n.y, a.n.z]);
    let dl = rot([dir.x, dir.y, dir.z]);
    let sdir = [0, 1, 2].map(|k| sign_pm1(dl[k] as f32));
    let snrm = [0, 1, 2].map(|k| sign_pm1(nl[k] as f32));
    let hf = [0, 1, 2].map(|k| -((snrm[k] as f32) * size[k]));
    let h = hf.map(d);

    // 1. triangle face vs the deepest box vertex
    let sd0 = ((vl[0][1] - h[1]) * nl[1] + (vl[0][0] - h[0]) * nl[0]) + (vl[0][2] - h[2]) * nl[2];
    let dn0 = -((dl[1] * nl[1] + dl[0] * nl[0]) + dl[2] * nl[2]);
    let s = f64::from(sign_pm1(dn0 as f32));
    let sd = sd0 * s;
    let dn = dn0 * s;
    let q = [0, 1, 2].map(|k| h[k] * dn - dl[k] * sd);
    let side = |i: usize, j: usize| -> u32 {
        let e = [0, 1, 2].map(|k| vl[j][k] - vl[i][k]);
        let w = [0, 1, 2].map(|k| q[k] - vl[i][k] * dn);
        let x = e[1] * w[2] - e[2] * w[1];
        let y = w[0] * e[2] - e[0] * w[2];
        let z = e[0] * w[1] - w[0] * e[1];
        (((y * nl[1] + x * nl[0]) + z * nl[2]) as f32).to_bits()
    };
    let c0 = side(0, 1);
    let c1 = side(1, 2);
    let c2 = side(2, 0);
    let hit = (!(c1 | c2 | c0) >> 31) & sgn((dn * 0.0 - sd) as f32);
    let mut feat = hit as i32 - 1;
    let mut den = f64::from(hit) * dn + f64::from(hit ^ 1);
    let mut num = f64::from(hit) * sd + f64::from(hit ^ 1) * 0.0;

    // 2. box faces vs the deepest triangle vertex
    for k in 0..3 {
        let sk = f64::from(sdir[k]);
        let a1 = sk * vl[1][k];
        let a0 = sk * vl[0][k];
        let a2 = sk * vl[2][k];
        let diff = a1 - a0;
        let lo = ((a1 + a0) - diff.abs()) * 0.5;
        let cm = sgn((a2 - lo) as f32);
        let m = ((sgn(diff as f32) & (cm ^ 1)) | (cm << 1)) as usize;
        let dnk = sk * dl[k];
        let sdk = d(size[k]) - sk * vl[m][k];
        let k1 = INC_MOD3[k] as usize;
        let k2 = DEC_MOD3[k] as usize;
        let in_a = sgn(((dnk * vl[m][k1] + sdk * dl[k1]).abs() - d(size[k1]) * dnk) as f32);
        let in_b = sgn(((dnk * vl[m][k2] + sdk * dl[k2]).abs() - d(size[k2]) * dnk) as f32);
        let x = sgn((num * dnk - den * sdk) as f32) & in_b & in_a;
        feat = sel_feat(x, feat, ((m as i32 | 0x10) << 2) | k as i32);
        den = sel(x, den, dnk);
        num = sel(x, num, sdk);
    }

    // 3. box edges vs triangle edges
    let cross_axis = |j: usize, e: [f64; 3]| -> [f64; 3] {
        let mut c = [0.0f64; 3];
        c[INC_MOD3[j] as usize] = e[DEC_MOD3[j] as usize];
        c[DEC_MOD3[j] as usize] = -e[INC_MOD3[j] as usize];
        c
    };
    for i in 0..3 {
        let i1 = INC_MOD3[i] as usize;
        let e = [0, 1, 2].map(|k| vl[i1][k] - vl[i][k]);
        let l = ((e[0] * e[0] + e[1] * e[1]) + e[2] * e[2]) * 1e-8;
        for j in 0..3 {
            let j1 = INC_MOD3[j] as usize;
            let j2 = DEC_MOD3[j] as usize;
            let c = cross_axis(j, e);
            let c2 = (c[0] * c[0] + c[1] * c[1]) + c[2] * c[2];
            if lt_or_nan_d(c2, l) {
                continue;
            }
            let dn0 = (c[1] * dl[1] + c[0] * dl[0]) + c[2] * dl[2];
            let s = sign_pm1(dn0 as f32);
            let s1 = sign_pm1(c[j1] as f32) * s;
            let s2 = sign_pm1(c[j2] as f32) * s;
            let mut bv = [0.0f64; 3];
            bv[j] = d(-size[j]);
            bv[j1] = d((s1 as f32) * size[j1]);
            bv[j2] = d((s2 as f32) * size[j2]);
            let w = [0, 1, 2].map(|k| bv[k] - vl[i][k]);
            let sd = ((w[0] * c[0] + w[1] * c[1]) + w[2] * c[2]) * f64::from(s);
            let dn = dn0 * f64::from(s);
            let u = [0, 1, 2].map(|k| w[k] * dn - dl[k] * sd);
            let c2d = c2 * dn;
            let f = cross_axis(j, u);
            let p1 = (c[0] * f[0] + f[1] * c[1]) + f[2] * c[2];
            // Oddity kept: this test subtracts |c2d| while the triangle-edge test below (and the
            // other cells) subtract c2d itself; dn ≥ 0 after the sign fold, so they only differ
            // for dn = −0 / NaN.
            let in1 = sgn(((p1 * 2.0 - c2d).abs() - c2d.abs()) as f32);
            let p2 = (c[0] * (u[1] * e[2] - u[2] * e[1]) + c[1] * (e[0] * u[2] - e[2] * u[0]))
                + c[2] * (e[1] * u[0] - e[0] * u[1]);
            let szc = d(size[j]) * c2d;
            let in2 = sgn(((p2 - szc).abs() - szc) as f32);
            let x = sgn((num * dn - den * sd) as f32) & in2 & in1;
            let fid = ((i as i32 | 8) << 4) | ((j as i32) << 2) | ((s1 + 1) >> 1) | (s2 + 1);
            feat = sel_feat(x, feat, fid);
            den = sel(x, den, dn);
            num = sel(x, num, sd);
        }
    }

    if feat == -1 {
        return 0;
    }
    match feat & 0xC0 {
        0 => {
            out.t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
            let wc = |k: usize| (d(r[0][k]) * h[0] + d(r[1][k]) * h[1]) + d(r[2][k]) * h[2];
            out.pt = Vec3::new(
                (d(bc.x) + wc(0)) as f32,
                (d(bc.y) + wc(1)) as f32,
                (d(bc.z) + wc(2)) as f32,
            );
            out.n = a.n;
            out.i_feature = [
                0x40,
                (2 - 2 * snrm[2]) | ((1 - snrm[0]) >> 1) | (1 - snrm[1]),
            ];
        }
        0x40 => {
            let k = (feat & 3) as usize;
            let m = ((feat >> 2) & 3) as usize;
            let t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
            out.t = t;
            let tf = t as f32;
            let v = a.pt[m];
            out.pt = Vec3::new(dir.x * tf + v.x, dir.y * tf + v.y, dir.z * tf + v.z);
            let sk = sdir[k];
            let ns = (-sk) as f32;
            out.n = Vec3::new(r[k][0] * ns, r[k][1] * ns, r[k][2] * ns);
            out.i_feature = [m as i32 | 0x80, ((k as i32 | 0x20) * 2) | ((sk + 1) >> 1)];
        }
        _ => {
            let jj = ((feat >> 2) & 3) as usize;
            let ii = ((feat >> 4) & 3) as usize;
            let i1 = INC_MOD3[ii] as usize;
            let j1 = INC_MOD3[jj] as usize;
            let j2 = DEC_MOD3[jj] as usize;
            let e = [0, 1, 2].map(|k| vl[i1][k] - vl[ii][k]);
            let c = cross_axis(jj, e);
            let dn0 = (c[1] * dl[1] + c[0] * dl[0]) + c[2] * dl[2];
            let s = sign_pm1(dn0 as f32);
            let mut bv = [0.0f64; 3];
            bv[jj] = d(-size[jj]);
            bv[j1] = d(((sign_pm1(c[j1] as f32) * s) as f32) * size[j1]);
            bv[j2] = d(((sign_pm1(c[j2] as f32) * s) as f32) * size[j2]);
            let u = [0, 1, 2].map(|k| (bv[k] - vl[ii][k]) * den - dl[k] * num);
            let f = cross_axis(jj, u);
            let pr = (c[0] * f[0] + f[1] * c[1]) + f[2] * c[2];
            let c2 = (c[0] * c[0] + c[1] * c[1]) + c[2] * c[2];
            // No zero check (den was accepted as a blend result; 0 → inf/NaN like the binary).
            let inv = 1.0 / (c2 * den);
            let t = (inv * num) * c2;
            out.t = t;
            let tf = t as f32;
            // Oddity kept: the time is solved in the box frame (double) but the contact point
            // is rebuilt in float from the world-space triangle edge.
            let prf = pr as f32;
            let invf = inv as f32;
            let pw = &a.pt;
            out.pt = Vec3::new(
                ((pw[i1].x - pw[ii].x) * prf) * invf + pw[ii].x + dir.x * tf,
                ((pw[i1].y - pw[ii].y) * prf) * invf + pw[ii].y + dir.y * tf,
                ((pw[i1].z - pw[ii].z) * prf) * invf + pw[ii].z + dir.z * tf,
            );
            let i2 = DEC_MOD3[ii] as usize;
            let dot = ((vl[ii][0] - vl[i2][0]) * c[0] + (vl[ii][1] - vl[i2][1]) * c[1])
                + (vl[ii][2] - vl[i2][2]) * c[2];
            let sn = f64::from(sign_pm1(dot as f32));
            let wn = |k: usize| ((d(r[1][k]) * c[1] + d(r[0][k]) * c[0]) + d(r[2][k]) * c[2]) * sn;
            out.n = Vec3::new(wn(0) as f32, wn(1) as f32, wn(2) as f32);
            out.i_feature = [ii as i32 | 0xA0, (feat & 0xF) | 0x20];
        }
    }
    1
}
