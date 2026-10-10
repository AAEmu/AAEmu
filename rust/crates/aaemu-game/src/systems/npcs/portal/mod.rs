//! `PortalManager` (`Core/Managers/PortalManager.cs`) + the behaviour of `CharacterPortals`
//! that is not part of the phase-2 load / save (`GetPortalInfo`, `RemoveFromBookPortal`,
//! `NotifySubZone`, `AddPrivatePortal`, `ChangePrivatePortalName`, `Send`), the summoned
//! `Portal` NPC (`Models/Game/Units/Portal`: `Delete`, `KillLinkedPortal`, `KillPortalTask`),
//! `WorldManager.ResendVisibleObjectsToCharacter` (used by `CSTeleportEnded`) and the portal /
//! teleport packets.
//!
//! The recall portals (`recalls.json`) and `district_return_points` stay in
//! `server.game_data.portals` (`data::world_data::PortalData`, phase 2); [`PortalTables`] holds
//! the rest of `PortalManager.Load`.

pub mod data;
pub mod packets;

use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::Duration;

pub use data::{OpenPortalReagents, PortalIndex, PortalTables};

use crate::data::world_data::Portal;
use crate::models::char_data::{CharacterPortals, VisitedDistrict};
use crate::models::npc::{Npc, PortalInfo};
use crate::models::transform::Transform;
use crate::net::registry::PacketRegistry;
use crate::packets::world::{
    PortalWire, SCCharacterPortalsPacket, SCCharacterReturnDistrictsPacket, SCErrorMsgPacket,
};
use crate::packets::world_objects::SCDoodadsCreatedPacket;
use crate::server::GameServer;
use crate::world::object::Entity;
use crate::world::{session, visibility, NpcRef, PlayerRef, WorldObject};
use packets::{
    SCLoadInstancePacket, SCPortalInfoSavedPacket, SCTeleportUnitPacket, SCUnitDeathPacket,
    KILL_REASON_PORTAL_TIMEOUT,
};

/// Template id of the green portal entrance NPC.
pub const PORTAL_ENTRANCE_TEMPLATE_ID: u32 = 3891;
/// Template id of the yellow portal exit NPC.
pub const PORTAL_EXIT_TEMPLATE_ID: u32 = 6949;
/// `KillPortalTask` delay.
pub const PORTAL_LIFETIME: Duration = Duration::from_secs(30);
/// `BuffConstants.TagOverburdened`.
pub const TAG_OVERBURDENED: u32 = 831;
/// `ItemTaskType.Teleport`.
pub const ITEM_TASK_TELEPORT: u8 = 23;

/// `ErrorMessageType` values used here.
pub mod error_message {
    pub const CANNOT_USE_PORTAL_WITH_BACKPACK: i16 = 610;
    pub const CANNOT_USE_PORTAL_IN_TRIAL: i16 = 611;
    pub const SLAVE_EQUIPMENT_LOADED_ITEM: i16 = 801;
}

/// `SkillObjectPortalInfo` (the skill object `OpenPortalEffect` receives).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct PortalEffectObject {
    pub kind: u8,
    pub id: i32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
}

/// Book entries removed since the last save (`CharacterPortals._removedPrivatePortals` /
/// `_removedVisitedDistricts`) of one character.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct PendingRemovals {
    pub private_portals: Vec<u32>,
    pub visited_districts: Vec<u32>,
}

/// Runtime state of `PortalManager`: the portal book deletions waiting for the next save.
#[derive(Debug, Default)]
pub struct PortalManager {
    pending: Mutex<HashMap<u32, PendingRemovals>>,
}

impl PortalManager {
    /// The pending deletions of `character_id` (tests / diagnostics).
    pub fn pending_removals(&self, character_id: u32) -> Option<PendingRemovals> {
        self.pending
            .lock()
            .expect("portal pending poisoned")
            .get(&character_id)
            .cloned()
    }

    fn record(&self, character_id: u32, f: impl FnOnce(&mut PendingRemovals)) {
        let mut p = self.pending.lock().expect("portal pending poisoned");
        f(p.entry(character_id).or_default());
    }
}

/// Startup: nothing to do (the tables are loaded with the game data).
pub async fn start(_server: &Arc<GameServer>) -> anyhow::Result<()> {
    Ok(())
}

/// C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    packets::register(reg);
}

