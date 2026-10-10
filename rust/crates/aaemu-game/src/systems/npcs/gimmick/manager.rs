//! `GimmickManager` (one per world instance), `Gimmick.GimmickTick`, `GimmickSpawner.Spawn` /
//! `Despawn`, and the skill-effect spawner constructor.
//!
//! The C# `Gimmick` runtime members (`Time`, `MoveDown`, `IsMoving`, `WaitTime`, `LastPos`,
//! `TotalLifeTime`, `MovementHandler`, `Spawner`, `Template`, `Respawn`, `Despawn`) live in
//! [`ActiveGimmick`] next to the [`GimmickRef`]; the members the packets read (`Vel`, `AngVel`,
//! `Scale`, transform) are in `models::gimmick::Gimmick`.
//!
//! Locking: an [`ActiveGimmick`]'s state mutex is taken before (never while holding) the
//! gimmick's data lock, and neither is held while calling into the world.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, MutexGuard, Weak};
use std::time::{Duration, Instant};

use tracing::{error, info, warn};

use super::movement::{self, MovementHandler, MovementState};
use super::packets::SCGimmickMovementPacket;
use super::spawner::GimmickSpawner;
use super::GimmickTemplate;
use crate::managers::chat::chat_type;
use crate::models::gimmick::Gimmick;
use crate::models::transform::{PositionAndRotation, Quaternion};
use crate::packets::world::SCChatMessagePacket;
use crate::server::GameServer;
use crate::world::object::Entity;
use crate::world::{self, session, GimmickRef, WorldInstance, WorldObject};

/// `GimmickManager.Delay`: the tick period.
pub const TICK_DELAY: Duration = Duration::from_millis(50);
/// The template id that gets `GimmickMovementFloatToSurface` (Recovered Treasure Chest).
pub const FLOAT_TO_SURFACE_TEMPLATE_ID: u32 = 37;

/// `(uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds`.
pub fn time_of_day_ms(now_ms: i64) -> u32 {
    now_ms.rem_euclid(86_400_000) as u32
}

/// The C# `Gimmick` runtime members.
#[derive(Debug, Clone, Default)]
pub struct GimmickState {
    /// `Time`.
    pub time: u32,
    /// `MoveDown`.
    pub move_down: bool,
    /// `IsMoving`.
    pub is_moving: bool,
    /// `WaitTime` (unix ms, 0 = `DateTime.MinValue`).
    pub wait_time_ms: i64,
    /// `TotalLifeTime`.
    pub total_life_time: Duration,
    /// `LastPos`.
    pub last_pos: [f32; 3],
    /// `LastRot`.
    pub last_rot: [f32; 3],
    /// `SkillStarted`.
    pub skill_started: bool,
    /// `Despawn != DateTime.MinValue`.
    pub despawned: bool,
    /// `Respawn` (unix ms, 0 = `DateTime.MinValue`).
    pub respawn_ms: i64,
}

impl GimmickState {
    /// `TimeLeft` (ms).
    pub fn time_left(&self, now_ms: i64) -> u32 {
        if self.wait_time_ms > now_ms {
            (self.wait_time_ms - now_ms) as u32
        } else {
            0
        }
    }
}

/// A gimmick in `GimmickManager._activeGimmicks`.
#[derive(Debug)]
pub struct ActiveGimmick {
    pub gimmick: GimmickRef,
    /// `ParentWorld.Id`.
    pub world_id: u32,
    /// `Spawner`.
    pub spawner: Arc<GimmickSpawner>,
    /// `Template` (`None` for elevators).
    pub template: Option<GimmickTemplate>,
    /// `MovementHandler`.
    pub handler: Option<MovementHandler>,
    state: Mutex<GimmickState>,
}

impl ActiveGimmick {
    pub fn state(&self) -> MutexGuard<'_, GimmickState> {
        self.state.lock().unwrap_or_else(|e| e.into_inner())
    }
}

