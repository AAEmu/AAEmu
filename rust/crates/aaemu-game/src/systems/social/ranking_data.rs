//! `GameData/RankingGameData.cs` (read from the later C# history at `7851f67cc`) against the tables
//! the 1.2 `compact.sqlite3` actually ships: `ranks`, `rank_scopes`, `rank_rewards`,
//! `rank_scope_links` and `rank_reward_links` (4 / 40 / 40 / 40 / 40 rows). The 10.x shapes the
//! later C# loads (`rank_details`, `rank_resets`, `rank_tiers`, `gear_rank_details`,
//! `item_rank_details`, `game_point_rank_details`) do not exist in the 1.2 schema and are not
//! invented here.
//!
//! This is the static data only: every authored column of the five tables, each in ascending `id`
//! order, plus the link associations. It holds no scores, standings, payouts or SQL state.
//!
//! # Boards
//!
//! `ranks.rank_kind_id` names what a board measures ([`RankKind`]). The shipped table defines four
//! boards: the arena board (kind 1), two fishing boards (kinds 3 and 4) and a racing board
//! (kind 2). The racing board has no runtime behaviour on this server, so it is kept as an
//! authored definition that [`RankDefinition::is_supported`] rejects and
//! [`RankingGameData::supported_ranks`] leaves out.
//!
//! # Time (UTC)
//!
//! Every moment here is UTC, the convention of the C# (`DateTime.UtcNow`) and of the other 1.2
//! schedule loaders: the authored calendar fields (`st_*` / `ed_*`) and the day/hour windows are
//! compared against a UTC instant, and a window is the half-open interval `[start, end)` — a
//! moment exactly on the end is already outside it.
//!
//! `start_time` / `end_time` are whole hours, and an `end_time` below `start_time` runs into the
//! next day (the racing row's 8 → 4). `day_of_week_id` is the client's day index (Sunday = 1 …
//! Saturday = 7, so the fishing boards' 7 is Saturday, the evening the contest ran);
//! [`day_of_week::NO_DAY`] (8), or anything outside 1..=7, names no day, so the slot recurs every
//! day. A board that names neither a day nor a time window is in its slot for the whole scoring
//! period.
//!
//! The absolute lifetime is `st_*` .. `ed_*`, where an all-zero side means the table authors no
//! bound there. A side with a *positive* component that is not a real date/time disables the board
//! — every query then answers "closed" — instead of silently becoming an unbounded side. The
//! scoring period is anchored at the authored start and repeats every `reset_week` weeks.

use std::collections::HashMap;

use chrono::{DateTime, Datelike, NaiveDate, NaiveTime, TimeDelta, Utc, Weekday};

use crate::data::sqlite::for_each_row;
use crate::data::GameDataError;
use crate::systems::DataLoadCtx;

/// The values of `ranks.day_of_week_id`, the client's day index
/// (`Models.Game.Schedules.DayOfWeek` of the 1.2 C#): Sunday = 1 … Saturday = 7, and 8 for a
/// board that names no day at all.
pub mod day_of_week {
    pub const SUNDAY: i32 = 1;
    pub const MONDAY: i32 = 2;
    pub const TUESDAY: i32 = 3;
    pub const WEDNESDAY: i32 = 4;
    pub const THURSDAY: i32 = 5;
    pub const FRIDAY: i32 = 6;
    pub const SATURDAY: i32 = 7;
    /// Names no day: the board's slot recurs every day (the arena and racing rows).
    pub const NO_DAY: i32 = 8;

    /// Whether `id` names a weekday (`1..=7`); anything else is "no day" (`8`, `0`, …).
    pub fn is_weekday(id: i32) -> bool {
        (SUNDAY..=SATURDAY).contains(&id)
    }
}

/// `ranks.rank_kind_id`: what a board measures (the client's `enum_rank_kinds`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RankKind {
    /// 1 — the arena board: `v1` ("검투장 점수") is the arena score, `v2` ("검투장 승리 횟수")
    /// the win count. Supported.
    Arena,
    /// 2 — the racing board. This server has no runtime behaviour for it.
    Racing,
    /// 3 — the fishing board that ranks the total weight caught in its window: `v1`
    /// ("무게 합산"). Supported.
    FishingWeight,
    /// 4 — the fishing board that ranks the longest single catch of its window: `v1`
    /// ("최고 길이"). Supported.
    FishingLength,
}

impl RankKind {
    /// The kind a `rank_kind_id` names, or `None` for an id the table does not define.
    pub fn from_id(id: i32) -> Option<Self> {
        Some(match id {
            1 => Self::Arena,
            2 => Self::Racing,
            3 => Self::FishingWeight,
            4 => Self::FishingLength,
            _ => return None,
        })
    }

    /// The authored `rank_kind_id`.
    pub const fn id(self) -> i32 {
        match self {
            Self::Arena => 1,
            Self::Racing => 2,
            Self::FishingWeight => 3,
            Self::FishingLength => 4,
        }
    }

    /// Whether the server runs this board (everything but [`RankKind::Racing`]).
    pub const fn is_supported(self) -> bool {
        !matches!(self, Self::Racing)
    }
}

/// Which authored absolute date is impossible ([`RankDefinition::lifetime`]).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RankDateIssue {
    /// The `st_*` columns have a positive component that is not a real date/time.
    Start,
    /// The `ed_*` columns have a positive component that is not a real date/time.
    End,
}

impl RankDateIssue {
    /// The authored side the issue belongs to (`"start"` / `"end"`).
    pub fn side(self) -> &'static str {
        match self {
            Self::Start => "start",
            Self::End => "end",
        }
    }
}

/// The authored absolute lifetime of a board: `None` on a side means the table bounds nothing
/// there.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct RankLifetime {
    pub from: Option<DateTime<Utc>>,
    pub until: Option<DateTime<Utc>>,
}

/// A half-open UTC window `[start, end)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct RankWindow {
    pub start: DateTime<Utc>,
    pub end: DateTime<Utc>,
}

impl RankWindow {
    /// Whether `moment` lies inside the window (`start <= moment < end`).
    pub fn contains(&self, moment: DateTime<Utc>) -> bool {
        self.start <= moment && moment < self.end
    }

    /// The window's length.
    pub fn duration(&self) -> TimeDelta {
        self.end - self.start
    }
}

/// `ranks`: one ranking board with every authored column of the table.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct RankDefinition {
    pub id: u32,
    pub name: String,
    pub st_year: i32,
    pub st_month: i32,
    pub st_day: i32,
    pub st_hour: i32,
    pub st_min: i32,
    pub ed_year: i32,
    pub ed_month: i32,
    pub ed_day: i32,
    pub ed_hour: i32,
    pub ed_min: i32,
    /// [`day_of_week`]: the weekday the slot opens on, or [`day_of_week::NO_DAY`].
    pub day_of_week_id: i32,
    /// The hour the slot opens (`0..=23`).
    pub start_time: i32,
    /// The hour the slot closes; below `start_time` it runs into the next day.
    pub end_time: i32,
    pub zone_group_id: u32,
    /// The value kind of the board ([`RankKind`]).
    pub rank_kind_id: i32,
    /// The scoring period's length in weeks (`1` weekly, `4` four-weekly); `0` never starts over.
    pub reset_week: i32,
    /// The board's first value label (e.g. "검투장 점수" / "최고 길이").
    pub v1: String,
    /// The board's second value label ("" when the board measures one value).
    pub v2: String,
    pub start_time_alarm: i32,
    pub end_time_alarm: i32,
    pub start_time_alarm_msg: String,
    pub end_time_alarm_msg: String,
}

