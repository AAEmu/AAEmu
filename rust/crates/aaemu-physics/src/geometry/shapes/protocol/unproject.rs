//! Slot 49 `UnprojectSphere(center, r, rsep, unproj_result*)` (inferred name): push a sphere
//! out of the geometry (geometry frame, no transform). Returns 1 and fills the 48-byte
//! [`UnprojResult`] on contact, 0 otherwise.
//!
//! The box and the cylinder forward into pair functions of the UNPROJECT table, which belong
//! to package P2b; the port calls them through [`SphereUnprojectCallees`]. The box also calls
//! the OVERLAP cell `[box][sphere]` (0x35196CE0, P2a), which is tiny and ported here as
//! [`overlap_box_sphere`].

use crate::collision::contact::{UnprojParams, UnprojResult};
use crate::geometry::primitives::{BoxPrim, CylinderPrim, SpherePrim};
use crate::geometry::shapes::cylinder::CylinderData;
use crate::geometry::shapes::{normalize_safe, BoxGeom, SphereGeom};
use crate::math::{max_idiom, sgnnz, Vec3};

/// The UNPROJECT cells slot 49 forwards into (owned by P2b). Both are called with
/// `iFeature = -1` on both sides and a null contact area.
pub trait SphereUnprojectCallees {
    /// `UNPROJECT[0][box][sphere]` = 0x35185B80 (linear unprojection, box vs sphere).
    fn unproject_box_sphere(
        &mut self,
        params: &mut UnprojParams,
        b: &BoxPrim,
        s: &SpherePrim,
        out: &mut UnprojResult,
    ) -> i32;

    /// `UNPROJECT[0][cylinder][sphere]` = 0x35187780 (linear unprojection, cylinder vs sphere).
    fn unproject_cylinder_sphere(
        &mut self,
        params: &mut UnprojParams,
        c: &CylinderPrim,
        s: &SpherePrim,
        out: &mut UnprojResult,
    ) -> i32;
}

/// The `unproj_params` slot 49 builds on its stack: `dir = 0` (minimum-penetration direction,
/// written back by the callee), `tmin` (`+36`) `= 0`, `maxcos` (`+44`) `= 0.1`, `+48 = 0`.
///
/// The binary leaves `mode` (`+0`), the rotation centre (`+16`), `vel` (`+28`), `tmax`
/// (`+32`) and `minPtDist` (`+40`) **uninitialised** (stack garbage). The port sets them to 0;
/// the linear box/cylinder–sphere routines do not read them on the `dir = 0` path (the oracle
/// tests replay the callees with zeroed fields and match the slot's output).
#[must_use]
pub fn slot49_params() -> UnprojParams {
    UnprojParams {
        mode: 0,
        dir: Vec3::ZERO,
        center: Vec3::ZERO,
        vel: 0.0,
        tmax: 0.0,
        tmin: 0.0,
        min_pt_dist: 0.0,
        max_cos: f32::from_bits(0x3DCC_CCCD),
        flag: 0,
    }
}

/// `OVERLAP[box][sphere]` (0x35196CE0; P2a's cell, ported locally because slot 49 needs it):
/// `l = Basis·(c_s − c_b)` (or the plain difference when not oriented), per axis
/// `q = (||l| − s| + (|l| − s))·0.5` (the `max(·, 0)` idiom), overlap iff
/// `isneg(((q_y² + q_x²) + q_z²) − r²)`.
#[must_use]
pub fn overlap_box_sphere(b: &BoxPrim, s: &SpherePrim) -> bool {
    let d = s.center - b.center;
    let l = if b.oriented != 0 {
        b.basis.mul_vec(d)
    } else {
        d
    };
    let qx = l.x.abs() - b.size.x;
    let qy = l.y.abs() - b.size.y;
    let qz = l.z.abs() - b.size.z;
    let vx = qx.abs() + qx;
    let vy = qy.abs() + qy;
    let vz = qz.abs() + qz;
    let (hx, hy, hz) = (vx * 0.5, vy * 0.5, vz * 0.5);
    let v = ((hy * hy + hx * hx) + hz * hz) - s.r * s.r;
    v.to_bits() >> 31 != 0
}

