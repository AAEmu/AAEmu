//! 3-component vectors in single ([`Vec3`]) and double ([`Vec3d`]) precision.
//!
//! Operation order follows the binary's scalar SSE code: `dot = (x·x' + y·y') + z·z'`
//! (the first addition is commutative, so either operand order of the decompile is the same),
//! `cross` component-wise `a·b − c·d`, scalar products per component. Normalisation is
//! `v · (1 / sqrt(|v|²))` (`sqrtss` + `divss` + `mulss`, never `rsqrtss`: the binary has none).

use std::ops::{Add, AddAssign, Div, Index, IndexMut, Mul, MulAssign, Neg, Sub, SubAssign};

/// Single-precision 3D vector (`Vec3` in the engine; 12 bytes, `#[repr(C)]`).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
#[repr(C)]
pub struct Vec3 {
    pub x: f32,
    pub y: f32,
    pub z: f32,
}

impl Vec3 {
    pub const ZERO: Vec3 = Vec3 {
        x: 0.0,
        y: 0.0,
        z: 0.0,
    };
    pub const X: Vec3 = Vec3 {
        x: 1.0,
        y: 0.0,
        z: 0.0,
    };
    pub const Y: Vec3 = Vec3 {
        x: 0.0,
        y: 1.0,
        z: 0.0,
    };
    pub const Z: Vec3 = Vec3 {
        x: 0.0,
        y: 0.0,
        z: 1.0,
    };

    #[inline]
    #[must_use]
    pub const fn new(x: f32, y: f32, z: f32) -> Self {
        Self { x, y, z }
    }

    #[inline]
    #[must_use]
    pub const fn splat(v: f32) -> Self {
        Self { x: v, y: v, z: v }
    }

    #[inline]
    #[must_use]
    pub const fn from_array(a: [f32; 3]) -> Self {
        Self {
            x: a[0],
            y: a[1],
            z: a[2],
        }
    }

    #[inline]
    #[must_use]
    pub const fn to_array(self) -> [f32; 3] {
        [self.x, self.y, self.z]
    }

    /// From raw IEEE bits (oracle traces).
    #[inline]
    #[must_use]
    pub fn from_bits(b: [u32; 3]) -> Self {
        Self::new(
            f32::from_bits(b[0]),
            f32::from_bits(b[1]),
            f32::from_bits(b[2]),
        )
    }

    #[inline]
    #[must_use]
    pub fn to_bits(self) -> [u32; 3] {
        [self.x.to_bits(), self.y.to_bits(), self.z.to_bits()]
    }

    /// `(x·b.x + y·b.y) + z·b.z`.
    #[inline]
    #[must_use]
    pub fn dot(self, b: Vec3) -> f32 {
        (self.x * b.x + self.y * b.y) + self.z * b.z
    }

    /// `self × b`.
    #[inline]
    #[must_use]
    pub fn cross(self, b: Vec3) -> Vec3 {
        Vec3::new(
            self.y * b.z - self.z * b.y,
            self.z * b.x - self.x * b.z,
            self.x * b.y - self.y * b.x,
        )
    }

    /// `|v|²` as `(x² + y²) + z²`.
    #[inline]
    #[must_use]
    pub fn len2(self) -> f32 {
        self.dot(self)
    }

    /// `sqrtss(|v|²)`.
    #[inline]
    #[must_use]
    pub fn len(self) -> f32 {
        self.len2().sqrt()
    }

    /// `v · (1 / |v|)`. Zero or non-finite input gives non-finite output, as in the engine;
    /// use [`Vec3::normalized_or`] where the binary guards the length.
    #[inline]
    #[must_use]
    pub fn normalized(self) -> Vec3 {
        self * (1.0 / self.len())
    }

    /// [`Vec3::normalized`], or `fallback` when `|v|² == 0` (e.g. sph–sph contacts default to +z).
    #[inline]
    #[must_use]
    pub fn normalized_or(self, fallback: Vec3) -> Vec3 {
        let l2 = self.len2();
        if l2 > 0.0 {
            self * (1.0 / l2.sqrt())
        } else {
            fallback
        }
    }