impl RankDefinition {
    /// The kind the board's `rank_kind_id` names.
    pub fn kind(&self) -> Option<RankKind> {
        RankKind::from_id(self.rank_kind_id)
    }

    /// Whether this server runs the board (see [`RankKind::is_supported`]).
    pub fn is_supported(&self) -> bool {
        self.kind().is_some_and(RankKind::is_supported)
    }

    /// The authored absolute lifetime (`st_*` .. `ed_*`), read as UTC.
    ///
    /// An all-zero side is "not authored" (`None`). A side with a positive component must be a
    /// real date/time (year > 0, month 1..=12, that day of the month, hour 0..=23, minute
    /// 0..=59); otherwise the board is disabled and `Err` names the side. A side that does not
    /// parse is never widened to "unbounded".
    pub fn lifetime(&self) -> Result<RankLifetime, RankDateIssue> {
        Ok(RankLifetime {
            from: authored_date(
                self.st_year,
                self.st_month,
                self.st_day,
                self.st_hour,
                self.st_min,
                RankDateIssue::Start,
            )?,
            until: authored_date(
                self.ed_year,
                self.ed_month,
                self.ed_day,
                self.ed_hour,
                self.ed_min,
                RankDateIssue::End,
            )?,
        })
    }

    /// Whether a positive authored date is impossible (every query then answers "closed").
    pub fn is_disabled(&self) -> bool {
        self.lifetime().is_err()
    }

    /// The `reset_week`-anchored scoring period that contains `moment` (UTC), clipped to the
    /// authored lifetime.
    ///
    /// The authored start is the first period's start and every following period starts
    /// `reset_week` weeks later (a board with no authored start anchors at the Unix epoch); with
    /// `reset_week` 0 the whole lifetime is one period. `None` when the board is disabled or
    /// `moment` lies outside the authored lifetime.
    pub fn period_at(&self, moment: DateTime<Utc>) -> Option<RankWindow> {
        let life = self.lifetime().ok()?;
        if life.from.is_some_and(|from| moment < from) {
            return None;
        }
        if life.until.is_some_and(|until| moment >= until) {
            return None;
        }

        let (mut start, mut end) = if self.reset_week > 0 {
            let length = TimeDelta::weeks(i64::from(self.reset_week));
            let seconds = length.num_seconds();
            let anchor = life.from.unwrap_or(DateTime::<Utc>::UNIX_EPOCH);
            let periods = (moment - anchor).num_seconds().div_euclid(seconds);
            let start = anchor.checked_add_signed(TimeDelta::seconds(periods * seconds))?;
            let end = start.checked_add_signed(length)?;
            (start, end)
        } else {
            (
                life.from.unwrap_or(DateTime::<Utc>::MIN_UTC),
                life.until.unwrap_or(DateTime::<Utc>::MAX_UTC),
            )
        };

        if let Some(from) = life.from {
            start = start.max(from);
        }
        if let Some(until) = life.until {
            end = end.min(until);
        }
        (start < end).then_some(RankWindow { start, end })
    }

    /// The window the board is running in at `moment` (UTC): the authored day/time slot that
    /// contains `moment`, clipped to the scoring period and the authored lifetime. `None` while
    /// the board is closed — outside its slot, after its authored end, or disabled.
    pub fn window_at(&self, moment: DateTime<Utc>) -> Option<RankWindow> {
        let period = self.period_at(moment)?;
        let slot = self.slot_at(moment)?;
        let start = slot.start.max(period.start);
        let end = slot.end.min(period.end);
        (start < end).then_some(RankWindow { start, end })
    }

    /// Whether the board is running at `moment` (UTC).
    pub fn is_open_at(&self, moment: DateTime<Utc>) -> bool {
        self.window_at(moment).is_some()
    }

    /// The authored recurring slot that contains `moment` (UTC): the weekly `day_of_week_id` slot
    /// when the board names a day, a daily one when it names none. A board with neither a day nor
    /// a time window is always in its slot (its window is then the period itself), which is how
    /// the arena row reads.
    fn slot_at(&self, moment: DateTime<Utc>) -> Option<RankWindow> {
        if !day_of_week::is_weekday(self.day_of_week_id) && self.start_time == self.end_time {
            return Some(RankWindow {
                start: DateTime::<Utc>::MIN_UTC,
                end: DateTime::<Utc>::MAX_UTC,
            });
        }

        let midnight = moment.date_naive().and_hms_opt(0, 0, 0)?.and_utc();
        let candidate = if day_of_week::is_weekday(self.day_of_week_id) {
            let wanted = weekday_from_id(self.day_of_week_id)?;
            let back = i64::from(moment.weekday().num_days_from_sunday())
                - i64::from(wanted.num_days_from_sunday());
            midnight.checked_sub_signed(TimeDelta::days(back.rem_euclid(7)))?
        } else {
            midnight
        };

        if let Some(slot) = self.slot_on(candidate) {
            if slot.contains(moment) {
                return Some(slot);
            }
        }
        // An end hour below the start hour runs past midnight, so an early-morning moment
        // belongs to the previous day's slot (the previous week's for a weekly board).
        if self.end_time < self.start_time {
            let previous = if day_of_week::is_weekday(self.day_of_week_id) {
                candidate.checked_sub_signed(TimeDelta::days(7))?
            } else {
                candidate.checked_sub_signed(TimeDelta::days(1))?
            };
            if let Some(slot) = self.slot_on(previous) {
                if slot.contains(moment) {
                    return Some(slot);
                }
            }
        }
        None
    }

    /// The slot of the day that starts at `day`: `start_time` .. `end_time`, where
    /// `start_time == end_time` is the whole 24 hours and `end_time < start_time` runs into the
    /// next day. Hours outside `0..=23` are not a slot the table can mean, so there is none.
    fn slot_on(&self, day: DateTime<Utc>) -> Option<RankWindow> {
        let start_hour = u32::try_from(self.start_time).ok().filter(|h| *h <= 23)?;
        let end_hour = u32::try_from(self.end_time).ok().filter(|h| *h <= 23)?;
        let start = day.checked_add_signed(TimeDelta::hours(i64::from(start_hour)))?;
        let hours = if start_hour == end_hour {
            24
        } else if end_hour > start_hour {
            i64::from(end_hour - start_hour)
        } else {
            i64::from(24 - start_hour + end_hour)
        };
        Some(RankWindow {
            start,
            end: start.checked_add_signed(TimeDelta::hours(hours))?,
        })
    }
}

/// A `Model.Game.Schedules.DayOfWeek` value as a [`Weekday`].
fn weekday_from_id(id: i32) -> Option<Weekday> {
    Some(match id {
        day_of_week::SUNDAY => Weekday::Sun,
        day_of_week::MONDAY => Weekday::Mon,
        day_of_week::TUESDAY => Weekday::Tue,
        day_of_week::WEDNESDAY => Weekday::Wed,
        day_of_week::THURSDAY => Weekday::Thu,
        day_of_week::FRIDAY => Weekday::Fri,
        day_of_week::SATURDAY => Weekday::Sat,
        _ => return None,
    })
}

/// One authored `st_*` / `ed_*` group as UTC: `Ok(None)` when every column is 0 (not authored),
/// `Err` when a positive component cannot form a real date/time.
fn authored_date(
    year: i32,
    month: i32,
    day: i32,
    hour: i32,
    minute: i32,
    issue: RankDateIssue,
) -> Result<Option<DateTime<Utc>>, RankDateIssue> {
    if year == 0 && month == 0 && day == 0 && hour == 0 && minute == 0 {
        return Ok(None);
    }
    if year <= 0
        || !(1..=12).contains(&month)
        || !(1..=31).contains(&day)
        || !(0..=23).contains(&hour)
        || !(0..=59).contains(&minute)
    {
        return Err(issue);
    }
    let date = NaiveDate::from_ymd_opt(year, month as u32, day as u32).ok_or(issue)?;
    let time = NaiveTime::from_hms_opt(hour as u32, minute as u32, 0).ok_or(issue)?;
    Ok(Some(date.and_time(time).and_utc()))
}

