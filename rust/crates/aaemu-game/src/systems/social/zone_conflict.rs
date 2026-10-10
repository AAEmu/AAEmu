//! Runtime state of the zone conflicts: `Models/Game/World/Zones/ZoneConflict` (`AddZoneKill`,
//! `SetState`, `ForceNextState`, `CheckTimer`, `SetTimerTask`, `SendSwitchZoneState`) and
//! `Models/Tasks/Zones/ZoneStateChangeTask`.
//!
//! `ZoneManager.Load` (`data::world_data::ZoneData::load`) already applied the initial
//! `SetState(Conflict)` of every open zone to the loaded values; [`ZoneConflictManager::start`]
//! copies them here and schedules their timers (the C# `SetState` -> `SendSwitchZoneState` ->
//! `SetTimerTask` at load; the broadcast reaches nobody at that point).

use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use crate::data::world_data::{conflict_type, ZoneConflict};
use crate::packets::world::SCConflictZoneStatePacket;
use crate::server::GameServer;

/// One conflict zone: the loaded values plus `NextStateTime` in milliseconds (the loaded
/// `next_state_time` is kept in seconds for the packets).
#[derive(Debug, Clone)]
struct Entry {
    c: ZoneConflict,
    /// `NextStateTime` (unix ms, 0 = `DateTime.MinValue`).
    next_state_ms: i64,
}

impl Entry {
    fn set_next(&mut self, ms: i64) {
        self.next_state_ms = ms;
        self.c.next_state_time = if ms == 0 { 0 } else { ms.div_euclid(1000) };
    }

    fn has_kill_counter(&self) -> bool {
        self.c.num_kills.iter().any(|&n| n != 0)
    }
}

/// What a state change asks the caller to do once the lock is released
/// (`SendSwitchZoneState`: `SetTimerTask` first, then the server-wide broadcast).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SwitchZoneState {
    pub zone_group_id: u16,
    pub state: u8,
    /// `NextStateTime` (unix seconds, 0 = none).
    pub next_state_time: i64,
    /// Delay of the `ZoneStateChangeTask` to schedule (`None` = "timer chain stopped").
    pub timer_ms: Option<i64>,
}

/// `ZoneManager._conflicts` at runtime (insertion order of `conflict_zones`).
#[derive(Debug, Default)]
pub struct ZoneConflictManager {
    inner: Mutex<(Vec<u16>, HashMap<u16, Entry>)>,
}

fn now_ms() -> i64 {
    chrono::Utc::now().timestamp_millis()
}

impl ZoneConflictManager {
    /// Copies the loaded conflicts (`ZoneManager.Load`) and schedules the timers of the open
    /// ones.
    pub fn start(server: &Arc<GameServer>) {
        let conflicts = &server.systems.social.conflicts;
        let switches = conflicts.init(&server.game_data.zones.conflicts, now_ms());
        for s in switches {
            schedule_timer(server, &s);
        }
    }

    /// Fills the table from the loaded values (`next_state_time` in seconds). Returns the
    /// timers to schedule (the zones with a `NextStateTime`).
    pub fn init(&self, loaded: &[ZoneConflict], now_ms: i64) -> Vec<SwitchZoneState> {
        let mut g = self.inner.lock().expect("conflicts poisoned");
        g.0.clear();
        g.1.clear();
        let mut out = Vec::new();
        for c in loaded {
            let mut e = Entry {
                c: c.clone(),
                next_state_ms: 0,
            };
            e.set_next(c.next_state_time * 1000);
            if e.next_state_ms > 0 {
                out.push(SwitchZoneState {
                    zone_group_id: c.zone_group_id,
                    state: c.current_state,
                    next_state_time: c.next_state_time,
                    timer_ms: Some(e.next_state_ms - now_ms),
                });
            }
            g.0.push(c.zone_group_id);
            g.1.insert(c.zone_group_id, e);
        }
        out
    }

    /// `ZoneManager.GetConflicts()` (a snapshot, in load order).
    pub fn conflicts(&self) -> Vec<ZoneConflict> {
        let g = self.inner.lock().expect("conflicts poisoned");
        g.0.iter()
            .filter_map(|id| g.1.get(id))
            .map(|e| e.c.clone())
            .collect()
    }

