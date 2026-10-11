//! `NpcSpawner` (`Models/Game/NPChar/NpcSpawner.cs`) and the spawning half of `NpcSpawnerNpc`
//! (`Models/Game/NPChar/NpcSpawnerNpc.cs`, see [`spawn_member`]).
//!
//! C# object graph and how it maps here:
//! * `SpawnManager.AddNpcSpawner` makes *memberwise clones* of one JSON entry, one per game-data
//!   spawner template id. A memberwise clone shares the reference-type members: `Position`,
//!   `SpawnedNpcs` (keyed by `SpawnerId`), the three 10-second caches and `NpcSpawnerIds`.
//!   They live in [`NpcSpawnerShared`] (one `Arc` per JSON entry); the value members
//!   (`_scheduledCount`, `IsSpawnScheduled`, `RespawnTime`, ...) are per clone.
//! * `npc.Spawner` = `server.systems.npcs.spawns.npc_spawner_of(obj_id)` (plus
//!   `Npc::spawner_id` = `Spawner.Id`).
//! * The static `_spawnLock` is the server-wide reentrant `SpawnManagers::spawn_lock` (C#
//!   `lock` is reentrant and the C# methods nest it freely).
//! * `DateTime.UtcNow` for caches / respawn times is `tokio::time::Instant` (mockable in tests);
//!   `npc.DeadTime` is the unix-ms `Npc::dead_time_ms`.
//!
//! Locking: the per-spawner `state` / shared mutexes are leaf locks, never held while locking an
//! NPC or calling into the world. Callers must not hold an object data lock.

use std::collections::{BTreeMap, HashMap};
use std::sync::{Arc, Mutex, MutexGuard};

use tokio::time::Instant;

use super::spawner_data::{npc_spawner_category, NpcSpawnerNpc, NpcSpawnerTemplate};
use crate::data::world_templates::WorldSpawnPosition;
use crate::server::GameServer;
use crate::systems::npcs::schedule::PeriodStatus;
use crate::systems::npcs::spawn_manager::{self, backend};
use crate::world::{NpcRef, WorldInstance, WorldObject};

/// `LootingContainer.LootDespawnExtensionTime` (seconds).
pub const LOOT_DESPAWN_EXTENSION_TIME: f32 = 300.0;

/// Seconds the player / population caches stay valid.
const CACHE_SECONDS: f64 = 10.0;

/// The reference-type members shared by memberwise clones of one spawner entry.
#[derive(Debug, Default)]
pub struct NpcSpawnerShared {
    /// `Position`.
    position: Mutex<WorldSpawnPosition>,
    /// `SpawnedNpcs` (`<SpawnerId, List<Npc>>`).
    spawned: Mutex<BTreeMap<u32, Vec<NpcRef>>>,
    /// `_playerCountCache`.
    player_count_cache: Mutex<HashMap<u32, (i32, Instant)>>,
    /// `_playerInRadiusCache`.
    player_in_radius_cache: Mutex<HashMap<u32, (bool, Instant)>>,
    /// `_npcsInZoneCache` (count, areNpcsInZone).
    npcs_in_zone_cache: Mutex<HashMap<u32, (i32, bool, Instant)>>,
}

impl NpcSpawnerShared {
    pub fn new(position: WorldSpawnPosition) -> Arc<Self> {
        Arc::new(Self {
            position: Mutex::new(position),
            ..Self::default()
        })
    }
}

/// The value members of a spawner (copied by a memberwise clone).
#[derive(Debug, Clone)]
pub struct NpcSpawnerInit {
    pub id: u32,
    pub spawner_id: u32,
    pub unit_id: u32,
    pub follow_path: String,
    pub follow_npc: u32,
    pub respawn_time: i32,
    pub despawn_time: i32,
    pub special_link: String,
    pub count: u32,
    pub npc_spawner_ids: Vec<u32>,
    /// `ParentWorld` (instance id).
    pub parent_world: Option<u32>,
    pub template: Option<Arc<NpcSpawnerTemplate>>,
    /// `SpawnableNpcs`.
    pub spawnable_npcs: Vec<NpcSpawnerNpc>,
}

#[derive(Debug, Clone)]
struct State {
    parent_world: Option<u32>,
    template: Option<Arc<NpcSpawnerTemplate>>,
    spawnable_npcs: Vec<NpcSpawnerNpc>,
    respawn_time: i32,
    /// `_scheduledCount`.
    scheduled_count: i32,
    is_spawn_scheduled: bool,
    is_despawn_scheduled: bool,
    /// `RespawnDenied` (written, never read in the C#).
    respawn_denied: bool,
}

/// `SpawnEffect` members `NpcSpawner.DoSpawnEffect` reads.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct SpawnEffectParams {
    pub use_summoner_faction: bool,
    pub use_summoner_aggro_target: bool,
    /// `LifeTime` (seconds; > 0 schedules `NpcSpawnerDoDespawnTask`).
    pub life_time: f32,
}

/// `NpcSpawner`.
#[derive(Debug)]
pub struct NpcSpawner {
    /// `Id` (index).
    pub id: u32,
    /// `SpawnerId` (spawner template id).
    pub spawner_id: u32,
    /// `UnitId` (NPC template id).
    pub unit_id: u32,
    pub follow_path: String,
    pub follow_npc: u32,
    pub despawn_time: i32,
    pub special_link: String,
    pub count: u32,
    pub npc_spawner_ids: Vec<u32>,
    shared: Arc<NpcSpawnerShared>,
    state: Mutex<State>,
}

/// `TimeSpan.FromHours(hours)` ticks (100 ns).
fn ticks_from_hours(hours: f32) -> i64 {
    (f64::from(hours) * 36_000_000_000.0) as i64
}

/// `NpcSpawner.IsTimeBetween(currentTime, startTime, endTime)` (TimeSpan ticks): inclusive,
/// wrapping over midnight when `start > end`.
pub fn is_time_between(current: i64, start: i64, end: i64) -> bool {
    if start <= end {
        current >= start && current <= end
    } else {
        current >= start || current <= end
    }
}

/// `IsTimeBetween` on game-clock hours (`TimeSpan.FromHours` of each value).
pub fn is_hour_between(current: f32, start: f32, end: f32) -> bool {
    is_time_between(
        ticks_from_hours(current),
        ticks_from_hours(start),
        ticks_from_hours(end),
    )
}

