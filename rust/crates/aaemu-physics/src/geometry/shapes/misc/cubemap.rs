//! Slot 51: rasterising a primitive into the occlusion cubemap (the leaf worker of slot 23
//! `DrawToOcclusionCubemap`), and the polygon rasteriser `sub_35131AB0` all three primitive
//! classes end in.
//!
//! ## Arguments (from slot 23 / `sub_3514DCF0`)
//!
//! Slot 51 is called as `(gwd, iStartPrim, nPrims, iPass, cubemap[6], res, rmin, rmax,
//! zscale)`; the primitives ignore the prim range. The cubemap is centred on the world origin
//! (slot 23 moves the viewer into the geometry frame and back), each face is `res × res`
//! 32-bit words, and slot 23 passes `zscale = 65535/rmax`. A pixel holds the quantised
//! distance along the face axis `round(clamp(d)·zscale)` in its low 31 bits; bit 31 is a flag
//! about `rmin` (see [`rasterize_polygon`]).
//!
//! ## Face and pixel numbering
//!
//! Face `2a + s` looks along axis `a` (`x, y, z`), `s = 1` towards `+a`. On face `(a, ·)` the
//! point `p` projects to `u = p[inc a]/|p[a]|`, `v = p[dec a]/|p[a]|` (`inc = {1,2,0}`,
//! `dec = {2,0,1}`; the divisor is `|p[a]|`, so the image is mirrored on the negative faces).
//! Pixel `(col, row) = (round((u + k)·res/2), round((v + k)·res/2))` with
//! `k = 1 − 1/res` (`v` rounded with `+0.5` for the lowest row), clamped to `[0, res−1]`,
//! index `row·res + col`.
//!
//! ## The three primitives
//!
//! * box (0x3513EEF0): the six faces of the world-space box, each a 4-point polygon;
//! * cylinder and capsule (0x35144AA0): a temporary `CBoxGeom` built from the tree's bounding
//!   box (`(r, r, hh)` or `(r, r, r + hh)` around the axis), so the box code draws the bounding
//!   box;
//! * sphere (0x351B0600): one 8-point "octagon" of radius `r·s` around the world centre `c`,
//!   in the plane through `c` perpendicular to `c` (the silhouette disc seen from the origin,
//!   approximated without the perspective correction).
//!
//! All three return 1.

use crate::geometry::primitives::BoxPrim;
use crate::geometry::shapes::cylinder::CylinderData;
use crate::geometry::shapes::{gwd_point, orthogonal, SphereGeom};
use crate::geometry::GeomWorldData;
use crate::math::{Mat33, Vec3};

/// The six faces of an occlusion cubemap, `res × res` words each (see the module docs).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Cubemap {
    pub res: i32,
    pub faces: [Vec<u32>; 6],
}

impl Cubemap {
    /// A cubemap with every pixel set to `fill` (slot 23 clears to `0x7FFFFFFF`).
    #[must_use]
    pub fn new(res: i32, fill: u32) -> Self {
        let n = (res.max(0) as usize).pow(2);
        Self {
            res,
            faces: std::array::from_fn(|_| vec![fill; n]),
        }
    }
}

/// 0x35209A10 / 0x35209A20 (the fourth entries are never used here).
const INC: [usize; 3] = [1, 2, 0];
const DEC: [usize; 3] = [2, 0, 1];
/// `12582912.0`: `round(x) = bits(x + C) − 0x4B400000` for `|x| < 2²²`.
const ROUND_C: f32 = 12_582_912.0;
const ROUND_BIAS: i32 = 0x4B40_0000;

