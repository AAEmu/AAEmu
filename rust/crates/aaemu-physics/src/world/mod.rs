//! **P3 — world core** (`CPhysicalWorld`, 0x5838 bytes; ctor 0x350827B0, reset 0x3507DE80):
//! the entity store and ids, the simulation-class lists, the deferred command queue, the
//! `TimeStep` driver (single-threaded), events and queries. Default tuning:
//! [`crate::physvars::PhysVars`]. `world.md` §1–§6.
//!
//! * [`grid`] — entity grid (`SetupEntityGrid`, thunks, `RepositionEntity`, `GetEntitiesInBox`).
//! * [`geoman`] — the embedded geometry manager (`CreatePrimitive`, `RegisterGeometry`).
//! * [`create`] — `CreatePhysicalEntity`, ids, `DestroyPhysicalEntity`.
//! * [`areas`] — `CPhysArea`, global area, `CheckAreas`.
//! * [`queries`] — `RayWorldIntersection` (incl. the ArcheAge "DrawRayOnGrid" ray-extent limit),
//!   `PrimitiveWorldIntersection`, entity-level queries.
//! * [`events`] — the 15 event types, immediate/logged clients, `PumpLoggedEvents`.
//!
//! **Pointers.** The binary links entities through raw pointers; the port stores every object
//! (entities and areas) in [`World::ents`] and links them by [`EntIdx`]. Creation order gives
//! increasing indices, which is also the order of the binary's pool addresses (used where it
//! sorts by address).
//!
//! **Threads.** The port is single-threaded. The physics-thread index of the calling thread
//! (the binary's TLS slot) is [`World::tls_thread`]: 4 ("external caller") until the first
//! `TimeStep` with `dt > 0`, then 0. Several paths depend on it (entity creation is queued while
//! it is 4, per-caller query caches).

pub mod api;
pub mod areas;
pub mod create;
pub mod events;
pub mod geoman;
pub mod grid;
pub mod grid_query;
pub mod queries;
pub mod queue;
pub mod step;
pub mod terrain;

use crate::entities::base::Entity;
use crate::math::Vec3;
use crate::physvars::PhysVars;
use crate::solver::{BodyArena, ContactPool};

/// Index of an entity (or area) object in the world's entity store. Stands in for the
/// binary's object pointers; the store never reuses an index while anything may still refer
/// to it.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct EntIdx(pub u32);

impl EntIdx {
    /// "null".
    pub const NONE: EntIdx = EntIdx(u32::MAX);

    #[inline]
    #[must_use]
    pub fn is_none(self) -> bool {
        self == Self::NONE
    }
}

/// Number of physics-thread slots of the per-caller arrays (4 workers + the external caller).
pub const MAX_CALLERS: usize = 5;

/// Per-thread island values (`world+21928 + 80·tid`).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct IslandVars {
    /// `+21928` island flag (all entities have flag 0x10000 … see the island step).
    pub flag: i32,
    /// `+21932` island dt.
    pub dt: f32,
    /// `+21936` island max friction (min over entities' slot 44).
    pub max_friction: f32,
}

/// Cache of the last `GetEntitiesInBox` per caller (`+1192 + 24·i` bbox, `+1312 + 4·i`
/// objtypes, `+1332 + 4·i` count, the list itself in the per-caller temp list).
#[derive(Clone, Debug, Default)]
pub struct BoxQueryCache {
    pub bbox: [Vec3; 2],
    pub objtypes: i32,
    pub list: Vec<EntIdx>,
}

/// Surface parameters (`SetSurfaceParameters`, world slot 29): 512 entries.
#[derive(Clone, Debug)]
pub struct SurfaceTables {
    /// `+4456` bounciness.
    pub bounciness: Vec<f32>,
    /// `+6504` static friction.
    pub friction: Vec<f32>,
    /// `+8552` dynamic friction (= ⅔ static when set through slot 29).
    pub dyn_friction: Vec<f32>,
    /// `+18792` flags.
    pub flags: Vec<i32>,
}