/// `WorldInstance.GimmickManager`: the active gimmicks of one instance (insertion order).
#[derive(Debug, Default)]
pub struct WorldGimmicks {
    active: Mutex<Vec<Arc<ActiveGimmick>>>,
}

impl WorldGimmicks {
    fn lock(&self) -> MutexGuard<'_, Vec<Arc<ActiveGimmick>>> {
        self.active.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// `GetActiveGimmicks()` (a snapshot).
    pub fn active(&self) -> Vec<Arc<ActiveGimmick>> {
        self.lock().clone()
    }

    /// `_activeGimmicks.TryGetValue(objId)`.
    pub fn get(&self, obj_id: u32) -> Option<Arc<ActiveGimmick>> {
        self.lock()
            .iter()
            .find(|a| a.gimmick.obj_id == obj_id)
            .cloned()
    }

    /// `_activeGimmicks.TryAdd`.
    fn add(&self, g: Arc<ActiveGimmick>) {
        let mut l = self.lock();
        if !l.iter().any(|a| a.gimmick.obj_id == g.gimmick.obj_id) {
            l.push(g);
        }
    }

    /// `RemoveActiveGimmick`.
    pub fn remove(&self, obj_id: u32) -> bool {
        let mut l = self.lock();
        let before = l.len();
        l.retain(|a| a.gimmick.obj_id != obj_id);
        l.len() != before
    }
}

/// `server.systems.npcs.gimmicks`: the `GimmickManager` of every world instance.
#[derive(Debug, Default)]
pub struct GimmickManager {
    worlds: Mutex<HashMap<u32, Arc<WorldGimmicks>>>,
}

impl GimmickManager {
    /// The `GimmickManager` of instance `world_id` (created on first use).
    pub fn for_world(&self, world_id: u32) -> Arc<WorldGimmicks> {
        self.worlds
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .entry(world_id)
            .or_default()
            .clone()
    }

    fn all(&self) -> Vec<(u32, Arc<WorldGimmicks>)> {
        let mut v: Vec<_> = self
            .worlds
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .iter()
            .map(|(k, w)| (*k, w.clone()))
            .collect();
        v.sort_by_key(|(k, _)| *k);
        v
    }

    /// The active gimmick with `obj_id` in any instance.
    pub fn find(&self, obj_id: u32) -> Option<Arc<ActiveGimmick>> {
        self.all().into_iter().find_map(|(_, w)| w.get(obj_id))
    }
}

/// `GimmickManager.AddActiveGimmick`: attaches the movement handler, sets `Time` and registers it.
fn add_active_gimmick(
    server: &GameServer,
    world_id: u32,
    gimmick: GimmickRef,
    spawner: Arc<GimmickSpawner>,
    template: Option<GimmickTemplate>,
) -> Arc<ActiveGimmick> {
    let (template_id, entity_guid) = {
        let g = gimmick.data();
        (g.unit.template_id, g.entity_guid)
    };
    let handler = if template_id == 0 && entity_guid > 0 {
        // Elevators defined in gimmick_spawns.json
        Some(MovementHandler::Elevator)
    } else if template_id == FLOAT_TO_SURFACE_TEMPLATE_ID {
        Some(MovementHandler::FloatToSurface)
    } else {
        None
    };
    let state = GimmickState {
        time: time_of_day_ms(world::now_ms()),
        ..Default::default()
    };
    let active = Arc::new(ActiveGimmick {
        gimmick,
        world_id,
        spawner,
        template,
        handler,
        state: Mutex::new(state),
    });
    server
        .systems
        .npcs
        .gimmicks
        .for_world(world_id)
        .add(active.clone());
    active
}

