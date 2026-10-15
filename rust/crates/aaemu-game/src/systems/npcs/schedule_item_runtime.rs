//! `ScheduleItemManager` progress runtime: one account's `schedule_items` claim state
//! (`AAEmu.Game/Core/Managers/ScheduleItemManager.cs` at `7851f67cc`: `_progress`,
//! `NoteConnected` / `NoteDisconnected`, `TickOnline`, `TryCommitClaim`, `ResetIfNeeded`).
//!
//! Pure state transitions only — no SQL, no packets, no item/mail grants, no timers, no session
//! hooks. The future manager owns those: it restores the saved rows with
//! [`AccountScheduleProgress::from_saved`], reports world entry / leave with
//! [`AccountScheduleProgress::join`] / [`AccountScheduleProgress::leave`], forwards the periodic
//! in-world sample to [`AccountScheduleProgress::tick`], records a delivered receipt with
//! [`AccountScheduleProgress::commit_claim`], and persists
//! [`AccountScheduleProgress::save_rows`] / [`AccountScheduleProgress::dirty_rows`] inside its own
//! transaction.
//!
//! # Identity
//!
//! Progress is account wide, exactly like `account_schedule_items`: rows are keyed by
//! `schedule_id` for one `account_id`, so every character of an account sees and advances the same
//! `gave` / `cumulated`. Characters only appear in the transient session set.
//!
//! # The clock
//!
//! There is one elapsed-time clock per account ([`AccountScheduleProgress::account_clock`]), not
//! one per row or per character: the C# keeps a `LastOnlineTick` on every row and re-baselines all
//! of them from the account's connection, so two characters of one account can never accrue the
//! same second twice. [`AccountScheduleProgress::join`] starts the clock for the first in-world
//! character and records that character's own sample time; joining while another character is
//! already in-world leaves the running clock alone (no gap, no double). The clock stops when the
//! last session leaves, and the per-character sample times are transient bookkeeping that is never
//! saved, so time spent offline is never credited. `tick` credits the account clock's elapsed
//! seconds to every available row and caps each row at its own term
//! (`ScheduleItemRules.TickCumulated`).
//!
//! Unlike the 10.x `TickOnline` (active-take rows only) silent rows accrue too: the 1.2 table has
//! no account-buff auto-grant path, so a `give_term > 0` row without a take button would otherwise
//! never become ready. Which rows a receipt may come from is still the authored `active_take`
//! flag, checked by [`ClaimMode`].
//!
//! # Daily reset
//!
//! `updated` doubles as the UTC day key ([`AccountScheduleState::utc_day_key`]): the first access
//! after the day changed resets `gave` and `cumulated` (`ResetIfNeeded`), not the character select.
//! `join` / `leave` never touch rows. A reset row is not credited in the tick that resets it,
//! mirroring the C# order (`ResetIfNeeded` runs inside `Load`, before `SessionAddSeconds`).
//!
//! # Receipts (reserve / commit)
//!
//! [`AccountScheduleProgress::plan_claim`] is a pure reservation: it reports what a receipt would
//! pay and against which `gave` / `cumulated` it was reserved, and changes nothing. The caller
//! grants the item or mail first and then records the receipt with
//! [`AccountScheduleProgress::commit_claim`], which only applies while the row still matches the
//! reservation. A failed or abandoned grant therefore keeps the term ready for a retry, a retried
//! or duplicated receipt is rejected as [`ClaimCommit::Stale`], and no speculative receipt is ever
//! written (a plan is a value, never a stored reservation).
//!
//! # Corrupt state
//!
//! Restored counters are clamped to `0` and, once a definition is known, to the row's own bounds
//! ([`AccountScheduleState::normalize`]): `cumulated` to `0 ..= give_term * 60` and `gave` to
//! `0 ..= min(give_max, u8::MAX)`. A receipt is refused when the `u8` counter could not record it,
//! so a corrupt `give_max` can never wrap the counter. An unusable `updated` (`<= 0`) is treated as
//! "unknown day" and forces the next access to reset the row.
//!
//! # The HUD snapshot
//!
//! [`AccountScheduleProgress::build_update_rows`] builds the `SCScheduleItemUpdatePacket` (opcode
//! `0x1eb`) rows the 1.2 client expects: the HUD rows (`active_take`, the client's
//! `GetScheduleItemList(true)`) first, then the silent rows the BM-mileage info bar lists with
//! `GetScheduleItemList(false)`, each group in table order and capped at
//! `ScheduleItemRules.MaxItemsInPacket`. Every row is built from the same `now` and from
//! [`AccountScheduleProgress::effective_state`], so expired, disabled, grantless or ineligible rows
//! are never resurrected.

use std::collections::BTreeMap;

use crate::packets::g2c_misc::ScheduleItem;
use crate::systems::npcs::schedule_items::{
    can_take, has_session_tick, seconds_for_term, session_add_seconds, tick_cumulated,
    ScheduleEligibility, ScheduleItemData, ScheduleItemDef, MAX_ITEMS_IN_PACKET,
};

/// One saved row of `account_schedule_items` (`ScheduleItemManager.Progress` without its transient
/// `LastOnlineTick`): the account-wide counters of one `schedule_items` row.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct AccountScheduleState {
    /// `gave`: receipts already delivered for this row.
    pub gave: u8,
    /// `cumulated`: online seconds accrued toward the next receipt (capped at the term).
    pub cumulated: i64,
    /// `updated`: unix seconds UTC of the last accrual, receipt or reset. Also the row's day key.
    pub updated: i64,
}

impl AccountScheduleState {
    /// Seconds in one UTC day (`ScheduleItemRules.NeedsDailyReset`'s `ServerCalendar` period).
    pub const SECONDS_PER_UTC_DAY: i64 = 86_400;

    /// The UTC day key of a unix second (`div_euclid`, so pre-epoch stamps floor correctly).
    pub fn utc_day_key(now: i64) -> i64 {
        now.div_euclid(Self::SECONDS_PER_UTC_DAY)
    }

    /// This row's UTC day key (`updated`'s day).
    pub fn day_key(&self) -> i64 {
        Self::utc_day_key(self.updated)
    }

    /// `updated <= 0` is not a usable stamp: the row's day is unknown, so the next access resets it.
    pub fn has_unknown_day(&self) -> bool {
        self.updated <= 0
    }

    /// `ScheduleItemRules.NeedsDailyReset(lastUpdatedUtc, utcNow)`: a different UTC day (or an
    /// unusable stamp) resets the counters.
    pub fn needs_daily_reset(&self, now: i64) -> bool {
        self.has_unknown_day() || self.day_key() != Self::utc_day_key(now)
    }

