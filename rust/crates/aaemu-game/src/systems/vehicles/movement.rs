//! The vehicle branches of `CSMoveUnitPacket.Execute` (`ShipRequestMoveType`,
//! `VehicleMoveType`, the mate / riding / standing-on parts of `UnitMoveType`), called by
//! `world::session::move_unit`.

use crate::models::movement::{
    get_slave_rotation_in_degrees, MoveType, MoveTypeFlags, ShipRequestMove, UnitMove, VehicleMove,
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
    // TODO (C#): Validate if we are in the driver seat
    let Some(ship) = target.as_slave() else {
        return;
    };
    {
        let mut d = ship.data();
        d.throttle_request = r.throttle;
        d.steering_request = r.steering;
    }
    super::ship_physics::set_request(server, ship, r.throttle, r.steering);
    // Make sure driver is attached to the ship
    attach_if_needed(server, character, target);
}

fn attach_if_needed(server: &GameServer, character: &PlayerRef, parent: &WorldObject) {
    let me = WorldObject::from(character);
    if me.transform().parent_obj_id == parent.obj_id() {
        return;
    }
    if let Some(world) = super::world_of(server, &me) {
        if me.transform().parent_obj_id != 0 {
            world.detach_child(&me);
        }
        world.attach_child(parent, &me);
    }
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
    // TODO (C#): Validate if we are in the driver seat
    let Some(car) = target.as_slave() else {
        return;
    };
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

/// The standing-on-object part of `UnitMoveType` (`MoveTypeFlags.StandingOnObject` of the move
/// header + `GcId`): parents the moving unit to the object it stands on (ship decks), or
/// detaches it.
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

pub fn standing_on(world: &WorldInstance, target: &WorldObject, mt: &MoveType, u: &UnitMove) {
    let standing = mt.flags.contains(MoveTypeFlags::STANDING_ON_OBJECT);
    // GcId 1 probably means "current parent" and is ignored.
    let parent = if standing && u.gc_id > 1 {
        world.object(u.gc_id).filter(|o| !o.same(target))
    } else {
        None
    };
    let current = target.transform().parent_obj_id;
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
