//! **P4a — `CPhysicalEntity`** (type 1 "static", 0x1C0 bytes; vtable 0x351EFE38; ctor
//! 0x35071D30) and the data every entity class shares: the placeholder header the world uses
//! (bbox, grid cells, ids, simclass, list links), the parts (`geom_part`, 136 bytes) and the
//! class-specific payload ([`EntityKind`]). Member layout: `entities.md` §2.1; part record §2.2.
//!
//! Entities live in the world's store ([`crate::world::World::ents`]) and refer to each other by
//! [`EntIdx`]. The binary's virtual slots are free functions over `&mut World` in this module
//! (base slots) and in [`super::rigid`] (rigid overrides); dispatch on [`EntityKind`] mirrors
//! the vtables.

pub mod bbox;
pub mod geometry;
pub mod params;

use crate::geometry::PhysGeometry;
use crate::math::{Quat, Vec3};
use crate::world::EntIdx;
use std::sync::Arc;

pub use bbox::compute_bbox;
pub use geometry::add_geometry;

/// `geom_part` (136 bytes, array at `entity+0x118`).
#[derive(Clone, Debug)]
pub struct Part {
    /// `+0` position in the entity frame (already multiplied by the entity scale).
    pub pos: Vec3,
    /// `+12` orientation in the entity frame.
    pub q: Quat,
    /// `+28` scale (already multiplied by the entity scale).
    pub scale: f32,
    /// `+32` / `+44` world bounding box of the part (written by ComputeBBox with flag 2).
    pub bbox: [Vec3; 2],
    /// `+56` the geometry.
    pub pgeom: Arc<PhysGeometry>,
    /// `+64` the proxy geometry (= `pgeom` unless a proxy was set).
    pub pgeom_proxy: Arc<PhysGeometry>,
    /// `+72` part id.
    pub id: i32,
    /// `+76` mass.
    pub mass: f32,
    /// `+80` flags (collision classes, [`crate::ids::part_flags`]).
    pub flags: u32,
    /// `+84` collider flags.
    pub flags_collider: u32,
    /// `+88` max half-size of the geometry bbox (unscaled).
    pub max_dim: f32,
    /// `+92` minimal contact distance.
    pub min_contact_dist: f32,
    /// `+96` bits 0–9 surface index, bits 10–23 idmatBreakable (0x3FFF = -1).
    pub surface_bits: u32,
    /// `+100` bits 0–30 number of material mappings, bit 31 flag.
    pub nmats_bits: u32,
    /// `+104` material mapping (`None` = the `phys_geometry`'s).
    pub mat_mapping: Option<Vec<i32>>,
    /// `+112` tetra lattice (not ported: always none).
    pub has_lattice: bool,
    /// `+128` (cleared on add).
    pub unk128: u64,
    /// `+120` new-coords pointer redirected to a temporary copy by ComputeBBoxRE
    /// (0x350ABF20): `Some(box)` holds the copy's box (`+32` of the 56-byte record), which
    /// ComputeBBox with flag 2 writes and UpdatePosition commits. `None` = the part itself.
    pub bbox_tmp: Option<[Vec3; 2]>,
}

impl Part {
    /// `surface_idx` (`+96` bits 0–9).
    #[inline]
    #[must_use]
    pub fn surface_idx(&self) -> i32 {
        (self.surface_bits & 0x3FF) as i32
    }
}

/// A whole-entity coordinate frame (`{Vec3 pos; Quat q}`): what `entity+200` (pNewCoords)
/// points at — the entity's own `pos`/`q` (`+408`/`+420`) for the base class, the stepped
/// `pos_new`/`q_new` (`+448`/`+460`) for rigid bodies.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Coords {
    pub pos: Vec3,
    pub q: Quat,
}