/// This module's part of `SaveManager.DoSave`: `CharacterPortals.Save`'s deletions
/// (`DELETE FROM portal_visited_district / portal_book_coords ... IN(...)`); the remaining book
/// rows are written by the character save (`db_character::save_character`).
pub async fn save(
    server: &GameServer,
    tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
    stage: crate::systems::SaveStage,
) -> crate::Result<usize> {
    if stage != crate::systems::SaveStage::AfterCharacters {
        return Ok(0);
    }
    let pending: Vec<(u32, PendingRemovals)> = {
        let mut p = server
            .systems
            .npcs
            .portals
            .pending
            .lock()
            .expect("portal pending poisoned");
        p.drain().collect()
    };
    let mut rows = 0;
    for (owner, removed) in pending {
        if !removed.visited_districts.is_empty() {
            let sql = format!(
                "DELETE FROM portal_visited_district WHERE owner = ? AND subzone IN({})",
                join_ids(&removed.visited_districts)
            );
            sqlx::query(&sql).bind(owner).execute(&mut **tx).await?;
            rows += removed.visited_districts.len();
        }
        if !removed.private_portals.is_empty() {
            let sql = format!(
                "DELETE FROM portal_book_coords WHERE owner = ? AND id IN({})",
                join_ids(&removed.private_portals)
            );
            sqlx::query(&sql).bind(owner).execute(&mut **tx).await?;
            rows += removed.private_portals.len();
        }
    }
    Ok(rows)
}

fn join_ids(ids: &[u32]) -> String {
    ids.iter().map(u32::to_string).collect::<Vec<_>>().join(",")
}

// ------------------------------------------------------------------------------------------
// Lookups
// ------------------------------------------------------------------------------------------

fn tables(server: &GameServer) -> &PortalTables {
    &server.game_data.systems.npcs.portal
}

/// `GetRecallBySubZoneId`.
pub fn recall_by_sub_zone(server: &GameServer, sub_zone_id: u32) -> Option<&[Portal]> {
    server.game_data.portals.recall_by_sub_zone(sub_zone_id)
}

/// `GetRecallById(returnPointId)`.
pub fn recall_by_id(server: &GameServer, return_point_id: u32) -> Option<&Portal> {
    let key = tables(server).recalls_key.get(&return_point_id)?;
    recall_by_sub_zone(server, *key)?
        .iter()
        .find(|p| p.id == return_point_id)
}

/// The `ZoneId` `PortalManager.Load` gives a respawn point:
/// `GetZoneId(GetWorldTemplateByName("main_world"), X, Y)`.
pub fn respawn_zone(server: &GameServer, p: &Portal) -> u32 {
    let worlds = &server.game_data.worlds;
    worlds.get_zone_id(worlds.get_world_template_by_name("main_world"), p.x, p.y)
}

fn with_respawn_zone(server: &GameServer, p: &Portal) -> Portal {
    Portal {
        zone_id: respawn_zone(server, p),
        ..p.clone()
    }
}

/// `GetRespawnBySubZoneId`.
pub fn respawn_by_sub_zone(server: &GameServer, sub_zone_id: u32) -> Option<Portal> {
    let p = tables(server).respawns.by_sub_zone(sub_zone_id)?;
    Some(with_respawn_zone(server, p))
}

/// `GetRespawnById`.
pub fn respawn_by_id(server: &GameServer, id: u32) -> Option<Portal> {
    let p = tables(server).respawns.by_id(id)?;
    Some(with_respawn_zone(server, p))
}

/// `GetWorldGatesBySubZoneId`.
pub fn world_gates_by_sub_zone(server: &GameServer, sub_zone_id: u32) -> Option<&Portal> {
    tables(server).world_gates.by_sub_zone(sub_zone_id)
}

/// `GetWorldGatesById`.
pub fn world_gates_by_id(server: &GameServer, id: u32) -> Option<&Portal> {
    tables(server).world_gates.by_id(id)
}

/// `GetDistrictReturnPoint(districtId)` (any faction; 0 when none).
pub fn district_return_point_any(server: &GameServer, district_id: u32) -> u32 {
    server
        .game_data
        .portals
        .district_return_points
        .iter()
        .find(|p| p.district_id == district_id)
        .map_or(0, |p| p.return_point_id)
}

/// `GetDistrictReturnPoint(districtId, factionId)`.
pub fn district_return_point(server: &GameServer, district_id: u32, faction_id: u32) -> u32 {
    server
        .game_data
        .portals
        .district_return_point(district_id, faction_id)
}

