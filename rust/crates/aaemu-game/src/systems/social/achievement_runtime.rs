//! Achievement runtime: the per-character record values and achievement progress, and the pure
//! evaluation that turns one into the other.
//!
//! Ported from the later C# history (`7851f67cc`), not from the 1.2 tree — which has the tables
//! and the packets but no achievement logic at all ([`super::achievement`] is the loader only):
//! `Models/Game/Achievement/AchievementRules.cs` (the reading of `complete_num` / `complete_or`
//! and the prerequisite gate), the state half of `Models/Game/Char/CharacterRecords.cs` and
//! `Models/Game/Char/CharacterAchievements.cs`, and the evaluation / cascade core of
//! `Core/Managers/AchievementManager.cs` (`Report`, `Refresh`, `RefreshQueue`, `Complete`).
//!
//! Nothing here knows about packets, MySQL, gameplay hooks or reward delivery: it owns the
//! state, evaluates the loaded content through [`AchievementIndex`], and hands the caller the
//! change / completion events in the order the client has to hear them. The manager that wires it
//! to `Character`, `SCAchievementsPacket` and the `character_achievements` / `character_records`
//! tables lives elsewhere.
//!
//! Deviations from that later C# (the 1.2 shape of the content decides them):
//!
//! * `is_active` exists in 1.2 and was dropped in 10.x. It is a gate here: a switched-off row is
//!   not evaluated, cannot complete, and is left out of the list builder. The shipped 1.2
//!   `compact.sqlite3` has it `'t'` on every row.
//! * 1.2 has no `season_off` column and no `CompleteAchievementSubCategory` record kind
//!   (`char_record_kind` stops at `BATTLE_FIELD_ASSIST`), so the later season-off objective
//!   filter and the "whole sub-category done" record have no counterpart.
//! * `Achievements::complete_num` is `u32` in 1.2; every quantity that can reach a packet is
//!   saturated and clamped to the packet's `i32` range instead of wrapping.
//! * The completion instant is passed in, not read from the clock, so a caller that stamps the
//!   completion packet uses the same time and tests stay deterministic.
//! * [`AchievementState::refresh`] stores the evaluation with the C#'s monotonic `SetAmount`, but
//!   reports a change only when the *stored* amount moved. The C# compares the evaluation with
//!   the stored amount and sends a change packet even where `SetAmount` refused to lower it
//!   (reachable through the GM `SetRecord` path or a stored value ahead of the records); a packet
//!   that contradicts the stored progress is a bug, not a semantic.

use std::collections::{BTreeMap, HashMap, HashSet, VecDeque};

use chrono::{DateTime, Utc};

use super::achievement::{
    AchievementDerived, AchievementGameData, AchievementInfo, AchievementObjectives, Achievements,
    CharRecords,
};

// ---- pure rules (`AchievementRules`) ----

/// What one achievement's objectives are worth against the character's record values
/// (`AchievementEvaluation` in the C# rules).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct AchievementEvaluation {
    /// What to report as the character's amount: the total clamped to [`Self::required`].
    pub progress: i32,
    /// What the achievement asks for: `complete_num`, or its objective count when that is 0.
    pub required: i32,
    /// How many of its objectives have been touched at all.
    pub satisfied: u32,
    /// Whether the amount reached the target. The prerequisite gate is applied by the caller.
    pub complete: bool,
}

/// `AchievementRules.Evaluate`: reads an achievement's objectives against the record values.
///
/// `complete_or` picks one of two shapes. True counts objectives: each objective whose record has
/// been touched is worth one, and the content's "complete one of these houses" rows never ask for
/// more objectives than they have. False sums the records up: the content's "complete 1,200
/// quests" rows ask for more than they have objectives. `complete_num` 0 means all of them, one
/// each, under either flag. An achievement with no objectives has nothing counting towards it and
/// therefore never completes.
///
/// Duplicate objectives are counted separately (the content lists the same record twice), a record
/// at or below zero counts for nothing (the table stores `-1` as its "no target" sentinel), and
/// the whole total saturates.
pub fn evaluate_achievement(
    complete_num: u32,
    complete_or: bool,
    objectives: &[AchievementObjectives],
    record_value: impl Fn(u32) -> i64,
) -> AchievementEvaluation {
    let all_of_them = complete_num == 0;
    let required = if all_of_them {
        objectives.len() as i64
    } else {
        i64::from(complete_num)
    };

    // Nothing counts towards an achievement without objectives, so nothing can complete it either
    // (78 of the content's rows are the parents whose children carry the objectives).
    if objectives.is_empty() {
        return AchievementEvaluation {
            progress: 0,
            required: clamp_to_i32(required),
            satisfied: 0,
            complete: false,
        };
    }

    let mut total = 0i64;
    let mut satisfied = 0u32;
    for objective in objectives {
        let value = record_value(objective.record_id);
        if value <= 0 {
            continue;
        }

        satisfied += 1;
        // Counting objectives: each one is worth one however far past it the record is. Summing
        // them: the records' own values are what the total is made of. "All of them" counts each
        // once either way.
        total = total.saturating_add(if complete_or || all_of_them { 1 } else { value });
    }

    AchievementEvaluation {
        progress: clamp_to_i32(total.min(required)),
        required: clamp_to_i32(required),
        satisfied,
        complete: total >= required,
    }
}

/// `AchievementRules.PrerequisitesMet`: whether every prerequisite achievement is already
/// complete. An empty list is no gate.
pub fn prerequisites_met(prerequisites: &[u32], is_complete: impl Fn(u32) -> bool) -> bool {
    prerequisites
        .iter()
        .all(|achievement_id| is_complete(*achievement_id))
}

/// Clamps a quantity to what a packet can carry, and to the non-negative range the client draws.
fn clamp_to_i32(value: i64) -> i32 {
    value.clamp(0, i64::from(i32::MAX)) as i32
}

// ---- content index (`AchievementGameData.PostLoad`) ----

/// What the content says about the achievements, pre-indexed for evaluation: every achievement's
/// objectives, who watches each record, the kind-9 completion records and the prerequisite graph.
///
/// The index borrows the loaded [`AchievementGameData`] together with the derived lookups cached
/// on it ([`AchievementGameData::derived`]), so building an index per event is two pointer copies,
/// not a rebuild of every reverse map: the derived maps are built once per loaded value and shared
/// by every index over it. The per-character [`AchievementState`] never holds content.
///
/// The index borrows immutably, and the cache assumes the loaded tables are not mutated after the
/// first index is built — the servers load `AchievementGameData` once at startup and never change
/// it, so no invalidation surface exists.
#[derive(Debug)]
pub struct AchievementIndex<'a> {
    data: &'a AchievementGameData,
    derived: &'a AchievementDerived,
}

impl<'a> AchievementIndex<'a> {
    /// Borrows the data and its cached lookups (`AchievementGameData.PostLoad`).
    pub fn new(data: &'a AchievementGameData) -> Self {
        Self {
            data,
            derived: data.derived(),
        }
    }

    /// The active achievements, ascending.
    pub fn active_ids(&self) -> &[u32] {
        self.derived.active_ids()
    }

    /// Whether the id names an achievement that is switched on. An unknown id is not active.
    pub fn is_active(&self, achievement_id: u32) -> bool {
        self.data
            .achievements
            .get(&achievement_id)
            .is_some_and(|achievement| achievement.is_active)
    }

