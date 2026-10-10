//! Slot 14 of the round primitives: sphere 0x351AFCB0, cylinder 0x35146F40 (with
//! CircleClipByLine 0x351440E0) and capsule 0x35140EA0.

use super::super::cylinder::CylinderData;
use super::super::sphere::SphereGeom;
use super::super::{gwd_point, orthogonal};
use super::polygon_drag_integrate;
use crate::geometry::primitives::{Plane, SpherePrim};
use crate::geometry::GeomWorldData;
use crate::math::{crt, max0_idiom, max_idiom, min_idiom, sgnnz, Vec3};

pub use cyl::{circle_clip_by_line, cylinder_medium_resistance};

mod cyl;

/// `π/2` (double).
const PIO2_F64: f64 = f64::from_bits(0x3FF9_21FB_5444_2D18);
/// `−π` (double 0xC00921FB54442D18).
const NEG_PI_F64: f64 = f64::from_bits(0xC009_21FB_5444_2D18);
/// `√3` (double 0x3FFBB67AE8584CAA), the 30° step of the side quads.
pub(super) const SQRT3_F64: f64 = f64::from_bits(0x3FFB_B67A_E858_4CAA);
/// `cos 15°`, `sin 15°` (float), the first side-quad normal.
pub(super) const COS15: f32 = f32::from_bits(0x3F77_46EA);
pub(super) const SIN15: f32 = f32::from_bits(0x3E84_83EE);

/// `max(min(x, 1), −1)` with the engine idioms (`asinf` argument clamps).
#[inline]
fn clamp_unit(x: f32) -> f32 {
    max_idiom(min_idiom(x, 1.0), -1.0)
}

/// One 30° step of a vector in the plane, in double:
/// `((double)x·√3 − (double)y)·0.5`, `((double)y·√3 + (double)x)·0.5`, each rounded to float.
#[inline]
pub(super) fn rot30(x: f32, y: f32) -> (f32, f32) {
    let nx = ((f64::from(x) * SQRT3_F64 - f64::from(y)) * 0.5) as f32;
    let ny = ((f64::from(y) * SQRT3_F64 + f64::from(x)) * 0.5) as f32;
    (nx, ny)
}

/// The side-quad frame of the cylinder and the capsule: rotation taking the frame's `z`
/// to `A = R·(−axis)`, written out as in the binary (Rodrigues with `cos = A.z`, `−sin`).
#[derive(Clone, Copy, Debug)]
pub(super) struct SideFrame {
    pub axis: Vec3,
    pub cos: f32,
    pub nsin: f32,
    /// `axis.z·0.0` (precomputed by the binary).
    pub az0: f32,
}

impl SideFrame {
    /// `A × (0,0,1)` with explicit `·0.0` products, `sin = |…|`, axis `(1,0,0)` when
    /// `sin <= 0.001`.
    pub fn new(a: Vec3) -> Self {
        let ax = a.y - a.z * 0.0;
        let ay = a.z * 0.0 - a.x;
        let az = a.x * 0.0 - a.y * 0.0;
        let sin = ((ay * ay + ax * ax) + az * az).sqrt();
        let axis = if sin <= 0.001 {
            Vec3::new(1.0, 0.0, 0.0)
        } else {
            let inv = 1.0 / sin;
            Vec3::new(ax * inv, ay * inv, az * inv)
        };
        Self {
            axis,
            cos: a.z,
            nsin: -sin,
            az0: axis.z * 0.0,
        }
    }

    /// Rotate `(x, y, 0)`.
    pub fn rot_xy(&self, x: f32, y: f32) -> Vec3 {
        let a = self.axis;
        let d = (a.x * x + a.y * y) + self.az0;
        let px = x - d * a.x;
        let py = y - a.y * d;
        let pz = -(a.z * d);
        Vec3::new(
            (px * self.cos + (pz * a.y - py * a.z) * self.nsin) + d * a.x,
            (py * self.cos + (a.z * px - pz * a.x) * self.nsin) + a.y * d,
            (pz * self.cos + (py * a.x - a.y * px) * self.nsin) + a.z * d,
        )
    }

