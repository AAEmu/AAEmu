//! `ZoneManager` conflict accessors used by the GM commands (`/zonestate`).
//!
//! The runtime state of every `ZoneConflict` (kill counter, `SetState` / `ForceNextState` /
//! `CheckTimer`, the `ZoneStateChangeTask` timer chain) lives in
//! [`crate::systems::social::zone_conflict::ZoneConflictManager`]; this module is a thin view
//! over it so the world API keeps the C# `ZoneManager` shape.

use crate::data::world_data::ZoneConflict;
use crate::server::GameServer;

/// `ZoneConflictType` member names (`ToString()`).
pub const ZONE_CONFLICT_TYPE_NAMES: [&str; 8] = [
    "Tension", "Danger", "Dispute", "Unrest", "Crisis", "Conflict", "War", "Peace",
];

/// `ZoneManager._conflicts` view (state is owned by `systems.social.conflicts`).
#[derive(Debug, Default)]
pub struct ZoneConflicts;

/// Servers that never ran the social `start` (tests) copy the loaded rows on first access,
/// without scheduling the load-time timers.
fn ensure_loaded(server: &GameServer) {
    let conflicts = &server.systems.social.conflicts;
    if conflicts.conflicts().is_empty() && !server.game_data.zones.conflicts.is_empty() {
        conflicts.init(
            &server.game_data.zones.conflicts,
            chrono::Utc::now().timestamp_millis(),
        );
    }
}

impl ZoneConflicts {
    /// `ZoneManager.GetConflicts()` (current values, load order).
    pub fn snapshot(&self, server: &GameServer) -> Vec<ZoneConflict> {
        ensure_loaded(server);
        server.systems.social.conflicts.conflicts()
    }

    /// `ZoneGroup.Conflict` (current values) of a zone group.
    pub fn get(&self, server: &GameServer, zone_group_id: u32) -> Option<ZoneConflict> {
        ensure_loaded(server);
        server.systems.social.conflicts.get(zone_group_id)
    }
}

/// `ZoneConflict.SetState(ct)` of the conflict of `zone_group_id`. `false` when the zone group
/// has no conflict (`ZoneGroup.Conflict == null`).
pub fn set_state(server: &GameServer, zone_group_id: u16, ct: u8) -> bool {
    ensure_loaded(server);
    let conflicts = &server.systems.social.conflicts;
    if conflicts.get(u32::from(zone_group_id)).is_none() {
        return false;
    }
    conflicts.set_state(server, zone_group_id, ct);
    true
}

/// `ZoneConflictType.ToString()`.
pub fn state_name(state: u8) -> String {
    ZONE_CONFLICT_TYPE_NAMES
        .get(usize::from(state))
        .map_or_else(|| state.to_string(), |s| (*s).to_string())
}

/// `CheckTimer()`.
pub fn check_timer(server: &GameServer, zone_group_id: u16) {
    ensure_loaded(server);
    server
        .systems
        .social
        .conflicts
        .check_timer(server, zone_group_id);
}

/// `ForceNextState()`.
pub fn force_next_state(server: &GameServer, zone_group_id: u16) {
    ensure_loaded(server);
    server
        .systems
        .social
        .conflicts
        .force_next_state(server, zone_group_id);
}