    /// The achievement row, when the content has it (active or not).
    pub fn achievement(&self, achievement_id: u32) -> Option<&'a Achievements> {
        self.data.achievements.get(&achievement_id)
    }

    /// The record row, when the content has it.
    pub fn record(&self, record_id: u32) -> Option<&'a CharRecords> {
        self.data.char_records.get(&record_id)
    }

    /// An achievement's objectives, ascending by objective id (`GetObjectives`).
    pub fn objectives(&self, achievement_id: u32) -> &'a [AchievementObjectives] {
        self.data.objectives_for(achievement_id)
    }

    /// The achievements whose objectives watch a record (`GetAchievementsWatchingRecord`).
    pub fn watching_record(&self, record_id: u32) -> &[u32] {
        self.derived.watching_record(record_id)
    }

    /// The achievements that must already be complete first (`GetPrerequisites`).
    pub fn prerequisites(&self, achievement_id: u32) -> &[u32] {
        self.derived.prerequisites(achievement_id)
    }

    /// The achievements this one gates (`GetGatedByPrerequisite`). None of the content's gated
    /// rows watch the prerequisite's completion record, so completing it has to re-evaluate them
    /// by name.
    pub fn gated_by_prerequisite(&self, prerequisite_id: u32) -> &[u32] {
        self.derived.gated_by_prerequisite(prerequisite_id)
    }

    /// The record counting an achievement's completion, when the content has one
    /// (`GetCompletionRecord`).
    pub fn completion_record(&self, achievement_id: u32) -> Option<u32> {
        self.derived.completion_record(achievement_id)
    }

    /// The achievement a kind-9 record counts (`GetAchievementForCompletionRecord`).
    pub fn achievement_for_completion_record(&self, record_id: u32) -> Option<u32> {
        self.derived.achievement_for_completion_record(record_id)
    }

    /// Evaluates one achievement against `record_value`; `None` when the id is not in the content
    /// or the row is switched off. Pure: nothing is stored.
    pub fn evaluate(
        &self,
        achievement_id: u32,
        record_value: impl Fn(u32) -> i64,
    ) -> Option<AchievementEvaluation> {
        let achievement = self.data.achievements.get(&achievement_id)?;
        if !achievement.is_active {
            return None;
        }

        Some(evaluate_achievement(
            achievement.complete_num,
            achievement.complete_or,
            self.objectives(achievement_id),
            record_value,
        ))
    }
}

// ---- per-character state (`CharacterRecords` + `CharacterAchievements`) ----

/// One character's progress with one achievement (`AchievementProgress`). Completion is a
/// timestamp rather than a flag because the client's packets carry the completion time.
///
/// `reward_delivered` is the reward receipt: whether the completion's reward has been handed over
/// (or deliberately settled). It is one-way while the row lives — a replay of the same completion
/// must not pay twice — and it goes with the row when the achievement is forgotten, so a reset
/// achievement is payable again when it is re-earned.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct AchievementProgress {
    achievement_id: u32,
    amount: i32,
    completed_at: Option<DateTime<Utc>>,
    reward_delivered: bool,
}

impl AchievementProgress {
    /// The `character_achievements.achievement_id` this row is for.
    pub fn achievement_id(&self) -> u32 {
        self.achievement_id
    }

    /// How far the character has got: a record total, or how many objectives are done.
    pub fn amount(&self) -> i32 {
        self.amount
    }

    /// When the achievement was earned, or `None` while it is still in progress.
    pub fn completed_at(&self) -> Option<DateTime<Utc>> {
        self.completed_at
    }

    /// Whether it has been earned (its completion timestamp is set).
    pub fn is_complete(&self) -> bool {
        self.completed_at.is_some()
    }

    /// Whether the completion's reward has been delivered (or deliberately settled) — the
    /// `character_achievements.reward_delivered` receipt.
    pub fn reward_delivered(&self) -> bool {
        self.reward_delivered
    }

    /// Whether this row is a completion whose reward is still waiting to be handed over.
    pub fn has_pending_reward(&self) -> bool {
        self.is_complete() && !self.reward_delivered
    }
}

/// What the client has to be told about, in the order it happened.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AchievementEvent {
    /// `SCAchievementChangedPacket(id, amount)`.
    AmountChanged { achievement_id: u32, amount: i32 },
    /// `SCAchievementCompletedPacket(id)`; `completed_at` is when it was earned.
    Completed {
        achievement_id: u32,
        completed_at: DateTime<Utc>,
    },
}

impl AchievementEvent {
    /// The achievement this event is about.
    pub fn achievement_id(&self) -> u32 {
        match self {
            Self::AmountChanged { achievement_id, .. } | Self::Completed { achievement_id, .. } => {
                *achievement_id
            }
        }
    }
}

/// What one re-evaluation of one achievement did (`AchievementManager.RefreshResult` plus the
/// events it produced).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct AchievementRefresh {
    /// The stored amount moved.
    pub amount_changed: bool,
    /// This call was the one that completed it.
    pub newly_completed: bool,
    /// The change / completion events, in send order.
    pub events: Vec<AchievementEvent>,
}

/// What a re-evaluation pass did: `AchievementManager.RefreshQueue`'s `(Moved, Completed)` plus
/// the events, in send order.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct RefreshOutcome {
    /// Achievements whose amount moved or that completed.
    pub moved: usize,
    /// Achievements this pass was the one to complete.
    pub completed: usize,
    /// The change / completion events, in send order.
    pub events: Vec<AchievementEvent>,
}

impl RefreshOutcome {
    /// Whether the pass completed anything.
    pub fn newly_completed(&self) -> bool {
        self.completed > 0
    }

    /// The ids whose amount changed, in first-seen order (each id once).
    pub fn changed_ids(&self) -> Vec<u32> {
        let mut seen = HashSet::new();
        self.events
            .iter()
            .filter_map(|event| match event {
                AchievementEvent::AmountChanged { achievement_id, .. } => Some(*achievement_id),
                AchievementEvent::Completed { .. } => None,
            })
            .filter(|achievement_id| seen.insert(*achievement_id))
            .collect()
    }

    /// The ids this pass completed, in first-seen order (each id once).
    pub fn completed_ids(&self) -> Vec<u32> {
        let mut seen = HashSet::new();
        self.events
            .iter()
            .filter_map(|event| match event {
                AchievementEvent::Completed { achievement_id, .. } => Some(*achievement_id),
                AchievementEvent::AmountChanged { .. } => None,
            })
            .filter(|achievement_id| seen.insert(*achievement_id))
            .collect()
    }
}

/// The per-character achievement state: the record values the engine has reported, and how far
/// the character has got with each achievement.
///
/// The C# keeps the two in `CharacterRecords` (one value per record, the highest ever reported)
/// and `CharacterAchievements` (one row per achievement, dropped on reset); this type is the two
/// halves, because an evaluation needs both. Records describe things that can go down as well as
/// up — ability levels fall on a respec — and an achievement that has been earned is not taken
/// back, so the store keeps the high-water mark; an explicit reset is [`Self::clear_record`] plus
/// [`Self::forget_achievement`].
#[derive(Debug, Clone, Default)]
pub struct AchievementState {
    /// Record id -> the highest value reported (`CharacterRecords._values`).
    records: HashMap<u32, i64>,
    /// Achievement id -> progress (`CharacterAchievements._progress`).
    progress: HashMap<u32, AchievementProgress>,
}

impl AchievementState {
    // ---- reads ----

    /// The value this character has reached for a record, or zero when it never reported one.
    pub fn record_value(&self, record_id: u32) -> i64 {
        self.records.get(&record_id).copied().unwrap_or(0)
    }

    /// How far the character has got with an achievement.
    pub fn amount(&self, achievement_id: u32) -> i32 {
        self.progress
            .get(&achievement_id)
            .map_or(0, AchievementProgress::amount)
    }

    /// When the achievement was earned, or `None` while it is still in progress.
    pub fn completed_at(&self, achievement_id: u32) -> Option<DateTime<Utc>> {
        self.progress
            .get(&achievement_id)
            .and_then(AchievementProgress::completed_at)
    }

