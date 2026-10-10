//! `Core/Managers/IndunManager.cs`: dungeon / system instance requests, entry limits,
//! deletion, leaving, the action chains and the 30 s info tick.

use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use crate::data::world_data::Zone;
use crate::server::GameServer;
use crate::systems::social::team::Team;
use crate::world::{PlayerRef, WorldInstance};

use super::data::IndunZone;
use super::dungeon::{self, Dungeon};
use super::worlds::{self, error_message as err, send_error};

/// `IndunManager` runtime state (`server.systems.instances.indun`).
#[derive(Debug, Default)]
pub struct IndunManager {
    /// `EntryHistory`: character id -> zone group id -> entry times (unix ms).
    entry_history: Mutex<HashMap<u32, HashMap<u32, Vec<i64>>>>,
}

fn now_ms() -> i64 {
    chrono::Utc::now().timestamp_millis()
}

impl IndunManager {
    /// `CheckEntryAttemptCount(characterId, zoneGroupId, indunZone, addAsNewEntry)`: at most
    /// `EnterCount` entries within the last 4 hours.
    pub fn check_entry_attempt_count(
        &self,
        character_id: u32,
        zone_group_id: u32,
        indun_zone: &IndunZone,
        add_as_new_entry: bool,
    ) -> bool {
        let mut h = self.entry_history.lock().expect("entry history poisoned");
        let entries = h
            .entry(character_id)
            .or_default()
            .entry(zone_group_id)
            .or_default();
        // Can customize this to your start of the day to make this daily entry count
        let threshold = now_ms() - 4 * 3600 * 1000;
        let recent = entries.iter().filter(|t| **t >= threshold).count();
        if recent as u64 >= u64::from(indun_zone.enter_count) {
            tracing::warn!(
                "Requesting instance too many daily entries ({recent} / {}), characterId: {character_id}, zoneGroupId: {zone_group_id}",
                indun_zone.enter_count
            );
            return false;
        }
        if add_as_new_entry {
            entries.push(now_ms());
            tracing::warn!(
                "Added entry for player {character_id} in zone {zone_group_id}, Count is now {}",
                entries.len()
            );
        }
        true
    }

    /// Entries recorded for a character / zone group (tests, `InfoAttempt`).
    pub fn entry_count(&self, character_id: u32, zone_group_id: u32) -> usize {
        self.entry_history
            .lock()
            .expect("entry history poisoned")
            .get(&character_id)
            .and_then(|z| z.get(&zone_group_id))
            .map_or(0, Vec::len)
    }

    /// `InfoAttempt()`.
    fn info_attempt(&self, server: &GameServer) {
        let h = self.entry_history.lock().expect("entry history poisoned");
        for (character_id, zones) in h.iter() {
            for (zone_group_id, entries) in zones {
                let name = server
                    .world
                    .character_by_id(*character_id)
                    .map(|p| p.data().unit.name.clone())
                    .unwrap_or_default();
                let group = server
                    .game_data
                    .zones
                    .group(*zone_group_id)
                    .map(|g| g.name.clone())
                    .unwrap_or_default();
                tracing::debug!(
                    "For player={character_id} ({name}): {} entries into dungeon zone group {zone_group_id} ({group})",
                    entries.len()
                );
            }
        }
    }
}

/// `ZoneManager.GetZoneById(zoneId)`.
pub fn zone_by_id(server: &GameServer, zone_id: u32) -> Option<Zone> {
    let zones = &server.game_data.zones;
    let key = zones.zone_id_to_key.get(&zone_id)?;
    zones.zone_by_key(*key).cloned()
}

/// `GetWorlds()` with a dungeon, in instance id order.
fn dungeon_worlds(server: &GameServer) -> Vec<(Arc<WorldInstance>, Arc<Dungeon>)> {
    let mut worlds = server.world.worlds();
    worlds.sort_by_key(|w| w.id);
    worlds
        .into_iter()
        .filter_map(|w| {
            let d = server.systems.instances.dungeon_of_world(w.id)?;
            Some((w, d))
        })
        .collect()
}

/// `IndunInfoTick` (30 s): logs the instances and removes expired solo dungeons.
pub fn indun_info_tick(server: &GameServer) {
    let list = dungeon_worlds(server);
    let sys = list.iter().filter(|(_, d)| d.is_system).count();
    let dun = list.len() - sys;
    if sys + dun == 0 {
        return;
    }
    tracing::info!("Active Instances: {sys} system instance(s), {dun} dungeon(s)");
    if dun == 0 {
        return;
    }
    for (w, d) in &list {
        tracing::debug!(
            "{} - used by {}/{} player(s): {}",
            worlds::world_name(w),
            worlds::character_count(w),
            d.st().players_with_access.len(),
            worlds::list_player_names(w, 10)
        );
        if d.is_expired() {
            tracing::warn!("Removing expired solo dungeon {}", worlds::world_name(w));
            dungeon::destroy_dungeon(server, d);
        }
    }
    server.systems.instances.indun.info_attempt(server);
}

