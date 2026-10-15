//! The achievement manager: the surface between the per-character
//! [`AchievementState`] and the client, plus the entry-path back-credit from state the character
//! already carries.
//!
//! C# scope (later history, `7851f67cc`): the packet half of
//! `Core/Managers/AchievementManager` — `Report`, `ReportCharacterProgress`, `BuildList`,
//! `SendList`, `RefreshAll` — on top of `CharacterRecords` / `CharacterAchievements`. The 1.2
//! tree has the tables, the packets and (in [`super::achievement_runtime`]) the state and
//! evaluation, but no manager; this file is that manager and nothing else.
//!
//! What is deliberately absent (each is its own task): reward delivery and overflow mail, reset
//! and the GM `SetRecord`, the sub-category completion records 10.x adds, and the gameplay hooks
//! that report the event records (kills, crafts, gold spent, ...). Nothing here reports a record
//! whose value is not legible from the character's *current* state, and nothing sends a packet
//! for a record or an achievement that did not move.
//!
//! # Locking
//!
//! * Every mutating entry point ([`AchievementContent::report_values`],
//!   [`AchievementContent::increment_matching`], [`AchievementContent::initial_snapshot`]) holds
//!   the economy `ops_lock` around the state mutation, because
//!   `SaveManager::capture_financial_snapshot` clones characters under that same lock: a
//!   completion's record values and progress then belong to one snapshot boundary, which is what
//!   a future item reward has to share. The lock is released before any packet is sent.
//! * The character lock (`player.data()`) is released before any packet is sent; the events are
//!   the core's, and [`send_events`] replays them in order.
//! * **Callers must not hold the character lock, an economy `ItemTxn` lock, the economy
//!   `ops_lock`, or an item container lock when they call a mutating entry point**: `ops_lock` is
//!   not re-entrant, so a call from inside an item transaction deadlocks. Call these after the
//!   object / `ItemTxn` locks are dropped, and send the packets from there.
//! * Read-only surfaces ([`AchievementContent::send_list`]) take only the character lock, and
//!   only for as long as it takes to copy the rows out.

use std::collections::{BTreeMap, BTreeSet};

use chrono::{DateTime, Utc};

use super::achievement::{char_record_kind, AchievementGameData, AchievementInfo, CharRecords};
use super::achievement_runtime::{
    AchievementEvent, AchievementIndex, AchievementState, RefreshOutcome,
};
use super::packets::misc::{
    SCAchievementChangedPacket, SCAchievementCompletedPacket, SCAchievementsPacket,
};
use crate::models::character::Character;
use crate::server::GameServer;
use crate::world::PlayerRef;

/// Achievements per `SCAchievementsPacket`. The 1.2 client does not accept a longer list in one
/// packet, so a list is split into as many packets as it takes.
pub const MAX_ENTRIES_PER_PACKET: usize = 50;

/// Seconds in an hour. `Character::online_time` is stored in seconds; the `PLAY_TIME` records
/// count hours (the shipped table asks for 5 / 10 / 24 / 100 / 250 / ... hours).
pub const SECONDS_PER_HOUR: u32 = 3600;

/// Copper in a gold piece. `Character::money` is stored in copper; the `MY_GOLD` records count
/// gold (the shipped table asks for 100 / 1,000 / 10,000 / 100,000 gold).
pub const COPPER_PER_GOLD: i64 = 10_000;

/// The loaded achievement content together with its pre-built [`AchievementIndex`].
///
/// Build once per loaded [`AchievementGameData`] (an index is O(rows)) and share it; the index
/// borrows the data, so the two live together here rather than in a self-referential type.
#[derive(Debug)]
pub struct AchievementContent<'a> {
    data: &'a AchievementGameData,
    index: AchievementIndex<'a>,
}

impl<'a> AchievementContent<'a> {
    /// Builds the index over `data` (`AchievementGameData.PostLoad`).
    pub fn new(data: &'a AchievementGameData) -> Self {
        Self {
            data,
            index: AchievementIndex::new(data),
        }
    }