#[inline]
fn bits_i(x: f32) -> i32 {
    x.to_bits() as i32
}
#[inline]
fn signbit(x: f32) -> u32 {
    x.to_bits() >> 31
}
/// `((bits >> 30) & ~1) + 1`: −1 for a set sign bit, else +1.
#[inline]
fn sgnnz_bits(x: f32) -> i32 {
    ((bits_i(x) >> 30) & !1) + 1
}
/// `round(x)` of an already biased value (`x + 12582912`).
#[inline]
fn unbias(x: f32) -> i32 {
    bits_i(x).wrapping_sub(ROUND_BIAS)
}
/// The branch-free clamp `min(max(q, 0), res − 1)` of the binary (wrapping arithmetic).
#[inline]
fn clamp_idx(q: i32, res: i32) -> i32 {
    let nq = q.wrapping_neg();
    let m = ((nq >> 31) & nq).wrapping_neg();
    let e = (res.wrapping_sub(1)).wrapping_sub(m);
    ((e >> 31) & e).wrapping_add(m)
}
/// The `!((c ^ (c − 1)) >> 31)` "is non-zero" idiom (also 0 for `i32::MIN`).
#[inline]
fn nonzero(c: i32) -> i32 {
    ((c.wrapping_sub(1) ^ c) >> 31).wrapping_neg() ^ 1
}
/// The dominant axis of `p` as the binary picks it (`|x| < |y|` by sign bit, then against
/// `|z|`) and the face `2a | (p[a] has a clear sign bit)`.
#[inline]
fn face_of(p: [f32; 3]) -> (usize, usize) {
    let s1 = signbit(p[0].abs() - p[1].abs());
    let s2 = signbit(p[s1 as usize].abs() - p[2].abs());
    let c = (s2 << 1) | s1;
    let ax = (c & (((c ^ 2) | 4) >> 1)) as usize;
    (ax, (ax << 1) | (signbit(p[ax]) ^ 1) as usize)
}
/// Cube-face corner `k` (mod 4) in face coordinates.
#[inline]
fn corner(k: i32) -> (f32, f32) {
    (
        (1 - ((k.wrapping_mul(-2) ^ k) & 2)) as f32,
        (1 - (k & 2)) as f32,
    )
}

/// One face record of the rasteriser's stack frame (272 bytes each in the binary at
/// `rbp+0x20+272f`): entry / exit corner, the face's nearest depth, the vertex count and a
/// 32-entry ring of projected vertices.
#[derive(Clone, Copy)]
struct FaceRec {
    entry: i32,
    exit: i32,
    zmin: f32,
    count: i32,
    pts: [(f32, f32); 32],
}

impl FaceRec {
    fn push(&mut self, p: (f32, f32)) {
        self.pts[(self.count & 31) as usize] = p;
        self.count = self.count.wrapping_add(1);
    }
}