/// Which entity class (vtable) an entity has.
#[derive(Clone, Debug)]
pub enum EntityKind {
    /// `CPhysicalEntity` (type 1).
    Static,
    /// `CRigidEntity` (type 2).
    Rigid(Box<super::rigid::RigidData>),
    /// `CPhysArea` (GetType 9; not a `CPhysicalEntity`, only the placeholder header is used).
    Area(Box<crate::world::areas::AreaData>),
}

/// An entity object: the placeholder header the world reads (`+8 … +136`), the
/// `CPhysicalEntity` members, and the class payload.
#[derive(Clone, Debug)]
pub struct Entity {
    // ---- placeholder header ----
    /// `+8` / `+20` world bounding box.
    pub bbox: [Vec3; 2],
    /// `+32` pForeignData (opaque).
    pub foreign_data: u64,
    /// `+40` iForeignData.
    pub i_foreign_data: i16,
    /// `+42` iForeignFlags.
    pub i_foreign_flags: i16,
    /// `+44`, `+46`, `+48`, `+50`: grid cell bbox `ix0, iy0, ix1, iy1` (-2 = not gridded,
    /// -3 = hidden / out of grid).
    pub grid_cells: [i16; 4],
    /// `+52` first grid thunk (20 bits).
    pub grid_thunk: u32,
    /// `+64` "processed" counter / caller-lock bits (bit `1 << iCaller` marks RWI skip;
    /// `+0x20000` = has queued commands).
    pub processed: u32,
    /// `+68` bits 0–22 id, bit 23 grid flag, byte 3 = simclass (signed).
    pub id_bits: u32,
    /// `+80` deletion state (0 alive, > 0 pending).
    pub deleted: i32,
    /// `+84` time since deleted.
    pub time_deleted: f32,
    /// `+88` POD refcount.
    pub pod_refs: i32,
    /// `+92` flags (default 0x8640, [`crate::ids::entity_flags`]).
    pub flags: u32,
    /// `+96`.
    pub unk96: i32,
    /// `+104` next in the simclass list.
    pub list_next: EntIdx,
    /// `+112` previous in the simclass list.
    pub list_prev: EntIdx,
    /// `+128` int16 POD refs, `+130` int16.
    pub unk128: u32,
    /// `+132` int16 = simclass of the list the entity is in (-1 none), bits 24–27 list
    /// insertion mode, bits 28–31 marker flag.
    pub list_bits: u32,
    /// `+134` byte group mark (island building: 0 free, 1 queued, 2 done).
    pub group_mark: u8,
    /// `+136` group index.
    pub group_idx: i32,
    /// `+144` next in the island (group) list.
    pub group_next: EntIdx,
    /// `+152` next in the island BFS queue.
    pub bfs_next: EntIdx,
    /// `+160` next in a job list.
    pub job_next: EntIdx,

    // ---- CPhysicalEntity ----
    /// `+168` secondary coords pos, `+180` q (prev/next frame, inferred).
    pub pos2: Vec3,
    pub q2: Quat,
    /// `+196` scale.
    pub scale: f32,
    /// `+208` / `+216` colliders (entities this one is in contact with).
    pub colliders: Vec<EntIdx>,
    /// `+232` group leader (living).
    pub unk232: EntIdx,
    /// `+248`.
    pub unk248: i32,
    /// `+252` sleep/idle timer (`timeIdle`).
    pub time_idle: f32,
    /// `+256` max idle time (`maxTimeIdle`).
    pub max_time_idle: f32,
    /// `+260`.
    pub unk260: i32,
    /// `+264` = 1 (cleared by lattice break).
    pub unk264: i32,
    /// `+280` / `+288` parts.
    pub parts: Vec<Part>,
    /// `+292` parts capacity (grows by 4; the static dummy part array counts as 0).
    pub parts_cap: i32,
    /// `+296` next free part id.
    pub next_part_id: i32,
    /// `+304` ground planes {origin, n} (params 20).
    pub ground_planes: Vec<(Vec3, Vec3)>,
    /// `+332` params 26 byte.
    pub byte332: u8,
    /// `+408` position.
    pub pos: Vec3,
    /// `+420` orientation.
    pub q: Quat,
    /// `+440` pStructure (breakable structure; not ported — always none).
    pub has_structure: bool,
    /// Engine type (`GetType`, slot 1).
    pub etype: i32,
    /// Class payload.
    pub kind: EntityKind,
}

