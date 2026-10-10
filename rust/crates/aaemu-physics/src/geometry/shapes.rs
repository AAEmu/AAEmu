//! **P1a — primitive geometries**: `CBoxGeom` (0xC8 bytes, ctor 0x35075190), `CSphereGeom`
//! (0x98, 0x35038C20), `CCylinderGeom` (0xB0, 0x35038960), `CCapsuleGeom` (0xB0, 0x35038AB0)
//! and `CRayGeom` (0x70, 0x35038DD0 / 0x3514D950), each implementing
//! [`crate::geometry::Geometry`] on top of [`crate::geometry::primitives`]. Slot table:
//! `rust/docs/physics/cryphysics/geometry.md` §2.1.
//!
//! There is no plane geometry class in the binary: a plane ([`Plane`]) only appears as the
//! argument of slots 13/14 (the water plane).
//!
//! Layout of the port:
//! * this file — the shared `CGeometry` header ([`GeomHeader`]), the embedded
//!   `CSingleBoxTree` data ([`SingleBoxBv`]) and small helpers the classes share;
//! * [`box_geom`], [`sphere`], [`cylinder`] (cylinder and capsule), [`ray`] — the classes and
//!   their [`Geometry`] implementations;
//! * [`stream`] — slots 25/27 and the CGF physics blob (`CGeomManager` Save/LoadPhysGeometry);
//! * [`closest`] (slot 11), [`drag`] (slot 14), [`buoyancy`] (slots 12/13), [`protocol`]
//!   (intersection-test helpers, slots 48/49/53/56–59), [`misc`] (slots 20/43/51).
//!
//! Every function names its binary address; every float operation follows the binary's
//! precision and association (crate README, "Fidelity policy").

use crate::geometry::primitives::{BoxPrim, Plane};
use crate::geometry::{GeomWorldData, Geometry};
use crate::math::{Mat33, Quat, Vec3};

pub mod box_geom;
pub mod buoyancy;
pub mod closest;
pub mod cylinder;
pub mod drag;
pub mod misc;
pub mod protocol;
pub mod ray;
pub mod sphere;
pub mod stream;

pub use box_geom::BoxGeom;
pub use cylinder::{CapsuleGeom, CylinderGeom};
pub use ray::RayGeom;
pub use sphere::SphereGeom;

/// `CGeometry` header fields shared by all classes (`geometry.md` §1.2).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct GeomHeader {
    /// `+16` m_minVtxDist (inferred name): `1e-4·Σsize` for primitives, 1.0 for a ray.
    pub min_vtx_dist: f32,
    /// `+20` collision priority: ray 0, primitives 3.
    pub priority: i32,
    /// `+24`: 1 for the solid primitives, 0 for the ray.
    pub flag24: i32,
    /// `+32` foreign data (slots 35/37; an opaque pointer-sized value).
    pub foreign_data: u64,
    /// `+40` iForeignData.
    pub i_foreign_data: i32,
}

impl GeomHeader {
    /// Header of the solid primitives after their constructor.
    #[must_use]
    pub const fn solid() -> Self {
        Self {
            min_vtx_dist: 0.0,
            priority: 3,
            flag24: 1,
            foreign_data: 0,
            i_foreign_data: 0,
        }
    }
}

/// The data of the `CSingleBoxTree` (88 bytes) embedded in box, sphere, cylinder and capsule:
/// `+16` box primitive, `+80` nPrims (1), `+84` bIsConvex (0 after the ctor). The tree
/// object itself (slot 46 `GetBVTree`, the 26-slot BV vtable) belongs to P1b; this is the
/// state the primitives own and serialize.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct SingleBoxBv {
    pub box_: BoxPrim,
    pub n_prims: i32,
    pub is_convex: i32,
}

impl Default for SingleBoxBv {
    /// After the geometry ctors: zeroed box (`memset(+16, 0, 64)`), nPrims 1, bIsConvex 0.
    fn default() -> Self {
        Self {
            box_: BoxPrim {
                basis: Mat33::ZERO,
                oriented: 0,
                center: Vec3::ZERO,
                size: Vec3::ZERO,
            },
            n_prims: 1,
            is_convex: 0,
        }
    }
}

impl SingleBoxBv {
    /// `sub_351AE290` (SetBox): copies basis, centre and size; `bOriented =
    /// !IsIdentity(basis)` ([`is_identity`]), ignoring the incoming flag.
    pub fn set_box(&mut self, b: &BoxPrim) {
        self.box_.basis = b.basis;
        self.box_.oriented = i32::from(!is_identity(&b.basis));
        self.box_.center = b.center;
        self.box_.size = b.size;
    }

    /// `CSingleBoxTree` slot 4 (`0x351ADF90`): the build cost `((sy·sx)·sz)·8` (all single).
    #[must_use]
    pub fn build_cost(&self) -> f32 {
        let s = self.box_.size;
        ((s.y * s.x) * s.z) * 8.0
    }
}

/// `sub_3513A010` IsIdentity: the sum
/// `|1−m00| + |m01| + |m02| + |m10| + |1−m11| + |m12| + |m20| + |m21| + |1−m22|` (single
/// precision, left to right) compared with 0 by `ucomiss`/`jne`.
///
/// Quirk kept: a NaN sum is unordered, `jne` falls through, and the matrix counts as the
/// identity (so a NaN basis gives `bOriented = 0`).
#[must_use]
pub fn is_identity(m: &Mat33) -> bool {
    let a = m.to_array();
    let s = (1.0 - a[0]).abs() + a[1].abs();
    let s = s + a[2].abs();
    let s = s + a[3].abs();
    let s = s + (1.0 - a[4]).abs();
    let s = s + a[5].abs();
    let s = s + a[6].abs();
    let s = s + a[7].abs();
    let s = s + (1.0 - a[8]).abs();
    s == 0.0 || s.is_nan()
}

