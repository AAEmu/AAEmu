//! Quaternions, stored `(x, y, z, w)` with `w` last, exactly as in the binary.
//!
//! * The product is the SSE shuffle sequence the engine inlines everywhere
//!   (body integration 0x3509E180, part poses in ray tracing 0x351AC640):
//!   `((m1 + m2) ^ signmask_w) − m3 + m4` per lane. [`Quat::product`] reproduces that order.
//!   `lhs * rhs` is the Hamilton product `lhs ⊗ rhs` (rotation `rhs` first, then `lhs`).
//! * Rotation of a vector is `sub_35022240` ([`Quat::rotate`]).
//! * Normalisation in body integration sums `((x² + w²) + y²) + z²`
//!   ([`Quat::normalized_xwyz`]).
//! * Matrix ↔ quaternion conversions live in [`crate::math::Mat33`] / [`Quat::from_mat33`].

use super::{Mat33, Vec3};
use std::ops::Mul;

/// Unit quaternion `(x, y, z, w)` (16 bytes, `#[repr(C)]`).
#[derive(Clone, Copy, Debug, PartialEq)]
#[repr(C)]
pub struct Quat {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub w: f32,
}

impl Default for Quat {
    fn default() -> Self {
        Self::IDENTITY
    }
}

impl Quat {
    pub const IDENTITY: Quat = Quat {
        x: 0.0,
        y: 0.0,
        z: 0.0,
        w: 1.0,
    };

    #[inline]
    #[must_use]
    pub const fn new(x: f32, y: f32, z: f32, w: f32) -> Self {
        Self { x, y, z, w }
    }

    #[inline]
    #[must_use]
    pub const fn from_array(a: [f32; 4]) -> Self {
        Self {
            x: a[0],
            y: a[1],
            z: a[2],
            w: a[3],
        }
    }

    #[inline]
    #[must_use]
    pub const fn to_array(self) -> [f32; 4] {
        [self.x, self.y, self.z, self.w]
    }

    #[inline]
    #[must_use]
    pub fn from_bits(b: [u32; 4]) -> Self {
        Self::new(
            f32::from_bits(b[0]),
            f32::from_bits(b[1]),
            f32::from_bits(b[2]),
            f32::from_bits(b[3]),
        )
    }

    #[inline]
    #[must_use]
    pub fn to_bits(self) -> [u32; 4] {
        [
            self.x.to_bits(),
            self.y.to_bits(),
            self.z.to_bits(),
            self.w.to_bits(),
        ]
    }

    /// Vector part.
    #[inline]
    #[must_use]
    pub const fn v(self) -> Vec3 {
        Vec3 {
            x: self.x,
            y: self.y,
            z: self.z,
        }
    }

    /// `(v, w)`.
    #[inline]
    #[must_use]
    pub const fn from_vw(v: Vec3, w: f32) -> Self {
        Self {
            x: v.x,
            y: v.y,
            z: v.z,
            w,
        }
    }

    /// Conjugate (inverse for a unit quaternion): `(-x, -y, -z, w)`.
    #[inline]
    #[must_use]
    pub fn conj(self) -> Quat {
        Quat::new(-self.x, -self.y, -self.z, self.w)
    }

    /// Hamilton product `self ⊗ rhs`, evaluated in the binary's lane order.
    ///
    /// With `a = rhs` (the register that gets shuffled with 0xBF/0x64/0x09/0xD2) and
    /// `b = self` (shuffled with 0xA4/0x7F/0x12/0xC9):
    /// ```text
    /// x = ((a.w·b.x + a.x·b.w) − a.y·b.z) + a.z·b.y
    /// y = ((a.w·b.y + a.y·b.w) − a.z·b.x) + a.x·b.z
    /// z = ((a.w·b.z + a.z·b.w) − a.x·b.y) + a.y·b.x
    /// w = (−(a.z·b.z + a.y·b.y) − a.x·b.x) + a.w·b.w
    /// ```
    #[inline]
    #[must_use]
    pub fn product(self, rhs: Quat) -> Quat {
        let (a, b) = (rhs, self);
        Quat {
            x: ((a.w * b.x + a.x * b.w) - a.y * b.z) + a.z * b.y,
            y: ((a.w * b.y + a.y * b.w) - a.z * b.x) + a.x * b.z,
            z: ((a.w * b.z + a.z * b.w) - a.x * b.y) + a.y * b.x,
            w: (-(a.z * b.z + a.y * b.y) - a.x * b.x) + a.w * b.w,
        }
    }

    /// Rotate `v` by this quaternion: `sub_35022240`.
    /// ```text
    /// t  = (v·w) + (v × q)        written per component as (a·b − c·d) + e·f
    /// u  = q.xyz × t
    /// v' = (u + v) + u
    /// ```
    #[inline]
    #[must_use]
    pub fn rotate(self, v: Vec3) -> Vec3 {
        let q = self;
        let tx = (v.z * q.y - v.y * q.z) + v.x * q.w;
        let ty = (v.x * q.z - q.x * v.z) + q.w * v.y;
        let tz = (q.x * v.y - v.x * q.y) + q.w * v.z;
        let ux = q.y * tz - q.z * ty;
        let uy = q.z * tx - q.x * tz;
        let uz = q.x * ty - q.y * tx;
        Vec3::new((ux + v.x) + ux, (uy + v.y) + uy, (uz + v.z) + uz)
    }