    /// Whether the achievement has been earned.
    pub fn is_complete(&self, achievement_id: u32) -> bool {
        self.completed_at(achievement_id).is_some()
    }

    /// A copy of one achievement's progress row, when the character has one.
    pub fn progress(&self, achievement_id: u32) -> Option<AchievementProgress> {
        self.progress.get(&achievement_id).copied()
    }

    /// The pure evaluation of one achievement against this character's records, with the
    /// prerequisites **not** applied (they gate completion only, see [`Self::refresh`]).
    pub fn evaluate(
        &self,
        index: &AchievementIndex<'_>,
        achievement_id: u32,
    ) -> Option<AchievementEvaluation> {
        index.evaluate(achievement_id, |record_id| self.record_value(record_id))
    }

    // ---- record store (`CharacterRecords`) ----

    /// `CharacterRecords.Report`: reports a record's current value, keeping whichever of the two
    /// is higher. Returns whether the record moved.
    pub fn report_record_value(&mut self, record_id: u32, value: i64) -> bool {
        let kept = self.record_value(record_id);
        if value <= kept {
            return false;
        }

        self.records.insert(record_id, value);
        true
    }

    /// `CharacterRecords.Set`: sets a record outright, lower values included.
    pub fn restore_record(&mut self, record_id: u32, value: i64) {
        self.records.insert(record_id, value);
    }

    /// `CharacterRecords.Clear`: forgets a record entirely, so the next report starts from zero.
    pub fn clear_record(&mut self, record_id: u32) -> Option<i64> {
        self.records.remove(&record_id)
    }

    // ---- progress store (`CharacterAchievements`) ----

    /// `CharacterAchievements.Load`: puts a `character_achievements` row back. The amount is
    /// clamped to the packet's range because it is what the client is sent. The reward receipt is
    /// kept as it is — a paid completion must not be re-opened by restoring its row again — and a
    /// row the character does not have yet starts not delivered. The DB load path restores the
    /// receipt a row was saved with through [`Self::restore_progress_with_receipt`].
    pub fn restore_progress(
        &mut self,
        achievement_id: u32,
        amount: i64,
        completed_at: Option<DateTime<Utc>>,
    ) {
        let delivered = self
            .progress
            .get(&achievement_id)
            .is_some_and(|row| row.reward_delivered);
        self.restore_progress_with_receipt(achievement_id, amount, completed_at, delivered);
    }

    /// As [`Self::restore_progress`], with the `character_achievements.reward_delivered` receipt
    /// the row was saved with.
    pub fn restore_progress_with_receipt(
        &mut self,
        achievement_id: u32,
        amount: i64,
        completed_at: Option<DateTime<Utc>>,
        reward_delivered: bool,
    ) {
        let progress = self.progress.entry(achievement_id).or_default();
        progress.achievement_id = achievement_id;
        progress.amount = clamp_to_i32(amount);
        progress.completed_at = completed_at;
        progress.reward_delivered = reward_delivered;
    }

    /// `CharacterAchievements.Reset`: drops an achievement's progress and returns it, reward
    /// receipt included. The caller clears the records it is judged by with
    /// [`Self::clear_record`], or the next evaluation puts it straight back.
    pub fn forget_achievement(&mut self, achievement_id: u32) -> Option<AchievementProgress> {
        self.progress.remove(&achievement_id)
    }

    // ---- reward receipts ----

    /// Whether a completed achievement's reward is still waiting to be handed over. An incomplete
    /// row — or none — is not a payable completion.
    pub fn has_pending_reward(&self, achievement_id: u32) -> bool {
        self.progress
            .get(&achievement_id)
            .is_some_and(AchievementProgress::has_pending_reward)
    }

    /// The completions whose reward has not been marked delivered, ascending achievement id.
    ///
    /// This reads the progress rows, not the event queue: a completion reached by a quiet pass
    /// (the entry-path back-credit, [`Self::refresh_all`]) is pending too, and a change that did
    /// not complete anything is not.
    pub fn pending_reward_ids(&self) -> Vec<u32> {
        let mut ids: Vec<u32> = self
            .progress
            .values()
            .filter(|row| row.has_pending_reward())
            .map(|row| row.achievement_id)
            .collect();
        ids.sort_unstable();
        ids
    }

    /// Marks a completed achievement's reward as delivered — the receipt a reward path writes
    /// once it has handed the reward over (or decided that there is nothing to hand over).
    ///
    /// Restricted to completions: an achievement that is not complete, or that the character does
    /// not have a row for, is not payable and returns `false`. The receipt is one-way while the
    /// row lives, so a repeated call returns `false` without re-opening anything; only
    /// [`Self::forget_achievement`] clears it.
    pub fn mark_reward_delivered(&mut self, achievement_id: u32) -> bool {
        let Some(progress) = self.progress.get_mut(&achievement_id) else {
            return false;
        };
        if !progress.is_complete() || progress.reward_delivered {
            return false;
        }

        progress.reward_delivered = true;
        true
    }

    /// `CharacterRecords.Snapshot`: every record the character has a value for, in ascending
    /// record id. The C# returns a dictionary; the ordered map keeps save and GM iteration
    /// deterministic.
    pub fn record_snapshot(&self) -> BTreeMap<u32, i64> {
        self.records
            .iter()
            .map(|(record_id, value)| (*record_id, *value))
            .collect()
    }

    /// `CharacterAchievements.Snapshot`: a copy of the progress — reward receipts included — in
    /// ascending achievement id.
    pub fn progress_snapshot(&self) -> Vec<AchievementProgress> {
        let mut progress: Vec<AchievementProgress> = self.progress.values().copied().collect();
        progress.sort_unstable_by_key(|row| row.achievement_id);
        progress
    }

    /// `AchievementManager.BuildList`: the rows of `SCAchievementsPacket`, one per active
    /// achievement the character has touched, in ascending id — or every active achievement when
    /// `include_untouched` (the client starts every achievement at nothing, so the entry path only
    /// sends what it has).
    pub fn achievement_list(
        &self,
        index: &AchievementIndex<'_>,
        include_untouched: bool,
    ) -> Vec<AchievementInfo> {
        let mut list = Vec::new();
        for achievement_id in index.active_ids() {
            let amount = self.amount(*achievement_id);
            let completed_at = self.completed_at(*achievement_id);
            if !include_untouched && amount == 0 && completed_at.is_none() {
                continue;
            }

            list.push(AchievementInfo {
                id: *achievement_id,
                amount: amount.max(0) as u32,
                complete: completed_at.unwrap_or_default(),
            });
        }
        list
    }

    // ---- evaluation (`AchievementManager`) ----

    /// `AchievementManager.Refresh`: re-evaluates one achievement, stores what it found and
    /// returns the events. Completion is one-way — a later call leaves the first timestamp alone,
    /// so the time the client shows is when it was actually earned — and objectives can be full
    /// while a prerequisite is still missing: the progress is stored, the completion waits.
    ///
    /// This is one achievement only. The kind-9 record a completion counts into, and the rows it
    /// gates, are driven by the queued paths ([`Self::report_record`], [`Self::refresh_all`],
    /// [`Self::force_complete`]).
    pub fn refresh(
        &mut self,
        index: &AchievementIndex<'_>,
        achievement_id: u32,
        now: DateTime<Utc>,
    ) -> AchievementRefresh {
        let Some(evaluation) = self.evaluate(index, achievement_id) else {
            return AchievementRefresh::default();
        };

        let mut events = Vec::new();
        let stored = self.amount(achievement_id);
        let mut amount_changed = false;
        if evaluation.progress != stored {
            let amount = self.store_amount(achievement_id, evaluation.progress);
            if amount != stored {
                amount_changed = true;
                events.push(AchievementEvent::AmountChanged {
                    achievement_id,
                    amount,
                });
            }
        }

        let complete = evaluation.complete
            && prerequisites_met(index.prerequisites(achievement_id), |required| {
                self.is_complete(required)
            });
        if !complete || !self.complete_once(achievement_id, now) {
            return AchievementRefresh {
                amount_changed,
                newly_completed: false,
                events,
            };
        }

        events.push(AchievementEvent::Completed {
            achievement_id,
            completed_at: now,
        });
        AchievementRefresh {
            amount_changed,
            newly_completed: true,
            events,
        }
    }