/// `MathUtil.CalculateDistance(a, b)` (2D, computed in double).
fn distance_2d(ax: f32, ay: f32, bx: f32, by: f32) -> f32 {
    let dx = f64::from(ax) - f64::from(bx);
    let dy = f64::from(ay) - f64::from(by);
    (dx * dx + dy * dy).sqrt() as f32
}

/// `ZoneManager.GetTargetIdByZoneId(zoneKey)`.
fn target_id_by_zone(server: &GameServer, zone_key: u32) -> u32 {
    let zones = &server.game_data.zones;
    zones
        .zone_by_key(zone_key)
        .and_then(|z| zones.group(z.group_id))
        .map_or(0, |g| g.target_id)
}

/// The world position of a player (`Transform.World.Position`).
fn player_world_position(server: &GameServer, p: &PlayerRef) -> (f32, f32, f32) {
    let inst = p.data().unit.transform.instance_id;
    match server.world.world(inst) {
        Some(w) if p.region().is_some() => {
            let pos = w.world_pos_rot(&WorldObject::from(p));
            (pos.x, pos.y, pos.z)
        }
        _ => {
            let c = p.data();
            (c.unit.transform.x, c.unit.transform.y, c.unit.transform.z)
        }
    }
}

/// `GetClosestReturnPortal(character)`: the closest respawn point allowed for the character's
/// faction (district-specific respawns) in an open zone (non-GMs); the current position as a
/// fail-safe.
pub fn closest_return_portal(server: &GameServer, player: &PlayerRef) -> Portal {
    let (x, y, z) = player_world_position(server, player);
    let (zone_id, faction_id, access_level) = {
        let c = player.data();
        (c.unit.transform.zone_id, c.unit.faction_id, c.access_level)
    };
    // `Faction.MotherId` (an unknown faction is null in C#; Rust uses 0, see PORTING.md).
    let mother_id = server
        .game_data
        .factions
        .get(faction_id)
        .map_or(0, |f| f.mother_id);
    let mut distance = 999_999f32;
    let mut portal = Portal {
        x,
        y,
        z,
        zone_id,
        ..Portal::default()
    };
    let districts = &server.game_data.portals.district_return_points;
    for value in &tables(server).respawns.portals {
        let factions: Vec<u32> = districts
            .iter()
            .filter(|d| d.return_point_id == value.id)
            .map(|d| d.faction_id)
            .collect();
        if !factions.is_empty() && !factions.contains(&mother_id) && !factions.contains(&faction_id)
        {
            continue;
        }
        let value_zone = respawn_zone(server, value);
        if access_level < 100 {
            match server.game_data.zones.zone_by_key(value_zone) {
                None => continue,
                Some(zone) if zone.closed => continue,
                Some(_) => {}
            }
        }
        let dist = distance_2d(x, y, value.x, value.y);
        if dist >= distance {
            continue;
        }
        distance = dist;
        portal = Portal {
            zone_id: value_zone,
            ..value.clone()
        };
    }
    portal
}

// ------------------------------------------------------------------------------------------
// CharacterPortals behaviour
// ------------------------------------------------------------------------------------------

/// `CharacterPortals.GetPortalInfo(id)`: district portals first, then private ones.
pub fn portal_info(portals: &CharacterPortals, id: u32) -> Option<&Portal> {
    portals
        .district_portals
        .iter()
        .find(|p| p.id == id)
        .or_else(|| portals.private_portals.iter().find(|p| p.id == id))
}

/// `Portal.Write` offsets (`ZoneManager.GetZoneOriginCell`).
pub fn portal_wire(server: &GameServer, p: &Portal) -> PortalWire {
    let (ox, oy) = if p.zone_id != 0 {
        server
            .game_data
            .worlds
            .get_world_template_by_zone_key(p.zone_id)
            .and_then(|w| w.xml_world_zones().get(&p.zone_id))
            .map_or((0.0, 0.0), |z| (z.origin_x as f32, z.origin_y as f32))
    } else {
        (0.0, 0.0)
    };
    PortalWire {
        id: p.id,
        name: p.name.clone(),
        zone_id: p.zone_id,
        off_x: p.x - ox * 1024.0,
        off_y: p.y - oy * 1024.0,
        z: p.z,
        z_rot: p.z_rot,
    }
}

