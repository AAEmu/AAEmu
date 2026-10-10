//! `DoodadSpawner` (`Models/Game/DoodadObj/DoodadSpawner.cs`) and the JSON shape of every
//! spawner kind read from `Data/Worlds/<world>/*_spawns*.json` (`Spawner<T>`,
//! `WorldSpawnPosition`, `NpcSpawner`, `DoodadSpawner`, `TransferSpawner`, `SlaveSpawner`) with
//! Newtonsoft semantics (`systems::npcs::json`); `GimmickSpawner` is `gimmick::spawner`.
//!
//! Runtime links of the C# object graph:
//! * `doodad.Spawner` = `server.systems.npcs.spawns.doodad_spawner_of(obj_id)` (+ the
//!   `Doodad::spawner_id` copy of `Spawner.Id`);
//! * `GameObject.Respawn` / `Despawn` of a doodad live in `SpawnManagers` (see `spawn_manager`).
//!
//! Locking: the spawner state mutex is a leaf lock, never held while calling into the doodad
//! manager / world (a doodad's phase funcs may call back into `do_despawn` synchronously).

use std::sync::{Arc, Mutex, MutexGuard};

use serde_json::{Map, Value};

use crate::data::world_templates::WorldSpawnPosition;
use crate::server::GameServer;
use crate::systems::npcs::json;
use crate::systems::npcs::schedule::{CronSchedule, PeriodStatus};
use crate::systems::npcs::spawn_manager::{self, backend};
use crate::world::{DoodadRef, WorldObject};

// ---------------------------------------------------------------------------------------------
// JSON (Newtonsoft) shapes
// ---------------------------------------------------------------------------------------------

/// `WorldSpawnPosition` from JSON (`WorldId` is `1` when absent: `[DefaultValue(1)]` +
/// `IgnoreAndPopulate`; every other member defaults to 0).
pub fn parse_position(obj: &Map<String, Value>) -> Result<WorldSpawnPosition, String> {
    Ok(WorldSpawnPosition {
        world_id: json::u32_or(obj, "WorldId", 1)?,
        zone_id: json::u32_or(obj, "ZoneId", 0)?,
        x: json::f32_or(obj, "X", 0.0)?,
        y: json::f32_or(obj, "Y", 0.0)?,
        z: json::f32_or(obj, "Z", 0.0)?,
        yaw: json::f32_or(obj, "Yaw", 0.0)?,
        pitch: json::f32_or(obj, "Pitch", 0.0)?,
        roll: json::f32_or(obj, "Roll", 0.0)?,
    })
}

/// The `Spawner<T>` members.
#[derive(Debug, Clone, PartialEq)]
pub struct SpawnerBase {
    /// `Id` (index; overwritten by `SpawnManager`).
    pub id: u32,
    /// `SpawnerId` (spawner template id).
    pub spawner_id: u32,
    /// `UnitId` (template id of the spawned object).
    pub unit_id: u32,
    pub follow_path: String,
    pub follow_npc: u32,
    /// `Position` (a missing / null `Position` is a load error: the C# dereferences it).
    pub position: WorldSpawnPosition,
    /// `RespawnTime` (15).
    pub respawn_time: i32,
    /// `DespawnTime` (20).
    pub despawn_time: i32,
    pub special_link: String,
}

impl Default for SpawnerBase {
    fn default() -> Self {
        Self {
            id: 0,
            spawner_id: 0,
            unit_id: 0,
            follow_path: String::new(),
            follow_npc: 0,
            position: WorldSpawnPosition {
                world_id: 1,
                ..WorldSpawnPosition::default()
            },
            respawn_time: 15,
            despawn_time: 20,
            special_link: String::new(),
        }
    }
}

