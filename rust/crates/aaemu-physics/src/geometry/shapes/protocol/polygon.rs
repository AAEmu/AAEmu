//! Slots 58 and 59, the feature-polygon helpers of the contact-area code (inferred roles):
//! slot 58 returns the polygon of a face feature, slot 59 the segment of an edge feature, both
//! in the gtest frame and projected onto the in-plane axes of a [`ContactFrame`]. When the
//! frame's normal is all zero (or NaN: `ucomiss` + `jne`), slot 58 first derives the frame
//! from the face itself.
//!
//! Signature in the binary: `int f(this, float frame[12], int iPrim, int iFeature, gtest*,
//! Vec2** pts, int** idsA, int** idsB)`, returning the number of points; the outputs live in
//! per-caller arena arrays (see [`FeaturePolygon`]).

use super::gtest::{ContactFrame, FeaturePolygon, GeomUnderTest};
use crate::geometry::shapes::cylinder::CylinderData;
use crate::geometry::shapes::{normalize_safe, orthogonal, BoxGeom, DEC_MOD3, INC_MOD3};
use crate::math::{crt, isneg, sgnnz, Mat33, Vec3};

/// Column `c` of `m` (a box axis in the gtest frame).
#[inline]
fn col(m: &Mat33, c: usize) -> Vec3 {
    Vec3::new(m.m[0][c], m.m[1][c], m.m[2][c])
}

/// `(p·ax, p·ay)` with the slots' association `(p.y·a.y + a.x·p.x) + p.z·a.z`.
#[inline]
fn project(frame: &ContactFrame, p: Vec3) -> [f32; 2] {
    [frame.axes[0].dot(p), frame.axes[1].dot(p)]
}

/// The box axes in the gtest frame as matrix columns: `R·Basisᵀ` (`sub_35022380`) when
/// oriented, else `R`.
fn box_axes(g: &BoxGeom, gt: &GeomUnderTest) -> Mat33 {
    if g.prim.oriented != 0 {
        gt.r.mul_mat(&g.prim.basis.transpose())
    } else {
        gt.r
    }
}

fn set_len<T: Default + Clone>(v: &mut Vec<T>, n: usize) {
    v.clear();
    v.resize(n, T::default());
}

/// Box slot 58 (0x3513C1C0): the four vertices of face `iFeature = 0x40 | i<<1 | s`.
///
/// Face centre `fc = ((R·c)·sc + o) + axis_i·(sign·size_i·sc)`. Zero frame normal: the frame
/// becomes `n = axis_i·sign`, `origin = fc`, axes `(axis_j, axis_k·sign)` and the winding
/// code `w = (2s − 1) & 3`; otherwise `w = −isneg(axis_i·n) & 3`. Vertex `m` (0..3) is
/// `axes·e + (fc − origin)` with `e_j = ±size_j·sc`, `e_k = ±size_k·sc` in the order
/// `(+,+), (−,+), (−,−), (+,−)`, stored at index `m ^ w` with vertex id
/// `s<<i | (m>>1 & 1)<<k | ((m ^ m>>1) & 1)<<j` and, at `((m ^ w) − (w & 1)) & 3`, the id of
/// the edge leaving it `0x20 | e<<2 | s<<(m&1) | (((!m)>>1 ^ m) & 1)<<((!m)&1)` with
/// `e = j` for even `m`, `k` for odd. Point 4 repeats point 0. Returns 4.
///
/// Axis index 3 (codes `0x46/0x47`) makes the binary read stack garbage; the port treats the
/// missing size as 0 (not covered by the oracle tests).
pub fn box_face_polygon(
    g: &BoxGeom,
    frame: &mut ContactFrame,
    i_feature: i32,
    gt: &GeomUnderTest,
    out: &mut FeaturePolygon,
) -> i32 {
    let b = &g.prim;
    let m = box_axes(g, gt);
    let sc = gt.scale;
    let sz = [b.size.x * sc, b.size.y * sc, b.size.z * sc, 0.0];
    let i = ((i_feature >> 1) & 3) as usize;
    let bit = i_feature & 1;
    let ii = i.min(2);
    let (j, k) = (INC_MOD3[ii], DEC_MOD3[ii]);
    let sign = (2 * bit - 1) as f32;
    let ai = col(&m, ii);
    let aj = col(&m, j);
    let ak = col(&m, k);
    let v42 = sign * sz[i];
    let rc = gt.r.mul_vec(b.center);
    let fc = Vec3::new(
        (rc.x * sc + gt.offset.x) + ai.x * v42,
        (rc.y * sc + gt.offset.y) + ai.y * v42,
        (rc.z * sc + gt.offset.z) + ai.z * v42,
    );
    let l2 = frame.n.len2();
    let w: i32 = if l2 == 0.0 || l2.is_nan() {
        frame.n = Vec3::new(ai.x * sign, ai.y * sign, ai.z * sign);
        frame.axes[0] = aj;
        frame.axes[1] = Vec3::new(ak.x * sign, ak.y * sign, ak.z * sign);
        frame.origin = fc;
        (2 * bit - 1) & 3
    } else {
        (-(isneg(ai.dot(frame.n)) as i32)) & 3
    };
    let d = fc - frame.origin;
    set_len(&mut out.pts, 5);
    set_len(&mut out.ids_a, 4);
    set_len(&mut out.ids_b, 4);
    out.ids_shared = false;
    let edge_axis = [j as i32, k as i32];
    for mi in 0..4i32 {
        let mut e = [0f32; 3];
        e[j] = (1 - ((mi ^ (2 * mi)) & 2)) as f32 * sz[j];
        e[k] = (1 - (mi & 2)) as f32 * sz[k];
        e[ii] = 0.0;
        let p = m.mul_vec(Vec3::new(e[0], e[1], e[2])) + d;
        let idx = (mi ^ w) as usize;
        out.pts[idx] = project(frame, p);
        let nm = !mi;
        out.ids_b[(((mi ^ w) - (w & 1)) & 3) as usize] = ((edge_axis[(mi & 1) as usize] | 8) << 2)
            | (bit << (mi & 1))
            | ((((nm >> 1) ^ mi) & 1) << (nm & 1));
        out.ids_a[idx] = (bit << i) | (((mi >> 1) & 1) << k) | ((((mi >> 1) ^ mi) & 1) << j);
    }
    out.pts[4] = out.pts[0];
    4
}

