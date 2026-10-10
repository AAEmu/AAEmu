//! **P1b — bounding-volume trees**: `CSingleBoxTree`, `CAABBTree` (12-byte nodes, 7-bit
//! quantised), `COBBTree` (72-byte nodes), `CRayBV`; the node interface `CHeightfieldBV` (P1c)
//! implements; `CVoxelBV` is deferred with `CVoxelGeom`. 26-slot vtable: `geometry.md` §3.
//!
//! Classes (x64 vtables): `CBVTree` 0x351EDC68, `CAABBTree` 0x351F30E8, `COBBTree` 0x351F3008,
//! `CSingleBoxTree` 0x351EE4A8, `CRayBV` 0x351EDEC8.
//!
//! * [`SingleBoxTree`] — [`single_box`]; [`AabbTree`] — [`aabb`]; [`ObbTree`] — [`obb`];
//!   [`RayBv`] — [`ray`].
//! * Building: [`AabbTree::build`] (slot 4, 0x35139490), [`ObbTree::build`] (0x35195210), with
//!   the principal-axes fit [`fit::principal_axes`] (0x35195400) and the convex hull
//!   [`hull::convex_hull`] (0x35128440) that the OBB builder and the mesh constructor use.
//! * (De)serialisation: slot 21 `Save` / slot 22 `Load` in the CGF physics-blob format
//!   ([`stream::MemWriter`] / [`stream::MemReader`], the engine's `CMemStream`).
//!
//! ## The per-caller arena ([`BvArena`]) and BV handles
//!
//! The original hands out BV records from a per-caller (per-thread) scratch arena (base
//! 0x35256F80, stride 0x39FB8, `geometry.md` §3.4): a pool of 512 × 72-byte records, a pool of
//! 128 × 136-byte swept-AABB records, single records for ray / heightfield BVs, and the used-node
//! bitmap and index-list pools. Records are taken and released two at a time with plain
//! counters, so **records alias**: `CAABBTree` slot 10 resets the swept pool to slot 0 and
//! slot 13 then hands out slots 0 and 1, overwriting the root record while it is still being
//! read. The port keeps the pools as raw 32-bit words with the same counters so that this (and
//! the stale-basis behaviour of slot 14) is reproduced; BV-returning slots give a [`BvHandle`]
//! into the arena, and [`BvArena::bv_box`] etc. read the record as the consumers (OVERLAP,
//! INTERSECT) see it.
//!
//! ## Interface for P1c / P2
//!
//! [`BvTree`] is the slot interface (object safe, `Box<dyn BvTree>` in the mesh). The parts of
//! `geometry_under_test` the trees touch are [`BvTestState`] (embed it in the gtest);
//! geometry slot 56 (`GetPrimitiveList`) is called by the traversal with the
//! [`NodeContents`] range that slot 23 returns. A mesh gets its tree from
//! [`select::build_mesh_tree`] (the `CTriMesh` constructor's choice for its build flags) or
//! [`load_tree`] (blob type id + slot 22); [`BuildMesh`] is the view of the mesh arrays the
//! builders permute. Consumers must ignore a record's basis when its `bOriented` is 0 (it is
//! stale pool memory, as in the original).
//!
//! ## Original bugs kept (each documented at its site)
//!
//! * `CAABBTree` slot 6 always leaves the used-node map null (marking never happens, but the
//!   arena pools are still consumed); slot 10 does not advance the swept pool, so slot 13
//!   overwrites the root record while reading it, and slot 13's second child tests the
//!   parent's *swept* `bOriented`.
//! * `COBBTree` slot 13 writes the second child's `bOriented` into the first child.
//! * The OBB builder fits nodes of > 100 triangles (depth ≤ 4) to their convex hull but reads
//!   the hull's indices from the mesh vertex array instead of the unique-vertex buffer.
//! * The mesh constructor does not back up the auxiliary per-triangle array (+160) between
//!   candidate trees.
//! * `IsIdentity` (sub_3513A010) treats a NaN sum as identity.
//!
//! Evidence: `tests/p1b_oracle.rs` (the DLL's own builders, slots and helpers via
//! `tests/p1b_probe.c`; 1,542 real meshes), `tests/p1b_blobs.rs` (byte-exact round trips of
//! every real serialized tree), `tests/p1b_traversal.rs`.

