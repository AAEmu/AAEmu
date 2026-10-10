//! The runtime state of `ZoneConflict` (`ZoneGroup.Conflict`): `CurrentZoneState`,
//! `NextStateTime`, `KillCount`, `SetState` / `ForceNextState` / `CheckTimer` and the
//! `ZoneStateChangeTask` timer chain (`SendSwitchZoneState`).
//!
//! The static rows (and the load-time state: open conflict zones start in `Conflict`) come from
//! `game_data.zones.conflicts`; the first access copies them. The load-time `SetState(Conflict)`
//! of `ZoneManager.Load` does not schedule its timer here (the timers start with the first
//! runtime `SetState`).

use std::sync::{Arc, Mutex, MutexGuard, OnceLock};
use std::time::Duration;

use crate::data::world_data::{conflict_type, ZoneConflict};
use crate::packets::world::SCConflictZoneStatePacket;
use crate::server::GameServer;

/// One conflict with its runtime counters.
#[derive(Debug, Clone)]
struct Entry {
    c: ZoneConflict,
    /// `KillCount`.
    kill_count: u32,
    /// Bumped by every state change: a scheduled `ZoneStateChangeTask` only checks the timer
    /// when it is still the latest one (the C# task re-reads `NextStateTime`, which is the same).
    timer_serial: u64,
}

/// `ZoneManager._conflicts` (runtime part).
#[derive(Debug, Default)]
pub struct ZoneConflicts {
    states: OnceLock<Mutex<Vec<Entry>>>,
}

/// `ZoneConflictType` member names (`ToString()`).
pub const ZONE_CONFLICT_TYPE_NAMES: [&str; 8] = [
    "Tension", "Danger", "Dispute", "Unrest", "Crisis", "Conflict", "War", "Peace",
];

fn now_unix() -> i64 {
    chrono::Utc::now().timestamp()
}

impl ZoneConflicts {
    fn lock(&self, server: &GameServer) -> MutexGuard<'_, Vec<Entry>> {
        self.states
            .get_or_init(|| {
                Mutex::new(
                    server
                        .game_data
                        .zones
                        .conflicts
                        .iter()
                        .map(|c| Entry {
                            c: c.clone(),
                            kill_count: 0,
                            timer_serial: 0,
                        })
                        .collect(),
                )
            })
            .lock()
            .unwrap_or_else(|e| e.into_inner())
    }

    /// `ZoneManager.GetConflicts()` (current values, load order).
    pub fn snapshot(&self, server: &GameServer) -> Vec<ZoneConflict> {
        self.lock(server).iter().map(|e| e.c.clone()).collect()
    }

    /// `ZoneGroup.Conflict` (current values) of a zone group.
    pub fn get(&self, server: &GameServer, zone_group_id: u32) -> Option<ZoneConflict> {
        self.lock(server)
            .iter()
            .find(|e| u32::from(e.c.zone_group_id) == zone_group_id)
            .map(|e| e.c.clone())
    }
}

/// `ZoneConflict.SetState(ct)` of the conflict of `zone_group_id`. `false` when the zone group
/// has no conflict (`ZoneGroup.Conflict == null`).
pub fn set_state(server: &GameServer, zone_group_id: u16, ct: u8) -> bool {
    let changed = {
        let mut states = server.world.zone_conflicts.lock(server);
        let Some(e) = states
            .iter_mut()
            .find(|e| e.c.zone_group_id == zone_group_id)
        else {
            return false;
        };
        if ct == e.c.current_state {
            return true;
        }
        let previous = e.c.current_state;
        match ct {
            conflict_type::CONFLICT => {
                e.kill_count = 0;
                e.c.next_state_time = now_unix() + i64::from(e.c.conflict_min) * 60;
            }
            conflict_type::WAR => {
                e.kill_count = 0;
                e.c.next_state_time = now_unix() + i64::from(e.c.war_min) * 60;
            }
            conflict_type::PEACE => {
                e.kill_count = 0;
                e.c.next_state_time = now_unix() + i64::from(e.c.peace_min) * 60;
            }
            _ => e.c.next_state_time = 0,
        }
        e.c.current_state = ct;
        e.timer_serial += 1;
        tracing::info!(
            "ZoneGroup {zone_group_id} changed from {} → {} (next state at {})",
            state_name(previous),
            state_name(ct),
            e.c.next_state_time
        );
        (e.c.clone(), e.timer_serial)
    };
    send_switch_zone_state(server, &changed.0, changed.1);
    true
}

/// `ZoneConflictType.ToString()`.
pub fn state_name(state: u8) -> String {
    ZONE_CONFLICT_TYPE_NAMES
        .get(usize::from(state))
        .map_or_else(|| state.to_string(), |s| (*s).to_string())
}

/// `SendSwitchZoneState()`: the next timer first, then the broadcast.
fn send_switch_zone_state(server: &GameServer, c: &ZoneConflict, serial: u64) {
    set_timer_task(server, c, serial);
    server
        .world
        .broadcast_to_server(&SCConflictZoneStatePacket {
            zone_group_id: c.zone_group_id,
            state: c.current_state,
            end_time: c.next_state_time,
        });
}

/// `SetTimerTask()`: a `ZoneStateChangeTask` at `NextStateTime`.
fn set_timer_task(server: &GameServer, c: &ZoneConflict, serial: u64) {
    if c.next_state_time <= 0 {
        tracing::debug!(
            "ZoneGroup {}: no NextStateTime set — timer chain stopped.",
            c.zone_group_id
        );
        return;
    }
    let (Some(server), Ok(rt)) = (server.self_arc(), tokio::runtime::Handle::try_current()) else {
        return;
    };
    let delay = Duration::from_secs((c.next_state_time - now_unix()).max(0) as u64);
    let weak = Arc::downgrade(&server);
    drop(server);
    let gid = c.zone_group_id;
    rt.spawn(async move {
        tokio::time::sleep(delay).await;
        if let Some(s) = weak.upgrade() {
            let current = s
                .world
                .zone_conflicts
                .lock(&s)
                .iter()
                .find(|e| e.c.zone_group_id == gid)
                .map(|e| e.timer_serial);
            if current == Some(serial) {
                check_timer(&s, gid);
            }
        }
    });
}

/// `CheckTimer()`.
pub fn check_timer(server: &GameServer, zone_group_id: u16) {
    let Some(c) = server
        .world
        .zone_conflicts
        .get(server, u32::from(zone_group_id))
    else {
        return;
    };
    if c.next_state_time > 0 && now_unix() >= c.next_state_time {
        force_next_state(server, zone_group_id);
    }
}

/// `ForceNextState()`.
pub fn force_next_state(server: &GameServer, zone_group_id: u16) {
    let Some(c) = server
        .world
        .zone_conflicts
        .get(server, u32::from(zone_group_id))
    else {
        return;
    };
    let no_kill_counter = c.num_kills.iter().all(|k| *k == 0);
    let next = if c.current_state < conflict_type::PEACE {
        if c.current_state == conflict_type::WAR && c.peace_min <= 0 {
            conflict_type::CONFLICT
        } else {
            c.current_state + 1
        }
    } else if no_kill_counter {
        // If it doesn't have a killcounter, go directly back to conflict (ocean areas)
        conflict_type::CONFLICT
    } else {
        conflict_type::TENSION
    };
    set_state(server, zone_group_id, next);
}
