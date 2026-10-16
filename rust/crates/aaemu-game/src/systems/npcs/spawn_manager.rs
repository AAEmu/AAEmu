//! `SpawnManager` (`Core/Managers/World/SpawnManager.cs`), one per world insance, kept in
//! [`SpawnManagers`] (`server.systems.npcs.spawns`), plus the spawner part of
//! `WorldManager.ActiveRegionTick` and the C# object-graph links the spawners need:
//!
//! * `npc.Spawner` / `doodad.Spawner` ([`SpawnManagers::npc_spawner_of`] /
//!   [`SpawnManagers::doodad_spawner_of`], by object id);
//! * `GameObject.Respawn` / `GameObject.Despawn` (the last time set through
//!   [`SpawnManager::add_respawn`] / [`SpawnManager::add_despawn`]; `None` = `MinValue`);
//! * the static `NpcSpawner._spawnLock` ([`SpawnManagers::spawn_lock`], reentrant like C# `lock`).
//!
//! The active-region tick is the bounded activation pass (`active_region_tick_stats`): ordinary
//! spawners are indexed per instance and 64 m region and queried only around present players, on
//! player activity / membership / config changes; each pass creates at most
//! `World.NpcSpawnsPerTick` NPCs (`visible neighbourhoods before prewarm rings, nearest first`,
//! rotating fairly across instances). A prepared placement is tracked in the warmed registry:
//! when it drops out of every player's warm set it keeps its NPCs for
//! `World.NpcWarmRetentionSeconds` and is then reclaimed gradually within the same per-tick
//! budget; ordinary quest givers / reporters and service roles stay resident per live member
//! after their first preparation (`World.NpcResidentServiceNpcs`), so a mixed placement still
//! expires its nonresident members. Event / tower / dynamic spawners keep their own
//! triggers and are not indexed.
//!
//! Object ids are reused after `ObjectIdManager.ReleaseId`, so the links / fields of an id are
//! dropped when this module releases it ([`forget_object`]) and when a spawner creates a new
//! object with it. Other modules that release spawned objects' ids call [`forget_object`].
//! (Loot containers are dropped by the object id manager's release observer, see
//! `LootManager::install_release_hook`, so no release path can miss them.)
//!
//! Times use `tokio::time::Instant` (`DateTime.UtcNow` in C#) so tests can drive them.

use std::collections::{BTreeMap, HashMap, HashSet};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicU32, AtomicU64, Ordering};
use std::sync::{Arc, Condvar, Mutex, MutexGuard, OnceLock};
use std::thread::ThreadId;
use std::time::Duration;

use dashmap::DashMap;
use tokio::time::Instant;

use crate::config::WorldConfig;
use crate::data::world_templates::REGION_SIZE;
use crate::models::doodad::doodad_owner_type;
use crate::server::GameServer;
use crate::systems::npcs::doodad::spawner::{
    parse_list, DoodadSpawner, DoodadSpawnerDef, NpcSpawnerDef, SimpleSpawnerDef,
};
use crate::systems::npcs::gimmick::spawner::{parse_gimmick_spawners, GimmickSpawner};
use crate::systems::npcs::npc::residency::ResidentNpcTemplates;
use crate::systems::npcs::npc::spawner::{
    npc_is_resident, NpcSpawner, NpcSpawnerInit, NpcSpawnerShared,
};
use crate::systems::npcs::npc::spawner_data::{NpcSpawnerNpc, NpcSpawnerTemplate};
use crate::world::{DoodadRef, NpcRef, ObjectKind, WorldInstance, WorldObject};

/// `s_fakeSpawnerId` start value.
pub const FIRST_FAKE_SPAWNER_ID: u32 = 9_000_001;

/// Upper bound of `World.NpcPrewarmRegionMargin`: each extra ring adds regions (and candidate
/// spawners) quadratically, so a pathological configuration is clamped to a modest ring.
pub const MAX_PREWARM_REGION_MARGIN: u32 = 4;

/// The configured prewarm margin, clamped to [`MAX_PREWARM_REGION_MARGIN`].
pub fn prewarm_region_margin(config: &WorldConfig) -> u32 {
    config
        .npc_prewarm_region_margin
        .min(MAX_PREWARM_REGION_MARGIN)
}

/// The activation tick interval from `World.NpcSpawnTickMs`: `0` falls back to the 250 ms
/// default (never a busy spin) and extreme values are clamped to a safe range.
pub fn spawn_tick_interval(config: &WorldConfig) -> Duration {
    let ms = config.npc_spawn_tick_ms;
    let ms = if ms == 0 { 250 } else { ms.clamp(50, 60_000) };
    Duration::from_millis(ms)
}

// ---------------------------------------------------------------------------------------------
// time helpers
// ---------------------------------------------------------------------------------------------

/// `DateTime.UtcNow` for spawn timing (tests shift it with [`advance_test_clock`]).
pub fn now() -> Instant {
    #[cfg(test)]
    {
        Instant::now() + TEST_CLOCK_OFFSET.with(std::cell::Cell::get)
    }
    #[cfg(not(test))]
    {
        Instant::now()
    }
}

#[cfg(test)]
thread_local! {
    static TEST_CLOCK_OFFSET: std::cell::Cell<Duration> = const { std::cell::Cell::new(Duration::ZERO) };
}

/// Moves this thread's spawn clock forward (tests; `#[tokio::test]` runs on one thread).
#[cfg(test)]
pub fn advance_test_clock(d: Duration) {
    TEST_CLOCK_OFFSET.with(|c| c.set(c.get() + d));
}

/// `t.AddSeconds(secs)` (negative values move back; clamped at the clock origin).
pub fn add_secs(t: Instant, secs: f64) -> Instant {
    if !secs.is_finite() {
        return t;
    }
    if secs >= 0.0 {
        t + Duration::from_secs_f64(secs)
    } else {
        t.checked_sub(Duration::from_secs_f64(-secs)).unwrap_or(t)
    }
}

/// Unix seconds (the `GameScheduleManager` queries).
pub fn unix_now() -> i64 {
    chrono::Utc::now().timestamp()
}

/// `MathF.PI / 180f * val` (`NumericExtensions.DegToRad(float)`).
pub fn deg_to_rad(v: f32) -> f32 {
    std::f32::consts::PI / 180.0 * v
}

// ---------------------------------------------------------------------------------------------
// reentrant lock (C# `lock`)
// ---------------------------------------------------------------------------------------------

/// A reentrant mutex (C# `Monitor`) for the static `NpcSpawner._spawnLock`. Never held across an
/// `.await`.
#[derive(Debug, Default)]
pub struct ReentrantLock {
    owner: Mutex<(Option<ThreadId>, usize)>,
    cv: Condvar,
}

/// Guard of [`ReentrantLock`].
#[derive(Debug)]
pub struct ReentrantGuard<'a>(&'a ReentrantLock);

impl ReentrantLock {
    pub fn lock(&self) -> ReentrantGuard<'_> {
        let me = std::thread::current().id();
        let mut st = self.owner.lock().expect("spawn lock poisoned");
        loop {
            match st.0 {
                None => {
                    *st = (Some(me), 1);
                    break;
                }
                Some(o) if o == me => {
                    st.1 += 1;
                    break;
                }
                Some(_) => st = self.cv.wait(st).expect("spawn lock poisoned"),
            }
        }
        ReentrantGuard(self)
    }
}

impl Drop for ReentrantGuard<'_> {
    fn drop(&mut self) {
        let mut st = self.0.owner.lock().expect("spawn lock poisoned");
        st.1 -= 1;
        if st.1 == 0 {
            st.0 = None;
            self.0.cv.notify_one();
        }
    }
}

// ---------------------------------------------------------------------------------------------
// backend: the other helpers' managers (+ a test seam while they are stubs)
// ---------------------------------------------------------------------------------------------

/// Calls into `NpcManager` / `DoodadManager` (tests can install a [`backend::TestBackend`] per
/// server while those managers are stubs).
pub mod backend {
    use std::sync::Arc;

    use crate::server::GameServer;
    use crate::world::{DoodadRef, NpcRef, WorldInstance, WorldObject};

    /// Test replacement of the NPC / doodad managers' `Create` (spawn / delete then use the
    /// plain world API).
    #[cfg(test)]
    pub trait TestBackend: Send + Sync {
        fn npc_create(&self, server: &GameServer, obj_id: u32, template_id: u32) -> Option<NpcRef>;
        fn doodad_create(
            &self,
            server: &GameServer,
            world: &Arc<WorldInstance>,
            obj_id: u32,
            template_id: u32,
        ) -> Option<DoodadRef>;
    }

    #[cfg(test)]
    fn test_backend(server: &GameServer) -> Option<Arc<dyn TestBackend>> {
        server
            .systems
            .npcs
            .spawns
            .test_backend
            .lock()
            .expect("poisoned")
            .clone()
    }

    /// `NpcManager.Instance.Create(world, objId, templateId)`.
    pub fn npc_create(server: &GameServer, obj_id: u32, template_id: u32) -> Option<NpcRef> {
        #[cfg(test)]
        if let Some(b) = test_backend(server) {
            return b.npc_create(server, obj_id, template_id);
        }
        crate::systems::npcs::npc::manager::create(server, obj_id, template_id)
    }

    /// `DoodadManager.Instance.Create(world, objId, templateId, owner, skipPhaseInit)`.
    pub fn doodad_create(
        server: &GameServer,
        world: &Arc<WorldInstance>,
        obj_id: u32,
        template_id: u32,
        owner: Option<&WorldObject>,
        skip_phase_init: bool,
    ) -> Option<DoodadRef> {
        #[cfg(test)]
        if let Some(b) = test_backend(server) {
            return b.doodad_create(server, world, obj_id, template_id);
        }
        crate::systems::npcs::doodad::manager::create(
            server,
            world,
            obj_id,
            template_id,
            owner,
            skip_phase_init,
        )
    }

    /// `Doodad.Spawn()`.
    pub fn doodad_spawn(server: &GameServer, world: &Arc<WorldInstance>, doodad: &DoodadRef) {
        #[cfg(test)]
        if test_backend(server).is_some() {
            world.spawn(doodad, &server.view_ctx());
            return;
        }
        crate::systems::npcs::doodad::manager::spawn(server, world, doodad)
    }

    /// `Doodad.Delete()`.
    pub fn doodad_delete(server: &GameServer, world: &Arc<WorldInstance>, doodad: &DoodadRef) {
        #[cfg(test)]
        if test_backend(server).is_some() {
            world.delete(doodad);
            return;
        }
        crate::systems::npcs::doodad::manager::delete(server, world, doodad)
    }
}

// ---------------------------------------------------------------------------------------------
// SpawnManagers
// ---------------------------------------------------------------------------------------------

/// `WorldInstance.SpawnManager` of every instance + the cross-instance spawner state.
pub struct SpawnManagers {
    managers: DashMap<u32, Arc<SpawnManager>>,
    /// `NpcSpawner._spawnLock` (static).
    spawn_lock: ReentrantLock,
    /// `npc.Spawner`.
    npc_spawner_of: DashMap<u32, Arc<NpcSpawner>>,
    /// `doodad.Spawner`.
    doodad_spawner_of: DashMap<u32, Arc<DoodadSpawner>>,
    /// `GameObject.Respawn` (absent = `DateTime.MinValue`).
    respawn_fields: DashMap<u32, Instant>,
    /// `GameObject.Despawn` (absent = `DateTime.MinValue`).
    despawn_fields: DashMap<u32, Instant>,
    /// `SpawnManager.s_fakeSpawnerId` (static, shared by all instances).
    fake_spawner_id: AtomicU32,
    /// Immutable quest-giver / reporter + service-role template snapshot, built once from the
    /// static game data (`ResidentNpcTemplates`; never rescanned per tick).
    resident_npcs: OnceLock<ResidentNpcTemplates>,
    active_tick_started: AtomicBool,
    /// Fairness cursor of the activation pass: the instance it starts with rotates every pass so
    /// a small `World.NpcSpawnsPerTick` budget cannot starve later instances.
    activation_rotation: AtomicU32,
    #[cfg(test)]
    pub(crate) test_backend: Mutex<Option<Arc<dyn backend::TestBackend>>>,
}

impl std::fmt::Debug for SpawnManagers {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("SpawnManagers")
            .field("instances", &self.managers.len())
            .field("npc_links", &self.npc_spawner_of.len())
            .field("doodad_links", &self.doodad_spawner_of.len())
            .finish()
    }
}

impl Default for SpawnManagers {
    fn default() -> Self {
        Self {
            managers: DashMap::new(),
            spawn_lock: ReentrantLock::default(),
            npc_spawner_of: DashMap::new(),
            doodad_spawner_of: DashMap::new(),
            respawn_fields: DashMap::new(),
            despawn_fields: DashMap::new(),
            fake_spawner_id: AtomicU32::new(FIRST_FAKE_SPAWNER_ID),
            resident_npcs: OnceLock::new(),
            active_tick_started: AtomicBool::new(false),
            activation_rotation: AtomicU32::new(0),
            #[cfg(test)]
            test_backend: Mutex::new(None),
        }
    }
}

impl SpawnManagers {
    /// `world.SpawnManager`.
    pub fn get(&self, world_id: u32) -> Option<Arc<SpawnManager>> {
        self.managers.get(&world_id).map(|m| m.clone())
    }