/// `Random.Shared.Next(float min, float max)` (`RandomExtensions`): `NextSingle() * (max - min)
/// + min`.
pub fn random_float(min: f32, max: f32) -> f32 {
    rand::random::<f32>() * (max - min) + min
}

/// `IEnumerableExtensions.RandomElementByWeight`.
pub fn random_element_by_weight<T>(items: &[T], weight: impl Fn(&T) -> f32) -> Option<&T> {
    let total: f32 = items.iter().map(&weight).sum();
    let target = rand::random::<f32>() * total;
    let mut current = 0.0f32;
    for item in items {
        current += weight(item);
        if current >= target {
            return Some(item);
        }
    }
    None
}

fn period_status_npc(server: &GameServer, id: u32) -> PeriodStatus {
    server
        .game_data
        .systems
        .npcs
        .schedule
        .period_status_npc(id as i32, spawn_manager::unix_now())
}

/// `Transform.World.Position` of a player (composed with its parent when attached).
fn player_world_pos(server: &GameServer, p: &crate::world::PlayerRef) -> (f32, f32, f32) {
    let t = p.data().unit.transform;
    if !t.has_parent() {
        return (t.x, t.y, t.z);
    }
    let obj = WorldObject::from(p);
    match server.world.world_of(&obj) {
        Some(w) => {
            let pr = w.world_pos_rot(&obj);
            (pr.x, pr.y, pr.z)
        }
        None => (t.x, t.y, t.z),
    }
}

impl NpcSpawner {
    /// A spawner from its value members and the (possibly shared) reference members.
    pub fn new(init: NpcSpawnerInit, shared: Arc<NpcSpawnerShared>) -> Self {
        Self {
            id: init.id,
            spawner_id: init.spawner_id,
            unit_id: init.unit_id,
            follow_path: init.follow_path,
            follow_npc: init.follow_npc,
            despawn_time: init.despawn_time,
            special_link: init.special_link,
            count: init.count,
            npc_spawner_ids: init.npc_spawner_ids,
            shared,
            state: Mutex::new(State {
                parent_world: init.parent_world,
                template: init.template,
                spawnable_npcs: init.spawnable_npcs,
                respawn_time: init.respawn_time,
                scheduled_count: 0,
                is_spawn_scheduled: false,
                is_despawn_scheduled: false,
                respawn_denied: false,
            }),
        }
    }

