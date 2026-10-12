//! The vehicle branches of `CSMoveUnitPacket.Execute` (`ShipRequestMoveType`,
//! `VehicleMoveType`, the mate / riding / standing-on parts of `UnitMoveType`), called by
//! `world::session::move_unit`.

use crate::models::movement::{
    get_slave_rotation_in_degrees, MoveType, MoveTypeActorFlags, MoveTypeFlags, ShipRequestMove,
    UnitMove, VehicleMove,
};
use crate::packets::world::SCOneUnitMovementPacket;
use crate::server::GameServer;
use crate::world::{MateRef, PlayerRef, WorldInstance, WorldObject};

use super::{broadcast, mates};

/// `ShipRequestMoveType`: the driver's throttle / steering request (the physics moves the ship
/// and sends the movement packets).
pub fn ship_request(
    server: &GameServer,
    character: &PlayerRef,
    target: &WorldObject,
    r: &ShipRequestMove,
) {
    let Some(ship) = target.as_slave() else {
        return;
    };
    if !controls_slave(character, ship) {
        return;
    }
    {
        let mut d = ship.data();
        d.throttle_request = r.throttle;
        d.steering_request = r.steering;
    }
    super::ship_physics::set_request(server, ship, r.throttle, r.steering);
    // Make sure driver is attached to the ship
    attach_if_needed(server, character, target);
}

fn controls_slave(character: &PlayerRef, slave: &crate::world::SlaveRef) -> bool {
    if character.data().attached_point != super::data::attach_point::DRIVER {
        return false;
    }
    slave
        .data()
        .attached_characters
        .iter()
        .any(|(point, id)| *point == super::data::attach_point::DRIVER && *id == character.obj_id)
}

fn attach_if_needed(server: &GameServer, character: &PlayerRef, parent: &WorldObject) {
    let me = WorldObject::from(character);
    if me.transform().parent_obj_id == parent.obj_id() {
        return;
    }
    if let Some(world) = super::world_of(server, &me) {
        crate::systems::npcs::doodad::funcs::world::attach_keeping_world(&world, parent, &me);
    }
}

/// A seated slave passenger follows the server's attachment until the discard packet.
/// A late walking update must not detach the driver before the next steering request.
pub fn player_bound_to_slave(world: &WorldInstance, player: &PlayerRef) -> bool {
    let (parent, point) = {
        let c = player.data();
        (c.unit.transform.parent_obj_id, c.attached_point)
    };
    point != super::data::attach_point::NONE
        && world.slave(parent).is_some_and(|s| {
            s.data()
                .attached_characters
                .iter()
                .any(|(p, id)| *p == point && *id == player.obj_id)
        })
}

/// `VehicleMoveType`: a land vehicle driven by the client.
pub fn vehicle_move(
    server: &GameServer,
    world: &WorldInstance,
    character: &PlayerRef,
    target: &WorldObject,
    mt: &MoveType,
    v: &VehicleMove,
) {
    let Some(car) = target.as_slave() else {
        return;
    };
    if !controls_slave(character, car) {
        return;
    }
    let (rx, ry, rz) = get_slave_rotation_in_degrees(v.rotation_x, v.rotation_y, v.rotation_z);
    attach_if_needed(server, character, target);
    car.data()
        .unit
        .transform
        .set_position(v.x, v.y, v.z, rx, ry, rz);
    broadcast(
        server,
        target,
        &SCOneUnitMovementPacket {
            obj_id: car.obj_id,
            move_type: mt.clone(),
        },
        true,
    );
    // FinalizeTransform: propagate position updates to all children
    let changes = world.add_visible_object(target, &server.view_ctx());
    crate::world::session::handle_zone_changes(server, changes);
}

/// The `targetUnit is Mate` part of `UnitMoveType`: buff removal, the mate XP task, the
/// passengers' buff removal.
pub fn on_mate_unit_move(server: &GameServer, character: &PlayerRef, mate: &MateRef, u: &UnitMove) {
    let obj = WorldObject::from(mate);
    remove_effects(server, &obj, u);
    // TODO (C#): Check if we're the owner, or allowed to otherwise control this pet
    if u.vel_x != 0 || u.vel_y != 0 {
        mates::start_update_xp(server, character, mate);
    } else {
        mates::stop_update_xp(mate);
    }
    let passengers = mate.data().passengers.clone();
    for (_, info) in passengers {
        if let Some(p) = server.world.character_by_obj_id(info.obj_id) {
            remove_effects(server, &WorldObject::from(&p), u);
        }
    }
}

/// `CSMoveUnitPacket.RemoveEffects(unit, moveType)`.
fn remove_effects(server: &GameServer, obj: &WorldObject, u: &UnitMove) {
    if u.vel_x != 0 || u.vel_y != 0 || u.vel_z != 0 {
        crate::systems::skills::buffs::trigger_remove_on(
            server,
            obj,
            crate::systems::skills::types::BuffRemoveOn::Move,
            0,
        );
        crate::systems::dispatch_buff_events(server, obj);
    }
}

/// The `player.IsRiding` part of `UnitMoveType` for a character target: a riding character
/// without a parent is re-attached to its first active mate; `true` = the movement is ignored.
pub fn player_riding(server: &GameServer, character: &PlayerRef, player: &PlayerRef) -> bool {
    if !player.data().is_riding {
        return false;
    }
    let me = WorldObject::from(player);
    if me.transform().parent_obj_id == 0 {
        let (instance, char_id) = {
            let c = character.data();
            (c.unit.transform.instance_id, c.id)
        };
        let mate = server
            .systems
            .vehicles
            .mates
            .get(instance)
            .get_active_mates(char_id)
            .into_iter()
            .next();
        if let Some(m) = mate {
            if let Some(world) = super::world_of(server, &me) {
                world.attach_child(&WorldObject::from(&m), &me);
            }
        }
    }
    tracing::debug!(
        "{} IsRiding, ignoring movement request",
        player.data().unit.name
    );
    true
}

/// `CSMoveUnitPacket.IsBoardedOnTransfer(unit)`: a character whose sticky parent is a transfer
/// that lists it in `AttachedCharacters`.
pub fn is_boarded_on_transfer(world: &WorldInstance, unit: &WorldObject) -> bool {
    if unit.as_player().is_none() {
        return false;
    }
    world
        .sticky_parent(unit)
        .and_then(|s| s.as_transfer().cloned())
        .is_some_and(|t| t.data().attached_characters.contains(&unit.obj_id()))
}

/// Parents the moving unit to its supporting object, or detaches it. The actor's
/// standing flag carries the GC block independently of the movement header: a
/// controller update can retain that block while clearing the header flag.
pub fn standing_on(world: &WorldInstance, target: &WorldObject, mt: &MoveType, u: &UnitMove) {
    let standing = mt.flags.contains(MoveTypeFlags::STANDING_ON_OBJECT)
        || u.actor_flags
            .contains(MoveTypeActorFlags::STANDING_ON_OBJECT);
    let current = target.transform().parent_obj_id;
    // GcId 1 means the current parent, not a request to detach from it.
    let parent_id = if u.gc_id == 1 { current } else { u.gc_id };
    let parent = if standing && parent_id != 0 {
        world.object(parent_id).filter(|o| !o.same(target))
    } else {
        None
    };
    match (current, parent) {
        (c, None) if c != 0 => {
            // No longer standing on object
            world.detach_child(target);
        }
        (0, Some(p)) => world.attach_child(&p, target),
        (c, Some(p)) if c != p.obj_id() => {
            world.detach_child(target);
            world.attach_child(&p, target);
        }
        _ => {}
    }
}
