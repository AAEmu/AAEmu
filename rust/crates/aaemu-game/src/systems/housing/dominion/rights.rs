//! Ordinary property rights in the original Auroria provinces. These checks never transfer
//! a property's character owner: the existing return/mail path keeps that original owner.
//! Placement and demolition callers hold housing.operation_lock through their mutation.

use crate::server::GameServer;
use crate::systems::economy::error_message as err;
use crate::world::{HouseRef, PlayerRef, WorldObject};

use super::state::DominionState;
use super::{fort, runtime};

fn current_member(server: &GameServer, player: &PlayerRef, expedition_id: u32) -> bool {
    expedition_id != 0
        && player.data().unit.expedition_id == expedition_id
        && server
            .expeditions
            .expedition_of_character(player.character_id)
            == expedition_id
        && server
            .expeditions
            .get(expedition_id)
            .is_some_and(|e| !e.is_disbanded)
}

/// Validate the province's retained authority even outside its castle radius. A missing or
/// replaced tower must not make an owned province fall through to unrestricted housing.
fn province_state(
    server: &GameServer,
    instance_id: u32,
    x: f32,
    y: f32,
) -> Result<Option<DominionState>, i16> {
    if !x.is_finite() || !y.is_finite() {
        return Err(err::HOUSE_CANNOT_LOCATE_INVALID_AREA);
    }
    let world = server
        .world
        .world(instance_id)
        .ok_or(err::HOUSE_CANNOT_LOCATE_INVALID_AREA)?;
    let group = server
        .game_data
        .zones
        .zone_by_key(world.template.zone_id_at(x, y))
        .ok_or(err::HOUSE_CANNOT_LOCATE_INVALID_AREA)?
        .group_id;
    if !server
        .game_data
        .systems
        .housing
        .castles
        .siege_zones
        .contains_key(&group)
    {
        return Ok(None);
    }
    let state = server
        .systems
        .housing
        .dominions
        .get(group)
        .ok_or(err::HOUSE_CANNOT_LOCATE_NOT_DOMINATED_ZONE)?;
    let tower = server
        .systems
        .housing
        .manager
        .by_id(state.tower.house_id)
        .ok_or(err::HOUSE_CANNOT_LOCATE_NOT_DOMINATED_ZONE)?;
    let radius = server
        .game_data
        .systems
        .housing
        .castles
        .guard_tower_settings
        .get(&state.tower.guard_tower_setting_id)
        .map_or(0, |s| s.radius_dominion);
    if state.owner_expedition_id == 0
        || state.tower.instance_id != instance_id
        || radius == 0
        || !runtime::is_live_object(server, &WorldObject::from(&tower))
        || runtime::tower_identity(server, &tower) != Some((group, state.tower))
        || !server
            .expeditions
            .get(state.owner_expedition_id)
            .is_some_and(|e| !e.is_disbanded && e.faction.owner_id != 0)
    {
        return Err(err::HOUSE_CANNOT_LOCATE_NOT_DOMINATED_ZONE);
    }
    let position = tower.data().unit.transform;
    if !position.x.is_finite() || !position.y.is_finite() {
        return Err(err::HOUSE_CANNOT_LOCATE_NOT_DOMINATED_ZONE);
    }
    Ok(Some(state))
}

/// Ordinary housing: original 1.2 native placement39334060/ordinary branch393341E9 rejects
/// an unclaimed authored siege province with340, then another guild inside its castle with341.
/// Normal provinces are unaffected. This is an additional gate, not full placement validation.
pub fn placement_allowed(
    server: &GameServer,
    player: &PlayerRef,
    instance_id: u32,
    x: f32,
    y: f32,
    garden_extent: f32,
) -> Result<(), i16> {
    if !garden_extent.is_finite()
        || garden_extent < 0.0
        || player.data().unit.transform.instance_id != instance_id
        || !runtime::is_live_object(server, &WorldObject::from(player))
    {
        return Err(err::HOUSE_CANNOT_LOCATE_INVALID_AREA);
    }
    let Some(state) = province_state(server, instance_id, x, y)? else {
        return Ok(());
    };
    let tower = server
        .systems
        .housing
        .manager
        .by_id(state.tower.house_id)
        .ok_or(err::HOUSE_CANNOT_LOCATE_NOT_DOMINATED_ZONE)?;
    let position = tower.data().unit.transform;
    let radius = server
        .game_data
        .systems
        .housing
        .castles
        .guard_tower_settings
        .get(&state.tower.guard_tower_setting_id)
        .map_or(0, |s| s.radius_dominion);
    // Native39334371 rejects an outsider's footprint intersecting the castle circle.
    // Placement gardens use an axis-aligned square. Compare its nearest point to the tower
    // in f64 so finite f32 coordinates/extents cannot overflow during bound arithmetic.
    let (x, y, extent) = (f64::from(x), f64::from(y), f64::from(garden_extent));
    let (tx, ty) = (f64::from(position.x), f64::from(position.y));
    let nearest_x = tx.clamp(x - extent, x + extent);
    let nearest_y = ty.clamp(y - extent, y + extent);
    let overlaps = (tx - nearest_x).powi(2) + (ty - nearest_y).powi(2) < f64::from(radius).powi(2);
    if overlaps && !current_member(server, player, state.owner_expedition_id) {
        return Err(err::HOUSE_CANNOT_LOCATE_NEAR_OTHERS_GUARD_HOUSE);
    }
    Ok(())
}

/// An additional demolition entitlement for the current ruling guild's leader, confined to
/// the retained live property's exact castle boundary. Ordinary owner entitlement is handled
/// by the caller. Trion's original2014 Auroria launch guide confirms no transfer on capture.
pub fn can_demolish_ordinary(server: &GameServer, player: &PlayerRef, house: &HouseRef) -> bool {
    let (id, owner, template_id, transform) = {
        let h = house.data();
        (h.id, h.unit.owner_id, h.unit.template_id, h.unit.transform)
    };
    if owner == 0
        || player.data().unit.transform.instance_id != transform.instance_id
        || !runtime::is_live_object(server, &WorldObject::from(player))
        || !runtime::is_live_object(server, &WorldObject::from(house))
        || !server
            .systems
            .housing
            .manager
            .by_id(id)
            .is_some_and(|retained| std::sync::Arc::ptr_eq(&retained, house))
        || !server
            .game_data
            .systems
            .housing
            .template(template_id)
            .is_some_and(|t| t.guard_tower_setting_id == 0 && !fort::castle_category(t.category_id))
    {
        return false;
    }
    let Some(state) = fort::state_at(server, transform.instance_id, transform.x, transform.y)
    else {
        return false;
    };
    current_member(server, player, state.owner_expedition_id)
        && server
            .expeditions
            .get(state.owner_expedition_id)
            .is_some_and(|e| e.faction.owner_id == player.character_id)
}
