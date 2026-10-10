//! Cylinder slot 14 (0x35146F40) and CircleClipByLine (0x351440E0).

use super::super::super::cylinder::CylinderData;
use super::super::super::gwd_point;
use super::super::rotate_axis_cos_sin;
use super::{side_quads, SideFrame};
use crate::geometry::primitives::Plane;
use crate::geometry::GeomWorldData;
use crate::math::{crt, max_idiom, min_idiom, sgnnz, Vec3};

/// A 2-D point of the cap polygons.
pub type P2 = (f32, f32);

/// Pseudo-angle of a 2-D vector used by [`circle_clip_by_line`] (monotonic in the polar
/// angle, homogeneous of degree 1): with `i = sign(|x| − |y|)`, `v` the larger-magnitude and
/// `o` the other coordinate, `k = 2·(((i ^ sy ^ sx) + (sy ^ sx)) + 2·sy)`, the result is
/// `k·|v| + ((1 − 2i)·o)·sgnnz(v)`; returns `(proxy, |v|)`.
fn pseudo_angle(x: f32, y: f32) -> (f32, f32) {
    let sx = x.to_bits() >> 31;
    let sy = y.to_bits() >> 31;
    let i = ((x.abs() - y.abs()).to_bits() >> 31) as usize;
    let c = [x, y];
    let v = c[i];
    let o = c[i ^ 1];
    let k = 2 * (((i as u32 ^ sy ^ sx) + (sy ^ sx)) + 2 * sy) as i32;
    let av = v.abs();
    let t = ((1 - 2 * i as i32) as f32 * o) * sgnnz(v) as f32;
    (k as f32 * av + t, av)
}

#[inline]
fn sign(x: f32) -> u32 {
    x.to_bits() >> 31
}

/// `sub_351440E0` CircleClipByLine (inferred name): clips a closed 2-D cap polygon whose edges
/// are straight segments (`flags[i] != 0`) or arcs of the circle of radius `r` around the
/// origin (`flags[i] == 0`) by the line through `p` with direction `m`, keeping the side
/// `(v − p) × m >= 0` (sign bit clear). `pts` holds `n + 1` points (closed). Output points carry
/// the flag of the edge that follows them (the clip line gets flag 1). Returns the count.
///
/// Circle/line intersections are `p + m·t/|m|²` with `t = −(p·m) ∓ √disc`,
/// `disc = (p·m)² − (|p|² − r²)·|m|²`; an arc edge is cut where the intersection's
/// pseudo-angle lies between its endpoints' (cross-multiplied comparisons, see
/// `pseudo_angle`); a straight edge where `|den − 2·s0| − |−den|` has its sign bit set
/// (`den = m.y·dx − dy·m.x`, `s0 = (y0 − p.y)·m.x − m.y·(x0 − p.x)`).
pub fn circle_clip_by_line(
    pts: &[P2],
    flags: &[i32],
    r: f32,
    p: P2,
    m: P2,
    out: &mut Vec<P2>,
    out_flags: &mut Vec<i32>,
) -> usize {
    out.clear();
    out_flags.clear();
    let n = flags.len();
    if n == 0 {
        return 0;
    }
    let r2 = r * r;
    let mm = m.0 * m.0 + m.1 * m.1;
    let pm = p.0 * m.0 + p.1 * m.1;
    let disc = pm * pm - ((p.0 * p.0 + p.1 * p.1) - r2) * mm;
    // candidates (num, den): 0/1 the circle intersections, 2 the straight-edge one
    let mut cand = [(0f32, 0f32); 3];
    let (mut av_i0, mut a_i0, mut av_i1, mut a_i1) = (0f32, 0f32, 0f32, 0f32);
    let have_circle = !(disc < 0.0);
    if have_circle {
        let sq = disc.sqrt();
        let t0 = -pm - sq;
        let bx = p.0 * mm;
        let by = mm * p.1;
        let x0 = bx + m.0 * t0;
        let y0 = by + m.1 * t0;
        (a_i0, av_i0) = pseudo_angle(x0, y0);
        let t1 = sq - pm;
        let x1 = bx + m.0 * t1;
        let y1 = by + m.1 * t1;
        (a_i1, av_i1) = pseudo_angle(x1, y1);
        cand[0] = (t0, mm);
        cand[1] = (t1, mm);
    }
    let side0 = (pts[0].1 - p.1) * m.0 - (pts[0].0 - p.0) * m.1;
    let mut side = 1 - sign(side0);
    for i in 0..n {
        let (x0, y0) = pts[i];
        let (x1, y1) = pts[i + 1];
        let mut start = 0usize;
        let mut end = 0usize;
        if flags[i] != 0 {
            let dy = y1 - y0;
            let ry = y0 - p.1;
            let dx = x1 - x0;
            let rx = x0 - p.0;
            let den = m.1 * dx - dy * m.0;
            let s0 = ry * m.0 - m.1 * rx;
            let test = (den - s0 * 2.0).abs() - (-den).abs();
            if sign(test) != 0 {
                cand[2] = (ry * dx - dy * rx, den);
                start = 2;
                end = 3;
            }
        } else if have_circle {
            let (a_v0, av_v0) = pseudo_angle(x0, y0);
            let (a_v1, av_v1) = pseudo_angle(x1, y1);
            let c1 = av_i0 * a_v0 - av_v0 * a_i0;
            let c2 = av_v1 * a_i0 - av_i0 * a_v1;
            let c3 = av_v0 * a_v1 - av_v1 * a_v0;
            let c4 = av_i1 * a_v0 - a_i1 * av_v0;
            let c5 = a_i1 * av_v1 - av_i1 * a_v1;
            start = ((sign(c2) + sign(c3) + sign(c1)).wrapping_sub(2) >> 31) as usize;
            end = ((1u32
                .wrapping_sub(sign(c3))
                .wrapping_sub(sign(c5))
                .wrapping_sub(sign(c4))
                >> 31)
                + 1) as usize;
        }
        if side != 0 {
            out.push((x0, y0));
            out_flags.push(flags[i]);
        }
        for &(num, den) in cand.iter().take(end).skip(start) {
            let inv = 1.0 / den;
            out.push((inv * (num * m.0) + p.0, (num * m.1) * inv + p.1));
            out_flags.push(flags[i] | side as i32);
            side ^= 1;
        }
    }
    out.len()
}

