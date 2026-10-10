//! **P3 — world queries** (`world.md` §6): `RayWorldIntersection`, `PrimitiveWorldIntersection`,
//! the entity-level queries and their queued variants.
//!
//! | function | address | port |
//! |---|---|---|
//! | RayWorldIntersection (world slot 42) | 0x351AA730 | [`World::rwi`] ([`rwi`]) |
//! | RayWorldIntersection arg-list wrapper (slot 104) | 0x35096EE0 | [`World::rwi`] (same params) |
//! | ray context ctor | 0x351AC4F0 | [`rwi::RayCtx::new`] |
//! | grid ray walk ("DrawRayOnGrid") | 0x351AD6A0 | [`rwi`] `ray_grid_walk` |
//! | per-cell callback | 0x351AC640 | [`rwi`] `ray_cell` |
//! | per-part hit registration | 0x351ABC10 | [`rwi`] `register_ray_contacts` |
//! | part material mapping | 0x35037930 | [`map_surface`] |
//! | water-plane ray (global + big areas) | 0x351A9540 | [`water`] |
//! | area ray test (plane-only areas) | 0x3504E5E0 / 0x3504CF00 | [`water::area_ray`] |
//! | terrain ray (slot 44) | 0x351A9BA0 | [`crate::world::terrain`] |
//! | queued RWI / PWI (flag 0x800) | 0x351AA730 / 0x35093100 head | [`queue`] |
//! | process queued queries (slot 43) | 0x351A9FC0 | [`World::process_queued_queries`] |
//! | queue ring growth | 0x351AC3A0 / 0x35098710 | [`queue`] (a `VecDeque`) |
//! | PrimitiveWorldIntersection wrapper (slot 64) | 0x350964F0 | [`World::pwi`] |
//! | PrimitiveWorldIntersection core (slot 65) | 0x35093100 | [`World::pwi`] ([`pwi`]) |
//! | sweep-axis entity quicksort | 0x3507C1E0 | [`pwi`] |
//! | copy `intersection_params` | 0x350961E0 | (struct copy) |
//! | RayTraceEntity (slot 61) | 0x3508AA10 | [`World::ray_trace_entity`] ([`entity`]) |
//! | CollideEntityWithPrimitive (slot 60) | 0x350874B0 | [`World::collide_entity_with_primitive`] |
//! | CollideEntityWithBeam (slot 59) | 0x35086DB0 | [`World::collide_entity_with_beam`] |
//! | point-in-box (oriented) | 0x350974A0 | [`entity`] |
//! | AABB overlap (`min,max` pairs) | 0x35038180 | [`aabb_overlap6`] |
//!
//! # Engine facts kept
//!
//! * **RWI and PWI need the world's temp entity pools.** RWI returns 0 while `world+792` is
//!   null; the pool is allocated by the first entity registration that grows the pools
//!   (`World::grow_pools`, i.e. `pool_size != 0`) — with the oracle that is the first
//!   `TimeStep` with an ordinary entity (creation from the external thread is queued until
//!   then). The port tests [`World::temp_pools_ready`].
//! * Part flags must contain RWI `geomFlagsAll` (high word of the flags, 0 → 0x8000; flag 0x40
//!   ORs in 1; flag 0x400 moves it to `geomFlagsAny` and clears `geomFlagsAll`) and intersect
//!   `geomFlagsAny` (default 0xFDFF).
//! * **"DrawRayOnGrid"** (ArcheAge addition): unless `gEnv` byte 289 is set, the grid walk
//!   rejects rays whose grid-axis `|dir.x|` or `|dir.y|` exceed an integer config value
//!   (`gEnv+112` → slot 20 → `+248`). It is a world setting here:
//!   [`QueryState::ray_dir_limit`] (default 1000, the oracle's value) and
//!   [`QueryState::env_byte_289`]. Rejected rays still get the terrain / water / hit-last /
//!   out-of-grid-cell results, which run before the walk.
//! * Hit 0 is the closest solid hit (`dist = -1` and `pt = org + dir` when there is none);
//!   pierceable hits fill slots `1..` (sorted ascending from the front, or from the back of the
//!   buffer when flag 0x200 is set and the surface lacks flag 0x200; flag 0x200 then permutes
//!   the buffer so that both runs are in order).
//!
//! # Kept bugs (documented at their sites)
//!
//! * A hit-last cache (`phitLast`) entity is only re-tested if grid cell (0, 0) is non-empty
//!   (the callback is invoked for cell (0, 0) with a fake thunk).
//! * After the hit-last test the ray is shortened to `hits[0].pt − org` whenever
//!   `hits[0].dist > 0` — which is also the case when nothing was hit (`dist = 1e10`), so the
//!   **stale** `hits[0].pt` the caller left in the buffer becomes the new ray end.
//! * The water-plane hit writes `ipart = 0` into `hits[0]` whatever slot the hit goes to.
//! * Water surface flags are read at `flags[waterMat]` without masking (index −1 by default
//!   reads the last entry of the 4th extended surface table, assumed 0 here).
//!
//! # Not ported (documented gaps)
//!
//! * Physics-on-demand activation in the ray callback (no POD grid in the port).
//! * Placeholders (`+56` links) and the "grid changed during query" re-fetch (`world+1108`).
//! * Entity slot 57 overrides (particles, ropes — P5/P6): the base returns no contacts.
//! * The gwd `iStartNode` (`+88`) seeded from `phitLast.iNode` and the `intersection_params`
//!   byte `+98` ("stop at first triangle", RWI flag 0x8000): only meshes (P1c) read them.
//! * Area ray tests for geometry areas, spline/polygon areas and areas with a circle
//!   (`+544`): only plane-only areas (the global water area) are tested.
//! * The debug ray callback (`world+656`, vars `+228 & 0x40`) and the profiler.