    /// The conflict of a zone group (`GetConflicts().FirstOrDefault(c => c.ZoneGroupId == ..)`).
    pub fn get(&self, zone_group_id: u32) -> Option<ZoneConflict> {
        let g = self.inner.lock().expect("conflicts poisoned");
        u16::try_from(zone_group_id)
            .ok()
            .and_then(|id| g.1.get(&id))
            .map(|e| e.c.clone())
    }

    fn with_entry<R>(&self, zone_group_id: u16, f: impl FnOnce(&mut Entry) -> R) -> Option<R> {
        let mut g = self.inner.lock().expect("conflicts poisoned");
        g.1.get_mut(&zone_group_id).map(f)
    }

    /// `AddZoneKill(numberOfKills)`: the kill counter climbs Tension -> Danger -> Dispute ->
    /// Unrest -> Crisis -> Conflict. Returns the state switch to send, if any.
    pub fn add_zone_kill_at(
        &self,
        zone_group_id: u16,
        number_of_kills: u32,
        now_ms: i64,
    ) -> Option<SwitchZoneState> {
        self.with_entry(zone_group_id, |e| {
            // Ignore when in conflict, war or peace
            if e.c.current_state >= conflict_type::CONFLICT {
                return None;
            }
            // Ignore if this zone doesn't have a kill counter mechanic
            if !e.has_kill_counter() {
                return None;
            }
            let last_state = e.c.current_state;
            e.c.kill_count = e.c.kill_count.wrapping_add(number_of_kills);
            let kills = i64::from(e.c.kill_count);
            let steps = [
                (conflict_type::TENSION, conflict_type::DANGER),
                (conflict_type::DANGER, conflict_type::DISPUTE),
                (conflict_type::DISPUTE, conflict_type::UNREST),
                (conflict_type::UNREST, conflict_type::CRISIS),
            ];
            for (i, (from, to)) in steps.into_iter().enumerate() {
                if e.c.current_state == from && kills > i64::from(e.c.num_kills[i]) {
                    e.c.current_state = to;
                    e.set_next(0);
                }
            }
            if e.c.current_state == conflict_type::CRISIS && kills > i64::from(e.c.num_kills[4]) {
                e.c.current_state = conflict_type::CONFLICT;
                e.set_next(now_ms + i64::from(e.c.conflict_min) * 60_000);
                e.c.kill_count = 0;
            }
            (last_state != e.c.current_state).then(|| switch(e, now_ms))
        })
        .flatten()
    }

    /// `SetState(ct)`. Returns the state switch to send (`None` when unchanged).
    pub fn set_state_at(&self, zone_group_id: u16, ct: u8, now_ms: i64) -> Option<SwitchZoneState> {
        self.with_entry(zone_group_id, |e| set_state(e, ct, now_ms))
            .flatten()
    }

    /// `ForceNextState()`.
    pub fn force_next_state_at(&self, zone_group_id: u16, now_ms: i64) -> Option<SwitchZoneState> {
        self.with_entry(zone_group_id, |e| force_next_state(e, now_ms))
            .flatten()
    }

    /// `CheckTimer()`: advances the state once `NextStateTime` has passed.
    pub fn check_timer_at(&self, zone_group_id: u16, now_ms: i64) -> Option<SwitchZoneState> {
        self.with_entry(zone_group_id, |e| {
            if e.next_state_ms > 0 && now_ms >= e.next_state_ms {
                tracing::debug!(
                    "ZoneGroup {zone_group_id}: timer elapsed, current state={}, advancing...",
                    e.c.current_state
                );
                force_next_state(e, now_ms)
            } else {
                None
            }
        })
        .flatten()
    }

    /// `ZoneConflict.AddZoneKill()` + `SendSwitchZoneState`.
    pub fn add_zone_kill(&self, server: &GameServer, zone_group_id: u16) {
        if let Some(s) = self.add_zone_kill_at(zone_group_id, 1, now_ms()) {
            send_switch_zone_state(server, &s);
        }
    }

    /// `ZoneConflict.SetState(ct)` + `SendSwitchZoneState`.
    pub fn set_state(&self, server: &GameServer, zone_group_id: u16, ct: u8) {
        if let Some(s) = self.set_state_at(zone_group_id, ct, now_ms()) {
            send_switch_zone_state(server, &s);
        }
    }