    /// Squared norm summed as `((x² + w²) + y²) + z²` (the order of body integration).
    #[inline]
    #[must_use]
    pub fn len2_xwyz(self) -> f32 {
        ((self.x * self.x + self.w * self.w) + self.y * self.y) + self.z * self.z
    }

    /// `q · (1 / sqrt(((x² + w²) + y²) + z²))`, the renormalisation at the end of body Step.
    #[inline]
    #[must_use]
    pub fn normalized_xwyz(self) -> Quat {
        let inv = 1.0 / self.len2_xwyz().sqrt();
        Quat::new(self.x * inv, self.y * inv, self.z * inv, self.w * inv)
    }

    /// Rotation matrix (row-major), [`Mat33::from_quat`].
    #[inline]
    #[must_use]
    pub fn to_mat33(self) -> Mat33 {
        Mat33::from_quat(self)
    }

    /// Quaternion from a row-major rotation matrix: `sub_35023EB0` (single precision,
    /// four trace branches). Used e.g. by `CBoxGeom::CalcPhysicalProperties` on `Basisᵀ`.
    #[must_use]
    pub fn from_mat33(m: &Mat33) -> Quat {
        let a = m.to_array();
        let (m00, m11, m22) = (a[0], a[4], a[8]);
        let tr = (m00 + m11) + m22;
        if tr > 0.0 {
            let s = (tr + 1.0).sqrt();
            let k = 0.5 / s;
            return Quat::new(
                (a[7] - a[5]) * k,
                (a[2] - a[6]) * k,
                (a[3] - a[1]) * k,
                s * 0.5,
            );
        }
        if !(m00 < m11 || m00 < m22) {
            let s = ((m00 - m11) - m22 + 1.0).sqrt();
            let k = 0.5 / s;
            return Quat::new(
                s * 0.5,
                (a[3] + a[1]) * k,
                (a[6] + a[2]) * k,
                (a[7] - a[5]) * k,
            );
        }
        if !(m11 < m00 || m11 < m22) {
            let s = (((m11 - m22) - m00) + 1.0).sqrt();
            let k = 0.5 / s;
            return Quat::new(
                (a[3] + a[1]) * k,
                s * 0.5,
                (a[7] + a[5]) * k,
                (a[2] - a[6]) * k,
            );
        }
        if !(m22 < m00 || m22 < m11) {
            let s = (((m22 - m00) - m11) + 1.0).sqrt();
            let k = 0.5 / s;
            return Quat::new(
                (a[6] + a[2]) * k,
                (a[7] + a[5]) * k,
                s * 0.5,
                (a[3] - a[1]) * k,
            );
        }
        // Only reachable with NaN on the diagonal.
        Quat::IDENTITY
    }
}

impl Mul for Quat {
    type Output = Quat;
    /// `self ⊗ rhs` in the binary's evaluation order (see [`Quat::product`]).
    #[inline]
    fn mul(self, rhs: Quat) -> Quat {
        Quat::product(self, rhs)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn identity_and_conj() {
        let q = Quat::new(0.1, -0.2, 0.3, 0.9).normalized_xwyz();
        assert_eq!(Quat::IDENTITY * q, q);
        assert_eq!(q * Quat::IDENTITY, q);
        let r = q * q.conj();
        assert!((r.w - 1.0).abs() < 1e-6 && r.v().len() < 1e-6);
    }

    #[test]
    fn hamilton_convention() {
        // 90 deg about z, then 90 deg about x: (qx ⊗ qz) applied to +x gives +z.
        let h = std::f32::consts::FRAC_1_SQRT_2;
        let qz = Quat::new(0.0, 0.0, h, h);
        let qx = Quat::new(h, 0.0, 0.0, h);
        let v = (qx * qz).rotate(Vec3::X);
        assert!((v - Vec3::Z).len() < 1e-6, "{v:?}");
        assert!((qz.rotate(Vec3::X) - Vec3::Y).len() < 1e-6);
    }

    #[test]
    fn rotate_matches_matrix() {
        let q = Quat::new(0.3, -0.5, 0.2, 0.7).normalized_xwyz();
        let v = Vec3::new(1.5, -2.0, 0.25);
        let a = q.rotate(v);
        let b = q.to_mat33() * v;
        assert!((a - b).len() < 1e-5);
    }

    #[test]
    fn mat_round_trip_all_branches() {
        for q in [
            Quat::new(0.1, 0.2, 0.3, 0.9),
            Quat::new(0.9, 0.2, 0.1, 0.05),
            Quat::new(0.1, 0.9, 0.2, 0.05),
            Quat::new(0.1, 0.2, 0.9, 0.05),
        ] {
            let q = q.normalized_xwyz();
            let r = Quat::from_mat33(&q.to_mat33());
            let s = if r.w * q.w < 0.0 { -1.0 } else { 1.0 };
            for (a, b) in r.to_array().iter().zip(q.to_array()) {
                assert!((a * s - b).abs() < 1e-5, "{r:?} vs {q:?}");
            }
        }
    }
}
