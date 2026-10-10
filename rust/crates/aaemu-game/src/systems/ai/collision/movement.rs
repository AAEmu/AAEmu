//! The zone's handling of a unit movement (`WZUnitMovement`, op 9, handler 0x39362E80): the
//! fall tracker (ZWUnitFell, [`super::fall`]) and the movement verifier (ZWReportMoveHack,
//! [`super::move_verify`]), fed from `world::session` for every client move.

use crate::models::movement::{MoveTypeActorFlags, UnitMove};
use crate::server::GameServer;
use crate::world::{WorldInstance, WorldObject};

use super::fall::zone_fall_vel;
use super::move_verify::MoveSample;

/// Called after a unit's client move was applied; returns the `fallVel` the world should use
/// for `DoFallDamage` (0 = no fall damage). Without the collision service the client value is
/// returned unchanged (C#).
pub fn on_unit_moved(
    server: &GameServer,
    world: &WorldInstance,
    unit: &WorldObject,
    time_ms: u32,
    u: &UnitMove,
) -> u16 {
    let cfg = &server.config.physics;
    let Some(pw) = super::phys_world(server, world.id) else {
        return u.fall_vel;
    };
    let sys = &server.systems.ai.collision;
    let obj_id = unit.obj_id();
    let now = crate::world::now_ms();
    if cfg.movement_verify_enable {
        let terrain_z = pw
            .terrain
            .as_ref()
            .map_or(0.0, |t| super::terrain::terrain_elevation(t.as_ref(), u.x, u.y));
        sys.move_verify.verify(
            obj_id,
            MoveSample {
                x: u.x,
                y: u.y,
                z: u.z,
                client_ms: i64::from(time_ms),
                server_ms: now,
                terrain_z,
                exempt: unit.core().sticky_parent() != 0,
            },
        );
    }
    // ZWUnitFell is sent only for unit kinds 0/1/2/5 (characters, NPCs, mates) and never while
    // swimming, levitating or on a ship.
    if !(unit.as_player().is_some() || unit.as_npc().is_some() || unit.as_mate().is_some()) {
        return u.fall_vel;
    }
    let on_object = unit.core().sticky_parent() != 0
        || u
            .actor_flags
            .contains(MoveTypeActorFlags::HANGING_FROM_OBJECT);
    if on_object || super::is_water(server, world.id, u.x, u.y, u.z) {
        sys.fall.reset(obj_id);
        return if on_object { 0 } else { u.fall_vel };
    }
    let start_z = sys.fall.start_z(obj_id).unwrap_or(u.z);
    let ground = super::ground_height(server, world.id, u.x, u.y, u.z);
    sys.fall.sample(obj_id, u.z, ground, now);
    if u.fall_vel == 0 || !cfg.fall_damage_zone_cap {
        return u.fall_vel;
    }
    let land_z = ground.unwrap_or(u.z).min(u.z);
    zone_fall_vel(u.fall_vel, start_z.max(u.z), land_z).unwrap_or(0)
}
