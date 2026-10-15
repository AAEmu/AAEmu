//! The achievement event facade: the one surface gameplay producers call to credit achievements.
//!
//! [`super::achievement_manager::AchievementContent`] already batches records and evaluates the
//! achievements that watch them, and [`super::achievement_rewards::deliver_pending`] hands over
//! what a completion pays. This module is the thin typed front for producers: a small
//! [`AchievementInput`] with its [`RecordMatch`] filter describes one event without knowing the
//! content, and every entry point resolves the loaded content itself and does nothing when a
//! record or achievement is absent.
//!
//! # Locking
//!
//! **Call every function here with no character (`player.data()`), economy `ItemTxn` /
//! `ops_lock`, or item container lock held.** The report paths take those locks themselves, and
//! `ops_lock` is not re-entrant: a call from inside an item transaction deadlocks. A producer
//! that learns about an event while it holds a lock (inside an `ItemTxn`, under a container or
//! character guard) should queue the [`AchievementInput`] (it is `Copy`, small, and has no
//! references) and flush it through [`report_inputs`] after every lock is released.
//!
//! The reward pass runs after the report has released the locks, and only when the report
//! completed something new; a reward that failed to hand over stays pending and is retried by
//! [`retry_pending_rewards`] (the periodic path) or by the next entry ([`initial_snapshot`]).
//!
//! # Record shapes
//!
//! The audited semantics of the authored `char_records` (see `rust/docs/achievement-hook-audit.txt`):
//!
//! * `value2 == 0` usually means "no sub-context" (any zone, no party cap).
//! * `u32::MAX` is the table's `-1` and is the any-grade wildcard ([`GRADE_WILDCARD`]).
//! * `KILL_NPC` (`kind_id` 25): `value1` is the npc template, `value2` is the party-member level
//!   cap (`0` = none; a level-20 cap means a party member of at most level 20 must have been in
//!   the kill).
//! * The event counters ([`AchievementInput::Increment`]) are raised by their own kind; the state
//!   records ([`AchievementInput::Record`]) are absolute high-water reports, and the entry path
//!   reconstructs the ones the character's current state can prove through
//!   [`refresh_current`].

use chrono::{DateTime, Utc};

use super::achievement::CharRecords;
use super::achievement_manager::{
    back_credit_values, AchievementContent, InitialSnapshot, RecordReport,
};
use super::achievement_rewards::{deliver_pending, RewardDelivery};
use super::achievement_runtime::RefreshOutcome;
use crate::server::GameServer;
use crate::world::PlayerRef;

/// The raw `char_records.value2` the content stores for the `-1` "any grade" sentinel.
pub const GRADE_WILDCARD: u32 = u32::MAX;

/// What one event means against a `char_records` row of its kind.
///
/// The variants carry only the event's own facts, never a record id: [`AchievementInput`] names
/// the kind and the facade decides which authored rows that event counts towards. The filter is
/// owned and `Copy`, so it can be queued across a lock as part of an [`AchievementInput`] (and
/// later as an `ItemFx` variant).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RecordMatch {
    /// Every record of the kind.
    Any,
    /// `value1 == value1`.
    Value1(u32),
    /// `value1 == value1 && value2 == value2` (exact sub-context, e.g. an npc + emotion, or a
    /// sold item + destination npc).
    Value1And2 { value1: u32, value2: u32 },
    /// A grade-bearing row (`GET_ITEM_TYPE` / `MAKE_ITEM_*`): `value1` names the template (or
    /// `items.impl_id`) and `value2` is the grade. A row's raw [`GRADE_WILDCARD`] matches any
    /// event grade; `grade: None` is an event with no grade and matches every row of the
    /// template.
    GradeWildcard { value1: u32, grade: Option<u32> },
    /// `value1 == value1` and `value2` is `value2` or the row's `0` "any" wildcard (zone-group
    /// records like `GET_HONOR_POINT`: `value2 == 0` counts in any zone, otherwise only in the
    /// listed zone group).
    Value1OrZeroValue2 { value1: u32, value2: u32 },
    /// A zone-group record (`DEAD_BY_PVP`): `value1` is the zone group id, `0` = any zone.
    ZoneGroup { zone_group: u32 },
    /// A `KILL_NPC` event: `value1` is the npc template and `value2` is the party-member level
    /// cap. A `0` cap needs no party member; a positive cap only counts when the lowest eligible
    /// party member's level is known (`lowest_party_level`) and at most the cap.
    NpcKill {
        template_id: u32,
        lowest_party_level: Option<u32>,
    },
}

