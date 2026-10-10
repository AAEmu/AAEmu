//! The `geometry_under_test` protocol as the generic `CGeometry::Intersect`
//! ([`crate::geometry::trimesh::intersect`]) drives it (`geometry.md` §2.1 slots 46, 53–59,
//! §2.3, §3.4).
//!
//! The per-side record is P1a's [`GeomUnderTest`] (the 392-byte record of the binary); the
//! primitives' slots 53/56/57/58/59 and the ray's slot 54 are P1a's
//! ([`crate::geometry::shapes::protocol`]). This module adds what the mesh side needs on top:
//!
//! * [`Side`]: the record plus the state the port keeps next to it — the used-node state of
//!   a P1b BV tree ([`BvTestState`], `+16..+44` for AABB/OBB trees), the triangle index of each
//!   52-byte triangle record (`+48`), and the sweep fields of side A (`+236..+264`);
//! * [`CallerArena`]: the per-caller scratch the intersection code shares across calls
//!   (P1b's BV pools, the contact / area pool counters, a few words);
//! * [`IntersectCtx`]: one `Intersect` call (both sides, parameters, tables, contacts);
//! * [`GeomProtocol`]: the slot interface each geometry offers to the generic `Intersect`
//!   (type, priority, `minVtxDist`, `+24`, BV tree, slots 53/54/55, BV slot 23 → slot 56), with
//!   P1a's [`IntersectionProtocol`] (slots 49/57/58/59) as supertrait.
//!
//! Implementations: [`mesh`] (`CTriMesh`), [`shapes`] (adapters for P1a's box, sphere,
//! cylinder, capsule and ray classes).
//!
//! **Contacts.** In the binary the contacts live in a 64-entry per-caller pool and registration
//! writes single fields of the next entry, so fields a writer skips keep stale contents. The
//! port appends fresh [`GeomContact`]s to the call's list (as P1a's ray slot 54 does); fields
//! the original never writes in a path are defaulted (documented at the writers).

pub mod mesh;
pub mod shapes;

use crate::collision::contact::{ContactArea, GeomContact, IntersectionParams, PrimInters};
use crate::collision::pairs::PairTables;
use crate::collision::swept::UnprojTables;
use crate::geometry::bvtree::{BvArena, BvRecord, BvTestState, BvTree};
use crate::geometry::shapes::protocol::{GeomUnderTest, IntersectionProtocol, UsedNodeMap};
use crate::ids::GeomType;
use crate::math::Vec3;

/// One side of an `Intersect` call.
#[derive(Clone, Debug)]
pub struct Side {
    /// The `geometry_under_test` record.
    pub gt: GeomUnderTest,
    /// Used-node state of the side's P1b tree (meshes; `+16..+44` of the record).
    pub bvs: BvTestState,
    /// Triangle index (`+48` of a 52-byte record) of each entry of `gt.prims`.
    pub prim_idx: Vec<i32>,
    /// Triangle index of each entry of `gt.unproj_prims`.
    pub unproj_idx: Vec<i32>,
    /// `+236` sweep direction (world, unit).
    pub sweep_dir: Vec3,
    /// `+248` sweep direction in the local frame.
    pub sweep_dir_loc: Vec3,
    /// `+260` sweep length (world).
    pub sweep_len: f32,
    /// `+264` sweep length (local).
    pub sweep_len_loc: f32,
}

impl Side {
    #[must_use]
    pub fn new(i_caller: i32) -> Self {
        Self {
            gt: GeomUnderTest::new(i_caller),
            bvs: BvTestState::default(),
            prim_idx: Vec::new(),
            unproj_idx: Vec::new(),
            sweep_dir: Vec3::ZERO,
            sweep_dir_loc: Vec3::ZERO,
            sweep_len: 0.0,
            sweep_len_loc: 0.0,
        }
    }

