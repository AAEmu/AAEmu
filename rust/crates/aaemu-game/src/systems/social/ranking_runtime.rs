//! Ranking score state and board views: the per-character scores behind the verified ranking
//! packets, plus the pure builders that turn them into the wire views.
//!
//! This module owns three things and nothing else:
//!
//! * [`RankingState`] — one character's scores, stored on `Character::ranking_state`. A score is
//!   keyed by `(rank_id, period_start)` in unix seconds and carries its period end, the holder's
//!   identifiers and the two figures the wire reports, so earlier periods survive for the frozen
//!   standings and payouts that come later.
//! * [`RankingState::record`] — the only writer. It accepts one bounded [`RankEvent`] for one
//!   board and answers with a [`RecordOutcome`]; nothing else changes stored scores, and the
//!   builders below never do.
//! * [`build_standings`], [`build_personal_rows`] and [`build_snapshot`] — pure views over the
//!   scores. The global standings are built by passing every character's scores in, which keeps
//!   the ordering logic independent of wherever those states are kept (a manager, a future
//!   score store, the tests here).
//!
//! Deliberately not here: SQL/persistence, the request handlers, the hooks that feed arena
//! results and catches in and the reward payouts — those are `ranking_manager` and
//! `ranking_persistence`. Also unresolved: the scope figures of a snapshot (their raw meaning
//! is unknown, so the builder sends none rather than invent cutoffs or totals). The racing board
//! is never recorded or shown.
//!
//! # Periods and windows
//!
//! The authored tables separate two things and so does this module. A board's **period** is its
//! reset cycle ([`RankDefinition::period_at`], `reset_week` weeks, clipped to the authored
//! dates): that is the key a score is stored under, so a reset starts a fresh score while the
//! old one stays. A board's **window** ([`RankDefinition::window_at`]) is the same period
//! narrowed to the authored day/hour slot — the contest actually running. Recording requires the
//! window, so a fishing catch only counts during the contest slot, while the arena board (no day
//! and no hour bounds) is open for its whole period. Both views answer `None` for a board whose
//! authored lifetime has passed: the shipped fishing contests ended in 2014, so they have no
//! current records and are never reopened by a later clock.
//!
//! # Units
//!
//! Fishing measures are read from the `BigFish` item fields the economy already fills
//! (`fish_length` / `fish_weight`) and are labelled `cm` and `kg` by the client's own ranking UI:
//! `game/scriptsbin/x2ui/ranking/ranking.alb` tags the length tab `cm` and the weight tab `kg`,
//! and its direct getters take the wire figures as raw integers — no client-side scaling. The
//! table's `min_length`/`max_length` are whole (the roll is `Random.Next(min, max)`), while
//! `min_weight`/`max_weight` are interpolated into a fractional `f32`; a board's figures are
//! `i32`, so each catch contributes its measure **rounded to the nearest whole cm/kg**, and a
//! running total is a sum of whole units.

use std::cmp::Ordering;
use std::collections::BTreeMap;

use chrono::{DateTime, TimeDelta, Utc};

use super::ranking_data::{RankDefinition, RankKind, RankWindow};
use super::ranking_packets::{
    SCRankCharacterRow, SCRankSnapshotEntry, SCRankSnapshotPacket, MAX_SNAPSHOT_ENTRIES,
};

// ---------------------------------------------------------------------------------------------
// Inputs
// ---------------------------------------------------------------------------------------------

/// The identity a score is stored and reported under.
///
/// The row the client receives carries these, so the state keeps them instead of asking the
/// character database at view time.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RankHolder {
    pub account_id: u32,
    /// Account name, cut to the client's buffer by the row serializer.
    pub account: String,
    pub character_id: u32,
    pub world_id: u8,
}

/// One bounded scoring input.
///
/// The caller decides which board an event belongs to (there are two fishing boards with
/// different measures); [`RankingState::record`] refuses an event whose kind does not match the
/// board.
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum RankEvent {
    /// One arena result: `points` is the score the match awarded or deducted (the authored
    /// `game_rule_sets.rank_*` figures) and `wins` the wins it added.
    Arena { points: i32, wins: i32 },
    /// One landed trophy fish: the measures its `BigFish` item carries (whole cm / kg, see the
    /// module docs).
    Catch { length: f32, weight: f32 },
}

/// What [`RankingState::record`] did with an event.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RecordOutcome {
    /// The board's `rank_kind_id` is the racing board or a kind the table does not define.
    UnsupportedBoard,
    /// The authored start/end columns cannot form real dates.
    DisabledBoard,
    /// The board has no window at this moment: its authored lifetime has passed, it has not
    /// started, or the moment is outside its day/hour slot.
    ClosedBoard,
    /// The board does not run in the event's zone group.
    ZoneMismatch,
    /// The event kind does not belong to this board (a catch on the arena board, an arena result
    /// on a fishing board).
    EventMismatch,
    /// A fishing measure that is not finite and positive.
    InvalidMeasure,
    /// Accepted, but the stored figures did not move (a shorter catch, or an event worth
    /// nothing): the state is untouched, including `updated`.
    Unchanged,
    /// Accepted and the stored figures moved.
    Changed,
}