/// `InstanceHasChannels(zoneId)` (a missing zone / dungeon is a NullReferenceException in C#).
pub fn instance_has_channels(server: &GameServer, zone_id: u32) -> bool {
    let Some(zone) = zone_by_id(server, zone_id) else {
        return false;
    };
    server
        .game_data
        .systems
        .instances
        .indun
        .dungeon_zone(zone.group_id)
        .is_some_and(|z| z.select_channel)
}

/// `GetExistingDungeonsByZoneKey(zoneKey)`.
pub fn existing_dungeons_by_zone_key(server: &GameServer, zone_key: u32) -> Vec<Arc<Dungeon>> {
    dungeon_worlds(server)
        .into_iter()
        .filter(|(w, _)| w.template.zone_keys.contains(&zone_key))
        .map(|(_, d)| d)
        .collect()
}

/// `RequestSystemInstance(character, zoneId, channelId, out dungeon)`.
pub fn request_system_instance(
    server: &GameServer,
    p: &PlayerRef,
    zone_id: u32,
    channel_id: u32,
) -> (bool, Option<Arc<Dungeon>>) {
    let Some(zone) = zone_by_id(server, zone_id) else {
        tracing::warn!(
            "Requesting non existing system instance for zone {zone_id}, character {}",
            p.data().unit.name
        );
        return (false, None);
    };
    for d in existing_dungeons_by_zone_key(server, zone.zone_key) {
        let channel = d
            .world_id()
            .map_or(0, |id| server.systems.instances.worlds.channel_id(id));
        if channel == channel_id {
            let ok = dungeon::queue_player(server, &d, p);
            return (ok, Some(d));
        }
    }
    let Some(d) = create_system_instance(server, Some(p), zone.zone_key, channel_id, false, 0)
    else {
        tracing::error!(
            "Failed to create system instance for zoneId {zone_id}, channel: {channel_id}, character {}",
            p.data().unit.name
        );
        return (false, None);
    };
    let ok = dungeon::queue_player(server, &d, p);
    (ok, Some(d))
}

/// `RequestDungeonInstance(character, zoneId, channelId)`.
pub fn request_dungeon_instance(
    server: &GameServer,
    p: &PlayerRef,
    zone_id: u32,
    channel_id: u32,
) -> bool {
    let team = server.systems.social.teams.team_by_obj_id(p.obj_id);
    // `zone.ZoneKey` on a missing zone is a NullReferenceException in C#.
    let Some(zone) = zone_by_id(server, zone_id) else {
        return false;
    };
    if server
        .game_data
        .worlds
        .get_world_template_by_zone_key(zone.zone_key)
        .is_none()
    {
        // Non-existing dungeon zone
        return false;
    }
    let Some(dungeon_zone) = server
        .game_data
        .systems
        .instances
        .indun
        .dungeon_zone(zone.group_id)
        .cloned()
    else {
        // Not a dungeon
        return false;
    };
    if !verify_dungeon_enter_requirements(server, &dungeon_zone, p, team.as_ref()) {
        return false;
    }

    // 1 - Re-enter an active dungeon of this zone the player is a member of
    let possible = existing_dungeons_by_zone_key(server, zone.zone_key);
    for d in &possible {
        if d.is_queued(p) {
            // Already queued, please wait
            send_error(p, err::TRY_LATER_INSTANCE, 0);
            return true;
        }
        if d.world(server)
            .is_some_and(|w| worlds::has_character(&w, p.character_id))
        {
            dungeon::add_player(server, d, p);
            return true;
        }
        if d.has_access(p.character_id) {
            let count = d.world(server).map_or(0, |w| worlds::character_count(&w));
            if count as u64 > u64::from(d.indun_zone.max_players) {
                send_error(p, err::INSTANCE_QUOTA, 0);
                return false;
            }
            return dungeon::queue_player(server, d, p);
        }
    }

    // 2 - Party required dungeons: join the team's dungeon
    if dungeon_zone.party_only {
        for d in &possible {
            if !dungeon::player_in_same_team(server, d, p) {
                continue;
            }
            if d.is_full(server) {
                send_error(p, err::INSTANCE_QUOTA, 0);
                return false;
            }
            return dungeon::queue_player(server, d, p);
        }
    }

    // 3 - Join the dungeon of a team member (party / raid leader first)
    if let Some(team) = &team {
        let mut check: Vec<PlayerRef> = Vec::new();
        for m in team.members.iter().flatten() {
            if m.character.character_id == team.owner_id {
                check.insert(0, m.character.clone());
            } else {
                check.push(m.character.clone());
            }
        }
        for member in &check {
            for d in &possible {
                if !d.has_access(member.character_id) {
                    continue;
                }
                // TODO (C#): not sure if we should toss a error here, or continue searching
                if d.is_full(server) {
                    send_error(p, err::INSTANCE_QUOTA, 0);
                    return false;
                }
                return dungeon::queue_player(server, d, p);
            }
        }
    }

    // 4 - Create a new dungeon
    tracing::info!(
        "Creating a new dungeon for player {} ({}), zone: {dungeon_zone}, channel: {channel_id}",
        p.data().unit.name,
        p.character_id
    );
    let Some(d) = create_dungeon_instance(server, &dungeon_zone, p, channel_id) else {
        tracing::error!(
            "Failed to create a new dungeon for player {} ({}), zone: {dungeon_zone}, channel: {channel_id}",
            p.data().unit.name,
            p.character_id
        );
        return false;
    };
    dungeon::queue_player(server, &d, p)
}