    /// The loaded tables the index was built from.
    pub fn data(&self) -> &'a AchievementGameData {
        self.data
    }

    /// Whether the content has a record of `kind_id` that `matches` accepts (lock-free: lets a
    /// producer skip an event nothing authored cares about).
    pub fn has_record(&self, kind_id: u32, matches: impl Fn(&CharRecords) -> bool) -> bool {
        self.data
            .char_records
            .values()
            .any(|record| record.kind_id == kind_id && matches(record))
    }

    /// The pre-built lookups.
    pub fn index(&self) -> &AchievementIndex<'a> {
        &self.index
    }

    /// Batch-reports known records and re-evaluates what watches them.
    ///
    /// `values` is `(record id, the character's current value)`; an id the content does not have
    /// is ignored rather than stored, so a caller cannot persist a record the loader never saw,
    /// and record id 0 is ignored. The store is monotonic
    /// ([`AchievementState::report_record_value`]): a value at or below what is already stored is
    /// not a record and does not re-evaluate anything, which is what keeps a later back-credit
    /// from lowering saved progress.
    ///
    /// All records are written before anything is evaluated, so one achievement watching two of
    /// them produces one change event for its final amount, not one per record. Events are sent
    /// in the order the core produced them, after both locks are dropped.
    pub fn report_values(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        now: DateTime<Utc>,
        values: impl IntoIterator<Item = (u32, i64)>,
    ) -> RecordReport {
        let (refresh, records) = {
            let _economy = server.systems.economy.ops_lock();
            let mut c = player.data();
            let mut moved = BTreeMap::new();
            let mut watchers = BTreeSet::new();

            for (record_id, value) in values {
                if record_id == 0 || self.index.record(record_id).is_none() {
                    continue;
                }
                if c.achievement_state.report_record_value(record_id, value) {
                    moved.insert(record_id, c.achievement_state.record_value(record_id));
                    watchers.extend(self.index.watching_record(record_id).iter().copied());
                }
            }

            let refresh = self.refresh_watchers(&mut c.achievement_state, watchers, now);
            (refresh, moved.into_iter().collect::<Vec<_>>())
        };

        send_events(player, &refresh);
        RecordReport { refresh, records }
    }

    /// Increments every record definition of `kind_id` that `matches` accepts, and re-evaluates
    /// what watches them.
    ///
    /// The predicate receives the whole [`CharRecords`] row and decides what a caller's event
    /// means: `value1` and `value2` are per-kind parameters the manager must not read for it (for
    /// `KILL_NPC`, for instance, `value1` is the npc and `value2` is a level band, so "counts
    /// towards the kill" is a `value1` match, not a `value2` comparison). Matched records are
    /// visited in ascending record id, so a batch is deterministic; each is raised by `amount`
    /// from the character's current value, and the monotonic store keeps whichever is higher.
    ///
    /// Nothing matched (or nothing moved) is a quiet, empty [`RecordReport`].
    pub fn increment_matching(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        now: DateTime<Utc>,
        kind_id: u32,
        amount: i64,
        matches: impl Fn(&CharRecords) -> bool,
    ) -> RecordReport {
        let (refresh, records) = {
            let _economy = server.systems.economy.ops_lock();
            let mut c = player.data();

            let mut ids: Vec<u32> = self
                .data
                .char_records
                .values()
                .filter(|record| record.kind_id == kind_id && matches(record))
                .map(|record| record.id)
                .collect();
            ids.sort_unstable();
            ids.dedup();

            let mut moved = BTreeMap::new();
            let mut watchers = BTreeSet::new();
            for record_id in ids {
                let current = c.achievement_state.record_value(record_id);
                let next = current.saturating_add(amount);
                if c.achievement_state.report_record_value(record_id, next) {
                    moved.insert(record_id, c.achievement_state.record_value(record_id));
                    watchers.extend(self.index.watching_record(record_id).iter().copied());
                }
            }

            let refresh = self.refresh_watchers(&mut c.achievement_state, watchers, now);
            (refresh, moved.into_iter().collect::<Vec<_>>())
        };

        send_events(player, &refresh);
        RecordReport { refresh, records }
    }

    /// `AchievementManager.SendList`: sends the character's list, one [`SCAchievementsPacket`]
    /// per [`MAX_ENTRIES_PER_PACKET`] rows, ascending by achievement id.
    ///
    /// Returns how many packets were sent. An **empty list sends nothing** (0 packets) — it is
    /// not an empty packet, and the client starts every achievement at nothing, so there is
    /// nothing to correct. `include_untouched` is the forced full-refresh variant; the entry path
    /// sends only the rows the character has touched.
    pub fn send_list(&self, player: &PlayerRef, include_untouched: bool) -> usize {
        let list = {
            let c = player.data();
            c.achievement_state
                .achievement_list(&self.index, include_untouched)
        };
        send_list_chunks(player, &list)
    }

    /// The entry path: back-credit everything the character's current state can prove, resolve
    /// what the records already say, and push the touched list.
    ///
    /// This is the only place back-credit runs, and it is deliberately quiet about progress: the
    /// records are written, [`AchievementState::refresh_all`] is run *without* sending anything
    /// (the C# `RefreshAll(sendPackets: false)`), and the client hears the result through the list
    /// — the amounts and completion times it carries — not through one change packet per record.
    /// A completion reached here therefore sends no [`SCAchievementCompletedPacket`] either; the
    /// list row carries its timestamp. Later, live reports use [`Self::report_values`] /
    /// [`Self::increment_matching`] and do send those packets.
    ///
    /// Returns what it did, and how many list packets went out: zero when the character has
    /// nothing touched and nothing to credit (an empty snapshot is silent).
    pub fn initial_snapshot(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        now: DateTime<Utc>,
    ) -> InitialSnapshot {
        let (records, refresh, touched) = {
            let _economy = server.systems.economy.ops_lock();
            let mut c = player.data();

            let mut moved = BTreeMap::new();
            for (record_id, value) in back_credit_values(server, &c, self.data) {
                if c.achievement_state.report_record_value(record_id, value) {
                    moved.insert(record_id, c.achievement_state.record_value(record_id));
                }
            }

            let refresh = c.achievement_state.refresh_all(&self.index, now);
            let touched = c.achievement_state.achievement_list(&self.index, false);
            (moved.into_iter().collect::<Vec<_>>(), refresh, touched)
        };

        let packets = send_list_chunks(player, &touched);
        InitialSnapshot {
            records,
            refresh,
            touched,
            packets,
        }
    }

    /// Runs the core over a batch of watchers, or nothing when no record moved (the core would
    /// walk an empty queue anyway; skipping keeps the "nothing moved is quiet" explicit).
    fn refresh_watchers(
        &self,
        state: &mut AchievementState,
        watchers: BTreeSet<u32>,
        now: DateTime<Utc>,
    ) -> RefreshOutcome {
        if watchers.is_empty() {
            return RefreshOutcome::default();
        }
        state.refresh_queue(&self.index, watchers, now)
    }
}