impl RecordMatch {
    /// Whether one `char_records` row counts for this event.
    pub fn matches(self, record: &CharRecords) -> bool {
        match self {
            Self::Any => true,
            Self::Value1(value1) => record.value1 == value1,
            Self::Value1And2 { value1, value2 } => {
                record.value1 == value1 && record.value2 == value2
            }
            Self::GradeWildcard { value1, grade } => {
                record.value1 == value1
                    && match grade {
                        None => true,
                        Some(grade) => record.value2 == grade || record.value2 == GRADE_WILDCARD,
                    }
            }
            Self::Value1OrZeroValue2 { value1, value2 } => {
                record.value1 == value1 && (record.value2 == 0 || record.value2 == value2)
            }
            Self::ZoneGroup { zone_group } => record.value1 == 0 || record.value1 == zone_group,
            Self::NpcKill {
                template_id,
                lowest_party_level,
            } => {
                record.value1 == template_id
                    && (record.value2 == 0
                        || lowest_party_level.is_some_and(|level| level <= record.value2))
            }
        }
    }
}

/// One achievement-relevant gameplay event.
///
/// This is the queueable unit: a producer that learns about the event under a lock stores the
/// input (it is `Copy`, small, and holds no references) and flushes it through [`report_inputs`]
/// once every lock is released.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AchievementInput {
    /// Report a record's absolute (current-state) value; the store keeps the high-water mark.
    /// An unknown record id, or record id 0, is ignored.
    Record { record_id: u32, value: i64 },
    /// Raise every `kind_id` record the matcher accepts by `amount`; `amount == 0` does nothing.
    Increment {
        kind_id: u32,
        amount: i64,
        matcher: RecordMatch,
    },
    /// Report `value` as the absolute (current-state) value of every `kind_id` record the matcher
    /// accepts (the state kinds that are not reconstructible from [`refresh_current`], e.g. a
    /// mate's level against its template). The store keeps the high-water mark.
    Absolute {
        kind_id: u32,
        value: i64,
        matcher: RecordMatch,
    },
}

impl AchievementInput {
    /// Whether the content has a record this event could move (lock-free).
    fn has_authored_record(&self, content: &AchievementContent<'_>) -> bool {
        match *self {
            Self::Record { record_id, .. } => {
                record_id != 0 && content.data().char_records.contains_key(&record_id)
            }
            Self::Increment {
                kind_id,
                amount,
                matcher,
            } => amount != 0 && content.has_record(kind_id, |record| matcher.matches(record)),
            Self::Absolute {
                kind_id, matcher, ..
            } => content.has_record(kind_id, |record| matcher.matches(record)),
        }
    }
}

/// What one facade report did: the core's evaluation pass, the records that moved, and the
/// rewards delivered because the report completed something.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct AchievementReport {
    /// The evaluation passes, events in send order.
    pub refresh: RefreshOutcome,
    /// The records whose stored value moved, ascending within each batch.
    pub records: Vec<(u32, i64)>,
    /// What the reward pass handed over after the report (`deliver_pending`); empty unless the
    /// report newly completed an achievement.
    pub rewards: RewardDelivery,
}

impl AchievementReport {
    /// Whether the report completed any achievement it had not already completed.
    pub fn newly_completed(&self) -> bool {
        self.refresh.newly_completed()
    }
}

/// What [`initial_snapshot`] did: the manager's entry snapshot plus the reward pass.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct EntryReport {
    /// The back-credit + list pass ([`AchievementContent::initial_snapshot`]).
    pub snapshot: InitialSnapshot,
    /// What the reward pass handed over: completions the back-credit found and completions a
    /// previous session left unpaid (the restored `reward_delivered` receipt was false).
    pub rewards: RewardDelivery,
}

/// Reports one event and pays the rewards of anything it completed.
///
/// Equivalent to [`report_inputs`] over a single input.
pub fn report_input(
    server: &GameServer,
    player: &PlayerRef,
    input: AchievementInput,
    now: DateTime<Utc>,
) -> AchievementReport {
    report_inputs(server, player, [input], now)
}