impl Default for SurfaceTables {
    fn default() -> Self {
        use crate::physvars::surface_defaults as sd;
        Self {
            bounciness: vec![sd::BOUNCINESS; 512],
            friction: vec![sd::FRICTION; 512],
            dyn_friction: vec![sd::DYNAMIC_FRICTION; 512],
            flags: vec![0; 512],
        }
    }
}

/// `CPhysicalWorld`.
#[derive(Debug)]
pub struct World {
    /// `+104 … +632` physics vars.
    pub vars: PhysVars,
    /// The object store (entities and areas); `None` = freed slot.
    pub ents: Vec<Option<Box<Entity>>>,
    /// Rigid bodies (index 0 = the static dummy of thread 0).
    pub bodies: BodyArena,
    /// Contact records and the world's free list (`+21576`).
    pub contacts: ContactPool,
    /// `+664 + 8·k` simclass list heads, `+728 + 8·k` insertion markers.
    pub list_head: [EntIdx; 8],
    pub list_marker: [EntIdx; 8],
    /// `+760` independent-entity stepping list, `+816` extra (hidden) list.
    pub list_760: EntIdx,
    pub list_816: EntIdx,
    /// The entity grid.
    pub grid: grid::Grid,
    /// `+1068` number of entities in the pools, `+1072` pool size.
    pub n_ents_pool: i32,
    pub pool_size: i32,
    /// `+1076` (cleared by non-static creation).
    pub flag1076: i32,
    /// `+1080` id table (`+1088` capacity = `id_table.len()`), `+1092` id upper bound,
    /// `+1096` id search cursor, `+1100` lowest auto id, `+1104` count of auto ids.
    pub id_table: Vec<EntIdx>,
    pub id_bound: i32,
    pub id_cursor: i32,
    pub id_low: i32,
    pub id_auto: i32,
    /// `+1108` "grid changed during query" flag.
    pub grid_changed: i32,
    /// `+1112` "flagged-only step" mask (0 or 0x800).
    pub flagged_step: u32,
    /// `+1116 + 4·type` entity counts by type.
    pub ent_count_by_type: [i32; 10],
    /// `+1160 + 4·i` GetEntitiesInBox call counters per caller.
    pub box_query_count: [i32; MAX_CALLERS],
    /// Per-caller GetEntitiesInBox cache.
    pub box_cache: [BoxQueryCache; MAX_CALLERS],
    /// `+1184`, `+1188` POD counters.
    pub pod1184: i32,
    pub pod1188: i32,
    /// `+1568 + 8·i` terrain entities per caller.
    pub terrain: [EntIdx; MAX_CALLERS],
    /// `+1652` terrain origin.
    pub terrain_origin: Vec3,
    /// `+1664` physics time, `+1688` integer physics time (in granularity units).
    pub time: f32,
    pub itime: i32,
    /// `+1740` step counter of the rigid loop, `+1744`, `+1748` current group dt.
    pub rigid_loop_counter: i32,
    pub flag1744: i32,
    pub group_dt: f32,
    /// `+4432` current dt.
    pub cur_dt: f32,
    /// `+4436` slow-motion counter.
    pub slowmo_counter: i32,
    /// Surface tables.
    pub surfaces: SurfaceTables,
    /// `+20840` water material, `+20844` objtypes water bit (0x200 when the global area has
    /// a water plane).
    pub water_mat: i32,
    pub water_objtype: u32,
    /// `+20928` global area (head of the all-areas list), `+20936` area count, `+20940` big
    /// area count, `+20944` deleted areas, `+20952 + 8·i` per-medium lists, `+21080`, `+21084`.
    pub global_area: EntIdx,
    pub n_areas: i32,
    pub n_big_areas: i32,
    pub deleted_areas: EntIdx,
    pub medium_lists: [EntIdx; 16],
    pub n_gravity_areas: i32,
    pub n_gravity_areas_nonglobal: i32,
    /// `+21572` step counter, `+22516` event pump counter.
    pub step_counter: i32,
    pub pump_counter: i32,
    /// Per-thread island values (`+21928 + 80·tid`).
    pub island: [IslandVars; MAX_CALLERS],
    /// `+22256` last island max friction.
    pub last_island_max_friction: f32,
    /// `+22416` number of groups, `+22420` group dt clamp, `+22428` group cursor.
    pub n_groups: i32,
    pub group_dt_clamp: f32,
    /// Island step arrays (`+800` leaders, `+824` mass sums / dts, `+840`, `+848`).
    pub group_leader: Vec<EntIdx>,
    pub group_mass: Vec<f32>,
    pub group_840: Vec<i32>,
    pub group_848: Vec<i32>,
    /// `+1752` awake list (post-step job list head).
    pub awake_list: EntIdx,
    /// `+22440` entities whose island finished in the post-solve.
    pub done_list: EntIdx,
    /// The calling thread's physics index (TLS); 4 until the first `TimeStep` with dt > 0.
    pub tls_thread: i32,
    /// The world lock `+22468` held by `TimeStep` after its command queue (deferrable calls
    /// made during a step are queued).
    pub step_lock: bool,
    /// `+4448` POD thread id (-1).
    pub pod_thread: i32,
    /// Deferred command queue (`+20872 …`).
    pub queue: queue::CommandQueue,
    /// Events.
    pub events: events::EventState,
    /// Immediate event-11 (area callback) clients (`+21408`), see [`areas::check`].
    pub area_hooks: areas::check::AreaHooks,
    /// Per-thread contact table of CheckForNewContacts (`qword_35218710[tid]`).
    pub ccd_scratch: Vec<crate::entities::rigid::contacts::ContactScratch>,
    /// Per-thread intersection arenas (`0x35256F80 + 0x39FB8·tid`).
    pub tscratch: Vec<crate::collision::prim_intersect::scratch::ThreadScratch>,
    /// The static dummy contact of RegisterContactPoint's aggregate path (`qword_3521AF50`).
    pub agg_contact_id: crate::solver::ContactId,
    /// Per-thread contact-solver blocks (`W+21928+80·tid` → solver context), created on
    /// first use.
    pub solver: Vec<Option<Box<crate::solver::SolverThread>>>,
    /// Query state: ray-limit settings, query queues, terrain basis ([`queries::QueryState`]).
    pub queries: queries::QueryState,
}