impl Entity {
    /// `CPhysicalEntity` ctor 0x35071D30 (with the placeholder ctor it chains to).
    #[must_use]
    pub fn new_base(etype: i32, kind: EntityKind) -> Self {
        Self {
            bbox: [Vec3::ZERO; 2],
            foreign_data: 0,
            i_foreign_data: 0,
            i_foreign_flags: 0,
            grid_cells: [-2, -2, -2, -2],
            grid_thunk: 0,
            processed: 0,
            // `&= 0xFF000000`, then `|= 0x7FFFFF` (id -1), simclass byte 0.
            id_bits: 0x007F_FFFF,
            deleted: 0,
            time_deleted: 0.0,
            pod_refs: 0,
            flags: crate::ids::entity_flags::DEFAULT,
            unk96: 0,
            list_next: EntIdx::NONE,
            list_prev: EntIdx::NONE,
            unk128: 0,
            // `|= 0xFFFF`, bits 24–27 = 1, bits 28–31 = 0.
            list_bits: 0x0100_FFFF,
            group_mark: 0,
            group_idx: -1,
            group_next: EntIdx::NONE,
            bfs_next: EntIdx::NONE,
            job_next: EntIdx::NONE,
            pos2: Vec3::ZERO,
            q2: Quat::IDENTITY,
            scale: 1.0,
            colliders: Vec::new(),
            unk232: EntIdx::NONE,
            unk248: 0,
            time_idle: 0.0,
            max_time_idle: 0.0,
            unk260: 0,
            unk264: 1,
            parts: Vec::new(),
            parts_cap: 0,
            next_part_id: 0,
            ground_planes: Vec::new(),
            byte332: 0,
            pos: Vec3::ZERO,
            q: Quat::IDENTITY,
            has_structure: false,
            etype,
            kind,
        }
    }

    /// Simulation class (`+71`, signed byte).
    #[inline]
    #[must_use]
    pub fn sim_class(&self) -> i32 {
        i32::from((self.id_bits >> 24) as u8 as i8)
    }

    /// Set the simclass byte.
    #[inline]
    pub fn set_sim_class(&mut self, sc: i32) {
        self.id_bits = (self.id_bits & 0x00FF_FFFF) | ((sc as u8 as u32) << 24);
    }

    /// Entity id (`+68` low 23 bits, sign-extended).
    #[inline]
    #[must_use]
    pub fn id(&self) -> i32 {
        ((self.id_bits << 9) as i32) >> 9
    }

    /// The simclass of the list the entity is linked into (`+132` int16; -1 none).
    #[inline]
    #[must_use]
    pub fn list_class(&self) -> i32 {
        i32::from(self.list_bits as u16 as i16)
    }

    /// The whole-entity new-coords frame (`*(entity+200)`).
    #[inline]
    #[must_use]
    pub fn new_coords(&self) -> Coords {
        match &self.kind {
            EntityKind::Rigid(r) => Coords {
                pos: r.pos_new,
                q: r.q_new,
            },
            _ => Coords {
                pos: self.pos,
                q: self.q,
            },
        }
    }

    /// Rigid payload.
    #[inline]
    #[must_use]
    pub fn rigid(&self) -> Option<&super::rigid::RigidData> {
        match &self.kind {
            EntityKind::Rigid(r) => Some(r),
            _ => None,
        }
    }

    /// Rigid payload (mutable).
    #[inline]
    pub fn rigid_mut(&mut self) -> Option<&mut super::rigid::RigidData> {
        match &mut self.kind {
            EntityKind::Rigid(r) => Some(r),
            _ => None,
        }
    }
}