    /// Rotate `(r, 0, 0)` as the initial point (the binary writes `perp.y = −(a.y·d)` there).
    pub fn rot_r(&self, r: f32) -> Vec3 {
        let a = self.axis;
        let d = (a.x * r + a.y * 0.0) + a.z * 0.0;
        let px = r - d * a.x;
        let py = -(a.y * d);
        let pz = -(a.z * d);
        Vec3::new(
            (self.cos * px + (pz * a.y - py * a.z) * self.nsin) + d * a.x,
            (py * self.cos + (a.z * px - pz * a.x) * self.nsin) + a.y * d,
            (pz * self.cos + (py * a.x - a.y * px) * self.nsin) + a.z * d,
        )
    }
}

/// The twelve side quads of a cylinder/capsule surface through [`polygon_drag_integrate`]:
/// circle points 30° apart (rotated in double), quad `[p1, p0, p0 − D, p1 − D]` with
/// `D = A·(2·hh·s)`, face normal at the half angle (starting at 15°).
#[allow(clippy::too_many_arguments)]
pub(super) fn side_quads(
    fr: &SideFrame,
    c: Vec3,
    r: f32,
    dd: Vec3,
    plane: &Plane,
    gwd: &GeomWorldData,
    dp: &mut Vec3,
    dl: &mut Vec3,
) {
    let p = fr.rot_r(r);
    let mut p0 = Vec3::new(p.x + c.x, p.y + c.y, p.z + c.z);
    let (mut x, mut y) = (r, 0.0f32);
    let (mut cs, mut sn) = (COS15, SIN15);
    for _ in 0..12 {
        (x, y) = rot30(x, y);
        let q = fr.rot_xy(x, y);
        let p1 = Vec3::new(q.x + c.x, q.y + c.y, q.z + c.z);
        let pts = [p1, p0, p0 - dd, p1 - dd];
        let n = fr.rot_xy(cs, sn);
        polygon_drag_integrate(&pts, n, plane, gwd.v, gwd.w, gwd.center_of_mass, dp, dl);
        (cs, sn) = rot30(cs, sn);
        p0 = p1;
    }
}

/// Sphere slot 14 (0x351AFCB0) — **assigns** `dP`/`dL`.
///
/// `c = (R·center)·s + offset`, `r = s·r`, `u = v + w × (c − com)` (velocity of the centre),
/// `h = n·(origin − c)`.
/// * `|h| > r`: zero; if `h > r` (fully submerged) `dP = u·(float)((double)r·(−π)·(double)r)`.
/// * otherwise the projected submerged disk: `û = normalize(u)` (or `(0,0,1)`),
///   `σ = |û × n|`, `b = (û × n)/σ` and `d1 = h/σ`, or for `σ < 0.01`
///   `b = normalize(orthogonal(û))`, `d1 = sgnnz(h)·r`; `a = b × û`;
///   `A1 = ½((asin(d1c/r) + π/2)·r² + q1·d1c)` (double), `d1c = clamp(d1, ±0.999r)`;
///   waterline disk `ρ = max(√(r² − h²), 0.001)`,
///   `d2 = clamp((d1 − σh)/max(|u·n|, 0.001), ±0.999ρ)`,
///   `A2 = ½((sgnnz(u·n)·asin(d2/ρ) + π/2)·ρ² + q2·d2)·|u·n|` (double);
///   `dP = −u·(A1 + A2)`; centre of pressure along `a` at
///   `t = ((σh − q2³/3·(u·n))·A2 − q1³/3·A1)/(A1 + A2)`, point
///   `c + a·t + û·√max0(r² − t²)`; `dL = (p − com) × dP`.
///
/// Uses the binary's MSVCR100 `asinf` ([`crt::asinf`]).
pub fn sphere_medium_resistance(
    g: &SphereGeom,
    plane: &Plane,
    gwd: &GeomWorldData,
    dp: &mut Vec3,
    dl: &mut Vec3,
) {
    sphere_drag_prim(&g.prim, plane, gwd, dp, dl);
}