    fn st(&self) -> MutexGuard<'_, State> {
        self.state.lock().expect("npc spawner poisoned")
    }

    fn spawned_map(&self) -> MutexGuard<'_, BTreeMap<u32, Vec<NpcRef>>> {
        self.shared.spawned.lock().expect("npc spawner poisoned")
    }

    // ----- accessors -----

    pub fn shared(&self) -> &Arc<NpcSpawnerShared> {
        &self.shared
    }

    pub fn template(&self) -> Option<Arc<NpcSpawnerTemplate>> {
        self.st().template.clone()
    }

    pub fn set_template(&self, t: Option<Arc<NpcSpawnerTemplate>>) {
        self.st().template = t;
    }

    pub fn position(&self) -> WorldSpawnPosition {
        *self.shared.position.lock().expect("npc spawner poisoned")
    }

    /// Writes `Position` (shared by the clones of the entry; `SpawnEffect` moves it).
    pub fn set_position(&self, p: WorldSpawnPosition) {
        *self.shared.position.lock().expect("npc spawner poisoned") = p;
    }

    pub fn respawn_time(&self) -> i32 {
        self.st().respawn_time
    }

    pub fn set_respawn_time(&self, v: i32) {
        self.st().respawn_time = v;
    }

    pub fn parent_world(&self) -> Option<u32> {
        self.st().parent_world
    }

    pub fn set_parent_world(&self, w: Option<u32>) {
        self.st().parent_world = w;
    }

    pub fn spawnable_npcs(&self) -> Vec<NpcSpawnerNpc> {
        self.st().spawnable_npcs.clone()
    }

    pub fn is_spawn_scheduled(&self) -> bool {
        self.st().is_spawn_scheduled
    }

    pub fn is_despawn_scheduled(&self) -> bool {
        self.st().is_despawn_scheduled
    }

    pub fn scheduled_count(&self) -> i32 {
        self.st().scheduled_count
    }

    pub fn respawn_denied(&self) -> bool {
        self.st().respawn_denied
    }

    /// `SpawnedNpcs` (a copy).
    pub fn spawned_npcs(&self) -> BTreeMap<u32, Vec<NpcRef>> {
        self.spawned_map().clone()
    }

    /// `SpawnedNpcs.Clear()`.
    /// `SpawnedNpcs.TryAdd(key, list)`: `false` (unchanged) when `key` is already present.
    pub fn try_add_spawned(&self, key: u32, npcs: Vec<NpcRef>) -> bool {
        match self.spawned_map().entry(key) {
            std::collections::btree_map::Entry::Occupied(_) => false,
            std::collections::btree_map::Entry::Vacant(v) => {
                v.insert(npcs);
                true
            }
        }
    }

    pub fn clear_spawned_npcs(&self) {
        self.spawned_map().clear();
    }

    /// `CurrentSpawnCount`.
    pub fn current_spawn_count(&self) -> i32 {
        self.spawned_map()
            .get(&self.spawner_id)
            .map_or(0, |l| l.len() as i32)
    }

    fn spawned_of(&self, key: u32) -> Option<Vec<NpcRef>> {
        self.spawned_map().get(&key).cloned()
    }

    /// The `Spawner.Template` of an NPC's spawner (this spawner when unknown).
    fn npc_spawner(&self, server: &GameServer, npc: &NpcRef) -> Option<Arc<NpcSpawner>> {
        server.systems.npcs.spawns.npc_spawner_of(npc.obj_id)
    }

    /// `InitializeSpawnableNpcs(template)`.
    pub fn initialize_spawnable_npcs(&self, template: Option<&NpcSpawnerTemplate>) {
        match template {
            Some(t) => self.st().spawnable_npcs = t.npcs.clone(),
            None => tracing::warn!(
                "Template or template.Npcs is null. SpawnableNpcs will not be initialized."
            ),
        }
    }

    /// The member update of `SpawnManager.GetNpcSpawner(spawnerId)` on an event spawner:
    /// `Template.Npcs[^1].MemberId = UnitId; .UnitId = UnitId; .MemberType = "Npc"`.
    pub fn retarget_last_template_npc(&self) {
        let mut st = self.st();
        if let Some(t) = st.template.as_mut() {
            let t = Arc::make_mut(t);
            if let Some(n) = t.npcs.last_mut() {
                n.member_id = self.unit_id;
                n.unit_id = self.unit_id;
                n.member_type = "Npc".into();
            }
        }
    }

    // ----- Update -----

    /// `Update()`: despawn (schedule / no player around) has priority over spawning.
    pub fn update(self: &Arc<Self>, server: &GameServer) {
        let _lock = server.systems.npcs.spawns.spawn_lock();
        let mut did_action = false;
        if self.can_despawn_npcs(server) {
            self.despawn_npcs(server);
            did_action = true;
        } else if !self.is_player_in_spawn_radius(server) && self.current_spawn_count() > 0 {
            self.despawn_npcs_now(server);
            did_action = true;
        }
        if !did_action && self.can_spawn_npcs(server) {
            self.do_spawn(server);
        }
    }

    /// `CanDespawnNpcs()`.
    pub fn can_despawn_npcs(&self, server: &GameServer) -> bool {
        self.is_despawning_schedule_enabled(server, self.spawner_id)
    }

    fn despawn_npcs(self: &Arc<Self>, server: &GameServer) {
        if self.is_despawn_scheduled() {
            tracing::debug!(
                "[Despawn] is already at the stage of removal for SpawnerId={}.",
                self.spawner_id
            );
            return;
        }
        if let Some(npcs) = self.spawned_of(self.spawner_id) {
            self.do_despawns(server, &npcs);
        }
    }

    /// `DespawnNpcsNow()`.
    pub fn despawn_npcs_now(self: &Arc<Self>, server: &GameServer) {
        if self.is_despawn_scheduled() {
            tracing::debug!(
                "[Despawn] is already at the stage of removal for SpawnerId={}.",
                self.spawner_id
            );
            return;
        }
        if let Some(npcs) = self.spawned_of(self.spawner_id) {
            self.do_despawns_now(server, &npcs);
        }
    }

    fn can_spawn_npcs(&self, server: &GameServer) -> bool {
        if !self.can_spawn(server) {
            return false;
        }
        // IsSpawnDelayNotElapsed(): `_lastSpawnTime` is a readonly DateTime.MinValue in the C#,
        // so the spawn delay never blocks.
        true
    }

    fn can_spawn(&self, server: &GameServer) -> bool {
        if self.template().is_none() {
            tracing::warn!(
                "[Spawn [SpawnerId={}, UnitId={}] Template is null. Cannot determine if NPC can be spawned.",
                self.spawner_id,
                self.unit_id
            );
            return false;
        }
        if self.has_corpse() {
            return false;
        }
        if self.is_despawn_scheduled() {
            return false;
        }
        if self.is_spawn_scheduled() {
            return false;
        }
        if !self.is_optimal_spawner(server) {
            return false;
        }
        if !self.is_spawning_schedule_enabled(server) {
            return false;
        }
        self.check_spawn_count_can_spawn(server)
    }

    /// `IsOptimalSpawner()`.
    pub fn is_optimal_spawner(&self, server: &GameServer) -> bool {
        self.select_spawner_id(server) == Some(self.spawner_id)
    }

    /// `SelectSpawnerId()`.
    fn select_spawner_id(&self, server: &GameServer) -> Option<u32> {
        if self.npc_spawner_ids.len() == 1 {
            return Some(self.spawner_id);
        }
        let category = self
            .template()
            .map_or(npc_spawner_category::NORMAL, |t| t.npc_spawner_category_id);
        if category == npc_spawner_category::AUTOCREATED && !self.has_scheduled_spawner(server) {
            return Some(self.spawner_id);
        }
        if self.is_there_spawning_schedule(server) {
            return Some(self.spawner_id);
        }
        None
    }

    /// `IsThereSpawningSchedule()`.
    pub fn is_there_spawning_schedule(&self, server: &GameServer) -> bool {
        match period_status_npc(server, self.spawner_id) {
            PeriodStatus::NotFound => {}
            PeriodStatus::InProgress | PeriodStatus::NotStarted | PeriodStatus::Ended => {
                return true
            }
        }
        self.has_spawning_time()
    }

    fn has_spawning_time(&self) -> bool {
        self.template()
            .is_some_and(|t| t.start_time > 0.0 || t.end_time > 0.0)
    }

    /// `HasScheduledSpawner()`.
    fn has_scheduled_spawner(&self, server: &GameServer) -> bool {
        if self.npc_spawner_ids.is_empty() {
            return true;
        }
        let data = &server.game_data.systems.npcs.npc_spawners;
        let mut result = false;
        for &spawner_id in &self.npc_spawner_ids {
            if spawner_id == 0 {
                continue;
            }
            let Some(t) = data.get_npc_spawner_template(spawner_id) else {
                continue;
            };
            let has_window = t.start_time > 0.0 && t.end_time > 0.0;
            // C#: the schedule check uses this spawner's own Template.Id for every entry.
            if has_window || self.check_game_schedule_status(server) {
                if self.spawner_id != spawner_id {
                    result = true;
                } else {
                    return true;
                }
            }
        }
        result
    }

    /// `CheckGameScheduleStatus()`.
    fn check_game_schedule_status(&self, server: &GameServer) -> bool {
        let Some(t) = self.template() else {
            return false;
        };
        period_status_npc(server, t.id) != PeriodStatus::NotFound
    }

    /// `CheckSpawnCountCanSpawn()`.
    fn check_spawn_count_can_spawn(&self, server: &GameServer) -> bool {
        let Some(t) = self.template() else {
            return false;
        };
        let mut min_population = i64::from(t.min_population);
        let mut max_population = i64::from(t.max_population);
        if min_population == 0 {
            min_population = 1;
        }
        let mut player_count = i64::from(self.get_number_of_player_in_spawn_radius(server, &t));
        if player_count == 0 {
            player_count = 1;
        }
        if player_count < min_population {
            max_population = i64::from(player_count as u32);
        }
        if player_count >= min_population && player_count <= max_population {
            max_population = i64::from(player_count as u32);
        }
        let current = i64::from(self.current_spawn_count());
        if t.suspend_spawn_count > 0
            && current + i64::from(self.are_other_npcs_in_spawn_zone().1)
                >= i64::from(t.suspend_spawn_count)
        {
            return false;
        }
        if current + i64::from(self.are_other_npcs_in_spawn_zone().1) >= max_population {
            return false;
        }
        true
    }

    fn has_corpse(&self) -> bool {
        self.spawned_of(self.spawner_id)
            .is_some_and(|npcs| npcs.iter().any(|n| n.data().unit.is_dead()))
    }

    /// `IsPlayerInSpawnRadius()` (cached for 10 s per `SpawnerId`): any player of the server
    /// within `TestRadiusPc` (or `TestRadiusNpc`) * 50 of the spawn position (3D).
    pub fn is_player_in_spawn_radius(&self, server: &GameServer) -> bool {
        let Some(t) = self.template() else {
            return false;
        };
        let r = if t.test_radius_pc == 0.0 {
            t.test_radius_npc
        } else {
            t.test_radius_pc
        };
        let test_radius = r * 50.0 * r * 50.0;
        let now = spawn_manager::now();
        {
            let cache = self
                .shared
                .player_in_radius_cache
                .lock()
                .expect("npc spawner poisoned");
            if let Some(&(v, at)) = cache.get(&self.spawner_id) {
                if now.saturating_duration_since(at).as_secs_f64() < CACHE_SECONDS {
                    return v;
                }
            }
        }
        let pos = self.position();
        let mut found = false;
        for p in server.world.all_characters() {
            let (x, y, z) = player_world_pos(server, &p);
            let (dx, dy, dz) = (x - pos.x, y - pos.y, z - pos.z);
            if dx * dx + dy * dy + dz * dz <= test_radius {
                found = true;
                break;
            }
        }
        self.shared
            .player_in_radius_cache
            .lock()
            .expect("npc spawner poisoned")
            .insert(self.spawner_id, (found, now));
        found
    }

    /// `GetNumberOfPlayerInSpawnRadius(template)` (cached for 10 s): players within
    /// `TestRadiusNpc * 50` (2D, `GetAround<Character>`) of the first NPC of the first
    /// `SpawnedNpcs` list (lowest key here; the C# takes the `ConcurrentDictionary` order).
    fn get_number_of_player_in_spawn_radius(
        &self,
        server: &GameServer,
        template: &NpcSpawnerTemplate,
    ) -> i32 {
        let now = spawn_manager::now();
        {
            let cache = self
                .shared
                .player_count_cache
                .lock()
                .expect("npc spawner poisoned");
            if let Some(&(v, at)) = cache.get(&self.spawner_id) {
                if now.saturating_duration_since(at).as_secs_f64() < CACHE_SECONDS {
                    return v;
                }
            }
        }
        if template.test_radius_npc <= 0.0 {
            return 0;
        }
        let mut player_count = 0;
        let first = self
            .spawned_map()
            .values()
            .next()
            .and_then(|l| l.first().cloned());
        if let Some(npc) = first {
            let tmp = count_players_around(server, &npc, template.test_radius_npc * 50.0);
            if player_count < tmp {
                player_count = tmp;
            }
        }
        self.shared
            .player_count_cache
            .lock()
            .expect("npc spawner poisoned")
            .insert(self.spawner_id, (player_count, now));
        player_count
    }

    /// `AreOtherNpcsInSpawnZone()` (cached for 10 s). Mirrors the C# bug: every other non-empty
    /// spawner adds *this* spawner's `CurrentSpawnCount + _scheduledCount`.
    fn are_other_npcs_in_spawn_zone(&self) -> (bool, i32) {
        let now = spawn_manager::now();
        {
            let cache = self
                .shared
                .npcs_in_zone_cache
                .lock()
                .expect("npc spawner poisoned");
            if let Some(&(count, are, at)) = cache.get(&self.spawner_id) {
                if now.saturating_duration_since(at).as_secs_f64() < CACHE_SECONDS {
                    return (are, count);
                }
            }
        }
        let others: Vec<usize> = self
            .spawned_map()
            .iter()
            .filter(|(k, _)| **k != self.spawner_id)
            .map(|(_, l)| l.len())
            .collect();
        let mut count = 0;
        let mut are = false;
        for len in others {
            if len > 0 {
                count += self.current_spawn_count() + self.scheduled_count();
                are = true;
            }
        }
        self.shared
            .npcs_in_zone_cache
            .lock()
            .expect("npc spawner poisoned")
            .insert(self.spawner_id, (count, are, now));
        (are, count)
    }

    // ----- spawn entry points -----

    /// `SpawnAll(beginning)`.
    pub fn spawn_all(self: &Arc<Self>, server: &GameServer, _beginning: bool) {
        if self.is_spawning_schedule_enabled(server) {
            return;
        }
        self.do_spawn(server);
        // `if (IsSpawnScheduled) IsDespawningScheduleEnabled(SpawnerId);` has no side effect.
    }

    /// `Spawn(objId)`: `DoSpawn()` and the first NPC of this spawner (`None` where the C#
    /// throws `KeyNotFoundException`).
    pub fn spawn(self: &Arc<Self>, server: &GameServer, _obj_id: u32) -> Option<NpcRef> {
        self.do_spawn(server);
        self.spawned_of(self.spawner_id)
            .and_then(|l| l.first().cloned())
    }

    /// `ForceSpawn(objId)`.
    pub fn force_spawn(self: &Arc<Self>, server: &GameServer, _obj_id: u32) -> Option<NpcRef> {
        if self.spawned_map().is_empty() {
            let t = self.template();
            self.initialize_spawnable_npcs(t.as_deref());
        }
        self.do_spawn(server);
        self.spawned_of(self.spawner_id)
            .and_then(|l| l.first().cloned())
    }

    // ----- despawn -----

    /// `Despawn(npc)`: removes the NPC from `SpawnedNpcs` and deletes it.
    pub fn despawn(&self, server: &GameServer, npc: &NpcRef) {
        let _lock = server.systems.npcs.spawns.spawn_lock();
        self.remove_npc_from_spawned_list(server, npc);
        // npc.UnregisterNpcEvents(): `Npc.Delete` drops the AI with its registrations.
        spawn_manager::delete_object(server, &WorldObject::from(npc), self.parent_world());
        self.st().is_despawn_scheduled = false;
    }

    fn remove_npc_from_spawned_list(&self, server: &GameServer, npc: &NpcRef) {
        let Some(spawner) = self.npc_spawner(server, npc) else {
            tracing::warn!(
                "NPC {} has no associated Spawner.",
                npc.data().unit.template_id
            );
            return;
        };
        let id = spawner.spawner_id;
        let mut map = self.spawned_map();
        match map.get_mut(&id) {
            Some(list) => {
                match list.iter().position(|n| Arc::ptr_eq(n, npc)) {
                    Some(i) => {
                        list.remove(i);
                    }
                    None => tracing::warn!(
                        "NPC {} not found in SpawnedNpcs for SpawnerId={id}.",
                        npc.obj_id
                    ),
                }
                if list.is_empty() {
                    map.remove(&id);
                }
            }
            None => tracing::warn!("SpawnerId={id} not found in SpawnedNpcs."),
        }
    }

    /// `DoDespawn(npc)` (`Npc.DoDie`, `NpcSpawnerDoDespawnTask`, `SpawnManager.DeSpawnAll`):
    /// schedules the respawn (when allowed) and the despawn of the corpse.
    pub fn do_despawn(&self, server: &GameServer, npc: &NpcRef) {
        let _lock = server.systems.npcs.spawns.spawn_lock();
        let Some(t) = self.template() else {
            tracing::error!(
                "Failed to process despawn for NPC {}:{}:{}.",
                self.unit_id,
                self.spawner_id,
                npc.obj_id
            );
            return;
        };
        let obj = WorldObject::from(npc);
        let Some(manager) = spawn_manager::manager_of(server, &obj, self.parent_world()) else {
            tracing::error!(
                "Failed to process despawn for NPC {}:{}:{}.",
                self.unit_id,
                self.spawner_id,
                npc.obj_id
            );
            return;
        };
        let respawn_time = self.respawn_time();
        let now = spawn_manager::now();
        if respawn_time > 0
            && i64::from(self.are_other_npcs_in_spawn_zone().1) + i64::from(self.scheduled_count())
                < i64::from(t.max_population)
        {
            self.increment_count(true);
            let dead_ms = npc.data().dead_time_ms;
            let since_death_ms = if dead_ms == 0 {
                0
            } else {
                crate::world::now_ms() - dead_ms
            };
            let at = spawn_manager::add_secs(
                now,
                f64::from(respawn_time) - since_death_ms as f64 / 1000.0,
            );
            manager.add_respawn(server, obj.clone(), Some(at));
        } else {
            self.increment_count(false);
        }
        let mut despawn_secs = f64::from(self.despawn_time);
        let has_loot = server
            .systems
            .npcs
            .hooks
            .economy
            .get()
            .is_some_and(|h| h.npc_has_loot_items(server, npc));
        if has_loot {
            despawn_secs += f64::from(LOOT_DESPAWN_EXTENSION_TIME);
        }
        manager.add_despawn(
            server,
            obj,
            Some(spawn_manager::add_secs(now, despawn_secs)),
        );
    }

    fn do_despawn_now(&self, server: &GameServer, npc: &NpcRef) {
        let _lock = server.systems.npcs.spawns.spawn_lock();
        let max = self.template().map_or(0, |t| i64::from(t.max_population));
        if i64::from(self.are_other_npcs_in_spawn_zone().1) + i64::from(self.scheduled_count())
            < max
        {
            self.increment_count(true);
            tracing::info!(
                "Immediate despawn scheduled for NPC {}:{}:{}.",
                self.unit_id,
                self.spawner_id,
                npc.obj_id
            );
        }
        let obj = WorldObject::from(npc);
        match spawn_manager::manager_of(server, &obj, self.parent_world()) {
            // The despawn time is whatever `npc.Despawn` holds (MinValue unless it died).
            Some(m) => m.add_despawn(server, obj, None),
            None => tracing::error!(
                "Failed to immediately despawn NPC {}:{}:{}.",
                self.unit_id,
                self.spawner_id,
                npc.obj_id
            ),
        }
    }

    /// `DespawnWithRespawn(npc)` (`KillNpcWithoutCorpseEffect`, `NpcDeleteTask`, simulation).
    pub fn despawn_with_respawn(&self, server: &GameServer, npc: &NpcRef) {
        let obj = WorldObject::from(npc);
        let manager = spawn_manager::manager_of(server, &obj, self.parent_world());
        spawn_manager::delete_object(server, &obj, self.parent_world());
        let respawn_time = self.respawn_time();
        let max = self.template().map_or(0, |t| i64::from(t.max_population));
        if respawn_time > 0 && i64::from(self.are_other_npcs_in_spawn_zone().1) < max {
            let at = spawn_manager::add_secs(spawn_manager::now(), f64::from(respawn_time));
            if let Some(m) = manager {
                m.add_respawn(server, obj, Some(at));
            }
            let mut st = self.st();
            st.scheduled_count += 1;
            if st.scheduled_count < 0 {
                st.scheduled_count = 0;
            }
        }
    }

    /// `DoDespawns(npcs)`: `DoDespawn` for every NPC (in combat or not).
    pub fn do_despawns(&self, server: &GameServer, npcs: &[NpcRef]) {
        let _lock = server.systems.npcs.spawns.spawn_lock();
        self.st().is_despawn_scheduled = true;
        for npc in npcs {
            self.do_despawn(server, npc);
        }
        self.st().is_despawn_scheduled = false;
    }

    /// `DoDespawnsNow(npcs)`.
    pub fn do_despawns_now(&self, server: &GameServer, npcs: &[NpcRef]) {
        let _lock = server.systems.npcs.spawns.spawn_lock();
        self.st().is_despawn_scheduled = true;
        for npc in npcs {
            self.do_despawn_now(server, npc);
            tracing::debug!(
                "Despawned NPC {}:{}.",
                npc.data().unit.template_id,
                npc.obj_id
            );
        }
        self.st().is_despawn_scheduled = false;
    }

    /// `SetSpawnScheduled(value)` (the respawn tick re-enables spawning with `false`).
    pub fn set_spawn_scheduled(&self, value: bool) {
        self.st().is_spawn_scheduled = value;
    }

    // ----- spawning -----

    /// `DoSpawn()`: spawns every `SpawnableNpcs` member.
    pub fn do_spawn(self: &Arc<Self>, server: &GameServer) {
        let Some(t) = self.template() else {
            tracing::error!(
                "[Spawn] Can't spawn npc {} from spawnerId {} - Template is null",
                self.unit_id,
                self.id
            );
            return;
        };
        tracing::debug!(
            "[Spawn] Starting spawn process for SpawnerId={}, UnitId={}, Template={}",
            self.spawner_id,
            self.unit_id,
            t.id
        );
        let current = self.current_spawn_count() as i64;
        if current >= i64::from(t.max_population) {
            return;
        }
        if t.suspend_spawn_count > 0 && current > i64::from(t.suspend_spawn_count) {
            return;
        }
        let spawnable = self.spawnable_npcs();
        if spawnable.is_empty() {
            tracing::warn!(
                "[Spawn] No spawnable NPCs available for SpawnerId={}",
                self.spawner_id
            );
            return;
        }
        let mut spawned_npcs = Vec::new();
        for nsn in &spawnable {
            let _lock = server.systems.npcs.spawns.spawn_lock();
            match spawn_member(server, nsn, self, 0) {
                Ok(Some(spawned)) if !spawned.is_empty() => {
                    for npc in &spawned {
                        let key = self
                            .npc_spawner(server, npc)
                            .map_or(self.spawner_id, |s| s.spawner_id);
                        self.add_npc_to_spawned(key, npc.clone());
                    }
                    spawned_npcs.extend(spawned);
                }
                Ok(_) => tracing::warn!(
                    "[Spawn] Failed to spawn NPC from template {}:{} for SpawnerId={}",
                    nsn.npc_spawner_template_id,
                    nsn.member_id,
                    self.spawner_id
                ),
                Err(e) => tracing::error!(
                    "[Spawn] Failed to spawn NPC from template {}:{} for SpawnerId={}: {e}",
                    nsn.npc_spawner_template_id,
                    nsn.member_id,
                    self.spawner_id
                ),
            }
        }
        if spawned_npcs.is_empty() {
            tracing::error!(
                "[Spawn] Failed to spawn any NPCs for {}:{}",
                self.unit_id,
                self.spawner_id
            );
            return;
        }
        self.decrement_count(spawned_npcs.len());
    }

    /// `IsSpawningScheduleEnabled()`: the game schedule, else the spawn time window (no window =
    /// enabled). Sets `IsSpawnScheduled` when enabled.
    pub fn is_spawning_schedule_enabled(&self, server: &GameServer) -> bool {
        if self.template().is_none() {
            tracing::warn!(
                "[Spawn] Can't spawn NPC {}:{} (index={}) — template is null.",
                self.spawner_id,
                self.unit_id,
                self.id
            );
            return false;
        }
        self.st().is_spawn_scheduled = false;
        match period_status_npc(server, self.spawner_id) {
            PeriodStatus::InProgress => {
                let mut st = self.st();
                st.respawn_denied = true;
                st.is_spawn_scheduled = true;
                return true;
            }
            PeriodStatus::NotFound => {}
            PeriodStatus::NotStarted | PeriodStatus::Ended => return false,
        }
        if self.is_within_spawn_time(server) {
            let mut st = self.st();
            st.respawn_denied = true;
            st.is_spawn_scheduled = true;
            return true;
        }
        false
    }

    /// `IsWithinSpawnTime()` (game clock; no window defined = true).
    fn is_within_spawn_time(&self, server: &GameServer) -> bool {
        let Some(t) = self.template() else {
            return true;
        };
        if t.start_time > 0.0 || t.end_time > 0.0 {
            return is_hour_between(server.time.get_time(), t.start_time, t.end_time);
        }
        true
    }

    /// `IsDespawningScheduleEnabled(spawnerId)`: true when an NPC of the spawner is outside its
    /// time window or its schedule is in progress.
    fn is_despawning_schedule_enabled(&self, server: &GameServer, spawner_id: u32) -> bool {
        let Some(npcs) = self.spawned_of(spawner_id) else {
            return false;
        };
        for npc in &npcs {
            let spawner = self.npc_spawner(server, npc);
            let template = spawner.as_ref().and_then(|s| s.template());
            if is_within_despawn_time(server, template.as_deref()) {
                return true;
            }
            if let Some(t) = template {
                if period_status_npc(server, t.id) == PeriodStatus::InProgress {
                    return true;
                }
            }
        }
        false
    }

    /// `DoEventSpawn()`: spawns the template member matching `UnitId`.
    pub fn do_event_spawn(self: &Arc<Self>, server: &GameServer) {
        let Some(t) = self.template() else {
            tracing::error!(
                "Can't spawn npc {} from spawnerId {}",
                self.unit_id,
                self.id
            );
            return;
        };
        let current = self.current_spawn_count() as i64;
        if current >= i64::from(t.max_population) {
            return;
        }
        if t.suspend_spawn_count > 0 && current > i64::from(t.suspend_spawn_count) {
            return;
        }
        let mut n = Vec::new();
        if let Some(nsn) = t.npcs.iter().find(|n| n.member_id == self.unit_id) {
            match spawn_member(server, nsn, self, 0) {
                Ok(Some(v)) => n = v,
                Ok(None) => {}
                Err(e) => tracing::error!("{e}"),
            }
        }
        for npc in &n {
            self.add_npc_to_spawned(self.spawner_id, npc.clone());
        }
        if n.is_empty() {
            tracing::error!("Can't spawn npc {} from spawnerId {}", self.unit_id, t.id);
            return;
        }
        self.decrement_count(n.len());
    }

    fn decrement_count(&self, n: usize) {
        let mut st = self.st();
        if st.scheduled_count > 0 {
            st.scheduled_count -= n as i32;
        }
    }

    fn increment_count(&self, respawn: bool) {
        if respawn {
            let mut st = self.st();
            st.scheduled_count += 1;
            if st.scheduled_count < 0 {
                st.scheduled_count = 0;
            }
        }
    }

    /// `DoRandomSpawn(spawnerId, ownerId)`: one weighted-random member of a spawner template.
    /// Mirrors the C#: an extra NPC is created through `NpcManager.Create` and dropped (its
    /// object id leaks), and the spawned NPC is `Spawn()`ed a second time.
    pub fn do_random_spawn(
        self: &Arc<Self>,
        server: &GameServer,
        spawner_id: u32,
        owner_id: u32,
    ) -> Option<NpcRef> {
        let template = server
            .game_data
            .systems
            .npcs
            .npc_spawners
            .get_npc_spawner_template(spawner_id);
        let Some(template) = template.filter(|t| !t.npcs.is_empty()) else {
            tracing::warn!("No NPC templates available for spawner {spawner_id}.");
            return None;
        };
        let Some(nsn) = random_element_by_weight(&template.npcs, |n| n.weight) else {
            tracing::warn!(
                "Random template returned null on the NPC selection for spawner {spawner_id}."
            );
            return None;
        };
        if backend::npc_create(server, 0, nsn.member_id).is_none() {
            tracing::warn!(
                "Failed to create NPC from template {}:{}",
                nsn.npc_spawner_template_id,
                nsn.member_id
            );
            return None;
        }
        let spawned = match spawn_member(server, nsn, self, owner_id) {
            Ok(Some(v)) if !v.is_empty() => v,
            Ok(_) => {
                tracing::warn!(
                    "No NPCs spawned from template {}:{}",
                    nsn.npc_spawner_template_id,
                    nsn.member_id
                );
                return None;
            }
            Err(e) => {
                tracing::error!(
                    "Failed to spawn NPC from template {}:{}: {e}",
                    nsn.npc_spawner_template_id,
                    nsn.member_id
                );
                return None;
            }
        };
        let npc = spawned[0].clone();
        {
            let _lock = server.systems.npcs.spawns.spawn_lock();
            let key = self
                .npc_spawner(server, &npc)
                .map_or(self.spawner_id, |s| s.spawner_id);
            self.add_npc_to_spawned(key, npc.clone());
        }
        if let Some(world) = self.parent_world().and_then(|id| server.world.world(id)) {
            spawn_into_world(server, &world, &npc);
        }
        Some(npc)
    }

    /// `DoSpawnEffect(spawnerId, effect, caster, target)` (`SpawnEffect`).
    pub fn do_spawn_effect(
        self: &Arc<Self>,
        server: &GameServer,
        spawner_id: u32,
        effect: &SpawnEffectParams,
        caster: Option<&WorldObject>,
        target: Option<&WorldObject>,
    ) {
        let Some(template) = server
            .game_data
            .systems
            .npcs
            .npc_spawners
            .get_npc_spawner_template(spawner_id)
        else {
            return;
        };
        let mut n = Vec::new();
        if let Some(nsn) = template.npcs.iter().find(|n| n.member_id == self.unit_id) {
            match spawn_member(server, nsn, self, 0) {
                Ok(Some(v)) => n = v,
                Ok(None) => return,
                Err(e) => tracing::error!("{e}"),
            }
        }
        for npc in &n {
            if let Some(s) = self.npc_spawner(server, npc) {
                s.set_respawn_time(0);
            }
            if effect.use_summoner_faction {
                let source = match target {
                    Some(t) if t.as_npc().is_some() => Some(t),
                    _ => caster,
                };
                if let Some(faction) = source.map(|o| o.with_base_unit(|b| b.faction_id())) {
                    npc.data().unit.faction_id = faction;
                }
            }
            if effect.use_summoner_aggro_target && !effect.use_summoner_faction {
                // npc.Ai.Owner.AddUnitAggro(Damage, target-or-caster, 1) +
                // Ai.OnAggroTargetChanged() (systems::ai)
                let unit = match target {
                    Some(t) if t.as_npc().is_some() => Some(t),
                    _ => caster,
                };
                if let (Some(ai), Some(unit)) = (server.systems.hooks.ai.get(), unit) {
                    ai.npc_engage(server, npc, unit);
                }
            }
            if effect.life_time > 0.0 {
                schedule_npc_do_despawn(server, npc, effect.life_time);
            }
        }
        if n.is_empty() {
            tracing::error!(
                "Can't spawn npc {} from spawner {}",
                self.unit_id,
                template.id
            );
            return;
        }
        for npc in &n {
            self.add_npc_to_spawned(self.spawner_id, npc.clone());
        }
        let mut st = self.st();
        if st.scheduled_count > 0 {
            st.scheduled_count -= n.len() as i32;
        }
    }

    fn add_npc_to_spawned(&self, key: u32, npc: NpcRef) {
        self.spawned_map().entry(key).or_default().push(npc);
    }
}