    /// Clamps corrupt counters instead of wrapping them: `cumulated` into `0 ..= give_term * 60`
    /// (a non-positive term keeps it `0`), `gave` into `0 ..= min(give_max, u8::MAX)` (a
    /// non-positive `give_max` clears it). `updated` is left as authored so
    /// [`Self::needs_daily_reset`] still sees an unusable stamp.
    pub fn normalize(&mut self, def: &ScheduleItemDef) {
        let term = seconds_for_term(def.give_term);
        self.cumulated = if term <= 0 {
            0
        } else {
            self.cumulated.clamp(0, term)
        };
        let cap = u8::try_from(def.give_max.max(0)).unwrap_or(u8::MAX);
        self.gave = self.gave.min(cap);
    }

    /// `ScheduleItemRules.CanTake`: a receipt is left in the budget and a full term has accrued.
    pub fn term_ready(&self, def: &ScheduleItemDef) -> bool {
        can_take(self.gave, def.give_max, self.cumulated, def.give_term)
    }
}

/// Which authored path a receipt comes from (`ScheduleItemManager.HandleTake` versus
/// `AutoGrantSilent`): a manual take needs `active_take`, a server auto-grant needs a silent row.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ClaimMode {
    /// A player take, i.e. an `active_take` HUD row.
    Manual,
    /// A server auto-grant, i.e. a row without a take button (`active_take = false`).
    Auto,
}

/// A reserved receipt: everything the caller needs to grant the reward and to record it with
/// [`AccountScheduleProgress::commit_claim`]. It is a value, not a stored reservation — dropping it
/// changes nothing, so a failed grant stays retryable.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ClaimPlan {
    /// The `schedule_items` row (`scheduleType`).
    pub schedule_id: i32,
    /// The in-world character the receipt is for.
    pub character_id: u32,
    /// The authored path the receipt was planned for.
    pub mode: ClaimMode,
    /// The row's reward (`schedule_items.item_id`).
    pub item_id: u32,
    /// The row's reward count (`schedule_items.item_count`).
    pub item_count: i32,
    /// `gave` the reservation was made against.
    pub gave_before: u8,
    /// `cumulated` the reservation was made against.
    pub cumulated_before: i64,
    /// `gave` after a successful receipt (`gave_before + 1`, never wrapping).
    pub gave_after: u8,
}

/// The result of [`AccountScheduleProgress::commit_claim`].
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ClaimCommit {
    /// The delivered receipt was recorded (`gave + 1`, `cumulated = 0`, `updated = now`).
    Committed {
        schedule_id: i32,
        gave: u8,
        cumulated: i64,
        updated: i64,
    },
    /// The row no longer matches the reservation (another receipt, a daily reset, or a different
    /// accrual): nothing was written, so a delivered reward can never be receipted twice.
    Stale,
}

/// What one [`AccountScheduleProgress::tick`] changed.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct TickOutcome {
    /// Seconds the account clock advanced (`0` when nobody was in-world).
    pub elapsed: i64,
    /// Schedule ids whose accrual advanced, in table order.
    pub advanced: Vec<i32>,
    /// Schedule ids reset by the UTC day boundary; they were not credited in this tick.
    pub reset: Vec<i32>,
}

impl TickOutcome {
    /// Whether the tick changed any row (i.e. anything is worth saving / re-sending).
    pub fn changed(&self) -> bool {
        !self.advanced.is_empty() || !self.reset.is_empty()
    }
}

/// One account's schedule-item progress (`ScheduleItemManager._progress` for an account) plus the
/// transient session clock, which is deliberately not part of any save.
#[derive(Debug, Clone, Default)]
pub struct AccountScheduleProgress {
    account_id: u32,
    /// `(account_id, schedule_id) -> Progress`, the only persisted part.
    rows: BTreeMap<i32, AccountScheduleState>,
    /// character id -> its own last in-world sample time; transient.
    sessions: BTreeMap<u32, i64>,
    /// The single account accrual clock (`0` = nobody in-world); transient.
    clock: i64,
    /// `schedule_id -> mark stamp` (`_dirty`); transient.
    dirty: BTreeMap<i32, u64>,
    stamp: u64,
}

impl AccountScheduleProgress {
    /// An account with no saved rows and nobody in-world.
    pub fn new(account_id: u32) -> Self {
        Self {
            account_id,
            ..Self::default()
        }
    }

    /// Restores one account's saved rows (`account_schedule_items`). The session clock stays idle
    /// and no character is online, so nothing is credited before the next `join`. A later row of a
    /// duplicated `schedule_id` replaces the earlier one; negative `cumulated` values are clamped
    /// (the per-term caps need a definition and are applied by [`Self::effective_state`]).
    pub fn from_saved(
        account_id: u32,
        rows: impl IntoIterator<Item = (i32, AccountScheduleState)>,
    ) -> Self {
        let mut progress = Self::new(account_id);
        for (schedule_id, state) in rows {
            progress.rows.insert(
                schedule_id,
                AccountScheduleState {
                    cumulated: state.cumulated.max(0),
                    ..state
                },
            );
        }
        progress
    }

    /// The account this progress belongs to.
    pub fn account_id(&self) -> u32 {
        self.account_id
    }

    /// Every row needed to save this account, in `schedule_id` order (`account_schedule_items`:
    /// `account_id`, `schedule_id`, `gave`, `cumulated`, `updated`). Contains no session baseline.
    pub fn save_rows(&self) -> Vec<(i32, AccountScheduleState)> {
        self.rows.iter().map(|(id, state)| (*id, *state)).collect()
    }

    /// The saved row of one schedule id, if the account has one.
    pub fn state(&self, schedule_id: i32) -> Option<&AccountScheduleState> {
        self.rows.get(&schedule_id)
    }

    /// The row as an access sees it (`TryLoad` + `ResetIfNeeded` + the C# clamps): a missing row is
    /// the fresh `gave` 0 / `cumulated` 0 / `updated = now`, a row of an earlier UTC day is the
    /// same fresh value, and a corrupt one is normalized against `def`. Pure: nothing is written.
    pub fn effective_state(&self, def: &ScheduleItemDef, now: i64) -> AccountScheduleState {
        let fresh = AccountScheduleState {
            updated: now,
            ..AccountScheduleState::default()
        };
        let Some(state) = self.rows.get(&def.id) else {
            return fresh;
        };
        if state.needs_daily_reset(now) {
            return fresh;
        }
        let mut state = *state;
        state.normalize(def);
        state
    }