/// `VerifyDungeonEnterRequirements(dungeonZone, character, team)`: entry count, level, party and
/// the entry item (consumed here).
pub fn verify_dungeon_enter_requirements(
    server: &GameServer,
    zone: &IndunZone,
    p: &PlayerRef,
    team: Option<&Team>,
) -> bool {
    let (id, level) = {
        let c = p.data();
        (c.id, u32::from(c.unit.level))
    };
    if !server.systems.instances.indun.check_entry_attempt_count(
        id,
        zone.zone_group_id,
        zone,
        false,
    ) {
        send_error(p, err::INSTANCE_VISIT_LIMIT, 0);
        return false;
    }
    if level < zone.level_min {
        tracing::warn!(
            "Requesting instance level too low ({level} < {}), characterId: {id}, zoneGroupId: {}",
            zone.level_min,
            zone.zone_group_id
        );
        send_error(p, err::INSTANCE_LEVEL, 0);
        return false;
    }
    if level > zone.level_max {
        tracing::warn!(
            "Requesting instance level too high ({level} > {}), characterId: {id}, zoneGroupId: {}",
            zone.level_max,
            zone.zone_group_id
        );
        send_error(p, err::INSTANCE_LEVEL, 0);
        return false;
    }
    if zone.party_only && team.is_none() {
        tracing::warn!(
            "Requesting instance team required, characterId: {id}, zoneGroupId: {}",
            zone.zone_group_id
        );
        send_error(p, err::NEED_PARTY, 0);
        return false;
    }
    if zone.item_id > 0
        && !crate::systems::npcs::portal::check_item_and_remove(server, p, zone.item_id, 1)
    {
        tracing::info!(
            "[IndunManager] Player does not have the required item to create a new dungeon, characterId: {id}, zoneGroupId: {}, item: {}",
            zone.zone_group_id,
            zone.item_id
        );
        send_error(p, err::ENTER_INST_REQ_ITEM, zone.item_id);
        return false;
    }
    true
}

/// `CreateDungeonInstance(dungeonZone, character, channelId, out dungeon)`.
///
/// Note (C#): the requirements are verified again (the entry item is consumed a second time).
fn create_dungeon_instance(
    server: &GameServer,
    zone: &IndunZone,
    p: &PlayerRef,
    channel_id: u32,
) -> Option<Arc<Dungeon>> {
    if server.world.worlds().len() as u64 > u64::from(server.config.world.max_instances) {
        tracing::warn!(
            "Requesting a new instance would exceeds the allowed ammount, characterId: {}, zoneGroupId: {}",
            p.character_id,
            zone.zone_group_id
        );
        send_error(p, err::NO_SERVER_INSTANCE_RESOURCE, 0);
        return None;
    }
    let team = server.systems.social.teams.team_by_obj_id(p.obj_id);
    tracing::info!(
        "Requesting instance, characterId: {}, zoneGroupId: {}",
        p.character_id,
        zone.zone_group_id
    );
    if !verify_dungeon_enter_requirements(server, zone, p, team.as_ref()) {
        return None;
    }
    let d = dungeon::create(server, zone, Some(p), channel_id, team, false, 0, false)?;
    // `return dungeon.QueuePlayer(character)` (already queued by the constructor).
    if !dungeon::queue_player(server, &d, p) {
        return None;
    }
    Some(d)
}