/// What one batched report did: the core's pass plus the records that moved.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct RecordReport {
    /// The core's evaluation pass, events in send order.
    pub refresh: RefreshOutcome,
    /// The records whose stored value moved, ascending by record id (the ids a save has to write).
    pub records: Vec<(u32, i64)>,
}

/// What [`AchievementContent::initial_snapshot`] did.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct InitialSnapshot {
    /// Records the back-credit stored, ascending by record id. No progress packet is sent for
    /// them; the list carries the amounts.
    pub records: Vec<(u32, i64)>,
    /// The core's full-refresh pass. Completions reached by the back-credit are in
    /// [`RefreshOutcome::events`] as [`AchievementEvent::Completed`]; no completion packet is sent
    /// for them either.
    pub refresh: RefreshOutcome,
    /// The rows sent, ascending by achievement id.
    pub touched: Vec<AchievementInfo>,
    /// [`SCAchievementsPacket`]s sent: `ceil(touched / 50)`, zero for an empty snapshot.
    pub packets: usize,
}

/// Sends one packet per event, in the order the core produced them and with the event's own
/// completion time (`SCAchievementChangedPacket` / `SCAchievementCompletedPacket`).
///
/// This is a send only: the caller must have released the character lock and the economy
/// `ops_lock` (see the module's locking notes). Returns the number of packets sent.
pub fn send_events(player: &PlayerRef, refresh: &RefreshOutcome) -> usize {
    for event in &refresh.events {
        match event {
            AchievementEvent::AmountChanged {
                achievement_id,
                amount,
            } => player.send(SCAchievementChangedPacket {
                id: *achievement_id,
                amount: *amount,
            }),
            AchievementEvent::Completed {
                achievement_id,
                completed_at,
            } => player.send(SCAchievementCompletedPacket {
                id: *achievement_id,
                complete: *completed_at,
            }),
        }
    }
    refresh.events.len()
}

/// Sends `list` in [`MAX_ENTRIES_PER_PACKET`]-row packets; zero rows send nothing. Returns the
/// packet count.
fn send_list_chunks(player: &PlayerRef, list: &[AchievementInfo]) -> usize {
    let mut packets = 0;
    for chunk in list.chunks(MAX_ENTRIES_PER_PACKET) {
        player.send(SCAchievementsPacket {
            achievements: chunk.to_vec(),
        });
        packets += 1;
    }
    packets
}

