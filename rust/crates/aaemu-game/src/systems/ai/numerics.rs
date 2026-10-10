//! The parts of `System.Numerics` (`Vector3`, `Quaternion`, `Matrix4x4`) the AI, gimmick and
//! physics code use, with the .NET formulas (single precision) so results match the C#.

use std::ops::{Add, AddAssign, Div, Mul, MulAssign, Neg, Sub, SubAssign};

use crate::systems::skills::controllers::Vec3;

impl Add for Vec3 {
    type Output = Vec3;
    fn add(self, o: Vec3) -> Vec3 {
        Vec3::new(self.x + o.x, self.y + o.y, self.z + o.z)
    }
}

impl Sub for Vec3 {
    type Output = Vec3;
    fn sub(self, o: Vec3) -> Vec3 {
        Vec3::new(self.x - o.x, self.y - o.y, self.z - o.z)
    }
}

impl Mul<f32> for Vec3 {
    type Output = Vec3;
    fn mul(self, s: f32) -> Vec3 {
        Vec3::new(self.x * s, self.y * s, self.z * s)
    }
}

impl Mul<Vec3> for f32 {
    type Output = Vec3;
    fn mul(self, v: Vec3) -> Vec3 {
        v * self
    }
}

impl Div<f32> for Vec3 {
    type Output = Vec3;
    fn div(self, s: f32) -> Vec3 {
        Vec3::new(self.x / s, self.y / s, self.z / s)
    }
}

impl Neg for Vec3 {
    type Output = Vec3;
    fn neg(self) -> Vec3 {
        Vec3::new(-self.x, -self.y, -self.z)
    }
}

impl AddAssign for Vec3 {
    fn add_assign(&mut self, o: Vec3) {
        *self = *self + o;
    }
}

impl SubAssign for Vec3 {
    fn sub_assign(&mut self, o: Vec3) {
        *self = *self - o;
    }
}

impl MulAssign<f32> for Vec3 {
    fn mul_assign(&mut self, s: f32) {
        *self = *self * s;
    }
}

/// `Vector3` helpers.
pub trait Vec3Ext {
    fn dot(self, o: Vec3) -> f32;
    fn cross(self, o: Vec3) -> Vec3;
    fn length(self) -> f32;
    fn length_squared(self) -> f32;
    /// `Vector3.Normalize` (`v / v.Length()`, NaN for a zero vector like .NET).
    fn normalized(self) -> Vec3;
    fn is_zero_vec(&self) -> bool;
    fn distance(self, o: Vec3) -> f32;
    fn distance_squared(self, o: Vec3) -> f32;
}

impl Vec3Ext for Vec3 {
    fn dot(self, o: Vec3) -> f32 {
        self.x * o.x + self.y * o.y + self.z * o.z
    }

    fn cross(self, o: Vec3) -> Vec3 {
        Vec3::new(
            self.y * o.z - self.z * o.y,
            self.z * o.x - self.x * o.z,
            self.x * o.y - self.y * o.x,
        )
    }

    fn length(self) -> f32 {
        self.length_squared().sqrt()
    }

    fn length_squared(self) -> f32 {
        self.dot(self)
    }

    fn normalized(self) -> Vec3 {
        self / self.length()
    }

    fn is_zero_vec(&self) -> bool {
        self.x == 0.0 && self.y == 0.0 && self.z == 0.0
    }

    fn distance(self, o: Vec3) -> f32 {
        (self - o).length()
    }

    fn distance_squared(self, o: Vec3) -> f32 {
        (self - o).length_squared()
    }
}