/// `rank_scopes`: one place range of a board (`scope_from` .. `scope_to`).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct RankScope {
    pub id: u32,
    pub name: String,
    /// The `COMMENT` column.
    pub comment: String,
    pub scope_from: i32,
    pub scope_to: i32,
}

/// `rank_rewards`: what a board's place range pays.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct RankReward {
    pub id: u32,
    pub name: String,
    /// The `COMMENT` column.
    pub comment: String,
    /// The appellation paid (0 when the table pays none).
    pub appellation_id: u32,
    pub item_id: u32,
    pub item_grade_id: i32,
    pub item_count: i32,
    /// How many weeks the reward lasts (4 on the arena rewards, 1 on the rest).
    pub weeks: i32,
}

/// `rank_scope_links`: a scope attached to a board. The id is the authored order of a board's
/// scopes.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct RankScopeLink {
    pub id: u32,
    pub rank_id: u32,
    pub rank_scope_id: u32,
}

/// `rank_reward_links`: a reward attached to a (board, scope) pair.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct RankRewardLink {
    pub id: u32,
    pub rank_id: u32,
    pub rank_scope_id: u32,
    pub rank_reward_id: u32,
}

/// `RankingGameData`: the five ranking tables, each in ascending `id` order, with the link
/// associations resolved for lookups.
#[derive(Debug, Default)]
pub struct RankingGameData {
    /// `ranks` rows, ascending `id`.
    pub ranks: Vec<RankDefinition>,
    /// `rank_scopes` rows, ascending `id`.
    pub scopes: Vec<RankScope>,
    /// `rank_rewards` rows, ascending `id`.
    pub rewards: Vec<RankReward>,
    /// `rank_scope_links` rows, ascending `id`.
    pub scope_links: Vec<RankScopeLink>,
    /// `rank_reward_links` rows, ascending `id`.
    pub reward_links: Vec<RankRewardLink>,
    /// `rank_id -> its scope ids`, in `rank_scope_links.id` order.
    scopes_by_rank: HashMap<u32, Vec<u32>>,
    /// `(rank_id, rank_scope_id) -> its reward ids`, in `rank_reward_links.id` order.
    rewards_by_rank_scope: HashMap<(u32, u32), Vec<u32>>,
}

impl RankingGameData {
    /// `RankingGameData.Load(connection)` over the 1.2 tables.
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, GameDataError> {
        let conn = ctx.conn;
        let mut d = RankingGameData::default();

        // Every table is read with `ORDER BY id`: the tables have no inherent row order, and
        // every lookup below is by id.
        for_each_row(conn, "SELECT * FROM ranks ORDER BY id", [], |r| {
            d.ranks.push(RankDefinition {
                id: r.u32("id")?,
                name: r.string_or("name", "")?,
                st_year: r.i32("st_year")?,
                st_month: r.i32("st_month")?,
                st_day: r.i32("st_day")?,
                st_hour: r.i32("st_hour")?,
                st_min: r.i32("st_min")?,
                ed_year: r.i32("ed_year")?,
                ed_month: r.i32("ed_month")?,
                ed_day: r.i32("ed_day")?,
                ed_hour: r.i32("ed_hour")?,
                ed_min: r.i32("ed_min")?,
                day_of_week_id: r.i32("day_of_week_id")?,
                start_time: r.i32("start_time")?,
                end_time: r.i32("end_time")?,
                zone_group_id: r.u32_or("zone_group_id", 0)?,
                rank_kind_id: r.i32("rank_kind_id")?,
                reset_week: r.i32("reset_week")?,
                v1: r.string_or("v1", "")?,
                v2: r.string_or("v2", "")?,
                start_time_alarm: r.i32("start_time_alarm")?,
                end_time_alarm: r.i32("end_time_alarm")?,
                start_time_alarm_msg: r.string_or("start_time_alarm_msg", "")?,
                end_time_alarm_msg: r.string_or("end_time_alarm_msg", "")?,
            });
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM rank_scopes ORDER BY id", [], |r| {
            d.scopes.push(RankScope {
                id: r.u32("id")?,
                name: r.string_or("name", "")?,
                comment: r.string_or("COMMENT", "")?,
                scope_from: r.i32_or("scope_from", 0)?,
                scope_to: r.i32_or("scope_to", 0)?,
            });
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM rank_rewards ORDER BY id", [], |r| {
            d.rewards.push(RankReward {
                id: r.u32("id")?,
                name: r.string_or("name", "")?,
                comment: r.string_or("COMMENT", "")?,
                appellation_id: r.u32_or("appellation_id", 0)?,
                item_id: r.u32_or("item_id", 0)?,
                item_grade_id: r.i32_or("item_grade_id", 0)?,
                item_count: r.i32_or("item_count", 0)?,
                weeks: r.i32_or("weeks", 0)?,
            });
            Ok(())
        })?;
        for_each_row(
            conn,
            "SELECT * FROM rank_scope_links ORDER BY id",
            [],
            |r| {
                d.scope_links.push(RankScopeLink {
                    id: r.u32("id")?,
                    rank_id: r.u32("rank_id")?,
                    rank_scope_id: r.u32("rank_scope_id")?,
                });
                Ok(())
            },
        )?;
        for_each_row(
            conn,
            "SELECT * FROM rank_reward_links ORDER BY id",
            [],
            |r| {
                d.reward_links.push(RankRewardLink {
                    id: r.u32("id")?,
                    rank_id: r.u32("rank_id")?,
                    rank_scope_id: r.u32("rank_scope_id")?,
                    rank_reward_id: r.u32("rank_reward_id")?,
                });
                Ok(())
            },
        )?;
        d.index_links();

        for rank in &d.ranks {
            if let Err(issue) = rank.lifetime() {
                tracing::warn!(
                    "Ranking board {} ({}) has an unusable authored {} date; it stays disabled",
                    rank.id,
                    rank.name,
                    issue.side()
                );
            }
            if !rank.is_supported() {
                tracing::warn!(
                    "Ranking board {} ({}) has kind {}, which this server does not run; it stays a \
                     raw definition",
                    rank.id,
                    rank.name,
                    rank.rank_kind_id
                );
            }
        }
        tracing::info!(
            "Loaded rankings ({} boards, {} scopes, {} rewards, {} scope links, {} reward links)",
            d.ranks.len(),
            d.scopes.len(),
            d.rewards.len(),
            d.scope_links.len(),
            d.reward_links.len()
        );
        Ok(d)
    }