/// [`sphere_medium_resistance`] on a bare primitive (the capsule builds temporary spheres).
pub(super) fn sphere_drag_prim(
    sp: &SpherePrim,
    plane: &Plane,
    gwd: &GeomWorldData,
    dp: &mut Vec3,
    dl: &mut Vec3,
) {
    let (n, o) = (plane.n, plane.origin);
    let s = gwd.scale;
    let c = gwd_point(gwd, sp.center);
    let r = s * sp.r;
    let com = gwd.center_of_mass;
    let (w, v) = (gwd.w, gwd.v);
    let d = c - com;
    let u = Vec3::new(
        (d.z * w.y - d.y * w.z) + v.x,
        (d.x * w.z - d.z * w.x) + v.y,
        (d.y * w.x - d.x * w.y) + v.z,
    );
    let h = ((o.x - c.x) * n.x + (o.y - c.y) * n.y) + (o.z - c.z) * n.z;
    if h.abs() > r {
        *dp = Vec3::ZERO;
        *dl = Vec3::ZERO;
        if h > r {
            let k = (f64::from(r) * NEG_PI_F64 * f64::from(r)) as f32;
            *dp = Vec3::new(k * u.x, u.y * k, u.z * k);
        }
        return;
    }
    let ul2 = (u.x * u.x + u.y * u.y) + u.z * u.z;
    let uh = if ul2 > 1e-20 {
        let inv = 1.0 / ul2.sqrt();
        Vec3::new(inv * u.x, u.y * inv, u.z * inv)
    } else {
        Vec3::new(0.0, 0.0, 1.0)
    };
    let b0 = Vec3::new(
        uh.y * n.z - uh.z * n.y,
        uh.z * n.x - n.z * uh.x,
        n.y * uh.x - uh.y * n.x,
    );
    let sigma = ((b0.x * b0.x + b0.y * b0.y) + b0.z * b0.z).sqrt();
    let (b, d1) = if sigma >= 0.01 {
        let inv = 1.0 / sigma;
        (Vec3::new(b0.x * inv, b0.y * inv, b0.z * inv), inv * h)
    } else {
        let o3 = orthogonal(uh);
        let l2 = (o3.x * o3.x + o3.y * o3.y) + o3.z * o3.z;
        let bb = if l2 > 1e-20 {
            let inv = 1.0 / l2.sqrt();
            Vec3::new(o3.x * inv, o3.y * inv, o3.z * inv)
        } else {
            Vec3::new(0.0, 0.0, 1.0)
        };
        (bb, sgnnz(h) as f32 * r)
    };
    let a = Vec3::new(
        b.y * uh.z - b.z * uh.y,
        b.z * uh.x - uh.z * b.x,
        uh.y * b.x - b.y * uh.x,
    );
    let r2 = r * r;
    let un = (u.x * n.x + u.y * n.y) + u.z * n.z;
    let sh = sigma * h;
    let q1 = (r2 - h * h).sqrt();
    let rho = max_idiom(q1, 0.001);
    let d1c = max_idiom(r * f32::from_bits(0xBF7F_BE77), min_idiom(r * f32::from_bits(0x3F7F_BE77), d1));
    let qa = (r2 - d1c * d1c).sqrt();
    let a1 = crt::asinf(clamp_unit(d1c / r));
    let area1 = (((f64::from(a1) + PIO2_F64) * f64::from(r2) + f64::from(qa * d1c)) * 0.5) as f32;
    let aun = un.abs();
    let k1 = ((qa * qa) * qa) * f32::from_bits(0xBEAA_AAAB);
    let rho2 = rho * rho;
    let den = max_idiom(aun, 0.001);
    let x2 = (d1 - sh) / den;
    let d2 = max_idiom(
        rho * f32::from_bits(0xBF7F_BE77),
        min_idiom(rho * f32::from_bits(0x3F7F_BE77), x2),
    );
    let qb = (rho2 - d2 * d2).sqrt();
    let a2 = crt::asinf(clamp_unit(d2 / rho));
    let a2s = a2 * sgnnz(un) as f32;
    let area2 = ((((f64::from(a2s) + PIO2_F64) * f64::from(rho2)) + f64::from(qb * d2))
        * f64::from(aun)
        * 0.5) as f32;
    let tot = area2 + area1;
    let f = Vec3::new(-u.x * tot, -u.y * tot, -u.z * tot);
    *dp = f;
    let k2 = sh - (((qb * qb) * qb) * f32::from_bits(0x3EAA_AAAB)) * un;
    let t = (k2 * area2 + k1 * area1) / tot;
    let q = max0_idiom(r2 - t * t).sqrt();
    let rp = Vec3::new(
        ((a.x * t + q * uh.x) + c.x) - com.x,
        ((a.y * t + uh.y * q) + c.y) - com.y,
        ((a.z * t + uh.z * q) + c.z) - com.z,
    );
    *dl = Vec3::new(
        rp.y * f.z - rp.z * f.y,
        rp.z * f.x - f.z * rp.x,
        f.y * rp.x - rp.y * f.x,
    );
}

