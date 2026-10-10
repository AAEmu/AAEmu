//! Slots 13 `CalculateBuoyancy(plane, gwd, Vec3 &massCenter)` → submerged volume, and 12
//! `CalcVolumetricPressure(gwd, epicenter, k, rmin, centerOfMass, P&, L&)` (`geometry.md`
//! §6.3, §6.5).
//!
//! The water plane is `{n, origin}`; "submerged" is the side `n·(p − origin) < 0`. The
//! centre output is written in the world frame (with the quirks noted per class).

use super::cylinder::{CylinderData, PI_F64};
use super::sphere::SPHERE_K_F64;
use super::{gwd_point, BoxGeom, SphereGeom};
use crate::geometry::primitives::Plane;
use crate::geometry::GeomWorldData;
use crate::math::{crt, max0_idiom, max_idiom, min_idiom, sgnnz, Vec3};

pub use mesh::{
    box_to_mesh, mesh_buoyancy, mesh_volumetric_pressure, tet_volume_centroid, TriMeshView,
};

mod mesh;

/// `π/2` (double 0x3FF921FB54442D18).
const PIO2_F64: f64 = f64::from_bits(0x3FF9_21FB_5444_2D18);

/// Box slot 13 (0x3513B1B0): builds a temporary `CTriMesh` from the box (`sub_3513AC00`, see
/// [`box_to_mesh`]) and returns the mesh's slot 13 (0x351DA620, [`mesh_buoyancy`]).
pub fn box_buoyancy(g: &BoxGeom, plane: &Plane, gwd: &GeomWorldData) -> (f32, Vec3) {
    let m = box_to_mesh(g);
    mesh_buoyancy(&m.view(), plane, gwd)
}

/// Sphere slot 13 (0x351AEC00).
///
/// `c = R·center + offset` (**the scale is not applied to the centre**, quirk kept),
/// `r = scale·r`, `h = n·(origin − c)` (water height above the centre); the centre output
/// starts as `c`.
/// * `h < −r` (or NaN): 0.
/// * `h > r`: `(float)((double)((r·r)·r)·4/3π)`.
/// * otherwise `V = (float)((double)(((r² − h²/3)·h) + (r²·r)·2/3)·π)` and the centre becomes
///   `c + n·off` with `off = (float)((double)(((r² − h²·0.5)·h²) − ((r²·r)·r)·0.5)·π/2 − h)`
///   — **not divided by V** (quirk kept: the binary subtracts `h` where a physical centroid
///   would divide by the volume).
pub fn sphere_buoyancy(g: &SphereGeom, plane: &Plane, gwd: &GeomWorldData) -> (f32, Vec3) {
    let c = g.world_center(gwd, false);
    let r = gwd.scale * g.prim.r;
    let (n, o) = (plane.n, plane.origin);
    let h = n.dot(o - c);
    if !(h >= -r) {
        return (0.0, c);
    }
    if h > r {
        return ((f64::from((r * r) * r) * SPHERE_K_F64) as f32, c);
    }
    let r2 = r * r;
    let h2 = h * h;
    let r3 = r2 * r;
    let a = (r2 - h2 * 0.5) * h2 - (r3 * r) * 0.5;
    let b = (r2 - h2 * f32::from_bits(0x3EAA_AAAB)) * h + r3 * f32::from_bits(0x3F2A_AAAB);
    let off = (f64::from(a) * PIO2_F64 - f64::from(h)) as f32;
    let v = (f64::from(b) * PI_F64) as f32;
    (
        v,
        Vec3::new(n.x * off + c.x, n.y * off + c.y, n.z * off + c.z),
    )
}

/// `max(min(x·rinv, 1), −1)` with the engine's idioms (the `asinf` argument clamp of the
/// cylinder code).
#[inline]
fn clamp_unit(u: f32) -> f32 {
    max_idiom(min_idiom(u, 1.0), -1.0)
}