pub mod aabb;
pub mod fit;
pub mod hull;
pub mod obb;
pub mod ray;
pub mod select;
pub mod single_box;
pub mod stream;
pub mod swept;

pub use aabb::{AabbNode, AabbTree};
pub use obb::{ObbNode, ObbTree};
pub use ray::RayBv;
pub use single_box::SingleBoxTree;
pub use stream::{MemReader, MemWriter, StreamError};

use crate::geometry::primitives::{BoxPrim, HeightfieldPrim, RayPrim};
use crate::ids::BvTreeType;
use crate::math::{Mat33, Vec3};

/// Records in the 72-byte BV pool (arena +0x2C9DC, counter +0x359DC).
pub const BV_POOL_SIZE: usize = 512;
/// Records in the 136-byte swept-AABB pool (arena +0x359E0, counter +0x39DE0).
pub const SWEPT_POOL_SIZE: usize = 128;
/// Dwords in the used-node bitmap pool (arena +0x46B0, counter +0xC6B0).
pub const USED_MAP_POOL_DWORDS: usize = 0x2000;
/// Ints in the used-node index-list pool (arena +0xC6B4, counter +0xC7B4).
pub const USED_LIST_POOL_INTS: usize = 64;

/// Words of a 72-byte BV record: `{int type; int iNode; box{basis[9] @2, bOriented @11,
/// center @12, size @15}}`. A ray record keeps `{origin @2, dir @5}` in the same words.
pub type BvWords = [u32; 18];
/// Words of a 136-byte swept-AABB record: the 72-byte record (swept box) followed by the
/// unswept box `{basis @18, bOriented @27, center @28, size @31}`.
pub type SweptBvWords = [u32; 34];

/// Word offsets of a box inside a record.
pub(crate) mod rec {
    pub const TYPE: usize = 0;
    pub const NODE: usize = 1;
    pub const BOX: usize = 2;
    pub const ORIG: usize = 18;
}

/// A BV record: the primitive type id (box 0, tri 1, heightfield 2, ray 3, sphere 4, voxel 7),
/// the tree node it bounds, and the primitive.
#[derive(Clone, Copy, Debug)]
pub struct BvRecord {
    pub kind: i32,
    pub i_node: i32,
    pub prim: BvPrim,
}

/// The primitive inside a [`BvRecord`].
#[derive(Clone, Copy, Debug)]
pub enum BvPrim {
    Box(BoxPrim),
    Ray(RayPrim),
    Heightfield(HeightfieldPrim),
}

/// Where a BV record lives in the [`BvArena`].
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum BvHandle {
    /// Slot of the 72-byte pool.
    Pool(i32),
    /// Slot of the 136-byte swept-AABB pool.
    Swept(i32),
    /// The ray BV record (arena +0x39F48).
    Ray,
    /// The heightfield BV record (arena +0x39DE8; written by `CHeightfieldBV`, P1c).
    Heightfield,
}

/// The heightfield BV record (arena +0x39DE8): `{int type = 2; int iNode; heightfield prim}`.
#[derive(Clone, Copy, Debug, Default)]
pub struct HeightfieldBvRecord {
    pub kind: i32,
    pub i_node: i32,
    pub hf: HeightfieldPrim,
}

/// The per-caller BV scratch arena (one per `iCaller`, base 0x35256F80 + iCaller·0x39FB8).
#[derive(Clone)]
pub struct BvArena {
    /// +0x2C9DC: 72-byte BV records.
    pub bv_pool: Vec<BvWords>,
    /// +0x359DC.
    pub bv_count: i32,
    /// +0x359E0: 136-byte swept-AABB records.
    pub swept_pool: Vec<SweptBvWords>,
    /// +0x39DE0.
    pub swept_count: i32,
    /// +0x39F48: `{int type; int iNode; ray}` (8 words).
    pub ray_bv: [u32; 8],
    /// +0x39DE8.
    pub hf_bv: HeightfieldBvRecord,
    /// +0x46B0: used-node bitmaps.
    pub used_map_pool: Vec<u32>,
    /// +0xC6B0 (reset by `CGeometry::Intersect`, 0x3514FD70).
    pub used_map_count: i32,
    /// +0xC6B4: used-node index lists.
    pub used_list_pool: [i32; USED_LIST_POOL_INTS],
    /// +0xC7B4 (reset by `CGeometry::Intersect`).
    pub used_list_count: i32,
}