/// Parses the `Spawner<T>` members of a spawn file entry (`Title` and unknown members are
/// ignored like Newtonsoft does).
pub fn parse_spawner_base(obj: &Map<String, Value>) -> Result<SpawnerBase, String> {
    let position = match json::prop(obj, "Position") {
        Some(Value::Object(p)) => parse_position(p)?,
        None | Some(Value::Null) => return Err("Position is null".into()),
        Some(o) => return Err(format!("Position: expected an object, got {o}")),
    };
    Ok(SpawnerBase {
        id: json::u32_or(obj, "Id", 0)?,
        spawner_id: json::u32_or(obj, "SpawnerId", 0)?,
        unit_id: json::u32_or(obj, "UnitId", 0)?,
        follow_path: json::str_or(obj, "FollowPath", "")?,
        follow_npc: json::u32_or(obj, "FollowNpc", 0)?,
        position,
        respawn_time: json::i32_or(obj, "RespawnTime", 15)?,
        despawn_time: json::i32_or(obj, "DespawnTime", 20)?,
        special_link: json::str_or(obj, "SpecialLink", "")?,
    })
}

/// A `NpcSpawner` entry of `npc_spawns*.json`.
#[derive(Debug, Clone, PartialEq)]
pub struct NpcSpawnerDef {
    pub base: SpawnerBase,
    /// `Count` (`[DefaultValue(1f)]`).
    pub count: u32,
    /// `NpcSpawnerIds` (event spawners list their spawner template ids here).
    pub npc_spawner_ids: Vec<u32>,
}

impl NpcSpawnerDef {
    pub fn from_json(obj: &Map<String, Value>) -> Result<Self, String> {
        Ok(Self {
            base: parse_spawner_base(obj)?,
            count: json::u32_or(obj, "Count", 1)?,
            npc_spawner_ids: json::u32_list(obj, "NpcSpawnerIds")?,
        })
    }
}

/// A `DoodadSpawner` entry of `doodad_spawns*.json`.
#[derive(Debug, Clone, PartialEq)]
pub struct DoodadSpawnerDef {
    pub base: SpawnerBase,
    /// `Scale` (the constructor sets 1).
    pub scale: f32,
    /// `Count` (1).
    pub count: u32,
    /// `RelatedIds` (`null` when absent).
    pub related_ids: Option<Vec<u32>>,
    pub respawn_doodad_template_id: u32,
}

impl DoodadSpawnerDef {
    pub fn from_json(obj: &Map<String, Value>) -> Result<Self, String> {
        let related_ids = match json::prop(obj, "RelatedIds") {
            None | Some(Value::Null) => None,
            Some(_) => Some(json::u32_list(obj, "RelatedIds")?),
        };
        Ok(Self {
            base: parse_spawner_base(obj)?,
            scale: json::f32_or(obj, "Scale", 1.0)?,
            count: json::u32_or(obj, "Count", 1)?,
            related_ids,
            respawn_doodad_template_id: json::u32_or(obj, "RespawnDoodadTemplateId", 0)?,
        })
    }
}

/// A `TransferSpawner` / `SlaveSpawner` entry (`Count` defaults to 1). Spawning transfers and
/// slaves is not ported: the spawners are loaded and counted only.
#[derive(Debug, Clone, PartialEq)]
pub struct SimpleSpawnerDef {
    pub base: SpawnerBase,
    pub count: u32,
}

impl SimpleSpawnerDef {
    pub fn from_json(obj: &Map<String, Value>) -> Result<Self, String> {
        Ok(Self {
            base: parse_spawner_base(obj)?,
            count: json::u32_or(obj, "Count", 1)?,
        })
    }
}

/// `JsonHelper.TryDeserializeObject<List<T>>`: the entries of a spawn file (`Err` = the C#
/// parse failure that aborts `SpawnManager.Load`).
pub fn parse_list<T>(
    text: &str,
    f: impl Fn(&Map<String, Value>) -> Result<T, String>,
) -> Result<Vec<T>, String> {
    let v = json::parse(text).map_err(|e| e.to_string())?;
    if v.is_null() {
        return Err("document is null".into());
    }
    json::array(&v)?
        .iter()
        .enumerate()
        .map(|(i, e)| match e {
            Value::Object(o) => f(o).map_err(|err| format!("entry {}: {err}", i + 1)),
            // A null list element deserializes to a null spawner, which the load loop
            // dereferences.
            o => Err(format!("entry {}: expected an object, got {o}", i + 1)),
        })
        .collect()
}