    /// `world.SpawnManager = new SpawnManager(world)` (existing manager kept).
    pub fn get_or_create(&self, world: &Arc<WorldInstance>) -> Arc<SpawnManager> {
        self.managers
            .entry(world.id)
            .or_insert_with(|| Arc::new(SpawnManager::new(world.clone())))
            .clone()
    }

    /// Whether the 1 s active-region ticker runs (`WorldManager.Initialize` subscription; tests).
    #[cfg(test)]
    pub(crate) fn active_tick_started(&self) -> bool {
        self.active_tick_started.load(Ordering::SeqCst)
    }

    /// Drops an instance's manager (`WorldInstance` removed; systems::instances).
    pub fn remove(&self, world_id: u32) -> Option<Arc<SpawnManager>> {
        self.managers.remove(&world_id).map(|(_, m)| m)
    }

    /// Every instance's manager.
    pub fn all(&self) -> Vec<Arc<SpawnManager>> {
        let mut v: Vec<_> = self.managers.iter().map(|m| m.clone()).collect();
        v.sort_by_key(|m| m.world.id);
        v
    }

    /// `lock (_spawnLock)`.
    pub fn spawn_lock(&self) -> ReentrantGuard<'_> {
        self.spawn_lock.lock()
    }

    /// `npc.Spawner`.
    pub fn npc_spawner_of(&self, obj_id: u32) -> Option<Arc<NpcSpawner>> {
        self.npc_spawner_of.get(&obj_id).map(|s| s.clone())
    }

    /// `npc.Spawner = spawner`.
    pub fn set_npc_spawner(&self, obj_id: u32, spawner: Arc<NpcSpawner>) {
        self.npc_spawner_of.insert(obj_id, spawner);
    }

    /// `doodad.Spawner`.
    pub fn doodad_spawner_of(&self, obj_id: u32) -> Option<Arc<DoodadSpawner>> {
        self.doodad_spawner_of.get(&obj_id).map(|s| s.clone())
    }

    /// `doodad.Spawner = spawner`.
    pub fn set_doodad_spawner(&self, obj_id: u32, spawner: Arc<DoodadSpawner>) {
        self.doodad_spawner_of.insert(obj_id, spawner);
    }

    /// `obj.Respawn` (`None` = `MinValue`).
    pub fn respawn_field(&self, obj_id: u32) -> Option<Instant> {
        self.respawn_fields.get(&obj_id).map(|t| *t)
    }

    /// `obj.Despawn` (`None` = `MinValue`).
    pub fn despawn_field(&self, obj_id: u32) -> Option<Instant> {
        self.despawn_fields.get(&obj_id).map(|t| *t)
    }

    /// `obj.Despawn = at` (the field only; an already scheduled despawn reads it).
    pub fn set_despawn_field(&self, obj_id: u32, at: Instant) {
        self.despawn_fields.insert(obj_id, at);
    }

    /// Drops the spawner links and `Respawn` / `Despawn` fields of a released object id.
    pub fn forget_object(&self, obj_id: u32) {
        self.npc_spawner_of.remove(&obj_id);
        self.doodad_spawner_of.remove(&obj_id);
        self.respawn_fields.remove(&obj_id);
        self.despawn_fields.remove(&obj_id);
    }

    /// The next `s_fakeSpawnerId` (not advanced).
    pub fn peek_fake_spawner_id(&self) -> u32 {
        self.fake_spawner_id.load(Ordering::SeqCst)
    }

    /// The immutable resident-template snapshot (ordinary quest givers / reporters and service
    /// roles), derived once from the static game data; the activation client code calls this
    /// instead of rescanning the quest acts.
    pub fn resident_npcs(&self, server: &GameServer) -> &ResidentNpcTemplates {
        self.resident_npcs
            .get_or_init(|| ResidentNpcTemplates::from_game_data(&server.game_data))
    }
}

/// [`SpawnManagers::forget_object`] on the server's managers.
pub fn forget_object(server: &GameServer, obj_id: u32) {
    server.systems.npcs.spawns.forget_object(obj_id);
}

/// Resets the `Respawn` / `Despawn` fields (and any stale looting container) of a freshly
/// created object (its id may be reused).
pub fn forget_object_fields(server: &GameServer, obj_id: u32) {
    let s = &server.systems.npcs.spawns;
    s.respawn_fields.remove(&obj_id);
    s.despawn_fields.remove(&obj_id);
    if let Some(h) = server.systems.npcs.hooks.economy.get() {
        h.forget_loot(server, obj_id);
    }
}

/// `obj.ParentWorld.SpawnManager` (by the object's world, else `fallback_world`).
pub fn manager_of(
    server: &GameServer,
    obj: &WorldObject,
    fallback_world: Option<u32>,
) -> Option<Arc<SpawnManager>> {
    let id = server
        .world
        .world_of(obj)
        .map(|w| w.id)
        .or(fallback_world)?;
    server.systems.npcs.spawns.get(id)
}

/// The instance a spawner belongs to (`ParentWorld`). Unlike [`crate::world::WorldManager::world`]
/// this stays quiet for a since-removed dungeon; `None` for a detached spawner.
pub fn owning_world(server: &GameServer, spawner: &NpcSpawner) -> Option<Arc<WorldInstance>> {
    let id = spawner.parent_world()?;
    if let Some(m) = server.systems.npcs.spawns.get(id) {
        return Some(m.world.clone());
    }
    server.world.worlds().into_iter().find(|w| w.id == id)
}

/// `GameObject.Delete()` in the object's world (or `fallback_world`). Doodads go through the
/// doodad manager.
pub fn delete_object(server: &GameServer, obj: &WorldObject, fallback_world: Option<u32>) {
    let world = server
        .world
        .world_of(obj)
        .or_else(|| fallback_world.and_then(|id| server.world.world(id)));
    let Some(world) = world else {
        return;
    };
    match obj {
        WorldObject::Doodad(d) => backend::doodad_delete(server, &world, d),
        // Npc.Delete detaches the AI; Slave / Mate have no Delete override.
        WorldObject::Npc(n) => crate::systems::npcs::npc::manager::delete(server, &world, n),
        o => world.delete(o.clone()),
    }
}

// ---------------------------------------------------------------------------------------------
// SpawnManager
// ---------------------------------------------------------------------------------------------

/// The spawner of a scheduled object, read when it was scheduled (`obj.Spawner`).
#[derive(Debug, Clone)]
pub enum SpawnerLink {
    Npc(Arc<NpcSpawner>),
    Doodad(Arc<DoodadSpawner>),
    None,
}

#[derive(Debug, Clone)]
struct Scheduled {
    obj: WorldObject,
    link: SpawnerLink,
    /// `obj.Respawn` / `obj.Despawn` (`None` = `MinValue`).
    at: Option<Instant>,
    /// `HashSet` enumeration order (insertion).
    seq: u64,
}

#[derive(Debug, Default)]
struct Inner {
    npc_spawners: BTreeMap<u32, Vec<Arc<NpcSpawner>>>,
    npc_event_spawners: BTreeMap<u32, Vec<Arc<NpcSpawner>>>,
    tower_def_spawners: BTreeMap<u32, Vec<Arc<NpcSpawner>>>,
    doodad_spawners: BTreeMap<u32, Arc<DoodadSpawner>>,
    transfer_spawners: BTreeMap<u32, SimpleSpawnerDef>,
    gimmick_spawners: BTreeMap<u32, GimmickSpawner>,
    slave_spawners: BTreeMap<u32, SimpleSpawnerDef>,
    player_doodads: Vec<DoodadRef>,
    /// `_nextId`.
    next_id: u32,
}

/// What `Load` read (logs and tests).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct LoadStats {
    pub npc_files: usize,
    pub npc_entries: usize,
    pub npc_duplicates: usize,
    pub npc_missing_templates: usize,
    /// Entries that produced spawner clones in `NpcSpawners`.
    pub npc_entries_with_spawners: usize,
    /// `NpcSpawner` clones in `NpcSpawners`.
    pub npc_spawners: usize,
    /// Generated ("fake") spawner templates (the generated spawner is stored too, unlike the C#).
    pub npc_fake_spawners: usize,
    pub npc_event_spawners: usize,
    pub tower_def_spawners: usize,
    pub doodad_files: usize,
    pub doodad_entries: usize,
    pub doodad_duplicates: usize,
    pub doodad_missing_templates: usize,
    pub doodad_spawners: usize,
    pub transfer_entries: usize,
    pub transfer_spawners: usize,
    pub gimmick_entries: usize,
    pub gimmick_spawners: usize,
    pub slave_entries: usize,
    pub slave_spawners: usize,
}

/// `Exist(templateId)` of the template managers `Load` filters with.
pub struct TemplateExists<'a> {
    pub npc: &'a dyn Fn(u32) -> bool,
    pub doodad: &'a dyn Fn(u32) -> bool,
    pub transfer: &'a dyn Fn(u32) -> bool,
    pub gimmick: &'a dyn Fn(u32) -> bool,
    pub slave: &'a dyn Fn(u32) -> bool,
}

/// Structured stats of one activation pass (tests / diagnostics; no per-NPC log lines).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ActivationStats {
    /// Candidate spawners examined this pass.
    pub candidates: usize,
    /// NPCs actually created this pass across all instances (never above
    /// `World.NpcSpawnsPerTick`).
    pub created: usize,
    /// Candidate spawners not examined this pass; the next rotated pass continues with them.
    pub queued: usize,
    /// Living NPCs of expired warm placements actually scheduled for reclamation this pass
    /// (never above `World.NpcSpawnsPerTick`; a large placement drains over several passes).
    pub reclaimed: usize,
}

/// A player's captured activation input: the owning region and the world position (Z included
/// for the far-authored 3D sphere).
type PlayerSnapshot = ((i32, i32), (f32, f32, f32));

/// A far-authored activation candidate: a placement whose authored `TestRadiusPc` /
/// `TestRadiusNpc` reaches beyond the configured prewarm ring. `radius` is the authored 3D
/// trigger in world units (`r * 50`, the rule of `NpcSpawner::is_player_in_spawn_radius`).
#[derive(Debug, Clone)]
struct AuthoredCandidate {
    spawner: Arc<NpcSpawner>,
    radius: f32,
}

/// One tracked warm placement of the activation registry.
#[derive(Debug, Clone)]
struct WarmEntry {
    spawner: Arc<NpcSpawner>,
    /// `true` only for a homogeneously resident placement: every authored member is an
    /// ordinary quest giver / reporter or service role **and** no live member is runtime-owned.
    /// Such a placement is kept after the first preparation instead of being distance-reclaimed;
    /// a mixed placement still expires its nonresident members per live NPC. Gated by
    /// `World.NpcResidentServiceNpcs` at booking and at reclamation time.
    resident: bool,
    /// Stable retention deadline, booked once when the placement dropped out of the warm set
    /// (`None` while it is warm or resident). Independent of later queue rebuilds / unchanged
    /// player revisions.
    expires_at: Option<Instant>,
}

/// One instance's activation index: every ordinary spawner filed under its authored 64 m region
/// (`WorldManager.REGION_SIZE`). Rebuilt only when the ordinary spawner membership changes
/// (load / add / delete-all), never on the fast tick itself.
#[derive(Debug, Default)]
struct ActivationState {
    /// `NpcSpawners` membership generation the region index was built from.
    index_built: bool,
    index_generation: u64,
    index: HashMap<(i32, i32), Vec<Arc<NpcSpawner>>>,
    /// Far-authored candidates (authored radius beyond the client-visible neighbourhood),
    /// grouped by their required radius in regions (`ceil(radius / REGION_SIZE)`); each group
    /// buckets centers into `g * REGION_SIZE` cells, so a player probes only the nine cells
    /// around it before the exact 3D sphere test. Groups the configured ring already covers are
    /// skipped at lookup, never at build (the margin is runtime-configurable).
    authored_index: HashMap<u32, HashMap<(i32, i32), Vec<AuthoredCandidate>>>,
    /// Whether the candidate queues below were built at least once.
    built: bool,
    /// Activity revision / generation / margin the queues were built for.
    revision: u64,
    queue_generation: u64,
    margin: u32,
    /// Spawners whose client-visible neighbourhood contains a player, nearest first.
    visible: Vec<Arc<NpcSpawner>>,
    /// Spawners in the prewarm ring (but not a visible neighbourhood), nearest first.
    prewarm: Vec<Arc<NpcSpawner>>,
    /// Ids of every queued spawner (the currently warm set).
    warm_ids: HashSet<u32>,
    /// Rotation / continuation cursors into the two queues.
    visible_cursor: usize,
    prewarm_cursor: usize,
    /// Placements prepared at least once while warm, with their residency and stable retention
    /// booking. Only this registry is swept for maintenance; the full spawner table is never
    /// scanned per tick.
    warmed: HashMap<u32, WarmEntry>,
}

/// `SpawnManager`.
#[derive(Debug)]
pub struct SpawnManager {
    /// `World` (the owning instance).
    pub world: Arc<WorldInstance>,
    inner: Mutex<Inner>,
    respawns: Mutex<HashMap<u32, Scheduled>>,
    despawns: Mutex<HashMap<u32, Scheduled>>,
    seq: AtomicU64,
    /// `_work`.
    work: AtomicBool,
    /// `_loaded`.
    loaded: AtomicBool,
    /// `_lockSpawner`.
    lock_spawner: Mutex<()>,
    /// `SpawnTasks`.
    spawn_tasks: Mutex<Vec<tokio::task::JoinHandle<usize>>>,
    /// The activation region index, candidate queues and warmed registry (see [`ActivationState`]).
    activation: Mutex<ActivationState>,
    /// Bumped whenever the ordinary `NpcSpawners` membership changes; invalidates the index and
    /// the candidate queues without rebuilding them per tick.
    activation_generation: AtomicU64,
}