fn new_gimmick(
    server: &GameServer,
    template_id: u32,
    template: Option<&GimmickTemplate>,
) -> Option<Gimmick> {
    let gimmick_id = server.ids.gimmick.get_next_id().ok()? as u16;
    let mut g = Gimmick::new(template_id);
    g.gimmick_id = gimmick_id;
    g.model_path = template.map(|t| t.model_path.clone()).unwrap_or_default();
    g.unit.faction_id = 0; // new SystemFaction()
    Some(g)
}

/// `GimmickManager.Create(objectId, templateId, spawner)` + `Gimmick.Spawn` +
/// `AddActiveGimmick`. Note (C#): the obj id and gimmick id are taken before the origin check
/// and leak when it fails.
fn create(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    obj_id: u32,
    template_id: u32,
    spawner: Arc<GimmickSpawner>,
) -> Option<Arc<ActiveGimmick>> {
    // For elevators: templateId = 0 and Template = null, but EntityGuid is used.
    let template = server
        .game_data
        .systems
        .npcs
        .gimmick
        .get_gimmick_template(template_id)
        .cloned();
    if template.is_none() && template_id != 0 {
        return None;
    }
    let obj_id = if obj_id > 0 {
        obj_id
    } else {
        server.ids.object.get_next_id().ok()?
    };
    let mut g = new_gimmick(server, template_id, template.as_ref())?;
    g.entity_guid = if template.is_none() {
        spawner.entity_guid
    } else {
        0
    };
    g.spawner_id = spawner.id;
    // Transform.ApplyWorldSpawnPosition(spawner.Position) (C#: NullReferenceException without one)
    let Some(pos) = spawner.position else {
        error!(
            "Can't spawn gimmick {template_id}: spawner {} has no Position",
            spawner.id
        );
        return None;
    };
    {
        let t = &mut g.unit.transform;
        t.instance_id = world.id;
        t.world_id = world.template.id;
        t.zone_id = pos.zone_id;
        t.set_position(pos.x, pos.y, pos.z, pos.roll, pos.pitch, pos.yaw);
        // Transform.Local.ApplyFromQuaternion(spawner rotation)
        let (roll, pitch, yaw) = PositionAndRotation::rotation_from_quaternion(Quaternion {
            x: spawner.rotation_x,
            y: spawner.rotation_y,
            z: spawner.rotation_z,
            w: spawner.rotation_w,
        });
        t.rot_x = roll;
        t.rot_y = pitch;
        t.rot_z = yaw;
    }
    g.vel = [0.0; 3];
    g.unit.scale = spawner.scale; // SetScale
    if g.unit.transform.x == 0.0 && g.unit.transform.y == 0.0 && g.unit.transform.z == 0.0 {
        error!("Can't spawn gimmick {template_id}");
        return None;
    }
    let gimmick: GimmickRef = Entity::new(obj_id, g);
    let changes = world.spawn(&gimmick, &server.view_ctx());
    session::handle_zone_changes(server, changes);
    Some(add_active_gimmick(
        server, world.id, gimmick, spawner, template,
    ))
}

/// `GimmickSpawner.Spawn(0)`: spawns the gimmick of `spawner` (template `UnitId`, 0 =
/// elevator) in `world`. The caller (SpawnManager) sets `Position.WorldId` / `ZoneId` first, as
/// `LoadGimmickSpawns` does. `None` = `"Gimmick {UnitId}, from spawn not exist at db"`.
pub fn spawn_from_spawner(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    spawner: &GimmickSpawner,
) -> Option<GimmickRef> {
    match create(server, world, 0, spawner.unit_id, Arc::new(spawner.clone())) {
        Some(a) => Some(a.gimmick.clone()),
        None => {
            warn!("Gimmick {}, from spawn not exist at db", spawner.unit_id);
            None
        }
    }
}

