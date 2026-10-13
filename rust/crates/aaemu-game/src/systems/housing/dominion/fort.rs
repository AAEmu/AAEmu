//! Original 1.2 expedition fortifications. Ordinary properties keep their character owner.
//! Placement/capture/destruction callers hold housing.operation_lock across this work.

use crate::models::house::housing_permission;
use crate::server::GameServer;
use crate::systems::economy::error_message as err;
use crate::systems::housing::data::{GuardTowerSetting, HousingTemplate};
use crate::systems::housing::manager;
use crate::world::{HouseRef, PlayerRef, WorldObject};

use super::state::DominionState;

pub const FORTIFICATION_BUILD_SKILL: u32 = 17497;
// MySQL DATETIME's last representable second. Fortifications have no weekly tax deadline.
pub const FORT_PROTECTION_END: i64 = 253_402_300_799;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FortKind {
    Tower,
    Wall,
    Stairs,
    Gate,
}

/// Usable original designs, excluding removed prototypes and zero-skill test items.
pub fn design(template: &HousingTemplate) -> Option<(u32, FortKind)> {
    let (item, category, kind) = match template.id {
        119 => (15795, 2, FortKind::Tower),
        120 => (15796, 2, FortKind::Tower),
        121 => (15797, 3, FortKind::Wall),
        26 => (16024, 4, FortKind::Stairs),
        135 => (16028, 14, FortKind::Gate),
        169 => (19681, 2, FortKind::Tower),
        168 => (19682, 2, FortKind::Tower),
        170 => (19683, 14, FortKind::Gate),
        158 => (19684, 3, FortKind::Wall),
        _ => return None,
    };
    (template.category_id == category).then_some((item, kind))
}