/// `Directory.GetFiles(dir, "<prefix>*.json")` (file-name order, case-insensitive match);
/// `None` when the directory cannot be read.
fn spawn_files(dir: &Path, prefix: &str) -> Option<Vec<PathBuf>> {
    let mut files: Vec<(String, PathBuf)> = std::fs::read_dir(dir)
        .ok()?
        .filter_map(Result::ok)
        .filter(|e| e.file_type().is_ok_and(|t| t.is_file()))
        .filter_map(|e| {
            let name = e.file_name().to_string_lossy().to_ascii_lowercase();
            (name.starts_with(prefix) && name.ends_with(".json")).then(|| (name, e.path()))
        })
        .collect();
    files.sort();
    Some(files.into_iter().map(|(_, p)| p).collect())
}

/// Reads a spawn file (`FileManager.GetFileContents`); `None` = missing / empty.
fn read_spawn_file(world: &WorldInstance, path: &Path) -> Option<String> {
    match std::fs::read_to_string(path) {
        Ok(s) if !s.trim().is_empty() && s.trim() != "\u{feff}" => Some(s),
        Ok(_) => {
            tracing::warn!("File {} is empty.", path.display());
            None
        }
        Err(_) => {
            tracing::info!(
                "World {} is missing {}",
                world.template.name,
                path.file_name().unwrap_or_default().to_string_lossy()
            );
            None
        }
    }
}

impl SpawnManager {
    pub fn new(world: Arc<WorldInstance>) -> Self {
        Self {
            world,
            inner: Mutex::new(Inner {
                next_id: 1,
                ..Inner::default()
            }),
            respawns: Mutex::new(HashMap::new()),
            despawns: Mutex::new(HashMap::new()),
            seq: AtomicU64::new(0),
            work: AtomicBool::new(true),
            loaded: AtomicBool::new(false),
            lock_spawner: Mutex::new(()),
            spawn_tasks: Mutex::new(Vec::new()),
            activation: Mutex::new(ActivationState::default()),
            activation_generation: AtomicU64::new(0),
        }
    }