/// Reports events in order and pays the rewards of anything they completed.
///
/// Batching follows the core's own boundaries and keeps the input order between them: a run of
/// adjacent [`AchievementInput::Record`]s is written and evaluated as one batch (one change event
/// for an achievement watching two of them, exactly like
/// [`AchievementContent::report_values`]), and every [`AchievementInput::Increment`] is its own
/// batch in input order. Every `Increment` starts a new batch, so an event that follows a
/// `Record` is evaluated only after the report's locks are released and the record is stored.
///
/// Returns a merged [`AchievementReport`]: the passes' events in send order, the records that
/// moved, and — when a pass newly completed an achievement — the rewards
/// [`deliver_pending`](super::achievement_rewards::deliver_pending) handed over after the report
/// released its locks. A reward that could not be handed over stays pending (see
/// [`retry_pending_rewards`]).
pub fn report_inputs(
    server: &GameServer,
    player: &PlayerRef,
    inputs: impl IntoIterator<Item = AchievementInput>,
    now: DateTime<Utc>,
) -> AchievementReport {
    let content = AchievementContent::new(&server.game_data.systems.social.achievements);
    let mut report = AchievementReport::default();
    let mut records: Vec<(u32, i64)> = Vec::new();

    for input in inputs {
        match input {
            AchievementInput::Record { record_id, value } => records.push((record_id, value)),
            AchievementInput::Absolute {
                kind_id,
                value,
                matcher,
            } => {
                let mut ids: Vec<u32> = content
                    .data()
                    .char_records
                    .values()
                    .filter(|record| record.kind_id == kind_id && matcher.matches(record))
                    .map(|record| record.id)
                    .collect();
                ids.sort_unstable();
                records.extend(ids.into_iter().map(|record_id| (record_id, value)));
            }
            AchievementInput::Increment {
                kind_id,
                amount,
                matcher,
            } => {
                // Most events have no authored record: skip them before any lock is taken.
                if amount == 0 || !content.has_record(kind_id, |record| matcher.matches(record)) {
                    continue;
                }
                if !records.is_empty() {
                    let values = std::mem::take(&mut records);
                    merge_record_report(
                        &mut report,
                        content.report_values(server, player, now, values),
                    );
                }
                merge_record_report(
                    &mut report,
                    content.increment_matching(server, player, now, kind_id, amount, |record| {
                        matcher.matches(record)
                    }),
                );
            }
        }
    }

    if !records.is_empty() {
        merge_record_report(
            &mut report,
            content.report_values(server, player, now, records),
        );
    }

    if report.newly_completed() {
        report.rewards = deliver_pending(server, player);
    }
    report
}

/// Re-reports everything the character's **current** state can prove (the entry back-credit
/// units: level, ability levels, actability points, hours online, carried gold, the current
/// party / raid / family / expedition memberships and the completed quest bitset) and evaluates
/// what watches them, this time as a live report: the client hears the change / completion
/// packets, and anything completed pays its reward.
///
/// [`back_credit_values`](super::achievement_manager::back_credit_values) is read under the
/// character lock and released before the report, which takes the report's own locks.
pub fn refresh_current(
    server: &GameServer,
    player: &PlayerRef,
    now: DateTime<Utc>,
) -> AchievementReport {
    let values = {
        let character = player.data();
        back_credit_values(
            server,
            &character,
            &server.game_data.systems.social.achievements,
        )
    };

    let content = AchievementContent::new(&server.game_data.systems.social.achievements);
    let mut report = AchievementReport::default();
    merge_record_report(
        &mut report,
        content.report_values(server, player, now, values),
    );
    if report.newly_completed() {
        report.rewards = deliver_pending(server, player);
    }
    report
}

/// The entry path: back-credit the current state, resolve what the records already say, push the
/// touched list, then pay every reward that is owed.
///
/// The back-credit and list are the manager's [`AchievementContent::initial_snapshot`] (quiet
/// about progress: the list carries the amounts and completion times, and no change / completion
/// packet is sent for the back-credit). The reward pass runs after the snapshot released its
/// locks and pays both the completions the back-credit found and any completion a previous
/// session left unpaid.
pub fn initial_snapshot(
    server: &GameServer,
    player: &PlayerRef,
    now: DateTime<Utc>,
) -> EntryReport {
    let content = AchievementContent::new(&server.game_data.systems.social.achievements);
    let snapshot = content.initial_snapshot(server, player, now);
    let rewards = deliver_pending(server, player);
    EntryReport { snapshot, rewards }
}

/// Retries every reward the character's completed achievements still owe (the period path for a
/// pass that could not hand an item over, e.g. a full bag at completion time).
///
/// This is the same helper the report paths use, exposed for a timer; a reward becomes pending
/// again only through a new completion, and a failed hand-over never writes its receipt.
pub fn retry_pending_rewards(server: &GameServer, player: &PlayerRef) -> RewardDelivery {
    deliver_pending(server, player)
}