/// Cylinder slot 13 (0x35143230); the capsule's slot 13 (0x3513F790) is a thunk to it and the
/// capsule is recognised inside through the virtual `GetType() == 6`.
///
/// Model: `r = s·r`, `L = (s·hh)·2` (a capsule adds `(r_unscaled·4/3)·s`, i.e. it is treated as
/// a cylinder of equal volume — quirk kept), `c = (R·center)·s + offset`, `a = R·axis` flipped
/// to `a·n >= 0` (`sgnnz`), bottom `b = c − a·L/2`. The centre output starts as
/// `(c − a·L/2) + a·L/2` (rounded twice, kept).
/// * near-vertical (`|n × a|² < 1e-4`): `d = max0(min(n·(origin − b), L))`, `V = π·r²·d`
///   (double), centre `b + a·d/2`;
/// * otherwise `e = normalize(n × a)`, `f = e × a`, waterline coordinates
///   `x(t) = n·(origin − b − a·t)/(f·n)`; up to three pieces (submerged slab, prism with the
///   circular-segment area `r²(π/2 − asin(x/r)) − x·√(r² − x²)`, the wedge in closed form),
///   summed left to right from `+0.0` (so `−0.0` pieces vanish); `V <= 0` returns 0 with the
///   initial centre, else `centre = M·(1/V) + b`.
///
/// `asinf` is MSVCR100's ([`crt::asinf`], bit-exact).
pub fn cylinder_buoyancy(
    d: &CylinderData,
    capsule: bool,
    plane: &Plane,
    gwd: &GeomWorldData,
) -> (f32, Vec3) {
    let p = &d.prim;
    let s = gwd.scale;
    let r = s * p.r;
    let mut len = (s * p.hh) * 2.0;
    let r2 = r * r;
    let rinv = 1.0 / r;
    if capsule {
        len += (p.r * f32::from_bits(0x3FAA_AAAB)) * s;
    }
    let c = gwd_point(gwd, p.center);
    let a0 = gwd.r.mul_vec(p.axis);
    let (n, o) = (plane.n, plane.origin);
    let sg = sgnnz(n.dot(a0)) as f32;
    let a = Vec3::new(sg * a0.x, a0.y * sg, a0.z * sg);
    let half = len * 0.5;
    let hy = a.y * half;
    let hz = a.z * half;
    let hx = half * a.x;
    let b = Vec3::new(c.x - hx, c.y - hy, c.z - hz);
    let center0 = Vec3::new(hx + b.x, hy + b.y, hz + b.z);
    let e0 = n.cross(a);
    let e2 = e0.len2();
    let full = |len: f32| (f64::from(r2) * PI_F64 * f64::from(len)) as f32;
    if e2 < 1e-4 {
        let t = n.dot(o - b);
        let dd = max0_idiom(min_idiom(t, len));
        let hd = dd * 0.5;
        let v = (f64::from(r2) * PI_F64 * f64::from(dd)) as f32;
        return (v, Vec3::new(hd * a.x + b.x, a.y * hd + b.y, a.z * hd + b.z));
    }
    let e = if e2 > 1e-20 {
        let inv = 1.0 / e2.sqrt();
        Vec3::new(inv * e0.x, e0.y * inv, e0.z * inv)
    } else {
        Vec3::Z
    };
    let f = e.cross(a);
    let fn_inv = 1.0 / f.dot(n);
    let mut x0 = n.dot(o - b) * fn_inv;
    if x0 >= r {
        return (0.0, center0);
    }
    // pieces: volumes and moments (relative to b)
    let mut vols = [0f32; 3];
    let mut moms = [Vec3::ZERO; 3];
    let mut cnt = 0usize;
    let mut t1 = 0.0f32;
    if !(x0 >= -r) {
        let an = n.dot(a);
        if an * an < 1e-4 {
            return (full(len), center0);
        }
        cnt = 1;
        x0 = r * f32::from_bits(0xBF7F_F972);
        let q = Vec3::new(b.x - f.x * r, b.y - f.y * r, b.z - f.z * r);
        t1 = ((o.x - q.x) * n.x + (o.y - q.y) * n.y + (o.z - q.z) * n.z) / an;
        let v1 = (f64::from(r2) * PI_F64 * f64::from(t1)) as f32;
        vols[0] = v1;
        let m = (t1 * 0.5) * v1;
        moms[0] = Vec3::new(m * a.x, a.y * m, a.z * m);
    }
    let top = Vec3::new(a.x * len + b.x, a.y * len + b.y, a.z * len + b.z);
    let mut x1 = ((o.x - top.x) * n.x + (o.y - top.y) * n.y + (o.z - top.z) * n.z) * fn_inv;
    if !(x1 >= -r) {
        return (full(len), center0);
    }
    let t_end;
    if x1 <= r || x1.is_nan() {
        t_end = len;
        let q1 = (r2 - x1 * x1).sqrt();
        let a1 = crt::asinf(clamp_unit(x1 * rinv));
        let lt = len - t1;
        let v2 = (((PIO2_F64 - f64::from(a1)) * f64::from(r2) - f64::from(q1 * x1)) * f64::from(lt))
            as f32;
        let m = ((t1 + len) * 0.5) * v2;
        let k = (((q1 * q1) * q1) * f32::from_bits(0x3F2A_AAAB)) * lt;
        vols[cnt] = v2;
        moms[cnt] = Vec3::new(k * f.x + m * a.x, f.y * k + a.y * m, f.z * k + a.z * m);
        cnt += 1;
    } else {
        x1 = r * f32::from_bits(0x3F7F_F972);
        let q = Vec3::new(f.x * r + b.x, f.y * r + b.y, f.z * r + b.z);
        let num = (o.x - q.x) * n.x + (o.y - q.y) * n.y + (o.z - q.z) * n.z;
        t_end = num / ((n.y * a.y + n.x * a.x) + n.z * a.z);
    }
    if x1 > r * 0.01 + x0 {
        let v88 = x1 - x0;
        let k = (t_end - t1) * (1.0 / v88);
        let x02 = x0 * x0;
        let q0 = (r2 - x02).sqrt();
        let a0 = crt::asinf(clamp_unit(x0 * rinv));
        let x12 = x1 * x1;
        let q1 = (r2 - x12).sqrt();
        let a1 = crt::asinf(clamp_unit(x1 * rinv));
        let v98 = q1 * x1;
        let q03 = (q0 * q0) * q0;
        let v100 = a1 * r2 + v98;
        let q13 = (q1 * q1) * q1;
        let v102 = (((q13 - q03) * f32::from_bits(0x3EAA_AAAB) + v88 * v100)
            - ((((a1 * x1 - a0 * x0) + q1) - q0) * r2))
            * k;
        let v103 = q03 * x0;
        let v104 = q0 * x0;
        let v106 = v98 - v104;
        let v107 = q13 * x1 - v103;
        let da = a1 - a0;
        let v108 = v106 * 0.75 - (r2 * 0.25) * da;
        let v109 = da * r2 + v106;
        let v110 =
            ((v109 * (r2 * 0.375) + v107 * 0.25) - v88 * q13) * (k * f32::from_bits(0x3F2A_AAAB));
        let v111 = (((x12 - x02) * v100 + v107 * 0.5) - (((v108 + x12 * a1) - x02 * a0) * r2))
            * ((k * k) * 0.5)
            + (t1 - k * x0) * v102;
        vols[cnt] = v102;
        moms[cnt] = Vec3::new(
            v110 * f.x + v111 * a.x,
            f.y * v110 + a.y * v111,
            f.z * v110 + a.z * v111,
        );
        cnt += 1;
    }
    let mut v = 0.0f32;
    let mut m = Vec3::ZERO;
    for i in 0..cnt {
        v += vols[i];
        m = Vec3::new(m.x + moms[i].x, m.y + moms[i].y, m.z + moms[i].z);
    }
    if v > 0.0 {
        let inv = 1.0 / v;
        (
            v,
            Vec3::new(m.x * inv + b.x, m.y * inv + b.y, m.z * inv + b.z),
        )
    } else {
        (0.0, center0)
    }
}