/// `System.Numerics.Quaternion`.
#[derive(Debug, Clone, Copy, PartialEq)]
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

    pub const fn new(x: f32, y: f32, z: f32, w: f32) -> Self {
        Self { x, y, z, w }
    }

    /// `Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll)` (yaw about Y, pitch about X, roll
    /// about Z).
    pub fn from_yaw_pitch_roll(yaw: f32, pitch: f32, roll: f32) -> Self {
        let (sr, cr) = (roll * 0.5).sin_cos();
        let (sp, cp) = (pitch * 0.5).sin_cos();
        let (sy, cy) = (yaw * 0.5).sin_cos();
        Self {
            x: cy * sp * cr + sy * cp * sr,
            y: sy * cp * cr - cy * sp * sr,
            z: cy * cp * sr - sy * sp * cr,
            w: cy * cp * cr + sy * sp * sr,
        }
    }

    /// `Quaternion.CreateFromAxisAngle(axis, angle)`.
    pub fn from_axis_angle(axis: Vec3, angle: f32) -> Self {
        let (s, c) = (angle * 0.5).sin_cos();
        Self::new(axis.x * s, axis.y * s, axis.z * s, c)
    }

    pub fn length(self) -> f32 {
        (self.x * self.x + self.y * self.y + self.z * self.z + self.w * self.w).sqrt()
    }

    /// `Quaternion.Normalize`.
    pub fn normalized(self) -> Self {
        let inv = 1.0 / self.length();
        Self::new(self.x * inv, self.y * inv, self.z * inv, self.w * inv)
    }

    /// `Quaternion.Multiply(a, b)` / `a * b` (.NET concatenation order).
    pub fn multiply(self, b: Quat) -> Quat {
        let a = self;
        let (q1x, q1y, q1z, q1w) = (a.x, a.y, a.z, a.w);
        let (q2x, q2y, q2z, q2w) = (b.x, b.y, b.z, b.w);
        let cx = q1y * q2z - q1z * q2y;
        let cy = q1z * q2x - q1x * q2z;
        let cz = q1x * q2y - q1y * q2x;
        let dot = q1x * q2x + q1y * q2y + q1z * q2z;
        Quat::new(
            q1x * q2w + q2x * q1w + cx,
            q1y * q2w + q2y * q1w + cy,
            q1z * q2w + q2z * q1w + cz,
            q1w * q2w - dot,
        )
    }

    pub fn conjugate(self) -> Quat {
        Quat::new(-self.x, -self.y, -self.z, self.w)
    }

    /// `Vector3.Transform(v, q)`.
    pub fn rotate(self, v: Vec3) -> Vec3 {
        let x2 = self.x + self.x;
        let y2 = self.y + self.y;
        let z2 = self.z + self.z;
        let wx2 = self.w * x2;
        let wy2 = self.w * y2;
        let wz2 = self.w * z2;
        let xx2 = self.x * x2;
        let xy2 = self.x * y2;
        let xz2 = self.x * z2;
        let yy2 = self.y * y2;
        let yz2 = self.y * z2;
        let zz2 = self.z * z2;
        Vec3::new(
            v.x * (1.0 - yy2 - zz2) + v.y * (xy2 - wz2) + v.z * (xz2 + wy2),
            v.x * (xy2 + wz2) + v.y * (1.0 - xx2 - zz2) + v.z * (yz2 - wx2),
            v.x * (xz2 - wy2) + v.y * (yz2 + wx2) + v.z * (1.0 - xx2 - yy2),
        )
    }
}

/// `float.DegToRad()`.
pub fn deg_to_rad(v: f32) -> f32 {
    v * (std::f32::consts::PI / 180.0)
}

/// `float.RadToDeg()`.
pub fn rad_to_deg(v: f32) -> f32 {
    v * (180.0 / std::f32::consts::PI)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn close(a: f32, b: f32) -> bool {
        (a - b).abs() < 1e-5
    }

    #[test]
    fn yaw_pitch_roll_matches_dotnet() {
        // Quaternion.CreateFromYawPitchRoll(0.5, 0.25, -0.75) in .NET
        let q = Quat::from_yaw_pitch_roll(0.5, 0.25, -0.75);
        assert!(close(q.length(), 1.0));
        let r = q.rotate(Vec3::new(0.0, 0.0, 1.0));
        // rotating Z by yaw (around Y) first-order check: x component sin(yaw)*cos(pitch)
        assert!(close(r.x, 0.5f32.sin() * 0.25f32.cos()));
        let id = Quat::IDENTITY.rotate(Vec3::new(1.0, 2.0, 3.0));
        assert_eq!(id, Vec3::new(1.0, 2.0, 3.0));
        let a = Quat::from_axis_angle(Vec3::new(0.0, 1.0, 0.0), std::f32::consts::FRAC_PI_2);
        let v = a.rotate(Vec3::new(1.0, 0.0, 0.0));
        assert!(close(v.x, 0.0) && close(v.z, -1.0));
        let m = a.multiply(a.conjugate());
        assert!(close(m.w, 1.0));
    }
}