/// What the other subsystems call to credit achievements, through `server.systems.hooks`
/// (`HookSlot<dyn AchievementHooks>`; see `systems::report_achievements`).
///
/// A caller of [`Self::report`] / [`Self::refresh_current`] must hold no character or container
/// lock. The economy operation lock is tolerated, because [`LiveAchievementHooks`] queues the
/// event while the current thread holds it. A caller that holds some other lock (the team
/// manager's, a family's) uses the `*_later` variants, which always queue. Queued events run from
/// [`AchievementHooks::flush_deferred`], which the economy transaction, the quest operation and
/// the packet dispatcher call once their locks are released. Without an installed implementation
/// (tests that do not install the social hooks) every call is a no-op.
pub trait AchievementHooks: Send + Sync {
    /// [`report_inputs`] over `inputs`, stamped with the current time.
    fn report(&self, server: &GameServer, player: &PlayerRef, inputs: &[AchievementInput]);

    /// [`refresh_current`], stamped with the current time.
    fn refresh_current(&self, server: &GameServer, player: &PlayerRef);

    /// Queues a [`refresh_current`] for the next [`Self::flush_deferred`] (callers that hold a
    /// lock the report path also takes).
    fn refresh_later(&self, player: &PlayerRef);

    /// Runs the queued events. A no-op while this thread still holds the economy operation lock.
    fn flush_deferred(&self, server: &GameServer);
}

/// One queued event.
enum Deferred {
    Report(PlayerRef, Vec<AchievementInput>),
    Refresh(PlayerRef),
}

/// The live [`AchievementHooks`] (installed by `social::bridges::install`).
#[derive(Default)]
pub struct LiveAchievementHooks {
    deferred: std::sync::Mutex<Vec<Deferred>>,
}

impl LiveAchievementHooks {
    fn queue(&self, event: Deferred) {
        self.deferred
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .push(event);
    }
}

impl AchievementHooks for LiveAchievementHooks {
    fn report(&self, server: &GameServer, player: &PlayerRef, inputs: &[AchievementInput]) {
        // Most events have no authored record: drop them before queueing or locking anything.
        let content = AchievementContent::new(&server.game_data.systems.social.achievements);
        let relevant: Vec<AchievementInput> = inputs
            .iter()
            .copied()
            .filter(|input| input.has_authored_record(&content))
            .collect();
        if relevant.is_empty() {
            return;
        }
        if crate::systems::economy::System::ops_held_on_this_thread() {
            self.queue(Deferred::Report(player.clone(), relevant));
            return;
        }
        report_inputs(server, player, relevant, Utc::now());
    }

    fn refresh_current(&self, server: &GameServer, player: &PlayerRef) {
        if crate::systems::economy::System::ops_held_on_this_thread() {
            self.queue(Deferred::Refresh(player.clone()));
            return;
        }
        refresh_current(server, player, Utc::now());
    }

    fn refresh_later(&self, player: &PlayerRef) {
        self.queue(Deferred::Refresh(player.clone()));
    }

    fn flush_deferred(&self, server: &GameServer) {
        if crate::systems::economy::System::ops_held_on_this_thread() {
            return;
        }
        loop {
            let batch =
                std::mem::take(&mut *self.deferred.lock().unwrap_or_else(|e| e.into_inner()));
            if batch.is_empty() {
                return;
            }
            for event in batch {
                match event {
                    Deferred::Report(player, inputs) => {
                        report_inputs(server, &player, inputs, Utc::now());
                    }
                    Deferred::Refresh(player) => {
                        refresh_current(server, &player, Utc::now());
                    }
                }
            }
        }
    }
}

/// The lowest level among the party / raid members of `player` (the killer included), or the
/// player's own level when solo: the `KILL_NPC` party-level cap input ([`RecordMatch::NpcKill`]).
pub fn lowest_party_level(server: &GameServer, player: &PlayerRef) -> u32 {
    let own = u32::from(player.data().unit.level);
    let Some(team) = server.systems.social.teams.team_by_obj_id(player.obj_id) else {
        return own;
    };
    team.members
        .iter()
        .flatten()
        .map(|member| u32::from(member.character.data().unit.level))
        .fold(own, u32::min)
}

/// Folds one manager pass into the facade result, keeping event order.
fn merge_record_report(report: &mut AchievementReport, part: RecordReport) {
    report.refresh.moved += part.refresh.moved;
    report.refresh.completed += part.refresh.completed;
    report.refresh.events.extend(part.refresh.events);
    report.records.extend(part.records);
}

#[cfg(test)]
mod tests {
    use std::sync::Arc;