impl World {
    /// `CreatePhysicalWorld(NULL)` → ctor 0x350827B0 + reset 0x3507DE80.
    #[must_use]
    pub fn new() -> Self {
        let mut w = Self {
            vars: PhysVars::default(),
            ents: Vec::new(),
            bodies: BodyArena::default(),
            contacts: ContactPool::default(),
            list_head: [EntIdx::NONE; 8],
            list_marker: [EntIdx::NONE; 8],
            list_760: EntIdx::NONE,
            list_816: EntIdx::NONE,
            grid: grid::Grid::default(),
            n_ents_pool: 0,
            pool_size: 0,
            flag1076: 0,
            id_table: Vec::new(),
            id_bound: 1,
            id_cursor: 0,
            id_low: 0,
            id_auto: 0,
            grid_changed: 0,
            flagged_step: 0,
            ent_count_by_type: [0; 10],
            box_query_count: [0; MAX_CALLERS],
            box_cache: Default::default(),
            pod1184: 0,
            pod1188: 1,
            terrain: [EntIdx::NONE; MAX_CALLERS],
            terrain_origin: Vec3::ZERO,
            time: 0.0,
            itime: 0,
            rigid_loop_counter: 0,
            flag1744: 0,
            group_dt: 0.0,
            cur_dt: 0.0,
            slowmo_counter: 0,
            surfaces: SurfaceTables::default(),
            water_mat: -1,
            water_objtype: 0,
            global_area: EntIdx::NONE,
            n_areas: 0,
            n_big_areas: 0,
            deleted_areas: EntIdx::NONE,
            medium_lists: [EntIdx::NONE; 16],
            n_gravity_areas: 0,
            n_gravity_areas_nonglobal: 0,
            step_counter: 0,
            pump_counter: 0,
            island: [IslandVars::default(); MAX_CALLERS],
            last_island_max_friction: 0.0,
            n_groups: 0,
            group_dt_clamp: 0.0,
            group_leader: Vec::new(),
            group_mass: Vec::new(),
            group_840: Vec::new(),
            group_848: Vec::new(),
            awake_list: EntIdx::NONE,
            done_list: EntIdx::NONE,
            tls_thread: 4,
            step_lock: false,
            pod_thread: -1,
            queue: queue::CommandQueue::default(),
            events: events::EventState::default(),
            area_hooks: areas::check::AreaHooks::default(),
            ccd_scratch: Vec::new(),
            tscratch: (0..MAX_CALLERS as i32)
                .map(crate::collision::prim_intersect::scratch::ThreadScratch::new)
                .collect(),
            agg_contact_id: crate::solver::ContactId::NONE,
            solver: (0..MAX_CALLERS).map(|_| None).collect(),
            queries: queries::QueryState::default(),
        };
        w.ccd_scratch
            .resize_with(MAX_CALLERS, crate::entities::rigid::contacts::ContactScratch::default);
        w
    }