/// The `SpawnGimmickEffect` members the skill-effect constructor reads.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct SpawnGimmickParams {
    pub gimmick_id: u32,
    pub offset_from_source: bool,
    pub offset_coordinate_id: i32,
    pub offset_x: f32,
    pub offset_y: f32,
    pub offset_z: f32,
    pub scale: f32,
    pub velocity_coordinate_id: i32,
    pub velocity_x: f32,
    pub velocity_y: f32,
    pub velocity_z: f32,
    pub ang_vel_coordinate_id: i32,
    pub ang_vel_x: f32,
    pub ang_vel_y: f32,
    pub ang_vel_z: f32,
}

/// `new GimmickSpawner(parentWorld, sgEffect, caster)` (`SpawnGimmickEffect`): creates the
/// gimmick (`GimmickManager.Create(templateId)`) at the caster's world transform, applies the
/// offset, spawns it and makes it active. The caller sets `npc.Gimmick` for an NPC caster.
/// `None` when the template does not exist or the offset type is invalid (C# throws).
/// Note (C#): `OffsetCoordinateType.Unk0` moves by `AddDistanceToFront(1, 1, pos, pos)`, which
/// is a no-op.
pub fn spawn_from_effect(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    params: &SpawnGimmickParams,
    caster: &WorldObject,
) -> Option<GimmickRef> {
    let template = server
        .game_data
        .systems
        .npcs
        .gimmick
        .get_gimmick_template(params.gimmick_id)
        .cloned()?;
    let spawner = Arc::new(GimmickSpawner {
        gimmick_id: params.gimmick_id,
        offset_from_source: params.offset_from_source,
        offset_coordinate_id: params.offset_coordinate_id,
        offset_x: params.offset_x,
        offset_y: params.offset_y,
        offset_z: params.offset_z,
        scale: params.scale,
        velocity_coordinate_id: params.velocity_coordinate_id,
        velocity_x: params.velocity_x,
        velocity_y: params.velocity_y,
        velocity_z: params.velocity_z,
        ang_vel_coordinate_id: params.ang_vel_coordinate_id,
        ang_vel_x: params.ang_vel_x,
        ang_vel_y: params.ang_vel_y,
        ang_vel_z: params.ang_vel_z,
        count: 1,
        respawn_time: 0, // don't respawn
        ..GimmickSpawner::default()
    });
    let obj_id = server.ids.object.get_next_id().ok()?;
    let mut g = new_gimmick(server, template.id, Some(&template))?;
    // gimmick.Transform = caster.Transform.CloneDetached(gimmick)
    let caster_t = caster.transform();
    let caster_world = world.world_pos_rot(caster);
    {
        let t = &mut g.unit.transform;
        t.instance_id = caster_t.instance_id;
        t.world_id = caster_t.world_id;
        t.zone_id = caster_t.zone_id;
        t.set_position(
            caster_world.x,
            caster_world.y,
            caster_world.z,
            caster_world.rot_x,
            caster_world.rot_y,
            caster_world.rot_z,
        );
    }
    g.entity_guid = 0;
    g.spawner_unit_id = caster.obj_id();
    g.grasper_unit_id = 0;
    match params.offset_coordinate_id {
        0 | 1 | 3 => {}
        2 => {
            // Transform.Local.AddDistance(OffsetX, OffsetY, OffsetZ): a plain translation
            let t = &mut g.unit.transform;
            t.set_xyz(
                t.x + params.offset_x,
                t.y + params.offset_y,
                t.z + params.offset_z,
            );
        }
        other => {
            error!("SpawnGimmickEffect: invalid OffsetCoordinateId {other}");
            return None;
        }
    }
    g.unit.scale = params.scale;
    let gimmick: GimmickRef = Entity::new(obj_id, g);
    let changes = world.spawn(&gimmick, &server.view_ctx());
    session::handle_zone_changes(server, changes);
    add_active_gimmick(server, world.id, gimmick.clone(), spawner, Some(template));
    Some(gimmick)
}

