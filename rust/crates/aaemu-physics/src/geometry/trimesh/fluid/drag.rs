//! The polygon drag core of slot 14 (`geometry.md` §6.4): `sub_35133E50`
//! PolygonDragIntegrate with its helpers `sub_35133970` ClipPolygonByPlane and `sub_35020EA0`
//! RotateAxisCosSin. Linear drag of the submerged surface moving into the medium; no density
//! and no `v²` (the caller, `ApplyBuoyancy` 0x350C0E50, scales by resistance and `dt`).
//!
//! Copied from P1a (`p7-phys-shapes`, `geometry/shapes/drag.rs`, where it was verified through
//! the box / cylinder / capsule slot 14) without the primitive drivers; the mesh feeds it
//! triangles (3-point polygons).

use crate::geometry::primitives::Plane;
use crate::math::Vec3;

/// Capacity of the clip buffers of [`polygon_drag_integrate`] (the stack arrays of
/// 0x35133E50 hold 32 points; the shapes pass at most 4).
pub const MAX_POLY: usize = 32;

/// `sub_35133970` ClipPolygonByPlane (inferred name): keeps the part of the closed polygon
/// `pts` with `n·p < d` (dot `(x·nx + y·ny) + z·nz`), writes it to `out`, returns its size.
///
/// The scan for the first kept vertex is unrolled by four in the binary; its first test of each
/// group is `!(dot >= d)` and the others are `dot < d`, so a NaN dot only starts the polygon at
/// a group's first vertex (kept as is). A vertex counts as kept when the sign bit of
/// `dot − d` is set (so `−0.0` counts); an edge is cut when `(dot_i − d)·(dot_next − d) < 0`
/// at `t = (d − dot_i)/(n·(p_next − p_i))`, point `p_i + (p_next − p_i)·t`.
pub fn clip_polygon_by_plane(pts: &[Vec3], n: Vec3, d: f32, out: &mut [Vec3]) -> usize {
    let np = pts.len();
    let dot = |p: Vec3| (p.y * n.y + p.x * n.x) + p.z * n.z;
    let mut i0 = 0usize;
    let mut v10 = 0usize;
    let mut found = false;
    if np >= 4 {
        loop {
            if !(dot(pts[v10]) >= d) {
                i0 = v10;
                found = true;
                break;
            }
            if dot(pts[v10 + 1]) < d {
                i0 = v10 + 1;
                found = true;
                break;
            }
            if dot(pts[v10 + 2]) < d {
                i0 = v10 + 2;
                found = true;
                break;
            }
            if dot(pts[v10 + 3]) < d {
                i0 = v10 + 3;
                found = true;
                break;
            }
            v10 += 4;
            if v10 + 3 >= np {
                break;
            }
        }
    }
    if !found {
        i0 = v10;
        while i0 < np {
            if dot(pts[i0]) < d {
                break;
            }
            i0 += 1;
        }
    }
    if i0 == np {
        return 0;
    }
    let mut cnt = 0usize;
    let mut i = i0;
    for _ in 0..np {
        let next = if i + 1 == np { 0 } else { i + 1 };
        let p = pts[i];
        let q = pts[next];
        out[cnt] = p;
        let di = dot(p) - d;
        cnt += (di.to_bits() >> 31) as usize;
        let dn = (q.x * n.x + q.y * n.y) + q.z * n.z - d;
        if di * dn < 0.0 {
            let dlt = q - p;
            let t = (d - dot(p)) / ((dlt.y * n.y + n.x * dlt.x) + dlt.z * n.z);
            out[cnt] = Vec3::new(p.x + dlt.x * t, p.y + dlt.y * t, p.z + dlt.z * t);
            cnt += 1;
        }
        i = next;
    }
    cnt
}

/// `sub_35020EA0` (inferred: rotate `v` about the unit `axis` by the angle with cosine `c`
/// and sine `s`, Rodrigues): `d = v·axis`, `par = axis·d`, `perp = v − par`,
/// `out = (perp·c + (axis × perp)·s) + par`.
#[must_use]
pub fn rotate_axis_cos_sin(v: Vec3, axis: Vec3, c: f32, s: f32) -> Vec3 {
    let a = axis;
    let d = (v.x * a.x + v.y * a.y) + v.z * a.z;
    let (py, px, pz) = (a.y * d, a.x * d, a.z * d);
    let (qy, qx, qz) = (v.y - a.y * d, v.x - px, v.z - a.z * d);
    let cx = a.y * qz - a.z * qy;
    let cy = a.z * qx - a.x * qz;
    let cz = a.x * qy - a.y * qx;
    Vec3::new(
        (qx * c + cx * s) + px,
        (qy * c + cy * s) + py,
        (qz * c + cz * s) + pz,
    )
}