    /// The aggregate dummy contact (created on first use; a self-linked record outside the
    /// pool counters, zero-initialised like the `.data` original).
    pub fn agg_contact(&mut self) -> crate::solver::ContactId {
        if self.agg_contact_id.is_none() {
            self.agg_contact_id = self.contacts.new_sentinel();
        }
        self.agg_contact_id
    }

    /// The solver block of thread `tid`.
    pub fn solver_thread(&mut self, tid: usize) -> &mut crate::solver::SolverThread {
        self.solver[tid].get_or_insert_with(|| Box::new(crate::solver::SolverThread::new(tid)))
    }

    /// The solver block of thread `tid` together with the contact pool.
    pub fn solver_and_pool(&mut self, tid: usize) -> (&mut crate::solver::SolverThread, &mut crate::solver::ContactPool) {
        let s = self.solver[tid].get_or_insert_with(|| Box::new(crate::solver::SolverThread::new(tid)));
        (s, &mut self.contacts)
    }

    /// The entity object behind `e`.
    #[inline]
    #[must_use]
    pub fn ent(&self, e: EntIdx) -> &Entity {
        self.ents[e.0 as usize].as_deref().expect("live entity")
    }

    /// The entity object behind `e` (mutable).
    #[inline]
    pub fn ent_mut(&mut self, e: EntIdx) -> &mut Entity {
        self.ents[e.0 as usize].as_deref_mut().expect("live entity")
    }

    /// Store a new object and return its index.
    pub(crate) fn store(&mut self, ent: Entity) -> EntIdx {
        self.ents.push(Some(Box::new(ent)));
        EntIdx(self.ents.len() as u32 - 1)
    }

    /// World gravity (vars `+284`).
    #[inline]
    #[must_use]
    pub fn gravity(&self) -> Vec3 {
        Vec3::new(self.vars.gravity_x, self.vars.gravity_y, self.vars.gravity_z)
    }

