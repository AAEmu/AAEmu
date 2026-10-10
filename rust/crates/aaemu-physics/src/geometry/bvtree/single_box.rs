//! `CSingleBoxTree` (vtable 0x351EE4A8, sizeof 88): one box over all primitives. Embedded in
//! the primitive geometries and used by meshes with build flag 0x80 (`SingleBox` in blobs).
//!
//! Layout: `+8` geometry, `+16` box primitive (basis +16, bOriented +52, centre +56, size +68),
//! `+80` nPrims, `+84` bIsConvex.

use super::stream::{MemReader, MemWriter, StreamError};
use super::swept::{sweep_box, transform_box};
use super::{box_to_words, rec, BvArena, BvHandle, BvTestState, BvTree, NodeContents, UsedNodeMap};
use crate::geometry::primitives::BoxPrim;
use crate::ids::BvTreeType;
use crate::math::{Mat33, Vec3};

/// `CSingleBoxTree`.
#[derive(Clone, Debug, PartialEq)]
pub struct SingleBoxTree {
    /// `+16`.
    pub bx: BoxPrim,
    /// `+80` (the mesh constructor and loader start it at 1).
    pub n_prims: i32,
    /// `+84` (slot 5).
    pub is_convex: i32,
}

impl Default for SingleBoxTree {
    /// What `CTriMesh` allocates before `SetBox`/`Load`: a zeroed box (zero basis), nPrims 1.
    fn default() -> Self {
        Self {
            bx: BoxPrim {
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

/// sub_3513A010 `IsIdentity`: `|1−m00| + |m01| + … + |1−m22| == 0` summed left to right in
/// single precision. **Quirk:** the compare is a bare `ucomiss`/`jne`, so a NaN sum also counts
/// as identity.
#[must_use]
pub fn is_identity_sum(m: &Mat33) -> bool {
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

impl SingleBoxTree {
    /// A tree over `n_prims` primitives with box `bx` (as stored, `bOriented` included).
    #[must_use]
    pub fn new(bx: BoxPrim, n_prims: i32) -> Self {
        Self {
            bx,
            n_prims,
            is_convex: 0,
        }
    }

    /// sub_351AE290 `SetBox`: copies basis, centre and size; `bOriented = !IsIdentity(basis)`
    /// (the incoming flag is ignored).
    pub fn set_box(&mut self, b: &BoxPrim) {
        self.bx.basis = b.basis;
        self.bx.oriented = i32::from(!is_identity_sum(&b.basis));
        self.bx.center = b.center;
        self.bx.size = b.size;
    }

    /// Slot 4 `Build` (0x351ADF90): only stores the geometry; returns the cost
    /// `((size.y·size.x)·size.z)·8`.
    #[must_use]
    pub fn build_cost(&self) -> f32 {
        ((self.bx.size.y * self.bx.size.x) * self.bx.size.z) * 8.0
    }

    /// Slot 22 `Load` (0x351AE380): the raw 64-byte box, then nPrims.
    ///
    /// # Errors
    /// Truncated stream.
    pub fn load(r: &mut MemReader<'_>) -> Result<Self, StreamError> {
        let basis = r.mat33()?;
        let oriented = r.i32()?;
        let center = r.vec3()?;
        let size = r.vec3()?;
        let n_prims = r.i32()?;
        Ok(Self {
            bx: BoxPrim {
                basis,
                oriented,
                center,
                size,
            },
            n_prims,
            is_convex: 0,
        })
    }

    fn write_rec(arena: &mut BvArena, b: &BoxPrim) -> BvHandle {
        let i = arena.alloc_bv();
        let w = &mut arena.bv_pool[i];
        w[rec::TYPE] = 0;
        w[rec::NODE] = 0;
        box_to_words(w, rec::BOX, b);
        BvHandle::Pool(i as i32)
    }
}

impl BvTree for SingleBoxTree {
    /// 0x350FE380.
    fn tree_type(&self) -> BvTreeType {
        BvTreeType::SingleBox
    }

    /// 0x351AE1D0.
    fn bbox(&self) -> BoxPrim {
        self.bx
    }

    /// 0x35038940: nPrims.
    fn max_prims_in_node(&self) -> i32 {
        self.n_prims
    }

    /// 0x35038930.
    fn set_geom_convex(&mut self) {
        self.is_convex = 1;
    }

    /// 0x351ADF70: the used-node map points at the gtest's own `+32` field, which is zeroed.
    fn prepare_for_intersection_test(&self, gtest: &mut BvTestState, _arena: &mut BvArena) -> i32 {
        gtest.used_map = UsedNodeMap::AliasLastUsed;
        gtest.i_last_used = 0;
        1
    }

    /// 0x351AE620.
    fn get_node_bv_swept_transformed(
        &self,
        arena: &mut BvArena,
        r: &Mat33,
        offset: Vec3,
        scale: f32,
        dir: Vec3,
        len: f32,
        _i_node: i32,
    ) -> Option<BvHandle> {
        let t = transform_box(
            &self.bx.basis,
            self.bx.center,
            self.bx.size,
            r,
            offset,
            scale,
        );
        Some(Self::write_rec(arena, &sweep_box(&t, dir, len)))
    }

    /// 0x351AE3F0.
    fn get_node_bv_transformed(
        &self,
        arena: &mut BvArena,
        r: &Mat33,
        offset: Vec3,
        scale: f32,
        _i_node: i32,
    ) -> BvHandle {
        let t = transform_box(
            &self.bx.basis,
            self.bx.center,
            self.bx.size,
            r,
            offset,
            scale,
        );
        Self::write_rec(arena, &t)
    }

    /// 0x351AE080: the box (with its own bOriented) swept; a 72-byte record.
    fn get_node_bv_swept(
        &self,
        arena: &mut BvArena,
        dir: Vec3,
        len: f32,
        _i_node: i32,
    ) -> Option<BvHandle> {
        Some(Self::write_rec(arena, &sweep_box(&self.bx, dir, len)))
    }

    /// 0x351ADFC0.
    fn get_node_bv(&self, arena: &mut BvArena, _i_node: i32) -> BvHandle {
        Self::write_rec(arena, &self.bx)
    }

    /// 0x351AE240: "SingleBox trees", 88 bytes.
    fn memory_size(&self) -> usize {
        88
    }

    /// 0x351AE320: the 64-byte box (sub_3513F6C0), then nPrims.
    fn save(&self, w: &mut MemWriter) {
        w.mat33(&self.bx.basis);
        w.i32(self.bx.oriented);
        w.vec3(self.bx.center);
        w.vec3(self.bx.size);
        w.i32(self.n_prims);
    }

    /// 0x351ADF00: `(0, nPrims)`.
    fn get_node_contents(&self, _i_node: i32) -> NodeContents {
        NodeContents {
            first_prim: 0,
            n_prims: self.n_prims,
        }
    }

    /// 0x35038950.
    fn get_node_contents_idx(&self, _i_node: i32) -> NodeContents {
        NodeContents {
            first_prim: 0,
            n_prims: self.n_prims,
        }
    }

    /// 0x351AE1B0: `r = bIsConvex | (nPrims < 2)`; `gtest+32 |= r` (node 0 "used") and
    /// `gtest+44 |= r`.
    fn mark_used_triangle(&self, _i_tri: i32, gtest: &mut BvTestState, _arena: &mut BvArena) {
        let r = self.is_convex | ((self.n_prims.wrapping_sub(2) as u32) >> 31) as i32;
        gtest.i_last_used |= r;
        gtest.b_marked |= r;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn save_load_roundtrip() {
        let t = SingleBoxTree::new(
            BoxPrim {
                basis: Mat33::from_array([0.0, 1.0, 0.0, -1.0, 0.0, 0.0, 0.0, 0.0, 1.0]),
                oriented: 1,
                center: Vec3::new(1.0, 2.0, 3.0),
                size: Vec3::new(0.5, 0.25, 4.0),
            },
            12,
        );
        let mut w = MemWriter::new();
        t.save(&mut w);
        let b = w.into_bytes();
        assert_eq!(b.len(), 68);
        let t2 = SingleBoxTree::load(&mut MemReader::new(&b)).unwrap();
        assert_eq!(t, t2);
    }

    #[test]
    fn identity_quirk() {
        assert!(is_identity_sum(&Mat33::IDENTITY));
        let mut m = Mat33::IDENTITY;
        m.m[0][1] = -0.0;
        assert!(is_identity_sum(&m));
        m.m[0][1] = f32::NAN;
        assert!(is_identity_sum(&m), "NaN sum counts as identity");
        m.m[0][1] = 1e-30;
        assert!(!is_identity_sum(&m));
    }

    #[test]
    fn mark_used_and_prepare() {
        let mut t = SingleBoxTree::new(BoxPrim::default(), 5);
        let mut a = BvArena::new();
        let mut g = BvTestState::default();
        t.prepare_for_intersection_test(&mut g, &mut a);
        assert_eq!(g.is_node_used(&a, 0), Some(false));
        t.mark_used_triangle(0, &mut g, &mut a);
        assert_eq!(g.is_node_used(&a, 0), Some(false));
        t.set_geom_convex();
        t.mark_used_triangle(0, &mut g, &mut a);
        assert_eq!(g.is_node_used(&a, 0), Some(true));
        assert_eq!(g.b_marked, 1);
    }
}