/// `GimmickSpawner.Despawn(gimmick)`: removes it from the manager and the world; releases its
/// ids unless a respawn is pending.
pub fn despawn(server: &GameServer, active: &ActiveGimmick) {
    server
        .systems
        .npcs
        .gimmicks
        .for_world(active.world_id)
        .remove(active.gimmick.obj_id);
    if let Some(world) = server.world.world(active.world_id) {
        world.delete(&active.gimmick);
    }
    if active.state().respawn_ms == 0 {
        if active.gimmick.obj_id > 0 {
            server.ids.object.release_id(active.gimmick.obj_id);
        }
        let gimmick_id = active.gimmick.data().gimmick_id;
        if gimmick_id > 0 {
            server.ids.gimmick.release_id(u32::from(gimmick_id));
        }
    }
}

/// `Gimmick.DoGimmickSkill(skillId)`: runs the skill once through the skills hook (caster =
/// the spawner unit, a `SkillDoodad` caster of the gimmick, cast at the gimmick's position) and
/// announces it in the system chat around the gimmick.
fn do_gimmick_skill(
    server: &GameServer,
    world: &WorldInstance,
    active: &ActiveGimmick,
    skill_id: u32,
) {
    if skill_id == 0 {
        return;
    }
    {
        let mut st = active.state();
        if st.skill_started {
            return;
        }
        st.skill_started = true;
    }
    let (spawner_unit_id, pos) = {
        let g = active.gimmick.data();
        (g.spawner_unit_id, g.unit.transform.local())
    };
    let caster = world.unit(spawner_unit_id);
    let me = WorldObject::from(&active.gimmick);
    if let Some(h) = server.systems.npcs.hooks.skills.get() {
        h.use_gimmick_skill(
            server,
            caster.as_ref(),
            &me,
            skill_id,
            (pos.x, pos.y, pos.z),
        );
    }
    let msg = SCChatMessagePacket {
        chat_type: chat_type::SYSTEM,
        sender: None,
        message: format!("Gimmick {} used skill {skill_id}", active.gimmick.obj_id),
        ability: 0,
        language_type: 0,
    };
    world::broadcast(world, &me, &msg, false);
}

/// `Gimmick.StopMovement()` (NPC AI gimmicks): uses the template skill when it has a target.
pub fn stop_movement(server: &GameServer, active: &ActiveGimmick) {
    if active.gimmick.data().unit.current_target == 0 {
        return;
    }
    if let Some(world) = server.world.world(active.world_id) {
        let skill = active.template.as_ref().map_or(0, |t| t.skill_id);
        do_gimmick_skill(server, &world, active, skill);
    }
}