/// Box slot 59 (0x3513C990): the two ends of edge `iFeature = 0x20 | a<<2 | b1 b0`.
///
/// `e_j = (2·b0 − 1)·size_j`, `e_k = (b1·2 − 1)·size_k`, `e_a = −size_a` (unscaled), first end
/// `p0 = (R·c + axes·e)·sc + o − origin`, second end `p0 + (axes·(2·size_a along a))·sc`; both
/// projected. Ids: `idsB = [f, f]`, `idsA = [b0<<j | b1<<k, idsA[0] | 1<<a]`. Point 2 repeats
/// point 0. The frame is not modified. Returns 2.
pub fn box_edge_segment(
    g: &BoxGeom,
    frame: &ContactFrame,
    i_feature: i32,
    gt: &GeomUnderTest,
    out: &mut FeaturePolygon,
) -> i32 {
    let b = &g.prim;
    let m = box_axes(g, gt);
    let a = ((i_feature >> 2) & 3) as usize;
    let aa = a.min(2);
    let (j, k) = (INC_MOD3[aa], DEC_MOD3[aa]);
    let size = |x: usize| -> f32 {
        if x < 3 {
            b.size[x]
        } else {
            0.0
        }
    };
    let mut e = [0f32; 3];
    e[j] = (2 * (i_feature & 1) - 1) as f32 * size(j);
    e[k] = ((i_feature & 2) - 1) as f32 * size(k);
    e[aa] = -size(a);
    let ev = Vec3::new(e[0], e[1], e[2]);
    let rc = gt.r.mul_vec(b.center);
    let me = m.mul_vec(ev);
    let sc = gt.scale;
    let p0 = Vec3::new(
        ((rc.x + me.x) * sc + gt.offset.x) - frame.origin.x,
        ((rc.y + me.y) * sc + gt.offset.y) - frame.origin.y,
        ((rc.z + me.z) * sc + gt.offset.z) - frame.origin.z,
    );
    let mut e2 = [0f32; 3];
    e2[aa] = size(a) * 2.0;
    let me2 = m.mul_vec(Vec3::new(e2[0], e2[1], e2[2]));
    let p1 = Vec3::new(me2.x * sc + p0.x, me2.y * sc + p0.y, me2.z * sc + p0.z);
    set_len(&mut out.pts, 3);
    set_len(&mut out.ids_a, 2);
    set_len(&mut out.ids_b, 2);
    out.ids_shared = false;
    out.pts[0] = project(frame, p0);
    out.pts[1] = project(frame, p1);
    out.pts[2] = out.pts[0];
    out.ids_b[0] = i_feature;
    out.ids_b[1] = i_feature;
    let id0 = ((i_feature & 1) << j) | (((i_feature >> 1) & 1) << k);
    out.ids_a[0] = id0;
    out.ids_a[1] = (1 << a) | id0;
    2
}