/// Box slot 12 (0x3513B0F0): temporary 12-triangle mesh + mesh slot 12 (0x351CBE20).
#[allow(clippy::too_many_arguments)]
pub fn box_volumetric_pressure(
    g: &BoxGeom,
    gwd: &GeomWorldData,
    epicenter: Vec3,
    k: f32,
    rmin: f32,
    com: Vec3,
    p: &mut Vec3,
    l: &mut Vec3,
) {
    let m = box_to_mesh(g);
    mesh_volumetric_pressure(&m.view(), gwd, epicenter, k, rmin, com, p, l);
}

/// Sphere slot 12 (0x351AEE70): `d = ((R·c)·s + offset) − epicenter`,
/// `P += d·(k / (max(|d|², rmin²)·√|d|²))` (max idiom; no area factor, `L` untouched).
#[allow(clippy::too_many_arguments)]
pub fn sphere_volumetric_pressure(
    g: &SphereGeom,
    gwd: &GeomWorldData,
    epicenter: Vec3,
    k: f32,
    rmin: f32,
    _com: Vec3,
    p: &mut Vec3,
    _l: &mut Vec3,
) {
    let d = g.world_center(gwd, true) - epicenter;
    let d2 = d.len2();
    let rm2 = rmin * rmin;
    let f = k / ((((d2 - rm2).abs() + (d2 + rm2)) * 0.5) * d2.sqrt());
    *p = Vec3::new(f * d.x + p.x, d.y * f + p.y, d.z * f + p.z);
}

/// Cylinder / capsule slot 12 (0x35142EE0): `d = c_w − epicenter`, `|d|` (sqrtss),
/// `d̂ = d·(1/|d|)` if `|d| > 0.01·r`, else `(0,0,1)`; `sinθ = |a_w × d̂|`;
/// `area = (float)(((double)(r·r)·π·(double)(1 − sinθ) + (double)(((r·hh)·4)·sinθ))·
/// (double)(s·s))`; `P += d̂·((area·k) / (m·m))` with `m = max(|d|, rmin)` (idiom). `L` is
/// untouched; the capsule uses the cylinder formula (its caps are ignored).
#[allow(clippy::too_many_arguments)]
pub fn cylinder_volumetric_pressure(
    d: &CylinderData,
    gwd: &GeomWorldData,
    epicenter: Vec3,
    k: f32,
    rmin: f32,
    _com: Vec3,
    p: &mut Vec3,
    _l: &mut Vec3,
) {
    let pr = &d.prim;
    let s = gwd.scale;
    let dv = gwd_point(gwd, pr.center) - epicenter;
    let len = dv.len2().sqrt();
    let u = if len > pr.r * 0.01 {
        let inv = 1.0 / len;
        Vec3::new(dv.x * inv, dv.y * inv, dv.z * inv)
    } else {
        Vec3::Z
    };
    let a = gwd.r.mul_vec(pr.axis);
    let sin_t = a.cross(u).len2().sqrt();
    let area = ((f64::from(pr.r * pr.r) * PI_F64 * f64::from(1.0 - sin_t)
        + f64::from(((pr.r * pr.hh) * 4.0) * sin_t))
        * f64::from(s * s)) as f32;
    let m = max_idiom(len, rmin);
    let f = (area * k) / (m * m);
    *p = Vec3::new(f * u.x + p.x, u.y * f + p.y, u.z * f + p.z);
}