/// `CharacterPortals.Send()`.
pub fn send_portals(server: &GameServer, player: &PlayerRef) {
    let (private, district, return_district_id, faction_id) = {
        let c = player.data();
        (
            c.portals.private_portals.clone(),
            c.portals.district_portals.clone(),
            c.return_district_id,
            c.unit.faction_id,
        )
    };
    if !private.is_empty() {
        player.send(SCCharacterPortalsPacket {
            portals: private.iter().map(|p| portal_wire(server, p)).collect(),
        });
    }
    if !district.is_empty() {
        let return_point = district_return_point(server, return_district_id, faction_id);
        player.send(SCCharacterReturnDistrictsPacket {
            portals: district.iter().map(|p| portal_wire(server, p)).collect(),
            return_district_id: return_point,
        });
    }
}

/// `CharacterPortals.NotifySubZone(subZoneId)` (`CSNotifySubZonePacket`): records a visited
/// district. As in C#, the district portals are rebuilt and the book is re-sent once per recall
/// portal of the sub zone.
pub fn notify_sub_zone(server: &GameServer, player: &PlayerRef, sub_zone_id: u32) {
    if player
        .data()
        .portals
        .visited_districts
        .iter()
        .any(|v| v.sub_zone == sub_zone_id)
    {
        return;
    }
    let Some(portals) = recall_by_sub_zone(server, sub_zone_id) else {
        return;
    };
    for portal in portals {
        let name = {
            let mut c = player.data();
            if !c
                .portals
                .visited_districts
                .iter()
                .any(|v| v.sub_zone == sub_zone_id)
            {
                let id = match server.ids.visited_sub_zone.get_next_id() {
                    Ok(id) => id,
                    Err(e) => {
                        tracing::error!("NotifySubZone: {e}");
                        return;
                    }
                };
                let owner = c.id;
                c.portals.visited_districts.push(VisitedDistrict {
                    id,
                    sub_zone: sub_zone_id,
                    owner,
                });
            }
            c.portals
                .populate_district_portals(&server.game_data.portals);
            c.unit.name.clone()
        };
        send_portals(server, player);
        tracing::debug!(
            "{name} - {}:{sub_zone_id} added to return district list",
            portal.name
        );
    }
}

/// `CharacterPortals.AddPrivatePortal(x, y, z, zRot, zoneId, name)`.
#[allow(clippy::too_many_arguments)]
pub fn add_private_portal(
    server: &GameServer,
    player: &PlayerRef,
    x: f32,
    y: f32,
    z: f32,
    z_rot: f32,
    zone_id: u32,
    name: &str,
) {
    let id = match server.ids.private_book.get_next_id() {
        Ok(id) => id,
        Err(e) => {
            tracing::error!("AddPrivatePortal: {e}");
            return;
        }
    };
    let portal = {
        let mut c = player.data();
        let portal = Portal {
            id,
            name: name.to_string(),
            x,
            y,
            z,
            zone_id,
            z_rot,
            owner: c.id,
            ..Portal::default()
        };
        c.portals.private_portals.push(portal.clone());
        portal
    };
    player.send(SCCharacterPortalsPacket {
        portals: vec![portal_wire(server, &portal)],
    });
}

/// `CharacterPortals.ChangePrivatePortalName(id, name)`.
pub fn change_private_portal_name(
    server: &GameServer,
    player: &PlayerRef,
    id: u32,
    name: &str,
) -> bool {
    let portal = {
        let mut c = player.data();
        match c.portals.private_portals.iter_mut().find(|p| p.id == id) {
            Some(p) => {
                p.name = name.to_string();
                p.clone()
            }
            None => return false,
        }
    };
    player.send(SCPortalInfoSavedPacket {
        portal: portal_wire(server, &portal),
    });
    true
}

/// `PortalManager.DeletePortal(owner, type, id)` (`CSDeletePortalPacket`): removes the book
/// entry (`RemoveFromBookPortal`). As in C#, no packet is sent and the district portal list is
/// not rebuilt (it still holds the removed district until the next load).
pub fn delete_portal(server: &GameServer, owner: &PlayerRef, portal_type: u8, id: u32) {
    let is_private = portal_type != 1;
    let (character_id, removed) = {
        let mut c = owner.data();
        let Some(portal) = portal_info(&c.portals, id).cloned() else {
            return;
        };
        let removed = if is_private {
            let before = c.portals.private_portals.len();
            c.portals.private_portals.retain(|p| p.id != portal.id);
            (c.portals.private_portals.len() != before).then_some((true, portal.id))
        } else {
            let before = c.portals.visited_districts.len();
            c.portals
                .visited_districts
                .retain(|v| v.sub_zone != portal.sub_zone_id);
            (c.portals.visited_districts.len() != before).then_some((false, portal.sub_zone_id))
        };
        (c.id, removed)
    };
    if let Some((private, key)) = removed {
        server.systems.npcs.portals.record(character_id, |p| {
            if private {
                p.private_portals.push(key);
            } else {
                p.visited_districts.push(key);
            }
        });
    }
}