/// `IsWithinDespawnTime(npc)` on the NPC's spawner template: outside its time window.
fn is_within_despawn_time(server: &GameServer, template: Option<&NpcSpawnerTemplate>) -> bool {
    let Some(t) = template else {
        return false;
    };
    if t.start_time <= 0.0 && t.end_time <= 0.0 {
        return false;
    }
    !is_hour_between(server.time.get_time(), t.start_time, t.end_time)
}

/// Players within `radius` (2D, strict) of `npc` in its region neighbourhood
/// (`WorldManager.GetAround<Character>(npc, radius).Count`).
fn count_players_around(server: &GameServer, npc: &NpcRef, radius: f32) -> i32 {
    if radius <= 0.0 {
        return 0;
    }
    let obj = WorldObject::from(npc);
    let Some(world) = server.world.world_of(&obj) else {
        return 0;
    };
    if obj.region().is_none() {
        return 0;
    }
    let center = world.world_pos_rot(&obj);
    let sq = radius * radius;
    let players = world.players_around(obj);
    players
        .iter()
        .filter(|p| {
            let (x, y, _) = player_world_pos(server, p);
            let dx = (x - center.x) * (x - center.x);
            if dx > sq {
                return false;
            }
            let dy = (y - center.y) * (y - center.y);
            dx + dy < sq
        })
        .count() as i32
}