    /// Resolves the link tables into `board -> scopes` and `(board, scope) -> rewards`, both in
    /// link `id` order. A link whose row is missing is reported and skipped by the lookups; it
    /// stays in the raw link tables.
    pub(crate) fn index_links(&mut self) {
        self.scopes_by_rank.clear();
        for i in 0..self.scope_links.len() {
            let link = self.scope_links[i];
            if self.scope(link.rank_scope_id).is_none() {
                tracing::warn!(
                    "rank_scope_links {}: scope {} does not exist",
                    link.id,
                    link.rank_scope_id
                );
            }
            self.scopes_by_rank
                .entry(link.rank_id)
                .or_default()
                .push(link.rank_scope_id);
        }
        self.rewards_by_rank_scope.clear();
        for i in 0..self.reward_links.len() {
            let link = self.reward_links[i];
            if self.scope(link.rank_scope_id).is_none() {
                tracing::warn!(
                    "rank_reward_links {}: scope {} does not exist",
                    link.id,
                    link.rank_scope_id
                );
            }
            if self.reward(link.rank_reward_id).is_none() {
                tracing::warn!(
                    "rank_reward_links {}: reward {} does not exist",
                    link.id,
                    link.rank_reward_id
                );
            }
            self.rewards_by_rank_scope
                .entry((link.rank_id, link.rank_scope_id))
                .or_default()
                .push(link.rank_reward_id);
        }
    }

    /// The board with the given `ranks.id`.
    pub fn rank(&self, id: u32) -> Option<&RankDefinition> {
        self.ranks
            .binary_search_by_key(&id, |rank| rank.id)
            .ok()
            .map(|i| &self.ranks[i])
    }

    /// The scope with the given `rank_scopes.id`.
    pub fn scope(&self, id: u32) -> Option<&RankScope> {
        self.scopes
            .binary_search_by_key(&id, |scope| scope.id)
            .ok()
            .map(|i| &self.scopes[i])
    }

    /// The reward with the given `rank_rewards.id`.
    pub fn reward(&self, id: u32) -> Option<&RankReward> {
        self.rewards
            .binary_search_by_key(&id, |reward| reward.id)
            .ok()
            .map(|i| &self.rewards[i])
    }

    /// The boards this server runs, ascending `id`: every board but the racing one.
    pub fn supported_ranks(&self) -> impl Iterator<Item = &RankDefinition> {
        self.ranks.iter().filter(|rank| rank.is_supported())
    }

    /// The scopes attached to a board (`rank_scope_links`), in link `id` order.
    pub fn scopes_of(&self, rank_id: u32) -> Vec<&RankScope> {
        self.scopes_by_rank
            .get(&rank_id)
            .into_iter()
            .flatten()
            .filter_map(|scope_id| self.scope(*scope_id))
            .collect()
    }

    /// The rewards attached to a (board, scope) pair (`rank_reward_links`), in link `id` order.
    pub fn rewards_of(&self, rank_id: u32, scope_id: u32) -> Vec<&RankReward> {
        self.rewards_by_rank_scope
            .get(&(rank_id, scope_id))
            .into_iter()
            .flatten()
            .filter_map(|reward_id| self.reward(*reward_id))
            .collect()
    }

    /// A board's scopes, each with the reward its first link names (the shipped table links one
    /// reward per scope), in the order [`Self::scopes_of`] gives.
    pub fn scope_rewards_of(&self, rank_id: u32) -> Vec<(&RankScope, Option<&RankReward>)> {
        self.scopes_of(rank_id)
            .into_iter()
            .map(|scope| {
                let reward = self.rewards_of(rank_id, scope.id).into_iter().next();
                (scope, reward)
            })
            .collect()
    }
}

#[cfg(test)]
mod tests {
    use chrono::NaiveDateTime;
    use rusqlite::Connection;

    use super::*;
    use crate::data::client_files::ClientFileManager;

    fn ts(s: &str) -> DateTime<Utc> {
        NaiveDateTime::parse_from_str(s, "%Y-%m-%d %H:%M:%S")
            .unwrap()
            .and_utc()
    }

