//! `TransferManager` (one per world instance: the active transfers and the 100 ms tick),
//! `TransferSpawner` and the `Transfer` route following (`GoToPath`, `MoveTo`, `NextPoint`).
//!
//! Deviation: the C# makes the carriage motor a transform child of its boarding part
//! (`owner.Transform.Parent = transfer.Transform`) and immediately detaches it again in
//! `TransferSpawner.Spawn` (`ApplyWorldSpawnPosition` -> `DetachAll`); the boarding part keeps a
//! sticky parent link only. Here both parts stay top-level objects and the boarding part is
//! moved explicitly by the motor's `MoveTo` (as the C# does for the packets).

use std::sync::{Arc, Mutex};

use dashmap::DashMap;

use crate::data::world_templates::WorldSpawnPosition;
use crate::models::movement::{MoveKind, MoveType, TransferMove, WorldPos};
use crate::models::transfer::Transfer;
use crate::packets::world::SCOneUnitMovementPacket;
use crate::server::GameServer;
use crate::systems::npcs::doodad::spawner::SimpleSpawnerDef;
use crate::systems::skills::types::KillReason;
use crate::world::{Entity, TransferRef, WorldInstance, WorldObject};

use super::data::{attach_point, TransferTemplate};
use super::slaves::buff;
use super::stats::{self, VehicleKind};
use super::{broadcast, now_ms};

/// `Transfer.BoundedChildAlongFrontOffsetMeters`.
pub const BOUNDED_CHILD_ALONG_FRONT_OFFSET_METERS: f32 = -9.24417;
/// `TransferManager.Delay` (tick period).
pub const TICK_MS: u64 = 100;
/// `FactionsEnum.PcFriendly`.
pub const PC_FRIENDLY: u32 = 165;
/// The template ids `TransferSpawner.Spawn` does not put on a route (trailers).
const NO_ROUTE_TEMPLATES: [u32; 5] = [46, 4, 122, 135, 137];

/// `MathUtil` pi constants of `ConvertRadianToDirectionShort` (the C# literal `3.14159`).
#[allow(clippy::approx_constant)]
const PI: f64 = 3.14159;
#[allow(clippy::approx_constant)]
const PI2: f64 = 3.14159 * 2.0;
#[allow(clippy::approx_constant)]
const PI05: f64 = 3.14159 * 0.5;

/// `MathUtil.CalculateDirection(obj1, obj2)`.
pub fn calculate_direction(a: [f32; 3], b: [f32; 3]) -> f64 {
    f64::from(b[1] - a[1]).atan2(f64::from(b[0] - a[0]))
}

/// `MathUtil.ConvertRadianToDirectionShort(radian)` (a yaw-only `Quaternion.
/// CreateFromYawPitchRoll`), returned as (x, y, z, w).
pub fn convert_radian_to_direction_short(mut radian: f64) -> [f32; 4] {
    if radian < 0.0 {
        radian += PI2;
    }
    radian -= PI05;
    if radian > PI {
        radian -= PI2;
    }
    let half = (radian as f32) * 0.5;
    // CreateFromYawPitchRoll(yaw, 0, 0): x = 0, y = sin(yaw/2), z = 0, w = cos(yaw/2)
    [0.0, half.sin(), 0.0, half.cos()]
}

/// `new Quaternion(rot.X, rot.Z, rot.Y, rot.W)` of a transfer's `Rot`.
fn transfer_rot(angle: f64) -> [f32; 4] {
    let q = convert_radian_to_direction_short(angle);
    [q[0], q[2], q[1], q[3]]
}

fn len3(v: [f32; 3]) -> f32 {
    (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]).sqrt()
}

fn normalize(v: [f32; 3]) -> [f32; 3] {
    let l = len3(v);
    [v[0] / l, v[1] / l, v[2] / l]
}

fn scale(v: [f32; 3], s: f32) -> [f32; 3] {
    [v[0] * s, v[1] * s, v[2] * s]
}

fn sub(a: [f32; 3], b: [f32; 3]) -> [f32; 3] {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}

fn add(a: [f32; 3], b: [f32; 3]) -> [f32; 3] {
    [a[0] + b[0], a[1] + b[1], a[2] + b[2]]
}