/// `GameObject.Spawn()` of an NPC + the zone-change dispatch.
pub fn spawn_into_world(server: &GameServer, world: &Arc<WorldInstance>, npc: &NpcRef) {
    let changes = world.spawn(npc, &server.view_ctx());
    crate::world::session::handle_zone_changes(server, changes);
}

/// `TaskManager.Schedule(new NpcSpawnerDoDespawnTask(npc), lifeTime)`:
/// `npc?.Spawner?.DoDespawn(npc)` after `secs` seconds.
pub(crate) fn schedule_npc_do_despawn(server: &GameServer, npc: &NpcRef, secs: f32) {
    let (Some(server), Ok(rt)) = (server.self_arc(), tokio::runtime::Handle::try_current()) else {
        tracing::warn!("NpcSpawnerDoDespawnTask: no runtime / server handle");
        return;
    };
    let npc = npc.clone();
    rt.spawn(async move {
        tokio::time::sleep(std::time::Duration::from_secs_f32(secs.max(0.0))).await;
        if let Some(s) = server.systems.npcs.spawns.npc_spawner_of(npc.obj_id) {
            s.do_despawn(&server, &npc);
        }
    });
}

/// `ParentWorld.Template.GeoData.GetHeight(position)` (`AiGeoDataManager.GetHeight`): the
/// navmesh (`.bai`, GeoDataMode) is not ported, so this is its fallback
/// `GetRawHeightMapHeight((int)Round(x), (int)Round(y))`; exceptions give 0.
pub fn geo_data_height(server: &GameServer, world: &WorldInstance, x: f32, y: f32) -> f32 {
    let Some(h) = server.systems.npcs.heightmaps.get() else {
        return 0.0;
    };
    let Some(t) = h.template(world.template.id) else {
        return 0.0;
    };
    t.get_raw_height_map_height(
        h.files(),
        x.round_ties_even() as i32,
        y.round_ties_even() as i32,
    )
    .unwrap_or(0.0)
}