impl RecordOutcome {
    /// Whether the stored score changed — the only outcome that writes.
    pub fn changed(self) -> bool {
        matches!(self, Self::Changed)
    }

    /// Whether the event was refused.
    pub fn is_rejected(self) -> bool {
        !matches!(self, Self::Changed | Self::Unchanged)
    }
}

// ---------------------------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------------------------

/// One character's score on one board in one period.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RankScore {
    /// `ranks.id` of the board.
    pub rank_id: u32,
    /// The period's start, unix seconds (half of the state key).
    pub period_start: i64,
    /// The period's end, unix seconds.
    pub period_end: i64,
    pub account_id: u32,
    pub account: String,
    pub character_id: u32,
    pub world_id: u8,
    /// The board's primary figure: a running total (arena score, total weight) or a best value
    /// (longest catch).
    pub v1: i32,
    /// The secondary figure: the arena win count, `0` on the fishing boards.
    pub v2: i32,
    /// When `v1`/`v2` last moved, unix seconds. The standings tie-break reads it.
    pub updated: i64,
}

/// One character's ranking scores, every period kept.
///
/// Ordered by `(rank_id, period_start)`, so iterating is deterministic and the current period of
/// a board is found by a key lookup rather than a scan.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct RankingState {
    scores: BTreeMap<(u32, i64), RankScore>,
}

impl RankingState {
    /// Whether the character has no stored score at all.
    pub fn is_empty(&self) -> bool {
        self.scores.is_empty()
    }

    /// How many `(board, period)` scores are stored.
    pub fn len(&self) -> usize {
        self.scores.len()
    }

    /// Puts a stored score back (the load path of the ranking store). A score already held for
    /// the same `(rank_id, period_start)` is replaced.
    pub fn restore(&mut self, score: RankScore) {
        self.scores
            .insert((score.rank_id, score.period_start), score);
    }

    /// The stored score of one board in one period.
    pub fn score(&self, rank_id: u32, period_start: i64) -> Option<&RankScore> {
        self.scores.get(&(rank_id, period_start))
    }

    /// Every stored score, ordered by `(rank_id, period_start)`.
    pub fn scores(&self) -> impl Iterator<Item = &RankScore> {
        self.scores.values()
    }

    /// The stored scores of one board, earliest period first.
    pub fn scores_of(&self, rank_id: u32) -> impl Iterator<Item = &RankScore> {
        self.scores
            .range((rank_id, i64::MIN)..=(rank_id, i64::MAX))
            .map(|(_, score)| score)
    }

    /// The stored score of the latest period of one board, if any.
    pub fn latest(&self, rank_id: u32) -> Option<&RankScore> {
        self.scores
            .range((rank_id, i64::MIN)..=(rank_id, i64::MAX))
            .next_back()
            .map(|(_, score)| score)
    }

    /// The character's score in the board's *current* period.
    ///
    /// `None` while the board is closed (its authored contest is over or it is outside its slot)
    /// and while the character has no score in the running period — an older period is history
    /// and is not reported as the current standing.
    pub fn current_score(&self, board: &RankDefinition, now: DateTime<Utc>) -> Option<&RankScore> {
        if !board.is_open_at(now) {
            return None;
        }
        let period = board.period_at(now)?;
        self.score(board.id, period.start.timestamp())
    }