/// `Transfer.Truncate(steering, maxForce)`.
fn truncate(v: [f32; 3], max: f32) -> [f32; 3] {
    if len3(v) > max {
        scale(normalize(v), max)
    } else {
        v
    }
}

/// `MathUtil.GetDistance(v1, v2)` (2D).
fn distance_2d(a: [f32; 3], b: [f32; 3]) -> f32 {
    ((a[0] - b[0]).powi(2) + (a[1] - b[1]).powi(2)).sqrt()
}

fn point(p: &WorldSpawnPosition) -> [f32; 3] {
    [p.x, p.y, p.z]
}

/// Milliseconds since UTC midnight (`MoveType.Time` of `UseTransferBase`).
fn time_of_day_ms() -> u32 {
    (now_ms().rem_euclid(86_400_000)) as u32
}

// ---------------------------------------------------------------------------------------------
// TransferManager
// ---------------------------------------------------------------------------------------------

/// `TransferManager` of one instance (`_activeTransfers` in insertion order).
#[derive(Debug, Default)]
pub struct TransferManager {
    active: Mutex<Vec<TransferRef>>,
}

impl TransferManager {
    /// `GetTransfers()`.
    pub fn get_transfers(&self) -> Vec<TransferRef> {
        self.active.lock().expect("transfers poisoned").clone()
    }

    fn add(&self, t: &TransferRef) {
        let mut a = self.active.lock().expect("transfers poisoned");
        if a.iter().any(|x| x.obj_id == t.obj_id) {
            // `_activeTransfers.Add` of an existing key throws in C#.
            tracing::error!("TransferManager: transfer {} already active", t.obj_id);
            return;
        }
        a.push(t.clone());
    }
}

/// The transfer state of every instance (`world.TransferManager`) and the spawners.
#[derive(Debug, Default)]
pub struct TransferManagers {
    managers: DashMap<u32, Arc<TransferManager>>,
    spawners: DashMap<u32, Arc<TransferSpawner>>,
    /// `transfer.Spawner` (obj id -> spawner id).
    spawner_of: DashMap<u32, u32>,
}

impl TransferManagers {
    pub fn get(&self, instance_id: u32) -> Arc<TransferManager> {
        self.managers.entry(instance_id).or_default().clone()
    }

    pub fn all(&self) -> Vec<Arc<TransferManager>> {
        self.managers.iter().map(|e| e.value().clone()).collect()
    }

    /// `transfer.Spawner`.
    pub fn spawner_of(&self, obj_id: u32) -> Option<Arc<TransferSpawner>> {
        let id = *self.spawner_of.get(&obj_id)?;
        self.spawners.get(&id).map(|s| s.clone())
    }
}