#![allow(clippy::neg_cmp_op_on_partial_ord)] // `!(a < b)` = the binary's NaN behaviour

pub mod entity;
pub mod pwi;
pub mod queue;
pub mod rwi;
pub mod water;

#[cfg(test)]
mod tests;

use super::World;
use crate::collision::contact::{GeomContact, IntersectionParams};
use crate::entities::base::Part;
use crate::geometry::primitives::Primitive;
use crate::math::Vec3;

pub use pwi::{PwiParams, PwiResult};
pub use queue::{QueuedPwiResult, QueuedRwiResult};
pub use rwi::{HitLast, RayHit, RwiParams};

/// RWI flag bits (`SRWIParams +52`).
pub mod rwi_flags {
    /// Bits 0–3: the ray's pierceability (hits on surfaces with a higher pierceability are
    /// pierceable hits).
    pub const PIERCEABILITY_MASK: u32 = 0xF;
    /// Ray collision class: ORs 1 into `geomFlagsAll`.
    pub const COLLTYPE_1: u32 = 0x40;
    /// Ignore hits on faces facing away from the ray (all hits).
    pub const IGNORE_BACK_FACES: u32 = 0x80;
    /// Ignore solid hits on faces facing away from the ray.
    pub const IGNORE_SOLID_BACK_FACES: u32 = 0x100;
    /// Pierceable hits go to the back of the buffer (unless the surface has 0x200) and the
    /// buffer is ordered afterwards.
    pub const SORT: u32 = 0x200;
    /// `geomFlagsAny` = the collision class instead of `geomFlagsAll`.
    pub const COLLTYPE_ANY: u32 = 0x400;
    /// Queue the request (processed by world slot 43, result through event 9).
    pub const QUEUE: u32 = 0x800;
    /// Parts without collision flags (`flags & 0x8FFB == 0`) are always pierceable.
    pub const FORCE_PIERCEABLE_NONCOLL: u32 = 0x1000;
    /// Write the closest hit back into the hit-last cache.
    pub const UPDATE_LAST_HIT: u32 = 0x4000;
    /// `intersection_params +98` (meshes stop at the first triangle; P1c).
    pub const STOP_AT_FIRST_TRI: u32 = 0x8000;
    /// High word: the collision class (`geomFlagsAll`, 0 → 0x8000).
    pub const COLLTYPE_SHIFT: u32 = 16;
}