    /// Records one bounded event against `board`.
    ///
    /// The board must be one this server runs (never racing), have real authored dates, be inside
    /// its window at `now`, and run in `zone_group_id`; the event must belong to the board's kind
    /// and a catch must carry finite, positive measures. An accepted event that is worth nothing
    /// (a zero arena result, a catch that rounds to zero units) leaves the state untouched.
    pub fn record(
        &mut self,
        board: &RankDefinition,
        holder: &RankHolder,
        event: RankEvent,
        zone_group_id: u32,
        now: DateTime<Utc>,
    ) -> RecordOutcome {
        let Some(kind) = board.kind().filter(|kind| kind.is_supported()) else {
            return RecordOutcome::UnsupportedBoard;
        };
        if board.is_disabled() {
            return RecordOutcome::DisabledBoard;
        }
        if !board.is_open_at(now) {
            return RecordOutcome::ClosedBoard;
        }
        if zone_group_id != board.zone_group_id {
            return RecordOutcome::ZoneMismatch;
        }

        // The event's contribution: a delta for the summing boards, a candidate for the
        // best-value board. A catch is measured on both figures, so both must be real.
        let (add_v1, add_v2, keeps_max) = match (kind, event) {
            (RankKind::Arena, RankEvent::Arena { points, wins }) => (points, wins, false),
            (RankKind::FishingWeight, RankEvent::Catch { length, weight }) => {
                let (Some(_), Some(weight)) = (measure_score(length), measure_score(weight)) else {
                    return RecordOutcome::InvalidMeasure;
                };
                (weight, 0, false)
            }
            (RankKind::FishingLength, RankEvent::Catch { length, weight }) => {
                let (Some(length), Some(_)) = (measure_score(length), measure_score(weight)) else {
                    return RecordOutcome::InvalidMeasure;
                };
                (length, 0, true)
            }
            _ => return RecordOutcome::EventMismatch,
        };

        // Nothing to add and nothing to beat: a record must not create a zero row or move the
        // tie-break stamp.
        let contributes = if keeps_max {
            add_v1 != 0
        } else {
            add_v1 != 0 || add_v2 != 0
        };
        if !contributes {
            return RecordOutcome::Unchanged;
        }

        // `is_open_at` already proved a period exists; the period (not the slot) is the key, so a
        // reset starts a new score while the finished one stays for history.
        let Some(period) = board.period_at(now) else {
            return RecordOutcome::ClosedBoard;
        };
        let entry = self
            .scores
            .entry((board.id, period.start.timestamp()))
            .or_insert_with(|| RankScore {
                rank_id: board.id,
                period_start: period.start.timestamp(),
                period_end: period.end.timestamp(),
                account_id: holder.account_id,
                account: holder.account.clone(),
                character_id: holder.character_id,
                world_id: holder.world_id,
                v1: 0,
                v2: 0,
                updated: now.timestamp(),
            });

        let before = (entry.v1, entry.v2);
        if keeps_max {
            entry.v1 = entry.v1.max(add_v1);
        } else {
            entry.v1 = entry.v1.saturating_add(add_v1);
            entry.v2 = entry.v2.saturating_add(add_v2);
        }
        if (entry.v1, entry.v2) == before {
            return RecordOutcome::Unchanged;
        }

        entry.period_end = period.end.timestamp();
        entry.account_id = holder.account_id;
        entry.account.clone_from(&holder.account);
        entry.character_id = holder.character_id;
        entry.world_id = holder.world_id;
        entry.updated = now.timestamp();
        RecordOutcome::Changed
    }
}

/// The whole-unit score of a catch measure, or `None` when the measure is not a finite positive
/// number (see the module docs for the `cm` / `kg` units).
fn measure_score(measure: f32) -> Option<i32> {
    if !measure.is_finite() || measure <= 0.0 {
        return None;
    }
    // Rust's float-to-int cast saturates, so a huge measure tops out instead of wrapping.
    Some(measure.round() as i32)
}

// ---------------------------------------------------------------------------------------------
// Views
// ---------------------------------------------------------------------------------------------

/// One board's standings for one period, best first.
///
/// The order is the primary figure descending, the secondary descending, then the earliest
/// `updated`, then `character_id` — a total order, so equal scores always take the same places.
/// At most [`MAX_SNAPSHOT_ENTRIES`] lines are returned, numbered from 1.
pub fn build_standings<'a>(
    board: &RankDefinition,
    period_start: i64,
    scores: impl IntoIterator<Item = &'a RankScore>,
) -> Vec<SCRankSnapshotEntry> {
    ranked_scores(board, period_start, scores)
        .into_iter()
        .take(MAX_SNAPSHOT_ENTRIES)
        .enumerate()
        .map(|(place, score)| SCRankSnapshotEntry {
            character_id: score.character_id,
            rank_id: board.id,
            world_id: score.world_id,
            v1: score.v1,
            v2: score.v2,
            updated: unix_timestamp(score.updated),
            ranking: place as u32 + 1,
        })
        .collect()
}

/// The character's own rows, one per supported board in ascending `ranks.id` order.
///
/// A board the character has no score on — including the boards whose authored contest is over —
/// still gets a row of zeros: the client files a row per board key, and only a known board makes
/// it ask for that board's snapshot later. The racing board is never included.
pub fn build_personal_rows<'a>(
    boards: impl IntoIterator<Item = &'a RankDefinition>,
    state: &RankingState,
    holder: &RankHolder,
    now: DateTime<Utc>,
) -> Vec<SCRankCharacterRow> {
    boards
        .into_iter()
        .filter_map(|board| {
            let kind = board.kind().filter(|kind| kind.is_supported())?;
            let score = state.current_score(board, now);
            let (v1, v2, recorded) = match score {
                Some(score) => (score.v1, score.v2, unix_timestamp(score.updated)),
                None => (0, 0, 0),
            };
            Some(SCRankCharacterRow {
                account_id: holder.account_id,
                account: holder.account.clone(),
                character_id: holder.character_id,
                rank_id: board.id,
                world_id: holder.world_id,
                v1,
                v2,
                // The arena's score and the total weight are running totals, the longest catch is
                // a best value; only the arena sums a second figure.
                sum_v1: kind != RankKind::FishingLength,
                sum_v2: kind == RankKind::Arena,
                recorded,
            })
        })
        .collect()
}