impl Default for BvArena {
    fn default() -> Self {
        Self::new()
    }
}

impl std::fmt::Debug for BvArena {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("BvArena")
            .field("bv_count", &self.bv_count)
            .field("swept_count", &self.swept_count)
            .field("used_map_count", &self.used_map_count)
            .field("used_list_count", &self.used_list_count)
            .finish_non_exhaustive()
    }
}

impl BvArena {
    /// A zeroed arena (the original is a zero-initialised static).
    #[must_use]
    pub fn new() -> Self {
        Self {
            bv_pool: vec![[0; 18]; BV_POOL_SIZE],
            bv_count: 0,
            swept_pool: vec![[0; 34]; SWEPT_POOL_SIZE],
            swept_count: 0,
            ray_bv: [0; 8],
            hf_bv: HeightfieldBvRecord::default(),
            used_map_pool: vec![0; USED_MAP_POOL_DWORDS],
            used_map_count: 0,
            used_list_pool: [0; USED_LIST_POOL_INTS],
            used_list_count: 0,
        }
    }

    /// What `CGeometry::Intersect` (0x3514FD70) does on entry: reset the used-node map and list
    /// counters.
    pub fn reset_used_pools(&mut self) {
        self.used_map_count = 0;
        self.used_list_count = 0;
    }

    /// Takes the next 72-byte record (`counter++`, no bounds check in the original; the port
    /// panics on overflow instead of corrupting memory).
    pub(crate) fn alloc_bv(&mut self) -> usize {
        let i = self.bv_count;
        self.bv_count += 1;
        usize::try_from(i).expect("BV pool underflow")
    }

    /// The words of a record (72-byte view; for a swept record its first 18 words).
    #[must_use]
    pub fn bv_words(&self, h: BvHandle) -> BvWords {
        match h {
            BvHandle::Pool(i) => self.bv_pool[i as usize],
            BvHandle::Swept(i) => {
                let mut w = [0u32; 18];
                w.copy_from_slice(&self.swept_pool[i as usize][..18]);
                w
            }
            BvHandle::Ray => {
                let mut w = [0u32; 18];
                w[..8].copy_from_slice(&self.ray_bv);
                w
            }
            BvHandle::Heightfield => {
                let mut w = [0u32; 18];
                w[0] = self.hf_bv.kind as u32;
                w[1] = self.hf_bv.i_node as u32;
                w
            }
        }
    }

    /// Record type id (`+0`).
    #[must_use]
    pub fn bv_kind(&self, h: BvHandle) -> i32 {
        match h {
            BvHandle::Heightfield => self.hf_bv.kind,
            _ => self.bv_words(h)[rec::TYPE] as i32,
        }
    }

    /// Record node index (`+4`).
    #[must_use]
    pub fn bv_node(&self, h: BvHandle) -> i32 {
        match h {
            BvHandle::Heightfield => self.hf_bv.i_node,
            _ => self.bv_words(h)[rec::NODE] as i32,
        }
    }

    /// The record's box (`+8`), as OVERLAP / INTERSECT read it.
    #[must_use]
    pub fn bv_box(&self, h: BvHandle) -> BoxPrim {
        box_from_words(&self.bv_words(h), rec::BOX)
    }

    /// The unswept box of a swept-AABB record (`+72`).
    #[must_use]
    pub fn swept_orig_box(&self, i: i32) -> BoxPrim {
        box_from_words(&self.swept_pool[i as usize], rec::ORIG)
    }