/// `pt −= dir·(float)t` after the forwarded unprojection (box and cylinder): moves the contact
/// point back by the unprojection, `dir` as the callee wrote it back into the params.
fn shift_back(params: &UnprojParams, out: &mut UnprojResult) {
    let t = out.t as f32;
    out.pt = Vec3::new(
        out.pt.x - params.dir.x * t,
        out.pt.y - params.dir.y * t,
        out.pt.z - params.dir.z * t,
    );
}

/// Box slot 49 (0x3513AFC0).
///
/// The sphere is `{center, rsep}`: **the box ignores `r`** and uses the fourth argument as
/// the radius (quirk kept). If [`overlap_box_sphere`] rejects, returns 0 and leaves `out`
/// untouched; otherwise calls `UNPROJECT[0][box][sphere]` with [`slot49_params`] (its return
/// value is ignored), shifts `out.pt` back by `dir·t` and returns 1.
pub fn box_unproject_sphere(
    g: &BoxGeom,
    center: Vec3,
    _r: f32,
    rsep: f32,
    out: &mut UnprojResult,
    callees: &mut dyn SphereUnprojectCallees,
) -> i32 {
    let s = SpherePrim { center, r: rsep };
    if !overlap_box_sphere(&g.prim, &s) {
        return 0;
    }
    let mut params = slot49_params();
    callees.unproject_box_sphere(&mut params, &g.prim, &s, out);
    shift_back(&params, out);
    1
}

/// Sphere slot 49 (0x351AFA60): `d = c − c_s`; returns 0 if `|d|² > (rsep + R)²` (a NaN
/// distance passes); `n = normalize_safe(d, +z)`; `t = (double)(((d·n) − R) − r)` (the signed
/// gap, negative when overlapping); `pt = n·R + c_s`. Writes `t`, `pt`, `n` only.
pub fn sphere_unproject_sphere(
    g: &SphereGeom,
    center: Vec3,
    r: f32,
    rsep: f32,
    out: &mut UnprojResult,
) -> i32 {
    let p = &g.prim;
    let d = center - p.center;
    let l2 = (d.y * d.y + d.x * d.x) + d.z * d.z;
    let lim = rsep + p.r;
    if l2 > lim * lim {
        return 0;
    }
    let n = normalize_safe(d, Vec3::Z);
    out.n = n;
    out.t = f64::from(((d.y * n.y + n.x * d.x) + d.z * n.z - p.r) - r);
    out.pt = Vec3::new(
        n.x * p.r + p.center.x,
        n.y * p.r + p.center.y,
        n.z * p.r + p.center.z,
    );
    1
}

/// Cylinder slot 49 (0x35142C10).
///
/// Cheap reject with `rsep`: with `d = c − c_c`, `ρ² = |a × d|²` (as
/// `((ax·dz − az·dx)² + (az·dy − ay·dz)²) + (ay·dx − ax·dy)²`), `m = max(ρ², R²)` (idiom),
/// `h = |d·a| − hh`, `v = ((max0(h))² + (m + R²)) − rsep²`; reject (return 0, `out` untouched)
/// if `|v|·v > (m·R²)·4`. Otherwise `UNPROJECT[0][cylinder][sphere]` on the sphere `{c, r}`
/// with [`slot49_params`] (return value ignored), `pt −= dir·t`, return 1.
pub fn cylinder_unproject_sphere(
    d: &CylinderData,
    center: Vec3,
    r: f32,
    rsep: f32,
    out: &mut UnprojResult,
    callees: &mut dyn SphereUnprojectCallees,
) -> i32 {
    let p = &d.prim;
    let a = p.axis;
    let dx = center.x - p.center.x;
    let dy = center.y - p.center.y;
    let dz = center.z - p.center.z;
    let r2 = p.r * p.r;
    let t1 = a.x * dz - a.z * dx;
    let t2 = a.z * dy - a.y * dz;
    let t3 = a.y * dx - a.x * dy;
    let rho2 = (t1 * t1 + t2 * t2) + t3 * t3;
    let m = max_idiom(rho2, r2);
    let h = ((dy * a.y + a.x * dx) + dz * a.z).abs() - p.hh;
    let hp = ((-h).abs() + h) * 0.5;
    let v = (hp * hp + (m + r2)) - rsep * rsep;
    if v.abs() * v > (m * r2) * 4.0 {
        return 0;
    }
    let s = SpherePrim { center, r };
    let mut params = slot49_params();
    callees.unproject_cylinder_sphere(&mut params, p, &s, out);
    shift_back(&params, out);
    1
}