/// One board's snapshot, or `None` for a board this server does not run (racing, unknown kind).
///
/// The standings are the board's *current* period. When the authored contest is over there is no
/// current period: the entries are empty (the frozen standings of a closed period are a later
/// task) while `reset_updated` still carries the last real period boundary. The scope list is
/// always empty — the raw meaning of a snapshot scope figure is unresolved, so none is invented.
pub fn build_snapshot<'a>(
    board: &RankDefinition,
    scores: impl IntoIterator<Item = &'a RankScore>,
    now: DateTime<Utc>,
) -> Option<SCRankSnapshotPacket> {
    let kind = board.kind()?;
    if !kind.is_supported() {
        return None;
    }
    let entries = match board.period_at(now) {
        Some(period) => build_standings(board, period.start.timestamp(), scores),
        None => Vec::new(),
    };
    Some(SCRankSnapshotPacket {
        rank_id: board.id,
        error: 0,
        // The client copies the board id into its cache and no getter reads it back, so the
        // board's own id is the honest value.
        board_id: i64::from(board.id),
        snapshot_updated: unix_timestamp(now.timestamp()),
        reset_updated: period_or_last(board, now)
            .map(|period| unix_timestamp(period.end.timestamp()))
            .unwrap_or(0),
        entries,
        scopes: Vec::new(),
    })
}

/// The board's scoring period at `moment`, or the last period its authored lifetime had once the
/// board has closed.
///
/// `None` when the board's dates are broken, when it has not started yet, and when a closed board
/// authored no end to fall back to.
pub fn period_or_last(board: &RankDefinition, moment: DateTime<Utc>) -> Option<RankWindow> {
    if let Some(current) = board.period_at(moment) {
        return Some(current);
    }
    let until = board.lifetime().ok()?.until?;
    if moment < until {
        return None;
    }
    // The moment just before the authored end lies in the board's final period.
    let last = until.checked_sub_signed(TimeDelta::seconds(1))?;
    board.period_at(last)
}

/// Every score of one board in one period in standings order (place 1 first), uncapped — the
/// payout reads the whole list, the snapshot caps it at [`MAX_SNAPSHOT_ENTRIES`].
pub fn ranked_scores<'a>(
    board: &RankDefinition,
    period_start: i64,
    scores: impl IntoIterator<Item = &'a RankScore>,
) -> Vec<&'a RankScore> {
    let mut ranked: Vec<&RankScore> = scores
        .into_iter()
        .filter(|score| score.rank_id == board.id && score.period_start == period_start)
        .collect();
    ranked.sort_by(|a, b| standings_order(a, b));
    ranked
}

/// The board order: primary descending, secondary descending, earliest update, then character id.
fn standings_order(a: &RankScore, b: &RankScore) -> Ordering {
    b.v1.cmp(&a.v1)
        .then_with(|| b.v2.cmp(&a.v2))
        .then_with(|| a.updated.cmp(&b.updated))
        .then_with(|| a.character_id.cmp(&b.character_id))
}