/// `Gimmick.GimmickTick(delta)`.
pub fn gimmick_tick(
    server: &GameServer,
    world: &WorldInstance,
    active: &ActiveGimmick,
    delta: Duration,
    now_ms: i64,
) {
    let delta_time = delta.as_secs_f32();
    // LastLifeTime / TotalLifeTime, TimeLeft
    let last_life_time = {
        let mut st = active.state();
        let last = st.total_life_time;
        st.total_life_time += delta;
        if st.time_left(now_ms) > 0 {
            return;
        }
        last
    };

    // MovementHandler?.Tick(delta)
    match active.handler {
        Some(MovementHandler::Elevator) => {
            let mut st = active.state();
            let (z, vel) = {
                let g = active.gimmick.data();
                (g.unit.transform.z, g.vel)
            };
            let mut m = MovementState {
                z,
                vel,
                is_moving: st.is_moving,
                move_down: st.move_down,
            };
            movement::elevator_tick(&mut m, &active.spawner, delta_time);
            st.is_moving = m.is_moving;
            st.move_down = m.move_down;
            let mut g = active.gimmick.data();
            g.unit.transform.set_height(m.z);
            g.vel = m.vel;
        }
        Some(MovementHandler::FloatToSurface) => {
            // World.Water.IsWater(World.Position + (0, 0, movement + 1)) (systems::ai water)
            let p = world.world_pos_rot(&crate::world::WorldObject::from(active.gimmick.clone()));
            let in_water = |h: f32| {
                let check = crate::systems::skills::controllers::Vec3::new(p.x, p.y, p.z + h);
                crate::systems::ai::physics::is_water(server, world.id, check).is_some()
            };
            if let Some(dz) = movement::float_to_surface_tick(delta_time, in_water) {
                let mut g = active.gimmick.data();
                let z = g.unit.transform.z;
                g.unit.transform.set_height(z + dz);
            }
        }
        None => {}
    }

    // Handle Delayed Skills
    if let Some(t) = &active.template {
        let started = active.state().skill_started;
        let total = active.state().total_life_time;
        if t.skill_delay > 0
            && !started
            && last_life_time.as_secs_f64() * 1000.0 < f64::from(t.skill_delay)
            && total.as_secs_f64() * 1000.0 >= f64::from(t.skill_delay)
        {
            do_gimmick_skill(server, world, active, t.skill_id);
        }
    }
    // TODO (C#): skill on collision (requires a physics engine).

    let time = time_of_day_ms(now_ms);
    let (packet, pos, rot) = {
        let last_pos = active.state().last_pos;
        let mut g = active.gimmick.data();
        let t = g.unit.transform;
        let pos = [t.x, t.y, t.z];
        let delta_position = [
            pos[0] - last_pos[0],
            pos[1] - last_pos[1],
            pos[2] - last_pos[2],
        ];
        // C#: `Vel = deltaPosition * deltaTime` (multiplied, not divided)
        g.vel = [
            delta_position[0] * delta_time,
            delta_position[1] * delta_time,
            delta_position[2] * delta_time,
        ];
        g.ang_vel = [0.0; 3];
        (
            SCGimmickMovementPacket::new(active.gimmick.obj_id, time, &g),
            pos,
            [t.rot_x, t.rot_y, t.rot_z],
        )
    };
    {
        let mut st = active.state();
        st.time = time;
    }
    world::broadcast(world, &active.gimmick, &packet, false);
    {
        let mut st = active.state();
        st.last_pos = pos;
        st.last_rot = rot;
        // MovementHandler?.AfterMove(delta, deltaPosition)
        if active.handler == Some(MovementHandler::Elevator) {
            let vel = active.gimmick.data().vel;
            let mut m = MovementState {
                z: pos[2],
                vel,
                is_moving: st.is_moving,
                move_down: st.move_down,
            };
            if let Some(w) = movement::elevator_after_move(&mut m, pos[2], &active.spawner, now_ms)
            {
                st.wait_time_ms = w;
            }
            st.move_down = m.move_down;
        }
    }

    // Check LifeTime and apply despawn time if needed
    if let Some(t) = &active.template {
        let despawn_now = {
            let mut st = active.state();
            let due = t.life_time > 0
                && !st.despawned
                && st.total_life_time.as_secs_f64() * 1000.0 >= f64::from(t.life_time);
            if due {
                st.despawned = true;
            }
            due
        };
        if despawn_now {
            despawn(server, active);
        }
    }
}

/// `GimmickManager.GimmickTick` of every instance.
pub fn tick_all(server: &GameServer, delta: Duration) {
    let now = world::now_ms();
    for (world_id, gimmicks) in server.systems.npcs.gimmicks.all() {
        let Some(world) = server.world.world(world_id) else {
            continue;
        };
        for g in gimmicks.active() {
            gimmick_tick(server, &world, &g, delta, now);
        }
    }
}