// ------------------------------------------------------------------------------------------
// Open portals (summoned Portal NPCs)
// ------------------------------------------------------------------------------------------

/// `CheckItemAndRemove(owner, itemId, amount)`: `Inventory.CheckItems(Inventory, ...)` on the
/// bag, then `Bag.ConsumeItem(Teleport, ...)` (economy).
pub fn check_item_and_remove(
    server: &GameServer,
    owner: &PlayerRef,
    item_id: u32,
    amount: i32,
) -> bool {
    let bag = owner.data().inventory.bag().clone();
    let count = bag
        .lock()
        .expect("container poisoned")
        .items_by_template(item_id, -1)
        .1;
    if count < amount {
        return false;
    }
    if let Some(eco) = server.systems.npcs.hooks.economy.get() {
        eco.consume_item(server, owner, ITEM_TASK_TELEPORT, item_id, amount);
    }
    true
}

/// `CheckCanOpenPortal(owner, targetZoneId)`: same continent -> inland reagents, else outland.
fn check_can_open_portal(server: &GameServer, owner: &PlayerRef, target_zone_id: u32) -> bool {
    let owner_zone = owner.data().unit.transform.zone_id;
    let target_continent = target_id_by_zone(server, target_zone_id);
    let owner_continent = target_id_by_zone(server, owner_zone);
    let t = tables(server);
    let reagents = if target_continent == owner_continent {
        &t.open_portal_inland_reagents
    } else {
        &t.open_portal_outland_reagents
    };
    reagents
        .iter()
        .any(|r| check_item_and_remove(server, owner, r.item_id, r.amount))
}

/// `PortalManager.OpenPortal(owner, portalEffectObj)` (`OpenPortalEffect`): consumes the
/// reagent and summons the linked entrance / exit portal NPCs. Returns them.
pub fn open_portal(
    server: &GameServer,
    owner: &PlayerRef,
    effect: &PortalEffectObject,
) -> Option<(NpcRef, NpcRef)> {
    let portal_info = portal_info(&owner.data().portals, effect.id as u32).cloned();
    // C#: a missing book entry throws (NRE on `portalInfo.ZoneId`).
    let Some(portal_info) = portal_info else {
        tracing::error!("OpenPortal: portal {} is not in the book", effect.id);
        return None;
    };
    if !check_can_open_portal(server, owner, portal_info.zone_id) {
        return None;
    }
    let entrance = make_portal(server, owner, false, &portal_info, effect)?;
    let exit = make_portal(server, owner, true, &portal_info, effect)?;
    if let Some(p) = entrance.data().portal.as_mut() {
        p.linked_portal_obj_id = exit.obj_id;
    }
    if let Some(p) = exit.data().portal.as_mut() {
        p.linked_portal_obj_id = entrance.obj_id;
    }
    Some((entrance, exit))
}