    /// `_dirty` snapshot: `(schedule_id, stamp)` of every row changed since the last
    /// [`Self::mark_saved`], in schedule id order.
    pub fn dirty_rows(&self) -> Vec<(i32, u64)> {
        self.dirty.iter().map(|(id, stamp)| (*id, *stamp)).collect()
    }

    /// `ScheduleItemManager.ConfirmSaved`: clears the marks of a save snapshot; a row whose stamp
    /// changed after the snapshot stays dirty.
    pub fn mark_saved(&mut self, rows: &[(i32, u64)]) {
        for &(schedule_id, stamp) in rows {
            if self.dirty.get(&schedule_id) == Some(&stamp) {
                self.dirty.remove(&schedule_id);
            }
        }
    }

    /// A character entered the world (world entry, character select or reconnect). The first
    /// session starts the account clock at `now`; a session that joins while another character is
    /// already in-world leaves the running clock alone, so overlapping characters never accrue the
    /// same second twice. A non-positive `now` records the session but cannot start the clock (no
    /// accrual until every session ends and a valid one restarts it).
    pub fn join(&mut self, character_id: u32, now: i64) {
        let first = self.sessions.is_empty();
        self.sessions.insert(character_id, now);
        if first && now > 0 {
            self.clock = now;
        }
    }

    /// A character left the world (logout or character select). When the last session ends the
    /// account clock stops, so the offline gap is never credited. Returns whether this call
    /// stopped the clock.
    pub fn leave(&mut self, character_id: u32) -> bool {
        if self.sessions.remove(&character_id).is_none() {
            return false;
        }
        if !self.sessions.is_empty() {
            return false;
        }
        let stopped = has_session_tick(self.clock);
        self.clock = 0;
        stopped
    }

    /// Whether `character_id` is currently in-world for this account.
    pub fn is_online(&self, character_id: u32) -> bool {
        self.sessions.contains_key(&character_id)
    }

    /// How many characters of this account are currently in-world.
    pub fn online_characters(&self) -> usize {
        self.sessions.len()
    }

    /// The in-world characters of this account, in character id order.
    pub fn online_character_ids(&self) -> Vec<u32> {
        self.sessions.keys().copied().collect()
    }

    /// The account accrual clock: the last sampled in-world second, or `0` when nobody is online.
    pub fn account_clock(&self) -> i64 {
        self.clock
    }

    /// Applies one in-world sample (`ScheduleItemManager.TickOnline`): the account's elapsed
    /// seconds are credited to every available row exactly once and capped at each row's term; a
    /// row whose UTC day changed is reset instead of credited (`ResetIfNeeded` runs before
    /// `SessionAddSeconds` in the C#).
    ///
    /// Nothing happens while the account is idle, so the offline gap is never credited (a
    /// reconnect starts a new baseline). Silent rows accrue as well; see the module documentation.
    pub fn tick(
        &mut self,
        now: i64,
        data: &ScheduleItemData,
        evidence: &ScheduleEligibility,
    ) -> TickOutcome {
        let elapsed = session_add_seconds(self.clock, now);
        if elapsed <= 0 {
            return TickOutcome::default();
        }
        self.clock = now;
        let mut outcome = TickOutcome {
            elapsed,
            ..TickOutcome::default()
        };
        for def in &data.items {
            if def.give_term <= 0 || !def.is_available(now, evidence) {
                continue;
            }
            let state = self.rows.entry(def.id).or_insert(AccountScheduleState {
                updated: now,
                ..AccountScheduleState::default()
            });
            if state.needs_daily_reset(now) {
                state.gave = 0;
                state.cumulated = 0;
                state.updated = now;
                outcome.reset.push(def.id);
                Self::bump_dirty(&mut self.dirty, &mut self.stamp, def.id);
                continue;
            }
            state.normalize(def);
            let next = tick_cumulated(state.cumulated, def.give_term, elapsed);
            if next == state.cumulated {
                continue;
            }
            state.cumulated = next;
            state.updated = now;
            outcome.advanced.push(def.id);
            Self::bump_dirty(&mut self.dirty, &mut self.stamp, def.id);
        }
        outcome
    }

    /// Reserves one receipt without touching the account: the caller grants the reward and records
    /// it with [`Self::commit_claim`]. `None` when the row is unknown, the character is not
    /// in-world, the row is not available (`on_air`, a real grant, an open authored window and the
    /// eligibility evidence), the mode does not match `active_take`, the term or the allowance is
    /// not ready, or the `u8` counter could not record another receipt.
    pub fn plan_claim(
        &self,
        data: &ScheduleItemData,
        schedule_id: i32,
        character_id: u32,
        mode: ClaimMode,
        now: i64,
        evidence: &ScheduleEligibility,
    ) -> Option<ClaimPlan> {
        let def = data.get(schedule_id)?;
        if !self.is_online(character_id) {
            return None;
        }
        if !def.is_available(now, evidence) {
            return None;
        }
        match mode {
            ClaimMode::Manual if !def.active_take => return None,
            ClaimMode::Auto if def.active_take => return None,
            _ => {}
        }
        let state = self.effective_state(def, now);
        if state.gave == u8::MAX {
            return None;
        }
        if !state.term_ready(def) {
            return None;
        }
        Some(ClaimPlan {
            schedule_id: def.id,
            character_id,
            mode,
            item_id: def.item_id,
            item_count: def.item_count,
            gave_before: state.gave,
            cumulated_before: state.cumulated,
            gave_after: state.gave.saturating_add(1),
        })
    }

    /// Records a receipt the caller has already delivered (`TryCommitClaim`'s success path:
    /// `gave + 1`, `cumulated = 0`, `updated = now`). The reservation is applied only while the row
    /// still matches its `gave` / `cumulated`, so a retried, duplicated or superseded receipt is
    /// [`ClaimCommit::Stale`] and changes nothing.
    ///
    /// The authored window and eligibility are deliberately not re-checked: the reward was already
    /// delivered, and refusing the receipt would let the row be claimed again. A UTC day boundary
    /// between plan and commit resets the row, which also makes the stale reservation visible
    /// instead of writing yesterday's counter into the new day. `def` is used to normalize a corrupt
    /// row into the same view [`Self::plan_claim`] saw.
    pub fn commit_claim(
        &mut self,
        data: &ScheduleItemData,
        plan: &ClaimPlan,
        now: i64,
    ) -> ClaimCommit {
        let Some(def) = data.get(plan.schedule_id) else {
            return ClaimCommit::Stale;
        };
        let fresh = AccountScheduleState {
            updated: now,
            ..AccountScheduleState::default()
        };
        let stored = self.rows.get(&plan.schedule_id).copied();
        let mut current = match stored {
            Some(state) if !state.needs_daily_reset(now) => state,
            _ => fresh,
        };
        current.normalize(def);
        if current.gave != plan.gave_before || current.cumulated != plan.cumulated_before {
            return ClaimCommit::Stale;
        }
        let row = self.rows.entry(plan.schedule_id).or_insert(fresh);
        row.gave = plan.gave_after;
        row.cumulated = 0;
        row.updated = now;
        Self::bump_dirty(&mut self.dirty, &mut self.stamp, plan.schedule_id);
        ClaimCommit::Committed {
            schedule_id: plan.schedule_id,
            gave: row.gave,
            cumulated: row.cumulated,
            updated: row.updated,
        }
    }