    fn inner(&self) -> MutexGuard<'_, Inner> {
        self.inner.lock().expect("spawn manager poisoned")
    }

    // ----- AddNpcSpawner -----

    /// `AddNpcSpawner(npcSpawner)` (the JSON entry). Returns the number of spawners added to
    /// `NpcSpawners` (0 for event spawners). `Err` where the C# throws (missing spawner template
    /// 1, duplicate fake ids).
    pub fn add_npc_spawner(
        &self,
        server: &GameServer,
        def: NpcSpawnerDef,
    ) -> Result<usize, String> {
        let data = &server.game_data.systems.npcs.npc_spawners;
        let spawns = &server.systems.npcs.spawns;
        let mut inner = self.inner();
        let mut ids = def.npc_spawner_ids.clone();
        if ids == [0] {
            ids.clear();
        }
        let b = &def.base;
        let new_shared = || {
            let shared = NpcSpawnerShared::new(b.position);
            shared.set_management_group(b.spawn_group_id);
            shared
        };
        let init =
            |id: u32, spawner_id: u32, ids: Vec<u32>, template: Option<Arc<NpcSpawnerTemplate>>| {
                NpcSpawnerInit {
                    id,
                    spawner_id,
                    unit_id: b.unit_id,
                    follow_path: b.follow_path.clone(),
                    follow_npc: b.follow_npc,
                    respawn_time: b.respawn_time,
                    despawn_time: b.despawn_time,
                    special_link: b.special_link.clone(),
                    count: def.count,
                    npc_spawner_ids: ids,
                    parent_world: Some(self.world.id),
                    spawnable_npcs: Vec::new(),
                    template,
                }
            };
        let tower_ids = server
            .game_data
            .systems
            .instances
            .tower_defs
            .npc_spawner_ids();
        let controller_ids: Vec<_> = data
            .get_spawner_ids(b.unit_id)
            .unwrap_or_default()
            .into_iter()
            .filter(|id| tower_ids.contains(id))
            .filter(|id| {
                data.get_npc_spawner_template(*id).is_some_and(|t| {
                    t.npcs
                        .iter()
                        .any(|n| n.member_id == b.unit_id && n.member_type == "Npc")
                })
            })
            .filter(|id| ids.is_empty() || ids.contains(id))
            .collect();
        if !controller_ids.is_empty() {
            // The shipped JSON lists event controllers as ordinary NPCs. Retain their
            // authored positions/paths, but only the tower defense runtime may spawn them.
            for id in controller_ids {
                let mut template = NpcSpawnerTemplate::with_member(id, b.unit_id);
                template.spawn_delay_min = 0.0;
                template.spawn_delay_max = 0.0;
                let spawner = NpcSpawner::new(
                    init(id, id, vec![id], Some(Arc::new(template))),
                    new_shared(),
                );
                inner
                    .tower_def_spawners
                    .entry(id)
                    .or_default()
                    .push(Arc::new(spawner));
            }
            return Ok(0);
        }
        if ids.is_empty() {
            let game_data_ids = data.get_spawner_ids(b.unit_id).unwrap_or_default();
            let mut spawners = Vec::new();
            let mut added = 0;
            if game_data_ids.is_empty() {
                // No pre-defined spawner: generate one from template 1 ("Test Warrior").
                tracing::trace!("SpawnerIds for Npc={} doesn't exist", b.unit_id);
                let id = spawns.fake_spawner_id.load(Ordering::SeqCst);
                let Some(tmp) = data.get_npc_spawner_template(1) else {
                    return Err(format!(
                        "AddNpcSpawner: npc spawner template 1 is missing (Npc={})",
                        b.unit_id
                    ));
                };
                let mut template = (*tmp).clone();
                template.id = id;
                let tmp_npc = NpcSpawnerNpc {
                    id,
                    npc_spawner_template_id: id,
                    member_id: b.unit_id,
                    member_type: "Npc".into(),
                    weight: 1.0,
                    unit_id: 0,
                };
                template.npcs = vec![tmp_npc.clone()];
                if !data.add_npc_spawner_npc(tmp_npc.clone()) {
                    return Err(format!("AddNpcSpawnerNpc: duplicate id {id}"));
                }
                data.add_member_and_spawner_template_ids(&tmp_npc);
                let template = Arc::new(template);
                if !data.add_npc_spawner((*template).clone()) {
                    return Err(format!("AddNpcSpawner: duplicate id {id}"));
                }
                spawns.fake_spawner_id.fetch_add(1, Ordering::SeqCst);
                // Deviation from the C#: the generated spawner is kept for this entry too. The C#
                // dropped it (`spawners` stayed empty), losing the first placement of every NPC
                // without game-data spawners; later entries cloned the generated template.
                let spawner = NpcSpawner::new(
                    init(inner.next_id, id, vec![id], Some(template.clone())),
                    new_shared(),
                );
                spawner.initialize_spawnable_npcs(Some(template.as_ref()));
                spawners.push(Arc::new(spawner));
                inner.next_id += 1;
                added += 1;
            } else {
                // Memberwise clones sharing Position / SpawnedNpcs / caches / NpcSpawnerIds.
                let shared = new_shared();
                for &id in &game_data_ids {
                    let template = data.get_npc_spawner_template(id);
                    let spawner = NpcSpawner::new(
                        init(inner.next_id, id, game_data_ids.clone(), template.clone()),
                        shared.clone(),
                    );
                    spawner.initialize_spawnable_npcs(template.as_deref());
                    spawners.push(Arc::new(spawner));
                    inner.next_id += 1;
                    added += 1;
                }
            }
            let key = inner.next_id;
            inner.npc_spawners.entry(key).or_insert(spawners);
            self.invalidate_activation_index();
            Ok(added)
        } else {
            // NPC spawns for events.
            let last = *ids.last().expect("non-empty");
            let template = NpcSpawnerTemplate::with_member(last, b.unit_id);
            let spawner = NpcSpawner::new(
                init(last, b.spawner_id, ids, Some(Arc::new(template))),
                new_shared(),
            );
            let key = inner.next_id;
            inner
                .npc_event_spawners
                .entry(key)
                .or_insert_with(|| vec![Arc::new(spawner)]);
            inner.next_id += 1;
            Ok(0)
        }
    }

    // ----- Load -----

    /// `Load()`: spawn files, persistent doodads (main world) and the respawn tick.
    pub async fn load(self: &Arc<Self>, server: &Arc<GameServer>) -> anyhow::Result<LoadStats> {
        if self.loaded.load(Ordering::SeqCst) {
            return Ok(LoadStats::default());
        }
        tracing::info!("Loading spawn data for {} ...", self.world.template.name);
        let world_path = server
            .config
            .app_path
            .join("Data")
            .join("Worlds")
            .join(&self.world.template.name);
        let gd = &server.game_data;
        let sd = &gd.systems.npcs.npc_spawners;
        let npc = |id| gd.systems.npcs.npc.exist(id);
        let doodad = |id| gd.systems.npcs.doodad.exist(id);
        let transfer = |id| sd.transfer_template_ids.contains(&id);
        let gimmick = |id| gd.systems.npcs.gimmick.exist(id);
        let slave = |id| sd.slave_template_ids.contains(&id);
        let exists = TemplateExists {
            npc: &npc,
            doodad: &doodad,
            transfer: &transfer,
            gimmick: &gimmick,
            slave: &slave,
        };
        let stats = self
            .load_spawn_files(server, &world_path, &exists)
            .map_err(anyhow::Error::msg)?;

        if self.world.template.id == gd.worlds.default_world_template_id() {
            // Load player housing data; houses need to be spawned before doodads.
            crate::systems::housing::load_for_world(server, &self.world).await?;
            tracing::info!(
                "Loading persistent doodads for {}",
                self.world.template.name
            );
            let mut spawned = 0;
            // Load furniture and bound doodads
            spawned += self
                .spawn_persistent_doodads(server, doodad_owner_type::HOUSING, -1, None, false)
                .await?;
            // Reconcile bound doodads: spawn any missing from DB, remove duplicates
            crate::systems::housing::after_housing_doodads_loaded(server);
            // Load plants/packs and everything else that was placed into the world by players
            for owner_type in [doodad_owner_type::SYSTEM, doodad_owner_type::CHARACTER] {
                spawned += self
                    .spawn_persistent_doodads(server, owner_type, -1, None, false)
                    .await?;
            }
            tracing::info!("{spawned} doodads loaded in {}.", self.world.template.name);
        }

        self.start_respawn_tick(server);
        self.loaded.store(true, Ordering::SeqCst);
        Ok(stats)
    }

    /// The spawn-file part of `Load` (`LoadNpcSpawns` .. `LoadSlaveSpawns`) from `world_path`
    /// (clears every spawner list first). `Err` = the C# `GameException` / parse failure.
    pub fn load_spawn_files(
        &self,
        server: &GameServer,
        world_path: &Path,
        exists: &TemplateExists<'_>,
    ) -> Result<LoadStats, String> {
        // Serialize the replacement with the activation pass, creation and despawn paths (the
        // reentrant lock; this function is sync and never held across an await).
        let _serial = server.systems.npcs.spawns.spawn_lock();
        self.respawns.lock().expect("poisoned").clear();
        self.despawns.lock().expect("poisoned").clear();
        {
            let mut inner = self.inner();
            inner.npc_spawners.clear();
            inner.npc_event_spawners.clear();
            inner.tower_def_spawners.clear();
            inner.doodad_spawners.clear();
            inner.transfer_spawners.clear();
            inner.gimmick_spawners.clear();
            inner.slave_spawners.clear();
            inner.player_doodads.clear();
        }
        // Reload: the ordinary spawner table was emptied and is refilled below; both steps
        // invalidate the activation index/queues.
        self.invalidate_activation_index();
        self.reset_activation();
        let mut stats = LoadStats::default();
        self.load_npc_spawns(server, world_path, exists, &mut stats)?;
        self.load_doodad_spawns(server, world_path, exists, &mut stats)?;
        self.load_simple_spawns(
            server,
            world_path,
            "transfer_spawns",
            exists.transfer,
            &mut stats,
        )?;
        self.load_gimmick_spawns(server, world_path, exists, &mut stats)?;
        self.load_simple_spawns(server, world_path, "slave_spawns", exists.slave, &mut stats)?;
        stats.tower_def_spawners = self.inner().tower_def_spawners.values().map(Vec::len).sum();
        tracing::info!("Spawn data for {}: {stats:?}", self.world.template.name);
        Ok(stats)
    }

    /// World id, zone id and `DegToRad` of a loaded position.
    fn fix_position(
        &self,
        server: &GameServer,
        p: &mut crate::data::world_templates::WorldSpawnPosition,
        rotate: bool,
    ) {
        p.world_id = self.world.id;
        p.zone_id = server
            .game_data
            .worlds
            .get_zone_id(Some(&self.world.template), p.x, p.y);
        if rotate {
            p.yaw = deg_to_rad(p.yaw);
            p.pitch = deg_to_rad(p.pitch);
            p.roll = deg_to_rad(p.roll);
        }
    }

    fn load_npc_spawns(
        &self,
        server: &GameServer,
        world_path: &Path,
        exists: &TemplateExists<'_>,
        stats: &mut LoadStats,
    ) -> Result<(), String> {
        let Some(mut files) = spawn_files(world_path, "npc_spawns") else {
            return Ok(());
        };
        files.reverse(); // ReverseSpawnFiles
                         // Positions of the entries stored in NpcSpawners, by UnitId (the duplicate
                         // filter; Z is part of the position, so cave / building floors do not collide).
        let mut stored: HashMap<u32, Vec<(f32, f32, f32)>> = HashMap::new();
        for path in files {
            let Some(text) = read_spawn_file(&self.world, &path) else {
                continue;
            };
            stats.npc_files += 1;
            let defs = parse_list(&text, NpcSpawnerDef::from_json)
                .map_err(|e| format!("SpawnManager: Parse {} file: {e}", path.display()))?;
            for (entry, mut def) in defs.into_iter().enumerate() {
                stats.npc_entries += 1;
                let (unit_id, x, y, z) = (
                    def.base.unit_id,
                    def.base.position.x,
                    def.base.position.y,
                    def.base.position.z,
                );
                if stored.get(&unit_id).is_some_and(|v| {
                    v.iter().any(|&(sx, sy, sz)| {
                        (sx - x).abs() < 2.0 && (sy - y).abs() < 2.0 && (sz - z).abs() < 2.0
                    })
                }) {
                    tracing::trace!(
                        "Duplicate NPC spawner found in {} (UnitId: {unit_id})",
                        path.display()
                    );
                    stats.npc_duplicates += 1;
                    continue;
                }
                if !(exists.npc)(unit_id) {
                    tracing::trace!(
                        "Npc Template {unit_id} (file entry {}) doesn't exist - {}",
                        entry + 1,
                        path.display()
                    );
                    stats.npc_missing_templates += 1;
                    continue;
                }
                self.fix_position(server, &mut def.base.position, true);
                let event = !def.npc_spawner_ids.is_empty() && def.npc_spawner_ids != [0];
                let fake_before = server.systems.npcs.spawns.peek_fake_spawner_id();
                let added = self.add_npc_spawner(server, def)?;
                if event {
                    stats.npc_event_spawners += 1;
                } else if server.systems.npcs.spawns.peek_fake_spawner_id() != fake_before {
                    stats.npc_fake_spawners += 1;
                }
                if added > 0 {
                    stats.npc_entries_with_spawners += 1;
                    stats.npc_spawners += added;
                    stored.entry(unit_id).or_default().push((x, y, z));
                }
            }
        }
        Ok(())
    }

    fn load_doodad_spawns(
        &self,
        server: &GameServer,
        world_path: &Path,
        exists: &TemplateExists<'_>,
        stats: &mut LoadStats,
    ) -> Result<(), String> {
        let Some(mut files) = spawn_files(world_path, "doodad_spawns") else {
            return Ok(());
        };
        files.reverse(); // ReverseSpawnFiles
        let mut stored: HashMap<u32, Vec<(f32, f32, f32)>> = HashMap::new();
        for path in files {
            let Some(text) = read_spawn_file(&self.world, &path) else {
                continue;
            };
            stats.doodad_files += 1;
            let defs = parse_list(&text, DoodadSpawnerDef::from_json)
                .map_err(|e| format!("SpawnManager: Parse {} file: {e}", path.display()))?;
            for (entry, mut def) in defs.into_iter().enumerate() {
                stats.doodad_entries += 1;
                let p = def.base.position;
                if stored.get(&def.base.unit_id).is_some_and(|v| {
                    v.iter().any(|&(x, y, z)| {
                        (x - p.x).abs() < 0.01 && (y - p.y).abs() < 0.01 && (z - p.z).abs() < 0.01
                    })
                }) {
                    stats.doodad_duplicates += 1;
                    continue;
                }
                if !(exists.doodad)(def.base.unit_id) {
                    tracing::warn!(
                        "Doodad Template {} (file entry {}) doesn't exist - {}",
                        def.base.unit_id,
                        entry + 1,
                        path.display()
                    );
                    stats.doodad_missing_templates += 1;
                    continue;
                }
                let mut inner = self.inner();
                def.base.id = inner.next_id;
                self.fix_position(server, &mut def.base.position, true);
                let unit_id = def.base.unit_id;
                let key = inner.next_id;
                inner
                    .doodad_spawners
                    .insert(key, Arc::new(DoodadSpawner::new(def, Some(self.world.id))));
                inner.next_id += 1;
                stats.doodad_spawners += 1;
                stored.entry(unit_id).or_default().push((p.x, p.y, p.z));
            }
        }
        Ok(())
    }

    /// `LoadTransferSpawns` / `LoadSlaveSpawns` (not reversed, no duplicate filter).
    fn load_simple_spawns(
        &self,
        server: &GameServer,
        world_path: &Path,
        prefix: &str,
        exists: &dyn Fn(u32) -> bool,
        stats: &mut LoadStats,
    ) -> Result<(), String> {
        let Some(files) = spawn_files(world_path, prefix) else {
            return Ok(());
        };
        let transfer = prefix.starts_with("transfer");
        for path in files {
            let Some(text) = read_spawn_file(&self.world, &path) else {
                continue;
            };
            let defs = parse_list(&text, SimpleSpawnerDef::from_json)
                .map_err(|e| format!("SpawnManager: Parse {} file: {e}", path.display()))?;
            for (entry, mut def) in defs.into_iter().enumerate() {
                if transfer {
                    stats.transfer_entries += 1;
                } else {
                    stats.slave_entries += 1;
                }
                if !exists(def.base.unit_id) {
                    tracing::warn!(
                        "{} Template {} (file entry {}) doesn't exist - {}",
                        if transfer { "Transfer" } else { "Slave" },
                        def.base.unit_id,
                        entry + 1,
                        path.display()
                    );
                    continue;
                }
                let mut inner = self.inner();
                def.base.id = inner.next_id;
                self.fix_position(server, &mut def.base.position, true);
                let key = inner.next_id;
                if transfer {
                    inner.transfer_spawners.insert(key, def);
                    stats.transfer_spawners += 1;
                } else {
                    inner.slave_spawners.insert(key, def);
                    stats.slave_spawners += 1;
                }
                inner.next_id += 1;
            }
        }
        Ok(())
    }

    /// `LoadGimmickSpawns` (no `DegToRad`; `UnitId == 0` is accepted).
    fn load_gimmick_spawns(
        &self,
        server: &GameServer,
        world_path: &Path,
        exists: &TemplateExists<'_>,
        stats: &mut LoadStats,
    ) -> Result<(), String> {
        let Some(files) = spawn_files(world_path, "gimmick_spawns") else {
            return Ok(());
        };
        for path in files {
            let Some(text) = read_spawn_file(&self.world, &path) else {
                continue;
            };
            let defs = crate::systems::npcs::json::parse(&text)
                .map_err(|e| e.to_string())
                .and_then(|doc| parse_gimmick_spawners(&doc))
                .map_err(|e| format!("SpawnManager: Parse {} file: {e}", path.display()))?;
            for (entry, mut def) in defs.into_iter().enumerate() {
                stats.gimmick_entries += 1;
                if def.unit_id != 0 && !(exists.gimmick)(def.unit_id) {
                    tracing::error!(
                        "Gimmick Template {} (file entry {}) doesn't exist - {}",
                        def.unit_id,
                        entry + 1,
                        path.display()
                    );
                    continue;
                }
                let Some(position) = def.position.as_mut() else {
                    // C#: NullReferenceException on `spawner.Position`.
                    return Err(format!(
                        "SpawnManager: {} entry {}: Position is null",
                        path.display(),
                        entry + 1
                    ));
                };
                self.fix_position(server, position, false);
                let mut inner = self.inner();
                def.id = inner.next_id;
                let key = inner.next_id;
                inner.gimmick_spawners.insert(key, def);
                inner.next_id += 1;
                stats.gimmick_spawners += 1;
            }
        }
        Ok(())
    }

    // ----- persistent doodads -----

    /// `SpawnPersistentDoodads(ownerType, ownerId, useParentObject, doSpawn)`: the `doodads`
    /// rows of an owner type (optionally of one `house_id`) into `PlayerDoodads`.
    pub async fn spawn_persistent_doodads(
        &self,
        server: &GameServer,
        owner_type_to_spawn: u8,
        owner_to_spawn_id: i64,
        use_parent_object: Option<&WorldObject>,
        do_spawn: bool,
    ) -> anyhow::Result<usize> {
        use crate::db::{dt, int, unix};
        use sqlx::Row;

        let mut sql = String::from(
            "SELECT CAST(id AS SIGNED) AS id, CAST(template_id AS SIGNED) AS template_id, \
             CAST(current_phase_id AS SIGNED) AS current_phase_id, x, y, z, roll, pitch, yaw, scale, \
             plant_time, growth_time, phase_time, CAST(owner_id AS SIGNED) AS owner_id, \
             CAST(owner_type AS SIGNED) AS owner_type, CAST(attach_point AS SIGNED) AS attach_point, \
             CAST(item_id AS UNSIGNED) AS item_id, CAST(house_id AS SIGNED) AS house_id, \
             CAST(parent_doodad AS SIGNED) AS parent_doodad, \
             CAST(item_template_id AS SIGNED) AS item_template_id, \
             CAST(item_container_id AS UNSIGNED) AS item_container_id, CAST(data AS SIGNED) AS data, \
             CAST(farm_type AS SIGNED) AS farm_type, interaction_counts FROM doodads WHERE owner_type = ?",
        );
        if owner_to_spawn_id >= 0 {
            sql.push_str(" AND house_id = ?");
        }
        sql.push_str(" ORDER BY `plant_time`");
        let mut q = sqlx::query(&sql).bind(owner_type_to_spawn);
        if owner_to_spawn_id >= 0 {
            q = q.bind(owner_to_spawn_id);
        }
        let rows = q.fetch_all(&server.db).await?;
        let mut count = 0;
        let mut new_coffers = Vec::new();
        for r in rows {
            let template_id = int(&r, "template_id")? as u32;
            let db_id = int(&r, "id")? as u32;
            let f = |c: &str| -> anyhow::Result<f32> { Ok(r.try_get::<f32, _>(c)?) };
            let (x, y, z) = (f("x")?, f("y")?, f("z")?);
            let (roll, pitch, yaw) = (f("roll")?, f("pitch")?, f("yaw")?);
            let scale = f("scale")?;
            let owner_id = int(&r, "owner_id")? as u32;
            let owner_type = int(&r, "owner_type")? as u8;
            let attach_point = int(&r, "attach_point")? as u8;
            let item_id: u64 = r.try_get("item_id")?;
            let house_id = int(&r, "house_id")? as u32;
            let parent_doodad = int(&r, "parent_doodad")? as u32;
            let item_template_id = int(&r, "item_template_id")? as u32;
            let item_container_id: u64 = r.try_get("item_container_id")?;
            let data = int(&r, "data")? as i32;
            let farm_type = int(&r, "farm_type")? as u8;
            let phase_id = int(&r, "current_phase_id")? as u32;

            let Some(doodad) =
                backend::doodad_create(server, &self.world, 0, template_id, None, true)
            else {
                // C#: NullReferenceException on the null doodad.
                anyhow::bail!("persistent doodad {db_id}: template {template_id} does not exist");
            };
            forget_object_fields(server, doodad.obj_id);
            let (item_tpl, ucc) = server
                .systems
                .npcs
                .hooks
                .economy
                .get()
                .and_then(|h| h.item_template_and_ucc(server, item_id))
                .unwrap_or((item_template_id, 0));
            let plant_time = dt(&r, "plant_time");
            let growth_time = dt(&r, "growth_time");
            let phase_time = dt(&r, "phase_time");
            {
                let mut d = doodad.data();
                d.db_id = db_id;
                d.owner_id = owner_id;
                d.owner_type = owner_type;
                d.attach_point = attach_point;
                d.plant_time = unix(plant_time);
                d.growth_time_ms = growth_time.map_or(0, |t| t.and_utc().timestamp_millis().max(0));
                d.item_id = item_id;
                d.owner_db_id = house_id;
                d.scale = if scale != 0.0 { scale } else { 1.0 };
                d.item_template_id = item_tpl;
                d.ucc_id = ucc;
                d.data = data;
                d.farm_type = farm_type;
            }
            // `FuncGroupId = phaseId` (setter: PhaseTime = now, Save, phase caches), then
            // `OverridePhaseTime = PhaseTime = phase_time`.
            crate::systems::npcs::doodad::manager::set_func_group_id(server, &doodad, phase_id);
            let interaction_counts = r
                .try_get::<Option<String>, _>("interaction_counts")?
                .map(|json| serde_json::from_str(&json))
                .transpose()?;
            {
                let pt = phase_time.map_or(0, |t| t.and_utc().timestamp_millis().max(0));
                let mut d = doodad.data();
                d.phase.override_phase_time_ms = pt;
                d.phase_time_ms = pt;
                d.phase.interaction_counts = interaction_counts.unwrap_or_default();
            }
            let doodad_obj = WorldObject::from(&doodad);
            let mut has_parent = false;
            if parent_doodad > 0 {
                let parent = self
                    .inner()
                    .player_doodads
                    .iter()
                    .find(|d| d.data().db_id == parent_doodad)
                    .cloned();
                match parent {
                    None => tracing::warn!(
                        "Unable to place doodad {db_id} can't find it's parent doodad {parent_doodad}"
                    ),
                    Some(p) => {
                        doodad.data().parent_obj_id = p.obj_id;
                        self.world.attach_child(&WorldObject::from(&p), &doodad_obj);
                        has_parent = true;
                    }
                }
            }
            if house_id > 0 && !has_parent {
                // HousingManager.GetHouseById(OwnerDbId): parent + bound doodad registration
                // (UsePersistentHouseDoodads).
                if !crate::systems::housing::attach_loaded_doodad(
                    server,
                    &self.world,
                    house_id,
                    &doodad,
                ) {
                    tracing::warn!(
                        "Unable to place doodad {db_id} can't find it's owning house {house_id}"
                    );
                }
            }
            if let Some(p) = use_parent_object {
                doodad.data().parent_obj_id = p.obj_id();
                self.world.attach_child(p, &doodad_obj);
            }
            doodad
                .data()
                .transform
                .set_position(x, y, z, roll, pitch, yaw);
            // Attach ItemContainer to coffer if needed
            if doodad.data().coffer.is_some() {
                if item_container_id > 0 {
                    let is_coffer_container = server
                        .items
                        .container_by_id(item_container_id)
                        .is_some_and(|c| {
                            c.lock().expect("container poisoned").kind
                                == crate::models::container::ContainerKind::Coffer
                        });
                    if is_coffer_container {
                        if let Some(c) = doodad.data().coffer.as_mut() {
                            c.container_id = item_container_id;
                        }
                    } else {
                        tracing::error!(
                            "Unable to attach ItemContainer {item_container_id} to DoodadCoffer, objId: {}, DbId: {db_id}",
                            doodad.obj_id
                        );
                    }
                } else {
                    tracing::warn!(
                        "DoodadCoffer has no persistent ItemContainer assigned to it, creating new one, objId: {}, DbId: {db_id}",
                        doodad.obj_id
                    );
                    if let Some(h) = server.systems.npcs.hooks.economy.get() {
                        h.initialize_coffer(server, &doodad, owner_id);
                    }
                    // Mark for saving again later when we're done with this loop
                    new_coffers.push(doodad.clone());
                }
            }
            // Slave AttachedDoodads
            if owner_type_to_spawn == doodad_owner_type::SLAVE {
                if let Some(slave) = use_parent_object.and_then(WorldObject::as_slave) {
                    let mut s = slave.data();
                    if !s.attached_doodads.contains(&doodad.obj_id) {
                        s.attached_doodads.push(doodad.obj_id);
                    }
                }
            }
            // Only enable saving after the phase, counters, parent and position are restored.
            doodad.data().is_persistent = true;
            if do_spawn {
                backend::doodad_spawn(server, &self.world, &doodad);
                crate::systems::npcs::doodad::manager::init_doodad(server, &self.world, &doodad);
            }
            if !doodad.data().phase.deleted {
                self.inner().player_doodads.push(doodad.clone());
                count += 1;
            }
        }
        // Save Coffer Doodads that had a new ItemContainer created for them
        for coffer in new_coffers {
            crate::systems::npcs::doodad::db::save(server, &coffer);
        }
        Ok(count)
    }

    /// `GetPlayerDoodads(charId)`.
    pub fn get_player_doodads(&self, char_id: u32) -> Vec<DoodadRef> {
        self.inner()
            .player_doodads
            .iter()
            .filter(|d| d.data().owner_id == char_id)
            .cloned()
            .collect()
    }

    /// `GetAllPlayerDoodads()`.
    pub fn get_all_player_doodads(&self) -> Vec<DoodadRef> {
        self.inner().player_doodads.clone()
    }

    /// `RemovePlayerDoodad(doodad)`.
    pub fn remove_player_doodad(&self, doodad: &DoodadRef) {
        let mut inner = self.inner();
        if let Some(i) = inner
            .player_doodads
            .iter()
            .position(|d| Arc::ptr_eq(d, doodad))
        {
            inner.player_doodads.remove(i);
        }
    }

    /// `AddPlayerDoodad(doodad)`.
    pub fn add_player_doodad(&self, doodad: DoodadRef) {
        self.inner().player_doodads.push(doodad);
    }

    // ----- SpawnAll -----

    /// `SpawnAll()`: doodads, transfers, gimmicks, slaves and player doodads on background
    /// tasks (`SpawnTasks`). NPCs are not spawned here: the active-region tick does it.
    pub fn spawn_all(self: &Arc<Self>, server: &Arc<GameServer>) {
        tracing::info!("Spawning NPCs...");
        tracing::info!("Spawning Doodads...");
        let mut tasks = Vec::new();
        {
            let (me, server) = (self.clone(), server.clone());
            tasks.push(tokio::task::spawn_blocking(move || {
                me.spawn_doodads(&server)
            }));
        }
        {
            let (me, server) = (self.clone(), server.clone());
            tasks.push(tokio::task::spawn_blocking(move || {
                me.spawn_other_spawners(&server)
            }));
        }
        {
            let (me, server) = (self.clone(), server.clone());
            tasks.push(tokio::task::spawn_blocking(move || {
                me.spawn_player_doodads(&server)
            }));
        }
        self.spawn_tasks.lock().expect("poisoned").extend(tasks);
    }

    /// Waits for the `SpawnTasks` started so far; returns the spawned counts.
    pub async fn wait_spawn_tasks(&self) -> Vec<usize> {
        let tasks: Vec<_> = self
            .spawn_tasks
            .lock()
            .expect("poisoned")
            .drain(..)
            .collect();
        let mut out = Vec::new();
        for t in tasks {
            out.push(t.await.unwrap_or(0));
        }
        out
    }

    /// The doodad task of `SpawnAll`: `spawner.Spawn(0)` for every doodad spawner.
    pub fn spawn_doodads(&self, server: &GameServer) -> usize {
        let start = std::time::Instant::now();
        let spawners: Vec<_> = self.inner().doodad_spawners.values().cloned().collect();
        tracing::info!(
            "Spawning {} Doodads in world {}",
            spawners.len(),
            self.world.template.name
        );
        let mut count = 0;
        for s in spawners {
            s.spawn(server, 0);
            count += 1;
            if count % 5000 == 0 {
                tracing::debug!(
                    "In world {} Doodads spawned: {count}...",
                    self.world.template.name
                );
            }
        }
        tracing::info!(
            "In world {} Doodads spawned: {count} in {:?}",
            self.world.template.name,
            start.elapsed()
        );
        // You have to wait for all the doodads to spawn before trying to initialize the fish
        // schools (FishSchoolManager.Load(World)).
        crate::systems::npcs::fish_school::load_world(server, &self.world);
        // Link doodads seats to the trial system
        if self.world.id == crate::models::character::DEFAULT_INSTANCE_ID {
            self.link_trial_spawners(server);
        }
        count
    }

    /// `TrialManager.Load()` + `InitializeDoodads(World)` + `InitializeNpc(World)`: the jury /
    /// audience seats (`justice.<court>.seat.<n>` / `.spectator` doodad spawners, their last
    /// spawned doodad) and the judges (`justice.<court>.judge` NPC spawners).
    pub fn link_trial_spawners(&self, server: &GameServer) {
        let trials = &server.systems.social.trials;
        trials.load(server);
        let doodads: Vec<(String, DoodadRef)> = self
            .get_special_doodad_spawners("justice.")
            .into_iter()
            .filter_map(|s| s.last().map(|d| (s.special_link.clone(), d)))
            .collect();
        trials.initialize_doodads(&doodads);
        let npcs: Vec<(String, u32)> = self
            .get_special_npc_spawners("justice.")
            .into_iter()
            .map(|s| (s.special_link.clone(), s.id))
            .collect();
        trials.initialize_npc(&npcs);
    }

    /// The transfer / gimmick / slave tasks of `SpawnAll` (returns the gimmicks spawned).
    /// `TransferSpawner.SpawnAll` / `SlaveSpawner.Spawn(0)` are the vehicles subsystem's.
    pub fn spawn_other_spawners(&self, server: &GameServer) -> usize {
        let (transfer_defs, gimmicks, slave_defs) = {
            let inner = self.inner();
            (
                inner
                    .transfer_spawners
                    .values()
                    .cloned()
                    .collect::<Vec<_>>(),
                inner.gimmick_spawners.values().cloned().collect::<Vec<_>>(),
                inner.slave_spawners.values().cloned().collect::<Vec<_>>(),
            )
        };
        let (transfers, slaves) = (transfer_defs.len(), slave_defs.len());
        let name = &self.world.template.name;
        tracing::info!("Spawning {transfers} Transfers in world {name}");
        crate::systems::vehicles::transfers::spawn_all(server, &self.world, transfer_defs);
        tracing::info!("Spawning {} Gimmicks in world {name}", gimmicks.len());
        let mut count = 0;
        for g in &gimmicks {
            // GimmickSpawner.Spawn(0)
            crate::systems::npcs::gimmick::spawn_from_spawner(server, &self.world, g);
            count += 1;
            if count % 25 == 0 {
                tracing::debug!("In world {name} Gimmicks spawned: {count}...");
            }
        }
        tracing::info!("In world {name} Gimmicks spawned: {count}");
        tracing::info!("Spawning {slaves} Slaves in world {name}");
        crate::systems::vehicles::slaves::spawn_all(server, &self.world, slave_defs);
        count
    }

    /// The player-doodad task of `SpawnAll`.
    pub fn spawn_player_doodads(&self, server: &GameServer) -> usize {
        let doodads = self.get_all_player_doodads();
        if !doodads.is_empty() {
            tracing::info!("Spawning {} Player Doodads", doodads.len());
        }
        let mut count = 0;
        for d in doodads {
            match server.systems.npcs.spawns.doodad_spawner_of(d.obj_id) {
                None => {
                    backend::doodad_spawn(server, &self.world, &d);
                    crate::systems::npcs::doodad::manager::init_doodad(server, &self.world, &d);
                    count += 1;
                }
                Some(s) => {
                    if s.spawn(server, d.obj_id).is_none() {
                        let (db_id, tpl) = {
                            let x = d.data();
                            (x.db_id, x.template_id)
                        };
                        tracing::error!(
                            "Failed to spawn player doodad DbId:{db_id}, TemplateId: {tpl}"
                        );
                    }
                }
            }
        }
        count
    }

    // ----- respawn / despawn -----

    /// `Stop()`.
    pub fn stop(&self) {
        self.work.store(false, Ordering::SeqCst);
    }

    fn schedule(
        &self,
        server: &GameServer,
        set: &Mutex<HashMap<u32, Scheduled>>,
        fields: &DashMap<u32, Instant>,
        obj: WorldObject,
        at: Option<Instant>,
    ) {
        let id = obj.obj_id();
        if let Some(t) = at {
            fields.insert(id, t);
        }
        let at = fields.get(&id).map(|t| *t);
        let spawns = &server.systems.npcs.spawns;
        let link = match &obj {
            WorldObject::Npc(_) => spawns
                .npc_spawner_of(id)
                .map_or(SpawnerLink::None, SpawnerLink::Npc),
            WorldObject::Doodad(_) => spawns
                .doodad_spawner_of(id)
                .map_or(SpawnerLink::None, SpawnerLink::Doodad),
            _ => SpawnerLink::None,
        };
        let mut set = set.lock().expect("poisoned");
        match set.get_mut(&id) {
            Some(e) if e.obj.same(&obj) => {
                e.at = at;
                e.link = link;
            }
            _ => {
                let seq = self.seq.fetch_add(1, Ordering::SeqCst);
                set.insert(id, Scheduled { obj, link, at, seq });
            }
        }
    }

    /// `AddRespawn(obj)` with `obj.Respawn = at` (`None` keeps the object's current value).
    pub fn add_respawn(&self, server: &GameServer, obj: WorldObject, at: Option<Instant>) {
        let fields = &server.systems.npcs.spawns.respawn_fields;
        self.schedule(server, &self.respawns, fields, obj, at);
    }

    /// `AddDespawn(obj)` with `obj.Despawn = at` (`None` keeps the object's current value).
    pub fn add_despawn(&self, server: &GameServer, obj: WorldObject, at: Option<Instant>) {
        let fields = &server.systems.npcs.spawns.despawn_fields;
        self.schedule(server, &self.despawns, fields, obj, at);
    }

    /// Objects waiting for a respawn (obj id, time).
    pub fn pending_respawns(&self) -> Vec<(u32, Option<Instant>)> {
        let mut v: Vec<_> = self
            .respawns
            .lock()
            .expect("poisoned")
            .values()
            .cloned()
            .collect();
        v.sort_by_key(|e| e.seq);
        v.into_iter().map(|e| (e.obj.obj_id(), e.at)).collect()
    }

    /// Objects waiting for a despawn (obj id, time).
    pub fn pending_despawns(&self) -> Vec<(u32, Option<Instant>)> {
        let mut v: Vec<_> = self
            .despawns
            .lock()
            .expect("poisoned")
            .values()
            .cloned()
            .collect();
        v.sort_by_key(|e| e.seq);
        v.into_iter().map(|e| (e.obj.obj_id(), e.at)).collect()
    }

    /// Whether `obj_id` already waits for a queued corpse / immediate despawn in this instance.
    pub fn has_pending_despawn(&self, obj_id: u32) -> bool {
        self.despawns
            .lock()
            .expect("poisoned")
            .contains_key(&obj_id)
    }

    /// Whether the placement (`shared` entry of one JSON line, shared by its memberwise clones)
    /// still owns queued respawn / despawn deadlines in this instance. The activation sweep uses
    /// it to keep an emptied cold placement tracked until its timers drain (every deadline is
    /// processed by [`Self::check_respawns_tick`], so the set cannot grow forever).
    fn placement_has_pending_timers(&self, shared: &Arc<NpcSpawnerShared>) -> bool {
        let linked = |e: &Scheduled| matches!(&e.link, SpawnerLink::Npc(s) if Arc::ptr_eq(s.shared(), shared));
        if self
            .respawns
            .lock()
            .expect("poisoned")
            .values()
            .any(&linked)
        {
            return true;
        }
        self.despawns
            .lock()
            .expect("poisoned")
            .values()
            .any(&linked)
    }

    fn ready(
        set: &Mutex<HashMap<u32, Scheduled>>,
        fields: &DashMap<u32, Instant>,
        now: Instant,
    ) -> Vec<Scheduled> {
        let mut v: Vec<Scheduled> = set
            .lock()
            .expect("poisoned")
            .values()
            .cloned()
            .map(|mut e| {
                // Looting updates obj.Despawn after scheduling the corpse. Read the
                // live field, like CheckRespawns in C#, rather than its old snapshot.
                if let Some(at) = fields.get(&e.obj.obj_id()) {
                    e.at = Some(*at);
                }
                e
            })
            .filter(|e| e.at.is_none_or(|t| t <= now))
            .collect();
        v.sort_by_key(|e| e.seq);
        v
    }

    fn remove_entry(set: &Mutex<HashMap<u32, Scheduled>>, e: &Scheduled) {
        let mut set = set.lock().expect("poisoned");
        if set.get(&e.obj.obj_id()).is_some_and(|x| x.seq == e.seq) {
            set.remove(&e.obj.obj_id());
        }
    }

    /// One iteration of `CheckRespawns` (the 1 s respawn / despawn thread).
    pub fn check_respawns_tick(&self, server: &GameServer) {
        let _serial = server.systems.npcs.spawns.spawn_lock();
        for e in Self::ready(
            &self.respawns,
            &server.systems.npcs.spawns.respawn_fields,
            now(),
        ) {
            if e.at.is_some_and(|t| t > now()) {
                continue;
            }
            match (&e.link, &e.obj) {
                // `npc.Spawner.Respawn(npc)` is disabled: Update() spawns again.
                (SpawnerLink::Npc(s), _) => s.set_spawn_scheduled(false),
                (SpawnerLink::Doodad(s), WorldObject::Doodad(d)) => s.respawn(server, d),
                // Transfer spawner respawns (vehicles). Gimmicks are never scheduled in the 1.2
                // C# (`GimmickSpawner.DecreaseCount` has no caller).
                (_, o) => crate::systems::vehicles::respawn_object(server, o),
            }
            Self::remove_entry(&self.respawns, &e);
            if matches!(e.link, SpawnerLink::Npc(_))
                && !self
                    .despawns
                    .lock()
                    .expect("poisoned")
                    .contains_key(&e.obj.obj_id())
                && self.world.object(e.obj.obj_id()).is_none()
            {
                // The looted corpse can disappear before its respawn timer. Reserve
                // its ID until both timers finish so another NPC cannot replace the
                // pending entry or supply a different live deadline under the same ID.
                server.ids.object.release_id(e.obj.obj_id());
                forget_object(server, e.obj.obj_id());
            }
        }

        for e in Self::ready(
            &self.despawns,
            &server.systems.npcs.spawns.despawn_fields,
            now(),
        ) {
            if e.at.is_some_and(|t| t > now()) {
                continue;
            }
            match (&e.link, &e.obj) {
                (SpawnerLink::Npc(s), WorldObject::Npc(n)) => s.despawn(server, n),
                (SpawnerLink::Doodad(s), WorldObject::Doodad(d)) => s.despawn(server, d),
                // Transfer spawner despawns (vehicles); `slave.Delete()` is the common delete.
                (_, o) => {
                    if !crate::systems::vehicles::despawn_object(server, o) {
                        delete_object(server, o, Some(self.world.id));
                    }
                }
            }
            // DoodadSpawner owns its ID release and may retain it for a pending respawn.
            if !matches!(
                (&e.link, &e.obj),
                (SpawnerLink::Doodad(_), WorldObject::Doodad(_))
            ) && !self
                .respawns
                .lock()
                .expect("poisoned")
                .get(&e.obj.obj_id())
                .is_some_and(|r| matches!(r.link, SpawnerLink::Npc(_)) && r.obj.same(&e.obj))
            {
                server.ids.object.release_id(e.obj.obj_id());
                forget_object(server, e.obj.obj_id());
            }
            Self::remove_entry(&self.despawns, &e);
        }

        drop(_serial);
        if let Some(h) = server.systems.npcs.hooks.economy.get() {
            h.make_loot_public_tick(server, self.world.id);
        }
    }

    fn start_respawn_tick(self: &Arc<Self>, server: &Arc<GameServer>) {
        let Ok(rt) = tokio::runtime::Handle::try_current() else {
            tracing::warn!("RespawnThread_{}: no runtime", self.world.id);
            return;
        };
        let me = self.clone();
        let weak = Arc::downgrade(server);
        rt.spawn(async move {
            while me.work.load(Ordering::SeqCst) {
                let Some(server) = weak.upgrade() else {
                    break;
                };
                me.check_respawns_tick(&server);
                drop(server);
                tokio::time::sleep(Duration::from_secs(1)).await;
            }
        });
    }

    // ----- queries -----

    /// `GetAllSpawners()` (a copy).
    pub fn get_all_spawners(&self) -> BTreeMap<u32, Vec<Arc<NpcSpawner>>> {
        self.inner().npc_spawners.clone()
    }

    /// A doodad management group uses explicit authored membership, not spawner template IDs.
    pub fn set_management_group(
        &self,
        server: &GameServer,
        zone: u32,
        group: u32,
        enabled: bool,
    ) -> usize {
        if group == 0 {
            return 0;
        }
        let _serial = server.systems.npcs.spawns.spawn_lock();
        let spawners: Vec<_> = {
            let inner = self.inner();
            inner
                .npc_spawners
                .values()
                .chain(inner.npc_event_spawners.values())
                .flatten()
                .filter(|s| s.shared().management_group() == group && s.position().zone_id == zone)
                .cloned()
                .collect()
        };
        for spawner in &spawners {
            spawner.shared().set_management_enabled(enabled);
            if enabled {
                spawner.do_event_spawn(server);
            } else {
                for npc in spawner.spawned_npcs().into_values().flatten() {
                    if !self
                        .world
                        .npc(npc.obj_id)
                        .is_some_and(|current| Arc::ptr_eq(&current, &npc))
                    {
                        continue;
                    }
                    if npc.data().dead_time_ms > 0 {
                        self.cancel_npc_respawn(&npc);
                        continue;
                    }
                    self.cancel_npc_timers(&npc);
                    spawner.despawn(server, &npc);
                    server.ids.object.release_id(npc.obj_id);
                    forget_object(server, npc.obj_id);
                }
            }
        }
        spawners.len()
    }

    /// Authored controller positions for a tower defense spawner template.
    pub fn tower_def_spawners(&self, id: u32) -> Vec<Arc<NpcSpawner>> {
        self.inner()
            .tower_def_spawners
            .get(&id)
            .cloned()
            .unwrap_or_default()
    }

    /// Cancel corpse/respawn timers before deleting an event-owned NPC. Prevents a later
    /// timer from deleting a different object after its id has been reused.
    pub fn cancel_npc_timers(&self, npc: &NpcRef) {
        for set in [&self.respawns, &self.despawns] {
            Self::cancel_npc_timer(set, npc);
        }
    }

    /// Stop an event NPC from respawning while leaving its normal loot corpse timer intact.
    pub fn cancel_npc_respawn(&self, npc: &NpcRef) {
        Self::cancel_npc_timer(&self.respawns, npc);
    }

    /// `NpcSpawner.ForceSpawn` repopulates a placement immediately, so its queued respawn
    /// deadlines are obsolete. Cancels only the respawns of spawners sharing `shared` (one
    /// authored placement across its memberwise clones); corpse despawn timers, other placements
    /// and other instances stay intact. An id is released only when neither the corpse despawn
    /// entry nor a live world object still owns it, mirroring `check_respawns_tick`. Every
    /// cancelled clone's `_scheduledCount` credits are reconciled too (its future timers are
    /// gone), not just the `ForceSpawn` caller's. Returns the number of cancelled respawns.
    pub fn cancel_placement_npc_respawns(
        &self,
        server: &GameServer,
        shared: &Arc<NpcSpawnerShared>,
    ) -> usize {
        let _serial = server.systems.npcs.spawns.spawn_lock();
        let entries: Vec<Scheduled> = {
            let set = self.respawns.lock().expect("poisoned");
            set.values()
                .filter(|e| match &e.link {
                    SpawnerLink::Npc(s) => Arc::ptr_eq(s.shared(), shared),
                    _ => false,
                })
                .cloned()
                .collect()
        };
        for e in &entries {
            Self::remove_entry(&self.respawns, e);
            let id = e.obj.obj_id();
            // Drop the queue locks before the world / object locks (as `check_respawns_tick`).
            let corpse_pending = self.despawns.lock().expect("poisoned").contains_key(&id);
            if !corpse_pending && self.world.object(id).is_none() {
                server.ids.object.release_id(id);
                forget_object(server, id);
            }
            // All same-placement deadlines are gone, so each linked clone's credits are stale.
            if let SpawnerLink::Npc(s) = &e.link {
                s.reconcile_scheduled_count(0);
            }
        }
        entries.len()
    }

    fn cancel_npc_timer(set: &Mutex<HashMap<u32, Scheduled>>, npc: &NpcRef) {
        let mut entries = set.lock().expect("poisoned");
        if entries
            .get(&npc.obj_id)
            .is_some_and(|e| e.obj.as_npc().is_some_and(|n| Arc::ptr_eq(n, npc)))
        {
            entries.remove(&npc.obj_id);
        }
    }

    /// `GetNpcSpawner(spawnerId)`: the event spawners with that `Id` (retargets their last
    /// template member to `UnitId`, as the C# does).
    pub fn get_npc_spawner(&self, spawner_id: u32) -> Vec<Arc<NpcSpawner>> {
        let inner = self.inner();
        let mut ret = Vec::new();
        for spawners in inner.npc_event_spawners.values() {
            for s in spawners {
                if s.id != spawner_id {
                    continue;
                }
                s.retarget_last_template_npc();
                ret.push(s.clone());
            }
        }
        ret
    }

    /// `GetNpcSpawner(unitId, unit)`: a new spawner at the unit's position (`SpawnEffect`).
    /// Mirrors the C# rewrite of the shared game-data template (`Template.Npcs = [first member
    /// retargeted to unitId]`).
    pub fn get_npc_spawner_at(
        &self,
        server: &GameServer,
        unit_id: u32,
        unit: &WorldObject,
    ) -> Option<Arc<NpcSpawner>> {
        let _g = self.lock_spawner.lock().expect("poisoned");
        let data = &server.game_data.systems.npcs.npc_spawners;
        let (id, template) = match data.get_spawner_ids(unit_id) {
            None => {
                let id = server.ids.object.get_next_id().ok()?;
                let mut t = NpcSpawnerTemplate::with_spawner(id);
                t.npcs[0].member_id = unit_id;
                t.npcs[0].unit_id = unit_id;
                t.npcs[0].member_type = "Npc".into();
                (id, Arc::new(t))
            }
            Some(ids) => {
                let id = *ids.first()?;
                data.get_npc_spawner_template(id)?;
                let mut nsn = data.get_npc_spawner_npc(id)?;
                nsn.member_id = unit_id;
                nsn.unit_id = unit_id;
                data.replace_template_npcs(id, nsn);
                (id, data.get_npc_spawner_template(id)?)
            }
        };
        let world = self.world.world_pos_rot(unit);
        let t = unit.transform();
        let position = crate::data::world_templates::WorldSpawnPosition {
            world_id: t.world_id,
            zone_id: t.zone_id,
            x: world.x,
            y: world.y,
            z: world.z,
            yaw: world.rot_z,
            pitch: 0.0,
            roll: 0.0,
        };
        Some(Arc::new(NpcSpawner::new(
            NpcSpawnerInit {
                id,
                spawner_id: 0,
                unit_id,
                follow_path: String::new(),
                follow_npc: 0,
                respawn_time: 15,
                despawn_time: 20,
                special_link: String::new(),
                count: 1,
                npc_spawner_ids: vec![id],
                parent_world: Some(self.world.id),
                template: Some(template),
                spawnable_npcs: Vec::new(),
            },
            NpcSpawnerShared::new(position),
        )))
    }

    /// `CloneNpcEventSpawners(from, to)`.
    pub fn clone_npc_event_spawners(&self, from: u8, to: u8) -> bool {
        let mut inner = self.inner();
        let value = inner
            .npc_event_spawners
            .get(&u32::from(from))
            .cloned()
            .unwrap_or_default();
        if inner.npc_event_spawners.contains_key(&u32::from(to)) {
            return false;
        }
        inner.npc_event_spawners.insert(u32::from(to), value);
        true
    }

    /// `RemoveNpcEventSpawners(from)`.
    pub fn remove_npc_event_spawners(&self, from: u8) -> bool {
        self.inner()
            .npc_event_spawners
            .remove(&u32::from(from))
            .is_some()
    }

    /// `GetTreasureChestDoodadSpawners()`: doodad spawners whose template (or respawn
    /// template) is a treasure chest.
    pub fn get_treasure_chest_doodad_spawners(
        &self,
        server: &GameServer,
    ) -> Vec<Arc<DoodadSpawner>> {
        let ids = server
            .game_data
            .systems
            .npcs
            .doodad
            .treasure_chest_template_ids();
        self.inner()
            .doodad_spawners
            .values()
            .filter(|d| ids.contains(&d.respawn_doodad_template_id()) || ids.contains(&d.unit_id))
            .cloned()
            .collect()
    }

    /// `GetDoodadSpawnersByUnitId(unitId)`.
    pub fn get_doodad_spawners_by_unit_id(&self, unit_id: u32) -> Vec<Arc<DoodadSpawner>> {
        let _g = self.lock_spawner.lock().expect("poisoned");
        self.inner()
            .doodad_spawners
            .values()
            .filter(|d| d.unit_id == unit_id)
            .cloned()
            .collect()
    }

    /// `GetNExtSpawnerId()`.
    pub fn get_next_spawner_id(&self) -> u32 {
        let mut inner = self.inner();
        let r = inner.next_id;
        inner.next_id += 1;
        r
    }

    /// `GetSpecialDoodadSpawners(header)`.
    pub fn get_special_doodad_spawners(&self, header: &str) -> Vec<Arc<DoodadSpawner>> {
        self.inner()
            .doodad_spawners
            .values()
            .filter(|d| !d.special_link.trim().is_empty() && d.special_link.starts_with(header))
            .cloned()
            .collect()
    }

    /// `GetSpecialNpcSpawners(header)`.
    pub fn get_special_npc_spawners(&self, header: &str) -> Vec<Arc<NpcSpawner>> {
        self.inner()
            .npc_spawners
            .values()
            .flatten()
            .filter(|s| !s.special_link.trim().is_empty() && s.special_link.starts_with(header))
            .cloned()
            .collect()
    }

    /// Loaded spawner counts (npc clones, event, doodad, transfer, gimmick, slave).
    pub fn counts(&self) -> [usize; 6] {
        let inner = self.inner();
        [
            inner.npc_spawners.values().map(Vec::len).sum(),
            inner.npc_event_spawners.values().map(Vec::len).sum(),
            inner.doodad_spawners.len(),
            inner.transfer_spawners.len(),
            inner.gimmick_spawners.len(),
            inner.slave_spawners.len(),
        ]
    }

    /// The loaded doodad spawners.
    pub fn doodad_spawners(&self) -> Vec<Arc<DoodadSpawner>> {
        self.inner().doodad_spawners.values().cloned().collect()
    }

    /// The loaded gimmick spawner entries (for the gimmick module).
    pub fn gimmick_spawners(&self) -> Vec<GimmickSpawner> {
        self.inner().gimmick_spawners.values().cloned().collect()
    }

    // ----- teardown -----

    /// `DeSpawnAll()`: every NPC / doodad / mate / slave of the instance; returns the count.
    /// Serialized with the activation pass; the warm registry is dropped afterwards (nothing of
    /// the instance is left to track, so a deliberately emptied instance cannot leave a growing
    /// set of placements whose 9 999 999 s respawn timers never drain).
    pub fn despawn_all(&self, server: &GameServer) -> usize {
        let _serial = server.systems.npcs.spawns.spawn_lock();
        let spawns = &server.systems.npcs.spawns;
        let mut res = 0;
        for npc in self.world.all_npcs() {
            match spawns.npc_spawner_of(npc.obj_id) {
                Some(s) => {
                    s.set_respawn_time(9_999_999);
                    s.do_despawn(server, &npc);
                }
                None => crate::systems::npcs::npc::manager::delete(server, &self.world, &npc),
            }
            res += 1;
        }
        for doodad in self.world.all_doodads() {
            match spawns.doodad_spawner_of(doodad.obj_id) {
                Some(s) => {
                    s.set_respawn_time(9_999_999);
                    s.despawn(server, &doodad);
                }
                None => {
                    // Detach from the save system so the delete does not remove the DB row.
                    doodad.data().is_persistent = false;
                    backend::doodad_delete(server, &self.world, &doodad);
                }
            }
            res += 1;
        }
        // Mate / Slave: GameObject.Delete (no override in C#).
        for kind in [ObjectKind::Mate, ObjectKind::Slave] {
            for o in self.world.all_of_kind(kind) {
                self.world.delete(o);
                res += 1;
            }
        }
        self.reset_activation();
        res
    }

    /// `DeleteAllSpawners()`: removes every spawner and what they spawned.
    pub fn delete_all_spawners(&self, server: &GameServer) {
        // Serialized with the activation pass so a stale queue cannot create NPCs between
        // despawning a placement's old members and detaching / resetting it.
        let _serial = server.systems.npcs.spawns.spawn_lock();
        let npc_spawners: Vec<_> = self
            .inner()
            .npc_spawners
            .values()
            .flatten()
            .cloned()
            .collect();
        for s in npc_spawners {
            for npc in s.spawned_npcs().into_values().flatten() {
                // npc.UnregisterNpcEvents(): the AI (and its event registrations) goes with the
                // deleted NPC.
                s.despawn(server, &npc);
            }
            s.clear_spawned_npcs();
            s.set_parent_world(None);
        }
        self.inner().npc_spawners.clear();
        self.invalidate_activation_index();
        self.reset_activation();
        let doodad_spawners: Vec<_> = self.inner().doodad_spawners.values().cloned().collect();
        for s in doodad_spawners {
            for d in s.spawned() {
                s.despawn(server, &d);
            }
            s.set_parent_world(None);
        }
        self.inner().doodad_spawners.clear();
        self.inner().gimmick_spawners.clear();
        // World.GimmickManager._activeGimmicks -> gimmick.Delete() (GameObject.Delete: the
        // active list itself is left as is, as in C#).
        for g in self.world.all_of_kind(ObjectKind::Gimmick) {
            self.world.delete(g);
        }
    }

    // ----- activation -----

    /// The ordinary `NpcSpawners` membership changed: the region index and the candidate queues
    /// are stale. Only the atomic generation is touched here, so callers may hold the inner lock.
    fn invalidate_activation_index(&self) {
        self.activation_generation.fetch_add(1, Ordering::SeqCst);
    }

    /// Drops the activation index, queues and warmed registry (the spawner lists were replaced
    /// wholesale: load / reload / delete-all). The caller must not hold the inner lock.
    fn reset_activation(&self) {
        let mut state = self.activation.lock().expect("spawn manager poisoned");
        state.index_built = false;
        state.index.clear();
        state.authored_index.clear();
        state.built = false;
        state.visible.clear();
        state.prewarm.clear();
        state.warm_ids.clear();
        state.visible_cursor = 0;
        state.prewarm_cursor = 0;
        state.warmed.clear();
    }

    /// Rebuilds the per-region index and the candidate queues when the world's player activity
    /// revision, the spawner membership generation or the prewarm margin changed. An unchanged
    /// revision/generation/margin keeps the cached queues (unfilled targets continue there).
    /// Candidates are the configured visible / prewarm rings plus placements whose authored 3D
    /// trigger sphere contains a player (far-authored, prewarm tier).
    fn refresh_activation(&self, margin: u32) {
        // Capture the revision *before* the player snapshot: a movement / arrival during the
        // collection bumps the revision afterwards, so the captured value stays behind and the
        // next pass rebuilds instead of stamping the stale snapshot with the new revision (which
        // would keep a false queue until the next activity change).
        let revision = self.world.player_activity_revision();
        // The client packet visibility radius is `WorldManager.REGION_NEIGHBORHOOD_SIZE` (2
        // regions) and stays unchanged; the prewarm ring adds the configured margin beyond it.
        const CLIENT_NEIGHBOURHOOD: i32 = 2;
        // Player regions / positions (with Z for the far-authored 3D sphere) are read before the
        // activation lock (object data locks are leaves, the activation lock is not held here
        // yet).
        let players: Vec<PlayerSnapshot> = self
            .world
            .all_characters()
            .into_iter()
            .filter_map(|p| {
                let (inst, rx, ry) = p.region()?;
                if inst != self.world.id {
                    return None;
                }
                let pr = self.world.world_pos_rot(&WorldObject::from(&p));
                Some(((rx, ry), (pr.x, pr.y, pr.z)))
            })
            .collect();
        let generation = self.activation_generation.load(Ordering::SeqCst);
        let mut state = self.activation.lock().expect("spawn manager poisoned");
        if !state.index_built || state.index_generation != generation {
            let mut index: HashMap<(i32, i32), Vec<Arc<NpcSpawner>>> = HashMap::new();
            let mut authored: HashMap<u32, HashMap<(i32, i32), Vec<AuthoredCandidate>>> =
                HashMap::new();
            for spawner in self.inner().npc_spawners.values().flatten() {
                let p = spawner.position();
                if let Some(region) = self.world.region_by_pos(p.x, p.y) {
                    index.entry(region).or_default().push(spawner.clone());
                }
                // An authored trigger never reachable through the visible ring is bucketed by
                // its required radius (the ring already covers everything up to radius 2).
                let radius = spawner.authored_spawn_radius();
                if radius.is_finite() && radius > 0.0 {
                    let g = (radius / REGION_SIZE as f32).ceil() as u32;
                    if g > CLIENT_NEIGHBOURHOOD as u32 {
                        let cell = g as f32 * REGION_SIZE as f32;
                        let key = ((p.x / cell).floor() as i32, (p.y / cell).floor() as i32);
                        authored.entry(g).or_default().entry(key).or_default().push(
                            AuthoredCandidate {
                                spawner: spawner.clone(),
                                radius,
                            },
                        );
                    }
                }
            }
            state.index = index;
            state.authored_index = authored;
            state.index_generation = generation;
            state.index_built = true;
            state.built = false;
        }
        if state.built
            && state.revision == revision
            && state.queue_generation == generation
            && state.margin == margin
        {
            return;
        }
        state.revision = revision;
        state.queue_generation = generation;
        state.margin = margin;
        state.built = true;
        state.visible.clear();
        state.prewarm.clear();
        state.warm_ids.clear();
        state.visible_cursor = 0;
        state.prewarm_cursor = 0;
        if players.is_empty() {
            // Keep `warmed`: the sweep still books the retention countdown of prepared
            // placements (and keeps residents) after the last player left.
            return;
        }
        let warm_radius = CLIENT_NEIGHBOURHOOD + margin as i32;
        // Deduplicate across players, ring overlaps and far-authored candidates: visible wins,
        // nearest horizontal distance wins.
        let mut found: HashMap<u32, (bool, f32, Arc<NpcSpawner>)> = HashMap::new();
        for &((rx, ry), (px, py, _pz)) in &players {
            for dx in -warm_radius..=warm_radius {
                for dy in -warm_radius..=warm_radius {
                    let region = (rx + dx, ry + dy);
                    if !self.world.template.valid_region(region.0, region.1) {
                        continue;
                    }
                    let Some(spawners) = state.index.get(&region) else {
                        continue;
                    };
                    let visible = dx.abs().max(dy.abs()) <= CLIENT_NEIGHBOURHOOD;
                    for spawner in spawners {
                        // A template-less spawner cannot ever spawn. An empty member list is not
                        // filtered here: `initialize_spawnable_npcs` may fill it later while this
                        // cached queue lives on, so the fill stays retryable.
                        if spawner.template().is_none() {
                            continue;
                        }
                        let p = spawner.position();
                        let d = (p.x - px) * (p.x - px) + (p.y - py) * (p.y - py);
                        found
                            .entry(spawner.id)
                            .and_modify(|e| {
                                e.0 |= visible;
                                if d < e.1 {
                                    e.1 = d;
                                }
                            })
                            .or_insert((visible, d, spawner.clone()));
                    }
                }
            }
        }
        // Far-authored eligibility beyond the configured ring: a player inside a placement's
        // authored 3D sphere queues it even when its region sits outside the visible / prewarm
        // rings. Each radius group probes only the nine buckets around the player (the cell size
        // equals the group's required radius), then the exact 3D sphere decides; a group the
        // configured ring already covers is skipped, as the queues above hold those placements.
        for &((_rx, _ry), (px, py, pz)) in &players {
            for (&g, cells) in &state.authored_index {
                if g <= warm_radius as u32 {
                    continue;
                }
                let cell = g as f32 * REGION_SIZE as f32;
                let (cx, cy) = ((px / cell).floor() as i32, (py / cell).floor() as i32);
                for dx in -1..=1 {
                    for dy in -1..=1 {
                        let Some(bucket) = cells.get(&(cx + dx, cy + dy)) else {
                            continue;
                        };
                        for candidate in bucket {
                            // Same template gate as the ring queues; an empty member list stays
                            // queued and is retried once it is initialized later.
                            if candidate.spawner.template().is_none() {
                                continue;
                            }
                            let p = candidate.spawner.position();
                            let (ox, oy, oz) = (p.x - px, p.y - py, p.z - pz);
                            if ox * ox + oy * oy + oz * oz > candidate.radius * candidate.radius {
                                continue;
                            }
                            let d = ox * ox + oy * oy;
                            found
                                .entry(candidate.spawner.id)
                                .and_modify(|e| {
                                    if d < e.1 {
                                        e.1 = d;
                                    }
                                })
                                .or_insert((false, d, candidate.spawner.clone()));
                        }
                    }
                }
            }
        }
        let mut visible: Vec<(f32, Arc<NpcSpawner>)> = Vec::new();
        let mut prewarm: Vec<(f32, Arc<NpcSpawner>)> = Vec::new();
        for (is_visible, d, spawner) in found.into_values() {
            if is_visible {
                visible.push((d, spawner));
            } else {
                prewarm.push((d, spawner));
            }
        }
        visible.sort_by(|a, b| a.0.total_cmp(&b.0));
        prewarm.sort_by(|a, b| a.0.total_cmp(&b.0));
        state.visible = visible.into_iter().map(|(_, s)| s).collect();
        state.prewarm = prewarm.into_iter().map(|(_, s)| s).collect();
        let mut warm_ids = HashSet::new();
        for spawner in state.visible.iter().chain(state.prewarm.iter()) {
            warm_ids.insert(spawner.id);
        }
        state.warm_ids = warm_ids;
    }

    /// Processes one of this instance's candidate queues from its rotation cursor within the
    /// remaining global creation budget. Unvisited candidates are left `queued` for the next
    /// pass; a completed sweep wraps the cursor so the next pass starts at the queue head again.
    fn activation_phase(
        &self,
        server: &GameServer,
        margin: u32,
        visible_phase: bool,
        budget: &mut i64,
        stats: &mut ActivationStats,
    ) {
        self.refresh_activation(margin);
        let (queue, mut cursor) = {
            let state = self.activation.lock().expect("spawn manager poisoned");
            if visible_phase {
                (state.visible.clone(), state.visible_cursor)
            } else {
                (state.prewarm.clone(), state.prewarm_cursor)
            }
        };
        let len = queue.len();
        if len == 0 {
            return;
        }
        let mut examined: Vec<Arc<NpcSpawner>> = Vec::new();
        let mut swept = false;
        if *budget > 0 {
            let mut i = cursor;
            while i < len && *budget > 0 {
                let spawner = &queue[i];
                let created = spawner.update_warm(server, true, *budget);
                i += 1;
                if created > 0 {
                    *budget -= created as i64;
                    stats.created += created;
                }
                examined.push(spawner.clone());
                stats.candidates += 1;
            }
            cursor = i;
            if cursor >= len {
                cursor = 0;
                swept = true;
            }
        }
        // The resident classification (static game data) and the live-population reads
        // (object-data leaves) happen before taking the activation guard; only the registry is
        // mutated under it. Every examined placement becomes tracked - also the currently empty
        // ones (e.g. corpse / respawn timers pending) so a later refill stays reclaimable.
        let residents = server.systems.npcs.spawns.resident_npcs(server);
        let resident_gate = server.config.world.npc_resident_service_npcs;
        let prepared: Vec<(Arc<NpcSpawner>, bool)> = examined
            .into_iter()
            .map(|spawner| {
                let resident = resident_gate && spawner_is_resident(server, residents, &spawner);
                (spawner, resident)
            })
            .collect();
        let mut state = self.activation.lock().expect("spawn manager poisoned");
        for (spawner, resident) in prepared {
            state.warmed.insert(
                spawner.id,
                WarmEntry {
                    spawner,
                    resident,
                    expires_at: None,
                },
            );
        }
        if visible_phase {
            state.visible_cursor = cursor;
        } else {
            state.prewarm_cursor = cursor;
        }
        if !swept {
            stats.queued += len - cursor;
        }
    }

    /// Maintenance pass over the warmed registry: clears the retention booking of placements
    /// inside the warm set, starts the stable countdown of the ones that dropped out, reclaims
    /// expired ordinary placements under the shared NPC budget and drops tracking once nothing
    /// (living NPCs or queued corpse / respawn deadlines) is left. Authored schedule /
    /// management despawns keep priority over residency and retention. Only the warmed registry
    /// is walked, never the full spawner table.
    fn activation_sweep(&self, server: &GameServer, budget: &mut i64, stats: &mut ActivationStats) {
        let now = now();
        let retention = Duration::from_secs(server.config.world.npc_warm_retention_seconds);
        // Snapshot the two halves before taking the activation guard; the object-data /
        // population reads below happen without it.
        let mut cold: Vec<(Option<Instant>, u32, Arc<NpcSpawner>, bool)> = Vec::new();
        let mut warm: Vec<u32> = Vec::new();
        {
            let state = self.activation.lock().expect("spawn manager poisoned");
            for (id, entry) in state.warmed.iter() {
                if state.warm_ids.contains(id) {
                    warm.push(*id);
                } else {
                    cold.push((entry.expires_at, *id, entry.spawner.clone(), entry.resident));
                }
            }
        }
        if !warm.is_empty() {
            let mut state = self.activation.lock().expect("spawn manager poisoned");
            for id in warm {
                if let Some(entry) = state.warmed.get_mut(&id) {
                    entry.expires_at = None;
                }
            }
        }
        if cold.is_empty() {
            return;
        }
        // Oldest booking first: a placement that dropped out never loses its stable deadline,
        // and budget-starved passes continue with the same oldest entrants next time.
        cold.sort_by(|a, b| a.0.cmp(&b.0).then_with(|| a.1.cmp(&b.1)));
        let resident_gate = server.config.world.npc_resident_service_npcs;
        let residents = server.systems.npcs.spawns.resident_npcs(server);
        let mut bookings: Vec<(u32, Instant)> = Vec::new();
        let mut drop_entries: Vec<u32> = Vec::new();
        for (expires_at, id, spawner, resident) in cold {
            // The homogeneous residency fast path is re-validated against the live members: a
            // placement whose classification is cached from an earlier pass must not stay
            // skipped once a runtime-owned (never resident) member appears.
            let resident_skip = resident
                && resident_gate
                && !spawner_has_nonresident_live_npc(server, residents, &spawner);
            if spawner.can_despawn_npcs(server) {
                // Authored schedule / management removal keeps its own lifecycle: it wins over
                // residency and retention.
                spawner.despawn_npcs(server);
            } else if !resident_skip {
                let deadline = match expires_at {
                    Some(t) => t,
                    None => {
                        let t = now + retention;
                        bookings.push((id, t));
                        t
                    }
                };
                if now >= deadline && *budget > 0 {
                    let removed =
                        spawner.reclaim_cold_npcs(server, *budget, residents, resident_gate);
                    *budget -= removed as i64;
                    stats.reclaimed += removed;
                }
            }
            // Stop tracking a placement with nothing left: no living NPCs and no queued
            // corpse / respawn deadlines (kept ones drain through the 1 s respawn tick, so the
            // registry cannot grow forever).
            if spawner.spawned_npcs().is_empty()
                && !self.placement_has_pending_timers(spawner.shared())
            {
                drop_entries.push(id);
            }
        }
        let mut state = self.activation.lock().expect("spawn manager poisoned");
        for (id, deadline) in bookings {
            if let Some(entry) = state.warmed.get_mut(&id) {
                entry.expires_at = Some(deadline);
            }
        }
        for id in drop_entries {
            state.warmed.remove(&id);
        }
    }
}

