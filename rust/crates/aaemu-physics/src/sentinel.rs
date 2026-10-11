//! The engine's "unused" sentinels for optional parameter fields.
//!
//! Every `pe_params_*` / `pe_action_*` / `pe_status_*` struct marks fields the caller did not set
//! with a sentinel, and every handler tests for it:
//!
//! * float (and vector / quaternion) fields: bits `0xFFBFFFFF` (a quiet NaN; Hex-Rays prints
//!   `NAN`), tested as `(bits & 0xFFA00000) == 0xFFA00000`. A vector is tested on `.x` only, a
//!   quaternion on `.w` only.
//! * int fields: `0x80000000` (`i32::MIN`).
//! * pointer fields: `-1` (some handlers also treat null as unset).
//!
//! In the engine API these become `Option`s; the `#[repr(C)]`-layout structs in
//! [`crate::params`] keep the raw sentinels so they can be byte-compared with the oracle.

use crate::math::{Quat, Vec3};

/// Bits written for an unused float.
pub const UNUSED_F32_BITS: u32 = 0xFFBF_FFFF;
/// Mask of the unused test: `(bits & UNUSED_MASK) == UNUSED_MASK`.
pub const UNUSED_MASK: u32 = 0xFFA0_0000;
/// The unused float value (a NaN; compare with [`is_unused`], never with `==`).
pub const UNUSED_F32: f32 = f32::from_bits(UNUSED_F32_BITS);
/// Unused int.
pub const UNUSED_I32: i32 = i32::MIN;
/// Unused vector (all three components carry the sentinel; only `.x` is tested).
pub const UNUSED_VEC3: Vec3 = Vec3 {
    x: UNUSED_F32,
    y: UNUSED_F32,
    z: UNUSED_F32,
};
/// Unused quaternion (all four components carry the sentinel; only `.w` is tested).
pub const UNUSED_QUAT: Quat = Quat {
    x: UNUSED_F32,
    y: UNUSED_F32,
    z: UNUSED_F32,
    w: UNUSED_F32,
};

/// The engine's float test.
#[inline]
#[must_use]
pub fn is_unused(x: f32) -> bool {
    x.to_bits() & UNUSED_MASK == UNUSED_MASK
}

/// A field that can carry the "unused" sentinel.
pub trait MaybeUnused: Copy {
    /// The sentinel value.
    const UNUSED: Self;
    /// The engine's test for this kind of field.
    fn is_unused(&self) -> bool;
    /// `!is_unused()`.
    #[inline]
    fn is_set(&self) -> bool {
        !self.is_unused()
    }
    /// `Some(self)` when set.
    #[inline]
    fn get(&self) -> Option<Self> {
        if self.is_unused() {
            None
        } else {
            Some(*self)
        }
    }
}

impl MaybeUnused for f32 {
    const UNUSED: f32 = UNUSED_F32;
    #[inline]
    fn is_unused(&self) -> bool {
        is_unused(*self)
    }
}

impl MaybeUnused for i32 {
    const UNUSED: i32 = UNUSED_I32;
    #[inline]
    fn is_unused(&self) -> bool {
        *self == UNUSED_I32
    }
}

impl MaybeUnused for Vec3 {
    const UNUSED: Vec3 = UNUSED_VEC3;
    /// Tests `.x` only, as the engine does.
    #[inline]
    fn is_unused(&self) -> bool {
        is_unused(self.x)
    }
}

impl MaybeUnused for Quat {
    const UNUSED: Quat = UNUSED_QUAT;
    /// Tests `.w` only, as the engine does.
    #[inline]
    fn is_unused(&self) -> bool {
        is_unused(self.w)
    }
}

/// A pointer-sized field of an x64 parameter struct. The Rust engine never dereferences it; it
/// exists so layouts match the binary (and the oracle) byte for byte. The engine API maps the
/// values it understands (null, unused, the "static world" marker `-10`) and replaces real
/// pointers with typed handles.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
#[repr(transparent)]
pub struct RawPtr(pub u64);

impl RawPtr {
    pub const NULL: RawPtr = RawPtr(0);
    pub const UNUSED: RawPtr = RawPtr(u64::MAX);
    /// `(void*)-10`: "the static world" as a constraint buddy / rope tie target.
    pub const WORLD: RawPtr = RawPtr(-10i64 as u64);

    #[inline]
    #[must_use]
    pub const fn is_null(self) -> bool {
        self.0 == 0
    }
}

impl MaybeUnused for RawPtr {
    const UNUSED: RawPtr = RawPtr::UNUSED;
    #[inline]
    fn is_unused(&self) -> bool {
        self.0 == u64::MAX
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sentinels() {
        assert!(UNUSED_F32.is_nan());
        assert!(UNUSED_F32.is_unused());
        assert!(!1.0f32.is_unused());
        // any NaN with the top 11 mask bits set counts as unused, ordinary NaN does too
        assert!(f32::from_bits(0xFFA0_0000).is_unused());
        assert!(!f32::NAN.is_unused()); // 0x7FC00000: sign bit clear
        assert!(!f32::from_bits(0xFFC0_0000).is_unused()); // the x86 default NaN is *not* "unused"
        assert_eq!(UNUSED_I32 as u32, 0x8000_0000);
        assert!(UNUSED_VEC3.is_unused());
        let mut v = UNUSED_VEC3;
        v.y = 1.0;
        assert!(v.is_unused(), "only .x is tested");
        let mut q = UNUSED_QUAT;
        q.x = 0.0;
        assert!(q.is_unused(), "only .w is tested");
        q.w = 1.0;
        assert!(q.is_set());
        assert_eq!(2.0f32.get(), Some(2.0));
        assert_eq!(UNUSED_F32.get(), None);
        assert!(RawPtr::UNUSED.is_unused() && RawPtr::NULL.is_null());
        assert_eq!(RawPtr::WORLD.0, 0xFFFF_FFFF_FFFF_FFF6);
    }
}