/// Capsule slot 49 (0x35140AA0): computed directly (no table call), **`rsep` is ignored**.
///
/// `h = (c − c_c)·a`. If `|h| ≥ hh` (end cap; NaN goes to the side branch): the cap centre
/// `q = a·(sgnnz(h)·hh) + c_c`; reject if `|q − c|² > (r + R)²`; `n = normalize_safe(c − q, +z)`,
/// `pt = n·R + q`, `iFeature[0] = 66 − isneg(h)` (0x42 / 0x41). Side: `n = (c − c_c) −
/// a·((c − c_c)·a)` is written to `out.n` **before** the reject test `|n|² > (R + r)²` (so a
/// rejected call still overwrites `out.n`, quirk kept), then normalised in place
/// (`sub_35021020`), `pt = (a·h + c_c) + n·R`, `iFeature[0] = 64`. `t` is never written.
pub fn capsule_unproject_sphere(
    d: &CylinderData,
    center: Vec3,
    r: f32,
    out: &mut UnprojResult,
) -> i32 {
    let p = &d.prim;
    let a = p.axis;
    let h = ((center.x - p.center.x) * a.x + (center.y - p.center.y) * a.y)
        + (center.z - p.center.z) * a.z;
    if h.abs() >= p.hh {
        let e = sgnnz(h) as f32 * p.hh;
        let q = Vec3::new(
            a.x * e + p.center.x,
            a.y * e + p.center.y,
            a.z * e + p.center.z,
        );
        let (qx, qy, qz) = (q.x - center.x, q.y - center.y, q.z - center.z);
        let lim = r + p.r;
        if (qy * qy + qx * qx) + qz * qz > lim * lim {
            return 0;
        }
        let dd = Vec3::new(center.x - q.x, center.y - q.y, center.z - q.z);
        let n = normalize_safe(Vec3::new(dd.x, dd.y, dd.z), Vec3::Z);
        out.n = n;
        out.pt = Vec3::new(n.x * p.r + q.x, n.y * p.r + q.y, n.z * p.r + q.z);
        out.i_feature[0] = 66 - (h.to_bits() >> 31) as i32;
        1
    } else {
        let v = Vec3::new(
            center.x - p.center.x,
            center.y - p.center.y,
            center.z - p.center.z,
        );
        let hh2 = (v.y * a.y + v.x * a.x) + v.z * a.z;
        let n = Vec3::new(v.x - a.x * hh2, v.y - a.y * hh2, v.z - a.z * hh2);
        out.n = n;
        let lim = p.r + r;
        if (n.x * n.x + n.y * n.y) + n.z * n.z > lim * lim {
            return 0;
        }
        let n = normalize_safe(n, Vec3::Z);
        out.n = n;
        out.pt = Vec3::new(
            (a.x * h + p.center.x) + n.x * p.r,
            (a.y * h + p.center.y) + n.y * p.r,
            (a.z * h + p.center.z) + n.z * p.r,
        );
        out.i_feature[0] = 64;
        1
    }
}