/// Whether an ordinary placement is homogeneously resident: every authored member (or the
/// `UnitId` fallback without a member list) is a quest giver / reporter or service role of the
/// immutable [`ResidentNpcTemplates`] set. Only such a placement may skip distance reclamation
/// as a whole, and only while no live member diverges from that classification (runtime
/// ownership, see [`spawner_has_nonresident_live_npc`]); a mixed placement is reclaimed per live
/// NPC ([`npc_is_resident`]), so its nonresident members expire while its residents stay.
/// Hostile quest kill / accept-kill targets and ordinary neutral NPCs are not residents.
fn spawner_is_resident(
    server: &GameServer,
    residents: &ResidentNpcTemplates,
    spawner: &NpcSpawner,
) -> bool {
    let npcs = &server.game_data.systems.npcs.npc;
    let is_resident = |id: u32| npcs.template(id).is_some_and(|t| residents.is_resident(t));
    let members = spawner.spawnable_npcs();
    if members.is_empty() {
        is_resident(spawner.unit_id)
    } else {
        members.iter().all(|member| is_resident(member.member_id))
    }
}

/// Whether any live NPC of an otherwise resident placement is not resident per the
/// authoritative live decision ([`npc_is_resident`], which excludes runtime-owned NPCs). A
/// cached homogeneous classification must not skip reclamation while such a member exists - it
/// follows its owner's lifecycle like any nonresident.
fn spawner_has_nonresident_live_npc(
    server: &GameServer,
    residents: &ResidentNpcTemplates,
    spawner: &NpcSpawner,
) -> bool {
    spawner
        .spawned_npcs()
        .values()
        .flatten()
        .any(|npc| !npc_is_resident(server, residents, npc))
}