/// The inverse frame rotation of a point (`+sin`), returning its first two components as the
/// cap polygons use them.
fn to_cap_2d(fr: &SideFrame, sin: f32, q: Vec3) -> P2 {
    let a = fr.axis;
    let d = (a.x * q.x + q.y * a.y) + q.z * a.z;
    let pz = q.z - a.z * d;
    let py = q.y - a.y * d;
    let px = q.x - a.x * d;
    let y = ((py * fr.cos) + ((a.z * px) - (pz * a.x)) * sin) + a.y * d;
    let x = ((fr.cos * px) + sin * ((pz * a.y) - (py * a.z))) + a.x * d;
    (x, y)
}

/// Cylinder slot 14 (0x35146F40): **zeroes** `dP`/`dL`; the twelve side quads (as the
/// capsule, see `side_quads`); then both caps (first the `−axis` end, then the frame is
/// flipped — `A`, the frame axis and `cos` negated — and the centre moved by `2·hh·s`).
///
/// Per cap, in the cap frame: the velocity of the cap centre `u = v + w × (c − com)` (only its
/// normal component `u'z` is used), the angular velocity `w'`; the disk (two arc edges) is
/// clipped by the water line (where the water plane cuts the cap plane; if the planes are
/// parallel the whole disk counts when the centre is below the water, quirk: the test point
/// on the water plane is the projection of the **first** cap's centre for both caps) and by
/// the line where the face's normal speed changes sign ([`circle_clip_by_line`]); the
/// polygon/arc pressure integrals (Green's theorem, arc terms with MSVCR100 `asinf` and the
/// `±0.9999` clamps) give the force `(0, 0, F)` and moment `(Lx, Ly, 0)`, rotated back and
/// subtracted.
pub fn cylinder_medium_resistance(
    d: &CylinderData,
    plane: &Plane,
    gwd: &GeomWorldData,
    dp: &mut Vec3,
    dl: &mut Vec3,
) {
    let pr = &d.prim;
    *dp = Vec3::ZERO;
    *dl = Vec3::ZERO;
    let s = gwd.scale;
    let hh = s * pr.hh;
    let r = s * pr.r;
    let r2 = r * r;
    let mut a = gwd.r.mul_vec(Vec3::new(-pr.axis.x, -pr.axis.y, -pr.axis.z));
    let mut fr = SideFrame::new(a);
    let sin = -fr.nsin;
    let (n, o) = (plane.n, plane.origin);
    let c0 = gwd_point(gwd, pr.center);
    let mut c = Vec3::new(c0.x + a.x * hh, c0.y + a.y * hh, c0.z + a.z * hh);
    let hgt = ((o.y - c.y) * n.y + (o.x - c.x) * n.x) + (o.z - c.z) * n.z;
    let q = Vec3::new(n.x * hgt + c.x, n.y * hgt + c.y, n.z * hgt + c.z);
    let rinv = 1.0 / r;
    let h2 = hh * 2.0;
    let dd = Vec3::new(a.x * h2, h2 * a.y, h2 * a.z);
    side_quads(&fr, c, r, dd, plane, gwd, dp, dl);
    let (v, w, com) = (gwd.v, gwd.w, gwd.center_of_mass);
    let neg_r = -r;
    let third = f32::from_bits(0x3EAA_AAAB);
    let k9999 = f32::from_bits(0x3F7F_F972);
    let (mut buf_a, mut fl_a) = (Vec::with_capacity(16), Vec::with_capacity(16));
    let (mut buf_b, mut fl_b) = (Vec::with_capacity(16), Vec::with_capacity(16));
    for cap in 0..2 {
        let dc = c - com;
        let uz = ((dc.y * w.x) - dc.x * w.y) + v.z;
        let ux = ((dc.z * w.y) - (dc.y * w.z)) + v.x;
        let uy = ((dc.x * w.z) - (dc.z * w.x)) + v.y;
        let ax = fr.axis;
        let du = (ux * ax.x + uy * ax.y) + uz * ax.z;
        let v78 = du * ax.z;
        let v79 = ax.x * (uy - du * ax.y) - (ux - du * ax.x) * ax.y;
        let uz2 = (((uz - v78) * fr.cos) + v79 * sin) + v78;
        let wr = rotate_axis_cos_sin(w, ax, fr.cos, sin);
        // the full disk: two arcs (0, r) → (0, −r) → (0, r)
        buf_a.clear();
        fl_a.clear();
        buf_a.extend_from_slice(&[(0.0, r), (0.0, neg_r), (0.0, r)]);
        fl_a.extend_from_slice(&[0, 0]);
        let na = (a.y * n.y + a.x * n.x) + a.z * n.z;
        let lv = Vec3::new(n.x * na - a.x, n.y * na - a.y, n.z * na - a.z);
        let mut cnt;
        // `cur` = the buffer holding the current polygon after the water-line step
        let mut cur_is_b;
        if (lv.x * lv.x + lv.y * lv.y) + lv.z * lv.z > 0.001 {
            let nr = rotate_axis_cos_sin(n, ax, fr.cos, sin);
            let m2 = (-nr.y, nr.x);
            let t = (((c.x - q.x) * a.x + (c.y - q.y) * a.y) + (c.z - q.z) * a.z)
                / ((lv.x * a.x + lv.y * a.y) + lv.z * a.z);
            let pw = Vec3::new(
                (t * lv.x + q.x) - c.x,
                (t * lv.y + q.y) - c.y,
                (t * lv.z + q.z) - c.z,
            );
            let p2 = to_cap_2d(&fr, sin, pw);
            cnt = circle_clip_by_line(&buf_a, &fl_a, r, p2, m2, &mut buf_b, &mut fl_b);
            cur_is_b = true;
            if cnt > 0 {
                let f0 = buf_b[0];
                buf_b.push(f0);
            }
        } else {
            // swap: the full disk becomes the "second" buffer
            std::mem::swap(&mut buf_a, &mut buf_b);
            std::mem::swap(&mut fl_a, &mut fl_b);
            cur_is_b = true;
            let lhs = (c.y * n.y + c.x * n.x) + c.z * n.z;
            let rhs = (q.x * n.x + q.y * n.y) + q.z * n.z;
            if lhs <= rhs {
                cnt = 2;
                let f0 = buf_b[0];
                buf_b.truncate(2);
                buf_b.push(f0);
            } else {
                cnt = 0;
            }
        }
        // second clip: the line where the normal speed changes sign
        let mx = w.y * a.z - w.z * a.y;
        let wxaz = w.x * a.z;
        let my = w.z * a.x - wxaz;
        let wxay = a.y * w.x;
        let mz = wxay - w.y * a.x;
        let m2l = (my * my + mx * mx) + mz * mz;
        let wl = (wr.y * wr.y + wr.x * wr.x) + wr.z * wr.z;
        let mut skip_close = false;
        if m2l <= wl * 0.001 {
            // the binary swaps its buffer pointers here; the polygon itself stays put
            let lhs = (c.x * (w.y * a.z - w.z * a.y) + (w.z * a.x - wxaz) * c.y)
                + (wxay - w.y * a.x) * c.z;
            let rhs = (a.x * v.x + a.y * v.y) + a.z * v.z;
            if lhs > rhs {
                cnt = 0;
                skip_close = true;
            }
        } else {
            let sv =
                (((a.x * v.x) + a.y * v.y) + a.z * v.z - ((c.x * mx + my * c.y) + mz * c.z)) / m2l;
            let px = sv * mx;
            let py = my * sv;
            let pz = mz * sv;
            let dd2 = (ax.x * px + py * ax.y) + pz * ax.z;
            let qx = px - ax.x * dd2;
            let qy = py - ax.y * dd2;
            let qz = pz - ax.z * dd2;
            let x2 = ((qx * fr.cos) + ((qz * ax.y) - (qy * ax.z)) * sin) + ax.x * dd2;
            let y2 = ((qy * fr.cos) + ((ax.z * qx) - (qz * ax.x)) * sin) + ax.y * dd2;
            let (src, sfl, dst, dfl) = if cur_is_b {
                (&buf_b, &fl_b, &mut buf_a, &mut fl_a)
            } else {
                (&buf_a, &fl_a, &mut buf_b, &mut fl_b)
            };
            let src_pts: Vec<P2> = src.iter().copied().take(cnt + 1).collect();
            let src_fl: Vec<i32> = sfl.iter().copied().take(cnt).collect();
            cnt = if cnt == 0 {
                dst.clear();
                dfl.clear();
                0
            } else {
                circle_clip_by_line(&src_pts, &src_fl, r, (x2, y2), (wr.x, wr.y), dst, dfl)
            };
            cur_is_b = !cur_is_b;
        }
        let (poly, pfl) = if cur_is_b {
            (&mut buf_b, &mut fl_b)
        } else {
            (&mut buf_a, &mut fl_a)
        };
        if !skip_close && cnt > 0 {
            poly.truncate(cnt);
            let f0 = poly[0];
            poly.push(f0);
        }
        // integrate
        let (mut s_area, mut s_f, mut s_ly, mut s_lx) = (0f32, 0f32, 0f32, 0f32);
        if cnt > 0 {
            let h_u = uz2 * 0.5;
            let wy3 = wr.y * third;
            let wy2 = wr.y * 0.5;
            let wx2 = wr.x * 0.5;
            for k in 0..cnt {
                let (x0, y0) = poly[k];
                let (x1, y1) = poly[k + 1];
                let (ar, i1, i2, i3, i4, i5);
                if pfl[k] != 0 {
                    let dy = y1 - y0;
                    let dx = x1 - x0;
                    let cr = x0 * y1 - y0 * x1;
                    let v138 = dy * dx;
                    ar = cr * 0.5;
                    let xy = x0 * y0;
                    let x02 = x0 * x0;
                    let y02 = y0 * y0;
                    let dx2 = dx * dx;
                    let dy2 = dy * dy;
                    i1 = ((((dy * x0) + (dx * y0)) * 0.5 + xy) + v138 * third) * dy;
                    i2 = (((dx + x0) * x0) + dx2 * third) * dy;
                    i3 = ((((((v138 * x0) * 2.0) + dx2 * y0) * third) + (dx2 * dy) * 0.25)
                        + (((xy * dx) * 2.0) + x02 * dy) * 0.5
                        + x02 * y0)
                        * dy;
                    i4 = ((((((v138 * y0) * 2.0) + dy2 * x0) * third) + (dy2 * dx) * 0.25)
                        + (((xy * dy) * 2.0) + y02 * dx) * 0.5
                        + y02 * x0)
                        * dy;
                    i5 =
                        ((((dx2 * dx) * 0.25 + dx2 * x0) + ((dx * x0) * x0) * 1.5) + x02 * x0) * dy;
                } else {
                    let cl = |y: f32| {
                        let m = max_idiom(min_idiom(y * rinv, k9999), -k9999);
                        max_idiom(min_idiom(m, 1.0), -1.0)
                    };
                    let b1 = cl(y1);
                    let b0 = cl(y0);
                    let a1 = crt::asinf(b1);
                    let a0 = crt::asinf(b0);
                    let v158 = (a1 - a0) * r2;
                    let v159 = (x1 * x1) * x1;
                    let v160 = sgnnz(y1 - y0) as f32;
                    ar = (v158 * 0.5) * v160;
                    let v161 = (x0 * x0) * x0;
                    i1 = (v159 - v161) * f32::from_bits(0xBEAA_AAAB);
                    let v162 = (((y1 * y1) * y1) - ((y0 * y0) * y0)) * third;
                    i2 = ((y1 - y0) * r2) - v162;
                    i3 = ((((y1 * y1) - (y0 * y0)) * r2) * 0.5) - v162;
                    let v163 = ((y1 * x1) - (x0 * y0)) + v160 * v158;
                    let v164 = (v159 * y1) - (v161 * y0);
                    i5 = (((r2 * 1.5) * v163) + v164) * 0.25;
                    i4 = ((r2 * 0.125) * v163) - (v164 * 0.25);
                }
                s_area += ar;
                s_f += (wr.x * i1) - (i2 * wy2);
                s_lx = (s_lx + uz2 * i1) + ((wr.x * i4) - (i3 * wy2));
                s_ly = (s_ly - i2 * h_u) - ((i3 * wx2) - (i5 * wy3));
            }
        }
        // rotate back (−sin) and subtract
        let (a0, a1, a2) = (fr.axis.x, fr.axis.y, fr.axis.z);
        let ns = fr.nsin;
        let cs = fr.cos;
        let f = s_f + uz2 * s_area;
        let k = ((a0 + a1) * 0.0) + a2 * f;
        let v170 = a2 * k;
        let v171 = f - a2 * k;
        let v172 = -(a0 * k);
        let v173 = -(a1 * k);
        let v174 = (v171 * a1) - (v173 * a2);
        let v175 = (a2 * v172) - (v171 * a0);
        let v176 = (v171 * cs) + ((v173 * a0) - (a1 * v172)) * ns;
        let v177 = (v173 * cs) + v175 * ns;
        let fx = dp.x - (((v172 * cs) + v174 * ns) + a0 * k);
        dp.y -= v177 + a1 * k;
        dp.x = fx;
        dp.z -= v176 + v170;
        let k2 = ((a0 * s_lx) + (a1 * s_ly)) + a2 * 0.0;
        let v185 = a2 * k2;
        let v186 = a0 * k2;
        let v187 = a1 * k2;
        let v188 = s_lx - v186;
        let v189 = -(a2 * k2);
        let v192 = s_ly - a1 * k2;
        let v193 = v192 * cs;
        let v194 = (v189 * a1) - (v192 * a2);
        let v195 = (a2 * v188) - (v189 * a0);
        let v196 = a1 * v188;
        let v197 = v188 * cs;
        let v198 = (v189 * cs) + ((v192 * a0) - v196) * ns;
        dl.y -= (v193 + v195 * ns) + v187;
        dl.x -= (v197 + v194 * ns) + v186;
        dl.z -= v198 + v185;
        if cap == 0 {
            a = Vec3::new(-a.x, -a.y, -a.z);
            fr.axis = Vec3::new(-fr.axis.x, -fr.axis.y, -fr.axis.z);
            fr.cos = -fr.cos;
            c = Vec3::new(c.x + h2 * a.x, c.y + a.y * h2, c.z + h2 * a.z);
        }
    }
}