    /// The `SCScheduleItemUpdatePacket` (`0x1eb`) rows of this account at `now`
    /// (`ScheduleItemManager.SendActive`, plus the silent rows the 1.2 BM-mileage info bar reads
    /// with `GetScheduleItemList(false)`): every available row with its current `gave` /
    /// `cumulated`, at most `ScheduleItemRules.MaxItemsInPacket` rows.
    ///
    /// `active_take` rows come first (the HUD timer list), then the silent rows, each group in
    /// table order, so the packet is deterministic. Every row is built from the same `now`; a row
    /// without state reports the fresh one ([`Self::effective_state`]) and is not materialized.
    /// Expired, disabled, grantless and ineligible rows are never sent.
    pub fn build_update_rows(
        &self,
        data: &ScheduleItemData,
        now: i64,
        evidence: &ScheduleEligibility,
    ) -> Vec<ScheduleItem> {
        let mut rows = Vec::new();
        for active_take in [true, false] {
            for def in &data.items {
                if def.active_take != active_take || !def.is_available(now, evidence) {
                    continue;
                }
                let Ok(item_template_id) = u32::try_from(def.id) else {
                    continue;
                };
                let state = self.effective_state(def, now);
                rows.push(ScheduleItem {
                    item_template_id,
                    gave: state.gave,
                    acumulated: i32::try_from(state.cumulated).unwrap_or(i32::MAX),
                    updated: state.updated,
                });
                if rows.len() >= MAX_ITEMS_IN_PACKET {
                    return rows;
                }
            }
        }
        rows
    }