/// The records the character's **current** state can be credited for, ascending by record id.
///
/// Only state the character carries is read, and only record kinds whose units are legible from
/// it: the character's level, each ability's level through the shared experience table, actability
/// points, hours online, carried gold, the family / expedition / party / raid memberships the
/// character is in right now, the faction the character belongs to, and the completed-quest
/// bitset. Historical events (kills, crafts,
/// gold spent, items used, ...) are not reconstructible and are left alone; a kind with no
/// definition in the content simply produces nothing.
///
/// Every value is what the *record* means, not what the character table stores:
/// [`SECONDS_PER_HOUR`] and [`COPPER_PER_GOLD`] convert the two that differ, and a membership is
/// credited as 1 (the shipped rows are satisfied by a single point). Values at or below zero are
/// left out — there is nothing to credit.
///
/// The result feeds [`AchievementState::report_record_value`], which keeps the higher of the two,
/// so running this again (or after progress was restored from the database) can never lower a
/// record the character already has.
pub fn back_credit_values(
    server: &GameServer,
    character: &Character,
    data: &AchievementGameData,
) -> Vec<(u32, i64)> {
    // The party / raid distinction comes from the team itself: a raid converted from a party
    // keeps `in_party`, so the flag alone would call a raider a party member.
    let team = server
        .systems
        .social
        .teams
        .active_team_by_unit(character.id);
    let in_party = team
        .as_ref()
        .map_or(character.in_party, |team| team.is_party);
    let in_raid = team.as_ref().is_some_and(|team| !team.is_party);

    // The completed bitset is enumerated once: each set bit is one unique quest id, so a
    // repeatable quest counts once however often it was run (the table has no per-run count, and
    // inventing one would credit progress the character cannot back up).
    let quests_by_category = known_completed_quests_by_category(server, character);

    let mut values: BTreeMap<u32, i64> = BTreeMap::new();
    for record in data.char_records.values() {
        let value = match record.kind_id {
            char_record_kind::CHAR_LEVEL => i64::from(character.unit.level),
            char_record_kind::ABILITY_LEVEL => ability_level(server, character, record.value1),
            char_record_kind::GET_ACTABILITY => actability_points(character, record.value1),
            char_record_kind::PLAY_TIME => i64::from(character.online_time / SECONDS_PER_HOUR),
            char_record_kind::MY_GOLD => character.money.div_euclid(COPPER_PER_GOLD),
            char_record_kind::ENROLL_PARTY => i64::from(in_party),
            char_record_kind::ENROLL_RAID_GROUP => i64::from(in_raid),
            char_record_kind::ENROLL_FAMILY => i64::from(character.family != 0),
            char_record_kind::ENROLL_GUILD => i64::from(character.unit.expedition_id != 0),
            char_record_kind::GET_FACTION => {
                i64::from(character.unit.faction_id == record.value1 && record.value1 != 0)
            }
            char_record_kind::COMPLETE_QUEST_TYPE => {
                i64::from(crate::systems::quests::engine::has_quest_completed(
                    &character.quests.completed,
                    record.value1,
                ))
            }
            char_record_kind::COMPLETE_QUEST_CATEGORY => {
                quests_by_category.get(&record.value1).copied().unwrap_or(0)
            }
            _ => continue,
        };

        if value > 0 {
            values.insert(record.id, value);
        }
    }
    values.into_iter().collect()
}

/// Each ability's level (`ABILITY_LEVEL` records name the ability in `value1`; the two share the
/// numbering). Read through the shared experience table, exactly like the quest unit
/// requirements read it (`quests::engine::OwnerView::ability_level`); an ability row the
/// character does not have, or one the table cannot place, is worth nothing.
fn ability_level(server: &GameServer, character: &Character, ability: u32) -> i64 {
    let Ok(ability) = u8::try_from(ability) else {
        return 0;
    };
    let exp = character
        .abilities
        .abilities
        .iter()
        .find(|row| row.id == ability)
        .map(|row| row.exp);
    exp.map_or(0, |exp| {
        server
            .game_data
            .experience
            .level_from_exp_any(exp, false)
            .map_or(0, |(level, _)| i64::from(level))
    })
}

/// The character's points in one actability group.
fn actability_points(character: &Character, group: u32) -> i64 {
    character
        .actability
        .actabilities
        .iter()
        .find(|row| row.id == group)
        .map_or(0, |row| i64::from(row.point))
}

/// How many distinct, **known** completed quests the character has per
/// `quest_contexts.category_id`.
///
/// The store is a bitset (one bit per quest id), so this counts unique quests: a repeatable quest
/// contributes one, and a quest the loaded content does not have (a removed row, the skipped
/// tutorial category) has no category to be counted under and is left out.
fn known_completed_quests_by_category(
    server: &GameServer,
    character: &Character,
) -> BTreeMap<u32, i64> {
    let quests = &server.game_data.systems.quests;
    let mut totals: BTreeMap<u32, i64> = BTreeMap::new();
    for block in &character.quests.completed {
        let body = block.body8();
        for bit in 0..64u32 {
            if body[(bit / 8) as usize] & (1 << (bit % 8)) == 0 {
                continue;
            }
            let quest_id = u32::from(block.id) * 64 + bit;
            if let Some(template) = quests.template(quest_id) {
                *totals.entry(template.category_id).or_insert(0) += 1;
            }
        }
    }
    totals
}