/// Capsule slot 14 (0x35140EA0): **zeroes** `dP`/`dL`, runs the twelve side quads of the
/// cylindrical part (see [`side_quads`]; frame from `A = R·(−axis)`, first point on the
/// `−axis` end circle `c + A·hh·s`), then adds the full sphere slot 14 of a temporary sphere
/// (unscaled `r`) at each end, `+axis·hh` first — so each end cap is counted as a **whole**
/// sphere (quirk kept).
pub fn capsule_medium_resistance(
    d: &CylinderData,
    plane: &Plane,
    gwd: &GeomWorldData,
    dp: &mut Vec3,
    dl: &mut Vec3,
) {
    let p = &d.prim;
    *dp = Vec3::ZERO;
    *dl = Vec3::ZERO;
    let s = gwd.scale;
    let hh = s * p.hh;
    let r = s * p.r;
    let a = gwd.r.mul_vec(Vec3::new(-p.axis.x, -p.axis.y, -p.axis.z));
    let fr = SideFrame::new(a);
    let c0 = gwd_point(gwd, p.center);
    let c = Vec3::new(c0.x + a.x * hh, c0.y + a.y * hh, c0.z + a.z * hh);
    let h2 = hh * 2.0;
    let dd = Vec3::new(h2 * a.x, h2 * a.y, h2 * a.z);
    side_quads(&fr, c, r, dd, plane, gwd, dp, dl);
    for sg in [1.0f32, -1.0] {
        let center = if sg > 0.0 {
            Vec3::new(
                p.axis.x * p.hh + p.center.x,
                p.axis.y * p.hh + p.center.y,
                p.axis.z * p.hh + p.center.z,
            )
        } else {
            Vec3::new(
                p.center.x - p.axis.x * p.hh,
                p.center.y - p.axis.y * p.hh,
                p.center.z - p.axis.z * p.hh,
            )
        };
        let sp = SpherePrim { center, r: p.r };
        let (mut sdp, mut sdl) = (Vec3::ZERO, Vec3::ZERO);
        sphere_drag_prim(&sp, plane, gwd, &mut sdp, &mut sdl);
        *dp = Vec3::new(sdp.x + dp.x, sdp.y + dp.y, sdp.z + dp.z);
        *dl = Vec3::new(sdl.x + dl.x, sdl.y + dl.y, sdl.z + dl.z);
    }
}