/// `GimmickManager.Initialize`: the 50 ms tick task.
pub fn start_tick(server: &Arc<GameServer>) {
    info!("GimmickTickTask: Started");
    let weak: Weak<GameServer> = Arc::downgrade(server);
    tokio::spawn(async move {
        let mut interval = tokio::time::interval(TICK_DELAY);
        interval.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
        let mut last = Instant::now();
        loop {
            interval.tick().await;
            let Some(server) = weak.upgrade() else {
                break;
            };
            let now = Instant::now();
            let delta = now - last;
            last = now;
            crate::systems::run_blocking(&server, move |s| tick_all(s, delta)).await;
        }
    });
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::test_support::test_server;

    fn elevator_spawner() -> GimmickSpawner {
        GimmickSpawner {
            position: Some(crate::data::world_templates::WorldSpawnPosition {
                world_id: 1,
                zone_id: 0,
                x: 100.0,
                y: 200.0,
                z: 10.0,
                ..Default::default()
            }),
            top_z: 11.0,
            bottom_z: 10.0,
            wait_time: 1.0,
            scale: 1.0,
            rotation_w: 1.0,
            entity_guid: 0x4a0d_a1f4_d842_0a38,
            ..GimmickSpawner::default()
        }
    }

    fn test_world(server: &GameServer) -> Arc<WorldInstance> {
        match server.world.worlds().into_iter().next() {
            Some(w) => w,
            None => {
                let t = Arc::new(crate::data::world_templates::WorldTemplate::synthetic(
                    0,
                    "main_world",
                    4,
                    4,
                    100,
                ));
                server.world.create_world_instance(t, 0)
            }
        }
    }

    #[tokio::test]
    async fn elevator_spawns_ticks_and_despawns() {
        let server = test_server();
        let world = test_world(&server);
        let g = spawn_from_spawner(&server, &world, &elevator_spawner()).expect("spawned");
        assert!(world.gimmick(g.obj_id).is_some());
        {
            let d = g.data();
            assert_eq!(d.unit.template_id, 0);
            assert_eq!(d.entity_guid, 0x4a0d_a1f4_d842_0a38);
            assert_eq!(d.unit.scale, 1.0);
            assert_eq!(d.unit.transform.instance_id, world.id);
        }
        let active = server.systems.npcs.gimmicks.find(g.obj_id).unwrap();
        assert_eq!(active.handler, Some(MovementHandler::Elevator));
        // 1 m up at 4.5 m/s, 0.05 s ticks: 4.444 -> 5 ticks (4 steps + snap), then waits 1 s.
        let dt = Duration::from_millis(50);
        for i in 0..5 {
            gimmick_tick(&server, &world, &active, dt, 1_000 + i * 50);
        }
        assert_eq!(g.data().unit.transform.z, 11.0);
        let wait = active.state().wait_time_ms;
        assert_eq!(wait, 1_200 + 1_000);
        assert!(active.state().move_down);
        // waiting: no movement
        gimmick_tick(&server, &world, &active, dt, 1_300);
        assert_eq!(g.data().unit.transform.z, 11.0);
        // after the wait it goes down
        gimmick_tick(&server, &world, &active, dt, 2_300);
        assert!(g.data().unit.transform.z < 11.0);
        despawn(&server, &active);
        assert!(world.gimmick(g.obj_id).is_none());
        assert!(server.systems.npcs.gimmicks.find(g.obj_id).is_none());
    }

    #[tokio::test]
    async fn unknown_template_or_origin_is_not_spawned() {
        let server = test_server();
        let world = test_world(&server);
        let s = GimmickSpawner {
            unit_id: 999_999,
            ..elevator_spawner()
        };
        assert!(spawn_from_spawner(&server, &world, &s).is_none());
        let s = GimmickSpawner {
            position: Some(Default::default()),
            ..elevator_spawner()
        };
        assert!(spawn_from_spawner(&server, &world, &s).is_none());
        let s = GimmickSpawner {
            position: None,
            ..elevator_spawner()
        };
        assert!(spawn_from_spawner(&server, &world, &s).is_none());
    }

    #[test]
    fn time_of_day() {
        assert_eq!(time_of_day_ms(86_400_000 + 1234), 1234);
    }
}