    /// Component-wise absolute value.
    #[inline]
    #[must_use]
    pub fn abs(self) -> Vec3 {
        Vec3::new(self.x.abs(), self.y.abs(), self.z.abs())
    }

    /// Component-wise product.
    #[inline]
    #[must_use]
    pub fn mul_comp(self, b: Vec3) -> Vec3 {
        Vec3::new(self.x * b.x, self.y * b.y, self.z * b.z)
    }

    /// Widen to double (`cvtps2pd`).
    #[inline]
    #[must_use]
    pub fn to_f64(self) -> Vec3d {
        Vec3d::new(f64::from(self.x), f64::from(self.y), f64::from(self.z))
    }

    /// True when every component is finite.
    #[inline]
    #[must_use]
    pub fn is_finite(self) -> bool {
        self.x.is_finite() && self.y.is_finite() && self.z.is_finite()
    }
}

impl Add for Vec3 {
    type Output = Vec3;
    #[inline]
    fn add(self, b: Vec3) -> Vec3 {
        Vec3::new(self.x + b.x, self.y + b.y, self.z + b.z)
    }
}
impl Sub for Vec3 {
    type Output = Vec3;
    #[inline]
    fn sub(self, b: Vec3) -> Vec3 {
        Vec3::new(self.x - b.x, self.y - b.y, self.z - b.z)
    }
}
impl Neg for Vec3 {
    type Output = Vec3;
    #[inline]
    fn neg(self) -> Vec3 {
        Vec3::new(-self.x, -self.y, -self.z)
    }
}
impl Mul<f32> for Vec3 {
    type Output = Vec3;
    #[inline]
    fn mul(self, s: f32) -> Vec3 {
        Vec3::new(self.x * s, self.y * s, self.z * s)
    }
}
impl Mul<Vec3> for f32 {
    type Output = Vec3;
    #[inline]
    fn mul(self, v: Vec3) -> Vec3 {
        Vec3::new(self * v.x, self * v.y, self * v.z)
    }
}
/// Division divides each component (`divss` ×3). Many engine sites instead multiply by a
/// precomputed reciprocal; use `v * (1.0 / s)` there.
impl Div<f32> for Vec3 {
    type Output = Vec3;
    #[inline]
    fn div(self, s: f32) -> Vec3 {
        Vec3::new(self.x / s, self.y / s, self.z / s)
    }
}
impl AddAssign for Vec3 {
    #[inline]
    fn add_assign(&mut self, b: Vec3) {
        *self = *self + b;
    }
}
impl SubAssign for Vec3 {
    #[inline]
    fn sub_assign(&mut self, b: Vec3) {
        *self = *self - b;
    }
}
impl MulAssign<f32> for Vec3 {
    #[inline]
    fn mul_assign(&mut self, s: f32) {
        *self = *self * s;
    }
}
impl Index<usize> for Vec3 {
    type Output = f32;
    #[inline]
    fn index(&self, i: usize) -> &f32 {
        match i {
            0 => &self.x,
            1 => &self.y,
            2 => &self.z,
            _ => panic!("Vec3 index {i} out of range"),
        }
    }
}
impl IndexMut<usize> for Vec3 {
    #[inline]
    fn index_mut(&mut self, i: usize) -> &mut f32 {
        match i {
            0 => &mut self.x,
            1 => &mut self.y,
            2 => &mut self.z,
            _ => panic!("Vec3 index {i} out of range"),
        }
    }
}

/// Double-precision 3D vector. The binary keeps a few quantities in double (contact depth in
/// the per-thread contact table, rope segment positions) and promotes many intermediate products.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
#[repr(C)]
pub struct Vec3d {
    pub x: f64,
    pub y: f64,
    pub z: f64,
}

impl Vec3d {
    pub const ZERO: Vec3d = Vec3d {
        x: 0.0,
        y: 0.0,
        z: 0.0,
    };

    #[inline]
    #[must_use]
    pub const fn new(x: f64, y: f64, z: f64) -> Self {
        Self { x, y, z }
    }