// ---------------------------------------------------------------------------------------------
// DoodadSpawner
// ---------------------------------------------------------------------------------------------

#[derive(Debug)]
struct State {
    /// `ParentWorld` (instance id).
    parent_world: Option<u32>,
    respawn_time: i32,
    scale: f32,
    /// `Last` (`new Doodad()` placeholder = `None`).
    last: Option<DoodadRef>,
    /// `_spawned`.
    spawned: Vec<DoodadRef>,
    scheduled_count: i32,
    spawn_count: i32,
    count: u32,
    /// `Permanent` (false when the doodad is on a schedule).
    permanent: bool,
    respawn_doodad_template_id: u32,
    /// `NotFoundInScheduler`.
    not_found_in_scheduler: bool,
}

/// `DoodadSpawner`.
#[derive(Debug)]
pub struct DoodadSpawner {
    pub id: u32,
    pub spawner_id: u32,
    pub unit_id: u32,
    pub follow_path: String,
    pub follow_npc: u32,
    pub position: WorldSpawnPosition,
    pub despawn_time: i32,
    pub special_link: String,
    /// `RelatedIds` (for `DoodadFuncPulse`).
    pub related_ids: Option<Vec<u32>>,
    state: Mutex<State>,
}

/// `GameScheduleManager.GetDoodadCronRemainingTime(id, start)`.
fn doodad_cron_remaining_time(server: &GameServer, template_id: u32, start: bool) -> String {
    server
        .game_data
        .systems
        .npcs
        .schedule
        .doodad_cron_remaining_time(template_id as i32, start)
}

/// `TaskManager.CronSchedule(task, cron)` (repeat count -1): runs `task` at every occurrence of
/// the expression (UTC wall clock) for as long as the server lives. `Err` = the
/// `CrontabException` the C# callers catch.
fn cron_schedule(
    server: &GameServer,
    expr: &str,
    task: impl Fn(&GameServer) + Send + Sync + 'static,
) -> Result<(), String> {
    let cron = CronSchedule::parse(expr).map_err(|e| format!("{e:?}"))?;
    let (Some(server), Ok(rt)) = (server.self_arc(), tokio::runtime::Handle::try_current()) else {
        return Err("no runtime / server handle".into());
    };
    let weak = Arc::downgrade(&server);
    drop(server);
    rt.spawn(async move {
        let mut base = chrono::Utc::now().naive_utc();
        loop {
            let next = cron.next_occurrence(base);
            if next.and_utc().timestamp() >= 253_402_300_799 {
                break; // DateTime.MaxValue: never fires
            }
            let wait = (next - chrono::Utc::now().naive_utc())
                .to_std()
                .unwrap_or_default();
            tokio::time::sleep(wait).await;
            let Some(server) = weak.upgrade() else {
                break;
            };
            task(&server);
            base = chrono::Utc::now().naive_utc().max(next);
        }
    });
    Ok(())
}

/// `new DoodadSpawnerDoDespawnTask(doodad)`: `doodad.DoDespawn()` (`Spawner.DoDespawn(this)`).
fn do_despawn_task(doodad: DoodadRef) -> impl Fn(&GameServer) + Send + Sync + 'static {
    move |server: &GameServer| {
        if let Some(s) = server.systems.npcs.spawns.doodad_spawner_of(doodad.obj_id) {
            s.do_despawn(server, &doodad);
        }
    }
}

/// `new DoodadSpawnerDoSpawnTask(spawner)`: `spawner.DoSpawn()`.
fn do_spawn_task(spawner: Arc<DoodadSpawner>) -> impl Fn(&GameServer) + Send + Sync + 'static {
    move |server: &GameServer| spawner.do_spawn(server)
}