/// `sub_35131AB0`: rasterises the planar convex polygon `pts` (world frame, relative to the
/// cubemap centre) into `cube`. Returns nothing (the binary's return value is a leftover).
///
/// 1. **Culling.** `n = (p1 − p0) × (p2 − p0)`; the polygon is drawn only if
///    `!signbit(n·p0) == pass` (pass 1: faces whose plane faces away from the origin).
/// 2. **Nearest depth per face.** Face `2a` starts at `rmax` and takes
///    `min(max(−p[a], 0))` over the vertices, face `2a+1` `min(max(p[a], 0))` (the `min`/`max`
///    idioms).
/// 3. **Clipping to the cube.** Each vertex is projected on its own face (`1e-5` guard: below
///    it `10000` is added to the divisor). An edge whose ends lie on different faces is walked
///    across the faces: the edge plane `p × q` is intersected with the face borders, the exit
///    point is added to the current face and the entry point to the next; a face entered while
///    it already has an exit corner gets the cube corners in between (direction by face sign
///    and `pass`). At most 6 face steps per edge and 8 border retries per step; a face ring
///    over 32 entries after an entry point is reset to 0. If exactly four faces around one axis
///    are hit, the cap face on the polygon's side of that axis is filled with its corners.
/// 4. **Scan conversion** of each face with 1..31 vertices, top (`max v`) to bottom, one row
///    per pixel, two active edges with incremental edge functions; the depth of a pixel is
///    `|d/(n·dir)|` (`d = n·p0`; 0 when `n·dir` is 0 or NaN), clamped to `[zmin(face), rmax]`,
///    times `zscale`, rounded. With `q` that value and `flag = q < round(rmin·zscale)`: the
///    pixel keeps the smaller of `q | flag` and its own low 31 bits, and bit 31 becomes
///    `bit31 | flag` (pass 0) or `bit31 & !flag` (pass 1).
/// 5. If no pixel was written at all, the pixel of `p0` alone is updated with depth
///    `min(|p0[a]|, rmax)` (pass 0 as above; pass 1 only clears bit 31 unless `q > rmin`).
///
/// Quirks kept: `pass` values other than 0/1 never draw (step 1 compares with 0/1); the
/// edge-advance loop of step 4 counts a 32-bit distance down to 0, so on degenerate input
/// (non-planar or 1e-6-sized point sets) it can spin 2³² times, and the span loop can
/// even cycle forever — both are reproduced (the oracle fuzz abandons the original after a
/// timeout and skips those cases).
///
/// Deviations (degenerate input only): uninitialised entry corners read as 0 here (the binary
/// reads stale stack words — only reachable when a walk is cut short by the step limits);
/// pixel writes outside the face are dropped instead of corrupting memory (the original then
/// writes out of bounds or faults; the fuzz skips those cases, 2 in 9000 degenerate ones).
/// The ring-overflow branches (a face reset after more than 32 entries, faces with 32+
/// entries skipped) are ported but no fuzz input reached them.
///
/// # Panics
///
/// If `pts` has fewer than 3 points (the binary reads past the array).
#[allow(clippy::too_many_lines, clippy::needless_range_loop)]
pub fn rasterize_polygon(
    pts: &[Vec3],
    pass: u32,
    cube: &mut Cubemap,
    rmin: f32,
    rmax: f32,
    zscale: f32,
) {
    assert!(pts.len() >= 3, "rasterize_polygon: needs 3 points");
    let res = cube.res;
    let np = pts.len() as i32;
    let p = |i: usize| -> [f32; 3] { pts[i].to_array() };
    let (p0, p1, p2) = (pts[0], pts[1], pts[2]);
    let (d0x, d0y, d0z) = (p1.x - p0.x, p1.y - p0.y, p1.z - p0.z);
    let (d1x, d1y, d1z) = (p2.x - p0.x, p2.y - p0.y, p2.z - p0.z);
    let nrm = [
        d0y * d1z - d0z * d1y,
        d1x * d0z - d0x * d1z,
        d0x * d1y - d1x * d0y,
    ];
    let dot = (p0.y * nrm[1] + nrm[0] * p0.x) + p0.z * nrm[2];
    if (signbit(dot) ^ 1) ^ pass != 0 {
        return;
    }
    let d35 = (nrm[0] * p0.x + nrm[1] * p0.y) + nrm[2] * p0.z;
    let resf = res as f32;
    let half = resf * 0.5;
    let inv2 = 2.0 / resf;
    let k274 = 1.0 - inv2 * 0.5;
    let q_rmin = unbias(rmin * zscale + ROUND_C);

    let mut rec = [FaceRec {
        entry: 0,
        exit: -1,
        zmin: rmax,
        count: 0,
        pts: [(0.0, 0.0); 32],
    }; 6];

    // 2. nearest depth per face
    for a in 0..3 {
        let (mut lo, mut hi) = (rec[2 * a].zmin, rec[2 * a + 1].zmin);
        for i in 0..pts.len() {
            let x = p(i)[a];
            let neg = (x.abs() + -x) * 0.5;
            lo = ((neg + lo) - (lo - neg).abs()) * 0.5;
            let pos = ((-x).abs() + x) * 0.5;
            hi = ((pos + hi) - (hi - pos).abs()) * 0.5;
        }
        rec[2 * a].zmin = lo;
        rec[2 * a + 1].zmin = hi;
    }

    // 3. clipping
    let pass_i = pass as i32;
    for i in 0..pts.len() {
        let pp = p(i);
        let j = if (i as i32 + 1) < np { i + 1 } else { 0 };
        let qq = p(j);
        let (ax, fp) = face_of(pp);
        let (_, fq) = face_of(qq);
        let en = [
            pp[1] * qq[2] - pp[2] * qq[1],
            pp[2] * qq[0] - pp[0] * qq[2],
            pp[0] * qq[1] - pp[1] * qq[0],
        ];
        let mut inv = [0f32; 3];
        let pa = pp[ax].abs();
        let k = 1.0 / ((signbit(pa - 1e-5) as i32 as f32) * 10000.0 + pa);
        rec[fp].push((k * pp[INC[ax]], k * pp[DEC[ax]]));
        if fp == fq {
            continue;
        }
        let mut cur = fp;
        let mut mask = 0u32;
        let mut iter = 0;
        loop {
            if iter >= 6 {
                break;
            }
            mask |= 1 << cur;
            let sgn = 2 * (cur as i32 & 1) - 1;
            let a = cur >> 1;
            let t = [INC[a], DEC[a]];
            let sgnf = sgn as f32;
            let v102 = -(sgnf * en[a]);
            let (n1, n2) = (en[t[0]], en[t[1]]);
            let s = [sgnnz_bits(n2) * sgn, -(sgnnz_bits(n1) * sgn)];
            let mut fl = [0f32; 4];
            fl[0] = s[0] as f32;
            fl[1] = v102 - n1 * fl[0];
            fl[3] = s[1] as f32;
            fl[2] = v102 - n2 * fl[3];
            let mut sel = signbit(((-n1 + n1) - fl[2] * 2.0).abs() - (-n1 - n1).abs()) as usize;
            let mut tries = 0;
            let mut nf;
            loop {
                let o = t[sel ^ 1];
                if inv[o] == 0.0 || inv[o].is_nan() {
                    let x = en[o];
                    inv[o] = 1.0 / ((signbit(x.abs() - 1e-5) as i32 as f32) * 10000.0 + x);
                }
                nf = ((2 * t[sel]) as i32 | ((s[sel] + 1) >> 1)) as usize;
                if nf == fq || mask & (1 << nf) == 0 {
                    break;
                }
                tries += 1;
                if tries >= 8 {
                    break;
                }
                sel ^= 1;
            }
            let oi = (sel ^ 1) + 2 * sel;
            fl[oi] *= inv[t[sel ^ 1]];
            rec[cur].push((fl[2 * sel], fl[2 * sel + 1]));
            rec[cur].exit = sel as i32 - s[sel] + 1;
            let entry_c = (sel ^ 1) as i32 - sgn + 1;
            if rec[nf].exit < 0 {
                rec[nf].entry = entry_c;
            } else {
                let step =
                    (2 * (nf as i32 & 1) - 1).wrapping_mul(pass_i.wrapping_mul(2).wrapping_sub(1));
                let off = step >> 31;
                let mut e = rec[nf].exit;
                if e != entry_c {
                    loop {
                        let kk = off.wrapping_add(e);
                        e = e.wrapping_add(step) & 3;
                        rec[nf].push(corner(kk));
                        if e == entry_c {
                            break;
                        }
                    }
                }
                rec[nf].exit = -1;
            }
            fl[3 * sel] = fl[oi];
            fl[oi] = sgnf;
            rec[nf].push((fl[2 * sel], fl[2 * sel + 1]));
            if rec[nf].count > 32 {
                rec[nf].count = 0;
            }
            iter += 1;
            cur = nf;
            if cur == fq {
                break;
            }
        }
    }

    // the four-faces-around-an-axis case
    let (mut even, mut odd, mut mask, mut rem) = (0i32, 0i32, 0i32, 6i32);
    for a in 0..3i32 {
        for (f, cnt) in [(2 * a, &mut even), (2 * a + 1, &mut odd)] {
            let flag = nonzero(rec[f as usize].count);
            *cnt += flag;
            let bit = flag << a;
            let newly = !mask & bit;
            mask |= bit;
            rem -= (newly.wrapping_neg() >> 31) & (a + 1);
        }
    }
    if even + odd == 4 {
        let r = rem.wrapping_sub(1);
        if (r as u32) <= 2 {
            let face = ((signbit(nrm[r as usize]) ^ pass) | (2 * r) as u32) as usize;
            let step =
                (2 * (face as i32 & 1) - 1).wrapping_mul(pass_i.wrapping_mul(2).wrapping_sub(1));
            let off = step >> 31;
            let mut k = 0i32;
            while rec[face].count < 4 {
                let kk = off.wrapping_add(k);
                k = k.wrapping_add(step) & 3;
                rec[face].push(corner(kk));
            }
        }
    }

    // 4. scan conversion
    let v150 = pass_i.wrapping_mul(2).wrapping_sub(1);
    let mut drawn = 0u32;
    for f in 0..6usize {
        let sf = 2 * (f as i32 & 1) - 1;
        let step = v150.wrapping_mul(sf);
        let off = step >> 31;
        {
            let r = &mut rec[f];
            let mut e = r.exit;
            if e >= 0 && e != r.entry {
                loop {
                    let kk = off.wrapping_add(e);
                    e = e.wrapping_add(step) & 3;
                    r.push(corner(kk));
                    if e == r.entry {
                        break;
                    }
                }
            }
        }
        let r = &rec[f];
        let cnt = r.count;
        if cnt == 0 || cnt >= 32 {
            continue;
        }
        let ax = f >> 1;
        let (t1, t2) = (INC[ax], DEC[ax]);
        let v164 = nrm[t1] * inv2;
        let u = |k: i32| r.pts[k as usize].0;
        let v = |k: i32| r.pts[k as usize].1;
        let (mut hi, mut lo) = (0i32, 0i32);
        for k in 1..cnt {
            let vk = v(k);
            if signbit(v(hi) - vk) != 0 {
                hi = k;
            }
            if signbit(vk - v(lo)) != 0 {
                lo = k;
            }
        }
        let mut lr = [hi, hi];
        let ymin = clamp_idx(unbias(((k274 + v(lo)) * half + 0.5) + ROUND_C), res);
        let mut x0 = clamp_idx(unbias((k274 + u(hi)) * half + ROUND_C), res);
        let mut ytop = clamp_idx(unbias((k274 + v(hi)) * half + ROUND_C), res);
        let v305 = sf as f32 * nrm[ax];
        let face_px = &mut cube.faces[f];
        let next = |k: i32, d: i32| -> i32 {
            let mut x = k.wrapping_add(d);
            x = x.wrapping_add((x >> 31) & cnt);
            x & (x.wrapping_sub(cnt) >> 31)
        };
        loop {
            let (a, b) = (lr[0], lr[1]);
            let na = next(a, step);
            let nb = next(b, step.wrapping_neg());
            let nab = [na, nb];
            let ua = u(na);
            let va = v(na);
            let side = signbit(va - v(nb)) as usize;
            let v183 = v(nb) - v(b);
            let v272 = ua - u(a);
            let v187 = v272 * (k274 + v(a));
            let v188 = k274 + v(b);
            let v189 = va - v(a);
            let v191 = (v187 - (k274 + u(a)) * v189) * half;
            let v275 = u(nb) - u(b);
            let v192 = (k274 + u(b)) * v183;
            let v196 = (v275 * v188 - v192) * half;
            let sx = sgnnz_bits(v272);
            let x1min = clamp_idx(
                unbias((((u(a) + ua) - (u(a) - ua).abs()) * 0.5 + k274) * half + ROUND_C),
                res,
            );
            let x2max = clamp_idx(
                unbias(((((u(b) - u(nb)).abs() + (u(b) + u(nb))) * 0.5 + k274) * half) + ROUND_C),
                res,
            );
            let dt = ytop.wrapping_sub(ymin);
            ytop = ytop.wrapping_sub((dt >> 31) & dt);
            let qb = unbias((k274 + v(nab[side])) * half + ROUND_C);
            let d = ymin.wrapping_sub(qb);
            let r8 = ymin.wrapping_sub((d >> 31) & d);
            let e = res.wrapping_sub(1).wrapping_sub(r8);
            let ybot = ((e >> 31) & e).wrapping_add(r8);
            let sxf = sx as f32;
            let v302 = v191 * sxf;
            let v303 = sx >> 31;
            let lim = [x1min, x2max][((sx + 1) >> 1) as usize].wrapping_add(v303);
            let mut rowbase = ytop.wrapping_mul(res);
            loop {
                let mut r9 = lim.wrapping_sub(x0);
                let mut xs = x0;
                let yf = ytop as f32;
                let ey = v272 * yf;
                let mut e1 = (ey - (xs as f32) * v189) * sxf - v302;
                if signbit(e1) != 0 && nonzero(r9) != 0 {
                    loop {
                        xs = xs.wrapping_add(sx);
                        r9 = r9.wrapping_sub(sx);
                        e1 = (ey - (xs as f32) * v189) * sxf - v302;
                        if !(signbit(e1) != 0 && nonzero(r9) != 0) {
                            break;
                        }
                    }
                }
                let mut xs = xs.wrapping_sub(v303);
                let v210 = v275 * yf;
                let mut pix = rowbase.wrapping_add(xs);
                let mut cl = xs.wrapping_sub(x2max).wrapping_sub(1);
                x0 = xs;
                let mut den =
                    ((yf * inv2 - k274) * nrm[t2] + ((xs as f32) * inv2 - k274) * nrm[t1]) + v305;
                let mut e2 = (v210 - (xs as f32) * v183) - v196;
                if (cl & bits_i(e2)) < 0 {
                    let zmin = r.zmin;
                    loop {
                        let z = if den == 0.0 || den.is_nan() {
                            0.0
                        } else {
                            d35 / den
                        };
                        drawn += 1;
                        den += v164;
                        let az = z.abs();
                        let w = ((az + rmax) - (az - rmax).abs()) * 0.5;
                        let q = unbias((((zmin - w).abs() + (zmin + w)) * 0.5) * zscale + ROUND_C);
                        if let Some(px) = usize::try_from(pix).ok().and_then(|i| face_px.get_mut(i))
                        {
                            write_pixel(px, q, q_rmin, pass);
                        }
                        xs = xs.wrapping_add(1);
                        cl = cl.wrapping_add(1);
                        pix = pix.wrapping_add(1);
                        e2 = (v210 - (xs as f32) * v183) - v196;
                        if (cl & bits_i(e2)) >= 0 {
                            break;
                        }
                    }
                }
                rowbase = rowbase.wrapping_sub(res);
                ytop = ytop.wrapping_sub(1);
                if ytop < ybot {
                    break;
                }
            }
            lr[side] = nab[side];
            let si = side as i32;
            x0 = (x0 & si.wrapping_neg()) | ((si - 1) & x1min);
            if ybot > ymin {
                continue;
            }
            break;
        }
    }

    // 5. nothing drawn: the pixel of p0
    if drawn == 0 {
        let pp = p(0);
        let (_, face) = face_of(pp);
        let a = face >> 1;
        let pa = pp[a].abs();
        let k = 1.0 / pa;
        let row = clamp_idx(unbias((k * pp[DEC[a]] + k274) * half + ROUND_C), res);
        let col = clamp_idx(unbias((k * pp[INC[a]] + k274) * half + ROUND_C), res);
        let idx = row.wrapping_mul(res).wrapping_add(col);
        let q = unbias((((pa + rmax) - (pa - rmax).abs()) * 0.5) * zscale + ROUND_C);
        if let Some(px) = usize::try_from(idx)
            .ok()
            .and_then(|i| cube.faces[face].get_mut(i))
        {
            if pass == 0 {
                write_pixel(px, q, q_rmin, 0);
            } else {
                *px &= (q_rmin.wrapping_sub(q) >> 31) as u32 | 0x7FFF_FFFF;
            }
        }
    }
}