    /// `ZoneConflict.ForceNextState()` + `SendSwitchZoneState`.
    pub fn force_next_state(&self, server: &GameServer, zone_group_id: u16) {
        if let Some(s) = self.force_next_state_at(zone_group_id, now_ms()) {
            send_switch_zone_state(server, &s);
        }
    }

    /// `ZoneStateChangeTask.Execute` -> `CheckTimer()`.
    pub fn check_timer(&self, server: &GameServer, zone_group_id: u16) {
        if let Some(s) = self.check_timer_at(zone_group_id, now_ms()) {
            send_switch_zone_state(server, &s);
        }
    }
}

fn switch(e: &Entry, now_ms: i64) -> SwitchZoneState {
    SwitchZoneState {
        zone_group_id: e.c.zone_group_id,
        state: e.c.current_state,
        next_state_time: e.c.next_state_time,
        timer_ms: (e.next_state_ms > 0).then(|| e.next_state_ms - now_ms),
    }
}

fn set_state(e: &mut Entry, ct: u8, now_ms: i64) -> Option<SwitchZoneState> {
    if ct == e.c.current_state {
        return None;
    }
    let previous = e.c.current_state;
    match ct {
        conflict_type::CONFLICT => {
            e.c.kill_count = 0;
            e.set_next(now_ms + i64::from(e.c.conflict_min) * 60_000);
        }
        conflict_type::WAR => {
            e.c.kill_count = 0;
            e.set_next(now_ms + i64::from(e.c.war_min) * 60_000);
        }
        conflict_type::PEACE => {
            e.c.kill_count = 0;
            e.set_next(now_ms + i64::from(e.c.peace_min) * 60_000);
        }
        _ => e.set_next(0),
    }
    e.c.current_state = ct;
    tracing::info!(
        "ZoneGroup {} changed from {previous} → {ct} (next state at {})",
        e.c.zone_group_id,
        e.c.next_state_time
    );
    Some(switch(e, now_ms))
}

fn force_next_state(e: &mut Entry, now_ms: i64) -> Option<SwitchZoneState> {
    if e.c.current_state < conflict_type::PEACE {
        if e.c.current_state == conflict_type::WAR && e.c.peace_min <= 0 {
            set_state(e, conflict_type::CONFLICT, now_ms)
        } else {
            set_state(e, e.c.current_state + 1, now_ms)
        }
    } else if !e.has_kill_counter() {
        // If it doesn't have a killcounter, go directly back to conflict (ocean areas)
        set_state(e, conflict_type::CONFLICT, now_ms)
    } else {
        set_state(e, conflict_type::TENSION, now_ms)
    }
}

/// `SetTimerTask()`: a `ZoneStateChangeTask` at `NextStateTime`.
fn schedule_timer(server: &GameServer, s: &SwitchZoneState) {
    let Some(delay) = s.timer_ms else {
        tracing::debug!(
            "ZoneGroup {}: no NextStateTime set — timer chain stopped.",
            s.zone_group_id
        );
        return;
    };
    tracing::debug!(
        "ZoneGroup {}: scheduling next state check in {:.1} min",
        s.zone_group_id,
        delay as f64 / 60_000.0
    );
    let (Some(arc), Ok(rt)) = (server.self_arc(), tokio::runtime::Handle::try_current()) else {
        return;
    };
    let weak = Arc::downgrade(&arc);
    drop(arc);
    let group = s.zone_group_id;
    let delay = Duration::from_millis(delay.max(0) as u64);
    rt.spawn(async move {
        tokio::time::sleep(delay).await;
        if let Some(server) = weak.upgrade() {
            server.systems.social.conflicts.check_timer(&server, group);
        }
    });
}

/// `SendSwitchZoneState()`: schedules the next timer first, then broadcasts
/// `SCConflictZoneStatePacket` to the whole server.
pub fn send_switch_zone_state(server: &GameServer, s: &SwitchZoneState) {
    schedule_timer(server, s);
    server
        .world
        .broadcast_to_server(&SCConflictZoneStatePacket {
            zone_group_id: s.zone_group_id,
            state: s.state,
            end_time: s.next_state_time,
        });
}