/// A unix timestamp as the wire's `u64`; a negative one (a pre-1970 authored date, `MIN_UTC`)
/// becomes `0` rather than wrapping around.
fn unix_timestamp(seconds: i64) -> u64 {
    u64::try_from(seconds).unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use chrono::NaiveDateTime;

    use super::*;
    use crate::systems::social::ranking_data::day_of_week;
    use crate::systems::social::ranking_packets::{
        CSRankSnapshotRequest, SCRankCharacterPacket, MAX_CHARACTER_RANK_ROWS,
    };

    fn ts(s: &str) -> DateTime<Utc> {
        NaiveDateTime::parse_from_str(s, "%Y-%m-%d %H:%M:%S")
            .unwrap()
            .and_utc()
    }

    /// A board with a synthetic authored start and no authored end (never closes), the way the
    /// arena row reads: no day and no hour bounds, so its window is its whole period.
    fn board(id: u32, kind: RankKind, zone_group_id: u32, reset_week: i32) -> RankDefinition {
        RankDefinition {
            id,
            rank_kind_id: kind.id(),
            zone_group_id,
            reset_week,
            day_of_week_id: day_of_week::NO_DAY,
            st_year: 2024,
            st_month: 1,
            st_day: 1,
            ..Default::default()
        }
    }

    /// The shipped fishing shape: Saturday 20:00-22:00, weekly, zone group 49, authored from
    /// 2013-11-04 until 2014-01-01 (`ranks` rows 2 and 3 of the 1.2 `compact.sqlite3`).
    fn fishing(id: u32, kind: RankKind) -> RankDefinition {
        RankDefinition {
            id,
            rank_kind_id: kind.id(),
            zone_group_id: 49,
            reset_week: 1,
            day_of_week_id: 7,
            start_time: 20,
            end_time: 22,
            st_year: 2013,
            st_month: 11,
            st_day: 4,
            st_hour: 8,
            ed_year: 2014,
            ed_month: 1,
            ed_day: 1,
            ..Default::default()
        }
    }

    fn holder(character_id: u32) -> RankHolder {
        RankHolder {
            account_id: 11,
            account: "Cosmic".into(),
            character_id,
            world_id: 1,
        }
    }

    fn score(character_id: u32, v1: i32, v2: i32, updated: i64) -> RankScore {
        RankScore {
            rank_id: 7,
            period_start: 1_000,
            period_end: 2_000,
            account_id: 1,
            account: "A".into(),
            character_id,
            world_id: 1,
            v1,
            v2,
            updated,
        }
    }

    #[test]
    fn arena_sums_points_and_wins() {
        let board = board(1, RankKind::Arena, 77, 4);
        let now = ts("2024-02-01 12:00:00");
        let mut state = RankingState::default();

        assert_eq!(
            state.record(
                &board,
                &holder(5),
                RankEvent::Arena { points: 4, wins: 1 },
                77,
                now
            ),
            RecordOutcome::Changed
        );
        assert_eq!(
            state.record(
                &board,
                &holder(5),
                RankEvent::Arena {
                    points: -1,
                    wins: 0
                },
                77,
                now
            ),
            RecordOutcome::Changed
        );
        let score = state.current_score(&board, now).unwrap();
        assert_eq!((score.v1, score.v2), (3, 1));
        assert_eq!(score.period_start, ts("2024-01-29 00:00:00").timestamp());
        assert_eq!(score.period_end, ts("2024-02-26 00:00:00").timestamp());
        assert_eq!(score.updated, now.timestamp());
        assert_eq!(score.character_id, 5);
        assert_eq!(score.account, "Cosmic");
    }

    #[test]
    fn arena_sums_saturate() {
        let board = board(1, RankKind::Arena, 77, 4);
        let now = ts("2024-02-01 12:00:00");
        let mut state = RankingState::default();
        for _ in 0..3 {
            state.record(
                &board,
                &holder(5),
                RankEvent::Arena {
                    points: i32::MAX,
                    wins: i32::MAX,
                },
                77,
                now,
            );
        }
        let score = state.current_score(&board, now).unwrap();
        assert_eq!((score.v1, score.v2), (i32::MAX, i32::MAX));
    }

    #[test]
    fn a_worthless_arena_result_does_not_write() {
        let board = board(1, RankKind::Arena, 77, 4);
        let now = ts("2024-02-01 12:00:00");
        let mut state = RankingState::default();
        assert_eq!(
            state.record(
                &board,
                &holder(5),
                RankEvent::Arena { points: 0, wins: 0 },
                77,
                now
            ),
            RecordOutcome::Unchanged
        );
        assert!(state.current_score(&board, now).is_none());
        assert!(state.is_empty());
    }

    #[test]
    fn catch_sums_whole_units_on_the_weight_board() {
        let board = fishing(3, RankKind::FishingWeight);
        // A Saturday inside the authored contest: 20:30 of the 20:00-22:00 slot.
        let now = ts("2013-11-09 20:30:00");
        let mut state = RankingState::default();
        assert_eq!(
            state.record(
                &board,
                &holder(5),
                RankEvent::Catch {
                    length: 300.0,
                    weight: 579.5
                },
                49,
                now
            ),
            RecordOutcome::Changed
        );
        assert_eq!(
            state.record(
                &board,
                &holder(5),
                RankEvent::Catch {
                    length: 350.0,
                    weight: 600.4
                },
                49,
                now
            ),
            RecordOutcome::Changed
        );
        let score = state.current_score(&board, now).unwrap();
        assert_eq!(score.v1, 580 + 600);
        assert_eq!(score.v2, 0);
    }

    #[test]
    fn catch_keeps_the_longest_on_the_length_board() {
        let board = fishing(2, RankKind::FishingLength);
        let now = ts("2013-11-09 20:30:00");
        let mut state = RankingState::default();
        state.record(
            &board,
            &holder(5),
            RankEvent::Catch {
                length: 286.0,
                weight: 500.0,
            },
            49,
            now,
        );
        assert_eq!(
            state.record(
                &board,
                &holder(5),
                RankEvent::Catch {
                    length: 358.4,
                    weight: 600.0
                },
                49,
                now
            ),
            RecordOutcome::Changed
        );
        let kept = state.current_score(&board, now).unwrap().clone();
        assert_eq!(kept.v1, 358);

        let later = ts("2013-11-09 21:00:00");
        assert_eq!(
            state.record(
                &board,
                &holder(5),
                RankEvent::Catch {
                    length: 300.0,
                    weight: 500.0
                },
                49,
                later
            ),
            RecordOutcome::Unchanged
        );
        assert_eq!(state.current_score(&board, later).unwrap(), &kept);
    }

    #[test]
    fn fishing_measures_must_be_finite_and_positive() {
        let board = fishing(3, RankKind::FishingWeight);
        let now = ts("2013-11-09 20:30:00");
        let mut state = RankingState::default();
        for (length, weight) in [
            (f32::NAN, 500.0),
            (300.0, f32::NAN),
            (0.0, 500.0),
            (300.0, 0.0),
            (-1.0, 500.0),
            (300.0, -1.0),
            (f32::INFINITY, 500.0),
            (300.0, f32::INFINITY),
        ] {
            assert_eq!(
                state.record(
                    &board,
                    &holder(5),
                    RankEvent::Catch { length, weight },
                    49,
                    now
                ),
                RecordOutcome::InvalidMeasure,
                "length {length} weight {weight}"
            );
        }
        assert!(state.is_empty());

        // A real catch that rounds to nothing is accepted but writes nothing.
        assert_eq!(
            state.record(
                &board,
                &holder(5),
                RankEvent::Catch {
                    length: 300.0,
                    weight: 0.4
                },
                49,
                now
            ),
            RecordOutcome::Unchanged
        );
        assert!(state.is_empty());
    }

    #[test]
    fn board_rules_reject_the_rest() {
        let arena = board(1, RankKind::Arena, 77, 4);
        let weight = fishing(3, RankKind::FishingWeight);
        // The arena's synthetic period is open in 2024, the shipped fishing window only inside
        // its authored 2013 contest, so each board is probed at a moment it is running.
        let now = ts("2024-02-01 12:00:00");
        let contest = ts("2013-11-09 20:30:00");
        let mut state = RankingState::default();
        let catch = RankEvent::Catch {
            length: 300.0,
            weight: 500.0,
        };

        // Racing and kinds the table does not define.
        let racing = board(4, RankKind::Racing, 49, 1);
        assert_eq!(
            state.record(&racing, &holder(5), catch, 49, now),
            RecordOutcome::UnsupportedBoard
        );
        let unknown = RankDefinition {
            rank_kind_id: 9,
            ..board(9, RankKind::Arena, 49, 1)
        };
        assert_eq!(
            state.record(&unknown, &holder(5), catch, 49, now),
            RecordOutcome::UnsupportedBoard
        );

        // Broken authored dates.
        let broken = RankDefinition {
            st_month: 13,
            ..board(5, RankKind::Arena, 77, 4)
        };
        assert_eq!(
            state.record(
                &broken,
                &holder(5),
                RankEvent::Arena { points: 4, wins: 1 },
                77,
                now
            ),
            RecordOutcome::DisabledBoard
        );

        // The zone group must be the board's.
        assert_eq!(
            state.record(
                &arena,
                &holder(5),
                RankEvent::Arena { points: 4, wins: 1 },
                49,
                now
            ),
            RecordOutcome::ZoneMismatch
        );

        // The event kind must belong to the board.
        assert_eq!(
            state.record(&arena, &holder(5), catch, 77, now),
            RecordOutcome::EventMismatch
        );
        assert_eq!(
            state.record(
                &weight,
                &holder(5),
                RankEvent::Arena { points: 4, wins: 1 },
                49,
                contest
            ),
            RecordOutcome::EventMismatch
        );
        assert!(state.is_empty());
    }

    #[test]
    fn a_closed_or_expired_board_takes_no_records() {
        // The shipped contests ended 2014-01-01; a 2026 clock must not reopen them.
        let weight = fishing(3, RankKind::FishingWeight);
        let mut state = RankingState::default();
        assert_eq!(
            state.record(
                &weight,
                &holder(5),
                RankEvent::Catch {
                    length: 300.0,
                    weight: 500.0
                },
                49,
                ts("2026-10-13 20:30:00")
            ),
            RecordOutcome::ClosedBoard
        );

        // Inside the authored month, but outside the Saturday 20:00-22:00 slot.
        assert_eq!(
            state.record(
                &weight,
                &holder(5),
                RankEvent::Catch {
                    length: 300.0,
                    weight: 500.0
                },
                49,
                ts("2013-11-06 20:30:00") // a Wednesday
            ),
            RecordOutcome::ClosedBoard
        );
        assert!(state.is_empty());
    }

    #[test]
    fn periods_reset_and_history_is_kept() {
        let board = board(1, RankKind::Arena, 77, 4);
        let first = ts("2024-02-01 12:00:00");
        let second = ts("2024-02-26 00:00:00"); // the next period's first instant
        let mut state = RankingState::default();
        state.record(
            &board,
            &holder(5),
            RankEvent::Arena {
                points: 40,
                wins: 10,
            },
            77,
            first,
        );
        assert_eq!(state.len(), 1);

        // The new period starts empty; the old one is still stored for history.
        assert!(state.current_score(&board, second).is_none());
        state.record(
            &board,
            &holder(5),
            RankEvent::Arena { points: 4, wins: 1 },
            77,
            second,
        );
        assert_eq!(state.len(), 2);
        assert_eq!(state.current_score(&board, second).unwrap().v1, 4);
        let history: Vec<&RankScore> = state.scores_of(1).collect();
        assert_eq!(history.len(), 2);
        assert_eq!(history[0].v1, 40);
        assert_eq!(
            history[0].period_start,
            ts("2024-01-29 00:00:00").timestamp()
        );
        assert_eq!(history[1].period_start, second.timestamp());
        assert_eq!(state.latest(1).unwrap().period_start, second.timestamp());
    }

    #[test]
    fn standings_order_is_total_and_stable() {
        let board = RankDefinition {
            id: 7,
            rank_kind_id: RankKind::Arena.id(),
            ..Default::default()
        };
        let scores = [
            score(4, 10, 5, 5),
            score(2, 10, 5, 5), // same figures and stamp as 4: lower id first
            score(3, 10, 5, 1), // earlier update wins the tie
            score(5, 10, 6, 9), // higher secondary first
            score(6, 11, 0, 9), // higher primary first
            score(7, 9, 99, 0), // lower primary last
        ];
        let standings = build_standings(&board, 1_000, &scores);
        let order: Vec<u32> = standings.iter().map(|e| e.character_id).collect();
        assert_eq!(order, vec![6, 5, 3, 2, 4, 7]);
        let places: Vec<u32> = standings.iter().map(|e| e.ranking).collect();
        assert_eq!(places, vec![1, 2, 3, 4, 5, 6]);
        assert_eq!(standings[0].rank_id, 7);
        assert_eq!(standings[0].updated, 9);
    }

    #[test]
    fn standings_filter_by_board_and_period_and_cap_at_100() {
        let board = RankDefinition {
            id: 7,
            rank_kind_id: RankKind::Arena.id(),
            ..Default::default()
        };
        let mut scores: Vec<RankScore> = (0..150)
            .map(|i| score(1_000 + i, 100 - i as i32, 0, i as i64))
            .collect();
        scores.push(RankScore {
            rank_id: 8,
            ..score(9, 999, 0, 0)
        });
        scores.push(RankScore {
            period_start: 2_000,
            ..score(8, 999, 0, 0)
        });
        let standings = build_standings(&board, 1_000, &scores);
        assert_eq!(standings.len(), MAX_SNAPSHOT_ENTRIES);
        assert_eq!(standings[0].character_id, 1_000);
        assert_eq!(standings[99].character_id, 1_099);
        assert_eq!(standings[99].ranking, 100);
    }

    #[test]
    fn personal_rows_cover_every_supported_board_and_never_race() {
        let boards = [
            board(1, RankKind::Arena, 77, 4),
            fishing(2, RankKind::FishingLength),
            fishing(3, RankKind::FishingWeight),
            board(4, RankKind::Racing, 49, 1),
        ];
        let now = ts("2026-10-13 12:00:00");
        let mut state = RankingState::default();
        state.record(
            &boards[0],
            &holder(5),
            RankEvent::Arena {
                points: 4660,
                wins: 7,
            },
            77,
            now,
        );

        let rows = build_personal_rows(&boards, &state, &holder(5), now);
        let ids: Vec<u32> = rows.iter().map(|row| row.rank_id).collect();
        assert_eq!(ids, vec![1, 2, 3]);
        assert!(ids.len() <= MAX_CHARACTER_RANK_ROWS);

        // The arena row reports the record; the expired fishing boards are zero rows the client
        // still files, so it can ask for their (empty) snapshots.
        assert_eq!((rows[0].v1, rows[0].v2), (4660, 7));
        assert!(rows[0].sum_v1 && rows[0].sum_v2);
        assert_eq!(rows[0].recorded, now.timestamp() as u64);
        for row in &rows[1..] {
            assert_eq!((row.v1, row.v2, row.recorded), (0, 0, 0));
        }
        // The sum flags follow the board's kind, not the row's place: only the arena sums both
        // figures, the longest catch is a best value, and the weight board sums one.
        let flags: Vec<(u32, bool, bool)> = rows
            .iter()
            .map(|row| (row.rank_id, row.sum_v1, row.sum_v2))
            .collect();
        assert_eq!(
            flags,
            vec![(1, true, true), (2, false, false), (3, true, false)]
        );
        for row in &rows {
            assert_eq!(row.character_id, 5);
            assert_eq!(row.account, "Cosmic");
            assert_eq!(row.world_id, 1);
            assert_eq!(row.account_id, 11);
        }
    }

    #[test]
    fn snapshot_builder_applies_the_current_period_and_sends_no_scopes() {
        let arena = board(1, RankKind::Arena, 77, 4);
        let now = ts("2024-02-01 12:00:00");
        let stale = RankScore {
            period_start: ts("2024-01-01 00:00:00").timestamp(),
            period_end: ts("2024-01-29 00:00:00").timestamp(),
            ..score(9, 999, 0, 0)
        };
        let current = [
            RankScore {
                rank_id: 1,
                period_start: ts("2024-01-29 00:00:00").timestamp(),
                period_end: ts("2024-02-26 00:00:00").timestamp(),
                ..score(5, 20, 3, now.timestamp() - 60)
            },
            RankScore {
                rank_id: 1,
                period_start: ts("2024-01-29 00:00:00").timestamp(),
                period_end: ts("2024-02-26 00:00:00").timestamp(),
                ..score(6, 40, 9, now.timestamp())
            },
        ];
        let packet = build_snapshot(&arena, current.iter().chain([&stale]), now).unwrap();
        assert_eq!(packet.rank_id, 1);
        assert_eq!(packet.error, 0);
        assert_eq!(packet.board_id, 1);
        assert_eq!(packet.snapshot_updated, now.timestamp() as u64);
        assert_eq!(
            packet.reset_updated,
            ts("2024-02-26 00:00:00").timestamp() as u64
        );
        assert!(packet.scopes.is_empty());
        let order: Vec<u32> = packet.entries.iter().map(|e| e.character_id).collect();
        assert_eq!(order, vec![6, 5]);
        assert_eq!(packet.entries[0].ranking, 1);
        assert_eq!(packet.entries[1].ranking, 2);

        // Racing is never served.
        assert!(build_snapshot(&board(4, RankKind::Racing, 49, 1), [], now).is_none());
    }

    #[test]
    fn closed_board_snapshot_has_no_current_entries_but_a_real_reset_date() {
        let weight = fishing(3, RankKind::FishingWeight);
        let now = ts("2026-10-13 12:00:00");
        let packet = build_snapshot(&weight, [], now).unwrap();
        assert!(packet.entries.is_empty());
        assert!(packet.scopes.is_empty());
        // The last authored period ended 2014-01-01, a real boundary, not a wrapped zero.
        assert_eq!(
            packet.reset_updated,
            ts("2014-01-01 00:00:00").timestamp() as u64
        );
        assert_eq!(packet.snapshot_updated, now.timestamp() as u64);
    }

    #[test]
    fn reset_dates_never_wrap_a_negative_timestamp() {
        // A board whose authored end is before the Unix epoch: the period end is negative and
        // must be reported as 0 rather than wrapping to a huge u64.
        let old = RankDefinition {
            id: 1,
            rank_kind_id: RankKind::Arena.id(),
            reset_week: 1,
            st_year: 1960,
            st_month: 1,
            st_day: 1,
            ed_year: 1965,
            ed_month: 1,
            ed_day: 1,
            ..Default::default()
        };
        let packet = build_snapshot(&old, [], ts("2026-01-01 00:00:00")).unwrap();
        assert_eq!(packet.reset_updated, 0);

        // A board with no reset at all (`reset_week` 0) is one long period; its far end is a
        // positive date, and never a wrapped value.
        let endless = RankDefinition {
            reset_week: 0,
            ..board(1, RankKind::Arena, 77, 0)
        };
        let packet = build_snapshot(&endless, [], ts("2026-01-01 00:00:00")).unwrap();
        assert!(packet.reset_updated > 0);
        assert_ne!(packet.reset_updated, u64::MAX);
    }

    #[test]
    fn views_do_not_mutate_the_scores() {
        let boards = [
            board(1, RankKind::Arena, 77, 4),
            fishing(2, RankKind::FishingLength),
            fishing(3, RankKind::FishingWeight),
        ];
        let now = ts("2024-02-01 12:00:00");
        let mut state = RankingState::default();
        state.record(
            &boards[0],
            &holder(5),
            RankEvent::Arena { points: 4, wins: 1 },
            77,
            now,
        );
        let before = state.clone();

        for _ in 0..2 {
            let _ = build_snapshot(&boards[0], state.scores(), now);
            let _ = build_personal_rows(&boards, &state, &holder(5), now);
            let _ = build_standings(
                &boards[0],
                ts("2024-01-29 00:00:00").timestamp(),
                state.scores(),
            );
            assert_eq!(state, before);
        }
    }

    #[test]
    fn personal_rows_packet_uses_the_verified_wire_shape() {
        let boards = [
            board(1, RankKind::Arena, 77, 4),
            fishing(2, RankKind::FishingLength),
            fishing(3, RankKind::FishingWeight),
        ];
        let now = ts("2026-10-13 12:00:00");
        let state = RankingState::default();
        let rows = build_personal_rows(&boards, &state, &holder(5), now);
        let packet = SCRankCharacterPacket {
            type_id: 0,
            error: 0,
            ack_time: now.timestamp() as u64,
            rows,
        };
        // 18-byte header + three zero rows: 4 + 2 + account + 4 + 4 + 1 + 4 + 1 + 4 + 1 + 8.
        let row_len = 4 + 2 + "Cosmic".len() + 4 + 4 + 1 + 4 + 1 + 4 + 1 + 8;
        let mut s = aaemu_commons::PacketStream::new();
        aaemu_commons::Marshal::write(&packet, &mut s);
        let bytes = s.into_bytes();
        assert_eq!(bytes.len(), 18 + 3 * row_len);
        assert_eq!(&bytes[14..18], &[3, 0, 0, 0]);
    }

    #[test]
    fn snapshot_request_round_trips_the_board_key() {
        // The client's request body is the board id, and the builder answers that board.
        let request = CSRankSnapshotRequest::decode(&[3, 0, 0, 0]).unwrap();
        let packet = build_snapshot(
            &fishing(3, RankKind::FishingWeight),
            [],
            ts("2026-01-01 00:00:00"),
        );
        assert_eq!(packet.unwrap().rank_id, request.rank_id);
    }
}