    use super::*;
    use crate::data::items::ItemTemplate;
    use crate::data::GameData;
    use crate::models::container::slot_type;
    use crate::models::item::Item;
    use crate::opcodes::sc;
    use crate::systems::economy::inventory::Inv;
    use crate::systems::social::achievement::{
        char_record_kind, AchievementGameData, AchievementObjectives, Achievements,
    };
    use crate::systems::social::achievement_rewards::GrantedReward;
    use crate::systems::social::achievement_runtime::AchievementEvent;
    use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};

    /// The reward item the reward tests grant (one unit per stack).
    const REWARD_ITEM: u32 = 7001;

    /// A fixed instant, so completed-at assertions are deterministic.
    fn at(seconds: i64) -> DateTime<Utc> {
        DateTime::from_timestamp(seconds, 0).expect("valid timestamp")
    }

    /// Content fixture: what `AchievementGameData.Load` would have produced for a small table.
    #[derive(Default)]
    struct Content {
        data: AchievementGameData,
        next_objective: u32,
    }

    impl Content {
        fn achievement(
            mut self,
            id: u32,
            complete_num: u32,
            complete_or: bool,
            records: &[u32],
        ) -> Self {
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
            self.data.achievements.insert(
                id,
                Achievements {
                    id,
                    complete_num,
                    complete_or,
                    is_active: true,
                    achievement_objectives: objectives.clone(),
                    ..Default::default()
                },
            );
            if !objectives.is_empty() {
                self.data.achievement_objectives.insert(id, objectives);
            }
            self
        }

        /// Gives an already-declared achievement an item reward.
        fn reward(mut self, id: u32, item_id: u32) -> Self {
            let achievement = self.data.achievements.entry(id).or_default();
            achievement.id = id;
            achievement.item_id = item_id;
            self
        }

        /// A `char_records` definition with its per-kind `value1` / `value2` parameters.
        fn record(mut self, id: u32, kind_id: u32, value1: u32, value2: u32) -> Self {
            self.data.char_records.insert(
                id,
                CharRecords {
                    id,
                    kind_id,
                    value1,
                    value2,
                },
            );
            self
        }

        /// The kind-9 record that counts an achievement's completion.
        fn completion(self, record_id: u32, achievement_id: u32) -> Self {
            self.record(
                record_id,
                char_record_kind::COMPLETE_ACHIEVEMENT,
                achievement_id,
                0,
            )
        }

        fn build(self) -> AchievementGameData {
            self.data
        }
    }

    struct Fixture {
        server: Arc<GameServer>,
        client: TestClient,
        player: PlayerRef,
    }

    /// A server with the reward template, the given content, the character name registered and a
    /// spawned character with its bag and mail escrow registered (the reward pass resolves
    /// containers through the item manager).
    fn fixture(data: AchievementGameData) -> Fixture {
        let mut game_data = GameData::empty();
        game_data
            .items
            .templates
            .insert(REWARD_ITEM, ItemTemplate::plain(REWARD_ITEM, 1));
        game_data.systems.social.achievements = data;
        let server = Arc::new(test_server_with(game_data));
        server.set_self();
        server.names.add_character(1, "Tester", 1);
        let mut client = TestClient::new(&server, 1, 1);
        let player = spawn_player(
            &server,
            &client,
            test_character(1, "Tester", 0.0, 0.0),
            1000,
        );
        let inventory = Inv::of(&player);
        server.items.add_container(inventory.bag());
        if let Some(escrow) = inventory.get(slot_type::MAIL) {
            server.items.add_container(escrow);
        }
        client.drain();
        Fixture {
            server,
            client,
            player,
        }
    }

    fn bag_items(f: &Fixture) -> Vec<Item> {
        Inv::of(&f.player)
            .bag()
            .lock()
            .expect("container poisoned")
            .items
            .clone()
    }

    /// The achievement packets of the drained frames, in send order.
    fn achievement_ops(client: &mut TestClient) -> Vec<u16> {
        client
            .drain_ops()
            .into_iter()
            .filter(|opcode| {
                matches!(
                    *opcode,
                    sc::SCAchievementsPacket
                        | sc::SCAchievementChangedPacket
                        | sc::SCAchievementCompletedPacket
                        | sc::SCAchievementItemSentPacket
                )
            })
            .collect()
    }

    // ---- the typed filters ----

    #[test]
    fn typed_filters_match_the_authored_record_shapes() {
        let row = |value1, value2| CharRecords {
            id: 1,
            kind_id: char_record_kind::KILL_NPC,
            value1,
            value2,
        };

        assert!(RecordMatch::Any.matches(&row(1, 2)));

        assert!(RecordMatch::Value1(100).matches(&row(100, 0)));
        assert!(!RecordMatch::Value1(100).matches(&row(101, 0)));

        assert!(RecordMatch::Value1And2 {
            value1: 100,
            value2: 12
        }
        .matches(&row(100, 12)));
        assert!(!RecordMatch::Value1And2 {
            value1: 100,
            value2: 12
        }
        .matches(&row(100, 0)));

        // A specific grade counts only that grade and the authored any-grade wildcard.
        assert!(RecordMatch::GradeWildcard {
            value1: 5,
            grade: Some(2)
        }
        .matches(&row(5, 2)));
        assert!(RecordMatch::GradeWildcard {
            value1: 5,
            grade: Some(2)
        }
        .matches(&row(5, GRADE_WILDCARD)));
        assert!(!RecordMatch::GradeWildcard {
            value1: 5,
            grade: Some(2)
        }
        .matches(&row(5, 3)));
        // An event with no grade matches every row of the template.
        assert!(RecordMatch::GradeWildcard {
            value1: 5,
            grade: None
        }
        .matches(&row(5, 3)));

        // The row's `0` sub-context is "any"; the event's zone still has to match a listed one.
        assert!(RecordMatch::Value1OrZeroValue2 {
            value1: 1,
            value2: 33
        }
        .matches(&row(1, 0)));
        assert!(RecordMatch::Value1OrZeroValue2 {
            value1: 1,
            value2: 33
        }
        .matches(&row(1, 33)));
        assert!(!RecordMatch::Value1OrZeroValue2 {
            value1: 1,
            value2: 33
        }
        .matches(&row(1, 34)));

        // NPC kill: the cap needs a known party member at or under it; 0 needs no party member.
        let no_cap = RecordMatch::NpcKill {
            template_id: 11364,
            lowest_party_level: None,
        };
        assert!(no_cap.matches(&row(11364, 0)));
        assert!(!no_cap.matches(&row(11364, 20)));
        assert!(!no_cap.matches(&row(999, 0)));

        let cap_20 = RecordMatch::NpcKill {
            template_id: 11364,
            lowest_party_level: Some(20),
        };
        assert!(cap_20.matches(&row(11364, 20)));
        assert!(cap_20.matches(&row(11364, 50)));
        assert!(!cap_20.matches(&row(11364, 19)));
        assert!(!RecordMatch::NpcKill {
            template_id: 11364,
            lowest_party_level: Some(21)
        }
        .matches(&row(11364, 20)));
    }

    // ---- quiet inputs ----

    #[tokio::test]
    async fn unknown_and_zero_records_are_ignored() {
        let data = Content::default()
            .achievement(1, 1, true, &[10])
            .record(10, char_record_kind::KILL_NPC, 100, 0)
            .build();
        let mut f = fixture(data);

        let zero_id = report_input(
            &f.server,
            &f.player,
            AchievementInput::Record {
                record_id: 0,
                value: 5,
            },
            at(1_000),
        );
        assert_eq!(zero_id, AchievementReport::default());

        let unknown_id = report_input(
            &f.server,
            &f.player,
            AchievementInput::Record {
                record_id: 9999,
                value: 5,
            },
            at(1_000),
        );
        assert_eq!(unknown_id, AchievementReport::default());
        assert_eq!(f.player.data().achievement_state.record_value(9999), 0);

        // Nothing matches the event, and a zero increment never touches the records.
        let no_match = report_input(
            &f.server,
            &f.player,
            AchievementInput::Increment {
                kind_id: char_record_kind::KILL_NPC,
                amount: 1,
                matcher: RecordMatch::Value1(424_242),
            },
            at(1_000),
        );
        assert_eq!(no_match, AchievementReport::default());
        let zero_amount = report_input(
            &f.server,
            &f.player,
            AchievementInput::Increment {
                kind_id: char_record_kind::KILL_NPC,
                amount: 0,
                matcher: RecordMatch::Any,
            },
            at(1_000),
        );
        assert_eq!(zero_amount, AchievementReport::default());
        assert_eq!(f.player.data().achievement_state.record_value(10), 0);
        assert!(f.client.drain_ops().is_empty());
    }

    // ---- live reports ----

    #[tokio::test]
    async fn a_live_completion_pays_one_reward_and_a_replay_is_silent() {
        let data = Content::default()
            .achievement(1, 1, true, &[10])
            .reward(1, REWARD_ITEM)
            .record(10, char_record_kind::KILL_NPC, 100, 0)
            .build();
        let mut f = fixture(data);

        let kill = AchievementInput::Increment {
            kind_id: char_record_kind::KILL_NPC,
            amount: 1,
            matcher: RecordMatch::Value1(100),
        };
        let report = report_input(&f.server, &f.player, kill, at(1_000));

        assert_eq!(report.records, [(10, 1)]);
        assert!(report.newly_completed());
        assert_eq!(report.refresh.completed_ids(), [1]);
        assert_eq!(
            report.rewards.granted,
            [GrantedReward {
                achievement_id: 1,
                by_mail: false
            }]
        );
        assert!(report.rewards.pending.is_empty());
        assert_eq!(
            achievement_ops(&mut f.client),
            [
                sc::SCAchievementChangedPacket,
                sc::SCAchievementCompletedPacket,
                sc::SCAchievementItemSentPacket,
            ]
        );

        // The item and the receipt: one default-grade item, nothing owed.
        let bag = bag_items(&f);
        assert_eq!(bag.len(), 1);
        assert_eq!(bag[0].template_id, REWARD_ITEM);
        {
            let c = f.player.data();
            assert!(!c.achievement_state.has_pending_reward(1));
            assert!(c
                .achievement_state
                .progress(1)
                .expect("progress row")
                .reward_delivered());
        }

        // Replaying the same event sends nothing and pays nothing. The kill counter itself keeps
        // counting (a later kill achievement may watch it), but the completion is one-way.
        let replay = report_input(&f.server, &f.player, kill, at(1_001));
        assert_eq!(replay.records, [(10, 2)]);
        assert_eq!(replay.refresh, RefreshOutcome::default());
        assert_eq!(replay.rewards, RewardDelivery::default());
        assert!(achievement_ops(&mut f.client).is_empty());
        assert_eq!(bag_items(&f).len(), 1);
        assert!(f
            .player
            .data()
            .achievement_state
            .pending_reward_ids()
            .is_empty());
    }

    #[tokio::test]
    async fn adjacent_records_batch_into_one_change_and_ordered_inputs_keep_their_order() {
        // Achievement 3 completes from Record 40, 5 from the two adjacent Records 10 + 20,
        // 1 from the increment and 4 cascades from 1's completion through the kind-9 record; 2
        // completes from the Record 30 that follows the increment.
        let data = Content::default()
            .achievement(1, 1, true, &[15])
            .achievement(2, 1, true, &[30])
            .achievement(3, 1, true, &[40])
            .completion(50, 1)
            .achievement(4, 1, true, &[50])
            .achievement(5, 2, false, &[10, 20])
            .record(10, char_record_kind::KILL_NPC, 0, 0)
            .record(20, char_record_kind::KILL_NPC, 0, 0)
            .record(15, char_record_kind::KILL_NPC, 100, 0)
            .record(30, char_record_kind::KILL_NPC, 200, 0)
            .record(40, char_record_kind::KILL_NPC, 300, 0)
            .build();
        let f = fixture(data);

        let report = report_inputs(
            &f.server,
            &f.player,
            [
                AchievementInput::Record {
                    record_id: 40,
                    value: 1,
                },
                AchievementInput::Record {
                    record_id: 10,
                    value: 1,
                },
                AchievementInput::Record {
                    record_id: 20,
                    value: 1,
                },
                AchievementInput::Increment {
                    kind_id: char_record_kind::KILL_NPC,
                    amount: 1,
                    matcher: RecordMatch::Value1(100),
                },
                AchievementInput::Record {
                    record_id: 30,
                    value: 1,
                },
            ],
            at(1_000),
        );

        // Order: the Record 40 batch, the Records 10 + 20 batch (one final change for 5), the
        // increment batch (1 completes, cascades into 4), then the Record 30 batch.
        assert_eq!(report.refresh.completed_ids(), [3, 5, 1, 4, 2]);
        assert_eq!(report.refresh.completed, 5);
        assert_eq!(
            report
                .refresh
                .events
                .iter()
                .map(|event| (
                    event.achievement_id(),
                    matches!(event, AchievementEvent::Completed { .. })
                ))
                .collect::<Vec<_>>(),
            [
                (3, false),
                (3, true),
                (5, false),
                (5, true),
                (1, false),
                (1, true),
                (4, false),
                (4, true),
                (2, false),
                (2, true),
            ]
        );
        // Achievement 5's two records were written before it was evaluated: one change to 2, not
        // one change per record. The increment only reached record 15. The records of one batch
        // are reported ascending, whatever order they were queued in.
        assert_eq!(
            report.records,
            [(10, 1), (20, 1), (40, 1), (15, 1), (30, 1)]
        );
        {
            let c = f.player.data();
            assert_eq!(c.achievement_state.amount(5), 2);
            assert_eq!(c.achievement_state.record_value(10), 1);
            assert_eq!(c.achievement_state.record_value(20), 1);
            assert_eq!(c.achievement_state.record_value(15), 1);
            for id in 1..=5 {
                assert!(c.achievement_state.is_complete(id), "achievement {id}");
            }
        }
    }

    // ---- current-state refresh ----

    #[tokio::test]
    async fn refresh_current_credits_the_state_units_and_pays_what_they_complete() {
        let data = Content::default()
            .achievement(1, 20, false, &[10])
            .record(10, char_record_kind::CHAR_LEVEL, 0, 0)
            .achievement(2, 1, false, &[14])
            .record(14, char_record_kind::MY_GOLD, 0, 0)
            .build();
        let mut f = fixture(data);
        {
            let mut c = f.player.data();
            c.unit.level = 20;
            // 1 gold and change: the record counts whole gold.
            c.money = 10_000 + 9_999;
        }

        let report = refresh_current(&f.server, &f.player, at(1_000));

        assert_eq!(report.records, [(10, 20), (14, 1)]);
        assert_eq!(report.refresh.completed_ids(), [1, 2]);
        assert_eq!(
            achievement_ops(&mut f.client),
            [
                sc::SCAchievementChangedPacket,
                sc::SCAchievementCompletedPacket,
                sc::SCAchievementChangedPacket,
                sc::SCAchievementCompletedPacket,
            ]
        );

        // A second pass is quiet: the current state did not move.
        assert_eq!(
            refresh_current(&f.server, &f.player, at(1_001)),
            AchievementReport::default()
        );
        assert!(achievement_ops(&mut f.client).is_empty());
    }

    // ---- the entry path ----

    #[tokio::test]
    async fn initial_snapshot_back_credits_and_pays_the_restored_pending_reward() {
        // Achievement 1 watches a state record the entry path credits; achievement 2 is restored
        // complete with an unpaid receipt, exactly like a relog after a failed hand-over.
        let data = Content::default()
            .achievement(1, 30, false, &[10])
            .reward(1, 0)
            .record(10, char_record_kind::CHAR_LEVEL, 0, 0)
            .achievement(2, 1, true, &[20])
            .reward(2, REWARD_ITEM)
            .record(20, char_record_kind::KILL_NPC, 500, 0)
            .build();
        let mut f = fixture(data);
        {
            let mut c = f.player.data();
            c.unit.level = 30;
            c.achievement_state
                .restore_progress_with_receipt(2, 1, Some(at(900)), false);
        }

        let entry = initial_snapshot(&f.server, &f.player, at(1_000));

        // The back-credit wrote the level and completed 1 (settled by its receipt alone); the
        // quiet pass sent only the list.
        assert_eq!(entry.snapshot.records, [(10, 30)]);
        assert_eq!(entry.snapshot.refresh.completed_ids(), [1]);
        assert_eq!(
            entry
                .snapshot
                .touched
                .iter()
                .map(|row| row.id)
                .collect::<Vec<_>>(),
            [1, 2]
        );
        assert_eq!(entry.snapshot.packets, 1);
        assert_eq!(entry.rewards.granted.len(), 1);
        assert_eq!(entry.rewards.granted[0].achievement_id, 2);
        assert!(entry.rewards.pending.is_empty());
        assert_eq!(
            achievement_ops(&mut f.client),
            [sc::SCAchievementsPacket, sc::SCAchievementItemSentPacket]
        );

        // Both receipts are written, the reward item is in the bag, and nothing is left owed.
        assert_eq!(bag_items(&f).len(), 1);
        {
            let c = f.player.data();
            assert!(c
                .achievement_state
                .progress(2)
                .expect("progress row")
                .reward_delivered());
            assert!(c.achievement_state.pending_reward_ids().is_empty());
        }

        // A second entry is quiet: the list rows are already touched and no reward is pending.
        let again = initial_snapshot(&f.server, &f.player, at(1_001));
        assert!(again.snapshot.records.is_empty());
        assert_eq!(again.snapshot.packets, 1);
        assert_eq!(again.rewards, RewardDelivery::default());
        assert_eq!(achievement_ops(&mut f.client), [sc::SCAchievementsPacket]);
        assert_eq!(bag_items(&f).len(), 1);
    }
}