    fn schema(c: &Connection) {
        c.execute_batch(
            "CREATE TABLE ranks (id INT PRIMARY KEY, name TEXT, st_year INT, st_month INT,
                st_day INT, st_hour INT, st_min INT, ed_year INT, ed_month INT, ed_day INT,
                ed_hour INT, ed_min INT, day_of_week_id INT, start_time INT, end_time INT,
                zone_group_id INT, rank_kind_id INT, reset_week INT, v1 TEXT, v2 TEXT,
                start_time_alarm INT, end_time_alarm INT, start_time_alarm_msg TEXT,
                end_time_alarm_msg TEXT);
             CREATE TABLE rank_scopes (id INT PRIMARY KEY, name TEXT, COMMENT TEXT,
                scope_from INT, scope_to INT);
             CREATE TABLE rank_rewards (id INT PRIMARY KEY, name TEXT, COMMENT TEXT,
                appellation_id INT, item_id INT, item_grade_id INT, item_count INT, weeks INT);
             CREATE TABLE rank_scope_links (id INT PRIMARY KEY, rank_id INT, rank_scope_id INT);
             CREATE TABLE rank_reward_links (id INT PRIMARY KEY, rank_id INT,
                rank_scope_id INT, rank_reward_id INT);",
        )
        .unwrap();
    }

    fn empty_tables() -> Connection {
        let c = Connection::open_in_memory().unwrap();
        schema(&c);
        c
    }

    fn insert_rank(c: &Connection, r: &RankDefinition) {
        c.execute(
            "INSERT INTO ranks (id, name, st_year, st_month, st_day, st_hour, st_min, ed_year,
                ed_month, ed_day, ed_hour, ed_min, day_of_week_id, start_time, end_time,
                zone_group_id, rank_kind_id, reset_week, v1, v2, start_time_alarm,
                end_time_alarm, start_time_alarm_msg, end_time_alarm_msg)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16,
                ?17, ?18, ?19, ?20, ?21, ?22, ?23, ?24)",
            rusqlite::params![
                r.id,
                r.name,
                r.st_year,
                r.st_month,
                r.st_day,
                r.st_hour,
                r.st_min,
                r.ed_year,
                r.ed_month,
                r.ed_day,
                r.ed_hour,
                r.ed_min,
                r.day_of_week_id,
                r.start_time,
                r.end_time,
                r.zone_group_id,
                r.rank_kind_id,
                r.reset_week,
                r.v1,
                r.v2,
                r.start_time_alarm,
                r.end_time_alarm,
                r.start_time_alarm_msg,
                r.end_time_alarm_msg,
            ],
        )
        .unwrap();
    }

    fn insert_scope(c: &Connection, s: &RankScope) {
        c.execute(
            "INSERT INTO rank_scopes (id, name, COMMENT, scope_from, scope_to)
             VALUES (?1, ?2, ?3, ?4, ?5)",
            rusqlite::params![s.id, s.name, s.comment, s.scope_from, s.scope_to],
        )
        .unwrap();
    }

    fn insert_reward(c: &Connection, r: &RankReward) {
        c.execute(
            "INSERT INTO rank_rewards (id, name, COMMENT, appellation_id, item_id, item_grade_id,
                item_count, weeks)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)",
            rusqlite::params![
                r.id,
                r.name,
                r.comment,
                r.appellation_id,
                r.item_id,
                r.item_grade_id,
                r.item_count,
                r.weeks,
            ],
        )
        .unwrap();
    }

    fn insert_scope_link(c: &Connection, l: RankScopeLink) {
        c.execute(
            "INSERT INTO rank_scope_links (id, rank_id, rank_scope_id) VALUES (?1, ?2, ?3)",
            rusqlite::params![l.id, l.rank_id, l.rank_scope_id],
        )
        .unwrap();
    }

    fn insert_reward_link(c: &Connection, l: RankRewardLink) {
        c.execute(
            "INSERT INTO rank_reward_links (id, rank_id, rank_scope_id, rank_reward_id)
             VALUES (?1, ?2, ?3, ?4)",
            rusqlite::params![l.id, l.rank_id, l.rank_scope_id, l.rank_reward_id],
        )
        .unwrap();
    }

    fn load(c: &Connection) -> RankingGameData {
        let files = ClientFileManager::default();
        let ctx = DataLoadCtx {
            conn: c,
            app_path: std::path::Path::new("."),
            files: &files,
            language: "en_us",
        };
        RankingGameData::load(&ctx).unwrap()
    }

    fn scope(id: u32, name: &str, comment: &str, from: i32, to: i32) -> RankScope {
        RankScope {
            id,
            name: name.to_string(),
            comment: comment.to_string(),
            scope_from: from,
            scope_to: to,
        }
    }

    fn reward(
        id: u32,
        name: &str,
        comment: &str,
        item_id: u32,
        item_grade_id: i32,
        item_count: i32,
        weeks: i32,
    ) -> RankReward {
        RankReward {
            id,
            name: name.to_string(),
            comment: comment.to_string(),
            appellation_id: 0,
            item_id,
            item_grade_id,
            item_count,
            weeks,
        }
    }

    /// The shipped board 1 (검투장 지배자): arena, four-week cycle, no end and no day/time window.
    fn arena() -> RankDefinition {
        RankDefinition {
            id: 1,
            name: "검투장 지배자".to_string(),
            st_year: 2014,
            st_month: 1,
            st_day: 6,
            st_hour: 8,
            st_min: 0,
            day_of_week_id: day_of_week::NO_DAY,
            start_time: 0,
            end_time: 0,
            zone_group_id: 77,
            rank_kind_id: RankKind::Arena.id(),
            reset_week: 4,
            v1: "검투장 점수".to_string(),
            v2: "검투장 승리 횟수".to_string(),
            ..RankDefinition::default()
        }
    }

    /// The shipped fishing boards 2 and 3: Saturday 20:00-22:00, weekly, ended 2014-01-01.
    fn fishing(id: u32, kind: RankKind, v1: &str) -> RankDefinition {
        RankDefinition {
            id,
            name: "월척 낚시꾼".to_string(),
            st_year: 2013,
            st_month: 11,
            st_day: 4,
            st_hour: 8,
            st_min: 0,
            ed_year: 2014,
            ed_month: 1,
            ed_day: 1,
            ed_hour: 0,
            ed_min: 0,
            day_of_week_id: day_of_week::SATURDAY,
            start_time: 20,
            end_time: 22,
            zone_group_id: 49,
            rank_kind_id: kind.id(),
            reset_week: 1,
            v1: v1.to_string(),
            start_time_alarm: -5,
            start_time_alarm_msg: "잠시후 신기루 낚시 대회가 시작됩니다.".to_string(),
            end_time_alarm_msg: "신기루 섬 낚시대회가 종료되었습니다.".to_string(),
            ..RankDefinition::default()
        }
    }

    /// The shipped board 4 (자동차 경주): racing, daily 08:00 → 04:00, weekly.
    fn racing() -> RankDefinition {
        RankDefinition {
            id: 4,
            name: "자동차 경주".to_string(),
            st_year: 2020,
            st_month: 1,
            st_day: 1,
            st_hour: 1,
            st_min: 1,
            day_of_week_id: day_of_week::NO_DAY,
            start_time: 8,
            end_time: 4,
            zone_group_id: 49,
            rank_kind_id: RankKind::Racing.id(),
            reset_week: 1,
            v1: "최고 기록".to_string(),
            ..RankDefinition::default()
        }
    }

    #[test]
    fn load_reads_the_five_tables_in_id_order() {
        let c = empty_tables();
        insert_rank(&c, &fishing(3, RankKind::FishingWeight, "무게 합산"));
        insert_rank(&c, &arena());
        insert_rank(&c, &fishing(2, RankKind::FishingLength, "최고 길이"));
        insert_scope(
            &c,
            &scope(20, "고급", "대어 낚시 무게 합산 3001 ~ 5000", 3001, 5000),
        );
        insert_scope(&c, &scope(1, "신화", "20등까지 상위권", 1, 3));
        insert_reward(
            &c,
            &reward(2, "전설", "전장 최상위권을 위한 보상", 32489, 10, 1, 4),
        );
        insert_reward(
            &c,
            &reward(1, "신화", "전장 최상위권을 위한 보상", 32488, 11, 1, 4),
        );
        insert_scope_link(
            &c,
            RankScopeLink {
                id: 2,
                rank_id: 1,
                rank_scope_id: 2,
            },
        );
        insert_scope_link(
            &c,
            RankScopeLink {
                id: 1,
                rank_id: 1,
                rank_scope_id: 1,
            },
        );
        insert_reward_link(
            &c,
            RankRewardLink {
                id: 35,
                rank_id: 1,
                rank_scope_id: 4,
                rank_reward_id: 24,
            },
        );
        insert_reward_link(
            &c,
            RankRewardLink {
                id: 2,
                rank_id: 1,
                rank_scope_id: 2,
                rank_reward_id: 2,
            },
        );

        let d = load(&c);

        // Every row is kept, ordered by id, although the inserts are out of order and the tables
        // have no inherent order of their own.
        assert_eq!(d.ranks.iter().map(|r| r.id).collect::<Vec<_>>(), [1, 2, 3]);
        assert_eq!(d.scopes.iter().map(|s| s.id).collect::<Vec<_>>(), [1, 20]);
        assert_eq!(d.rewards.iter().map(|r| r.id).collect::<Vec<_>>(), [1, 2]);
        assert_eq!(
            d.scope_links.iter().map(|l| l.id).collect::<Vec<_>>(),
            [1, 2]
        );
        assert_eq!(
            d.reward_links.iter().map(|l| l.id).collect::<Vec<_>>(),
            [2, 35]
        );

        // The authored columns survive the round trip.
        let a = d.rank(1).unwrap();
        assert_eq!(a.name, "검투장 지배자");
        assert_eq!(
            (a.st_year, a.st_month, a.st_day, a.st_hour, a.st_min),
            (2014, 1, 6, 8, 0)
        );
        assert_eq!(
            (a.ed_year, a.ed_month, a.ed_day, a.ed_hour, a.ed_min),
            (0, 0, 0, 0, 0)
        );
        assert_eq!(
            (a.day_of_week_id, a.start_time, a.end_time),
            (day_of_week::NO_DAY, 0, 0)
        );
        assert_eq!((a.zone_group_id, a.rank_kind_id, a.reset_week), (77, 1, 4));
        assert_eq!(
            (a.v1.as_str(), a.v2.as_str()),
            ("검투장 점수", "검투장 승리 횟수")
        );
        assert_eq!(d.rank(2).unwrap().name, "월척 낚시꾼");
        assert_eq!(d.rank(4), None);
    }

    #[test]
    fn load_links_scopes_and_rewards_to_their_boards() {
        let c = empty_tables();
        insert_rank(&c, &arena());
        insert_rank(&c, &fishing(2, RankKind::FishingLength, "최고 길이"));
        insert_scope(&c, &scope(1, "신화", "20등까지 상위권", 1, 3));
        insert_scope(&c, &scope(2, "전설", "그래도 최상위권", 4, 10));
        insert_scope(&c, &scope(11, "신화", "대어 낚시 최고 길이 1 ~ 10", 1, 10));
        insert_reward(
            &c,
            &reward(1, "신화", "전장 최상위권을 위한 보상", 32488, 11, 1, 4),
        );
        insert_reward(
            &c,
            &reward(4, "신화", "낚시 대회(최대 길이) 보상", 30219, 0, 1, 1),
        );
        for (id, rank_id, rank_scope_id) in [(1, 1, 1), (2, 1, 2), (11, 2, 11), (12, 2, 999)] {
            insert_scope_link(
                &c,
                RankScopeLink {
                    id,
                    rank_id,
                    rank_scope_id,
                },
            );
        }
        for (id, rank_id, rank_scope_id, rank_reward_id) in [
            (1, 1, 1, 1),
            (2, 1, 2, 4),
            (5, 2, 11, 1),
            (6, 2, 999, 1),
            (7, 2, 11, 999),
        ] {
            insert_reward_link(
                &c,
                RankRewardLink {
                    id,
                    rank_id,
                    rank_scope_id,
                    rank_reward_id,
                },
            );
        }

        let d = load(&c);

        // The links resolve to the authored rows, in link id order.
        assert_eq!(
            d.scopes_of(1).iter().map(|s| s.id).collect::<Vec<_>>(),
            [1, 2]
        );
        assert_eq!(
            d.scopes_of(2).iter().map(|s| s.id).collect::<Vec<_>>(),
            [11]
        );
        assert!(d.scopes_of(9).is_empty());
        assert_eq!(
            d.rewards_of(1, 1).iter().map(|r| r.id).collect::<Vec<_>>(),
            [1]
        );
        assert_eq!(
            d.rewards_of(1, 2).iter().map(|r| r.id).collect::<Vec<_>>(),
            [4]
        );
        assert_eq!(
            d.rewards_of(2, 11).iter().map(|r| r.id).collect::<Vec<_>>(),
            [1]
        );
        // A dangling link resolves to nothing on the missing side, but stays in the raw link
        // table: scope 999 has no row, and reward 999 has none either.
        assert_eq!(
            d.rewards_of(2, 999)
                .iter()
                .map(|r| r.id)
                .collect::<Vec<_>>(),
            [1]
        );
        assert_eq!(
            d.scope_rewards_of(2)
                .iter()
                .map(|(s, r)| (s.id, r.map(|r| r.id)))
                .collect::<Vec<_>>(),
            [(11, Some(1))]
        );
        assert_eq!(d.scope_links.len(), 4);
        assert_eq!(d.reward_links.len(), 5);
        assert_eq!(
            d.scope_rewards_of(1)
                .iter()
                .map(|(s, r)| (s.id, r.map(|r| r.id)))
                .collect::<Vec<_>>(),
            [(1, Some(1)), (2, Some(4))]
        );
        assert_eq!(d.rank(1).unwrap().id, 1);
        assert_eq!(d.scope(2).unwrap().name, "전설");
        assert_eq!(d.reward(4).unwrap().item_id, 30219);
        assert_eq!(d.scope(999), None);
        assert_eq!(d.reward(999), None);
    }

    #[test]
    fn textual_values_and_alarm_columns_round_trip() {
        let c = empty_tables();
        insert_rank(&c, &fishing(2, RankKind::FishingLength, "최고 길이"));
        insert_scope(&c, &scope(11, "신화", "대어 낚시 최고 길이 1 ~ 10", 1, 10));
        insert_reward(
            &c,
            &reward(4, "신화", "낚시 대회(최대 길이) 보상", 30219, 0, 1, 1),
        );
        // A NULL name / COMMENT / v1 is an empty string, not an error.
        c.execute_batch(
            "INSERT INTO rank_scopes (id, name, COMMENT, scope_from, scope_to)
                VALUES (12, NULL, NULL, NULL, NULL);
             INSERT INTO ranks (id, name, st_year, st_month, st_day, st_hour, st_min, ed_year,
                ed_month, ed_day, ed_hour, ed_min, day_of_week_id, start_time, end_time,
                zone_group_id, rank_kind_id, reset_week, v1, v2, start_time_alarm, end_time_alarm,
                start_time_alarm_msg, end_time_alarm_msg)
                VALUES (9, 'n', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 1, 0, NULL, NULL, 0, 0,
                    NULL, NULL);",
        )
        .unwrap();

        let d = load(&c);

        let f = d.rank(2).unwrap();
        assert_eq!(f.name, "월척 낚시꾼");
        assert_eq!((f.v1.as_str(), f.v2.as_str()), ("최고 길이", ""));
        assert_eq!((f.start_time_alarm, f.end_time_alarm), (-5, 0));
        assert_eq!(
            f.start_time_alarm_msg,
            "잠시후 신기루 낚시 대회가 시작됩니다."
        );
        assert_eq!(f.end_time_alarm_msg, "신기루 섬 낚시대회가 종료되었습니다.");
        let nulls = d.rank(9).unwrap();
        assert_eq!(
            (
                nulls.name.as_str(),
                nulls.v1.as_str(),
                nulls.v2.as_str(),
                nulls.start_time_alarm_msg.as_str(),
                nulls.end_time_alarm_msg.as_str()
            ),
            ("n", "", "", "", "")
        );

        let s = d.scope(11).unwrap();
        assert_eq!(
            (
                s.name.as_str(),
                s.comment.as_str(),
                s.scope_from,
                s.scope_to
            ),
            ("신화", "대어 낚시 최고 길이 1 ~ 10", 1, 10)
        );
        let empty = d.scope(12).unwrap();
        assert_eq!(
            (
                empty.name.as_str(),
                empty.comment.as_str(),
                empty.scope_from,
                empty.scope_to
            ),
            ("", "", 0, 0)
        );

        let r = d.reward(4).unwrap();
        assert_eq!(
            (
                r.name.as_str(),
                r.comment.as_str(),
                r.item_id,
                r.item_grade_id,
                r.item_count,
                r.weeks
            ),
            ("신화", "낚시 대회(최대 길이) 보상", 30219, 0, 1, 1)
        );
        // The NULL `appellation_id` the shipped rows carry reads as 0.
        assert_eq!(r.appellation_id, 0);
    }

    #[test]
    fn arena_board_stays_open_and_anchors_every_four_weeks() {
        let a = arena();
        assert_eq!(a.kind(), Some(RankKind::Arena));
        assert!(a.is_supported());
        assert!(!a.is_disabled());
        assert_eq!(
            a.lifetime().unwrap(),
            RankLifetime {
                from: Some(ts("2014-01-06 08:00:00")),
                until: None,
            }
        );

        // The authored start is the first period's start; the board has no end and no day/time
        // window, so it is open for the whole period.
        let first = a.period_at(ts("2014-01-06 08:00:00")).unwrap();
        assert_eq!(first.start, ts("2014-01-06 08:00:00"));
        assert_eq!(first.end, ts("2014-02-03 08:00:00"));
        assert_eq!(first.duration(), TimeDelta::weeks(4));
        assert_eq!(a.window_at(ts("2014-01-06 08:00:00")), Some(first));
        // Strict start boundary: one second earlier the board has not opened.
        assert_eq!(a.period_at(ts("2014-01-06 07:59:59")), None);
        assert!(!a.is_open_at(ts("2014-01-06 07:59:59")));

        // The next period starts exactly four weeks after the first, and the period in between
        // still belongs to the first.
        assert_eq!(a.period_at(ts("2014-02-03 07:59:59")).unwrap(), first);
        let second = a.period_at(ts("2014-02-03 08:00:00")).unwrap();
        assert_eq!(second.start, ts("2014-02-03 08:00:00"));
        assert_eq!(second.end, ts("2014-03-03 08:00:00"));
        assert_ne!(first, second);

        // It is still open today, in the period anchored at start + 4 weeks * k.
        let now = ts("2026-06-10 12:00:00");
        let period = a.period_at(now).unwrap();
        assert!(period.contains(now));
        assert_eq!(period.duration(), TimeDelta::weeks(4));
        assert_eq!((period.start - first.start).num_days() % 28, 0);
        assert_eq!(a.window_at(now), Some(period));
        assert!(a.is_open_at(now));
        assert_eq!(a.period_at(period.start).unwrap(), period);
        // The period that ends when this one opens is the previous one.
        assert_eq!(
            a.period_at(period.start - TimeDelta::seconds(1))
                .unwrap()
                .end,
            period.start
        );
    }

    #[test]
    fn fishing_boards_are_expired_and_do_not_reactivate() {
        let f = fishing(2, RankKind::FishingLength, "최고 길이");
        assert_eq!(f.kind(), Some(RankKind::FishingLength));
        assert!(f.is_supported());
        assert!(!f.is_disabled());
        assert_eq!(
            f.lifetime().unwrap(),
            RankLifetime {
                from: Some(ts("2013-11-04 08:00:00")),
                until: Some(ts("2014-01-01 00:00:00")),
            }
        );

        // The last contest inside the board's lifetime: Saturday 2013-12-28, 20:00-22:00.
        let last = f.window_at(ts("2013-12-28 20:30:00")).unwrap();
        assert_eq!(
            (last.start, last.end),
            (ts("2013-12-28 20:00:00"), ts("2013-12-28 22:00:00"))
        );
        assert!(f.is_open_at(ts("2013-12-28 20:30:00")));

        // Expired since its authored end: neither the end itself nor any later Saturday opens it
        // again.
        for now in [
            "2014-01-01 00:00:00",
            "2014-01-04 20:30:00",
            "2026-06-13 20:30:00",
        ] {
            assert_eq!(f.period_at(ts(now)), None, "{now}");
            assert_eq!(f.window_at(ts(now)), None, "{now}");
            assert!(!f.is_open_at(ts(now)), "{now}");
        }
    }

    #[test]
    fn weekly_slot_boundaries_are_half_open() {
        let f = fishing(3, RankKind::FishingWeight, "무게 합산");

        // Saturday 2013-11-09 is the first contest after the authored start.
        assert!(!f.is_open_at(ts("2013-11-09 19:59:59")));
        assert!(f.is_open_at(ts("2013-11-09 20:00:00")));
        assert!(f.is_open_at(ts("2013-11-09 21:59:59")));
        // The end is exclusive.
        assert!(!f.is_open_at(ts("2013-11-09 22:00:00")));
        assert_eq!(f.window_at(ts("2013-11-09 22:00:00")), None);
        // The slot is weekly: other days, and the Fridays/Sundays around it, are closed.
        assert!(!f.is_open_at(ts("2013-11-08 20:30:00")));
        assert!(!f.is_open_at(ts("2013-11-10 20:30:00")));
        // The next Saturday is in the next weekly period and opens again.
        assert!(f.is_open_at(ts("2013-11-16 20:30:00")));

        // The scoring period is one week, anchored at the authored start (Monday 08:00), and the
        // period boundary crosses the week exactly there.
        let p = f.period_at(ts("2013-11-16 20:30:00")).unwrap();
        assert_eq!(
            (p.start, p.end),
            (ts("2013-11-11 08:00:00"), ts("2013-11-18 08:00:00"))
        );
        assert_eq!(
            f.period_at(ts("2013-11-11 07:59:59")).unwrap().start,
            ts("2013-11-04 08:00:00")
        );
        assert_eq!(
            f.period_at(ts("2013-11-11 08:00:00")).unwrap().start,
            ts("2013-11-11 08:00:00")
        );
        // Before the authored start the board is closed as well.
        assert_eq!(f.period_at(ts("2013-11-04 07:59:59")), None);
    }

    #[test]
    fn slots_cross_midnight_into_the_next_day() {
        // The racing row authors 08:00 → 04:00, which runs into the next day. It stays a raw
        // definition: this server does not run the racing board.
        let racing = racing();
        assert_eq!(racing.kind(), Some(RankKind::Racing));
        assert!(!racing.is_supported());
        assert!(racing.is_open_at(ts("2020-01-01 08:00:00")));
        assert!(!racing.is_open_at(ts("2020-01-01 07:59:59")));
        let overnight = racing.window_at(ts("2020-01-02 01:00:00")).unwrap();
        assert_eq!(
            (overnight.start, overnight.end),
            (ts("2020-01-01 08:00:00"), ts("2020-01-02 04:00:00"))
        );
        assert!(!racing.is_open_at(ts("2020-01-02 04:00:00")));

        // A weekly board whose slot runs Saturday 22:00 → Sunday 02:00: Sunday's early hours
        // belong to Saturday's slot, still inside the Monday-anchored weekly period.
        let mut weekly = fishing(9, RankKind::FishingLength, "최고 길이");
        weekly.start_time = 22;
        weekly.end_time = 2;
        assert!(!weekly.is_open_at(ts("2013-11-09 21:59:59")));
        let w = weekly.window_at(ts("2013-11-10 01:00:00")).unwrap();
        assert_eq!(
            (w.start, w.end),
            (ts("2013-11-09 22:00:00"), ts("2013-11-10 02:00:00"))
        );
        assert_eq!(
            weekly.period_at(ts("2013-11-10 01:00:00")).unwrap().start,
            ts("2013-11-04 08:00:00")
        );
        assert!(!weekly.is_open_at(ts("2013-11-10 02:00:00")));
        // The following Sunday morning is the next Saturday's slot.
        let next = weekly.window_at(ts("2013-11-17 01:00:00")).unwrap();
        assert_eq!(
            (next.start, next.end),
            (ts("2013-11-16 22:00:00"), ts("2013-11-17 02:00:00"))
        );
    }

    #[test]
    fn lifetime_boundaries_are_half_open() {
        // A board with no day/time window that ends: every query is strict at both ends.
        let mut b = arena();
        b.id = 9;
        b.ed_year = 2014;
        b.ed_month = 1;
        b.ed_day = 7;
        b.ed_hour = 8;
        b.ed_min = 0;
        b.reset_week = 1;

        assert_eq!(
            b.period_at(ts("2014-01-06 08:00:00")).unwrap(),
            RankWindow {
                start: ts("2014-01-06 08:00:00"),
                end: ts("2014-01-07 08:00:00"),
            }
        );
        assert!(b.is_open_at(ts("2014-01-07 07:59:59")));
        // The authored end is the first moment that is out, so it is never open again.
        assert_eq!(b.period_at(ts("2014-01-07 08:00:00")), None);
        assert_eq!(b.window_at(ts("2014-01-07 08:00:00")), None);
        assert!(!b.is_open_at(ts("2014-01-07 08:00:00")));
        assert_eq!(b.period_at(ts("2014-01-06 07:59:59")), None);
    }

    #[test]
    fn invalid_authored_dates_disable_a_board() {
        // A positive end month that does not exist: the board must not become unbounded.
        let mut bad_end = fishing(2, RankKind::FishingLength, "최고 길이");
        bad_end.ed_month = 13;
        assert_eq!(bad_end.lifetime(), Err(RankDateIssue::End));
        assert_eq!(RankDateIssue::End.side(), "end");
        assert!(bad_end.is_disabled());
        assert_eq!(bad_end.period_at(ts("2013-11-09 20:30:00")), None);
        assert_eq!(bad_end.window_at(ts("2013-11-09 20:30:00")), None);
        assert!(!bad_end.is_open_at(ts("2013-11-09 20:30:00")));

        // An end year with no month/day at all is the same authored mistake.
        let mut partial_end = fishing(3, RankKind::FishingWeight, "무게 합산");
        partial_end.ed_month = 0;
        partial_end.ed_day = 0;
        partial_end.ed_hour = 0;
        partial_end.ed_min = 0;
        assert_eq!(partial_end.lifetime(), Err(RankDateIssue::End));

        // A start that is not a real date (February 30) disables the board as well.
        let mut bad_start = arena();
        bad_start.st_month = 2;
        bad_start.st_day = 30;
        assert_eq!(bad_start.lifetime(), Err(RankDateIssue::Start));
        assert_eq!(RankDateIssue::Start.side(), "start");
        assert_eq!(bad_start.period_at(ts("2014-03-01 00:00:00")), None);

        // All-zero dates are "authored nothing", not a mistake: such a board is unbounded.
        let mut open = arena();
        open.st_year = 0;
        open.st_month = 0;
        open.st_day = 0;
        open.st_hour = 0;
        open.st_min = 0;
        open.reset_week = 0;
        assert_eq!(
            open.lifetime(),
            Ok(RankLifetime {
                from: None,
                until: None,
            })
        );
        assert!(!open.is_disabled());
        let w = open.window_at(ts("2026-06-10 12:00:00")).unwrap();
        assert_eq!(
            (w.start, w.end),
            (DateTime::<Utc>::MIN_UTC, DateTime::<Utc>::MAX_UTC)
        );
    }

    #[test]
    fn only_supported_board_kinds_are_handed_out() {
        let c = empty_tables();
        insert_rank(&c, &arena());
        insert_rank(&c, &fishing(2, RankKind::FishingLength, "최고 길이"));
        insert_rank(&c, &fishing(3, RankKind::FishingWeight, "무게 합산"));
        insert_rank(&c, &racing());
        let mut unknown = arena();
        unknown.id = 5;
        unknown.rank_kind_id = 99;
        insert_rank(&c, &unknown);

        let d = load(&c);

        // All five rows are kept, in id order.
        assert_eq!(
            d.ranks.iter().map(|r| r.id).collect::<Vec<_>>(),
            [1, 2, 3, 4, 5]
        );
        // The supported boards are the arena and the two fishing boards.
        assert_eq!(
            d.supported_ranks().map(|r| r.id).collect::<Vec<_>>(),
            [1, 2, 3]
        );
        assert_eq!(d.rank(4).unwrap().kind(), Some(RankKind::Racing));
        assert!(!d.rank(4).unwrap().is_supported());
        // A kind the table does not define is kept raw and is not supported either.
        assert_eq!(d.rank(5).unwrap().rank_kind_id, 99);
        assert_eq!(d.rank(5).unwrap().kind(), None);
        assert!(!d.rank(5).unwrap().is_supported());
        // Every kind maps back to its authored id.
        assert_eq!(RankKind::from_id(0), None);
        for kind in [
            RankKind::Arena,
            RankKind::Racing,
            RankKind::FishingWeight,
            RankKind::FishingLength,
        ] {
            assert_eq!(RankKind::from_id(kind.id()), Some(kind));
        }
        assert!(day_of_week::is_weekday(day_of_week::SATURDAY));
        assert!(!day_of_week::is_weekday(day_of_week::NO_DAY));
    }

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_compact_db_rankings() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let c =
            Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let count = |table: &str| -> usize {
            c.query_row(&format!("SELECT COUNT(*) FROM {table}"), [], |r| {
                r.get::<_, i64>(0)
            })
            .unwrap() as usize
        };
        let d = load(&c);

        // The shipped 1.2 tables: 4 boards, 40 scopes, 40 rewards and 40 rows in each link table.
        assert_eq!(
            (
                d.ranks.len(),
                d.scopes.len(),
                d.rewards.len(),
                d.scope_links.len(),
                d.reward_links.len(),
            ),
            (4, 40, 40, 40, 40)
        );
        assert_eq!(d.ranks.len(), count("ranks"));
        assert_eq!(d.scopes.len(), count("rank_scopes"));
        assert_eq!(d.rewards.len(), count("rank_rewards"));
        assert_eq!(d.scope_links.len(), count("rank_scope_links"));
        assert_eq!(d.reward_links.len(), count("rank_reward_links"));

        // Board 1: arena, four weeks, no end, no day/time window, open today.
        let arena = d.rank(1).unwrap();
        assert_eq!(arena.name, "검투장 지배자");
        assert_eq!(
            (
                arena.st_year,
                arena.st_month,
                arena.st_day,
                arena.st_hour,
                arena.st_min
            ),
            (2014, 1, 6, 8, 0)
        );
        assert_eq!(
            (
                arena.ed_year,
                arena.ed_month,
                arena.ed_day,
                arena.ed_hour,
                arena.ed_min
            ),
            (0, 0, 0, 0, 0)
        );
        assert_eq!(
            (
                arena.day_of_week_id,
                arena.start_time,
                arena.end_time,
                arena.zone_group_id,
                arena.rank_kind_id,
                arena.reset_week,
            ),
            (day_of_week::NO_DAY, 0, 0, 77, 1, 4)
        );
        assert_eq!(
            (arena.v1.as_str(), arena.v2.as_str()),
            ("검투장 점수", "검투장 승리 횟수")
        );
        assert_eq!(arena.lifetime().unwrap().until, None);
        let now = Utc::now();
        let period = arena.period_at(now).unwrap();
        assert!(period.contains(now));
        assert_eq!(period.duration(), TimeDelta::weeks(4));
        assert!(arena.is_open_at(now));

        // Boards 2 and 3: the fishing contest, Saturday 20:00-22:00, weekly, ended 2014-01-01.
        for (id, kind, v1) in [
            (2, RankKind::FishingLength, "최고 길이"),
            (3, RankKind::FishingWeight, "무게 합산"),
        ] {
            let f = d.rank(id).unwrap();
            assert_eq!(f.kind(), Some(kind), "board {id}");
            assert_eq!(
                (f.st_year, f.st_month, f.st_day, f.st_hour, f.ed_year, f.ed_month, f.ed_day,),
                (2013, 11, 4, 8, 2014, 1, 1),
                "board {id}"
            );
            assert_eq!(
                (
                    f.day_of_week_id,
                    f.start_time,
                    f.end_time,
                    f.zone_group_id,
                    f.reset_week,
                    f.v1.as_str(),
                ),
                (day_of_week::SATURDAY, 20, 22, 49, 1, v1),
                "board {id}"
            );
            // Expired: the board is not reactivated today.
            assert!(!f.is_open_at(now), "board {id}");
            assert_eq!(f.period_at(now), None, "board {id}");
        }

        // Board 4 is the racing board: kept, but this server does not run it.
        let racing = d.rank(4).unwrap();
        assert_eq!(racing.rank_kind_id, RankKind::Racing.id());
        assert!(!racing.is_supported());
        assert_eq!(
            d.supported_ranks().map(|r| r.id).collect::<Vec<_>>(),
            [1, 2, 3]
        );

        // Every board's scope links resolve to a scope and one reward, and the reward rows carry
        // the authored items.
        assert_eq!(d.scopes_of(1).len(), 10);
        assert_eq!(d.scopes_of(2).len(), 10);
        assert_eq!(d.scopes_of(3).len(), 10);
        assert_eq!(d.scopes_of(4).len(), 10);
        assert_eq!(d.rewards_of(1, 1)[0].item_id, 32488);
        assert_eq!(d.rewards_of(1, 1)[0].weeks, 4);
        assert_eq!(d.rewards_of(2, 11)[0].item_id, 30219);
        for rank in &d.ranks {
            let pairs = d.scope_rewards_of(rank.id);
            assert_eq!(pairs.len(), d.scopes_of(rank.id).len());
            assert!(pairs.iter().all(|(_, reward)| reward.is_some()));
        }
    }
}