    /// Whether BV node `i` is marked used (the traversal's `map[i >> 5] >> (i & 31) & 1`; a
    /// null map reads a zeroed local). Meshes keep the map in [`Side::bvs`]; the primitives'
    /// `CSingleBoxTree` points it at the record's own `+32` word.
    #[must_use]
    pub fn node_used(&self, arena: &BvArena, i: i32) -> i32 {
        if let Some(u) = self.bvs.is_node_used(arena, i) {
            return i32::from(u);
        }
        match &self.gt.used.map {
            UsedNodeMap::Null => 0,
            UsedNodeMap::Inline => (self.gt.used.word >> (i & 31)) & 1,
            UsedNodeMap::Words(w) => w
                .get((i >> 5) as usize)
                .map_or(0, |v| ((*v as i32) >> (i & 31)) & 1),
        }
    }
}

/// The per-caller scratch (base 0x35256F80 + iCaller·0x39FB8) as far as the intersection
/// routines share it across calls.
#[derive(Clone, Debug)]
pub struct CallerArena {
    /// BV records and used-node maps (P1b).
    pub bv: BvArena,
    /// +0xE9B8: contacts in use in the 64-entry contact pool.
    pub n_contacts: i32,
    /// +0xF2C0: contact areas handed out (32 max).
    pub n_areas: i32,
    /// +0x10EC4: contact-area points handed out (256 max).
    pub n_area_pts: i32,
    /// +0x16EC8 / +0x16ECC: cleared with the contacts; `CTriMesh` slot 53 copies the first
    /// into the second.
    pub w16ec8: u64,
    pub w16ecc: u32,
    /// +0x39F68: OVERLAP scratch, −1 at the start of every `Intersect`.
    pub overlap_scratch: i32,
}

impl Default for CallerArena {
    fn default() -> Self {
        Self {
            bv: BvArena::new(),
            n_contacts: 0,
            n_areas: 0,
            n_area_pts: 0,
            w16ec8: 0,
            w16ecc: 0,
            overlap_scratch: 0,
        }
    }
}

/// Entries of the contact pool.
pub const CONTACT_POOL: i32 = 64;
/// Areas in the area pool.
pub const AREA_POOL: i32 = 32;
/// Points in the area-point pool.
pub const AREA_PT_POOL: i32 = 256;

impl CallerArena {
    #[must_use]
    pub fn new() -> Self {
        Self::default()
    }

    /// The area the original attaches to a contact slot (`Intersect` for the first one,
    /// `RegisterIntersection` for the next): `None` when areas are disabled or a pool is
    /// exhausted, else an empty area of `256 − points used` points. The counters are advanced
    /// by the code that keeps the area.
    #[must_use]
    pub fn peek_area(&self, no_area: bool) -> Option<ContactArea> {
        if no_area || self.n_areas >= AREA_POOL || self.n_area_pts >= AREA_PT_POOL {
            return None;
        }
        Some(crate::collision::swept::new_area(
            (AREA_PT_POOL - self.n_area_pts) as usize,
        ))
    }
}

/// Where a contact's border pointer (`geom_contact +88`) points.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum BorderKind {
    /// `&contact.pt` (+8): one point, the contact point itself.
    #[default]
    Pt,
    /// `&contact.center` (+112).
    Center,
    /// A buffer of its own (the per-caller border-point pool).
    Pool,
}

/// One `Intersect` call: both sides (`s[0]` = A, `s[1]` = B), parameters, tables, the per-caller
/// arena and the contacts written so far (`contacts.len()` is `*pnContacts`).
pub struct IntersectCtx<'a> {
    pub tables: &'a PairTables,
    pub unproj: &'a UnprojTables,
    pub ip: IntersectionParams,
    /// `ip+112`: `A.+24 & B.+24`.
    pub ip_convex: i32,
    /// `ip+98`: stop after the first contact.
    pub first_only: bool,
    pub arena: &'a mut CallerArena,
    pub s: [Side; 2],
    /// The contacts of this call.
    pub contacts: Vec<GeomContact>,
    /// Border kind of each contact.
    pub border_kind: Vec<BorderKind>,
    /// Area attached to the next contact slot.
    pub next_area: Option<ContactArea>,
}