/// `sub_35133E50` PolygonDragIntegrate (inferred name): the drag of one planar face.
///
/// 1. Clip `pts` (closed polygon) to the submerged side `plane.n·p < plane.n·origin`.
/// 2. Make the points relative to `com`.
/// 3. Clip by `(w × n_f)·r < n_f·v`, i.e. keep the part moving into the fluid
///    (`n_f·(v + w×r) > 0`).
/// 4. Rotate everything into the face frame: axis `n_f × z` (computed with explicit `·0.0`
///    products), `cos = n_f.z`, `sin = |n_f × z|`, axis `(1,0,0)` when `sin <= 0.001`.
/// 5. Integrate the linear pressure `p = v'z + w'x·y − w'y·x` over the 2-D polygon edge by
///    edge (Green's theorem, single precision, the binary's polynomial forms).
/// 6. Rotate the force `(0, 0, ∫p)` and the moment `(∫y·p, −∫x·p, 0)` back
///    (angle `−θ`) and **subtract** them from `dp` / `dl`.
#[allow(clippy::too_many_arguments)]
pub fn polygon_drag_integrate(
    pts: &[Vec3],
    nf: Vec3,
    plane: &Plane,
    v: Vec3,
    w: Vec3,
    com: Vec3,
    dp: &mut Vec3,
    dl: &mut Vec3,
) {
    let pn = plane.n;
    let po = plane.origin;
    let mut buf1 = [Vec3::ZERO; MAX_POLY];
    let d1 = (pn.y * po.y + pn.x * po.x) + pn.z * po.z;
    let n1 = clip_polygon_by_plane(pts, pn, d1, &mut buf1);
    for p in buf1.iter_mut().take(n1) {
        *p -= com;
    }
    let thr = (nf.y * v.y + nf.x * v.x) + nf.z * v.z;
    let m = Vec3::new(
        w.y * nf.z - w.z * nf.y,
        nf.x * w.z - w.x * nf.z,
        w.x * nf.y - nf.x * w.y,
    );
    let mut buf2 = [Vec3::ZERO; MAX_POLY + 1];
    let n2 = clip_polygon_by_plane(&buf1[..n1], m, thr, &mut buf2);
    // rotation into the face frame
    let z0 = nf.z * 0.0;
    let ax = nf.y - z0;
    let ay = z0 - nf.x;
    let az = nf.x * 0.0 - nf.y * 0.0;
    let sin = ((ax * ax + ay * ay) + az * az).sqrt();
    let axis = if sin <= 0.001 {
        Vec3::new(1.0, 0.0, 0.0)
    } else {
        let inv = 1.0 / sin;
        Vec3::new(ax * inv, ay * inv, az * inv)
    };
    let cos = nf.z;
    let vr = rotate_axis_cos_sin(v, axis, cos, sin);
    let wr = rotate_axis_cos_sin(w, axis, cos, sin);
    for p in buf2.iter_mut().take(n2) {
        *p = rotate_axis_cos_sin(*p, axis, cos, sin);
    }
    let mut area2 = 0.0f32; // v112
    let mut fz = 0.0f32; // v107
    let mut ly = 0.0f32; // v110
    let mut lx = 0.0f32; // v95
    if n2 > 0 {
        buf2[n2] = buf2[0];
        let h = wr.y * 0.5; // v61
        let t3 = wr.y * f32::from_bits(0x3EAA_AAAB); // v62
        let third = f32::from_bits(0x3EAA_AAAB);
        for i in 0..n2 {
            let (x, y) = (buf2[i].x, buf2[i].y);
            let (x1, y1) = (buf2[i + 1].x, buf2[i + 1].y);
            let xy = y * x; // v65
            let dy = y1 - y; // v67
            let yx1 = y * x1; // v68
            let dx = x1 - x; // v69
            area2 += y1 * x - yx1;
            let v70 = vr.z * dy;
            let v71 = (((dx * y + dy * x) * 0.5) + xy) + (dy * dx) * third;
            let v72 = ((dx + x) * x) + (dx * dx) * third;
            fz += ((wr.x * v71) - (h * v72)) * dy;
            let v111 = ly + (v70 * -0.5) * v72;
            let dx2 = dx * dx;
            let x2 = x * x;
            let v73 = ((((((dy * dx) * x) * 2.0 + dx2 * y) * third) + (dx2 * dy) * 0.25)
                + (((xy * dx) * 2.0 + x2 * dy) * 0.5))
                + x2 * y;
            let dy2 = dy * dy;
            let y2 = y * y;
            let qy = ((((((dy * dx) * y) * 2.0 + dy2 * x) * third) + (dy2 * dx) * 0.25)
                + (((xy * dy) * 2.0 + y2 * dx) * 0.5))
                + y2 * x;
            lx = (lx + v70 * v71) + ((qy * wr.x) - v73 * h) * dy;
            let cub = (((dx2 * dx) * 0.25 + dx2 * x) + ((dx * x) * x) * 1.5) + x2 * x;
            ly = v111 - ((v73 * (wr.x * 0.5)) - cub * t3) * dy;
        }
    }
    let ns = -sin; // v74
    let (a0, a1, a2) = (axis.x, axis.y, axis.z);
    // force (0, 0, f) rotated back
    let f = fz + (vr.z * area2) * 0.5;
    let k = ((a1 + a0) * 0.0) + a2 * f; // v76
    let v77 = -(a1 * k);
    let v78 = k * a0;
    let v108 = f - a2 * k;
    let v79 = v108 * a1 - v77 * a2;
    let fz_out = (v108 * cos + ((v77 * a0) - (a1 * -(k * a0))) * ns) + a2 * k;
    let fy_out = (v77 * cos + ((a2 * -(k * a0)) - (v108 * a0)) * ns) + a1 * k;
    let fx_out = ((-v78 * cos) + v79 * ns) + v78;
    dp.y -= fy_out;
    dp.x -= fx_out;
    dp.z -= fz_out;
    // moment (lx, ly, 0) rotated back
    let k2 = ((a1 * ly) + (a0 * lx)) + a2 * 0.0; // v85
    let v87 = ly - a1 * k2;
    let v88 = lx - k2 * a0;
    let v89 = -(a2 * k2);
    let mx = (v88 * cos + ((v89 * a1) - (v87 * a2)) * ns) + k2 * a0;
    let my = (v87 * cos + ((a2 * v88) - (v89 * a0)) * ns) + a1 * k2;
    let mz = (v89 * cos + ((v87 * a0) - (a1 * v88)) * ns) + a2 * k2;
    let nx = dl.x - mx;
    dl.y -= my;
    dl.x = nx;
    dl.z -= mz;
}