    /// The record as a typed value.
    #[must_use]
    pub fn bv_record(&self, h: BvHandle) -> BvRecord {
        match h {
            BvHandle::Heightfield => BvRecord {
                kind: self.hf_bv.kind,
                i_node: self.hf_bv.i_node,
                prim: BvPrim::Heightfield(self.hf_bv.hf),
            },
            BvHandle::Ray => {
                let w = &self.ray_bv;
                BvRecord {
                    kind: w[0] as i32,
                    i_node: w[1] as i32,
                    prim: BvPrim::Ray(RayPrim {
                        origin: vec_from_words(w, 2),
                        dir: vec_from_words(w, 5),
                    }),
                }
            }
            _ => {
                let w = self.bv_words(h);
                BvRecord {
                    kind: w[rec::TYPE] as i32,
                    i_node: w[rec::NODE] as i32,
                    prim: BvPrim::Box(box_from_words(&w, rec::BOX)),
                }
            }
        }
    }
}

/// Reads a 16-word box at word offset `at`.
#[must_use]
pub(crate) fn box_from_words(w: &[u32], at: usize) -> BoxPrim {
    let mut b = [0f32; 9];
    for (k, v) in b.iter_mut().enumerate() {
        *v = f32::from_bits(w[at + k]);
    }
    BoxPrim {
        basis: Mat33::from_array(b),
        oriented: w[at + 9] as i32,
        center: vec_from_words(w, at + 10),
        size: vec_from_words(w, at + 13),
    }
}

/// Writes a 16-word box at word offset `at`.
pub(crate) fn box_to_words(w: &mut [u32], at: usize, b: &BoxPrim) {
    for (k, v) in b.basis.to_array().iter().enumerate() {
        w[at + k] = v.to_bits();
    }
    w[at + 9] = b.oriented as u32;
    vec_to_words(w, at + 10, b.center);
    vec_to_words(w, at + 13, b.size);
}

#[must_use]
pub(crate) fn vec_from_words(w: &[u32], at: usize) -> Vec3 {
    Vec3::new(
        f32::from_bits(w[at]),
        f32::from_bits(w[at + 1]),
        f32::from_bits(w[at + 2]),
    )
}

pub(crate) fn vec_to_words(w: &mut [u32], at: usize, v: Vec3) {
    w[at] = v.x.to_bits();
    w[at + 1] = v.y.to_bits();
    w[at + 2] = v.z.to_bits();
}

/// The used-node bitmap pointer of a gtest (`+16`).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub enum UsedNodeMap {
    /// Null.
    #[default]
    None,
    /// In the arena's bitmap pool, starting at this dword.
    Arena(usize),
    /// Heap-allocated (the pool was full).
    Heap(Vec<u32>),
    /// `CSingleBoxTree` points the map at the gtest's own `+32` field ([`BvTestState::
    /// i_last_used`]), so node 0's "used" bit is bit 0 of that int.
    AliasLastUsed,
}

/// The fields of `geometry_under_test` (392 bytes, `geometry.md` §2.3) that the BV trees read
/// and write. P2 embeds this in its gtest.
#[derive(Clone, Debug, Default)]
pub struct BvTestState {
    /// `+16` used-node bitmap.
    pub used_map: UsedNodeMap,
    /// `+24` used-node index list: offset into [`BvArena::used_list_pool`] (`None` = null).
    pub used_list: Option<usize>,
    /// `+32` index of the last entry in the used list (−1 = empty).
    pub i_last_used: i32,
    /// `+36` capacity of the used list.
    pub n_used_cap: i32,
    /// `+44` set when a node was marked used *(inferred: "contacts registered on marked
    /// nodes")*.
    pub b_marked: i32,
}

impl BvTestState {
    /// Whether node `i` is marked in the used-node map (`map[i >> 5] & 1 << (i & 31)`), `None`
    /// when the map pointer is null.
    #[must_use]
    pub fn is_node_used(&self, arena: &BvArena, i: i32) -> Option<bool> {
        let bit = 1u32 << (i & 31);
        let w = (i >> 5) as usize;
        match &self.used_map {
            UsedNodeMap::None => None,
            UsedNodeMap::Arena(off) => Some(arena.used_map_pool[off + w] & bit != 0),
            UsedNodeMap::Heap(v) => Some(v[w] & bit != 0),
            UsedNodeMap::AliasLastUsed => {
                debug_assert_eq!(w, 0, "single-box map only has node 0");
                Some(self.i_last_used as u32 & bit != 0)
            }
        }
    }