// ---------------------------------------------------------------------------------------------
// startup, active-region tick, NPC death
// ---------------------------------------------------------------------------------------------

/// One bounded activation pass over every instance with a spawn manager (tests / diagnostics
/// call this; production goes through [`active_region_tick`]).
///
/// Each instance's candidate queues are refreshed only when its player activity revision, its
/// ordinary spawner membership or the prewarm margin changed, so an unchanged pass never scans
/// the full spawner table. Candidates fill within the global `World.NpcSpawnsPerTick` created-NPC
/// budget: client-visible neighbourhoods before prewarm rings, nearest first, and the starting
/// instance rotates every pass so small budgets cannot starve later instances. Afterwards the
/// warmed registry is swept: placements that dropped out of the warm set keep their NPCs for
/// `World.NpcWarmRetentionSeconds` (residents stay after their first preparation) and expired
/// ordinary ones are reclaimed within the same per-tick budget, counted in actual NPCs.
pub fn active_region_tick_stats(server: &GameServer) -> ActivationStats {
    // The whole pass holds the spawners' reentrant lock: creation, despawn, placement reload and
    // delete-all serialize against the queued candidates (sync fn, never held across an await).
    let _serial = server.systems.npcs.spawns.spawn_lock();
    let margin = prewarm_region_margin(&server.config.world);
    let mut budget = i64::from(server.config.world.npc_spawns_per_tick.max(1));
    let worlds = server.systems.npcs.spawns.all();
    let mut stats = ActivationStats::default();
    if worlds.is_empty() {
        return stats;
    }
    let n = worlds.len();
    let start = server
        .systems
        .npcs
        .spawns
        .activation_rotation
        .fetch_add(1, Ordering::Relaxed) as usize
        % n;
    for i in 0..n {
        worlds[(start + i) % n].activation_phase(server, margin, true, &mut budget, &mut stats);
    }
    for i in 0..n {
        worlds[(start + i) % n].activation_phase(server, margin, false, &mut budget, &mut stats);
    }
    let mut reclaim_budget = i64::from(server.config.world.npc_spawns_per_tick.max(1));
    for i in 0..n {
        worlds[(start + i) % n].activation_sweep(server, &mut reclaim_budget, &mut stats);
    }
    stats
}

