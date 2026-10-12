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
    // A gcFlags=1 deck update carries primary coordinates in the supporting
    // object's frame. Session already applied that local transform: compose it
    // here instead of placing the physics proxy near the world origin.
    let parented = unit.transform().parent_obj_id != 0;
    let (x, y, z) = if parented {
        let pos = world.world_pos_rot(unit);
        (pos.x, pos.y, pos.z)
    } else if u
        .actor_flags
        .contains(MoveTypeActorFlags::STANDING_ON_OBJECT)
        && u.gc_flags & 1 != 0
    {
        // A late frame after the supporting object vanished still has a world
        // backup; its primary deck offset is no longer a world position.
        (u.x2, u.y2, u.z2)
    } else {
        (u.x, u.y, u.z)
    };
    let on_object = parented
        || unit.core().sticky_parent() != 0
        || u.actor_flags
            .contains(MoveTypeActorFlags::STANDING_ON_OBJECT)
        || u.actor_flags
            .contains(MoveTypeActorFlags::HANGING_FROM_OBJECT);
    super::living::sync_one(server, world.id, unit, [x, y, z]);
    let now = crate::world::now_ms();
    if cfg.movement_verify_enable {
        let terrain_z = pw
            .terrain
            .as_ref()
            .map_or(0.0, |t| super::terrain::terrain_elevation(t.as_ref(), x, y));
        sys.move_verify.verify(
            obj_id,
            MoveSample {
                x,
                y,
                z,
                client_ms: i64::from(time_ms),
                server_ms: now,
                terrain_z,
                exempt: on_object,
            },
        );
    }
    // ZWUnitFell is sent only for unit kinds 0/1/2/5 (characters, NPCs, mates) and never while
    // swimming, levitating or on a ship.
    if !(unit.as_player().is_some() || unit.as_npc().is_some() || unit.as_mate().is_some()) {
        return u.fall_vel;
    }
    if on_object || super::is_water(server, world.id, x, y, z) {
        sys.fall.reset(obj_id);
        // the tracker (path a) does not run there; path b forwards the client value
        return u.fall_vel;
    }
    let start_z = sys.fall.start_z(obj_id).unwrap_or(z);
    let ground = super::ground_height(server, world.id, x, y, z);
    sys.fall.sample(obj_id, z, ground, now);
    if u.fall_vel == 0 || !cfg.fall_damage_zone_cap {
        return u.fall_vel;
    }
    let land_z = ground.unwrap_or(z).min(z);
    zone_fall_vel(u.fall_vel, start_z.max(z), land_z).unwrap_or(0)
}