/// `CreateSystemInstance(character, zoneKey, channelId, overrideInstanceId, fixedInstanceId)`.
pub fn create_system_instance(
    server: &GameServer,
    character: Option<&PlayerRef>,
    zone_key: u32,
    channel_id: u32,
    override_instance_id: bool,
    fixed_instance_id: u32,
) -> Option<Arc<Dungeon>> {
    let who = character.map_or_else(|| "[SYSTEM]".to_string(), |p| p.data().unit.name.clone());
    let ov = if override_instance_id {
        fixed_instance_id.to_string()
    } else {
        "NO".into()
    };
    tracing::info!(
        "Requesting system instance, zoneKey: {zone_key}, character: {who}, channel: {channel_id}, override InstanceId: {ov}"
    );
    let team = character.and_then(|p| server.systems.social.teams.team_by_obj_id(p.obj_id));
    let group = server
        .game_data
        .zones
        .zone_by_key(zone_key)
        .map(|z| z.group_id);
    let Some(zone) = group.and_then(|g| {
        server
            .game_data
            .systems
            .instances
            .indun
            .dungeon_zone(g)
            .cloned()
    }) else {
        tracing::error!(
            "Requesting invalid system instance: , zoneKey: {zone_key}, character: {who}, channel: {channel_id}, override InstanceId: {ov}"
        );
        return None;
    };
    // Check for duplicate system instances
    for (w, d) in dungeon_worlds(server) {
        if server.systems.instances.worlds.channel_id(w.id) == channel_id
            && d.zone_group_id() == zone.zone_group_id
        {
            if let Some(p) = character {
                if verify_dungeon_enter_requirements(server, &zone, p, team.as_ref()) {
                    dungeon::queue_player(server, &d, p);
                }
            }
            return Some(d);
        }
    }
    let d = dungeon::create(
        server,
        &zone,
        character,
        channel_id,
        team.clone(),
        override_instance_id,
        fixed_instance_id,
        true,
    )?;
    if Some(zone.zone_group_id) != group {
        tracing::info!("[IndunManager] system dungeon request on different area.");
        if let Some(p) = character {
            send_error(p, err::PROHIBITED_IN_INSTANCE, 0);
        }
        return None;
    }
    if let Some(p) = character {
        if verify_dungeon_enter_requirements(server, &d.indun_zone, p, team.as_ref()) {
            dungeon::queue_player(server, &d, p);
        }
    }
    Some(d)
}

/// `RequestDeletion(character, zone)`: the player gives up their access to the dungeons of the
/// zone; a dungeon nobody has access to any more is destroyed.
pub fn request_deletion(server: &GameServer, p: &PlayerRef, zone: Option<&Zone>) -> bool {
    let Some(zone) = zone else {
        send_error(p, err::ALREADY_UNBOUND_INSTANCE, 0);
        return false;
    };
    let mut removed = 0;
    for d in existing_dungeons_by_zone_key(server, zone.zone_key) {
        if d.is_system {
            continue;
        }
        let empty = {
            let mut st = d.st();
            if !st.players_with_access.remove(&p.character_id) {
                continue;
            }
            st.players_with_access.is_empty()
        };
        removed += 1;
        if empty {
            dungeon::destroy_dungeon(server, &d);
        }
    }
    if removed == 0 {
        send_error(p, err::ALREADY_UNBOUND_INSTANCE, 0);
    }
    true
}

/// `RequestLeaveInstance(character)`: raises `OnDungeonLeave` when the player is in any
/// instance's character list (main_world included, as in C#).
pub fn request_leave_instance(server: &GameServer, p: &PlayerRef) -> bool {
    let mut worlds = server.world.worlds();
    worlds.sort_by_key(|w| w.id);
    if !worlds
        .iter()
        .any(|w| worlds::has_character(w, p.character_id))
    {
        return false;
    }
    super::events::fire_dungeon_leave(server, p);
    true
}

/// `DoIndunActions(startActionId, worldInstance)`: runs an action chain.
pub fn do_indun_actions(server: &GameServer, start_action_id: u32, world: &Arc<WorldInstance>) {
    let mut id = start_action_id;
    loop {
        let Some(action) = server.game_data.systems.instances.indun.action(id).copied() else {
            // `action.Execute` on null (NullReferenceException in C#).
            tracing::error!("DoIndunActions: action {id} does not exist");
            return;
        };
        super::events::execute(server, &action, world);
        tracing::warn!(
            "DoIndunActions: world={}, action.Id={}, action.NextActionId={}",
            world.id,
            action.id,
            action.next_action_id
        );
        if action.next_action_id > 0 {
            id = action.next_action_id;
            continue;
        }
        break;
    }
}