/// A castle category cannot fall through to ordinary-property placement.
pub fn castle_category(category: u32) -> bool {
    matches!(category, 2..=5 | 14)
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct FortOwner {
    pub expedition_id: u32,
    pub character_id: u32,
    pub account_id: u32,
    pub faction_id: u32,
}

pub fn owner(server: &GameServer, state: &DominionState) -> Option<FortOwner> {
    let expedition = server.expeditions.get(state.owner_expedition_id)?;
    if expedition.is_disbanded || expedition.faction.owner_id == 0 {
        return None;
    }
    let character_id = expedition.faction.owner_id;
    let account_id = server.names.get_character_account(character_id);
    if account_id == 0 || server.names.get_character_name(character_id).is_none() {
        return None;
    }
    Some(FortOwner {
        expedition_id: state.owner_expedition_id,
        character_id,
        account_id,
        faction_id: expedition.faction.mother_id,
    })
}

/// Only the currently registered live lodestone in the same world/zone authorizes a fort.
pub fn state_at(server: &GameServer, instance_id: u32, x: f32, y: f32) -> Option<DominionState> {
    if !x.is_finite() || !y.is_finite() {
        return None;
    }
    let world = server.world.world(instance_id)?;
    let zone = world.template.zone_id_at(x, y);
    let group = server.game_data.zones.zone_by_key(zone)?.group_id;
    let state = server.systems.housing.dominions.get(group)?;
    if state.owner_expedition_id == 0 || state.tower.instance_id != instance_id {
        return None;
    }
    let tower = server.systems.housing.manager.by_id(state.tower.house_id)?;
    if !super::runtime::is_live_object(server, &WorldObject::from(&tower))
        || super::runtime::tower_identity(server, &tower)? != (group, state.tower)
    {
        return None;
    }
    let radius = server
        .game_data
        .systems
        .housing
        .castles
        .guard_tower_settings
        .get(&state.tower.guard_tower_setting_id)?
        .radius_dominion as f32;
    let pos = tower.data().unit.transform.local();
    let distance2 = (x - pos.x).powi(2) + (y - pos.y).powi(2);
    (radius > 0.0 && distance2.is_finite() && distance2 < radius * radius).then_some(state)
}

pub fn state_for_house(server: &GameServer, house: &HouseRef) -> Option<DominionState> {
    let (template_id, transform) = {
        let h = house.data();
        (h.unit.template_id, h.unit.transform)
    };
    design(server.game_data.systems.housing.template(template_id)?)?;
    state_at(server, transform.instance_id, transform.x, transform.y)
}

pub fn is_fort(server: &GameServer, house: &HouseRef) -> bool {
    manager::template_of(server, house).is_some_and(|template| design(template).is_some())
}

pub fn allowed_to_interact(server: &GameServer, house: &HouseRef, player: &PlayerRef) -> bool {
    let Some(state) = state_for_house(server, house) else {
        return false;
    };
    let (character, instance) = {
        let c = player.data();
        (c.id, c.unit.transform.instance_id)
    };
    house.data().unit.owner_id != 0
        && instance == state.tower.instance_id
        && server.expeditions.expedition_of_character(character) == state.owner_expedition_id
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct FortCounts {
    pub gates: u32,
    pub walls: u32,
}

pub fn counts(server: &GameServer, state: &DominionState, completed_only: bool) -> FortCounts {
    let mut counts = FortCounts::default();
    for house in server.systems.housing.manager.all() {
        let (template_id, owner, step, hp) = {
            let h = house.data();
            (
                h.unit.template_id,
                h.unit.owner_id,
                h.current_step,
                h.unit.hp,
            )
        };
        if completed_only && (owner == 0 || hp <= 0 || step != -1) {
            continue;
        }
        let Some(template) = server.game_data.systems.housing.template(template_id) else {
            continue;
        };
        let transform = house.data().unit.transform;
        if !state_at(server, transform.instance_id, transform.x, transform.y)
            .is_some_and(|s| s.tower == state.tower)
        {
            continue;
        }
        match template.category_id {
            14 => counts.gates += 1,
            2 | 3 => counts.walls += 1,
            _ => {}
        }
    }
    counts
}

/// Returns None for ordinary housing; castle prototypes are explicitly rejected.
pub fn placement_owner(
    server: &GameServer,
    player: &PlayerRef,
    template: &HousingTemplate,
    x: f32,
    y: f32,
) -> Result<Option<FortOwner>, i16> {
    if !castle_category(template.category_id) {
        return Ok(None);
    }
    let (item_id, kind) = design(template).ok_or(err::CASTLE_CANNOT_LOCATE_INVALID_AREA)?;
    if !server
        .game_data
        .items
        .template(item_id)
        .is_some_and(|item| item.use_skill_id == FORTIFICATION_BUILD_SKILL)
        || server
            .game_data
            .systems
            .housing
            .item_id_by_design(template.id, |id| id == item_id)
            != item_id
    {
        return Err(err::CASTLE_CANNOT_LOCATE_INVALID_AREA);
    }
    let (character, instance) = {
        let c = player.data();
        (c.id, c.unit.transform.instance_id)
    };
    let state =
        state_at(server, instance, x, y).ok_or(err::HOUSE_CANNOT_LOCATE_NOT_DOMINATED_ZONE)?;
    if server.expeditions.expedition_of_character(character) != state.owner_expedition_id {
        return Err(err::HOUSE_CANNOT_LOCATE_NEAR_OTHERS_GUARD_HOUSE);
    }
    let owner = owner(server, &state).ok_or(err::CASTLE_CANNOT_LOCATE_INVALID_AREA)?;
    let setting = &server
        .game_data
        .systems
        .housing
        .castles
        .guard_tower_settings[&state.tower.guard_tower_setting_id];
    let counts = counts(server, &state, false);
    let exceeded = match kind {
        FortKind::Gate => counts.gates >= setting.max_gates,
        FortKind::Tower | FortKind::Wall => counts.walls >= setting.max_walls,
        FortKind::Stairs => false,
    };
    if exceeded {
        return Err(err::EXCEED_MAX_DOMINION_GATE_OR_WALL);
    }
    Ok(Some(owner))
}

/// Used after placement and after load/capture. All values are persisted by HouseRow.
pub fn apply_owner(server: &GameServer, house: &HouseRef, owner: FortOwner) {
    let (previous, changed, tl) = {
        let mut h = house.data();
        let previous = h.unit.owner_id;
        let changed = previous != owner.character_id
            || h.account_id != owner.account_id
            || h.co_owner_id != owner.character_id
            || h.unit.expedition_id != owner.expedition_id
            || h.permission != housing_permission::GUILD
            || h.tax != 0
            || h.protection_end_date != FORT_PROTECTION_END
            || h.sell_price != 0
            || h.sell_to_player_id != 0;
        h.unit.owner_id = owner.character_id;
        h.co_owner_id = owner.character_id;
        h.account_id = owner.account_id;
        h.unit.expedition_id = owner.expedition_id;
        h.permission = housing_permission::GUILD;
        h.tax = 0;
        h.protection_end_date = FORT_PROTECTION_END;
        h.sell_price = 0;
        h.sell_to_player_id = 0;
        h.is_dirty |= changed;
        (previous, changed, h.unit.tl_id)
    };
    if changed && super::runtime::is_live_object(server, &WorldObject::from(house)) {
        if let Some(player) = server.world.character_by_id(previous) {
            player.send(crate::systems::housing::packets::SCMyHouseRemovedPacket { tl });
        }
        if let Some(player) = server.world.character_by_id(owner.character_id) {
            player.send(crate::systems::housing::ops::my_house_packet(server, house));
        }
        manager::broadcast(
            server,
            house,
            &crate::packets::world_objects::SCHouseStatePacket {
                body: crate::systems::housing::ops::house_body(server, house),
            },
        );
    }
    if house.data().unit.faction_id != owner.faction_id {
        manager::update_house_faction(server, house, owner.faction_id);
    }
}

pub fn refresh_owner(server: &GameServer, house: &HouseRef) {
    if house.data().unit.owner_id == 0 {
        return;
    }
    if let Some(owner) = state_for_house(server, house).and_then(|state| owner(server, &state)) {
        apply_owner(server, house, owner);
    }
}

/// Surviving forts follow the captured Dominion. Ordinary houses are never selected.
pub fn transfer_captured(server: &GameServer, state: &DominionState) {
    if !server
        .systems
        .housing
        .dominions
        .get(state.zone_group_id)
        .is_some_and(|current| {
            current.tower == state.tower
                && current.owner_expedition_id == state.owner_expedition_id
                && current.ownership_generation == state.ownership_generation
        })
    {
        return;
    }
    let Some(owner) = owner(server, state) else {
        return;
    };
    for house in server.systems.housing.manager.all() {
        if house.data().unit.owner_id != 0
            && state_for_house(server, &house).is_some_and(|s| s.tower == state.tower)
        {
            apply_owner(server, &house, owner);
            manager::update_tax_info(server, &house);
        }
    }
    refresh_protection_buff(server, state);
}

/// Call after Dominion authority loads, before the tax timer and client logins start.
pub fn reconcile_all(server: &GameServer) {
    for state in server.systems.housing.dominions.snapshots() {
        transfer_captured(server, &state);
    }
}

pub fn protection_buff(setting: &GuardTowerSetting, counts: FortCounts) -> u32 {
    setting
        .steps
        .values()
        .rev()
        .find(|step| counts.gates >= step.num_gates && counts.walls >= step.num_walls)
        .map_or(setting.initial_buff_id, |step| step.buff_id)
}

/// Exactly one authored refuge stage is active; a destroyed tower retains only Conversion.
pub fn refresh_protection_buff(server: &GameServer, state: &DominionState) {
    let Some(tower) = server.systems.housing.manager.by_id(state.tower.house_id) else {
        return;
    };
    if super::runtime::tower_identity(server, &tower) != Some((state.zone_group_id, state.tower)) {
        return;
    }
    let Some(setting) = server
        .game_data
        .systems
        .housing
        .castles
        .guard_tower_settings
        .get(&state.tower.guard_tower_setting_id)
    else {
        return;
    };
    let selected = if state.tower_destroyed {
        0
    } else {
        protection_buff(setting, counts(server, state, true))
    };
    let object = WorldObject::from(&tower);
    for buff in std::iter::once(setting.initial_buff_id)
        .chain(setting.steps.values().map(|step| step.buff_id))
    {
        if buff != 0 && buff != selected {
            crate::systems::skills::buffs::remove_buff(server, &object, buff);
        }
    }
    if selected != 0 && !tower.data().unit.buffs.check_buff(selected) {
        crate::systems::skills::buffs::add_buff_by_id(server, &object, selected, &object, 0);
    }
}

pub fn refresh_for_house(server: &GameServer, house: &HouseRef) {
    if let Some(state) = state_for_house(server, house) {
        if super::runtime::is_live_object(server, &WorldObject::from(house)) {
            refresh_owner(server, house);
            manager::update_tax_info(server, house);
        }
        refresh_protection_buff(server, &state);
    }
}
