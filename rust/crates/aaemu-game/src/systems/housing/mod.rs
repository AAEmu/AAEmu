//! Phase-3 subsystem: player housing.
//!
//! C# scope: `Core/Managers/HousingManager` (all of it), `GameData/HousingGameData`,
//! `Core/Managers/TaxationsManager` (the `taxations` table), `Models/Game/Housing/**` (the
//! `House` behaviour beyond the shared `models::house` data), `Models/Tasks/Housing/
//! HousingTaxTask`, `Models/Game/Mails/MailForTax` (the tax mail content), the housing packets
//! (`Core/Packets/C2G/CS*House*`, `Core/Packets/G2C/SC*House*`, `SCLoginCharInfoHouse`,
//! `SCConstructHouseTax`, `SCHousingRecoverToggle`), the `Building` branch of `CraftEffect`,
//! `House.OnDeath`, and the housing parts of `SpawnManager.Load` / `SpawnPersistentDoodads`,
//! `GameConnection.LoadAccount`, `CSListCharacter`, `CSSelectCharacter`, `Character.Load` and
//! `CharacterManager.DeleteCharacterAssets`.
//!
//! | module | C# |
//! |---|---|
//! | [`data`] | `HousingGameData`, `taxations`, `housing_bindings*.json` |
//! | [`manager`] | `HousingManager` state, `House` model behaviour, load / save, taxes |
//! | [`ops`] | the player operations (build, sale, demolish, decorate, ...) and `CraftEffect` |
//! | [`packets`] | C->S / S->C housing packets |
//! | [`db`] | the `housings` table |
//! | [`hooks`] | items / money / mail services from the economy subsystem |

pub mod data;
pub mod db;
pub mod hooks;
pub mod manager;
pub mod ops;
pub mod packets;
pub mod placement;

#[cfg(test)]
mod access_tests;
#[cfg(test)]
mod lifecycle_tests;
#[cfg(test)]
mod manager_tests;
#[cfg(test)]
mod tests;

use std::sync::Arc;

use crate::net::connection::GameConnection;
use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::skills::data::{EffectKind, EffectTemplate};
use crate::systems::skills::effects::EffectArgs;
use crate::systems::SaveStage;
use crate::world::{HouseRef, PlayerRef, WorldInstance, WorldObject};

/// Game-data tables of this subsystem (`server.game_data.systems.housing`).
pub type Data = data::HousingData;

/// Managers of this subsystem (`server.systems.housing`).
#[derive(Debug, Default)]
pub struct System {
    /// Serializes housing payments, sales and furniture placement/recovery.
    pub(crate) operation_lock: std::sync::Mutex<()>,
    /// `HousingManager`.
    pub manager: manager::HousingManager,
    /// Items / money / mail (installed by the economy subsystem).
    pub hooks: hooks::HousingHookSlot,
}

impl System {
    /// Startup. The houses themselves are loaded by `SpawnManager.Load` of `main_world`
    /// ([`load_for_world`], called by the npcs subsystem before the persistent doodads), as in
    /// the C#.
    pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
        // Housing rows are loaded with NPC spawns, before economy hooks and persisted mail.
        // Start billing only after those subsystems have finished loading their state.
        manager::start_tax_task(server);
        Ok(())
    }

    /// `SaveManager.DoSave`: `HousingManager.Save` runs first (`SaveStage::BeforeItems`).
    pub async fn save(
        &self,
        server: &GameServer,
        tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        stage: SaveStage,
    ) -> crate::Result<usize> {
        if stage != SaveStage::BeforeItems {
            return Ok(0);
        }
        let (updated, deleted) = manager::save(server, tx).await?;
        if updated + deleted > 0 {
            tracing::debug!("Updated {updated} and deleted {deleted} houses ...");
        }
        Ok(updated + deleted)
    }
}

/// Registers the housing C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    packets::register(reg);
}

// ---------------------------------------------------------------------------------------------
// entry points used by other modules
// ---------------------------------------------------------------------------------------------