    /// `map[i >> 5]` as a mutable dword.
    pub(crate) fn map_word_mut<'a>(
        &'a mut self,
        arena: &'a mut BvArena,
        i: i32,
    ) -> Option<&'a mut u32> {
        let w = (i >> 5) as usize;
        match &mut self.used_map {
            UsedNodeMap::None => None,
            UsedNodeMap::Arena(off) => Some(&mut arena.used_map_pool[*off + w]),
            UsedNodeMap::Heap(v) => Some(&mut v[w]),
            UsedNodeMap::AliasLastUsed => None,
        }
    }

    /// Appends `node` to the used list the way the trees do it:
    /// `iLast = min(iLast + 1, cap − 1); list[iLast] = node`.
    pub(crate) fn push_used(&mut self, arena: &mut BvArena, node: i32) {
        let cap1 = self.n_used_cap - 1;
        let d = (self.i_last_used + 1).wrapping_sub(cap1);
        let v = cap1.wrapping_add(d & (d >> 31));
        self.i_last_used = v;
        if let Some(off) = self.used_list {
            // With cap = 0 the original writes list[-1] (memory before the list); unreachable
            // with at most two gtests per Intersect (the counters reset each call).
            let idx = off as i64 + i64::from(v);
            debug_assert!(idx >= 0, "used-list write before the pool");
            if let Ok(idx) = usize::try_from(idx) {
                if idx < USED_LIST_POOL_INTS {
                    arena.used_list_pool[idx] = node;
                }
            }
        }
    }
}

/// Shared slot-6 allocation of `CAABBTree` (0x35136780) / `COBBTree` (0x351921E0): a used-node
/// bitmap of `((nNodes − 1) >> 5) + 1` dwords from the arena pool (heap if it does not fit) and
/// a used list of `min(32, 64 − listCount)` ints.
pub(crate) fn alloc_used_map_and_list(gtest: &mut BvTestState, arena: &mut BvArena, n_nodes: i32) {
    let n = ((n_nodes - 1) >> 5) + 1;
    let count = arena.used_map_count;
    if n > USED_MAP_POOL_DWORDS as i32 - count {
        gtest.used_map = UsedNodeMap::Heap(vec![0; n.max(0) as usize]);
    } else {
        gtest.used_map = UsedNodeMap::Arena(count as usize);
        arena.used_map_count += n;
    }
    let lc = arena.used_list_count;
    gtest.used_list = Some(lc as usize);
    let a = 64 - lc;
    let d = 32 - a;
    let cap = a + (d & (d >> 31));
    gtest.n_used_cap = cap;
    arena.used_list_count += cap;
}

/// Shared slot-7 cleanup of `CAABBTree` (0x35136520) / `COBBTree` (0x35191FA0): pool bitmaps
/// are cleared (all of it if the list overflowed, else the listed bits); heap ones are freed.
pub(crate) fn cleanup_used_map(gtest: &mut BvTestState, arena: &mut BvArena, n_nodes: i32) {
    match std::mem::take(&mut gtest.used_map) {
        UsedNodeMap::Arena(off) => {
            if gtest.i_last_used >= gtest.n_used_cap - 1 {
                let n = (((n_nodes - 1) >> 5) + 1) as usize;
                arena.used_map_pool[off..off + n].fill(0);
            } else if gtest.i_last_used >= 0 {
                let list = gtest.used_list.unwrap_or(0);
                for k in 0..=gtest.i_last_used as usize {
                    let v = arena.used_list_pool[list + k];
                    arena.used_map_pool[off + (v >> 5) as usize] &= !(1u32 << (v & 31));
                }
            }
            // The original leaves the pointer in the gtest.
            gtest.used_map = UsedNodeMap::Arena(off);
        }
        UsedNodeMap::Heap(_) => {
            // `operator delete[]`; the pointer is left dangling in the original.
            gtest.used_map = UsedNodeMap::None;
        }
        other => gtest.used_map = other,
    }
}

/// What slot 23 (`GetNodeContents`) forwards to geometry slot 56 (`GetPrimitiveList`): the
/// primitive range of a leaf.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct NodeContents {
    pub first_prim: i32,
    pub n_prims: i32,
}