/// `MakePortal(owner, isExit, portalInfo, portalEffectObj)`: creates, spawns and schedules the
/// `KillPortalTask` of one portal NPC.
fn make_portal(
    server: &GameServer,
    owner: &PlayerRef,
    is_exit: bool,
    portal_info: &Portal,
    effect: &PortalEffectObject,
) -> Option<NpcRef> {
    let template_id = if is_exit {
        PORTAL_EXIT_TEMPLATE_ID
    } else {
        PORTAL_ENTRANCE_TEMPLATE_ID
    };
    // C#: a missing template throws (NRE on `template.ModelId`).
    let Some(template) = server.game_data.systems.npcs.npc.template(template_id) else {
        tracing::error!("MakePortal: npc template {template_id} not found");
        return None;
    };
    let world_rot = owner_world_rotation(server, owner);
    let (instance_id, owner_id, faction_id) = {
        let c = owner.data();
        (c.unit.transform.instance_id, c.id, c.unit.faction_id)
    };
    let world = server.world.world(instance_id)?;
    let obj_id = match server.ids.object.get_next_id() {
        Ok(id) => id,
        Err(e) => {
            tracing::error!("MakePortal: {e}");
            return None;
        }
    };
    // `new Transform(null, null, portalInfo.ZoneId, owner.InstanceId, X, Y, Z, 0, 0, ZRot)`.
    let mut teleport_position = Transform {
        zone_id: portal_info.zone_id,
        instance_id,
        ..Transform::default()
    };
    teleport_position.set_position(
        portal_info.x,
        portal_info.y,
        portal_info.z,
        0.0,
        0.0,
        portal_info.z_rot,
    );
    let mut npc = Npc::new(template_id);
    npc.unit.owner_id = owner_id;
    npc.unit.model_id = template.model_id;
    npc.unit.faction_id = faction_id;
    npc.unit.level = template.level;
    npc.unit.name = portal_info.name.clone();
    npc.template_race = template.race;
    npc.template_gender = template.gender;
    npc.unit.scale = template.scale;
    npc.portal = Some(PortalInfo {
        teleport_position,
        linked_portal_obj_id: 0,
    });
    npc.unit.transform.zone_id = portal_info.zone_id;
    npc.unit.transform.instance_id = instance_id;
    if is_exit {
        // The exit gets the book's ZRot (degrees) as its yaw, as in C#.
        npc.unit.transform.set_position(
            portal_info.x,
            portal_info.y,
            portal_info.z,
            0.0,
            0.0,
            portal_info.z_rot,
        );
    } else {
        npc.unit.transform.set_position(
            effect.x,
            effect.y,
            effect.z,
            world_rot.0,
            world_rot.1,
            world_rot.2,
        );
    }
    // `InitializeSpawnBuffs` / `UpdateGearBonuses(null, null)` (skills: buffs / gear bonuses);
    // `MaxHp` / `MaxMp` are the NPC stat getters.
    super::npc::stats::refresh_cached_stats(&server.game_data, &mut npc);
    npc.unit.hp = npc.unit.max_hp;
    npc.unit.mp = npc.unit.max_mp;
    let npc = Entity::new(obj_id, npc);
    let changes = world.spawn(&npc, &server.view_ctx());
    session::handle_zone_changes(server, changes);
    schedule_kill(server, &npc);
    Some(npc)
}

fn owner_world_rotation(server: &GameServer, owner: &PlayerRef) -> (f32, f32, f32) {
    let inst = owner.data().unit.transform.instance_id;
    match server.world.world(inst) {
        Some(w) if owner.region().is_some() => {
            let p = w.world_pos_rot(&WorldObject::from(owner));
            (p.rot_x, p.rot_y, p.rot_z)
        }
        _ => {
            let c = owner.data();
            (
                c.unit.transform.rot_x,
                c.unit.transform.rot_y,
                c.unit.transform.rot_z,
            )
        }
    }
}

/// `TaskManager.Schedule(new KillPortalTask(portal), 30 s)`.
fn schedule_kill(server: &GameServer, npc: &NpcRef) {
    let Some(server) = server.self_arc() else {
        return; // no runtime server (tests): nothing schedules tasks
    };
    let weak = Arc::downgrade(&server);
    let npc = npc.clone();
    tokio::spawn(async move {
        tokio::time::sleep(PORTAL_LIFETIME).await;
        if let Some(server) = weak.upgrade() {
            delete_portal_npc(&server, &npc);
        }
    });
}

/// `Portal.Delete()`: broadcast `SCUnitDeathPacket(PortalTimeout)`, `Npc.Delete` (detach AI,
/// hide, remove) and `KillLinkedPortal` (`Hp = 0`, delete the linked portal when it is still
/// alive).
pub fn delete_portal_npc(server: &GameServer, npc: &NpcRef) {
    let (instance_id, linked) = {
        let n = npc.data();
        (
            n.unit.transform.instance_id,
            n.portal.as_ref().map_or(0, |p| p.linked_portal_obj_id),
        )
    };
    let Some(world) = server.world.world(instance_id) else {
        return;
    };
    crate::world::broadcast(
        &world,
        npc,
        &SCUnitDeathPacket {
            obj_id: npc.obj_id,
            kill_reason: KILL_REASON_PORTAL_TIMEOUT,
            resurrect_wait_time: 0,
            lost_exp: 0,
            durability_loss_rate: 0,
            killer: None,
        },
        false,
    );
    {
        let mut n = npc.data();
        n.ai_should_tick = false;
        n.has_ai = false;
    }
    world.delete(npc);
    // KillLinkedPortal
    npc.data().unit.hp = 0;
    if linked != 0 {
        if let Some(other) = world.npc(linked) {
            let alive = other.data().unit.hp > 0;
            if alive {
                delete_portal_npc(server, &other);
            }
        }
    }
}