#[cfg(test)]
mod tests {
    use super::*;

    fn zone(num_kills: [i32; 5], state: u8) -> ZoneConflict {
        ZoneConflict {
            zone_group_id: 14,
            num_kills,
            conflict_min: 5,
            war_min: 80,
            peace_min: 120,
            current_state: state,
            ..Default::default()
        }
    }

    const NOW: i64 = 1_000_000_000_000;

    #[test]
    fn kills_climb_to_conflict() {
        let m = ZoneConflictManager::default();
        assert!(m
            .init(&[zone([2, 4, 6, 8, 10], conflict_type::TENSION)], NOW)
            .is_empty());
        // 1, 2 kills: still tension (KillCount > NumKills[0] needed)
        assert_eq!(m.add_zone_kill_at(14, 1, NOW), None);
        assert_eq!(m.add_zone_kill_at(14, 1, NOW), None);
        let s = m.add_zone_kill_at(14, 1, NOW).unwrap();
        assert_eq!(
            (s.state, s.timer_ms, s.next_state_time),
            (conflict_type::DANGER, None, 0)
        );
        // A burst can climb several steps at once.
        let s = m.add_zone_kill_at(14, 20, NOW).unwrap();
        assert_eq!(s.state, conflict_type::CONFLICT);
        assert_eq!(s.timer_ms, Some(5 * 60_000));
        assert_eq!(s.next_state_time, NOW / 1000 + 300);
        let c = m.get(14).unwrap();
        assert_eq!(c.kill_count, 0);
        // Ignored from Conflict on.
        assert_eq!(m.add_zone_kill_at(14, 50, NOW), None);
        assert_eq!(m.get(14).unwrap().kill_count, 0);
    }

    #[test]
    fn zones_without_kill_counter_ignore_kills() {
        let m = ZoneConflictManager::default();
        m.init(&[zone([0; 5], conflict_type::TENSION)], NOW);
        assert_eq!(m.add_zone_kill_at(14, 100, NOW), None);
        assert_eq!(m.get(14).unwrap().kill_count, 0);
    }

    #[test]
    fn timers_walk_conflict_war_peace_tension() {
        let m = ZoneConflictManager::default();
        let mut z = zone([2, 4, 6, 8, 10], conflict_type::CONFLICT);
        z.next_state_time = NOW / 1000 + 300;
        let timers = m.init(&[z], NOW);
        assert_eq!(timers[0].timer_ms, Some(300_000));
        // Too early: nothing.
        assert_eq!(m.check_timer_at(14, NOW + 299_999), None);
        let s = m.check_timer_at(14, NOW + 300_000).unwrap();
        assert_eq!(s.state, conflict_type::WAR);
        assert_eq!(s.timer_ms, Some(80 * 60_000));
        let t = NOW + 300_000 + 80 * 60_000;
        let s = m.check_timer_at(14, t).unwrap();
        assert_eq!(s.state, conflict_type::PEACE);
        let t2 = t + 120 * 60_000;
        let s = m.check_timer_at(14, t2).unwrap();
        assert_eq!((s.state, s.timer_ms), (conflict_type::TENSION, None));
        // No timer: CheckTimer does nothing.
        assert_eq!(m.check_timer_at(14, t2 + 10_000_000), None);
    }

    #[test]
    fn war_without_peace_returns_to_conflict_and_ocean_zones_skip_tension() {
        let m = ZoneConflictManager::default();
        let mut z = zone([0; 5], conflict_type::WAR);
        z.peace_min = 0;
        m.init(&[z], NOW);
        assert_eq!(
            m.force_next_state_at(14, NOW).unwrap().state,
            conflict_type::CONFLICT
        );
        assert_eq!(
            m.set_state_at(14, conflict_type::PEACE, NOW).unwrap().state,
            conflict_type::PEACE
        );
        // Peace of a zone without a kill counter goes straight back to Conflict.
        assert_eq!(
            m.force_next_state_at(14, NOW).unwrap().state,
            conflict_type::CONFLICT
        );
        // Setting the current state again is a no-op.
        assert_eq!(m.set_state_at(14, conflict_type::CONFLICT, NOW), None);
    }
}
