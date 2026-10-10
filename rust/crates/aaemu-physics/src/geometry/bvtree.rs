//! **P1b — bounding-volume trees** (stub): `CSingleBoxTree`, `CAABBTree` (12-byte nodes,
//! 7-bit quantised), `COBBTree`, `CRayBV`; plus the node interface `CHeightfieldBV` (P1c) and
//! `CVoxelBV` (deferred) implement. 26-slot vtable: `geometry.md` §3.
//!
//! Stable interface: [`BvTree`]. P1b may add methods (slot 6–25 roles: node BVs, children,
//! node contents → geometry slot 56 primitive lists, used-triangle marks) but must not change
//! the ones below.

use crate::geometry::primitives::BoxPrim;
use crate::ids::BvTreeType;

/// A BV tree over a geometry's primitives.
pub trait BvTree: Send + Sync + std::fmt::Debug {
    /// Slot 1 `GetType`.
    fn tree_type(&self) -> BvTreeType;
    /// Slot 2 `GetBBox`: the root box (64-byte box primitive).
    fn bbox(&self) -> BoxPrim;
    /// Slot 3 `MaxPrimsInNode`.
    fn max_prims_in_node(&self) -> i32;
}