#[cfg(test)]
mod tests {
    use super::super::achievement::{AchievementObjectives, Achievements};
    use super::*;
    use crate::data::experience::{ExperienceTable, LevelTemplate};
    use crate::data::GameData;
    use crate::models::char_data::Actability;
    use crate::opcodes::sc;
    use crate::systems::quests::engine::set_completed_quest_flag;
    use crate::systems::quests::templates::QuestTemplate;
    use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};

    /// A fixed instant, so the completion timestamps assertions read are deterministic.
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

        /// A `char_records` definition, with the per-kind `value1` / `value2` parameters.
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

        fn build(self) -> AchievementGameData {
            self.data
        }
    }

    /// A `levels` table with three levels, so an ability with 200 exp reads as level 3.
    fn experience() -> ExperienceTable {
        ExperienceTable::from_templates(vec![
            LevelTemplate {
                level: 1,
                total_exp: 0,
                total_mate_exp: 0,
                skill_points: 0,
            },
            LevelTemplate {
                level: 2,
                total_exp: 100,
                total_mate_exp: 50,
                skill_points: 1,
            },
            LevelTemplate {
                level: 3,
                total_exp: 200,
                total_mate_exp: 100,
                skill_points: 2,
            },
        ])
        .expect("valid table")
    }

    /// A server, its client (drained of the spawn frames) and a spawned level-10 character.
    struct Fixture {
        server: GameServer,
        client: TestClient,
        player: PlayerRef,
    }

    /// A server whose game data is `GameData::empty()` plus whatever `setup` puts in it.
    fn fixture_with(setup: impl FnOnce(&mut GameData)) -> Fixture {
        let mut data = GameData::empty();
        setup(&mut data);
        let server = test_server_with(data);
        let mut client = TestClient::new(&server, 1, 1);
        let player = spawn_player(
            &server,
            &client,
            test_character(1, "Tester", 0.0, 0.0),
            1000,
        );
        client.drain();
        Fixture {
            server,
            client,
            player,
        }
    }

    /// [`fixture_with`] with the given achievements as the only content.
    fn fixture_with_content(achievements: AchievementGameData) -> Fixture {
        fixture_with(|data| data.systems.social.achievements = achievements)
    }

    /// Borrows the content out of the server's game data. Takes the `GameServer` (not the whole
    /// fixture) so the borrow stays on that field and the test can still use its client.
    fn manager(server: &GameServer) -> AchievementContent<'_> {
        AchievementContent::new(&server.game_data.systems.social.achievements)
    }

    // ---- the entry path ----

    #[tokio::test]
    async fn initial_snapshot_credits_the_level_and_sends_only_the_list() {
        let data = Content::default()
            .achievement(1, 30, false, &[10])
            .record(10, char_record_kind::CHAR_LEVEL, 0, 0)
            .build();
        let mut f = fixture_with_content(data);
        let content = manager(&f.server);
        f.player.data().unit.level = 30;

        let snapshot = content.initial_snapshot(&f.server, &f.player, at(1_000));

        assert_eq!(snapshot.records, [(10, 30)]);
        assert_eq!(snapshot.refresh.completed_ids(), [1]);
        assert_eq!(
            snapshot
                .touched
                .iter()
                .map(|row| (row.id, row.amount))
                .collect::<Vec<_>>(),
            [(1, 30)]
        );
        assert_eq!(snapshot.packets, 1);
        // The list is the only packet: the entry path sends no change / completion packets.
        assert_eq!(f.client.drain_ops(), [sc::SCAchievementsPacket]);
        assert_eq!(
            f.player.data().achievement_state.completed_at(1),
            Some(at(1_000))
        );
    }

    #[tokio::test]
    async fn initial_snapshot_is_quiet_when_no_current_state_matches() {
        // A fresh character carries no gold, so the only record the content has is never reported.
        let data = Content::default()
            .achievement(1, 100, false, &[14])
            .record(14, char_record_kind::MY_GOLD, 0, 0)
            .build();
        let mut f = fixture_with_content(data);
        let content = manager(&f.server);

        let snapshot = content.initial_snapshot(&f.server, &f.player, at(1_000));

        assert!(snapshot.records.is_empty());
        assert!(snapshot.refresh.events.is_empty());
        assert!(snapshot.touched.is_empty());
        assert_eq!(snapshot.packets, 0);
        assert!(f.client.drain_ops().is_empty());
    }

    #[tokio::test]
    async fn initial_snapshot_cascades_a_completion_through_the_kind_nine_record() {
        // 1 completes from the level; 2 watches the record that counts 1's completion.
        let data = Content::default()
            .achievement(1, 30, false, &[10])
            .record(10, char_record_kind::CHAR_LEVEL, 0, 0)
            .achievement(2, 1, true, &[11])
            .record(11, char_record_kind::COMPLETE_ACHIEVEMENT, 1, 0)
            .build();
        let f = fixture_with_content(data);
        let content = manager(&f.server);
        f.player.data().unit.level = 30;

        let snapshot = content.initial_snapshot(&f.server, &f.player, at(1_000));

        assert_eq!(snapshot.refresh.completed_ids(), [1, 2]);
        assert_eq!(snapshot.refresh.completed, 2);
        assert_eq!(
            snapshot
                .touched
                .iter()
                .map(|row| row.id)
                .collect::<Vec<_>>(),
            [1, 2]
        );
        assert!(f.player.data().achievement_state.is_complete(1));
        assert!(f.player.data().achievement_state.is_complete(2));
        assert_eq!(f.player.data().achievement_state.record_value(11), 1);
    }

    // ---- back-credit units and sources ----

    #[tokio::test]
    async fn back_credit_converts_hours_gold_and_actability_points() {
        let data = Content::default()
            .achievement(1, 20, false, &[10])
            .record(10, char_record_kind::CHAR_LEVEL, 0, 0)
            .achievement(2, 10, false, &[12])
            .record(12, char_record_kind::PLAY_TIME, 0, 0)
            .achievement(3, 2, false, &[14])
            .record(14, char_record_kind::MY_GOLD, 0, 0)
            .achievement(4, 50_000, false, &[18])
            .record(18, char_record_kind::GET_ACTABILITY, 1, 0)
            .build();
        let f = fixture_with_content(data);
        let content = manager(&f.server);
        {
            let mut c = f.player.data();
            c.unit.level = 20;
            // 10 hours and 3,599 seconds: the record counts whole hours, not seconds.
            c.online_time = 10 * SECONDS_PER_HOUR + 3599;
            // 2 gold and change: the record counts whole gold, not copper.
            c.money = 2 * COPPER_PER_GOLD + 9_999;
            c.actability.actabilities.push(Actability {
                id: 1,
                point: 50_000,
                step: 0,
            });
        }

        let snapshot = content.initial_snapshot(&f.server, &f.player, at(1_000));

        assert_eq!(
            snapshot.records,
            [(10, 20), (12, 10), (14, 2), (18, 50_000)]
        );
        assert_eq!(snapshot.refresh.completed_ids(), [1, 2, 3, 4]);
        assert_eq!(snapshot.packets, 1);
    }

    #[tokio::test]
    async fn back_credit_reads_ability_levels_through_the_experience_table() {
        let data = Content::default()
            .achievement(1, 3, false, &[26])
            .record(26, char_record_kind::ABILITY_LEVEL, 3, 0)
            .build();
        let f = fixture_with(|game_data| {
            game_data.experience = experience();
            game_data.systems.social.achievements = data;
        });
        let content = manager(&f.server);
        f.player.data().abilities.get_mut(3).expect("ability 3").exp = 200;

        let snapshot = content.initial_snapshot(&f.server, &f.player, at(1_000));

        assert_eq!(snapshot.records, [(26, 3)]);
        assert_eq!(snapshot.refresh.completed_ids(), [1]);
    }

    #[tokio::test]
    async fn back_credit_credits_memberships_from_the_current_team() {
        let data = Content::default()
            .achievement(1, 1, false, &[52])
            .record(52, char_record_kind::ENROLL_PARTY, 0, 0)
            .achievement(2, 1, false, &[53])
            .record(53, char_record_kind::ENROLL_RAID_GROUP, 0, 0)
            .achievement(3, 1, false, &[54])
            .record(54, char_record_kind::ENROLL_FAMILY, 0, 0)
            .achievement(4, 0, true, &[55])
            .record(55, char_record_kind::ENROLL_GUILD, 0, 0)
            .build();
        let mut f = fixture_with_content(data);
        let content = manager(&f.server);

        // Alone: nothing is credited.
        let empty = content.initial_snapshot(&f.server, &f.player, at(1_000));
        assert!(empty.records.is_empty());

        {
            let mut c = f.player.data();
            c.family = 7;
            c.unit.expedition_id = 9;
        }
        f.server
            .systems
            .social
            .teams
            .create_solo_team(&f.server, &f.player, true);
        f.client.drain();

        let in_party = content.initial_snapshot(&f.server, &f.player, at(1_001));
        assert_eq!(in_party.records, [(52, 1), (54, 1), (55, 1)]);
        assert_eq!(in_party.refresh.completed_ids(), [1, 3, 4]);

        // A raid converted from the party is the raid record's, not the party's.
        let team = f
            .server
            .systems
            .social
            .teams
            .active_team_by_unit(f.player.character_id)
            .expect("team");
        f.server
            .systems
            .social
            .teams
            .convert_to_raid(&f.server, &f.player, team.id);
        f.client.drain();

        let in_raid = content.initial_snapshot(&f.server, &f.player, at(1_002));
        assert_eq!(in_raid.records, [(53, 1)]);
        assert_eq!(in_raid.refresh.completed_ids(), [2]);
    }

    #[tokio::test]
    async fn back_credit_counts_unique_completed_quests_by_category() {
        let data = Content::default()
            .achievement(1, 1, false, &[1500])
            .record(1500, char_record_kind::COMPLETE_QUEST_TYPE, 100, 0)
            .achievement(2, 1, false, &[1501])
            .record(1501, char_record_kind::COMPLETE_QUEST_TYPE, 999, 0)
            .achievement(3, 2, false, &[1510])
            .record(1510, char_record_kind::COMPLETE_QUEST_CATEGORY, 7, 0)
            .achievement(4, 1, false, &[1511])
            .record(1511, char_record_kind::COMPLETE_QUEST_CATEGORY, 8, 0)
            .build();
        let f = fixture_with(|game_data| {
            // Quest 100 is repeatable: its one bit still counts once, not once per run.
            game_data.systems.quests.templates.insert(
                100,
                QuestTemplate {
                    id: 100,
                    repeatable: true,
                    category_id: 7,
                    ..Default::default()
                },
            );
            game_data.systems.quests.templates.insert(
                101,
                QuestTemplate {
                    id: 101,
                    category_id: 7,
                    ..Default::default()
                },
            );
            game_data.systems.quests.templates.insert(
                65,
                QuestTemplate {
                    id: 65,
                    category_id: 8,
                    ..Default::default()
                },
            );
            game_data.systems.social.achievements = data;
        });
        let content = manager(&f.server);
        {
            let mut c = f.player.data();
            for quest_id in [65u32, 100, 101, 500] {
                set_completed_quest_flag(&mut c.quests.completed, quest_id, true);
            }
        }

        let snapshot = content.initial_snapshot(&f.server, &f.player, at(1_000));

        // 100 is completed (presence 1); 999 is not; category 7 has two known quests, category 8
        // one, and quest 500 has no template so it adds to no category.
        assert_eq!(snapshot.records, [(1500, 1), (1510, 2), (1511, 1)]);
        assert_eq!(snapshot.refresh.completed_ids(), [1, 3, 4]);
    }

    #[tokio::test]
    async fn back_credit_never_lowers_a_record_the_character_already_has() {
        let data = Content::default()
            .achievement(1, 10, false, &[10])
            .record(10, char_record_kind::CHAR_LEVEL, 0, 0)
            .build();
        let f = fixture_with_content(data);
        let content = manager(&f.server);
        {
            // Saved progress is ahead of the character's current level; the back-credit keeps it.
            let mut c = f.player.data();
            c.unit.level = 5;
            c.achievement_state.restore_record(10, 100);
        }

        let snapshot = content.initial_snapshot(&f.server, &f.player, at(1_000));

        assert!(snapshot.records.is_empty());
        assert_eq!(f.player.data().achievement_state.record_value(10), 100);
        assert_eq!(snapshot.refresh.completed_ids(), [1]);
    }

    // ---- live reports ----

    #[tokio::test]
    async fn report_values_batches_known_records_and_emits_events_in_order() {
        let data = Content::default()
            .achievement(1, 10, false, &[10])
            .achievement(2, 1, true, &[20])
            .record(10, char_record_kind::KILL_NPC, 100, 0)
            .record(20, char_record_kind::KILL_NPC, 200, 0)
            .build();
        let mut f = fixture_with_content(data);
        let content = manager(&f.server);

        // An unknown record id is ignored, not stored.
        let report = content.report_values(
            &f.server,
            &f.player,
            at(1_000),
            [(10, 10), (9999, 5), (20, 1)],
        );

        assert_eq!(report.records, [(10, 10), (20, 1)]);
        assert_eq!(
            report.refresh.events,
            [
                AchievementEvent::AmountChanged {
                    achievement_id: 1,
                    amount: 10
                },
                AchievementEvent::Completed {
                    achievement_id: 1,
                    completed_at: at(1_000)
                },
                AchievementEvent::AmountChanged {
                    achievement_id: 2,
                    amount: 1
                },
                AchievementEvent::Completed {
                    achievement_id: 2,
                    completed_at: at(1_000)
                },
            ]
        );
        assert_eq!(
            f.client.drain_ops(),
            [
                sc::SCAchievementChangedPacket,
                sc::SCAchievementCompletedPacket,
                sc::SCAchievementChangedPacket,
                sc::SCAchievementCompletedPacket,
            ]
        );
        assert_eq!(f.player.data().achievement_state.record_value(9999), 0);

        // A repeated report does not move anything and is silent (one-way completions).
        let again = content.report_values(&f.server, &f.player, at(1_001), [(10, 10), (20, 1)]);
        assert_eq!(again, RecordReport::default());
        assert!(f.client.drain_ops().is_empty());
        assert_eq!(
            f.player.data().achievement_state.completed_at(1),
            Some(at(1_000))
        );
    }

    #[tokio::test]
    async fn increment_matching_uses_the_callers_predicate_for_value1_not_value2() {
        let data = Content::default()
            .achievement(1, 2, false, &[5, 6])
            .achievement(2, 100, false, &[7])
            .record(5, char_record_kind::KILL_NPC, 100, 20)
            .record(6, char_record_kind::KILL_NPC, 100, 0)
            .record(7, char_record_kind::KILL_NPC, 200, 999)
            .build();
        let mut f = fixture_with_content(data);
        let content = manager(&f.server);

        // "An npc 100 was killed": the caller matches `value1`; `value2` is a level band and must
        // not be read as a count or a target by the manager.
        let report = content.increment_matching(
            &f.server,
            &f.player,
            at(1_000),
            char_record_kind::KILL_NPC,
            1,
            |record| record.value1 == 100,
        );

        assert_eq!(report.records, [(5, 1), (6, 1)]);
        assert_eq!(report.refresh.completed_ids(), [1]);
        assert_eq!(f.player.data().achievement_state.record_value(5), 1);
        assert_eq!(f.player.data().achievement_state.record_value(6), 1);
        assert_eq!(f.player.data().achievement_state.record_value(7), 0);

        // A second kill raises the two records to 2, but achievement 1 is already complete: the
        // records move and nothing is sent.
        f.client.drain();
        let second = content.increment_matching(
            &f.server,
            &f.player,
            at(1_001),
            char_record_kind::KILL_NPC,
            1,
            |record| record.value1 == 100,
        );
        assert_eq!(second.records, [(5, 2), (6, 2)]);
        assert!(second.refresh.events.is_empty());
        assert!(f.client.drain_ops().is_empty());

        // Nothing matching is a quiet, empty report.
        let none = content.increment_matching(
            &f.server,
            &f.player,
            at(1_002),
            char_record_kind::KILL_NPC,
            1,
            |record| record.value1 == 424_242,
        );
        assert_eq!(none, RecordReport::default());
    }

    // ---- the list ----

    #[tokio::test]
    async fn send_list_chunks_at_fifty_entries() {
        let mut builder = Content::default();
        for id in 1..=120u32 {
            builder = builder.achievement(id, 1, true, &[1000 + id]).record(
                1000 + id,
                char_record_kind::KILL_NPC,
                id,
                0,
            );
        }
        // One extra achievement the character never touches.
        builder = builder.achievement(121, 1, true, &[2000]).record(
            2000,
            char_record_kind::KILL_NPC,
            0,
            0,
        );
        let mut f = fixture_with_content(builder.build());
        let content = manager(&f.server);

        let values: Vec<(u32, i64)> = (1..=120).map(|id| (1000 + id, 1)).collect();
        content.report_values(&f.server, &f.player, at(1_000), values);
        f.client.drain();

        // 120 touched rows: three packets of 50 / 50 / 20, ascending by id.
        assert_eq!(content.send_list(&f.player, false), 3);
        let frames = f.client.drain();
        assert_eq!(
            frames
                .iter()
                .map(|(opcode, body)| (*opcode, i32::from_le_bytes(body[..4].try_into().unwrap())))
                .collect::<Vec<_>>(),
            [
                (sc::SCAchievementsPacket, 50),
                (sc::SCAchievementsPacket, 50),
                (sc::SCAchievementsPacket, 20),
            ]
        );

        // The full-refresh variant also carries the untouched row: 121 rows, 50 / 50 / 21.
        assert_eq!(content.send_list(&f.player, true), 3);
        assert_eq!(
            f.client
                .drain()
                .iter()
                .map(|(_, body)| i32::from_le_bytes(body[..4].try_into().unwrap()))
                .collect::<Vec<_>>(),
            [50, 50, 21]
        );
    }

    #[tokio::test]
    async fn an_empty_list_sends_nothing() {
        let data = Content::default()
            .achievement(1, 100, false, &[14])
            .record(14, char_record_kind::MY_GOLD, 0, 0)
            .build();
        let mut f = fixture_with_content(data);
        let content = manager(&f.server);

        assert_eq!(content.send_list(&f.player, false), 0);
        assert_eq!(content.send_list(&f.player, true), 1);
        let frames = f.client.drain();
        assert_eq!(frames.len(), 1);
        assert_eq!(
            (
                frames[0].0,
                i32::from_le_bytes(frames[0].1[..4].try_into().unwrap())
            ),
            (sc::SCAchievementsPacket, 1)
        );
    }
}