/// `SpawnManager.Load` housing part: `LoadPlayerHousing(World)` + `SpawnAll()` (houses before
/// the persistent doodads). Returns the number of houses.
pub async fn load_for_world(
    server: &Arc<GameServer>,
    world: &Arc<WorldInstance>,
) -> anyhow::Result<usize> {
    tracing::info!("Loading player housing for {}", world.template.name);
    let n = manager::load_player_housing(server, world).await?;
    // Houses need to be spawned before doodads
    manager::spawn_all(server);
    Ok(n)
}

/// `SpawnManager.Load`: `ReconcileBoundDoodads()` after the housing doodads were loaded (only
/// with `UsePersistentHouseDoodads`).
pub fn after_housing_doodads_loaded(server: &GameServer) {
    if server.config.world.use_persistent_house_doodads {
        manager::reconcile_bound_doodads(server);
    }
}

/// `SpawnPersistentDoodads` for a doodad with `house_id > 0` and no parent doodad. False when
/// the house is unknown ("Unable to place doodad ...").
pub fn attach_loaded_doodad(
    server: &GameServer,
    world: &WorldInstance,
    house_id: u32,
    doodad: &crate::world::DoodadRef,
) -> bool {
    manager::attach_loaded_doodad(server, world, house_id, doodad)
}

/// `HousingManager.GetHouseById(houseId)`.
pub fn house_by_id(server: &GameServer, id: u32) -> Option<HouseRef> {
    server.systems.housing.manager.by_id(id)
}

/// `HousingManager.GetHouseAtLocation(x, y)`.
pub fn house_at_location(server: &GameServer, x: f32, y: f32) -> Option<HouseRef> {
    manager::house_at_location(server, x, y)
}

/// House land at a position in a specific world instance.
pub fn house_at_location_in_world(
    server: &GameServer,
    instance_id: u32,
    x: f32,
    y: f32,
) -> Option<HouseRef> {
    manager::house_at_location_in_world(server, instance_id, x, y)
}

/// `House.AllowedToInteract(player)`.
pub fn allowed_to_interact(server: &GameServer, house: &HouseRef, player: &PlayerRef) -> bool {
    manager::allowed_to_interact(server, house, player)
}

/// `GetHouseById(id)?.AllowedToInteract(player)` (`None` = no such house).
pub fn allowed_to_interact_by_id(
    server: &GameServer,
    house_id: u32,
    player: &PlayerRef,
) -> Option<bool> {
    house_by_id(server, house_id).map(|h| allowed_to_interact(server, &h, player))
}

/// `HousingManager.Demolish(character.Connection, house, false, false)` (`Demolish` world
/// interaction).
pub fn demolish_by_player(server: &GameServer, player: &PlayerRef, house: &HouseRef) {
    ops::demolish(server, Some(player), house, false, false);
}

/// `HousingManager.UpdateOwnedHousingFaction(characterId, factionId)`.
pub fn update_owned_housing_faction(server: &GameServer, character_id: u32, faction_id: u32) {
    manager::update_owned_housing_faction(server, character_id, faction_id);
}

/// `HousingManager.PayWeeklyTax(GetHouseById(houseId))` (paying a tax bill; mail subsystem).
pub fn pay_weekly_tax(server: &GameServer, house_id: u32) -> bool {
    house_by_id(server, house_id).is_some_and(|h| manager::pay_weekly_tax(server, &h))
}

/// `HousingManager.GetActAbilityBonusFromHouse(GetHouseAtLocation(x, y))` (crafting).
pub fn act_ability_bonus_at(server: &GameServer, actability_group_id: i32, x: f32, y: f32) -> u32 {
    house_at_location(server, x, y).map_or(0, |h| {
        manager::act_ability_bonus_from_house(server, actability_group_id, &h)
    })
}

/// Housing proficiency bonus in the player's current world instance.
pub fn act_ability_bonus_at_in_world(
    server: &GameServer,
    instance_id: u32,
    actability_group_id: i32,
    x: f32,
    y: f32,
) -> u32 {
    house_at_location_in_world(server, instance_id, x, y).map_or(0, |h| {
        manager::act_ability_bonus_from_house(server, actability_group_id, &h)
    })
}

