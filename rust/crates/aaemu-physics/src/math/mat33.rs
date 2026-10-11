//! Row-major 3×3 matrix (`Matrix33`, 36 bytes).
//!
//! * [`Mat33::from_quat`] is `sub_350247B0`: doubled components in single precision, the
//!   products and `1 −` terms in **double**, each element rounded once to single.
//! * [`Mat33::sandwich_diag`] is the `R·diag(d)·Rᵀ` of the rigid body's `UpdateState`
//!   (0x3509CE40) with its exact (non-symmetric!) rounding: element `[i][j] =
//!   ((d0·R[i][0])·R[j][0] + (d1·R[i][1])·R[j][1]) + (d2·R[i][2])·R[j][2]`, so `[i][j]` and
//!   `[j][i]` can differ in the last bit.
//! * Matrix × vector is `(m[i][0]·v.x + m[i][1]·v.y) + m[i][2]·v.z`.

use super::{Quat, Vec3};
use std::ops::{Index, IndexMut, Mul};

/// Row-major 3×3 matrix; `m[r][c]`.
#[derive(Clone, Copy, Debug, PartialEq)]
#[repr(C)]
pub struct Mat33 {
    pub m: [[f32; 3]; 3],
}

impl Default for Mat33 {
    fn default() -> Self {
        Self::IDENTITY
    }
}

impl Mat33 {
    pub const IDENTITY: Mat33 = Mat33 {
        m: [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]],
    };
    pub const ZERO: Mat33 = Mat33 { m: [[0.0; 3]; 3] };

    #[inline]
    #[must_use]
    pub const fn from_rows(r0: Vec3, r1: Vec3, r2: Vec3) -> Self {
        Self {
            m: [[r0.x, r0.y, r0.z], [r1.x, r1.y, r1.z], [r2.x, r2.y, r2.z]],
        }
    }

    /// From 9 floats in memory order (row-major).
    #[inline]
    #[must_use]
    pub const fn from_array(a: [f32; 9]) -> Self {
        Self {
            m: [[a[0], a[1], a[2]], [a[3], a[4], a[5]], [a[6], a[7], a[8]]],
        }
    }

    /// 9 floats in memory order (row-major).
    #[inline]
    #[must_use]
    pub const fn to_array(&self) -> [f32; 9] {
        let m = &self.m;
        [
            m[0][0], m[0][1], m[0][2], m[1][0], m[1][1], m[1][2], m[2][0], m[2][1], m[2][2],
        ]
    }

    #[inline]
    #[must_use]
    pub fn from_bits(b: [u32; 9]) -> Self {
        Self::from_array(b.map(f32::from_bits))
    }

    #[inline]
    #[must_use]
    pub fn to_bits(&self) -> [u32; 9] {
        self.to_array().map(f32::to_bits)
    }

    #[inline]
    #[must_use]
    pub const fn row(&self, i: usize) -> Vec3 {
        Vec3 {
            x: self.m[i][0],
            y: self.m[i][1],
            z: self.m[i][2],
        }
    }

    #[inline]
    #[must_use]
    pub const fn col(&self, j: usize) -> Vec3 {
        Vec3 {
            x: self.m[0][j],
            y: self.m[1][j],
            z: self.m[2][j],
        }
    }

    #[inline]
    #[must_use]
    pub const fn diag(d: Vec3) -> Self {
        Self {
            m: [[d.x, 0.0, 0.0], [0.0, d.y, 0.0], [0.0, 0.0, d.z]],
        }
    }

    #[inline]
    #[must_use]
    pub fn transpose(&self) -> Mat33 {
        let m = &self.m;
        Mat33 {
            m: [
                [m[0][0], m[1][0], m[2][0]],
                [m[0][1], m[1][1], m[2][1]],
                [m[0][2], m[1][2], m[2][2]],
            ],
        }
    }

    /// Rotation matrix of a quaternion, bit-exact port of `sub_350247B0`.
    #[must_use]
    pub fn from_quat(q: Quat) -> Mat33 {
        let x2 = f64::from(q.x * 2.0);
        let y2 = f64::from(q.y * 2.0);
        let z2 = f64::from(q.z * 2.0);
        let (x, y, z, w) = (
            f64::from(q.x),
            f64::from(q.y),
            f64::from(q.z),
            f64::from(q.w),
        );
        let yy = y * y2;
        let xx = x * x2;
        let zz = z * z2;
        let xy = x * y2;
        let xz = x * z2;
        let yz = y * z2;
        let wx = w * x2;
        let wy = w * y2;
        let wz = w * z2;
        let one_xx = 1.0 - xx;
        Mat33 {
            m: [
                [((1.0 - yy) - zz) as f32, (xy - wz) as f32, (xz + wy) as f32],
                [(wz + xy) as f32, (one_xx - zz) as f32, (yz - wx) as f32],
                [(xz - wy) as f32, (yz + wx) as f32, (one_xx - yy) as f32],
            ],
        }
    }

    /// `R · diag(d) · Rᵀ` in `UpdateState` order (see the module docs).
    #[must_use]
    pub fn sandwich_diag(r: &Mat33, d: Vec3) -> Mat33 {
        let mut out = Mat33::ZERO;
        let m = &r.m;
        for i in 0..3 {
            for j in 0..3 {
                out.m[i][j] = ((d.x * m[i][0]) * m[j][0] + (d.y * m[i][1]) * m[j][1])
                    + (d.z * m[i][2]) * m[j][2];
            }
        }
        out
    }

    /// `self · v`: `(m[i][0]·v.x + m[i][1]·v.y) + m[i][2]·v.z`.
    #[inline]
    #[must_use]
    pub fn mul_vec(&self, v: Vec3) -> Vec3 {
        let m = &self.m;
        Vec3::new(
            (m[0][0] * v.x + m[0][1] * v.y) + m[0][2] * v.z,
            (m[1][0] * v.x + m[1][1] * v.y) + m[1][2] * v.z,
            (m[2][0] * v.x + m[2][1] * v.y) + m[2][2] * v.z,
        )
    }

    /// `selfᵀ · v` (columns dotted with `v`): `(m[0][j]·v.x + m[1][j]·v.y) + m[2][j]·v.z`.
    #[inline]
    #[must_use]
    pub fn t_mul_vec(&self, v: Vec3) -> Vec3 {
        let m = &self.m;
        Vec3::new(
            (m[0][0] * v.x + m[1][0] * v.y) + m[2][0] * v.z,
            (m[0][1] * v.x + m[1][1] * v.y) + m[2][1] * v.z,
            (m[0][2] * v.x + m[1][2] * v.y) + m[2][2] * v.z,
        )
    }

    /// `self · b`, element `[i][j] = (a[i][0]·b[0][j] + a[i][1]·b[1][j]) + a[i][2]·b[2][j]`.
    #[must_use]
    pub fn mul_mat(&self, b: &Mat33) -> Mat33 {
        let mut out = Mat33::ZERO;
        for i in 0..3 {
            for j in 0..3 {
                out.m[i][j] = (self.m[i][0] * b.m[0][j] + self.m[i][1] * b.m[1][j])
                    + self.m[i][2] * b.m[2][j];
            }
        }
        out
    }

    /// True when exactly the identity (`IsIdentity` in `CBoxGeom::SetData`, sub_3513A010, sets
    /// `bOriented = !IsIdentity(Basis)`).
    #[inline]
    #[must_use]
    pub fn is_identity(&self) -> bool {
        *self == Mat33::IDENTITY
    }
}