/// Entity-type mask bits (`objtypes` / `entTypes`): `1 << simclass` plus the extras.
pub mod ent_types {
    pub const STATIC: u32 = 0x1;
    pub const SLEEPING_RIGID: u32 = 0x2;
    pub const RIGID: u32 = 0x4;
    pub const LIVING: u32 = 0x8;
    pub const INDEPENDENT: u32 = 0x10;
    /// Simclass 5: areas (water areas are ray-tested through their plane).
    pub const AREAS: u32 = 0x20;
    pub const TERRAIN: u32 = 0x100;
    /// The global area's water plane (only effective while `world.water_objtype` has 0x200).
    pub const WATER: u32 = 0x200;
    /// Everything the game commonly uses (`0x13F`).
    pub const ALL: u32 = 0x13F;
}

/// Query-related world state the port keeps outside the binary's layout.
#[derive(Debug)]
pub struct QueryState {
    /// The "DrawRayOnGrid" limit (`gEnv+112` → slot 20 → `+248`): rays whose grid-axis
    /// `|dir.x|` or `|dir.y|` exceed it are not walked through the grid. `None` = no limit.
    /// Default `Some(1000)` (the oracle's value).
    pub ray_dir_limit: Option<i32>,
    /// `gEnv` byte 289: when set the limit is not applied.
    pub env_byte_289: bool,
    /// Queued RWI requests (`world+21696` ring, flag 0x800).
    pub rwi_queue: std::collections::VecDeque<queue::QueuedRwi>,
    /// Queued PWI requests (`world+21752` ring).
    pub pwi_queue: std::collections::VecDeque<queue::QueuedPwi>,
    /// Results of processed queued queries waiting for `PumpLoggedEvents` (event types 9 and
    /// 10 are logged; the pump calls their own callback).
    pub logged_results: Vec<queue::QueuedResult>,
    /// `world+1616` the terrain heightfield basis (SetHeightfieldData).
    pub hf_basis: crate::math::Mat33,
}

impl Default for QueryState {
    fn default() -> Self {
        Self {
            ray_dir_limit: Some(1000),
            env_byte_289: false,
            rwi_queue: std::collections::VecDeque::new(),
            pwi_queue: std::collections::VecDeque::new(),
            logged_results: Vec::new(),
            hf_basis: crate::math::Mat33::IDENTITY,
        }
    }
}

/// `sub_35037930(entity, id, ipart)`: the surface index of material id `id` on part `ipart`:
/// a negative id takes the part's surface index (`+96` bits 0–9, sign-extended); with a
/// material mapping (`+104`) the result is `mapping[id < nMats ? id : 0]`. `ipart` out of
/// range returns `id` unchanged.
///
/// Quirk kept: a negative id with a mapping indexes `mapping[negative]` in the original (out
/// of bounds); the port returns the raw id in that case.
#[must_use]
pub fn map_surface(parts: &[Part], id: i32, ipart: i32) -> i32 {
    if ipart < 0 || ipart as usize >= parts.len() {
        return id;
    }
    let p = &parts[ipart as usize];
    let v7 = ((id >> 31) & ((((p.surface_bits << 22) as i32) >> 22) - id)) + id;
    let mapping: &[i32] = match &p.mat_mapping {
        Some(m) => m,
        None => &p.pgeom.mat_mapping,
    };
    if mapping.is_empty() {
        return v7;
    }
    let nmats = ((p.nmats_bits << 1) as i32) >> 1;
    let idx = v7 & ((v7.wrapping_sub(nmats)) >> 31);
    if idx < 0 {
        return v7;
    }
    mapping.get(idx as usize).copied().unwrap_or(0)
}

/// `idmatOrg` of a hit: `id + ((id >> 31) & (sext10(part.surface) + 1))` (low 16 bits).
#[must_use]
pub(crate) fn idmat_org(id: i32, surface_bits: u32) -> i16 {
    let s = i32::from((((surface_bits as u16) << 6) as i16) >> 6);
    (id as i16).wrapping_add(((id >> 31) & (s + 1)) as i16)
}