/// The mesh arrays a tree build reads and permutes (`CTriMesh` +56 indices, +64 materials,
/// +72 foreign ids, +80 vertices, +160 auxiliary per-triangle ints).
#[derive(Debug)]
pub struct BuildMesh<'a> {
    pub vertices: &'a [Vec3],
    /// 3 vertex indices per triangle; permuted by the build.
    pub indices: &'a mut [i32],
    pub mats: Option<&'a mut [i8]>,
    pub foreign_idx: Option<&'a mut [i32]>,
    pub aux: Option<&'a mut [i32]>,
}

impl BuildMesh<'_> {
    /// `nTris` (+120).
    #[must_use]
    pub fn n_tris(&self) -> i32 {
        (self.indices.len() / 3) as i32
    }

    #[inline]
    pub(crate) fn vtx(&self, tri: usize, k: usize) -> Vec3 {
        self.vertices[self.indices[tri * 3 + k] as usize]
    }

    /// Swaps triangles `a` and `b` in every permuted array (indices, materials, foreign ids,
    /// auxiliary ints), in the original's order.
    pub(crate) fn swap_tris(&mut self, a: usize, b: usize) {
        for k in 0..3 {
            self.indices.swap(a * 3 + k, b * 3 + k);
        }
        if let Some(m) = self.mats.as_deref_mut() {
            m.swap(a, b);
        }
        if let Some(f) = self.foreign_idx.as_deref_mut() {
            f.swap(a, b);
        }
        if let Some(x) = self.aux.as_deref_mut() {
            x.swap(a, b);
        }
    }
}

/// A BV tree over a geometry's primitives: the 26-slot `CBVTree` vtable. Defaults are the
/// `CBVTree` base implementations (or `nop` where the base slot is pure / empty).
pub trait BvTree: Send + Sync + std::fmt::Debug {
    /// Slot 1 `GetType`.
    fn tree_type(&self) -> BvTreeType;
    /// Slot 2 `GetBBox`: the root box (64-byte box primitive).
    fn bbox(&self) -> BoxPrim;
    /// Slot 3 `MaxPrimsInNode`.
    fn max_prims_in_node(&self) -> i32;

    /// Slot 5 `SetGeomConvex` (base: nop).
    fn set_geom_convex(&mut self) {}

    /// Slot 6 `PrepareForIntersectionTest` (base 0x35024C60: map and list null, `iLast = −1`,
    /// capacity 0). Returns 1.
    fn prepare_for_intersection_test(&self, gtest: &mut BvTestState, _arena: &mut BvArena) -> i32 {
        gtest.i_last_used = -1;
        gtest.used_map = UsedNodeMap::None;
        gtest.used_list = None;
        gtest.n_used_cap = 0;
        1
    }

    /// Slot 7 `CleanupAfterIntersectionTest` (base: nop).
    fn cleanup_after_intersection_test(&self, _gtest: &mut BvTestState, _arena: &mut BvArena) {}

    /// Slot 8: node BV transformed by `(r, offset, scale)` and swept along unit `dir` by `len`.
    /// `None` where the slot is a nop (the output pointer is left unset).
    #[allow(clippy::too_many_arguments)]
    fn get_node_bv_swept_transformed(
        &self,
        _arena: &mut BvArena,
        _r: &Mat33,
        _offset: Vec3,
        _scale: f32,
        _dir: Vec3,
        _len: f32,
        _i_node: i32,
    ) -> Option<BvHandle> {
        None
    }

    /// Slot 9: the BV of node `i_node` transformed into another frame (`R·x·scale + offset`).
    /// Pure in `CBVTree` (`_purecall` aborts; the default panics likewise).
    fn get_node_bv_transformed(
        &self,
        _arena: &mut BvArena,
        _r: &Mat33,
        _offset: Vec3,
        _scale: f32,
        _i_node: i32,
    ) -> BvHandle {
        panic!("pure virtual CBVTree slot 9 (GetNodeBV transformed)")
    }