    /// `AchievementManager.Report`: reports a record's new value and re-evaluates the achievements
    /// whose objectives watch it. A report that does not move the record does nothing.
    pub fn report_record(
        &mut self,
        index: &AchievementIndex<'_>,
        record_id: u32,
        value: i64,
        now: DateTime<Utc>,
    ) -> RefreshOutcome {
        if record_id == 0 || !self.report_record_value(record_id, value) {
            return RefreshOutcome::default();
        }

        let watchers = index.watching_record(record_id);
        self.refresh_queue(index, watchers.iter().copied(), now)
    }

    /// `AchievementManager.SetRecord` (the GM surface, and the reset path's counterpart): sets a
    /// record outright and lets the achievements that watch it follow. A record that is not in the
    /// content is ignored, like the C#.
    pub fn set_record(
        &mut self,
        index: &AchievementIndex<'_>,
        record_id: u32,
        value: i64,
        now: DateTime<Utc>,
    ) -> RefreshOutcome {
        if index.record(record_id).is_none() {
            return RefreshOutcome::default();
        }

        self.records.insert(record_id, value);
        let watchers = index.watching_record(record_id);
        self.refresh_queue(index, watchers.iter().copied(), now)
    }

    /// `AchievementManager.RefreshAll`: re-evaluates every active achievement, in id order. This
    /// is what the entry path does before it pushes the list, without sending anything — the
    /// caller drops the returned events.
    pub fn refresh_all(
        &mut self,
        index: &AchievementIndex<'_>,
        now: DateTime<Utc>,
    ) -> RefreshOutcome {
        let active = index.active_ids();
        self.refresh_queue(index, active.iter().copied(), now)
    }

    /// `AchievementManager.RefreshQueue`: re-evaluates a set of achievements and everything their
    /// completions lead to.
    ///
    /// A completion is itself a kind-9 record that other achievements watch, and it is what the
    /// rows naming it as a prerequisite were waiting for, so this is a work queue rather than one
    /// pass. A watcher that was already passed over is looked at again when a completion it
    /// watches lands: the content is not ordered by id, and most of its completion links run from
    /// a lower parent id to a higher child. It still terminates — an achievement completes once
    /// however many ways it is reached (completion is idempotent), so a cycle re-queues work that
    /// then finds nothing to do.
    pub fn refresh_queue(
        &mut self,
        index: &AchievementIndex<'_>,
        achievement_ids: impl IntoIterator<Item = u32>,
        now: DateTime<Utc>,
    ) -> RefreshOutcome {
        let mut pending: VecDeque<u32> = achievement_ids.into_iter().collect();
        let mut visited: HashSet<u32> = HashSet::new();
        let mut outcome = RefreshOutcome::default();

        while let Some(achievement_id) = pending.pop_front() {
            if !visited.insert(achievement_id) {
                continue;
            }

            let refresh = self.refresh(index, achievement_id, now);
            if refresh.amount_changed || refresh.newly_completed {
                outcome.moved += 1;
            }
            outcome.events.extend(refresh.events);
            if !refresh.newly_completed {
                continue;
            }
            outcome.completed += 1;

            // A completion is a record of its own: report it and look at what watches it.
            if let Some(record_id) = index.completion_record(achievement_id) {
                self.report_record_value(record_id, 1);
                for watcher in index.watching_record(record_id) {
                    visited.remove(watcher);
                    pending.push_back(*watcher);
                }
            }

            // And completing it is what the rows that require it were waiting for.
            for gated in index.gated_by_prerequisite(achievement_id) {
                visited.remove(gated);
                pending.push_back(*gated);
            }
        }

        outcome
    }

    /// `AchievementManager.Complete`: completes an achievement outright — the GM path, and what
    /// completing by hand has to do to keep the dependent achievements honest.
    ///
    /// Prerequisites are a gate on [`Self::refresh`], not a credit from this force: they are not
    /// checked and not paid. Completion is idempotent, so completing an achievement the character
    /// already holds does nothing. The amount is left alone, like the C#; the records a completion
    /// counts into and the rows it gates are driven through the queue.
    pub fn force_complete(
        &mut self,
        index: &AchievementIndex<'_>,
        achievement_id: u32,
        now: DateTime<Utc>,
    ) -> RefreshOutcome {
        let mut outcome = RefreshOutcome::default();
        if !index.is_active(achievement_id) || !self.complete_once(achievement_id, now) {
            return outcome;
        }
        outcome.moved += 1;
        outcome.completed += 1;
        outcome.events.push(AchievementEvent::Completed {
            achievement_id,
            completed_at: now,
        });

        // The records it counts into. A kind-9 record that did not move refreshes nothing, which
        // is the C# `Report` guard.
        if let Some(record_id) = index.completion_record(achievement_id) {
            if self.report_record_value(record_id, 1) {
                let watchers = index.watching_record(record_id);
                let watched = self.refresh_queue(index, watchers.iter().copied(), now);
                merge_outcome(&mut outcome, watched);
            }
        }

        // Gated rows do not watch the completion record, so they are queued by name.
        let gated = index.gated_by_prerequisite(achievement_id);
        let gated = self.refresh_queue(index, gated.iter().copied(), now);
        merge_outcome(&mut outcome, gated);
        outcome
    }

    /// `CharacterAchievements.SetAmount`: sets an achievement's counter, never lowering it.
    /// Returns the stored amount.
    fn store_amount(&mut self, achievement_id: u32, amount: i32) -> i32 {
        let progress = self.progress.entry(achievement_id).or_default();
        progress.achievement_id = achievement_id;
        if amount > progress.amount {
            progress.amount = amount;
        }
        progress.amount
    }

    /// `CharacterAchievements.Complete`: marks an achievement complete if it is not already.
    /// Completion is one-way, so the first timestamp is the one kept, and an already-complete row
    /// is left exactly as it was — replaying it must not re-open a reward that was paid. A fresh
    /// completion starts with its receipt unset, so it is owed.
    fn complete_once(&mut self, achievement_id: u32, completed_at: DateTime<Utc>) -> bool {
        let progress = self.progress.entry(achievement_id).or_default();
        progress.achievement_id = achievement_id;
        if progress.completed_at.is_some() {
            return false;
        }

        progress.completed_at = Some(completed_at);
        progress.reward_delivered = false;
        true
    }
}

/// Folds a queued pass into the pass that started it.
fn merge_outcome(outcome: &mut RefreshOutcome, other: RefreshOutcome) {
    outcome.moved += other.moved;
    outcome.completed += other.completed;
    outcome.events.extend(other.events);
}

#[cfg(test)]
mod tests {
    use chrono::{DateTime, Utc};

    use super::super::achievement::{
        char_record_kind, AchievementGameData, AchievementObjectives, Achievements, CharRecords,
        PreCompletedAchievements,
    };
    use super::*;

    /// A fixed instant, so the completion timestamps assertions read are deterministic.
    fn at(seconds: i64) -> DateTime<Utc> {
        DateTime::from_timestamp(seconds, 0).expect("valid timestamp")
    }