/// Index tables at 0x35209B20 / 0x35209B30 (also 0x35209308/18, 0x35209D30/40): `inc = {1,2,0}`,
/// `dec = {2,0,1}`.
pub const INC_MOD3: [usize; 3] = [1, 2, 0];
/// See [`INC_MOD3`].
pub const DEC_MOD3: [usize; 3] = [2, 0, 1];

/// The engine's `GetOrthogonal` idiom (inlined in cylinder/capsule `SetData` 0x35144DB0 /
/// 0x35141920 and `QuatRotationArc` 0x35040F80): `i = isneg(|a|²·0.81 − a.x²)`;
/// `v[i] = 0, v[inc i] = a[dec i], v[dec i] = −a[inc i]` (unnormalised).
#[must_use]
pub fn orthogonal(a: Vec3) -> Vec3 {
    let k = f32::from_bits(0x3F4F_5C28); // 0.80999994
    let t = a.len2() * k - a.x * a.x;
    let i = (t.to_bits() >> 31) as usize;
    let mut v = Vec3::ZERO;
    v[i] = 0.0;
    v[INC_MOD3[i]] = a[DEC_MOD3[i]];
    v[DEC_MOD3[i]] = -a[INC_MOD3[i]];
    v
}

/// `v · (1/sqrt(|v|²))` if `|v|² > 1e-20`, else `fallback` (the common "normalize safe" idiom;
/// single precision, `1.0 / sqrtss`).
#[must_use]
pub fn normalize_safe(v: Vec3, fallback: Vec3) -> Vec3 {
    let l2 = v.len2();
    if l2 > 1e-20 {
        let inv = 1.0 / l2.sqrt();
        Vec3::new(v.x * inv, v.y * inv, v.z * inv)
    } else {
        fallback
    }
}

/// `sub_35040F80` QuatRotationArc(from, to): the shortest-arc rotation taking unit `from` to
/// unit `to`.
///
/// `d = (from·to) + 1` (single, `to.x·from.x + to.y·from.y` first) widened to double; if
/// `d > 1e-4` (double compare): `q = (from × to, d)` normalised in double with the squared
/// norm summed `((x² + w²) + y²) + z²`; else `w = 0` and `v` = the normalised orthogonal of
/// `from` (`1/sqrtss(|o|² + FLT_MIN)`).
#[must_use]
pub fn quat_rotation_arc(from: Vec3, to: Vec3) -> Quat {
    let dotp = (to.x * from.x + to.y * from.y) + to.z * from.z;
    let d = f64::from(dotp + 1.0);
    if d > f64::from(1e-4_f32) {
        let cx = f64::from(to.z * from.y - to.y * from.z);
        let cy = f64::from(from.z * to.x - to.z * from.x);
        let cz = f64::from(to.y * from.x - from.y * to.x);
        let n2 = ((cx * cx + d * d) + cy * cy) + cz * cz;
        let inv = 1.0 / n2.sqrt();
        Quat::new(
            (inv * cx) as f32,
            (inv * cy) as f32,
            (inv * cz) as f32,
            (inv * d) as f32,
        )
    } else {
        let o = orthogonal(from);
        let inv = 1.0 / (o.len2() + f32::MIN_POSITIVE).sqrt();
        Quat::new(inv * o.x, inv * o.y, inv * o.z, 0.0)
    }
}

/// `R·v·scale + offset` as the primitives compute a world point (`(R·v)·s + off`, R row-major,
/// each row dotted `(m0·x + m1·y) + m2·z`).
#[inline]
#[must_use]
pub fn gwd_point(gwd: &GeomWorldData, v: Vec3) -> Vec3 {
    gwd.r.mul_vec(v) * gwd.scale + gwd.offset
}

/// The plane argument of slots 13/14 as `(n, origin)`.
#[inline]
#[must_use]
pub fn plane_parts(p: &Plane) -> (Vec3, Vec3) {
    (p.n, p.origin)
}

/// Shared Geometry plumbing for the five classes: header access.
pub trait PrimitiveGeom: Geometry {
    /// The `CGeometry` header.
    fn header(&self) -> &GeomHeader;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn identity_quirks() {
        assert!(is_identity(&Mat33::IDENTITY));
        let mut m = Mat33::IDENTITY;
        m.m[0][1] = 1e-30;
        assert!(!is_identity(&m));
        m.m[0][1] = f32::NAN;
        assert!(is_identity(&m), "NaN basis counts as identity");
        m.m[0][1] = -0.0;
        assert!(is_identity(&m));
    }

    #[test]
    fn orthogonal_picks_the_small_axes() {
        assert_eq!(orthogonal(Vec3::Z), Vec3::new(0.0, 1.0, -0.0));
        assert_eq!(orthogonal(Vec3::X), Vec3::new(-0.0, 0.0, 1.0));
        let a = Vec3::new(0.3, -0.5, 0.8);
        assert_eq!(orthogonal(a).dot(a), 0.0);
    }

    #[test]
    fn rotation_arc() {
        assert_eq!(quat_rotation_arc(Vec3::Z, Vec3::Z), Quat::IDENTITY);
        let q = quat_rotation_arc(Vec3::Z, -Vec3::Z);
        assert_eq!(q.w, 0.0);
        assert!((q.rotate(Vec3::Z) + Vec3::Z).len() < 1e-6);
        let to = Vec3::new(0.6, 0.0, 0.8);
        assert!((quat_rotation_arc(Vec3::Z, to).rotate(Vec3::Z) - to).len() < 1e-6);
    }
}