    /// Slot 10: the local BV swept along unit `dir` by `len`. `None` where the slot is a nop.
    fn get_node_bv_swept(
        &self,
        _arena: &mut BvArena,
        _dir: Vec3,
        _len: f32,
        _i_node: i32,
    ) -> Option<BvHandle> {
        None
    }

    /// Slot 11: the local BV of node `i_node`. Pure in `CBVTree`.
    fn get_node_bv(&self, _arena: &mut BvArena, _i_node: i32) -> BvHandle {
        panic!("pure virtual CBVTree slot 11 (GetNodeBV)")
    }

    /// Slot 12 `SplitPriority`: `size.x·y·z` of the record if its node is internal, else 0.
    fn split_priority(&self, _arena: &BvArena, _bv: BvHandle) -> f32 {
        0.0
    }

    /// Slot 13: children of a swept BV, swept again. `None` = nop.
    fn get_node_children_bvs_swept(
        &self,
        _arena: &mut BvArena,
        _parent: BvHandle,
        _dir: Vec3,
        _len: f32,
    ) -> Option<(BvHandle, BvHandle)> {
        None
    }

    /// Slot 14: children of a local BV. `None` = nop.
    fn get_node_children_bvs(
        &self,
        _arena: &mut BvArena,
        _parent: BvHandle,
    ) -> Option<(BvHandle, BvHandle)> {
        None
    }

    /// Slot 15: children of a transformed BV. `None` = nop.
    fn get_node_children_bvs_transformed(
        &self,
        _arena: &mut BvArena,
        _r: &Mat33,
        _offset: Vec3,
        _scale: f32,
        _parent: BvHandle,
    ) -> Option<(BvHandle, BvHandle)> {
        None
    }

    /// Slot 16 `ReleaseLastBVs`: BV-pool counter −= 2 (base: nop).
    fn release_last_bvs(&self, _arena: &mut BvArena) {}

    /// Slot 17 `ReleaseLastSweptBVs` (base: nop).
    fn release_last_swept_bvs(&self, _arena: &mut BvArena) {}

    /// Slot 18 `ResetCollisionArea` (voxel only; base: nop).
    fn reset_collision_area(&mut self) {}

    /// Slot 19 `GetMaxSkipDim` (base: 0).
    fn max_skip_dim(&self) -> f32 {
        0.0
    }

    /// Slot 20 `GetMemoryStatistics`: the bytes the tree reports to the sizer.
    fn memory_size(&self) -> usize {
        0
    }

    /// Slot 21 `Save` (base: nop).
    fn save(&self, _w: &mut MemWriter) {}

    /// Slot 23 `GetNodeContents`: the primitive range forwarded to geometry slot 56. Pure in
    /// `CBVTree` (`CHeightfieldBV` generates the cell triangles here instead, P1c).
    fn get_node_contents(&self, _i_node: i32) -> NodeContents {
        panic!("pure virtual CBVTree slot 23 (GetNodeContents)")
    }

    /// Slot 24 `GetNodeContentsIdx` (base 0x35024C90: `(0, 1)`): `(iStartPrim, nPrims)`.
    fn get_node_contents_idx(&self, _i_node: i32) -> NodeContents {
        NodeContents {
            first_prim: 0,
            n_prims: 1,
        }
    }

    /// Slot 25 `MarkUsedTriangle` (base: nop).
    fn mark_used_triangle(&self, _i_tri: i32, _gtest: &mut BvTestState, _arena: &mut BvArena) {}
}

/// Loads a tree of the given serialized type id (what `CTriMesh::Load`, 0x351D83B0, does after
/// reading the type: 0 OBB, 1 AABB, 2 SingleBox; then slot 22). `n_tris` is the mesh's
/// triangle count (the trees size their triangle → leaf maps from it).
///
/// # Errors
/// Truncated stream or an unknown type id.
pub fn load_tree(
    tree_type: i32,
    r: &mut MemReader<'_>,
    n_tris: i32,
) -> Result<Box<dyn BvTree>, StreamError> {
    match tree_type {
        0 => Ok(Box::new(ObbTree::load(r, n_tris)?)),
        1 => Ok(Box::new(AabbTree::load(r, n_tris)?)),
        2 => Ok(Box::new(SingleBoxTree::load(r)?)),
        t => Err(StreamError::BadValue(format!("BV tree type {t}"))),
    }
}