    /// Content fixture: what `AchievementGameData.Load` would have produced for a small table.
    #[derive(Default)]
    struct Content {
        data: AchievementGameData,
        next_objective: u32,
        next_prerequisite: u32,
    }

    impl Content {
        fn achievement(
            mut self,
            id: u32,
            complete_num: u32,
            complete_or: bool,
            records: &[u32],
        ) -> Self {
            self.add_achievement(id, complete_num, complete_or, records, true);
            self
        }

        fn inactive_achievement(
            mut self,
            id: u32,
            complete_num: u32,
            complete_or: bool,
            records: &[u32],
        ) -> Self {
            self.add_achievement(id, complete_num, complete_or, records, false);
            self
        }

        fn add_achievement(
            &mut self,
            id: u32,
            complete_num: u32,
            complete_or: bool,
            records: &[u32],
            is_active: bool,
        ) {
            let mut objectives = Vec::new();
            for record_id in records {
                self.next_objective += 1;
                objectives.push(AchievementObjectives {
                    id: self.next_objective,
                    achievement_id: id,
                    record_id: *record_id,
                    ..Default::default()
                });
            }

            // Like the loader: the list is grouped by achievement id and linked onto the row.
            self.data.achievements.insert(
                id,
                Achievements {
                    id,
                    complete_num,
                    complete_or,
                    is_active,
                    achievement_objectives: objectives.clone(),
                    ..Default::default()
                },
            );
            if !objectives.is_empty() {
                self.data.achievement_objectives.insert(id, objectives);
            }
        }

        /// A record definition the engine can report (`char_records`).
        fn record(mut self, record_id: u32, kind_id: u32) -> Self {
            self.data.char_records.insert(
                record_id,
                CharRecords {
                    id: record_id,
                    kind_id,
                    ..Default::default()
                },
            );
            self
        }

        /// The kind-9 record that counts an achievement's completion.
        fn completion(mut self, record_id: u32, achievement_id: u32) -> Self {
            self.data.char_records.insert(
                record_id,
                CharRecords {
                    id: record_id,
                    kind_id: char_record_kind::COMPLETE_ACHIEVEMENT,
                    value1: achievement_id,
                    ..Default::default()
                },
            );
            self
        }

        /// `pre_completed_achievements`: `achievement_id` requires `required_id` first.
        fn prerequisite(mut self, achievement_id: u32, required_id: u32) -> Self {
            self.next_prerequisite += 1;
            self.data
                .pre_completed_achievements
                .entry(required_id)
                .or_default()
                .push(PreCompletedAchievements {
                    id: self.next_prerequisite,
                    completed_achievement_id: required_id,
                    my_achievement_id: achievement_id,
                });
            self
        }

        fn build(self) -> AchievementGameData {
            self.data
        }
    }

    fn objectives(records: &[u32]) -> Vec<AchievementObjectives> {
        records
            .iter()
            .enumerate()
            .map(|(index, record_id)| AchievementObjectives {
                id: index as u32 + 1,
                record_id: *record_id,
                ..Default::default()
            })
            .collect()
    }

