//! The NPC gimmick movement of `FlytrapAttackBehavior` (its private `MoveInRange` /
//! `Npc.Gimmick`) and `Gimmick.MoveTowards` / `RotateBarrel`.
//!
//! `npc.Gimmick` is the gimmick a `SpawnGimmickEffect` cast by the NPC created
//! (`GimmickSpawner.Spawn(caster)`): the latest active gimmick whose `SpawnerUnitId` is the NPC.
//! Note (C#): `Gimmick.CurrentTarget` is never assigned by any code, so the flytrap only moves
//! its gimmick when something else sets the gimmick unit's target.

use std::collections::HashMap;
use std::sync::Mutex;

use crate::server::GameServer;
use crate::systems::npcs::gimmick::manager::ActiveGimmick;
use crate::systems::npcs::gimmick::packets::SCGimmickMovementPacket;
use crate::systems::skills::controllers::{
    add_distance_to_front_between, calculate_distance, Vec3,
};
use crate::world::{GimmickRef, WorldObject};

use super::framework::AiCtx;
use super::numerics::{deg_to_rad, Quat, Vec3Ext};

/// The members `Gimmick` keeps for `MoveTowards` (`Target`, `_pitch`, `_yaw`, `_roll`).
#[derive(Debug, Clone, Copy, Default)]
pub struct GimmickMove {
    pub target: Vec3,
    pitch: f32,
    yaw: f32,
    roll: f32,
}

/// Per gimmick obj id.
#[derive(Debug, Default)]
pub struct GimmickMoves(Mutex<HashMap<u32, GimmickMove>>);

impl GimmickMoves {
    pub fn get(&self, id: u32) -> GimmickMove {
        self.0
            .lock()
            .expect("gimmick moves poisoned")
            .get(&id)
            .copied()
            .unwrap_or_default()
    }

    pub fn set(&self, id: u32, m: GimmickMove) {
        self.0.lock().expect("gimmick moves poisoned").insert(id, m);
    }
}

/// `npc.Gimmick`.
pub fn npc_gimmick(
    server: &GameServer,
    npc_obj: &WorldObject,
) -> Option<std::sync::Arc<ActiveGimmick>> {
    let world_id = npc_obj.transform().instance_id;
    server
        .systems
        .npcs
        .gimmicks
        .for_world(world_id)
        .active()
        .into_iter()
        .filter(|g| g.gimmick.data().spawner_unit_id == npc_obj.obj_id())
        .last()
}

/// `Gimmick.MoveTowards(other, distance, distanceZ)`.
pub fn gimmick_move_towards(
    server: &GameServer,
    active: &ActiveGimmick,
    other: Vec3,
    distance: f32,
    distance_z: f32,
) {
    let g: &GimmickRef = &active.gimmick;
    let obj = WorldObject::from(g);
    let local = {
        let t = g.data().unit.transform;
        Vec3::new(t.x, t.y, t.z)
    };
    let target_dist = calculate_distance(local, other, true);
    let travel = target_dist.min(distance);
    let new = add_distance_to_front_between(travel, target_dist, local, other);
    // RotateBarrel: advance the angles, rotate by the previous ones
    let mut st = server.systems.ai.gimmick_moves.get(g.obj_id);
    let (p, y, r) = (st.pitch, st.yaw, st.roll);
    st.pitch = (st.pitch + active.spawner.velocity_x) % 360.0;
    st.yaw = (st.yaw + active.spawner.velocity_y) % 360.0;
    st.roll = (st.roll + active.spawner.velocity_z) % 360.0;
    server.systems.ai.gimmick_moves.set(g.obj_id, st);
    let q = Quat::from_yaw_pitch_roll(deg_to_rad(p), deg_to_rad(y), deg_to_rad(r));
    let (rx, ry, rz) = crate::models::transform::PositionAndRotation::rotation_from_quaternion(
        crate::models::transform::Quaternion {
            x: q.x,
            y: q.y,
            z: q.z,
            w: q.w,
        },
    );
    let packet = {
        let mut d = g.data();
        d.unit
            .transform
            .set_position(new.x, new.y, new.z, rx, ry, rz);
        d.vel = [0.0, 0.0, -distance_z];
        d.ang_vel = [0.0, 0.0, 0.0];
        SCGimmickMovementPacket::new(g.obj_id, super::npc::ms_of_day(), &d)
    };
    active.state().time = super::npc::ms_of_day();
    let moved = new != local;
    super::npc::check_moved_position(server, &obj);
    if moved {
        crate::systems::skills::broadcast(server, &obj, &packet, false);
    }
}

/// `FlytrapAttackBehavior.MoveInRange(target, delta)` (the gimmick follows the target).
pub fn flytrap_move_in_range(
    c: &mut AiCtx<'_>,
    active: &ActiveGimmick,
    target: &WorldObject,
    delta_ms: i64,
) {
    let gimmick_pos = {
        let t = active.gimmick.data().unit.transform;
        Vec3::new(t.x, t.y, t.z)
    };
    let mut gm = c.server.systems.ai.gimmick_moves.get(active.gimmick.obj_id);
    let target_pos = super::npc::world_pos(c.server, target);
    if gm.target.is_zero_vec() {
        gm.target = target_pos;
        c.server
            .systems
            .ai
            .gimmick_moves
            .set(active.gimmick.obj_id, gm);
    }
    let final_point = gm.target;
    let range = 0.1f32;
    let dt = (delta_ms.rem_euclid(1000)) as f32 / 1000.0;
    // Gimmick.BaseMoveSpeed is Unit's 1
    let move_distance = dt + 1.0;
    let gravity = active.template.as_ref().map_or(0.0, |t| t.gravity);
    let move_distance_z = gravity * dt;
    let distance_to_target = calculate_distance(gimmick_pos, gm.target, true);
    if c.server.config.world.geo_data_mode {
        // find the path to the abuser only if the target coordinates have changed
        let end = c.ai.path_node.end_point_pos;
        if c.ai.path_node.found_path.is_empty()
            && (end - target_pos).length() <= super::npc::model_size(c.server, &c.obj)
        {
            super::geodata::npc_find_path(c, target);
            c.ai.path_node.end_point_pos = target_pos;
            gm.target = target_pos;
            c.server
                .systems
                .ai
                .gimmick_moves
                .set(active.gimmick.obj_id, gm);
        }
        let next = c.ai.path_node.found_path.front().copied();
        if let Some(next) = next.filter(|n| !n.is_zero_vec()) {
            let _ = next;
            let route_point = c.ai.path_node.position;
            let d = calculate_distance(gimmick_pos, route_point, true);
            if d > range {
                gimmick_move_towards(
                    c.server,
                    active,
                    route_point,
                    move_distance,
                    move_distance_z,
                );
            } else {
                if c.ai.path_node.found_path.is_empty() {
                    super::npc::stop_movement(c.server, &c.npc);
                    c.ai.path_node.found_path.clear();
                    return;
                }
                if let Some(p) = c.ai.path_node.found_path.pop_front() {
                    c.ai.path_node.current_target_pos = p;
                }
            }
            return;
        }
    }
    if distance_to_target > range {
        gimmick_move_towards(
            c.server,
            active,
            final_point,
            move_distance,
            move_distance_z,
        );
    } else {
        crate::systems::npcs::gimmick::manager::stop_movement(c.server, active);
    }
}
