//! Original siege participant buffs. The client grants headquarters teleport from its own
//! buff_skills rows: 3733 -> 17046 (offense), 3734 -> 20950 (defense).
//! Native IsInSiegeTeam392F3CC0 requires phase4 and the current-zone character roster.
//! Commander buffs are cleared but never granted without an authoritative appointment.

use crate::server::GameServer;
use crate::systems::skills::buffs;
use crate::world::{PlayerRef, WorldObject};

use super::runtime;
use super::state::SiegeSide;

pub const OFFENSE_PARTICIPANT_BUFF: u32 = 3733;
pub const DEFENSE_PARTICIPANT_BUFF: u32 = 3734;
pub const OFFENSE_COMMANDER_BUFF: u32 = 3727;
pub const DEFENSE_COMMANDER_BUFF: u32 = 3735;

const PARTICIPANT_BUFFS: [u32; 4] = [
    OFFENSE_PARTICIPANT_BUFF,
    DEFENSE_PARTICIPANT_BUFF,
    OFFENSE_COMMANDER_BUFF,
    DEFENSE_COMMANDER_BUFF,
];

/// Reconcile all currently connected characters after an authority/roster phase change.
/// Re-read authority rather than accepting a caller's possibly stale snapshot.
pub fn sync_all(server: &GameServer) {
    let now = chrono::Utc::now().timestamp();
    for player in server.world.all_characters() {
        sync_player_at(server, &player, now);
    }
}

/// Reconcile world entry, zone/instance changes and expedition membership changes.
pub fn sync_player(server: &GameServer, player: &PlayerRef) {
    sync_player_at(server, player, chrono::Utc::now().timestamp());
}

/// Remove transient participation before saving/disconnecting the character.
pub fn clear_player(server: &GameServer, player: &PlayerRef) {
    project(server, player, None);
}

fn selected_buff(server: &GameServer, player: &PlayerRef, now: i64) -> Option<u32> {
    let (online, expedition, transform) = {
        let c = player.data();
        (c.is_online, c.unit.expedition_id, c.unit.transform)
    };
    if !online || !runtime::is_live_object(server, &WorldObject::from(player)) {
        return None;
    }
    let group = server
        .game_data
        .zones
        .zone_by_key(transform.zone_id)?
        .group_id;
    let state = server.systems.housing.dominions.get(group)?;
    let siege = state.siege.as_ref()?;
    if state.owner_expedition_id == 0
        || state.tower.instance_id != transform.instance_id
        || !siege.active_at(now)
    {
        return None;
    }
    let tower = server.systems.housing.manager.by_id(state.tower.house_id)?;
    if !runtime::is_live_object(server, &WorldObject::from(&tower))
        || runtime::tower_identity(server, &tower) != Some((group, state.tower))
    {
        return None;
    }
    let participant = siege.participants.get(&player.character_id)?;
    let side_expedition = match participant.side {
        SiegeSide::Attacker => siege.attacker_expedition_id,
        SiegeSide::Defender => siege.defender_expedition_id,
    };
    // The currently implemented authority registers members of the two named expeditions.
    // A future verified mercenary flow must extend that authority before granting its buffs.
    if expedition == 0
        || participant.expedition_id != side_expedition
        || expedition != side_expedition
        || server
            .expeditions
            .expedition_of_character(player.character_id)
            != expedition
        || !server
            .expeditions
            .get(expedition)
            .is_some_and(|e| !e.is_disbanded)
    {
        return None;
    }
    Some(match participant.side {
        SiegeSide::Attacker => OFFENSE_PARTICIPANT_BUFF,
        SiegeSide::Defender => DEFENSE_PARTICIPANT_BUFF,
    })
}

pub(super) fn sync_player_at(server: &GameServer, player: &PlayerRef, now: i64) {
    project(server, player, selected_buff(server, player, now));
}

fn project(server: &GameServer, player: &PlayerRef, selected: Option<u32>) {
    let object = WorldObject::from(player);
    for id in PARTICIPANT_BUFFS {
        if Some(id) != selected && player.data().unit.buffs.check_buff(id) {
            buffs::remove_buff(server, &object, id);
        }
    }
    if let Some(id) = selected {
        if !player.data().unit.buffs.check_buff(id) {
            buffs::add_buff_by_id(server, &object, id, &object, 0);
        }
    }
}