    fn records(pairs: &[(u32, i64)]) -> impl Fn(u32) -> i64 + '_ {
        move |record_id| {
            pairs
                .iter()
                .find(|(id, _)| *id == record_id)
                .map_or(0, |(_, value)| *value)
        }
    }

    // ---- the pure rules ----

    #[test]
    fn summing_shape_adds_the_records_up_to_complete_num() {
        // "Complete 1,200 quests" over eleven records: the number is a total, not a count.
        let rules = objectives(&(1..=11).collect::<Vec<u32>>());

        let partial = evaluate_achievement(1200, false, &rules, records(&[(1, 700), (2, 499)]));
        assert_eq!(partial.progress, 1199);
        assert_eq!(partial.required, 1200);
        assert!(!partial.complete);

        let done = evaluate_achievement(1200, false, &rules, records(&[(1, 700), (2, 500)]));
        assert_eq!(done.progress, 1200);
        assert!(done.complete);
    }

    #[test]
    fn counting_shape_needs_that_many_objectives() {
        // The novel-crafting ladder: 45 titles, asking for 5, then 10, then all 45 kinds.
        let rules = objectives(&(1..=45).collect::<Vec<u32>>());
        let five_records: Vec<(u32, i64)> = (1..=5).map(|id| (id, 1)).collect();
        let five = records(&five_records);

        assert!(evaluate_achievement(5, true, &rules, &five).complete);
        assert_eq!(evaluate_achievement(10, true, &rules, &five).progress, 5);
        assert!(!evaluate_achievement(10, true, &rules, &five).complete);
        assert!(!evaluate_achievement(45, true, &rules, &five).complete);

        // A record far past its own target is still one kind.
        let heavy_records: Vec<(u32, i64)> = (1..=5).map(|id| (id, 999_999)).collect();
        let heavy = records(&heavy_records);
        assert!(evaluate_achievement(5, true, &rules, &heavy).complete);
        assert_eq!(evaluate_achievement(10, true, &rules, &heavy).progress, 5);
    }

    #[test]
    fn complete_num_zero_means_all_of_them_one_each() {
        // "Train the abilities of fourteen heroes ... for all fourteen".
        let rules = objectives(&(1..=14).collect::<Vec<u32>>());

        let thirteen_records: Vec<(u32, i64)> = (1..=13).map(|id| (id, 50)).collect();
        let thirteen = records(&thirteen_records);
        let partial = evaluate_achievement(0, true, &rules, &thirteen);
        assert_eq!(partial.required, 14);
        assert_eq!(partial.satisfied, 13);
        assert!(!partial.complete);

        let fourteen_records: Vec<(u32, i64)> = (1..=14).map(|id| (id, 1)).collect();
        let fourteen = records(&fourteen_records);
        let done = evaluate_achievement(0, true, &rules, &fourteen);
        assert_eq!(done.progress, 14);
        assert!(done.complete);

        // The same under the summing flag, where a record's size would otherwise carry the total.
        assert!(!evaluate_achievement(0, false, &rules, &thirteen).complete);
        assert!(evaluate_achievement(0, false, &rules, &fourteen).complete);
    }

    #[test]
    fn records_at_or_below_zero_do_not_count() {
        // The table stores -1 as its "no target" sentinel, and untouched counters read 0.
        let evaluation = evaluate_achievement(
            0,
            true,
            &objectives(&[1, 2, 3]),
            records(&[(1, 0), (2, -1), (3, 1)]),
        );

        assert_eq!(evaluation.satisfied, 1);
        assert_eq!(evaluation.progress, 1);
        assert!(!evaluation.complete);
    }

    #[test]
    fn duplicate_objectives_each_count() {
        // The content lists the same record more than once, and each row is its own objective.
        let evaluation = evaluate_achievement(0, true, &objectives(&[7, 7]), records(&[(7, 3)]));

        assert_eq!(evaluation.required, 2);
        assert_eq!(evaluation.progress, 2);
        assert!(evaluation.complete);
    }

    #[test]
    fn an_achievement_without_objectives_is_never_completed() {
        let evaluation = evaluate_achievement(10, true, &[], records(&[]));

        assert_eq!((evaluation.progress, evaluation.required), (0, 10));
        assert!(!evaluation.complete);

        let zero = evaluate_achievement(0, true, &[], records(&[]));
        assert_eq!((zero.progress, zero.required), (0, 0));
        assert!(!zero.complete);
    }

    #[test]
    fn quantities_saturate_and_stay_inside_the_packet_range() {
        // `complete_num` is a u32 in 1.2 and the amount is sent as an i32.
        let rules = objectives(&[1, 2]);
        let huge = evaluate_achievement(u32::MAX, false, &rules, |_| i64::MAX);

        assert_eq!(huge.required, i32::MAX);
        assert_eq!(huge.progress, i32::MAX);
        assert!(huge.complete);

        // The records' sum saturates instead of wrapping.
        let small = evaluate_achievement(u32::MAX, false, &objectives(&[1]), |_| i64::MAX);
        assert_eq!(small.progress, i32::MAX);
    }

    #[test]
    fn prerequisites_met_needs_every_listed_achievement() {
        let held = [2052u32];
        assert!(prerequisites_met(&[], |_| false));
        assert!(prerequisites_met(&[2052], |id| held.contains(&id)));
        assert!(!prerequisites_met(&[2052, 1], |id| held.contains(&id)));
        assert!(!prerequisites_met(&[2074], |id| held.contains(&id)));
    }

    // ---- record reports and refresh ----

    #[test]
    fn report_record_moves_the_achievement_and_emits_its_events() {
        let data = Content::default().achievement(1, 50, false, &[10]).build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();

        let partial = state.report_record(&index, 10, 49, at(1_000));
        assert_eq!(partial.moved, 1);
        assert_eq!(partial.completed, 0);
        assert_eq!(state.amount(1), 49);
        assert!(!state.is_complete(1));
        assert_eq!(
            partial.events,
            [AchievementEvent::AmountChanged {
                achievement_id: 1,
                amount: 49
            }]
        );

        let done = state.report_record(&index, 10, 50, at(1_001));
        assert_eq!(done.moved, 1);
        assert_eq!(done.completed, 1);
        assert_eq!(done.completed_ids(), [1]);
        assert_eq!(state.completed_at(1), Some(at(1_001)));
        assert_eq!(
            done.events,
            [
                AchievementEvent::AmountChanged {
                    achievement_id: 1,
                    amount: 50
                },
                AchievementEvent::Completed {
                    achievement_id: 1,
                    completed_at: at(1_001)
                },
            ]
        );
    }

    #[test]
    fn report_record_keeps_the_high_water_mark_and_is_quiet_when_nothing_moved() {
        let data = Content::default().achievement(1, 50, false, &[10]).build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();

        state.report_record(&index, 10, 50, at(1_000));

        // A lower report is not a record: the store keeps the highest value ever reported.
        let lower = state.report_record(&index, 10, 10, at(1_001));
        assert_eq!(lower, RefreshOutcome::default());
        assert_eq!(state.record_value(10), 50);
        assert_eq!(state.amount(1), 50);

        // A duplicate report of the same value is quiet too, and does not complete twice.
        let again = state.report_record(&index, 10, 50, at(1_002));
        assert_eq!(again, RefreshOutcome::default());
        assert_eq!(state.completed_at(1), Some(at(1_000)));

        let all = state.refresh_all(&index, at(1_003));
        assert_eq!(all, RefreshOutcome::default());
    }

    #[test]
    fn counting_achievement_completes_on_one_of_its_objectives() {
        // "Complete one of these houses": complete_num 1 over three records.
        let data = Content::default()
            .achievement(2, 1, true, &[20, 21, 22])
            .achievement(3, 5, false, &[20])
            .build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();

        let outcome = state.report_record(&index, 21, 1, at(1_000));
        assert_eq!(outcome.completed_ids(), [2]);
        assert_eq!(state.amount(2), 1);
        assert!(state.is_complete(2));

        // Only the achievements watching the reported record were evaluated.
        assert_eq!(state.amount(3), 0);
        assert!(!state.is_complete(3));
    }

    #[test]
    fn refresh_all_completes_what_the_records_already_say() {
        // Records outlive a session, so the entry path resolves what they already say.
        let data = Content::default().achievement(1, 50, false, &[10]).build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();
        state.restore_record(10, 50);

        let outcome = state.refresh_all(&index, at(1_000));
        assert_eq!((outcome.moved, outcome.completed), (1, 1));
        assert_eq!(state.amount(1), 50);
        assert_eq!(state.completed_at(1), Some(at(1_000)));

        // The second pass has nothing left to do.
        assert_eq!(
            state.refresh_all(&index, at(1_001)),
            RefreshOutcome::default()
        );
        assert_eq!(state.completed_at(1), Some(at(1_000)));
    }

    #[test]
    fn completion_cascades_through_the_kind_nine_record() {
        // The child (31) completes first and reports its kind-9 record; the parent (30) watches
        // that record and is the lower id, so a full pass reaches it before the child does and has
        // to look at it again.
        let data = Content::default()
            .achievement(30, 1, true, &[300])
            .achievement(31, 3, false, &[100])
            .completion(300, 31)
            .build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();
        state.restore_record(100, 3);

        let outcome = state.refresh_all(&index, at(1_000));
        assert_eq!((outcome.moved, outcome.completed), (2, 2));
        assert_eq!(outcome.changed_ids(), [31, 30]);
        assert_eq!(outcome.completed_ids(), [31, 30]);
        assert!(state.is_complete(31));
        assert!(state.is_complete(30));
        assert_eq!(state.record_value(300), 1);

        // The completion record is a high-water mark like any other record.
        let again = state.refresh_all(&index, at(1_001));
        assert_eq!(again, RefreshOutcome::default());
        assert_eq!(state.record_value(300), 1);
    }

    #[test]
    fn met_objectives_stay_stored_until_the_prerequisite_completes() {
        // 2 requires 1 first (the workbench / level-30 shape). 1 has no completion record, so
        // only the prerequisite gate reaches 2.
        let data = Content::default()
            .achievement(1, 5, false, &[10])
            .achievement(2, 10, false, &[20])
            .prerequisite(2, 1)
            .build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();

        let gated = state.report_record(&index, 20, 10, at(1_000));
        assert_eq!(state.amount(2), 10);
        assert!(!state.is_complete(2));
        assert!(!gated.newly_completed());
        // The gate is not a credit: requiring 1 does not complete 1.
        assert!(!state.is_complete(1));
        assert_eq!(state.amount(1), 0);

        let unlocked = state.report_record(&index, 10, 5, at(1_001));
        assert!(state.is_complete(1));
        assert!(state.is_complete(2));
        // 2 was already at its target, so only its completion moved.
        assert_eq!(unlocked.completed_ids(), [1, 2]);
        assert_eq!(unlocked.changed_ids(), [1]);
    }

    #[test]
    fn prerequisite_cycle_keeps_both_incomplete() {
        // 20 and 21 require each other; meeting both objectives still cannot complete either.
        let data = Content::default()
            .achievement(20, 1, true, &[10])
            .achievement(21, 1, true, &[11])
            .prerequisite(20, 21)
            .prerequisite(21, 20)
            .build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();

        state.report_record(&index, 10, 1, at(1_000));
        state.report_record(&index, 11, 1, at(1_001));

        assert!(!state.is_complete(20));
        assert!(!state.is_complete(21));
        assert_eq!(state.amount(20), 1);
        assert_eq!(state.amount(21), 1);
    }

    #[test]
    fn mutual_completion_cycle_terminates_with_both_complete() {
        // 1 watches 2's completion record and 2 watches 1's, so each completion re-queues the
        // other. One of them starts on its own record, and the queue must stop when it comes back
        // around to an achievement that is already complete.
        let data = Content::default()
            .achievement(1, 1, true, &[10, 301])
            .achievement(2, 1, true, &[20, 300])
            .completion(300, 1)
            .completion(301, 2)
            .build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();

        let outcome = state.report_record(&index, 10, 1, at(1_000));
        assert_eq!((outcome.moved, outcome.completed), (2, 2));
        assert_eq!(outcome.completed_ids(), [1, 2]);
        assert_eq!(state.record_value(300), 1);
        assert_eq!(state.record_value(301), 1);
        assert!(state.is_complete(1));
        assert!(state.is_complete(2));

        // Nothing completes a second time.
        assert_eq!(
            state.refresh_all(&index, at(1_001)),
            RefreshOutcome::default()
        );
    }

    #[test]
    fn forced_completion_cascades_and_is_idempotent() {
        // 30 waits for the child 31's completion record, and 32 requires 31 first.
        let data = Content::default()
            .achievement(30, 1, true, &[300])
            .achievement(31, 3, false, &[100])
            .achievement(32, 2, false, &[200])
            .completion(300, 31)
            .prerequisite(32, 31)
            .build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();
        state.restore_record(200, 2);

        let outcome = state.force_complete(&index, 31, at(1_000));
        assert_eq!((outcome.moved, outcome.completed), (3, 3));
        assert!(state.is_complete(31));
        assert!(state.is_complete(30));
        assert!(state.is_complete(32));
        assert_eq!(state.record_value(300), 1);
        // The force leaves the amount alone, like the C#.
        assert_eq!(state.amount(31), 0);
        assert_eq!(state.completed_at(31), Some(at(1_000)));

        // Completing it again is a no-op: no second timestamp, no duplicate events.
        let again = state.force_complete(&index, 31, at(1_001));
        assert_eq!(again, RefreshOutcome::default());
        assert_eq!(state.completed_at(31), Some(at(1_000)));

        // An id the content does not have is refused.
        assert_eq!(
            state.force_complete(&index, 9999, at(1_002)),
            RefreshOutcome::default()
        );
    }

    #[test]
    fn switched_off_rows_are_not_evaluated() {
        let data = Content::default()
            .inactive_achievement(1, 1, true, &[10])
            .achievement(2, 1, true, &[10])
            .build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();

        assert!(!index.is_active(1));
        assert!(index.evaluate(1, |_| 1).is_none());
        assert!(index.evaluate(9999, |_| 1).is_none());

        // The report only moves the active achievement, and the switched-off row cannot be forced.
        let outcome = state.report_record(&index, 10, 1, at(1_000));
        assert_eq!(outcome.completed_ids(), [2]);
        assert_eq!(state.amount(1), 0);
        assert!(!state.is_complete(1));
        assert_eq!(
            state.force_complete(&index, 1, at(1_001)),
            RefreshOutcome::default()
        );

        // The list builder leaves it out too.
        let list = state.achievement_list(&index, true);
        assert_eq!(list.iter().map(|row| row.id).collect::<Vec<_>>(), [2]);
    }

    #[test]
    fn set_record_ignores_unknown_records_and_lets_watchers_follow() {
        let data = Content::default()
            .record(10, char_record_kind::KILL_NPC)
            .achievement(1, 10, false, &[10])
            .build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();

        assert_eq!(
            state.set_record(&index, 9999, 5, at(1_000)),
            RefreshOutcome::default()
        );
        assert_eq!(state.record_value(9999), 0);

        let outcome = state.set_record(&index, 10, 10, at(1_001));
        assert_eq!(outcome.completed_ids(), [1]);
        assert!(state.is_complete(1));

        // `Set`, not `Report`: a lower value is written, and the monotonic amount store keeps the
        // highest amount the character reached.
        state.set_record(&index, 10, 3, at(1_002));
        assert_eq!(state.record_value(10), 3);
        assert_eq!(state.amount(1), 10);
    }

    #[test]
    fn achievement_list_is_deterministic_and_skips_untouched_rows() {
        let data = Content::default()
            .achievement(1, 50, false, &[10])
            .achievement(2, 1, true, &[20])
            .achievement(5, 10, false, &[30])
            .build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();

        state.report_record(&index, 20, 1, at(1_000));

        let touched = state.achievement_list(&index, false);
        assert_eq!(touched.iter().map(|row| row.id).collect::<Vec<_>>(), [2]);
        assert_eq!(touched[0].amount, 1);
        assert_ne!(touched[0].complete, DateTime::<Utc>::default());

        let all = state.achievement_list(&index, true);
        assert_eq!(all.iter().map(|row| row.id).collect::<Vec<_>>(), [1, 2, 5]);
        assert_eq!(all[0].amount, 0);
        assert_eq!(all[0].complete, DateTime::<Utc>::default());

        // A forced completion is listed even at amount 0.
        state.force_complete(&index, 5, at(1_001));
        let after = state.achievement_list(&index, false);
        assert_eq!(after.iter().map(|row| row.id).collect::<Vec<_>>(), [2, 5]);
        assert_eq!(after[1].amount, 0);
        assert_eq!(after[1].complete, at(1_001));
    }

    #[test]
    fn snapshots_are_ordered_and_round_trip() {
        let data = Content::default()
            .achievement(1, 50, false, &[10])
            .achievement(2, 10, false, &[20])
            .record(20, char_record_kind::KILL_NPC)
            .build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();

        state.report_record(&index, 20, 4, at(1_000));
        state.report_record(&index, 10, 7, at(1_001));

        // Ascending record id, whatever order they were reported in.
        let records = state.record_snapshot();
        assert_eq!(records.keys().copied().collect::<Vec<_>>(), [10, 20]);
        assert_eq!(records[&10], 7);

        // Ascending achievement id, with the timestamps a save carries.
        let progress = state.progress_snapshot();
        assert_eq!(
            progress
                .iter()
                .map(|row| row.achievement_id())
                .collect::<Vec<_>>(),
            [1, 2]
        );
        assert_eq!(progress[0].amount(), 7);
        assert_eq!(progress[0].completed_at(), None);
        assert!(!progress[0].is_complete());

        // The DB-load path puts the same rows back.
        let mut loaded = AchievementState::default();
        for (record_id, value) in state.record_snapshot() {
            loaded.restore_record(record_id, value);
        }
        for row in state.progress_snapshot() {
            loaded.restore_progress(
                row.achievement_id(),
                i64::from(row.amount()),
                row.completed_at(),
            );
        }
        assert_eq!(loaded.record_snapshot(), state.record_snapshot());
        assert_eq!(loaded.progress_snapshot(), state.progress_snapshot());
    }

    #[test]
    fn forgetting_an_achievement_lasts_until_its_records_are_cleared() {
        // The reset path: dropping the progress row alone is not enough, because the records that
        // completed it still say the character did it.
        let data = Content::default()
            .achievement(1, 1, true, &[10, 11])
            .build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();
        state.report_record(&index, 10, 1, at(1_000));
        assert!(state.is_complete(1));

        let dropped = state.forget_achievement(1);
        assert_eq!(dropped.map(|row| row.amount()), Some(1));
        assert!(!state.is_complete(1));
        assert_eq!(state.refresh_all(&index, at(1_001)).completed_ids(), [1]);

        // Clearing the records the achievement is judged by makes the reset stick.
        state.forget_achievement(1);
        state.clear_record(10);
        state.clear_record(11);
        assert_eq!(state.clear_record(10), None);
        assert_eq!(
            state.refresh_all(&index, at(1_002)),
            RefreshOutcome::default()
        );
        assert!(!state.is_complete(1));
        assert_eq!(state.amount(1), 0);
    }

    #[test]
    fn restore_progress_clamps_the_amount_to_the_packet_range() {
        let data = Content::default().achievement(1, 50, false, &[10]).build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();

        // A stored amount ahead of the records keeps a full bar and does not complete anything by
        // itself, and a negative one is clamped to nothing.
        state.restore_progress(1, i64::from(i32::MAX) + 5, Some(at(1_000)));
        state.restore_progress(2, -4, None);

        assert_eq!(state.amount(1), i32::MAX);
        assert!(state.is_complete(1));
        assert_eq!(state.amount(2), 0);

        // Re-evaluating keeps the stored amount and does not emit a change that contradicts it.
        assert_eq!(
            state.refresh_all(&index, at(1_001)),
            RefreshOutcome::default()
        );
        assert_eq!(state.amount(1), i32::MAX);
    }

    #[test]
    fn a_completion_waits_to_be_paid_until_its_receipt_is_marked() {
        let data = Content::default().achievement(1, 1, true, &[10]).build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();

        // Nothing has completed: nothing to pay, and nothing can be marked.
        assert!(state.pending_reward_ids().is_empty());
        assert!(!state.has_pending_reward(1));
        assert!(!state.mark_reward_delivered(1));

        state.report_record(&index, 10, 1, at(1_000));
        assert!(state.is_complete(1));
        assert!(state.has_pending_reward(1));
        assert_eq!(state.pending_reward_ids(), [1]);
        let row = state.progress(1).expect("row");
        assert!(row.has_pending_reward());
        assert!(!row.reward_delivered());

        // The receipt is one-way: the first mark is the one that settles it.
        assert!(state.mark_reward_delivered(1));
        assert!(!state.mark_reward_delivered(1));
        assert!(state.progress(1).expect("row").reward_delivered());
        assert!(!state.has_pending_reward(1));
        assert!(state.pending_reward_ids().is_empty());
    }

    #[test]
    fn only_completed_achievements_can_be_marked_delivered() {
        let data = Content::default().achievement(1, 50, false, &[10]).build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();

        // A touched but unfinished row is progress, not a payable completion.
        state.report_record(&index, 10, 20, at(1_000));
        assert_eq!(state.amount(1), 20);
        assert!(!state.is_complete(1));
        assert!(!state.has_pending_reward(1));
        assert!(!state.mark_reward_delivered(1));
        assert!(!state.progress(1).expect("row").reward_delivered());

        // An id the character has no row for is not payable either.
        assert!(!state.mark_reward_delivered(2));
        assert!(state.pending_reward_ids().is_empty());

        // The completion that follows is payable.
        state.report_record(&index, 10, 50, at(1_001));
        assert!(state.mark_reward_delivered(1));
        assert!(state.pending_reward_ids().is_empty());
    }

    #[test]
    fn a_quiet_pass_completion_is_pending_too() {
        // The entry path completes from saved records without a caller acting on the events; the
        // reward is still owed, and a later pass that produces no events does not change that.
        let data = Content::default().achievement(1, 50, false, &[10]).build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();
        state.restore_record(10, 50);

        assert_eq!(state.refresh_all(&index, at(1_000)).completed_ids(), [1]);
        assert_eq!(state.pending_reward_ids(), [1]);
        assert_eq!(
            state.refresh_all(&index, at(1_001)),
            RefreshOutcome::default()
        );
        assert_eq!(state.pending_reward_ids(), [1]);
    }

    #[test]
    fn replaying_a_paid_completion_does_not_re_open_it() {
        let data = Content::default().achievement(1, 50, false, &[10]).build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();
        state.report_record(&index, 10, 50, at(1_000));
        assert!(state.mark_reward_delivered(1));

        // A higher record, a full pass and a forced completion: the achievement is already
        // complete, so nothing completes it again and the receipt stays.
        state.report_record(&index, 10, 80, at(1_001));
        state.refresh_all(&index, at(1_002));
        assert_eq!(
            state.force_complete(&index, 1, at(1_003)),
            RefreshOutcome::default()
        );
        assert_eq!(state.completed_at(1), Some(at(1_000)));
        assert!(state.progress(1).expect("row").reward_delivered());
        assert!(state.pending_reward_ids().is_empty());
    }

    #[test]
    fn forgetting_clears_the_receipt_and_re_earning_is_payable_again() {
        let data = Content::default().achievement(1, 1, true, &[10]).build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();
        state.report_record(&index, 10, 1, at(1_000));
        assert!(state.mark_reward_delivered(1));

        // The reset path drops the row, receipt included.
        let dropped = state.forget_achievement(1).expect("row");
        assert!(dropped.reward_delivered());
        assert!(state.pending_reward_ids().is_empty());
        assert!(!state.mark_reward_delivered(1));

        // Re-earning after the reset is a fresh completion, so its reward is owed again.
        assert_eq!(state.refresh_all(&index, at(1_001)).completed_ids(), [1]);
        assert!(!state.progress(1).expect("row").reward_delivered());
        assert_eq!(state.pending_reward_ids(), [1]);
        assert!(state.mark_reward_delivered(1));
    }

    #[test]
    fn receipts_round_trip_through_the_snapshot() {
        let data = Content::default().achievement(1, 1, true, &[10]).build();
        let index = AchievementIndex::new(&data);
        let mut state = AchievementState::default();
        state.report_record(&index, 10, 1, at(1_000));
        assert!(state.mark_reward_delivered(1));
        // A stored row may carry a receipt on an unfinished achievement; it is restored as it is
        // and is not payable.
        state.restore_progress_with_receipt(2, 3, None, true);
        state.restore_progress(3, 4, Some(at(1_002)));

        let mut loaded = AchievementState::default();
        for row in state.progress_snapshot() {
            loaded.restore_progress_with_receipt(
                row.achievement_id(),
                i64::from(row.amount()),
                row.completed_at(),
                row.reward_delivered(),
            );
        }
        assert_eq!(loaded.progress_snapshot(), state.progress_snapshot());
        assert!(loaded.progress(1).expect("row").reward_delivered());
        assert!(loaded.progress(2).expect("row").reward_delivered());
        assert!(!loaded.has_pending_reward(2));
        assert!(!loaded.progress(3).expect("row").reward_delivered());
        assert_eq!(loaded.pending_reward_ids(), [3]);

        // The compatibility setter keeps a receipt the row already has, so restoring a paid
        // completion again cannot make its reward payable a second time.
        loaded.restore_progress(1, 1, Some(at(1_000)));
        assert!(loaded.progress(1).expect("row").reward_delivered());
        assert_eq!(loaded.pending_reward_ids(), [3]);
    }

    // ---- the cached index ----

    #[test]
    fn the_derived_index_is_built_once_shared_across_indexes_and_out_of_equality() {
        let data = Content::default()
            .achievement(1, 1, true, &[10])
            .record(10, char_record_kind::KILL_NPC)
            .completion(11, 1)
            .prerequisite(2, 1)
            .build();
        // A clone taken before any index exists, so its cache is cold.
        let clone = data.clone();

        let first = AchievementIndex::new(&data);
        let built = data.derived();
        let second = AchievementIndex::new(&data);

        // One cache serves every index over the value, and the lookups agree.
        assert!(std::ptr::eq(built, data.derived()));
        assert_eq!(first.active_ids(), second.active_ids());
        assert_eq!(first.watching_record(10), second.watching_record(10));
        assert_eq!(first.prerequisites(2), second.prerequisites(2));
        assert_eq!(
            first.gated_by_prerequisite(1),
            second.gated_by_prerequisite(1)
        );
        assert_eq!(first.completion_record(1), second.completion_record(1));
        assert_eq!(
            first.achievement_for_completion_record(11),
            second.achievement_for_completion_record(11)
        );

        // The cache is not part of equality: a warm value still equals an equal cold clone, and
        // warming the clone changes nothing.
        assert_eq!(data, clone);
        assert_eq!(clone, data);
        let _ = AchievementIndex::new(&clone);
        assert_eq!(data, clone);

        // And equality still notices a real content change.
        let mut changed = clone;
        changed.achievements.get_mut(&1).expect("row").complete_num = 99;
        assert_ne!(data, changed);
    }
}