fn cron_unusable(expr: &str) -> bool {
    expr.is_empty() || expr == "0 0 0 0 0 ?"
}

impl DoodadSpawner {
    /// A spawner from its JSON entry (`ParentWorld` / `Id` / position fix-ups are applied by
    /// `SpawnManager.Load`).
    pub fn new(def: DoodadSpawnerDef, parent_world: Option<u32>) -> Self {
        Self {
            id: def.base.id,
            spawner_id: def.base.spawner_id,
            unit_id: def.base.unit_id,
            follow_path: def.base.follow_path,
            follow_npc: def.base.follow_npc,
            position: def.base.position,
            despawn_time: def.base.despawn_time,
            special_link: def.base.special_link,
            related_ids: def.related_ids,
            state: Mutex::new(State {
                parent_world,
                respawn_time: def.base.respawn_time,
                scale: def.scale,
                last: None,
                spawned: Vec::new(),
                scheduled_count: 0,
                spawn_count: 0,
                count: def.count,
                permanent: true,
                respawn_doodad_template_id: def.respawn_doodad_template_id,
                not_found_in_scheduler: false,
            }),
        }
    }

    fn st(&self) -> MutexGuard<'_, State> {
        self.state.lock().expect("doodad spawner poisoned")
    }

    pub fn respawn_time(&self) -> i32 {
        self.st().respawn_time
    }

    pub fn set_respawn_time(&self, v: i32) {
        self.st().respawn_time = v;
    }

    pub fn scale(&self) -> f32 {
        self.st().scale
    }

    pub fn count(&self) -> u32 {
        self.st().count
    }

    /// `RespawnDoodadTemplateId`.
    pub fn respawn_doodad_template_id(&self) -> u32 {
        self.st().respawn_doodad_template_id
    }

    /// `RespawnDoodadTemplateId = id` (`DoodadFuncRatioRespawn`).
    pub fn set_respawn_doodad_template_id(&self, id: u32) {
        self.st().respawn_doodad_template_id = id;
    }

    /// `Last`.
    pub fn last(&self) -> Option<DoodadRef> {
        self.st().last.clone()
    }

    /// `_spawned`.
    pub fn spawned(&self) -> Vec<DoodadRef> {
        self.st().spawned.clone()
    }

    pub fn parent_world(&self) -> Option<u32> {
        self.st().parent_world
    }

    pub fn set_parent_world(&self, w: Option<u32>) {
        self.st().parent_world = w;
    }

    /// The spawn-time reset at the top of both `Spawn` overloads.
    fn reset_for_spawn(&self) {
        let mut st = self.st();
        st.permanent = true;
        st.spawned = Vec::new();
        st.count = 1;
        st.last = None;
    }

    /// The common part after `DoodadManager.Create`: spawner link, transform, plant time, scale.
    fn setup_doodad(self: &Arc<Self>, server: &GameServer, doodad: &DoodadRef, instance_id: u32) {
        let scale = self.scale();
        spawn_manager::forget_object_fields(server, doodad.obj_id);
        server
            .systems
            .npcs
            .spawns
            .set_doodad_spawner(doodad.obj_id, self.clone());
        let mut d = doodad.data();
        d.spawner_id = self.id;
        // Transform.ApplyWorldSpawnPosition(Position)
        let p = self.position;
        d.transform.zone_id = p.zone_id;
        d.transform
            .set_position(p.x, p.y, p.z, p.roll, p.pitch, p.yaw);
        if d.transform.instance_id == 0 {
            d.transform.instance_id = instance_id;
        }
        // TODO for test (C#): PlantTime = DateTime.UtcNow
        d.plant_time = chrono::Utc::now().timestamp();
        if scale > 0.0 {
            d.scale = scale;
        }
    }

    /// `Spawn(objId, itemId, charId)`: a doodad owned by a character (player-created spawns).
    pub fn spawn_for_character(
        self: &Arc<Self>,
        server: &GameServer,
        obj_id: u32,
        item_id: u64,
        char_obj_id: u32,
    ) -> Option<DoodadRef> {
        self.reset_for_spawn();
        // C#: WorldManager.GetCharacterByObjId(charId).ParentWorld (null -> exception).
        let character = server.world.character_by_obj_id(char_obj_id)?;
        let owner = WorldObject::from(&character);
        let world = server.world.world_of(&owner)?;
        let Some(doodad) =
            backend::doodad_create(server, &world, obj_id, self.unit_id, Some(&owner), false)
        else {
            tracing::warn!("Doodad {}, from spawn not exist at db", self.unit_id);
            return None;
        };
        self.setup_doodad(server, &doodad, world.id);
        {
            let mut d = doodad.data();
            d.quest_glow = 0; // TODO: make this OOP
            d.item_id = item_id;
        }
        self.st().last = Some(doodad.clone());
        self.do_spawn(server); // schedule check and spawn
        Some(doodad)
    }

    /// `Spawn(objId)` (initial spawn and respawns). Only `objId == 0` creates a doodad.
    pub fn spawn(self: &Arc<Self>, server: &GameServer, obj_id: u32) -> Option<DoodadRef> {
        // TODO (C#): clean up each doodad using the same call
        self.reset_for_spawn();
        if obj_id != 0 {
            return None;
        }
        let new_unit_id = {
            let mut st = self.st();
            let id = if st.respawn_doodad_template_id > 0 {
                st.respawn_doodad_template_id
            } else {
                self.unit_id
            };
            st.respawn_doodad_template_id = 0; // reset it after 1 spawn
            id
        };
        let world = self.parent_world().and_then(|id| server.world.world(id));
        let Some(world) = world else {
            tracing::error!("Can't spawn doodad {new_unit_id} from spawn {}", self.id);
            return None;
        };
        let Some(doodad) = backend::doodad_create(server, &world, obj_id, new_unit_id, None, false)
        else {
            tracing::warn!("Doodad Temaplte {new_unit_id}, used in Spawn() does not exist in db");
            return None;
        };
        self.setup_doodad(server, &doodad, world.id);
        self.st().last = Some(doodad.clone());
        self.do_spawn(server); // schedule check and spawn
        Some(doodad)
    }

    /// `Spawner<Doodad>.Respawn(obj)` (not overridden: `Spawn(0)`).
    pub fn respawn(self: &Arc<Self>, server: &GameServer, _doodad: &DoodadRef) {
        self.spawn(server, 0);
    }

    /// `Despawn(doodad)`.
    pub fn despawn(&self, server: &GameServer, doodad: &DoodadRef) {
        // doodad.FuncTask?.Cancel(): the doodad manager cancels the func task on delete.
        delete_doodad(server, doodad, self.parent_world());
        let spawns = &server.systems.npcs.spawns;
        if spawns.respawn_field(doodad.obj_id).is_none() {
            server.ids.object.release_id(doodad.obj_id);
            spawns.forget_object(doodad.obj_id);
        }
        let mut st = self.st();
        if let Some(i) = st.spawned.iter().position(|d| Arc::ptr_eq(d, doodad)) {
            st.spawned.remove(i);
        }
        if st.last.as_ref().is_some_and(|l| Arc::ptr_eq(l, doodad)) {
            st.last = None;
        }
    }

    /// `DespawnAllSpawnedDoodads()`: `_spawned`, `Last` and every doodad of the parent world
    /// linked to this spawner (orphans of `Spawn` resetting `_spawned`).
    pub fn despawn_all_spawned_doodads(self: &Arc<Self>, server: &GameServer) {
        let mut set: Vec<DoodadRef> = Vec::new();
        let mut add = |d: DoodadRef| {
            if !set.iter().any(|s| Arc::ptr_eq(s, &d)) {
                set.push(d);
            }
        };
        let (spawned, last, parent) = {
            let st = self.st();
            (st.spawned.clone(), st.last.clone(), st.parent_world)
        };
        spawned.into_iter().for_each(&mut add);
        if let Some(l) = last {
            add(l);
        }
        if let Some(world) = parent.and_then(|id| server.world.world(id)) {
            let spawns = &server.systems.npcs.spawns;
            for d in world.all_doodads() {
                if spawns
                    .doodad_spawner_of(d.obj_id)
                    .is_some_and(|s| Arc::ptr_eq(&s, self))
                {
                    add(d);
                }
            }
        }
        for d in &set {
            self.despawn(server, d);
        }
        let mut st = self.st();
        st.spawned = Vec::new();
        st.last = None;
    }

    /// `DecreaseCount(doodad)`: deletes the doodad and schedules a respawn.
    pub fn decrease_count(self: &Arc<Self>, server: &GameServer, doodad: &DoodadRef) {
        let respawn_time = self.respawn_time();
        if respawn_time > 0 {
            let at = spawn_manager::add_secs(spawn_manager::now(), f64::from(respawn_time));
            let obj = WorldObject::from(doodad);
            match server
                .world
                .world_of(&obj)
                .and_then(|w| server.systems.npcs.spawns.get(w.id))
            {
                Some(m) => m.add_respawn(server, obj, Some(at)),
                None => tracing::error!("DecreaseCount: doodad {} has no world", doodad.obj_id),
            }
        } else {
            self.st().last = None;
        }
        delete_doodad(server, doodad, self.parent_world());
    }

    /// `DoDespawn(doodad)` (`Doodad.DoDespawn` / `DoodadSpawnerDoDespawnTask`).
    pub fn do_despawn(self: &Arc<Self>, server: &GameServer, doodad: &DoodadRef) {
        let (template_id, obj_id, func_group_id) = {
            let d = doodad.data();
            (d.template_id, doodad.obj_id, d.func_group_id)
        };
        let schedule = &server.game_data.systems.npcs.schedule;
        let schedule_spawner = schedule.check_doodad_in_schedule_spawners(template_id as i32);
        if schedule_spawner && schedule.check_doodad_in_game_schedules(template_id) {
            match schedule.period_status_doodad(template_id as i32, spawn_manager::unix_now()) {
                PeriodStatus::NotFound => {
                    // All the same, we will despawn the doodad (no record in the scheduler).
                    self.st().not_found_in_scheduler = true;
                    tracing::trace!("DoSpawnSchedule: Doodad was not found in the schedule, we will despawn it templateId={template_id} objId={obj_id}");
                }
                PeriodStatus::NotStarted => {
                    tracing::trace!("DoDespawn: The period has not yet begun. Can despawn Doodad templateId={template_id} objId={obj_id}");
                }
                PeriodStatus::InProgress => {
                    // The period is already running, we need to schedule the despawn.
                    let cron = doodad_cron_remaining_time(server, template_id, false);
                    if cron_unusable(&cron) {
                        tracing::trace!("DoDespawn: Can't schedule despawn Doodad templateId={template_id} objId={obj_id} cronExpression {cron}");
                        return;
                    }
                    match cron_schedule(server, &cron, do_despawn_task(doodad.clone())) {
                        Ok(()) => tracing::trace!("DoDespawn: Schedule the despawn of Doodad templateId={template_id} objId={obj_id} cronExpression {cron}"),
                        Err(e) => tracing::trace!("DoDespawn: Can't schedule despawn Doodad templateId={template_id} objId={obj_id} cronExpression {cron}: {e}"),
                    }
                    return;
                }
                PeriodStatus::Ended => {
                    tracing::trace!("DoDespawn: The period has ended. Can despawn Doodad templateId={template_id} objId={obj_id}");
                }
            }
        }

        self.despawn(server, doodad);

        if schedule_spawner && !self.st().not_found_in_scheduler {
            let cron = doodad_cron_remaining_time(server, template_id, true);
            if cron_unusable(&cron) {
                tracing::trace!("DoDespawn: Can't schedule spawn Doodad templateId={template_id} objId={obj_id} cronExpression {cron}");
            } else {
                tracing::debug!("DoDespawn: Doodad TemplateId {template_id}, objId {obj_id} FuncGroupId {func_group_id}, cronExpression={cron} spawn reschedule next time...");
                if let Err(e) = cron_schedule(server, &cron, do_spawn_task(self.clone())) {
                    tracing::trace!("DoDespawn: Can't schedule spawn Doodad templateId={template_id} objId={obj_id} cronExpression {cron}: {e}");
                }
            }
        }
    }

    /// `DoSpawn()`: spawns `Last` (with the game schedule checks).
    pub fn do_spawn(self: &Arc<Self>, server: &GameServer) {
        let schedule = &server.game_data.systems.npcs.schedule;
        let last_obj_id = self.last().map_or(0, |l| l.obj_id);
        let unit_id = self.unit_id;
        if schedule.check_doodad_in_schedule_spawners(unit_id as i32)
            && schedule.check_doodad_in_game_schedules(unit_id)
        {
            self.st().permanent = false; // Doodad on the schedule.
            if last_obj_id == 0 {
                // C#: every branch below logs `Last.ObjId` (NullReferenceException when a cron
                // DoSpawn task runs after Despawn cleared `Last`).
                return;
            }
            match schedule.period_status_doodad(unit_id as i32, spawn_manager::unix_now()) {
                PeriodStatus::NotFound => {
                    // Not on the schedule although it should be: spawn it anyway.
                    self.st().not_found_in_scheduler = true;
                    tracing::trace!("DoSpawnSchedule: Doodad was not found in the schedule, we will spawn it templateId={unit_id} objId={last_obj_id}");
                }
                PeriodStatus::NotStarted => {
                    let cron = doodad_cron_remaining_time(server, unit_id, true);
                    if cron_unusable(&cron) {
                        tracing::trace!("DoSpawnSchedule: Can't schedule spawn Doodad templateId={unit_id} objId={last_obj_id} cronExpression {cron}");
                        self.st().permanent = true;
                        return;
                    }
                    if let Err(e) = cron_schedule(server, &cron, do_spawn_task(self.clone())) {
                        tracing::trace!("DoSpawnSchedule: Can't schedule spawn Doodad templateId={unit_id} objId={last_obj_id} cronExpression {cron}: {e}");
                        self.st().permanent = true;
                    }
                    return; // Reschedule when OK
                }
                PeriodStatus::InProgress => {
                    tracing::trace!("DoSpawnSchedule: Can spawn. The period is already underway. Doodad templateId={unit_id} objId={last_obj_id}");
                }
                PeriodStatus::Ended => {
                    tracing::trace!("DoSpawnSchedule: Can't spawn. The period has ended. Doodad templateId={unit_id} objId={last_obj_id}");
                    return;
                }
            }
        }

        // C#: Last.Spawn() on the `new Doodad()` placeholder when nothing was created (only
        // reachable from a cron task, which is not ported).
        let Some(last) = self.last() else {
            return;
        };
        let obj = WorldObject::from(&last);
        let world = server
            .world
            .world_of(&obj)
            .or_else(|| self.parent_world().and_then(|id| server.world.world(id)));
        let Some(world) = world else {
            tracing::error!(
                "Can't spawn doodad {unit_id} from spawn {}: no world",
                self.id
            );
            return;
        };
        backend::doodad_spawn(server, &world, &last);
        // TODO(npcs): dungeon instances (`Last.ParentWorld.DungeonInstance` ->
        // `Events.OnDoodadSpawn`) for worlds other than the default world template.

        let (template_id, reschedule) = {
            let mut st = self.st();
            st.spawned.push(last.clone());
            if st.scheduled_count > 0 {
                st.scheduled_count -= 1;
            }
            st.spawn_count = st.spawned.len() as i32;
            if st.spawn_count < 0 {
                st.spawn_count = 0;
            }
            (
                last.data().template_id,
                !st.permanent && !st.not_found_in_scheduler,
            )
        };
        if reschedule {
            let cron = doodad_cron_remaining_time(server, template_id, false);
            if cron_unusable(&cron) {
                tracing::trace!("DoSpawnSchedule: Can't reschedule despawn Doodad templateId={template_id} objId={} cronExpression {cron}", last.obj_id);
            } else if let Err(e) = cron_schedule(server, &cron, do_despawn_task(last.clone())) {
                tracing::trace!("DoSpawnSchedule: Can't reschedule despawn Doodad templateId={template_id} objId={} cronExpression {cron}: {e}", last.obj_id);
            }
        }
    }
}