/// `WorldManager.ActiveRegionTick` spawner part (the production entry point; the structured
/// result is [`active_region_tick_stats`]).
pub fn active_region_tick(server: &GameServer) {
    let _ = active_region_tick_stats(server);
}

/// Starts the active-region tick (once per server) at `World.NpcSpawnTickMs`.
pub fn start_active_region_tick(server: &Arc<GameServer>) {
    if server
        .systems
        .npcs
        .spawns
        .active_tick_started
        .swap(true, Ordering::SeqCst)
    {
        return;
    }
    let Ok(rt) = tokio::runtime::Handle::try_current() else {
        return;
    };
    // Build the immutable resident-template snapshot once, before the first pass, instead of
    // lazily inside it (it scans the static quest acts).
    let _ = server.systems.npcs.spawns.resident_npcs(server);
    let weak = Arc::downgrade(server);
    rt.spawn(async move {
        loop {
            // Re-read the interval every pass so a config reload applies without a restart;
            // `0` never spins (see [`spawn_tick_interval`]).
            let wait = match weak.upgrade() {
                Some(s) => spawn_tick_interval(&s.config.world),
                None => break,
            };
            tokio::time::sleep(wait).await;
            let Some(server) = weak.upgrade() else {
                break;
            };
            let start = std::time::Instant::now();
            crate::systems::run_blocking(&server, |s| {
                let _ = active_region_tick_stats(s);
            })
            .await;
            if start.elapsed() > Duration::from_millis(100) {
                tracing::warn!("ActiveRegionTick took {} ms", start.elapsed().as_millis());
            }
        }
    });
}

/// `WorldManager.CreateStaticInstances` spawn part: `SpawnManager.Load()` + `SpawnAll()` for
/// `main_world` (NPCs appear through the active-region tick).
pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
    let Some(world) = server
        .world
        .world(crate::models::character::DEFAULT_INSTANCE_ID)
    else {
        return Ok(());
    };
    let manager = server.systems.npcs.spawns.get_or_create(&world);
    manager.load(server).await?;
    manager.spawn_all(server);
    start_active_region_tick(server);
    Ok(())
}

/// `Npc.DoDie` → `Spawner?.DoDespawn(this)`: schedules the despawn (corpse) and, when allowed,
/// the respawn of an NPC that died. No-op for NPCs without a spawner. The caller must not hold
/// any object lock.
pub fn on_npc_died(server: &GameServer, npc: &NpcRef) {
    if let Some(s) = server.systems.npcs.spawns.npc_spawner_of(npc.obj_id) {
        s.do_despawn(server, npc);
    }
}