    /// Marks one row dirty with the next stamp (`ScheduleItemManager.MarkDirty`).
    fn bump_dirty(dirty: &mut BTreeMap<i32, u64>, stamp: &mut u64, schedule_id: i32) {
        *stamp = stamp.wrapping_add(1);
        dirty.insert(schedule_id, *stamp);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::systems::npcs::schedule_items::ScheduleItemKind;

    /// Unix seconds of a UTC stamp.
    fn ts(s: &str) -> i64 {
        chrono::NaiveDateTime::parse_from_str(s, "%Y-%m-%d %H:%M:%S")
            .unwrap()
            .and_utc()
            .timestamp()
    }

    /// A live kind-1 HUD row: 2026-09-06 00:00 .. 2026-09-10 00:00 UTC, a 30 minute term, at most
    /// three receipts of 2x `1000 + id`.
    fn live(id: i32) -> ScheduleItemDef {
        ScheduleItemDef {
            id,
            name: format!("live-{id}"),
            kind: ScheduleItemKind::Every,
            st_year: 2026,
            st_month: 9,
            st_day: 6,
            ed_year: 2026,
            ed_month: 9,
            ed_day: 10,
            give_term: 30,
            give_max: 3,
            item_id: 1000 + u32::try_from(id).unwrap(),
            item_count: 2,
            active_take: true,
            on_air: true,
            ..ScheduleItemDef::default()
        }
    }

    /// The same row without a take button (`ScheduleItemGameData.SilentOnAir`).
    fn silent(id: i32) -> ScheduleItemDef {
        ScheduleItemDef {
            active_take: false,
            ..live(id)
        }
    }

    /// An account with character 1 in-world since `start`.
    fn online(account_id: u32, start: i64) -> AccountScheduleProgress {
        let mut progress = AccountScheduleProgress::new(account_id);
        progress.join(1, start);
        progress
    }

    /// The schedule ids of a snapshot, in packet order.
    fn ids(rows: &[ScheduleItem]) -> Vec<i32> {
        rows.iter().map(|r| r.item_template_id as i32).collect()
    }

    #[test]
    fn a_live_row_accrues_one_term_then_pays_through_the_reservation() {
        let data = ScheduleItemData::from_rows(vec![live(28)]);
        let none = ScheduleEligibility::default();
        let start = ts("2026-09-06 01:00:00");
        let mut progress = online(7, start);

        // One minute in: the term is not full and nothing can be reserved.
        let out = progress.tick(start + 60, &data, &none);
        assert_eq!(out.elapsed, 60);
        assert_eq!(out.advanced, vec![28]);
        assert!(out.changed());
        assert_eq!(progress.state(28).unwrap().cumulated, 60);
        assert!(progress
            .plan_claim(&data, 28, 1, ClaimMode::Manual, start + 60, &none)
            .is_none());

        // A full 30 minute term (1800 s) makes the row ready; the reservation is pure.
        progress.tick(start + 1800, &data, &none);
        assert_eq!(progress.state(28).unwrap().cumulated, 1800);
        let plan = progress
            .plan_claim(&data, 28, 1, ClaimMode::Manual, start + 1800, &none)
            .expect("ready");
        assert_eq!(plan.schedule_id, 28);
        assert_eq!(plan.character_id, 1);
        assert_eq!(plan.mode, ClaimMode::Manual);
        assert_eq!((plan.item_id, plan.item_count), (1028, 2));
        assert_eq!(plan.gave_before, 0);
        assert_eq!(plan.cumulated_before, 1800);
        assert_eq!(plan.gave_after, 1);
        assert_eq!(
            progress.state(28).unwrap(),
            &AccountScheduleState {
                gave: 0,
                cumulated: 1800,
                updated: start + 1800,
            }
        );

        // The delivered receipt is recorded once ...
        assert_eq!(
            progress.commit_claim(&data, &plan, start + 1801),
            ClaimCommit::Committed {
                schedule_id: 28,
                gave: 1,
                cumulated: 0,
                updated: start + 1801,
            }
        );
        // ... and a duplicate of the same reservation cannot pay twice.
        assert_eq!(
            progress.commit_claim(&data, &plan, start + 1802),
            ClaimCommit::Stale
        );
        assert_eq!(progress.state(28).unwrap().gave, 1);
        assert_eq!(progress.state(28).unwrap().cumulated, 0);

        // Two more terms pay twice, then the authored budget is spent (`give_max` = 3). The clock
        // keeps running from the last tick (the commit does not sample time).
        let mut now = start + 1800;
        for gave in 2..=3u8 {
            now += 1800;
            assert_eq!(progress.tick(now, &data, &none).elapsed, 1800);
            let plan = progress
                .plan_claim(&data, 28, 1, ClaimMode::Manual, now, &none)
                .expect("ready");
            assert_eq!(plan.gave_before, gave - 1);
            assert_eq!(plan.gave_after, gave);
            assert!(matches!(
                progress.commit_claim(&data, &plan, now),
                ClaimCommit::Committed { gave: g, .. } if g == gave
            ));
        }
        now += 1800;
        assert_eq!(progress.tick(now, &data, &none).elapsed, 1800);
        assert_eq!(progress.state(28).unwrap().gave, 3);
        assert_eq!(progress.state(28).unwrap().cumulated, 1800);
        assert!(progress
            .plan_claim(&data, 28, 1, ClaimMode::Manual, now, &none)
            .is_none());
    }

    #[test]
    fn manual_and_auto_receipts_need_the_matching_active_take_flag() {
        let data = ScheduleItemData::from_rows(vec![live(1), silent(2)]);
        let none = ScheduleEligibility::default();
        let start = ts("2026-09-06 01:00:00");
        let mut progress = online(7, start);

        // Silent rows accrue too, so both rows are ready after one term.
        let out = progress.tick(start + 1800, &data, &none);
        assert_eq!(out.advanced, vec![1, 2]);
        assert_eq!(progress.state(1).unwrap().cumulated, 1800);
        assert_eq!(progress.state(2).unwrap().cumulated, 1800);

        let now = start + 1800;
        assert!(progress
            .plan_claim(&data, 1, 1, ClaimMode::Manual, now, &none)
            .is_some());
        assert!(progress
            .plan_claim(&data, 1, 1, ClaimMode::Auto, now, &none)
            .is_none());
        assert!(progress
            .plan_claim(&data, 2, 1, ClaimMode::Auto, now, &none)
            .is_some());
        assert!(progress
            .plan_claim(&data, 2, 1, ClaimMode::Manual, now, &none)
            .is_none());
    }

    #[test]
    fn overlapping_characters_share_one_clock_and_offline_time_is_never_credited() {
        let data = ScheduleItemData::from_rows(vec![live(1)]);
        let none = ScheduleEligibility::default();
        let start = ts("2026-09-06 01:00:00");
        let mut progress = AccountScheduleProgress::new(7);
        progress.join(1, start);
        progress.join(2, start + 10);
        assert!(progress.is_online(1) && progress.is_online(2));
        assert_eq!(progress.online_characters(), 2);
        // The second character did not restart the shared clock: one minute is credited once.
        assert_eq!(progress.account_clock(), start);
        assert_eq!(progress.tick(start + 60, &data, &none).elapsed, 60);
        assert_eq!(progress.account_clock(), start + 60);
        assert_eq!(progress.state(1).unwrap().cumulated, 60);

        // One character leaves; the shared clock keeps running for the other.
        assert!(!progress.leave(1));
        assert_eq!(progress.account_clock(), start + 60);
        assert_eq!(progress.tick(start + 120, &data, &none).elapsed, 60);
        assert_eq!(progress.state(1).unwrap().cumulated, 120);

        // The last session ends: the clock stops and the offline gap is not credited.
        assert!(progress.leave(2));
        assert_eq!(progress.account_clock(), 0);
        assert_eq!(
            progress.tick(start + 5000, &data, &none),
            TickOutcome::default()
        );
        assert_eq!(progress.state(1).unwrap().cumulated, 120);

        // A reconnect starts a new baseline: only the new online seconds count.
        progress.join(1, start + 5000);
        assert_eq!(progress.account_clock(), start + 5000);
        assert_eq!(progress.tick(start + 5010, &data, &none).elapsed, 10);
        assert_eq!(progress.state(1).unwrap().cumulated, 130);
        assert!(progress.leave(1));
        assert!(!progress.leave(1));
        assert_eq!(progress.account_clock(), 0);
    }

    #[test]
    fn the_utc_day_boundary_resets_a_ready_row_but_a_character_select_does_not() {
        let def = live(1);
        let data = ScheduleItemData::from_rows(vec![def.clone()]);
        let none = ScheduleEligibility::default();
        let mut progress = online(7, ts("2026-09-06 23:00:00"));
        let ready = ts("2026-09-06 23:30:00");
        progress.tick(ready, &data, &none);
        assert_eq!(progress.state(1).unwrap().cumulated, 1800);
        let plan = progress
            .plan_claim(&data, 1, 1, ClaimMode::Manual, ready, &none)
            .unwrap();
        assert!(matches!(
            progress.commit_claim(&data, &plan, ready),
            ClaimCommit::Committed { gave: 1, .. }
        ));

        // A character select / reconnect on the same UTC day changes nothing.
        assert!(progress.leave(1));
        progress.join(1, ts("2026-09-06 23:40:00"));
        assert_eq!(
            progress.effective_state(&def, ts("2026-09-06 23:41:00")),
            AccountScheduleState {
                gave: 1,
                cumulated: 0,
                updated: ready,
            }
        );
        progress.tick(ts("2026-09-06 23:59:00"), &data, &none);
        assert_eq!(progress.state(1).unwrap().cumulated, 1140);

        // The new UTC day resets the row and never credits the tick that resets it.
        let after = ts("2026-09-07 00:00:10");
        let out = progress.tick(after, &data, &none);
        assert_eq!(out.elapsed, 70);
        assert_eq!(out.reset, vec![1]);
        assert!(out.advanced.is_empty());
        assert_eq!(
            progress.state(1).unwrap(),
            &AccountScheduleState {
                gave: 0,
                cumulated: 0,
                updated: after,
            }
        );

        // Accrual resumes inside the new day.
        assert_eq!(progress.tick(after + 300, &data, &none).elapsed, 300);
        assert_eq!(progress.state(1).unwrap().cumulated, 300);
    }

    #[test]
    fn a_failed_grant_stays_retryable_and_a_superseded_receipt_is_stale() {
        let data = ScheduleItemData::from_rows(vec![live(1)]);
        let none = ScheduleEligibility::default();
        let start = ts("2026-09-06 01:00:00");
        let mut progress = online(7, start);
        let ready = start + 1800;
        progress.tick(ready, &data, &none);

        // The first grant fails: the caller drops the plan and the term stays ready.
        let failed = progress
            .plan_claim(&data, 1, 1, ClaimMode::Manual, ready, &none)
            .unwrap();
        assert_eq!(
            progress.state(1).unwrap(),
            &AccountScheduleState {
                gave: 0,
                cumulated: 1800,
                updated: ready,
            }
        );
        let retry = progress
            .plan_claim(&data, 1, 1, ClaimMode::Manual, ready, &none)
            .unwrap();
        assert_eq!(retry, failed);
        assert!(matches!(
            progress.commit_claim(&data, &retry, ready),
            ClaimCommit::Committed { gave: 1, .. }
        ));

        // The duplicate of the recorded reservation cannot pay again, and the term restarted.
        assert_eq!(
            progress.commit_claim(&data, &retry, ready),
            ClaimCommit::Stale
        );
        assert_eq!(progress.state(1).unwrap().gave, 1);
        assert!(progress
            .plan_claim(&data, 1, 1, ClaimMode::Manual, ready + 1, &none)
            .is_none());

        // A second term pays the second receipt.
        let second_ready = ready + 1800;
        progress.tick(second_ready, &data, &none);
        let second = progress
            .plan_claim(&data, 1, 1, ClaimMode::Manual, second_ready, &none)
            .unwrap();
        assert_eq!(second.gave_before, 1);
        assert!(matches!(
            progress.commit_claim(&data, &second, second_ready),
            ClaimCommit::Committed { gave: 2, .. }
        ));

        // Two reservations of one term: the first wins, the second is stale, so a single term can
        // never pay twice.
        let third_ready = second_ready + 1800;
        progress.tick(third_ready, &data, &none);
        let a = progress
            .plan_claim(&data, 1, 1, ClaimMode::Manual, third_ready, &none)
            .unwrap();
        let b = progress
            .plan_claim(&data, 1, 1, ClaimMode::Manual, third_ready, &none)
            .unwrap();
        assert_eq!(a, b);
        assert!(matches!(
            progress.commit_claim(&data, &a, third_ready),
            ClaimCommit::Committed { gave: 3, .. }
        ));
        assert_eq!(
            progress.commit_claim(&data, &b, third_ready),
            ClaimCommit::Stale
        );

        // The authored budget is spent and the counter stopped at `give_max`.
        assert_eq!(progress.state(1).unwrap().gave, 3);
        let after = third_ready + 1800;
        progress.tick(after, &data, &none);
        assert_eq!(progress.state(1).unwrap().gave, 3);
        assert!(progress
            .plan_claim(&data, 1, 1, ClaimMode::Manual, after, &none)
            .is_none());
    }

    #[test]
    fn expired_ineligible_malformed_and_grantless_rows_are_never_planned_or_sent() {
        let none = ScheduleEligibility::default();
        let now = ts("2026-09-06 12:00:00");

        let mut expired = live(1);
        expired.ed_year = 2025;
        expired.ed_month = 1;
        expired.ed_day = 1;
        let mut malformed = live(2);
        malformed.st_month = 2;
        malformed.st_day = 30;
        let mut premium = silent(3);
        premium.kind = ScheduleItemKind::Premium;
        premium.premium_grade_id = 2;
        let mut pc_bang = silent(4);
        pc_bang.kind = ScheduleItemKind::PcBang;
        let quiet = silent(5);
        let mut grantless = live(6);
        grantless.item_id = 0;

        let data = ScheduleItemData::from_rows(vec![
            expired, malformed, premium, pc_bang, quiet, grantless,
        ]);
        let mut progress = online(7, ts("2026-09-06 11:00:00"));
        let out = progress.tick(now, &data, &none);
        assert_eq!(out.advanced, vec![5]);
        for id in [1, 2, 3, 4, 6] {
            assert!(
                progress
                    .plan_claim(&data, id, 1, ClaimMode::Auto, now, &none)
                    .is_none(),
                "{id}"
            );
            assert_eq!(progress.state(id), None, "{id}");
        }
        assert!(progress
            .plan_claim(&data, 5, 1, ClaimMode::Auto, now, &none)
            .is_some());

        // The snapshot sends exactly the available rows.
        assert_eq!(ids(&progress.build_update_rows(&data, now, &none)), vec![5]);

        // Eligibility evidence turns the gated kinds on while their windows are open.
        let premium_evidence = ScheduleEligibility {
            paid_patron: true,
            premium_grade_id: 2,
            pc_bang: false,
        };
        let pc_bang_evidence = ScheduleEligibility {
            pc_bang: true,
            ..ScheduleEligibility::default()
        };
        // Gated rows are sent and accrue once the evidence is passed in.
        assert_eq!(
            ids(&progress.build_update_rows(&data, now, &premium_evidence)),
            vec![3, 5]
        );
        assert_eq!(
            ids(&progress.build_update_rows(&data, now, &pc_bang_evidence)),
            vec![4, 5]
        );
        let premium_ready = now + 1800;
        assert_eq!(
            progress
                .tick(premium_ready, &data, &premium_evidence)
                .advanced,
            vec![3]
        );
        let pc_bang_ready = premium_ready + 1800;
        assert_eq!(
            progress
                .tick(pc_bang_ready, &data, &pc_bang_evidence)
                .advanced,
            vec![4]
        );
        assert_eq!(progress.state(3).unwrap().cumulated, 1800);
        assert_eq!(progress.state(4).unwrap().cumulated, 1800);
        assert!(progress
            .plan_claim(
                &data,
                3,
                1,
                ClaimMode::Auto,
                premium_ready,
                &premium_evidence
            )
            .is_some());
        assert!(progress
            .plan_claim(
                &data,
                4,
                1,
                ClaimMode::Auto,
                pc_bang_ready,
                &pc_bang_evidence
            )
            .is_some());
        assert!(progress
            .plan_claim(
                &data,
                3,
                1,
                ClaimMode::Auto,
                premium_ready,
                &pc_bang_evidence
            )
            .is_none());
        assert!(progress
            .plan_claim(
                &data,
                4,
                1,
                ClaimMode::Auto,
                pc_bang_ready,
                &premium_evidence
            )
            .is_none());

        // A later `now` cannot resurrect an ended event.
        let later = ts("2026-09-20 12:00:00");
        assert!(progress
            .build_update_rows(&data, later, &premium_evidence)
            .is_empty());
        assert!(progress
            .plan_claim(&data, 3, 1, ClaimMode::Auto, later, &premium_evidence)
            .is_none());
    }

    #[test]
    fn corrupt_counters_are_clamped_and_the_u8_counter_never_wraps() {
        let def = live(1);
        let data = ScheduleItemData::from_rows(vec![def.clone()]);
        let none = ScheduleEligibility::default();
        let day = ts("2026-09-06 11:00:00");

        // An over-budget `gave` and an over-term `cumulated` are clamped to the authored bounds.
        let progress = AccountScheduleProgress::from_saved(
            7,
            [(
                1,
                AccountScheduleState {
                    gave: 200,
                    cumulated: 99_999,
                    updated: day,
                },
            )],
        );
        let effective = progress.effective_state(&def, day + 60);
        assert_eq!(
            effective,
            AccountScheduleState {
                gave: 3,
                cumulated: 1800,
                updated: day,
            }
        );
        assert!(!effective.term_ready(&def)); // the clamped budget is spent

        // A negative `cumulated` is zero, not a negative countdown.
        let progress = AccountScheduleProgress::from_saved(
            7,
            [(
                1,
                AccountScheduleState {
                    gave: 0,
                    cumulated: -500,
                    updated: day,
                },
            )],
        );
        let effective = progress.effective_state(&def, day + 60);
        assert_eq!(effective.cumulated, 0);
        assert!(!effective.term_ready(&def));

        // An unusable `updated` is an unknown day: the first tick resets the row instead of
        // trusting the corrupt counters.
        let mut progress = AccountScheduleProgress::from_saved(
            7,
            [(
                1,
                AccountScheduleState {
                    gave: 5,
                    cumulated: 99_999,
                    updated: -1,
                },
            )],
        );
        progress.join(1, day);
        let out = progress.tick(day + 60, &data, &none);
        assert_eq!(out.elapsed, 60);
        assert_eq!(out.reset, vec![1]);
        assert_eq!(
            progress.state(1).unwrap(),
            &AccountScheduleState {
                gave: 0,
                cumulated: 0,
                updated: day + 60,
            }
        );

        // A non-positive `give_max` clears the counter and can never pay.
        let mut no_budget = live(2);
        no_budget.give_max = -1;
        no_budget.give_term = 0;
        let no_budget_data = ScheduleItemData::from_rows(vec![no_budget]);
        let mut progress = AccountScheduleProgress::from_saved(
            7,
            [(
                2,
                AccountScheduleState {
                    gave: 9,
                    cumulated: 0,
                    updated: day,
                },
            )],
        );
        progress.join(1, day);
        assert_eq!(
            progress
                .effective_state(no_budget_data.get(2).unwrap(), day + 1)
                .gave,
            0
        );
        assert!(progress
            .plan_claim(&no_budget_data, 2, 1, ClaimMode::Manual, day, &none)
            .is_none());

        // A corrupt `give_max` above `u8::MAX` cannot wrap the counter: the last representable
        // receipt is recorded, then the row stops paying instead of starting over.
        let mut huge = live(3);
        huge.give_max = 300;
        huge.give_term = 0;
        let huge_data = ScheduleItemData::from_rows(vec![huge]);
        let mut progress = AccountScheduleProgress::from_saved(
            7,
            [(
                3,
                AccountScheduleState {
                    gave: 255,
                    cumulated: 0,
                    updated: day,
                },
            )],
        );
        progress.join(1, day);
        assert!(progress
            .plan_claim(&huge_data, 3, 1, ClaimMode::Manual, day + 1, &none)
            .is_none());

        let mut progress = AccountScheduleProgress::from_saved(
            7,
            [(
                3,
                AccountScheduleState {
                    gave: 254,
                    cumulated: 0,
                    updated: day,
                },
            )],
        );
        progress.join(1, day);
        let plan = progress
            .plan_claim(&huge_data, 3, 1, ClaimMode::Manual, day + 1, &none)
            .unwrap();
        assert_eq!(plan.gave_after, 255);
        assert!(matches!(
            progress.commit_claim(&huge_data, &plan, day + 1),
            ClaimCommit::Committed { gave: 255, .. }
        ));
        assert!(progress
            .plan_claim(&huge_data, 3, 1, ClaimMode::Manual, day + 2, &none)
            .is_none());
    }

    #[test]
    fn the_update_snapshot_is_deterministic_capped_and_shares_one_clock() {
        let none = ScheduleEligibility::default();
        let now = ts("2026-09-06 12:00:00");
        let mut defs = Vec::new();
        for id in 1..=20 {
            defs.push(live(id));
        }
        for id in 21..=40 {
            defs.push(silent(id));
        }
        let data = ScheduleItemData::from_rows(defs);
        let mut progress = AccountScheduleProgress::from_saved(
            7,
            [
                (
                    2,
                    AccountScheduleState {
                        gave: 5,
                        cumulated: -10,
                        updated: now - 60,
                    },
                ),
                (
                    30,
                    AccountScheduleState {
                        gave: 1,
                        cumulated: 600,
                        updated: now - 60,
                    },
                ),
            ],
        );
        progress.join(1, ts("2026-09-06 11:00:00"));

        let rows = progress.build_update_rows(&data, now, &none);
        // 20 HUD rows first, then 12 of the 20 silent rows: the packet stops at 32.
        assert_eq!(rows.len(), MAX_ITEMS_IN_PACKET);
        assert_eq!(
            ids(&rows),
            (1..=32).collect::<Vec<i32>>(),
            "HUD rows first, then silent rows, each in table order"
        );
        // Saved counters are reported (corrupt ones clamped), the silent row is included, and a
        // row without state shares the snapshot clock.
        assert_eq!(
            rows[1],
            ScheduleItem {
                item_template_id: 2,
                gave: 3,
                acumulated: 0,
                updated: now - 60,
            }
        );
        assert_eq!(
            rows[29],
            ScheduleItem {
                item_template_id: 30,
                gave: 1,
                acumulated: 600,
                updated: now - 60,
            }
        );
        assert_eq!(
            rows[19],
            ScheduleItem {
                item_template_id: 20,
                gave: 0,
                acumulated: 0,
                updated: now,
            }
        );
        assert_eq!(
            rows[31],
            ScheduleItem {
                item_template_id: 32,
                gave: 0,
                acumulated: 0,
                updated: now,
            }
        );
        // The same state and clock build the same packet.
        assert_eq!(progress.build_update_rows(&data, now, &none), rows);
        assert_eq!(progress.build_update_rows(&data, now, &none), rows);

        // `cumulated` is written as the `i32` wire field without wrapping.
        let mut huge = live(41);
        huge.give_term = i32::MAX;
        let huge_data = ScheduleItemData::from_rows(vec![huge]);
        let progress = AccountScheduleProgress::from_saved(
            7,
            [(
                41,
                AccountScheduleState {
                    gave: 0,
                    cumulated: i64::MAX,
                    updated: now,
                },
            )],
        );
        let rows = progress.build_update_rows(&huge_data, now, &none);
        assert_eq!(rows.len(), 1);
        assert_eq!(rows[0].acumulated, i32::MAX);
    }

    #[test]
    fn saved_rows_carry_the_counters_but_no_session_baseline() {
        let data = ScheduleItemData::from_rows(vec![live(1)]);
        let none = ScheduleEligibility::default();
        let start = ts("2026-09-06 01:00:00");
        let mut source = online(7, start);
        source.tick(start + 600, &data, &none);
        let saved = source.save_rows();
        assert_eq!(
            saved,
            vec![(
                1,
                AccountScheduleState {
                    gave: 0,
                    cumulated: 600,
                    updated: start + 600,
                },
            )]
        );

        // A restore carries the counters and no clock, no characters and no dirty marks.
        let mut restored = AccountScheduleProgress::from_saved(7, saved.clone());
        assert_eq!(restored.account_id(), 7);
        assert_eq!(restored.save_rows(), saved);
        assert_eq!(restored.account_clock(), 0);
        assert_eq!(restored.online_characters(), 0);
        assert!(restored.dirty_rows().is_empty());

        // Dirty marks are transient and survive only a matching save snapshot.
        restored.join(1, start + 600);
        assert_eq!(restored.tick(start + 660, &data, &none).advanced, vec![1]);
        assert_eq!(restored.state(1).unwrap().cumulated, 660);
        let marks = restored.dirty_rows();
        assert_eq!(marks.len(), 1);
        assert_eq!(marks[0].0, 1);
        restored.mark_saved(&marks);
        assert!(restored.dirty_rows().is_empty());

        // A stamp that changed after the snapshot is not cleared by the older snapshot.
        assert_eq!(restored.tick(start + 720, &data, &none).advanced, vec![1]);
        let older = restored.dirty_rows();
        assert_eq!(restored.tick(start + 780, &data, &none).advanced, vec![1]);
        assert_ne!(restored.dirty_rows(), older);
        restored.mark_saved(&older);
        assert_eq!(restored.dirty_rows().len(), 1);
    }

    #[test]
    fn a_reservation_belongs_to_an_in_world_character_and_a_known_row() {
        let data = ScheduleItemData::from_rows(vec![live(1)]);
        let none = ScheduleEligibility::default();
        let start = ts("2026-09-06 01:00:00");
        let ready = start + 1800;
        let mut progress = online(7, start);
        progress.tick(ready, &data, &none);

        // Character 2 is not the in-world session, and row 999 does not exist.
        assert!(progress
            .plan_claim(&data, 1, 2, ClaimMode::Manual, ready, &none)
            .is_none());
        assert!(progress
            .plan_claim(&data, 999, 1, ClaimMode::Manual, ready, &none)
            .is_none());
        assert!(progress
            .plan_claim(&data, 1, 1, ClaimMode::Manual, ready, &none)
            .is_some());

        // Logout ends the entitlement to a new reservation, while a receipt reserved before it is
        // still recorded (the reward was already delivered).
        assert!(progress.leave(1));
        assert!(progress
            .plan_claim(&data, 1, 1, ClaimMode::Manual, ready, &none)
            .is_none());
        let mut progress = online(7, start);
        progress.tick(ready, &data, &none);
        let plan = progress
            .plan_claim(&data, 1, 1, ClaimMode::Manual, ready, &none)
            .unwrap();
        assert!(progress.leave(1));
        assert!(matches!(
            progress.commit_claim(&data, &plan, ready),
            ClaimCommit::Committed { gave: 1, .. }
        ));
    }

    /// The shipped table has no eligible live row for a modern `now`: every authored window ended
    /// in 2014 except the unbounded premium `VIP 서비스` row (8000001), which the server cannot
    /// prove without premium evidence. Nothing is resurrected by a snapshot or a tick.
    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_compact_db_never_fabricates_a_live_receipt_today() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn = rusqlite::Connection::open(&path).unwrap();
        let files = crate::data::client_files::ClientFileManager::default();
        let ctx = crate::systems::DataLoadCtx {
            conn: &conn,
            app_path: std::path::Path::new("."),
            files: &files,
            language: "en_us",
        };
        let data = ScheduleItemData::load(&ctx).unwrap();
        assert_eq!(data.len(), 32);

        let now = ts("2026-01-01 00:00:00");
        let none = ScheduleEligibility::default();
        let mut progress = online(1, now - 3600);
        assert!(data.active_on_air(now).is_empty());
        assert!(progress.build_update_rows(&data, now, &none).is_empty());

        // With no evidence nothing accrues, nothing is sent and nothing is marked for saving.
        let out = progress.tick(now, &data, &none);
        assert!(out.advanced.is_empty());
        assert!(out.reset.is_empty());
        assert!(progress.dirty_rows().is_empty());
        assert!(progress
            .plan_claim(&data, 8_000_001, 1, ClaimMode::Auto, now, &none)
            .is_none());
        assert_eq!(progress.state(8_000_001), None);

        // Real premium evidence turns the one unbounded silent row on: it is exactly the snapshot
        // row and its `give_term` 0 auto receipt is plannable, never a manual take.
        let premium = ScheduleEligibility {
            paid_patron: true,
            premium_grade_id: 2,
            pc_bang: false,
        };
        let rows = progress.build_update_rows(&data, now, &premium);
        assert_eq!(rows.len(), 1);
        assert_eq!(rows[0].item_template_id, 8_000_001);
        assert_eq!(rows[0].gave, 0);
        assert_eq!(rows[0].acumulated, 0);
        assert_eq!(rows[0].updated, now);
        let plan = progress
            .plan_claim(&data, 8_000_001, 1, ClaimMode::Auto, now, &premium)
            .unwrap();
        assert_eq!((plan.item_id, plan.item_count), (28586, 5));
        assert_eq!(plan.gave_before, 0);
        assert_eq!(plan.gave_after, 1);
        assert!(progress
            .plan_claim(&data, 8_000_001, 1, ClaimMode::Manual, now, &premium)
            .is_none());
    }
}