/// The per-pixel update of [`rasterize_polygon`] step 4.
#[inline]
fn write_pixel(px: &mut u32, q: i32, q_rmin: i32, pass: u32) {
    let flag = q.wrapping_sub(q_rmin) >> 31;
    let new = (q | flag) as u32 & 0x7FFF_FFFF;
    let old = *px & 0x7FFF_FFFF;
    let chosen = if new >= old { old } else { new };
    let sign = if pass == 0 {
        (*px | flag as u32) & 0x8000_0000
    } else {
        (*px & !(flag as u32)) & 0x8000_0000
    };
    *px = chosen | sign;
}

/// Box slot 51 (0x3513EEF0) on the box primitive `prim` (the cylinder passes its tree box):
/// world centre `c = (R·center)·s + offset`, half-sizes `size·s`, rotation
/// `M = R·Basisᵀ` (`sub_35022380`) when `bOriented`, else `R`. Face `i` (axis `a = i/2`, sign
/// of `i & 1`) is the polygon `M·p + c` over the corners
/// `p[inc a], p[dec a] = (+,σ), (−,σ), (−,−σ), (+,−σ)` (`σ` = the face sign; `−` written as
/// `h·(−1.0)`), `p[a] = σ·h[a]`. Returns 1.
#[allow(clippy::neg_multiply)] // `h·(−1.0)` is a `mulss`: a NaN keeps its sign, unlike `−h`
pub fn box_prim_draw_to_cubemap(
    prim: &BoxPrim,
    gwd: &GeomWorldData,
    pass: u32,
    cube: &mut Cubemap,
    rmin: f32,
    rmax: f32,
    zscale: f32,
) -> i32 {
    let s = gwd.scale;
    let h = [prim.size.x * s, prim.size.y * s, prim.size.z * s];
    let c = gwd_point(gwd, prim.center);
    let m: Mat33 = if prim.oriented != 0 {
        gwd.r.mul_mat(&prim.basis.transpose())
    } else {
        gwd.r
    };
    for i in 0..6i32 {
        let a = (i >> 1) as usize;
        let (t1, t2) = (INC[a], DEC[a]);
        let (h1, h2) = (h[t1], h[t2]);
        let ha = (2 * (i & 1) - 1) as f32 * h[a];
        let sa = (((2 * i) & 2) - 1) as f32;
        let sb = ((!(2 * i) & 2) - 1) as f32;
        let corners = [
            (h1, sa * h2),
            (h1 * -1.0, sa * h2),
            (h1 * -1.0, sb * h2),
            (h1, sb * h2),
        ];
        let mut poly = [Vec3::ZERO; 4];
        for (k, &(c1, c2)) in corners.iter().enumerate() {
            let mut q = [0f32; 3];
            q[t1] = c1;
            q[t2] = c2;
            q[a] = ha;
            poly[k] = m.mul_vec(Vec3::from_array(q)) + c;
        }
        rasterize_polygon(&poly, pass, cube, rmin, rmax, zscale);
    }
    1
}