/// `deg.DegToRad()` (float).
fn deg_to_rad(v: f32) -> f32 {
    std::f32::consts::PI / 180.0 * v
}

/// `PortalManager.UsePortal(character, objId)` (`CSUsePortalPacket`).
pub fn use_portal(server: &GameServer, player: &PlayerRef, obj_id: u32) {
    let instance_id = player.data().unit.transform.instance_id;
    let Some(world) = server.world.world(instance_id) else {
        return;
    };
    let Some(npc) = world.npc(obj_id) else {
        return;
    };
    // `(Portal)GetNpc(objId)`: any other NPC is an InvalidCastException in C#.
    let Some(teleport) = npc.data().portal.as_ref().map(|p| p.teleport_position) else {
        tracing::warn!("UsePortal: npc {obj_id} is not a portal");
        return;
    };
    let overburdened =
        server.systems.npcs.hooks.skills.get().is_some_and(|s| {
            s.check_buff_tag(server, &WorldObject::from(player), TAG_OVERBURDENED)
        });
    if overburdened {
        player.send(SCErrorMsgPacket::new(
            error_message::CANNOT_USE_PORTAL_WITH_BACKPACK,
            0,
            true,
        ));
        return;
    }
    if server
        .systems
        .social
        .trials
        .is_player_in_court(player.character_id)
    {
        player.send(SCErrorMsgPacket::new(
            crate::systems::instances::worlds::error_message::CANNOT_USE_PORTAL_IN_TRIAL,
            0,
            true,
        ));
        return;
    }
    let current_instance = {
        let mut c = player.data();
        c.disabled_set_position = true;
        c.unit.transform.instance_id
    };
    if teleport.instance_id != current_instance {
        // The loaded-slave check (SlaveEquipmentLoadedItem) and
        // `RemoveAndDespawnAllActiveOwnedMates/Slaves` (vehicles subsystem).
        if !crate::systems::vehicles::on_instance_change(server, player) {
            return;
        }
        player.send(SCLoadInstancePacket {
            instance_id: teleport.world_id,
            zone_id: teleport.zone_id,
            x: teleport.x,
            y: teleport.y,
            z: teleport.z,
            ang_x: deg_to_rad(teleport.rot_x),
            ang_y: deg_to_rad(teleport.rot_y),
            ang_z: deg_to_rad(teleport.rot_z),
        });
        let mut c = player.data();
        let parent = c.unit.transform.parent_obj_id;
        c.unit.transform = Transform {
            instance_id: teleport.world_id,
            parent_obj_id: parent,
            ..teleport
        };
    }
    player.send(SCTeleportUnitPacket {
        reason: packets::teleport_reason::PORTAL,
        error_message: 0,
        x: teleport.x,
        y: teleport.y,
        z: teleport.z,
        z2: deg_to_rad(teleport.rot_z),
    });
}

/// `WorldManager.ResendVisibleObjectsToCharacter(character)`: every object of the neighbouring
/// regions is shown again (`AddVisibleObject(character)`); doodads are batched into
/// `SCDoodadsCreatedPacket`s.
pub fn resend_visible_objects_to_character(server: &GameServer, player: &PlayerRef) {
    let instance_id = player.data().unit.transform.instance_id;
    let Some(world) = server.world.world(instance_id) else {
        return;
    };
    let ctx = server.view_ctx();
    let mut doodads = Vec::new();
    for obj in world.objects_around(&WorldObject::from(player)) {
        match &obj {
            WorldObject::Doodad(d) => doodads.push(d.clone()),
            _ => visibility::add_visible_object(&world, &obj, player, &ctx),
        }
    }
    for chunk in doodads.chunks(SCDoodadsCreatedPacket::MAX_COUNT_PER_PACKET) {
        let bodies = chunk
            .iter()
            .map(|d| visibility::doodad_body(&world, d, ctx.now_ms))
            .collect();
        player.send(SCDoodadsCreatedPacket { bodies });
    }
}

#[cfg(test)]
mod tests;