/// `House.OnDeath` (`Unit.DoDie` → `Events.OnDeath`): `RemoveDeadHouse`.
pub fn on_unit_died(server: &GameServer, victim: &WorldObject) {
    if let Some(house) = victim.as_house() {
        {
            let h = house.data();
            tracing::debug!(
                "House died ObjId:{} - TemplateId:{} - {}",
                house.obj_id,
                h.unit.template_id,
                h.unit.name
            );
        }
        manager::remove_dead_house(server, house);
    }
}

/// `CSListCharacterPacket`: one `SCLoginCharInfoHouse(house.OwnerId, house)` per house of the
/// account (`Connection.Houses`, filled by `LoadAccount`).
pub fn send_login_houses(server: &GameServer, conn: &mut GameConnection) {
    for house in server.systems.housing.manager.by_account(conn.account_id()) {
        let owner = house.data().unit.owner_id;
        conn.send(&ops::login_char_info_house_packet(server, owner, &house));
    }
}

/// `CSSelectCharacterPacket`: `SCMyHousePacket` for every house of the account owned by the
/// selected character.
pub fn send_my_houses(server: &GameServer, conn: &mut GameConnection, character_id: u32) {
    for house in server.systems.housing.manager.by_account(conn.account_id()) {
        if house.data().unit.owner_id == character_id {
            conn.send(&ops::my_house_packet(server, &house));
        }
    }
}

/// `Character.Load()`: "Update sync housing factions on login".
pub fn on_character_loaded(server: &GameServer, character_id: u32, faction_id: u32) {
    update_owned_housing_faction(server, character_id, faction_id);
}

/// `CharacterManager.DeleteCharacterAssets` housing part: owned houses become public and
/// expire (the tax timer demolishes them).
pub fn delete_character_assets(server: &GameServer, character_id: u32) {
    for house in server.systems.housing.manager.by_character(character_id) {
        {
            let template = manager::template_of(server, &house);
            let mut h = house.data();
            manager::set_permission(
                &mut h,
                template,
                crate::models::house::housing_permission::PUBLIC,
            );
            // force expire the house
            h.protection_end_date = manager::now() - 21 * 24 * 3600;
            h.is_dirty = true;
        }
        manager::update_tax_info(server, &house);
    }
}

/// `BaseUnit.GetDistanceTo`: the house part (`GardenRadius * Scale` instead of the model
/// radius). `None` for other objects.
pub fn house_radius(server: &GameServer, obj: &WorldObject) -> Option<f32> {
    let house = obj.as_house()?;
    let (template_id, scale) = {
        let h = house.data();
        (h.unit.template_id, h.unit.scale)
    };
    let r = server
        .game_data
        .systems
        .housing
        .template(template_id)
        .map_or(0.0, |t| t.garden_radius);
    Some(r * scale)
}

/// The `CraftEffect` `Building` branch (routed from `ExtHooks::route_effect` before the other
/// subsystems): a character building a house. `true` = handled.
pub fn apply_effect(
    server: &GameServer,
    kind: EffectKind,
    template: &EffectTemplate,
    args: &EffectArgs<'_>,
) -> bool {
    let _ = kind;
    let EffectTemplate::Craft {
        world_interaction, ..
    } = template
    else {
        return false;
    };
    let group = server
        .game_data
        .systems
        .npcs
        .area_trigger
        .get_world_interaction_group(*world_interaction);
    if group != Some(crate::systems::npcs::area_trigger::world_interaction_group::BUILDING) {
        return false;
    }
    let Some(player) = args.caster.as_ref().and_then(|c| c.as_player()).cloned() else {
        return false;
    };
    let crate::systems::skills::geometry::Target::Obj(target) = &args.target else {
        return false;
    };
    let Some(house) = target.as_house().cloned() else {
        return false;
    };
    tracing::debug!("CraftEffect, {world_interaction}");
    let used_skill = match args.cast {
        crate::systems::skills::types::CastAction::Skill { skill_id, .. } => skill_id,
        _ => 0,
    };
    if ops::craft_building(server, &player, &house, used_skill) {
        let template_id = house.data().unit.template_id;
        crate::systems::quests::events::on_doodad_interaction(
            server,
            &player,
            &player,
            template_id,
        );
    }
    true
}