    /// `(x·b.x + y·b.y) + z·b.z`.
    #[inline]
    #[must_use]
    pub fn dot(self, b: Vec3d) -> f64 {
        (self.x * b.x + self.y * b.y) + self.z * b.z
    }

    #[inline]
    #[must_use]
    pub fn cross(self, b: Vec3d) -> Vec3d {
        Vec3d::new(
            self.y * b.z - self.z * b.y,
            self.z * b.x - self.x * b.z,
            self.x * b.y - self.y * b.x,
        )
    }

    #[inline]
    #[must_use]
    pub fn len2(self) -> f64 {
        self.dot(self)
    }

    /// Narrow to single (`cvtsd2ss`, round to nearest even).
    #[inline]
    #[must_use]
    pub fn to_f32(self) -> Vec3 {
        Vec3::new(self.x as f32, self.y as f32, self.z as f32)
    }
}

impl Add for Vec3d {
    type Output = Vec3d;
    #[inline]
    fn add(self, b: Vec3d) -> Vec3d {
        Vec3d::new(self.x + b.x, self.y + b.y, self.z + b.z)
    }
}
impl Sub for Vec3d {
    type Output = Vec3d;
    #[inline]
    fn sub(self, b: Vec3d) -> Vec3d {
        Vec3d::new(self.x - b.x, self.y - b.y, self.z - b.z)
    }
}
impl Neg for Vec3d {
    type Output = Vec3d;
    #[inline]
    fn neg(self) -> Vec3d {
        Vec3d::new(-self.x, -self.y, -self.z)
    }
}
impl Mul<f64> for Vec3d {
    type Output = Vec3d;
    #[inline]
    fn mul(self, s: f64) -> Vec3d {
        Vec3d::new(self.x * s, self.y * s, self.z * s)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn basic_ops() {
        let a = Vec3::new(1.0, 2.0, 3.0);
        let b = Vec3::new(-4.0, 0.5, 2.0);
        assert_eq!(a + b, Vec3::new(-3.0, 2.5, 5.0));
        assert_eq!(a - b, Vec3::new(5.0, 1.5, 1.0));
        assert_eq!(-a, Vec3::new(-1.0, -2.0, -3.0));
        assert_eq!(a * 2.0, Vec3::new(2.0, 4.0, 6.0));
        assert_eq!(2.0 * a, a * 2.0);
        assert_eq!(a.dot(b), 3.0);
        assert_eq!(a.cross(b), Vec3::new(2.5, -14.0, 8.5));
        assert_eq!(Vec3::X.cross(Vec3::Y), Vec3::Z);
        assert_eq!(a.len2(), 14.0);
        assert_eq!(a[2], 3.0);
        assert_eq!(Vec3::from_bits(a.to_bits()), a);
        assert_eq!(a.mul_comp(b), Vec3::new(-4.0, 1.0, 6.0));
    }

    #[test]
    fn dot_order_is_x_y_then_z() {
        // (x + y) + z differs from x + (y + z) here; the engine adds x and y first.
        let a = Vec3::new(1.0e8, -1.0e8, 1.0);
        let one = Vec3::splat(1.0);
        assert_eq!(a.dot(one), 1.0);
        assert_eq!(1.0e8f32 + (-1.0e8f32 + 1.0), 0.0);
    }

    #[test]
    fn normalisation_uses_reciprocal_then_multiply() {
        let v = Vec3::new(3.0, 4.0, 12.0);
        let n = v.normalized();
        let inv = 1.0f32 / 13.0;
        assert_eq!(n, Vec3::new(3.0 * inv, 4.0 * inv, 12.0 * inv));
        assert_eq!(Vec3::ZERO.normalized_or(Vec3::Z), Vec3::Z);
    }

    #[test]
    fn double_round_trip() {
        let a = Vec3::new(0.1, 0.2, 0.3);
        assert_eq!(a.to_f64().to_f32(), a);
        let d = Vec3d::new(1.0, 2.0, 3.0);
        assert_eq!(
            d.cross(Vec3d::new(0.0, 0.0, 1.0)),
            Vec3d::new(2.0, -1.0, 0.0)
        );
        assert_eq!(d.len2(), 14.0);
    }
}