/// Engine idioms shared by the builders.
pub(crate) mod idiom {
    /// `if (a >= b) a = b` — the compiled `min` of the build loops (ties keep `b`).
    #[inline]
    #[must_use]
    pub fn sel_min(a: f32, b: f32) -> f32 {
        if a >= b {
            b
        } else {
            a
        }
    }

    /// `if (a <= b) a = b`.
    #[inline]
    #[must_use]
    pub fn sel_max(a: f32, b: f32) -> f32 {
        if a <= b {
            b
        } else {
            a
        }
    }

    /// Branch-free index of the largest of three values:
    /// `i = isneg(v0 − v1); j = isneg(v[i] − v2); (2j|i) & (((2j|i) ^ 2 | 4) >> 1)`.
    #[inline]
    #[must_use]
    pub fn idxmax3(v: [f32; 3]) -> usize {
        let i = ((v[0] - v[1]).to_bits() >> 31) as i32;
        let j = ((v[i as usize] - v[2]).to_bits() >> 31) as i32;
        let a = (j << 1) | i;
        (a & (((a ^ 2) | 4) >> 1)) as usize
    }

    /// Branch-free index of the smallest of three values (`isneg(v1 − v0)` first).
    #[inline]
    #[must_use]
    pub fn idxmin3(v: [f32; 3]) -> usize {
        let i = ((v[1] - v[0]).to_bits() >> 31) as i32;
        let j = ((v[2] - v[i as usize]).to_bits() >> 31) as i32;
        let a = (j << 1) | i;
        (a & (((a ^ 2) | 4) >> 1)) as usize
    }

    /// `{1, 2, 0}` (0x35209A30 / 0x3520A3C0).
    pub const INC_MOD3: [usize; 3] = [1, 2, 0];
    /// `{2, 0, 1}` (0x35209A40 / 0x3520A3D0).
    pub const DEC_MOD3: [usize; 3] = [2, 0, 1];
}

#[cfg(test)]
mod tests {
    use super::idiom::*;
    use super::*;

    #[test]
    fn idx_idioms() {
        assert_eq!(idxmax3([1.0, 2.0, 3.0]), 2);
        assert_eq!(idxmax3([3.0, 2.0, 1.0]), 0);
        assert_eq!(idxmax3([1.0, 3.0, 2.0]), 1);
        assert_eq!(idxmin3([1.0, 2.0, 3.0]), 0);
        assert_eq!(idxmin3([3.0, 1.0, 2.0]), 1);
        assert_eq!(idxmin3([3.0, 2.0, 1.0]), 2);
        // Ties: max keeps the first, min the first.
        assert_eq!(idxmax3([1.0, 1.0, 1.0]), 0);
        assert_eq!(idxmin3([1.0, 1.0, 1.0]), 0);
    }

    #[test]
    fn used_list_allocation() {
        let mut a = BvArena::new();
        let mut g1 = BvTestState::default();
        let mut g2 = BvTestState::default();
        let mut g3 = BvTestState::default();
        alloc_used_map_and_list(&mut g1, &mut a, 100);
        alloc_used_map_and_list(&mut g2, &mut a, 33);
        alloc_used_map_and_list(&mut g3, &mut a, 1);
        assert_eq!(g1.used_map, UsedNodeMap::Arena(0));
        assert_eq!(g2.used_map, UsedNodeMap::Arena(4));
        assert_eq!((g1.n_used_cap, g2.n_used_cap, g3.n_used_cap), (32, 32, 0));
        assert_eq!(a.used_map_count, 4 + 2 + 1);
    }

    #[test]
    fn box_words_roundtrip() {
        let b = BoxPrim {
            basis: Mat33::from_array([1., 2., 3., 4., 5., 6., 7., 8., 9.]),
            oriented: 5,
            center: Vec3::new(-1.0, 0.5, 2.0),
            size: Vec3::new(0.25, 3.0, 9.0),
        };
        let mut w = [0u32; 34];
        box_to_words(&mut w, rec::ORIG, &b);
        assert_eq!(box_from_words(&w, rec::ORIG), b);
    }
}