/// `TransferTick(delta)`: `MoveTo` of every active transfer of every instance.
pub fn transfer_tick(server: &GameServer) {
    for m in server.systems.vehicles.transfers.all() {
        for t in m.get_transfers() {
            move_to(server, &t);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Create
// ---------------------------------------------------------------------------------------------

fn new_transfer(
    server: &GameServer,
    template: &TransferTemplate,
    world: &WorldInstance,
) -> Transfer {
    let mut t = Transfer::new(template.id);
    t.unit.name = template.name.clone();
    t.unit.model_id = template.model_id;
    t.unit.level = 1;
    t.unit.transform.instance_id = world.id;
    stats::refresh_pools(server, VehicleKind::Transfer, &mut t.unit);
    t.unit.hp = t.unit.max_hp;
    t.unit.mp = t.unit.max_mp;
    t.unit.faction_id = PC_FRIENDLY;
    t.unit.is_in_patrol = false;
    t
}

/// `TransferManager.Create(parentWorld, objectId, templateId, spawner)`: the cabin / motor,
/// its bound boarding part and that part's doodads (benches). Returns the motor.
pub fn create(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    obj_id: u32,
    template_id: u32,
    spawner: &Arc<TransferSpawner>,
) -> Option<TransferRef> {
    let data = &server.game_data.systems.vehicles;
    let carriage = data.transfer_template(template_id)?.clone();
    let manager = server.systems.vehicles.transfers.get(world.id);
    let tl_id = server.ids.tl.get_next_id().ok()? as u16;
    let obj_id = if obj_id == 0 {
        server.ids.object.get_next_id().ok()?
    } else {
        obj_id
    };
    let pos = spawner.def.base.position;
    let mut owner = new_transfer(server, &carriage, world);
    owner.unit.tl_id = tl_id;
    owner.unit.owner_id = 255;
    owner.attach_point_id = attach_point::SYSTEM;
    owner.bonding_obj_id = 0;
    owner.spawner_id = spawner.id;
    owner.unit.transform.zone_id = pos.zone_id;
    owner
        .unit
        .transform
        .set_position(pos.x, pos.y, pos.z, pos.roll, pos.pitch, pos.yaw);
    let owner = Entity::new(obj_id, owner);
    let owner_obj = WorldObject::from(&owner);
    server
        .systems
        .vehicles
        .transfers
        .spawner_of
        .insert(obj_id, spawner.id);
    // BUFF: Untouchable (Unable to attack this target)
    crate::systems::skills::buffs::add_buff_by_id(
        server,
        &owner_obj,
        buff::UNTOUCHABLE,
        &owner_obj,
        0,
    );
    let changes = world.spawn(&owner, &server.view_ctx());
    crate::world::session::handle_zone_changes(server, changes);
    manager.add(&owner);

    // Add additional transfer units if defined (like a Carriage/Boarding Part for example)
    if let Some(binding) = carriage.transfer_bindings.first() {
        let Some(boarding) = data.transfer_template(binding.transfer_id).cloned() else {
            tracing::error!(
                "Transfer {}: bound template {} missing (NullReferenceException)",
                carriage.id,
                binding.transfer_id
            );
            return Some(owner);
        };
        let part_obj_id = server.ids.object.get_next_id().ok()?;
        let mut part = new_transfer(server, &boarding, world);
        part.unit.tl_id = tl_id;
        part.unit.owner_id = obj_id;
        part.spawner_id = spawner.id;
        part.attach_point_id = binding.attach_point_id;
        part.bonding_obj_id = obj_id;
        part.unit.transform.zone_id = pos.zone_id;
        part.unit
            .transform
            .set_position(pos.x, pos.y, pos.z, pos.roll, pos.pitch, pos.yaw);
        super::slaves::add_distance_to_front(
            &mut part.unit.transform,
            BOUNDED_CHILD_ALONG_FRONT_OFFSET_METERS,
        );
        // the ground under the part (zone: transfer path rays 0x101, sub_3973EF70)
        let (px, py, pz) = (
            part.unit.transform.x,
            part.unit.transform.y,
            part.unit.transform.z,
        );
        let h =
            crate::systems::ai::collision::ground_height_or(server, world.id, px, py, pz, || {
                crate::systems::npcs::heightmap::world_height(server, world, px, py).unwrap_or(0.0)
            });
        part.unit.transform.set_height(h);
        let part = Entity::new(part_obj_id, part);
        let part_obj = WorldObject::from(&part);
        server
            .systems
            .vehicles
            .transfers
            .spawner_of
            .insert(part_obj_id, spawner.id);
        crate::systems::skills::buffs::add_buff_by_id(
            server,
            &part_obj,
            buff::UNTOUCHABLE,
            &part_obj,
            0,
        );
        owner.data().bounded_obj_id = part_obj_id;
        let changes = world.spawn(&part, &server.view_ctx());
        crate::world::session::handle_zone_changes(server, changes);
        manager.add(&part);

        for b in &boarding.transfer_binding_doodads {
            let Some(doodad) = crate::systems::npcs::doodad::manager::create(
                server,
                world,
                0,
                b.doodad_id,
                Some(&part_obj),
                false,
            ) else {
                tracing::error!("Transfer doodad template {} missing", b.doodad_id);
                continue;
            };
            {
                let mut d = doodad.data();
                d.parent_obj_id = part_obj_id;
                d.attach_point = b.attach_point_id;
                match b.attach_point_id {
                    attach_point::PASSENGER0 => d.transform.set_position(
                        0.005_374_76,
                        5.7852,
                        1.36648,
                        0.0,
                        0.0,
                        std::f32::consts::PI * 2.0,
                    ),
                    attach_point::PASSENGER1 => {
                        d.transform
                            .set_position(0.005_374_76, 1.63614, 1.36648, 0.0, 0.0, 0.0)
                    }
                    _ => {}
                }
                d.plant_time = chrono::Utc::now().timestamp();
                d.data = i32::from(b.attach_point_id);
                d.scale = 1.0;
                d.owner_type = crate::models::doodad::doodad_owner_type::SYSTEM;
            }
            let start = crate::systems::npcs::doodad::phase::get_func_group_id(
                &server.game_data.systems.npcs.doodad,
                b.doodad_id,
            );
            crate::systems::npcs::doodad::manager::set_func_group_id(server, &doodad, start);
            world.attach_child(&part_obj, &WorldObject::from(&doodad));
            crate::systems::npcs::doodad::manager::spawn(server, world, &doodad);
            part.data().attached_doodads.push(doodad.obj_id);
        }
        let hp = part.data().unit.hp;
        let owner_hp = owner.data().unit.hp;
        crate::systems::skills::combat::post_update_current_hp(
            server,
            &owner_obj,
            Some(&owner_obj),
            0,
            owner_hp,
            KillReason::Unknown,
        );
        crate::systems::skills::combat::post_update_current_hp(
            server,
            &part_obj,
            Some(&part_obj),
            0,
            hp,
            KillReason::Unknown,
        );
    }
    Some(owner)
}

// ---------------------------------------------------------------------------------------------
// TransferSpawner
// ---------------------------------------------------------------------------------------------

#[derive(Debug, Default)]
struct SpawnerState {
    spawned: Vec<u32>,
    last_spawn: Option<u32>,
    scheduled_count: i32,
    spawn_count: i32,
}

/// `TransferSpawner`.
#[derive(Debug)]
pub struct TransferSpawner {
    pub id: u32,
    pub world_id: u32,
    pub def: SimpleSpawnerDef,
    state: Mutex<SpawnerState>,
}

impl TransferSpawner {
    pub fn new(world_id: u32, def: SimpleSpawnerDef) -> Self {
        Self {
            id: def.base.id,
            world_id,
            def,
            state: Mutex::new(SpawnerState::default()),
        }
    }

    /// `SpawnAll()`.
    pub fn spawn_all(self: &Arc<Self>, server: &GameServer) -> Vec<TransferRef> {
        let mut list = Vec::new();
        let (from, count) = {
            let st = self.state.lock().expect("spawner poisoned");
            (st.scheduled_count, self.def.count as i32)
        };
        for _ in from..count {
            if let Some(t) = self.spawn(server, 0) {
                list.push(t);
            }
        }
        list
    }

    /// `Spawn(objId)`.
    pub fn spawn(self: &Arc<Self>, server: &GameServer, obj_id: u32) -> Option<TransferRef> {
        let world = server.world.world(self.world_id)?;
        let Some(transfer) = create(server, &world, obj_id, self.def.base.unit_id, self) else {
            tracing::warn!(
                "Transfer {}, from spawn not exist at db",
                self.def.base.unit_id
            );
            return None;
        };
        let pos = self.def.base.position;
        {
            let mut t = transfer.data();
            t.spawner_id = self.id;
            t.unit
                .transform
                .set_position(pos.x, pos.y, pos.z, pos.roll, pos.pitch, pos.yaw);
            t.unit.transform.zone_id = pos.zone_id;
        }
        let template_id = transfer.data().unit.template_id;
        if !NO_ROUTE_TEMPLATES.contains(&template_id) && !transfer.data().unit.is_in_patrol {
            let template = server
                .game_data
                .systems
                .vehicles
                .transfer_template(template_id)
                .cloned()
                .unwrap_or_default();
            let routes: Vec<Vec<WorldSpawnPosition>> = template
                .transfer_roads
                .iter()
                .map(|r| r.pos.clone())
                .collect();
            let path = routes.first().cloned().unwrap_or_default();
            if path.len() >= 2 {
                let (p0, p1) = (path[0], path[1]);
                let angle = calculate_direction(point(&p0), point(&p1));
                {
                    let mut t = transfer.data();
                    t.routes = routes;
                    t.transfer_path = path;
                    t.unit.is_in_patrol = true;
                    t.steering = 0;
                    t.path_point_index = 0;
                    t.angle = angle;
                    t.rot = transfer_rot(angle);
                    t.unit
                        .transform
                        .set_position(p0.x, p0.y, p0.z, p0.roll, p0.pitch, p0.yaw);
                    t.unit.transform.zone_id = p0.zone_id;
                }
                go_to_path(server, &transfer);
            } else {
                transfer.data().routes = routes;
                match template.transfer_all_paths.first() {
                    Some(p) => tracing::warn!("PathName: {} not found!", p.path_name),
                    None => tracing::error!(
                        "Transfer {template_id} has no paths (ArgumentOutOfRangeException)"
                    ),
                }
            }
        }
        let changes = world.spawn(&transfer, &server.view_ctx());
        crate::world::session::handle_zone_changes(server, changes);
        let mut st = self.state.lock().expect("spawner poisoned");
        st.last_spawn = Some(transfer.obj_id);
        st.spawned.push(transfer.obj_id);
        st.scheduled_count -= 1;
        st.spawn_count += 1;
        Some(transfer)
    }

    /// `Despawn(transfer)`.
    pub fn despawn(&self, server: &GameServer, transfer: &TransferRef) {
        if let Some(world) = server.world.world_of(&WorldObject::from(transfer)) {
            world.delete(transfer);
        }
        let mut st = self.state.lock().expect("spawner poisoned");
        if server
            .systems
            .npcs
            .spawns
            .respawn_field(transfer.obj_id)
            .is_none()
        {
            st.spawned.retain(|id| *id != transfer.obj_id);
            server.ids.object.release_id(transfer.obj_id);
            st.spawn_count -= 1;
        }
        if st.last_spawn.is_none_or(|l| l == transfer.obj_id) {
            st.last_spawn = st.spawned.last().copied();
        }
    }

    /// `DecreaseCount(transfer)`.
    pub fn decrease_count(&self, server: &GameServer, transfer: &TransferRef) {
        let Some(sm) = server.systems.npcs.spawns.get(self.world_id) else {
            return;
        };
        let now = crate::systems::npcs::spawn_manager::now();
        let respawn = {
            let mut st = self.state.lock().expect("spawner poisoned");
            st.spawn_count -= 1;
            st.spawned.retain(|id| *id != transfer.obj_id);
            let r = self.def.base.respawn_time > 0
                && st.spawn_count + st.scheduled_count < self.def.count as i32;
            if r {
                st.scheduled_count += 1;
            }
            r
        };
        let obj = WorldObject::from(transfer);
        if respawn {
            sm.add_respawn(
                server,
                obj.clone(),
                Some(crate::systems::npcs::spawn_manager::add_secs(
                    now,
                    f64::from(self.def.base.respawn_time),
                )),
            );
        }
        sm.add_despawn(
            server,
            obj,
            Some(crate::systems::npcs::spawn_manager::add_secs(
                now,
                f64::from(self.def.base.despawn_time),
            )),
        );
    }

    /// The spawned motor obj ids.
    pub fn spawned(&self) -> Vec<u32> {
        self.state.lock().expect("spawner poisoned").spawned.clone()
    }
}

/// The transfer part of `SpawnManager.SpawnAll` for one instance (`spawner.SpawnAll()` per
/// spawner). Returns the number of spawners processed.
pub fn spawn_all(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    defs: Vec<SimpleSpawnerDef>,
) -> usize {
    let mut count = 0;
    for def in defs {
        let s = Arc::new(TransferSpawner::new(world.id, def));
        server
            .systems
            .vehicles
            .transfers
            .spawners
            .insert(s.id, s.clone());
        s.spawn_all(server);
        count += 1;
        if count % 25 == 0 {
            tracing::debug!(
                "In world {} Transfers spawned: {count}...",
                world.template.name
            );
        }
    }
    tracing::info!(
        "In world {} Transfers spawned: {count}",
        world.template.name
    );
    count
}

// ---------------------------------------------------------------------------------------------
// Route following
// ---------------------------------------------------------------------------------------------

/// `StopMove(transfer)`.
fn stop_move(t: &mut Transfer) {
    t.throttle = 0;
    t.speed = 0.0;
    t.rot_speed = 0.0;
    t.velocity = [0.0; 3];
    t.v_velocity = [0.0; 3];
}

/// `GetMinCheckPoint(transfer, pointsList)` (`vTarget` / `vPosition` side effects included).
fn get_min_check_point(t: &mut Transfer, world_pos: [f32; 3]) -> i32 {
    if t.transfer_path.is_empty() {
        return -1;
    }
    let mut index = -1;
    let mut min_dist = 0.0f32;
    t.v_target = world_pos;
    for i in 0..t.transfer_path.len() {
        t.v_position = point(&t.transfer_path[i]);
        let delta = distance_2d(t.v_target, t.v_position);
        if delta > 200.0 {
            continue;
        }
        if index == -1 {
            index = i as i32;
            min_dist = delta;
        }
        if delta < min_dist {
            index = i as i32;
            min_dist = delta;
        }
    }
    index
}

/// `GoToPath(transfer)`.
pub fn go_to_path(server: &GameServer, transfer: &TransferRef) {
    let world_pos = {
        let t = transfer.data().unit.transform;
        [t.x, t.y, t.z]
    };
    let mut t = transfer.data();
    if t.transfer_path.is_empty() {
        return;
    }
    t.move_to_path_enabled = true;
    t.move_to_forward = true;
    let template = server
        .game_data
        .systems
        .vehicles
        .transfer_template(t.unit.template_id)
        .cloned()
        .unwrap_or_default();
    t.max_velocity_forward = template.path_smoothing + 1.6;
    t.speed = 0.0;
    if !t.move_to_path_enabled || !t.unit.is_in_patrol {
        stop_move(&mut t);
        return;
    }
    let i = get_min_check_point(&mut t, world_pos);
    if i < 0 {
        stop_move(&mut t);
        return;
    }
    t.move_to_path_enabled = true;
    t.move_step_index = i;
    t.v_target = point(&t.transfer_path[i as usize]);
    if t.move_step_index == 0 && t.steering == 0 && template.wait_time > 0.0 {
        t.wait_time_ms = now_ms() + (template.wait_time * 1000.0) as i64;
    }
}

/// `TransferData.UseTransferBase(transfer)` as an `SCOneUnitMovementPacket` move type.
fn transfer_move_type(t: &Transfer) -> MoveType {
    let tr = t.unit.transform;
    let mut mt = MoveType::new(MoveKind::Transfer(TransferMove {
        x: tr.x,
        y: tr.y,
        z: tr.z,
        world_pos: WorldPos::from_xyz(tr.x, tr.y, tr.z),
        velocity: t.velocity,
        rot: t.rot,
        ang_vel: t.ang_vel,
        steering: t.steering,
        path_point_index: t.path_point_index,
        speed: t.speed,
        reverse: t.reverse,
        rotation_degrees: tr.rot_z / std::f32::consts::PI * 180.0,
        rot_speed: t.rot_speed,
        throttle: t.throttle,
    }));
    mt.time = time_of_day_ms();
    mt
}

/// `Transfer.MoveTo(transfer)`: one 100 ms step toward the current checkpoint.
pub fn move_to(server: &GameServer, transfer: &TransferRef) {
    let obj = WorldObject::from(transfer);
    let Some(world) = server.world.world_of(&obj) else {
        return;
    };
    let now = now_ms();
    let (model_id, template_id) = {
        let mut t = transfer.data();
        if t.time_left(now) > 0 {
            return;
        }
        if !t.move_to_path_enabled || !t.unit.is_in_patrol {
            stop_move(&mut t);
            return;
        }
        (t.unit.model_id, t.unit.template_id)
    };
    let data = &server.game_data.systems.vehicles;
    let model_id = data
        .transfer_template(template_id)
        .map_or(model_id, |t| t.model_id);
    let Some(model) = data.vehicle_model(model_id).copied() else {
        return;
    };
    let (packet, bounded, pos, angle, rot) = {
        let mut t = transfer.data();
        let tr = t.unit.transform;
        t.v_position = [tr.x, tr.y, tr.z];
        t.max_velocity_forward = model.velocity;
        t.max_force = model.ang_vel;
        t.mass = model.wheeled_vehicle_mass;
        t.vel_accel = match model.wheeled_vehicle_max_gear {
            1 => model.wheeled_vehicle_gear_speed_ratio_1,
            2 => model.wheeled_vehicle_gear_speed_ratio_2,
            3 => model.wheeled_vehicle_gear_speed_ratio_3,
            _ => 0.3,
        };
        t.v_distance = sub(t.v_target, t.v_position);
        t.distance = len3(t.v_distance);
        if t.distance < t.range_to_check_point {
            t.v_position = t.v_target;
            drop(t);
            next_point(server, transfer);
            return;
        }
        let dir = normalize(t.v_distance);
        t.v_velocity = scale(dir, t.vel_accel);
        let desired = scale(dir, t.max_velocity_forward);
        let mut steering = sub(desired, t.v_velocity);
        steering = truncate(steering, t.max_force);
        steering = scale(steering, 1.0 / t.mass);
        t.v_velocity = truncate(add(t.v_velocity, steering), t.max_velocity_forward);
        t.v_position = add(t.v_position, t.v_velocity);
        t.speed = t.max_velocity_forward;
        t.velocity = scale(t.v_velocity, 5.0);
        t.angle = calculate_direction(t.v_position, t.v_target);
        if t.reverse {
            t.angle += std::f64::consts::PI;
            t.ang_vel = [t.max_velocity_forward, 0.0, 0.0];
        } else {
            t.ang_vel = [-t.max_velocity_forward, 0.0, 0.0];
        }
        let angle = t.angle;
        t.unit.transform.rot_z = angle as f32;
        t.rot = transfer_rot(angle);
        let packet = transfer_move_type(&t);
        (packet, t.bounded_obj_id, t.v_position, angle, t.rot)
    };
    // Only send movement of the main vehicle motor, client will drag carriage on its own
    // (`Bounded is not null || ParentObj is null`: every transfer here is top-level).
    broadcast(
        server,
        &obj,
        &SCOneUnitMovementPacket {
            obj_id: transfer.obj_id,
            move_type: packet,
        },
        false,
    );
    transfer
        .data()
        .unit
        .transform
        .set_position(pos[0], pos[1], pos[2], 0.0, 0.0, angle as f32);
    let changes = world.add_visible_object(&obj, &server.view_ctx());
    crate::world::session::handle_zone_changes(server, changes);

    // Keep bound carriage parts in sync server-side.
    if bounded != 0 {
        let Some(child) = world.transfer(bounded) else {
            return;
        };
        let a = angle as f32;
        let cx = BOUNDED_CHILD_ALONG_FRONT_OFFSET_METERS * a.cos() + pos[0];
        let cy = BOUNDED_CHILD_ALONG_FRONT_OFFSET_METERS * a.sin() + pos[1];
        let cz = pos[2];
        let child_packet = {
            let mut c = child.data();
            c.unit.transform.set_position(cx, cy, cz, 0.0, 0.0, a);
            c.rot = rot;
            transfer_move_type(&c)
        };
        let child_obj = WorldObject::from(&child);
        let changes = world.add_visible_object(&child_obj, &server.view_ctx());
        crate::world::session::handle_zone_changes(server, changes);
        broadcast(
            server,
            &child_obj,
            &SCOneUnitMovementPacket {
                obj_id: child.obj_id,
                move_type: child_packet,
            },
            false,
        );
    }
}

/// `TransferAllPaths[i]` of a transfer's template (`None` = the C# index throws).
fn path_at(template: &TransferTemplate, i: i32) -> Option<&super::data::TransferPath> {
    usize::try_from(i)
        .ok()
        .and_then(|i| template.transfer_all_paths.get(i))
}

/// `Transfer.NextPoint(transfer)`.
pub fn next_point(server: &GameServer, transfer: &TransferRef) {
    let template = {
        let id = transfer.data().unit.template_id;
        server
            .game_data
            .systems
            .vehicles
            .transfer_template(id)
            .cloned()
            .unwrap_or_default()
    };
    let now = now_ms();
    let mut t = transfer.data();
    if !t.move_to_path_enabled {
        stop_move(&mut t);
        return;
    }
    if t.transfer_path.is_empty() {
        return;
    }
    let oob = |what: &str| {
        tracing::error!(
            "Transfer {}: {what} out of range (ArgumentOutOfRangeException)",
            template.id
        )
    };
    let count = t.transfer_path.len() as i32;
    if template.cyclic {
        if t.move_step_index >= count - 1 {
            t.move_step_index = 0;
            if t.steering >= t.routes.len() as i32 - 1 {
                t.steering = 0;
                t.transfer_path = t.routes.first().cloned().unwrap_or_default();
                match t.transfer_path.first() {
                    Some(s) => t.v_target = point(s),
                    None => oob("TransferPath[0]"),
                }
            } else {
                t.steering += 1;
                let Some(route) = t.routes.get(t.steering as usize).cloned() else {
                    oob("Routes[Steering]");
                    return;
                };
                t.transfer_path = route;
                match t.transfer_path.first() {
                    Some(s) => t.v_target = point(s),
                    None => {
                        oob("TransferPath[0]");
                        return;
                    }
                }
                // a pause at the end of the road part (if any)
                if t.steering != 0 {
                    let Some(prev) = path_at(&template, t.steering - 1) else {
                        oob("TransferAllPaths[Steering - 1]");
                        return;
                    };
                    let pause = if prev.wait_time_end > 0.0 {
                        true
                    } else {
                        match path_at(&template, t.steering) {
                            Some(cur) => cur.wait_time_start > 0.0,
                            None => {
                                oob("TransferAllPaths[Steering]");
                                return;
                            }
                        }
                    };
                    if pause {
                        let time = if prev.wait_time_end > 0.0 {
                            prev.wait_time_end
                        } else {
                            path_at(&template, t.steering).map_or(0.0, |p| p.wait_time_start)
                        };
                        t.wait_time_ms = now + (time * 1000.0) as i64;
                        stop_move(&mut t);
                    }
                }
            }
        } else {
            // a pause at the start of the road part (if any)
            if t.move_step_index == 0 && t.steering == 0 {
                let Some(cur) = path_at(&template, t.steering) else {
                    oob("TransferAllPaths[Steering]");
                    return;
                };
                if cur.wait_time_start > 0.0 || template.wait_time > 0.0 {
                    let time = if cur.wait_time_start > 0.0 {
                        cur.wait_time_start
                    } else {
                        template.wait_time
                    };
                    t.wait_time_ms = now + (time * 1000.0) as i64;
                    stop_move(&mut t);
                }
            }
            t.move_step_index += 1;
            let s = t.transfer_path[t.move_step_index as usize];
            t.v_target = point(&s);
        }
    } else {
        // one road part: forward, then back
        t.steering = 0;
        let Some(cur) = path_at(&template, t.steering).cloned() else {
            oob("TransferAllPaths[Steering]");
            return;
        };
        if t.move_step_index >= count - 1 {
            t.reverse = true;
            let time = if cur.wait_time_start > 0.0 {
                cur.wait_time_start
            } else {
                template.wait_time
            };
            t.wait_time_ms = now + (time * 1000.0) as i64;
            stop_move(&mut t);
        }
        if t.move_step_index == 0 {
            t.reverse = false;
            let time = if cur.wait_time_end > 0.0 {
                cur.wait_time_end
            } else {
                cur.wait_time_start
            };
            t.wait_time_ms = now + (time * 1000.0) as i64;
        }
        if t.reverse {
            t.move_step_index -= 1;
        } else {
            t.move_step_index += 1;
        }
        match usize::try_from(t.move_step_index)
            .ok()
            .and_then(|i| t.transfer_path.get(i).copied())
        {
            Some(s) => t.v_target = point(&s),
            None => oob("TransferPath[MoveStepIndex]"),
        }
    }
}

/// `TransferManager.GetTransfers()` of an instance as telescope entries
/// (`Transfer.WriteTelescopeUnit`).
pub fn telescope_units(
    server: &GameServer,
    instance_id: u32,
) -> Vec<super::packets::TelescopeUnit> {
    let Some(world) = server.world.world(instance_id) else {
        return Vec::new();
    };
    server
        .systems
        .vehicles
        .transfers
        .get(instance_id)
        .get_transfers()
        .into_iter()
        .map(|t| {
            let p = world.world_pos_rot(&WorldObject::from(&t));
            let d = t.data();
            super::packets::TelescopeUnit {
                obj_id: t.obj_id,
                template_id: d.unit.template_id,
                pos: (p.x, p.y, p.z),
                name: d.unit.name.clone(),
            }
        })
        .collect()
}
