//! P0 math: the engine's vector, matrix and quaternion types and its scalar idioms.
//!
//! Everything here is `f32` unless the type says otherwise, mirrors the evaluation order of the
//! x64 10.x binary, and is checked bit-for-bit against routines called inside the real DLL
//! (`tests/math_oracle.rs`, goldens `tests/golden/math_*.trace`).

pub mod crt;
pub mod eigen;
pub mod mat33;
pub mod quat;
pub mod quotient;
pub mod scalar;
pub mod vec3;

pub use mat33::Mat33;
pub use quat::Quat;
pub use quotient::Quotient;
pub use scalar::*;
pub use vec3::{Vec3, Vec3d};

/// Axis-aligned box `{min, max}` (the entity `BBox[2]` and geometry `GetBBox` results).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
#[repr(C)]
pub struct Aabb {
    pub min: Vec3,
    pub max: Vec3,
}

impl Aabb {
    #[inline]
    #[must_use]
    pub const fn new(min: Vec3, max: Vec3) -> Self {
        Self { min, max }
    }

    /// `(min + max) * 0.5`.
    #[inline]
    #[must_use]
    pub fn center(&self) -> Vec3 {
        (self.min + self.max) * 0.5
    }

    /// `(max - min) * 0.5`.
    #[inline]
    #[must_use]
    pub fn half_size(&self) -> Vec3 {
        (self.max - self.min) * 0.5
    }

    /// Inclusive overlap test on all three axes.
    #[inline]
    #[must_use]
    pub fn overlaps(&self, b: &Aabb) -> bool {
        self.min.x <= b.max.x
            && b.min.x <= self.max.x
            && self.min.y <= b.max.y
            && b.min.y <= self.max.y
            && self.min.z <= b.max.z
            && b.min.z <= self.max.z
    }
}
