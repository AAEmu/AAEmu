//! The remaining miscellaneous slots of the primitives:
//!
//! * slot 20 `GetFeature` — [`feature`] (box only; every other primitive returns 0);
//! * slot 43 `GetRandomPos` — [`random`] (the `CGeometry` base used by the box and the ray, the
//!   sphere, the cylinder/capsule) together with xlcommon's `CryRandom` generator it draws from;
//! * slot 51, the occlusion-cubemap rasteriser (leaf worker of slot 23
//!   `DrawToOcclusionCubemap`) — [`cubemap`], including the shared polygon rasteriser
//!   `sub_35131AB0`;
//! * slot 15 `DrawWireframe` — [`wireframe`], through the [`wireframe::PhysRenderer`] trait;
//! * slot 24 `GetMemoryStatistics` — [`memory_statistics`] in this file.
//!
//! Slot 23 itself (`CGeometry` 0x3514EBF0) is **not** ported here: it walks the geometry's BV
//! tree with the tree-vs-sphere traversal `sub_3514DCF0` (BV slots 11/12/14/16/24 and the
//! `OVERLAP[box][sphere]` cell of 0x3520A400), converts the "behind `rmin`" flag bits of every
//! pixel afterwards and, when asked to, runs the cubemap post-pass `sub_3512C0E0` (a 32 KB-stack
//! flood/grow over the six faces). The traversal and the overlap test belong to the BV-tree
//! (P1b) and pair-table (P2a) packages, so the port of slot 23 is left to the BV package; it
//! calls [`cubemap::box_draw_to_cubemap`] / [`cubemap::sphere_draw_to_cubemap`] /
//! [`cubemap::cylinder_draw_to_cubemap`] at the leaves, with the arguments documented there.

pub mod cubemap;
pub mod feature;
pub mod random;
pub mod wireframe;

pub use cubemap::Cubemap;
pub use feature::box_get_feature;
pub use random::{CryRandom, PosNorm};
pub use wireframe::PhysRenderer;

use crate::geometry::Geometry;
use crate::ids::GeomType;

/// The part of `ICrySizer` the primitives call: slot 6 (`+48`) `AddObject(ptr, size)`.
pub trait CrySizer {
    /// `AddObject(pObject, nBytes)`; the object is the geometry itself.
    fn add_object(&mut self, size: usize);
}

/// Slot 24 `GetMemoryStatistics(ICrySizer*)`: one `AddObject(this, sizeof)` call —
/// box 0x3513A420 (200 bytes), sphere 0x351AE9C0 (152), cylinder and capsule 0x35141E50 (176).
/// The ray (and the `CGeometry` base) has the `nop` stub 0x35037700. The primitives do not
/// report their embedded `CSingleBoxTree` (its own slot 20, 0x351AE240, would push the
/// "SingleBox trees" label with 88 bytes, but nothing calls it from here).
pub fn memory_statistics(g: &dyn Geometry, sizer: &mut dyn CrySizer) {
    let size = match g.geom_type() {
        GeomType::Box => 200,
        GeomType::Sphere => 152,
        GeomType::Cylinder | GeomType::Capsule => 176,
        _ => return,
    };
    sizer.add_object(size);
}