/// `sub_35038180`: AABB overlap of `a = [min, max]` and `b = [min, max]` with the binary's
/// arithmetic (`max(max(dy, dx), dz) < 0`, the idiom min/max).
#[must_use]
pub fn aabb_overlap6(a: &[Vec3; 2], b: &[Vec3; 2]) -> bool {
    let d = |i: usize| {
        ((((a[0][i] + a[1][i]) - b[0][i]) - b[1][i]).abs() - (a[1][i] - a[0][i]))
            - (b[1][i] - b[0][i])
    };
    let v2 = d(1);
    let v3 = d(0);
    let v4 = ((v3 - v2).abs() + (v3 + v2)) * 0.5;
    let v5 = d(2);
    (((v4 - v5).abs() + (v5 + v4)) * 0.5) < 0.0
}

impl World {
    /// Geometry slot 10 as the queries call it, on the calling thread's intersection arena
    /// (`World::tscratch[tls_thread]`): the primitive `Intersect` (0x35151F20, with its `gwd1`
    /// write-back) when `a` is a primitive, else the geometry's own slot 10 (P1c meshes /
    /// heightfields). Returns copies of the new contact records.
    pub(crate) fn query_geom_intersect(
        &mut self,
        a: &dyn crate::geometry::Geometry,
        b: &dyn crate::geometry::Geometry,
        gwd1: &mut crate::geometry::GeomWorldData,
        gwd2: &crate::geometry::GeomWorldData,
        ip: &mut IntersectionParams,
    ) -> Vec<GeomContact> {
        if a.is_a_primitive() {
            let env = crate::collision::prim_intersect::env_with_generic();
            let tid = self.tls_thread.clamp(0, 4) as usize;
            let s = &mut self.tscratch[tid];
            let out = crate::collision::prim_intersect::intersect(&env, s, a, b, gwd1, gwd2, ip);
            match (out.first, out.ret > 0) {
                (Some(f), true) => s.contacts(f, out.ret),
                _ => Vec::new(),
            }
        } else {
            let mut v = Vec::new();
            let n = a.intersect(b, gwd1, gwd2, ip, &mut v);
            v.truncate(n);
            v
        }
    }

    /// The engine's "temp entity pools exist" test (`world+792 != 0`), required by RWI.
    /// The pools are allocated by the pool growth of entity registration
    /// (`World::grow_pools`); the `TimeStep` path is what triggers it for entities created
    /// from the external thread.
    #[inline]
    #[must_use]
    pub fn temp_pools_ready(&self) -> bool {
        self.pool_size != 0
    }

    /// Surface flags (`world+18792`) for an unmasked index (`-1` and other out-of-range
    /// indices read neighbouring tables in the original; the port returns 0 there).
    #[must_use]
    pub(crate) fn surface_flags_raw(&self, idx: i32) -> i32 {
        if idx >= 0 && (idx as usize) < self.surfaces.flags.len() {
            self.surfaces.flags[idx as usize]
        } else {
            0
        }
    }
}

/// The primitive a PWI / CollideEntityWithPrimitive call tests, as the geometry library
/// builds it: box, sphere, cylinder or capsule (types 0, 4, 5, 6).
#[must_use]
pub(crate) fn prim_supported(p: &Primitive) -> bool {
    matches!(
        p,
        Primitive::Box(_) | Primitive::Sphere(_) | Primitive::Cylinder(_) | Primitive::Capsule(_)
    )
}

/// Intersection params as the PWI-style callers pass them: the shared record plus the byte
/// `+98` the binary's control flow reads ("stop at the first hit" for PWI).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct QueryIntersectionParams {
    pub ip: IntersectionParams,
    /// `+98`.
    pub stop_at_first: bool,
}

/// A contact as a query returns it (copies of the per-thread contact records).
pub type QueryContact = GeomContact;