impl std::fmt::Debug for IntersectCtx<'_> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("IntersectCtx")
            .field("ip", &self.ip)
            .field("contacts", &self.contacts.len())
            .finish_non_exhaustive()
    }
}

impl IntersectCtx<'_> {
    /// `*pnContacts`.
    #[must_use]
    pub fn n_contacts(&self) -> i32 {
        self.contacts.len() as i32
    }

    /// Appends a contact with its border kind.
    pub fn push_contact(&mut self, c: GeomContact, kind: BorderKind) {
        self.contacts.push(c);
        self.border_kind.push(kind);
    }
}

/// A BV tree owned by or borrowed from a geometry.
pub enum BvRef<'a> {
    Borrowed(&'a dyn BvTree),
    Owned(Box<dyn BvTree>),
}

impl BvRef<'_> {
    /// The tree.
    #[must_use]
    pub fn tree(&self) -> &dyn BvTree {
        match self {
            BvRef::Borrowed(t) => *t,
            BvRef::Owned(b) => &**b,
        }
    }
}

/// The geometry side of the generic `Intersect` (one implementation per class).
pub trait GeomProtocol: IntersectionProtocol + std::fmt::Debug {
    /// Slot 1.
    fn proto_type(&self) -> GeomType;
    /// `+16` minVtxDist.
    fn proto_min_vtx_dist(&self) -> f32;
    /// `+20` collision priority (ray 0, mesh / heightfield 1, primitives 3).
    fn proto_priority(&self) -> i32;
    /// `+24` (1 for the solid primitives, the mesh's convexity flag).
    fn proto_flag24(&self) -> i32;
    /// Slot 46 `GetBVTree`.
    fn proto_bv_tree(&self) -> BvRef<'_>;

    /// Slot 53 `PrepareForIntersectionTest(gtest, other, gtestOther, bKeepPrevContacts)`.
    /// Returns 0 to abort the test.
    fn proto_prepare(&self, ctx: &mut IntersectCtx<'_>, side: usize, keep_prev: bool) -> i32;

    /// Slot 55 `CleanupAfterIntersectionTest` (base: nop).
    fn proto_cleanup(&self, _ctx: &mut IntersectCtx<'_>, _side: usize) {}

    /// BV slot 23 `GetNodeContents` followed by slot 56 `GetPrimitiveList`: the primitives of
    /// leaf `i_node` that `collider` may touch, into the side's `+288` buffer; their number.
    fn proto_node_contents(
        &self,
        ctx: &mut IntersectCtx<'_>,
        side: usize,
        i_node: i32,
        collider: &BvRecord,
        collider_used: i32,
        b_local: i32,
    ) -> i32 {
        let _ = collider_used;
        let c = self.proto_bv_tree().tree().get_node_contents(i_node);
        self.proto_primitive_list(ctx, side, c.first_prim, c.n_prims, collider, b_local)
    }

    /// Slot 56 `GetPrimitiveList`.
    fn proto_primitive_list(
        &self,
        ctx: &mut IntersectCtx<'_>,
        side: usize,
        i_start: i32,
        n_prims: i32,
        collider: &BvRecord,
        b_local: i32,
    ) -> i32;

    /// Slot 54 `RegisterIntersection(primThis, primOther, gtestThis, gtestOther, inters)`: the
    /// primitives are entries `i_this` / `i_other` of the two sides' `+288` buffers. Returns
    /// the number of contacts added.
    fn proto_register(
        &self,
        ctx: &mut IntersectCtx<'_>,
        side: usize,
        other: &dyn GeomProtocol,
        i_this: usize,
        i_other: usize,
        inters: &mut PrimInters,
    ) -> i32;
}