/// `doodad.Delete()` in the doodad's world (or the spawner's parent world).
fn delete_doodad(server: &GameServer, doodad: &DoodadRef, parent_world: Option<u32>) {
    let obj = WorldObject::from(doodad);
    let world = server
        .world
        .world_of(&obj)
        .or_else(|| parent_world.and_then(|id| server.world.world(id)));
    if let Some(world) = world {
        backend::doodad_delete(server, &world, doodad);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn spawner_json_defaults() {
        let v = parse_list(
            r#"[ { "UnitId": 7056, "Title": "Egg", "Position": { "X": 1.5, "Y": 2, "Z": 3, "Yaw": 90 } },
                 { "unitid": 1, "Scale": 0.5, "Count": 3, "RespawnTime": 0, "DespawnTime": -1,
                   "RelatedIds": [1, 2], "RespawnDoodadTemplateId": 9, "SpecialLink": "justice.1",
                   "funcGroupId": 5, "Position": { "X": 0, "Y": 0, "WorldId": 3, "ZoneId": 7 } }, ]"#,
            DoodadSpawnerDef::from_json,
        )
        .unwrap();
        assert_eq!(v[0].base.unit_id, 7056);
        assert_eq!(v[0].base.respawn_time, 15);
        assert_eq!(v[0].base.despawn_time, 20);
        assert_eq!(v[0].scale, 1.0);
        assert_eq!(v[0].count, 1);
        assert_eq!(v[0].related_ids, None);
        assert_eq!(v[0].base.position.world_id, 1);
        assert_eq!(v[0].base.position.yaw, 90.0);
        assert_eq!(v[1].scale, 0.5);
        assert_eq!(v[1].count, 3);
        assert_eq!(v[1].base.respawn_time, 0);
        assert_eq!(v[1].base.despawn_time, -1);
        assert_eq!(v[1].related_ids, Some(vec![1, 2]));
        assert_eq!(v[1].respawn_doodad_template_id, 9);
        assert_eq!(v[1].base.special_link, "justice.1");
        assert_eq!(v[1].base.position.world_id, 3);
        assert_eq!(v[1].base.position.zone_id, 7);

        let n = parse_list(
            r#"[{"UnitId": 5, "NpcSpawnerIds": [0], "FollowPath": "p1", "Position": {}}]"#,
            NpcSpawnerDef::from_json,
        )
        .unwrap();
        assert_eq!(n[0].npc_spawner_ids, vec![0]);
        assert_eq!(n[0].count, 1);
        assert_eq!(n[0].base.follow_path, "p1");

        assert!(parse_list(r#"[{"UnitId": 1}]"#, SimpleSpawnerDef::from_json).is_err());
        assert!(parse_list(r#"{"UnitId": 1}"#, SimpleSpawnerDef::from_json).is_err());
        assert!(parse_list("null", SimpleSpawnerDef::from_json).is_err());
        assert!(parse_list(
            r#"[{"UnitId": 1.5, "Position": {}}]"#,
            SimpleSpawnerDef::from_json
        )
        .is_err());
    }
}