    /// `sub_35077650`: move an entity (and the run of entities sharing its group leader, for
    /// living entities) to the list of its current simclass, honouring the list's insertion
    /// marker.
    pub fn relist_entity(&mut self, e: EntIdx) -> bool {
        let lb = self.ent(e).list_bits;
        let lc = i32::from(lb as u16 as i16);
        if (lc - 256) as u32 >= 8 {
            // ok
        } else {
            // "Error: *major* problem - trying to change iSimClass of a hidden entity!"
            if self.vars.debug_flags & 0x1000 != 0 {
                return false;
            }
        }
        let sc = self.ent(e).sim_class();
        let v6 = lc;
        let (mut v7, mut v8) = (e, e);
        let mut v9 = ((lb << 4) as i32) >> 28;
        let unlink = if sc == 4 && v6 == 4 {
            let leader = self.ent(e).unk232;
            if !leader.is_none() {
                let mut i = self.ent(e).list_prev;
                while !i.is_none() {
                    if self.ent(i).unk232 != leader {
                        break;
                    }
                    v7 = i;
                    v9 |= ((self.ent(i).list_bits << 4) as i32) >> 28;
                    i = self.ent(i).list_prev;
                }
                let mut j = self.ent(e).list_next;
                while !j.is_none() {
                    if self.ent(j).unk232 != leader {
                        break;
                    }
                    v8 = j;
                    v9 |= ((self.ent(j).list_bits << 4) as i32) >> 28;
                    j = self.ent(j).list_next;
                }
            }
            true
        } else {
            (v6 as u32) < 8
        };
        if unlink {
            let n8 = self.ent(v8).list_next;
            let p7 = self.ent(v7).list_prev;
            if !n8.is_none() {
                self.ent_mut(n8).list_prev = p7;
            }
            if !p7.is_none() {
                self.ent_mut(p7).list_next = n8;
            } else {
                self.list_head[v6 as usize] = n8;
            }
            // Move the marker off the removed run.
            let v16 = n8;
            let mut v17 = v7;
            if v7 != v16 {
                let marker = self.list_marker[v6 as usize];
                loop {
                    if v17 == marker {
                        self.list_marker[v6 as usize] = v16;
                        break;
                    }
                    v17 = self.ent(v17).list_next;
                    if v17 == v16 {
                        break;
                    }
                }
            }
        }
        let k = sc as usize;
        if v9 != 0 {
            let marker = self.list_marker[k];
            self.ent_mut(v8).list_next = marker;
            if !marker.is_none() {
                let mp = self.ent(marker).list_prev;
                self.ent_mut(v7).list_prev = mp;
                if !mp.is_none() {
                    self.ent_mut(mp).list_next = v7;
                }
                self.ent_mut(marker).list_prev = v8;
            } else {
                let mut t = self.list_head[k];
                if !t.is_none() {
                    while !self.ent(t).list_next.is_none() {
                        t = self.ent(t).list_next;
                    }
                    self.ent_mut(v7).list_prev = t;
                    self.ent_mut(t).list_next = v7;
                } else {
                    self.ent_mut(v7).list_prev = EntIdx::NONE;
                }
            }
            if self.list_marker[k] == self.list_head[k] {
                self.list_head[k] = v7;
            }
            self.list_marker[k] = v7;
        } else {
            let head = self.list_head[k];
            self.ent_mut(v8).list_next = head;
            self.ent_mut(v7).list_prev = EntIdx::NONE;
            if !head.is_none() {
                self.ent_mut(head).list_prev = v8;
            }
            self.list_head[k] = v7;
        }
        let mut t = v7;
        loop {
            let ent = self.ent_mut(t);
            ent.list_bits = (ent.list_bits & 0x0FFF_FFFF) | ((v9 as u32) << 28);
            ent.list_bits = (ent.list_bits & 0xFFFF_0000) | (sc as u16 as u32);
            if t == v8 {
                break;
            }
            t = ent.list_next;
        }
        // Update the simclass bits of the grid thunks.
        let mut th = self.ent(e).grid_thunk;
        let scb = ((sc as i8 as i32) << 20) as u32;
        while th != 0 {
            let t = &mut self.grid.thunks[th as usize];
            t.w8 ^= (t.w8 ^ scb) & 0x70_0000;
            th = t.w8 & 0xFFFFF;
        }
        true
    }

    /// Entities of the simclass list `k` in list order.
    #[must_use]
    pub fn list(&self, k: usize) -> Vec<EntIdx> {
        let mut v = Vec::new();
        let mut e = self.list_head[k];
        while !e.is_none() {
            v.push(e);
            e = self.ent(e).list_next;
        }
        v
    }
}

impl Default for World {
    fn default() -> Self {
        Self::new()
    }
}