/// Cylinder slot 58 (0x351455A0, also the capsule's): the `nTess`-gon of a cap.
///
/// `A = R·axis`, `e0 = normalize_safe(orthogonal(A), +z)`, `e1 = A × e0`; cap sign
/// `σ = 2·((f & 3) − (f >> 6)) − 1` (`0x41` bottom, `0x42` top); cap centre
/// `cc = ((R·c)·sc + A·(sc·hh·σ)) + o`. Zero frame normal: frame = `(A·σ, cc, e0, e1·σ)`.
/// Angle step `(float)((double)sgnnz(A·n) · (2π / (double)nTess))`, accumulated in single
/// precision from 0; point `m` is `((e0·rs)·cos + (cc − origin)) + (e1·rs)·sin` (rs = r·sc,
/// MSVCR100 `sinf`/`cosf`), every id `((2·v) | 0x40) >> 1` with `v = (f & 3) − (f >> 6)`.
/// Point `n` repeats point 0. Returns `nTess`.
///
/// Quirk kept in spirit: the binary also copies `ids[0]` into `ids[2]` (instead of `ids[n]`);
/// all ids are equal, so this only matters for `nTess < 3`, where the binary leaves arena
/// garbage at index 1 and the port writes nothing beyond `n`.
pub fn cylinder_face_polygon(
    d: &CylinderData,
    frame: &mut ContactFrame,
    i_feature: i32,
    gt: &GeomUnderTest,
    out: &mut FeaturePolygon,
) -> i32 {
    let p = &d.prim;
    let a = gt.r.mul_vec(p.axis);
    let e0 = normalize_safe(orthogonal(a), Vec3::Z);
    let e1 = Vec3::new(
        e0.z * a.y - e0.y * a.z,
        a.z * e0.x - e0.z * a.x,
        e0.y * a.x - a.y * e0.x,
    );
    let sc = gt.scale;
    let v = (i_feature & 3).wrapping_sub(i_feature >> 6);
    let sigma = 2i32.wrapping_mul(v).wrapping_sub(1) as f32;
    let v36 = (sc * p.hh) * sigma;
    let rc = gt.r.mul_vec(p.center);
    let cc = Vec3::new(
        (rc.x * sc + v36 * a.x) + gt.offset.x,
        (rc.y * sc + a.y * v36) + gt.offset.y,
        (rc.z * sc + a.z * v36) + gt.offset.z,
    );
    let l2 = frame.n.len2();
    if l2 == 0.0 || l2.is_nan() {
        frame.n = Vec3::new(sigma * a.x, sigma * a.y, sigma * a.z);
        frame.axes[0] = e0;
        frame.axes[1] = Vec3::new(sigma * e1.x, sigma * e1.y, sigma * e1.z);
        frame.origin = cc;
    }
    let dd = cc - frame.origin;
    let n_tess = d.n_tess;
    let two_pi = f64::from_bits(0x4019_21FB_5444_2D18);
    let step = (f64::from(sgnnz(a.dot(frame.n))) * (two_pi / f64::from(n_tess))) as f32;
    let rs = p.r * sc;
    let (e0r, e1r) = (e0 * rs, e1 * rs);
    let id = ((2i32.wrapping_mul(v)) | 0x40) >> 1;
    let n = n_tess.max(0) as usize;
    set_len(&mut out.pts, n + 1);
    set_len(&mut out.ids_a, n);
    out.ids_shared = true;
    let mut ang = 0f32;
    for m in 0..n {
        let sn = crt::sinf(ang);
        let (sx, sy, sz) = (e1r.x * sn, e1r.y * sn, e1r.z * sn);
        let cs = crt::cosf(ang);
        ang += step;
        let q = Vec3::new(
            (cs * e0r.x + dd.x) + sx,
            (e0r.y * cs + dd.y) + sy,
            (e0r.z * cs + dd.z) + sz,
        );
        out.pts[m] = project(frame, q);
        out.ids_a[m] = id;
    }
    out.pts[n] = out.pts[0];
    if n > 2 {
        out.ids_a[2] = out.ids_a[0];
    }
    out.ids_b = out.ids_a.clone();
    n as i32
}

/// Cylinder slot 59 (0x351427D0, also the capsule's): the axis segment through the centre,
/// `p± = (sc·hh)·(±A) + ((R·c)·sc + o) − origin` (`+` first; the feature is ignored), both
/// projected; point 2 repeats point 0; the three ids are 64 (one array for both outputs).
/// Returns 2.
pub fn cylinder_edge_segment(
    d: &CylinderData,
    frame: &ContactFrame,
    gt: &GeomUnderTest,
    out: &mut FeaturePolygon,
) -> i32 {
    let p = &d.prim;
    let a = gt.r.mul_vec(p.axis);
    let sc = gt.scale;
    let rc = gt.r.mul_vec(p.center);
    let c = Vec3::new(
        rc.x * sc + gt.offset.x,
        rc.y * sc + gt.offset.y,
        rc.z * sc + gt.offset.z,
    );
    let h = sc * p.hh;
    let p0 = Vec3::new(
        (h * a.x + c.x) - frame.origin.x,
        (a.y * h + c.y) - frame.origin.y,
        (a.z * h + c.z) - frame.origin.z,
    );
    let h2 = p.hh * sc;
    let p1 = Vec3::new(
        (c.x - h2 * a.x) - frame.origin.x,
        (c.y - a.y * h2) - frame.origin.y,
        (c.z - a.z * h2) - frame.origin.z,
    );
    set_len(&mut out.pts, 3);
    out.pts[0] = project(frame, p0);
    out.pts[1] = project(frame, p1);
    out.pts[2] = out.pts[0];
    out.ids_a.clear();
    out.ids_a.extend_from_slice(&[64, 64, 64]);
    out.ids_b = out.ids_a.clone();
    out.ids_shared = true;
    2
}