impl Mul<Vec3> for Mat33 {
    type Output = Vec3;
    #[inline]
    fn mul(self, v: Vec3) -> Vec3 {
        self.mul_vec(v)
    }
}

impl Mul for Mat33 {
    type Output = Mat33;
    #[inline]
    fn mul(self, b: Mat33) -> Mat33 {
        self.mul_mat(&b)
    }
}

impl Index<usize> for Mat33 {
    type Output = [f32; 3];
    #[inline]
    fn index(&self, i: usize) -> &[f32; 3] {
        &self.m[i]
    }
}

impl IndexMut<usize> for Mat33 {
    #[inline]
    fn index_mut(&mut self, i: usize) -> &mut [f32; 3] {
        &mut self.m[i]
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn identity_quat() {
        assert_eq!(Mat33::from_quat(Quat::IDENTITY), Mat33::IDENTITY);
        assert!(Mat33::IDENTITY.is_identity());
    }

    #[test]
    fn ops() {
        let a = Mat33::from_array([1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 10.0]);
        let v = Vec3::new(1.0, -1.0, 2.0);
        assert_eq!(a * v, Vec3::new(5.0, 11.0, 19.0));
        assert_eq!(a.t_mul_vec(v), a.transpose() * v);
        assert_eq!(a * Mat33::IDENTITY, a);
        assert_eq!(a.row(1), Vec3::new(4.0, 5.0, 6.0));
        assert_eq!(a.col(2), Vec3::new(3.0, 6.0, 10.0));
        assert_eq!(Mat33::from_bits(a.to_bits()), a);
        let d = Vec3::new(2.0, 3.0, 4.0);
        assert_eq!(Mat33::sandwich_diag(&Mat33::IDENTITY, d), Mat33::diag(d));
    }

    #[test]
    fn sandwich_is_r_d_rt() {
        let r = Mat33::from_quat(Quat::new(0.2, -0.4, 0.1, 0.88).normalized_xwyz());
        let d = Vec3::new(0.5, 2.0, 7.0);
        let s = Mat33::sandwich_diag(&r, d);
        let t = r * Mat33::diag(d) * r.transpose();
        for i in 0..3 {
            for j in 0..3 {
                assert!((s[i][j] - t[i][j]).abs() < 1e-5);
            }
        }
    }
}