/// `NpcSpawnerNpc.Spawn(npcSpawner, ownerId)`: `Err` = the C# `InvalidOperationException` for
/// an unsupported `MemberType`; `Ok(None)` = the C# `null` (template missing).
pub fn spawn_member(
    server: &GameServer,
    nsn: &NpcSpawnerNpc,
    spawner: &Arc<NpcSpawner>,
    owner_id: u32,
) -> Result<Option<Vec<NpcRef>>, String> {
    match nsn.member_type.as_str() {
        // SpawnNpcGroup is SpawnNpc.
        "Npc" | "NpcGroup" => spawn_npc(server, nsn, spawner, owner_id),
        _ => Err(format!(
            "Tried spawning an unsupported line from NpcSpawnerNpc - Id: {}",
            nsn.id
        )),
    }
}

/// `NpcSpawnerNpc.SpawnNpc`.
fn spawn_npc(
    server: &GameServer,
    nsn: &NpcSpawnerNpc,
    spawner: &Arc<NpcSpawner>,
    owner_id: u32,
) -> Result<Option<Vec<NpcRef>>, String> {
    let Some(npc) = backend::npc_create(server, 0, nsn.member_id) else {
        tracing::warn!(
            "Npc {}, from spawner Id {} not exist at db. Spawner Position: {:?}",
            nsn.member_id,
            spawner.id,
            spawner.position()
        );
        return Ok(None);
    };
    let Some(world) = spawner.parent_world().and_then(|id| server.world.world(id)) else {
        // C#: npcSpawner.ParentWorld.Template (NullReferenceException).
        return Err(format!("spawner {} has no parent world", spawner.id));
    };
    spawn_manager::forget_object_fields(server, npc.obj_id);
    // npc.RegisterNpcEvents(): `ai::hooks::spawn_positioned` (before GoToSpawn).
    let can_fly = npc.data().can_fly;
    if !can_fly {
        let mut p = spawner.position();
        // npc_spawn_on_terrain: the physics ground (collision service) under the spawn point
        let new_z = crate::systems::ai::collision::ground_height_or(
            server,
            world.id,
            p.x,
            p.y,
            p.z,
            || geo_data_height(server, &world, p.x, p.y),
        );
        if (p.z - new_z).abs() < 1.0 {
            p.z = new_z;
            spawner.set_position(p);
        }
    }
    let pos = spawner.position();
    {
        let mut d = npc.data();
        d.unit.owner_id = owner_id;
        // Transform.ApplyWorldSpawnPosition(Position)
        d.unit.transform.zone_id = pos.zone_id;
        d.unit
            .transform
            .set_position(pos.x, pos.y, pos.z, pos.roll, pos.pitch, pos.yaw);
        d.unit.transform.instance_id = world.id;
        d.spawner_id = spawner.id;
    }
    tracing::trace!(
        "Spawn npc templateId {} objId {} from spawnerId {} at Position: {:?}",
        nsn.member_id,
        npc.obj_id,
        nsn.npc_spawner_template_id,
        pos
    );
    server
        .systems
        .npcs
        .spawns
        .set_npc_spawner(npc.obj_id, spawner.clone());
    // Ai.HomePosition / IdlePosition = world position, Ai.GoToSpawn() (systems::ai)
    if let Some(ai) = server.systems.hooks.ai.get() {
        ai.npc_spawn_positioned(server, &npc);
    }
    if let Some(t) = spawner.template() {
        spawner.set_respawn_time(random_float(t.spawn_delay_min, t.spawn_delay_max) as i32);
    }
    spawn_into_world(server, &world, &npc);
    // world.Events.OnUnitSpawn (dungeon events, systems::instances).
    crate::systems::instances::events::on_unit_spawn(server, &world, &npc);
    // RegisterNpcEvents, `npc.Simulation = new Simulation(npc)` and
    // `Ai.LoadAiPathPoints(FollowPath)` (systems::ai).
    if let Some(ai) = server.systems.hooks.ai.get() {
        ai.npc_spawned(server, &npc, &spawner.follow_path);
    }
    Ok(Some(vec![npc]))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn time_between_inclusive_and_wrapping() {
        // Normal window.
        assert!(is_hour_between(10.0, 8.0, 20.0));
        assert!(is_hour_between(8.0, 8.0, 20.0));
        assert!(is_hour_between(20.0, 8.0, 20.0));
        assert!(!is_hour_between(7.99, 8.0, 20.0));
        assert!(!is_hour_between(20.01, 8.0, 20.0));
        // Wrapping over midnight.
        assert!(is_hour_between(23.0, 22.0, 4.0));
        assert!(is_hour_between(1.0, 22.0, 4.0));
        assert!(is_hour_between(4.0, 22.0, 4.0));
        assert!(!is_hour_between(12.0, 22.0, 4.0));
        // Equal start / end: only that instant.
        assert!(is_hour_between(5.0, 5.0, 5.0));
        assert!(!is_hour_between(5.5, 5.0, 5.0));
        assert_eq!(ticks_from_hours(1.0), 36_000_000_000);
        assert_eq!(ticks_from_hours(0.5), 18_000_000_000);
    }

    #[test]
    fn random_helpers() {
        for _ in 0..200 {
            let v = random_float(10.0, 20.0);
            assert!((10.0..20.0).contains(&v));
            assert_eq!(random_float(7.0, 7.0), 7.0);
        }
        let items = [(1u32, 0.0f32), (2, 1.0), (3, 0.0)];
        for _ in 0..50 {
            assert_eq!(random_element_by_weight(&items, |i| i.1).unwrap().0, 2);
        }
        let empty: [(u32, f32); 0] = [];
        assert!(random_element_by_weight(&empty, |i| i.1).is_none());
    }
}