/// Box slot 51 (0x3513EEF0), see [`box_prim_draw_to_cubemap`].
pub fn box_draw_to_cubemap(
    g: &crate::geometry::shapes::BoxGeom,
    gwd: &GeomWorldData,
    pass: u32,
    cube: &mut Cubemap,
    rmin: f32,
    rmax: f32,
    zscale: f32,
) -> i32 {
    box_prim_draw_to_cubemap(&g.prim, gwd, pass, cube, rmin, rmax, zscale)
}

/// Cylinder / capsule slot 51 (0x35144AA0): a temporary `CBoxGeom` whose primitive is the
/// tree's box (basis, `bOriented`, centre and size copied verbatim), drawn by the box code.
pub fn cylinder_draw_to_cubemap(
    d: &CylinderData,
    gwd: &GeomWorldData,
    pass: u32,
    cube: &mut Cubemap,
    rmin: f32,
    rmax: f32,
    zscale: f32,
) -> i32 {
    box_prim_draw_to_cubemap(&d.bv.box_, gwd, pass, cube, rmin, rmax, zscale)
}

/// Sphere slot 51 (0x351B0600): world centre `c = (R·center)·s + offset`, radius `a = s·r`;
/// `e = normalise(orthogonal(c))`, `f = normalise(e × c)` (both `(0,0,1)` when `|·|² ≤ 1e-20`);
/// 8 points `pt_k = ((e·a_k + c) + f·b_k)` with `(a_0, b_0) = (s·r, 0)` and the 45° step
/// `a' = (float)((double)(a − b)·0.7071067811865475)`,
/// `b' = (float)((double)b·1.4142135623730951 + (double)a')`. Returns 1.
pub fn sphere_draw_to_cubemap(
    g: &SphereGeom,
    gwd: &GeomWorldData,
    pass: u32,
    cube: &mut Cubemap,
    rmin: f32,
    rmax: f32,
    zscale: f32,
) -> i32 {
    let c = gwd_point(gwd, g.prim.center);
    let mut a = gwd.scale * g.prim.r;
    let mut b = 0f32;
    let guard = |v: Vec3| -> Vec3 {
        let l2 = (v.y * v.y + v.x * v.x) + v.z * v.z;
        if l2 > 1e-20 {
            let inv = 1.0 / l2.sqrt();
            Vec3::new(v.x * inv, v.y * inv, v.z * inv)
        } else {
            Vec3::Z
        }
    };
    let e = guard(orthogonal(c));
    let f = guard(e.cross(c));
    let k1 = f64::from_bits(0x3FE6_A09E_667F_3BCC);
    let k2 = f64::from_bits(0x3FF6_A09E_667F_3BCD);
    let mut poly = [Vec3::ZERO; 8];
    for p in &mut poly {
        *p = Vec3::new(
            (e.x * a + c.x) + f.x * b,
            (e.y * a + c.y) + f.y * b,
            (e.z * a + c.z) + f.z * b,
        );
        let an = (f64::from(a - b) * k1) as f32;
        b = (f64::from(b) * k2 + f64::from(an)) as f32;
        a = an;
    }
    rasterize_polygon(&poly, pass, cube, rmin, rmax, zscale);
    1
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn clamp_and_round() {
        assert_eq!(clamp_idx(-5, 8), 0);
        assert_eq!(clamp_idx(3, 8), 3);
        assert_eq!(clamp_idx(99, 8), 7);
        assert_eq!(unbias(2.4 + ROUND_C), 2);
        assert_eq!(unbias(-2.6 + ROUND_C), -3);
        assert_eq!(nonzero(0), 0);
        assert_eq!(nonzero(-3), 1);
        assert_eq!(nonzero(i32::MIN), 0);
    }

    #[test]
    fn faces() {
        assert_eq!(face_of([1.0, 0.5, 0.2]), (0, 1));
        assert_eq!(face_of([-1.0, 0.5, 0.2]), (0, 0));
        assert_eq!(face_of([0.1, -2.0, 0.2]), (1, 2));
        assert_eq!(face_of([0.1, 0.5, 3.0]), (2, 5));
        assert_eq!(face_of([0.0, 0.0, -0.0]), (0, 1)); // ties go to x
    }

    #[test]
    fn a_wall_in_front_fills_the_face() {
        // a big quad at x = 2 facing the origin covers the whole +x face (pass 0)
        let mut cube = Cubemap::new(4, 0x7FFF_FFFF);
        let pts = [
            Vec3::new(2.0, -50.0, -50.0),
            Vec3::new(2.0, -50.0, 50.0),
            Vec3::new(2.0, 50.0, 50.0),
            Vec3::new(2.0, 50.0, -50.0),
        ];
        let rmax = 10.0;
        rasterize_polygon(&pts, 0, &mut cube, 0.0, rmax, 65535.0 / rmax);
        let mut other = cube.clone();
        other.faces[1] = vec![0x7FFF_FFFF; 16];
        let drew_x = cube.faces[1].iter().filter(|&&p| p != 0x7FFF_FFFF).count();
        assert!(drew_x > 0);
        // the centre pixels are at distance ~2 along x
        let q = cube.faces[1][5] & 0x7FFF_FFFF;
        assert!((q as f32 / 6553.5 - 2.0).abs() < 0.01, "{q}");
    }
}
