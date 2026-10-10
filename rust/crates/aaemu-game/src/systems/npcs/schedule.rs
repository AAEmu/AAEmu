//! `GameScheduleManager` + `SchedulesGameData` (`game_schedules`, `game_schedule_spawners`,
//! `game_schedule_doodads`, `game_schedule_quests`).
//!
//! The C# keeps the rows in `SchedulesGameData` and hands them to the manager in `PostLoad`;
//! here both live in [`ScheduleData`] (`server.game_data.systems.npcs.schedule`): the manager has
//! no state of its own besides the derived `spawner -> schedules` / `doodad -> schedules` maps
//! built by `LoadGameScheduleSpawnersData`.
//!
//! Time: every C# check uses `DateTime.UtcNow`; the Rust functions take `now` explicitly (unix
//! seconds, UTC) so they are testable. `GetCronRemainingTime` / `GetDoodadCronRemainingTime`
//! return the same NCrontab expressions (6 fields, seconds first) as the C#; [`CronSchedule`]
//! ports the subset of NCrontab (`CrontabSchedule.Parse` with `IncludingSeconds` +
//! `GetNextOccurrence`) that `GetRemainingTime` and the cron-driven spawner tasks need.

use std::collections::{HashMap, HashSet};

use chrono::{DateTime, Datelike, NaiveDate, NaiveDateTime, TimeDelta, Timelike};

use crate::data::sqlite::for_each_row;
use crate::systems::DataLoadCtx;

/// `GameScheduleManager.PeriodStatus`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PeriodStatus {
    /// The doodad / spawner id is not in any schedule.
    NotFound,
    /// The period has not started.
    NotStarted,
    /// The period is in progress.
    InProgress,
    /// The period has ended.
    Ended,
}

/// `Models.Game.Schedules.DayOfWeek` (Sunday = 1 .. Saturday = 7, Invalid = 8).
pub mod day_of_week {
    pub const SUNDAY: i32 = 1;
    pub const MONDAY: i32 = 2;
    pub const TUESDAY: i32 = 3;
    pub const WEDNESDAY: i32 = 4;
    pub const THURSDAY: i32 = 5;
    pub const FRIDAY: i32 = 6;
    pub const SATURDAY: i32 = 7;
    pub const INVALID: i32 = 8;
}

/// `GameSchedules`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct GameSchedule {
    pub id: i32,
    pub name: String,
    /// `DayOfWeekId` ([`day_of_week`]).
    pub day_of_week_id: i32,
    pub start_time: i32,
    pub end_time: i32,
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
    pub start_time_min: i32,
    pub end_time_min: i32,
}

/// `GameScheduleSpawners`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct GameScheduleSpawner {
    pub id: i32,
    pub game_schedule_id: i32,
    pub spawner_id: i32,
}

/// `GameScheduleDoodads`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct GameScheduleDoodad {
    pub id: i32,
    pub game_schedule_id: i32,
    pub doodad_id: i32,
}

/// `GameScheduleQuests`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct GameScheduleQuest {
    pub id: i32,
    pub game_schedule_id: i32,
    pub quest_id: i32,
}

/// Schedule tables + queries (`SchedulesGameData` + `GameScheduleManager`).
#[derive(Debug, Default)]
pub struct ScheduleData {
    /// `_gameSchedules` (by id).
    pub game_schedules: HashMap<i32, GameSchedule>,
    /// `_gameScheduleSpawners.Values` (insertion order, `TryAdd` by id keeps the first row).
    pub game_schedule_spawners: Vec<GameScheduleSpawner>,
    /// `_gameScheduleDoodads.Values`.
    pub game_schedule_doodads: Vec<GameScheduleDoodad>,
    /// `_gameScheduleQuests.Values`.
    pub game_schedule_quests: Vec<GameScheduleQuest>,
    /// `_gameScheduleSpawnerIds` (spawner id -> schedule ids in row order).
    spawner_ids: HashMap<i32, Vec<i32>>,
    /// `_gameScheduleDoodadIds` (doodad template id -> schedule ids in row order).
    doodad_ids: HashMap<i32, Vec<i32>>,
}

/// `Dictionary.TryAdd` over a row list keyed by `id`.
fn try_add<T>(list: &mut Vec<T>, seen: &mut HashSet<i32>, id: i32, row: T) {
    if seen.insert(id) {
        list.push(row);
    }
}

impl ScheduleData {
    /// `SchedulesGameData.Load` + `GameScheduleManager.Load` (`PostLoad` +
    /// `LoadGameScheduleSpawnersData`).
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        let conn = ctx.conn;
        let mut d = ScheduleData::default();
        for_each_row(conn, "SELECT * FROM game_schedules", [], |r| {
            let t = GameSchedule {
                id: r.i32("id")?,
                name: r.string_or("name", "")?,
                day_of_week_id: r.i32("day_of_week_id")?,
                start_time: r.i32("start_time")?,
                end_time: r.i32("end_time")?,
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
                start_time_min: r.i32("start_time_min")?,
                end_time_min: r.i32("end_time_min")?,
            };
            d.game_schedules.entry(t.id).or_insert(t);
            Ok(())
        })?;
        let mut seen = HashSet::new();
        for_each_row(conn, "SELECT * FROM game_schedule_spawners", [], |r| {
            let t = GameScheduleSpawner {
                id: r.i32("id")?,
                game_schedule_id: r.i32("game_schedule_id")?,
                spawner_id: r.i32("spawner_id")?,
            };
            try_add(&mut d.game_schedule_spawners, &mut seen, t.id, t);
            Ok(())
        })?;
        let mut seen = HashSet::new();
        for_each_row(conn, "SELECT * FROM game_schedule_doodads", [], |r| {
            let t = GameScheduleDoodad {
                id: r.i32("id")?,
                game_schedule_id: r.i32("game_schedule_id")?,
                doodad_id: r.i32("doodad_id")?,
            };
            try_add(&mut d.game_schedule_doodads, &mut seen, t.id, t);
            Ok(())
        })?;
        let mut seen = HashSet::new();
        for_each_row(conn, "SELECT * FROM game_schedule_quests", [], |r| {
            let t = GameScheduleQuest {
                id: r.i32("id")?,
                game_schedule_id: r.i32("game_schedule_id")?,
                quest_id: r.i32("quest_id")?,
            };
            try_add(&mut d.game_schedule_quests, &mut seen, t.id, t);
            Ok(())
        })?;
        d.load_game_schedule_spawners_data();
        tracing::info!(
            "Loaded schedules ({} schedules, {} spawners, {} doodads, {} quests)",
            d.game_schedules.len(),
            d.game_schedule_spawners.len(),
            d.game_schedule_doodads.len(),
            d.game_schedule_quests.len()
        );
        Ok(d)
    }

    /// Builds the data from rows (tests / synthetic data); runs `LoadGameScheduleSpawnersData`.
    pub fn from_rows(
        schedules: Vec<GameSchedule>,
        spawners: Vec<GameScheduleSpawner>,
        doodads: Vec<GameScheduleDoodad>,
        quests: Vec<GameScheduleQuest>,
    ) -> Self {
        let mut d = ScheduleData::default();
        for s in schedules {
            d.game_schedules.entry(s.id).or_insert(s);
        }
        let mut seen = HashSet::new();
        for s in spawners {
            try_add(&mut d.game_schedule_spawners, &mut seen, s.id, s);
        }
        let mut seen = HashSet::new();
        for s in doodads {
            try_add(&mut d.game_schedule_doodads, &mut seen, s.id, s);
        }
        let mut seen = HashSet::new();
        for s in quests {
            try_add(&mut d.game_schedule_quests, &mut seen, s.id, s);
        }
        d.load_game_schedule_spawners_data();
        d
    }

    /// `LoadGameScheduleSpawnersData` (quests: "TODO: quests data" in C# too).
    fn load_game_schedule_spawners_data(&mut self) {
        self.spawner_ids.clear();
        for gss in &self.game_schedule_spawners {
            self.spawner_ids
                .entry(gss.spawner_id)
                .or_default()
                .push(gss.game_schedule_id);
        }
        self.doodad_ids.clear();
        for gsd in &self.game_schedule_doodads {
            self.doodad_ids
                .entry(gsd.doodad_id)
                .or_default()
                .push(gsd.game_schedule_id);
        }
    }

    /// `CheckSpawnerInScheduleSpawners(spawnerId)`.
    pub fn check_spawner_in_schedule_spawners(&self, spawner_id: i32) -> bool {
        self.spawner_ids.contains_key(&spawner_id)
    }

    /// `CheckDoodadInScheduleSpawners(id)`.
    pub fn check_doodad_in_schedule_spawners(&self, doodad_id: i32) -> bool {
        self.doodad_ids.contains_key(&doodad_id)
    }

    /// `CheckSpawnerInGameSchedules(spawnerId)`: one of the spawner's schedule ids exists.
    /// C# indexes `_gameScheduleSpawnerIds[spawnerId]` and throws `KeyNotFoundException` for an
    /// unknown spawner (callers check `CheckSpawnerInScheduleSpawners` first); Rust returns false.
    pub fn check_spawner_in_game_schedules(&self, spawner_id: i32) -> bool {
        self.spawner_ids
            .get(&spawner_id)
            .is_some_and(|ids| ids.iter().any(|id| self.game_schedules.contains_key(id)))
    }

    /// `CheckDoodadInGameSchedules(id)` (same unknown-id note as
    /// [`Self::check_spawner_in_game_schedules`]).
    pub fn check_doodad_in_game_schedules(&self, doodad_id: u32) -> bool {
        self.doodad_ids
            .get(&(doodad_id as i32))
            .is_some_and(|ids| ids.iter().any(|id| self.game_schedules.contains_key(id)))
    }

    /// `GetPeriodStatusNpc(spawnerId)` at `now` (unix seconds, UTC: `DateTime.UtcNow`).
    pub fn period_status_npc(&self, spawner_id: i32, now: i64) -> PeriodStatus {
        match self.spawner_ids.get(&spawner_id) {
            Some(ids) => self.check_period_status(ids, now),
            None => PeriodStatus::NotFound,
        }
    }

    /// `GetPeriodStatusDoodad(doodadId)` at `now` (unix seconds, UTC).
    pub fn period_status_doodad(&self, doodad_id: i32, now: i64) -> PeriodStatus {
        match self.doodad_ids.get(&doodad_id) {
            Some(ids) => self.check_period_status(ids, now),
            None => PeriodStatus::NotFound,
        }
    }

    /// `CheckPeriodStatus(ids)`.
    fn check_period_status(&self, ids: &[i32], now: i64) -> PeriodStatus {
        let mut has_in_progress = false;
        let mut has_ended = false;
        for id in ids {
            if let Some(gs) = self.game_schedules.get(id) {
                let (started, ended) = check_data(gs, now);
                if started && !ended {
                    has_in_progress = true;
                } else if ended {
                    has_ended = true;
                }
            }
        }
        if has_in_progress {
            PeriodStatus::InProgress
        } else if has_ended {
            PeriodStatus::Ended
        } else {
            PeriodStatus::NotStarted
        }
    }

    /// `GetCronRemainingTime(spawnerId, start)`: the cron expression of the spawner's **last**
    /// existing schedule (the loop overwrites), or "" when the spawner has none.
    pub fn cron_remaining_time(&self, spawner_id: i32, start: bool) -> String {
        self.last_cron(self.spawner_ids.get(&spawner_id), start)
    }

    /// `GetDoodadCronRemainingTime(doodadId, start)` (same rules as
    /// [`Self::cron_remaining_time`]).
    pub fn doodad_cron_remaining_time(&self, doodad_id: i32, start: bool) -> String {
        self.last_cron(self.doodad_ids.get(&doodad_id), start)
    }

    fn last_cron(&self, ids: Option<&Vec<i32>>, start: bool) -> String {
        let mut cron = String::new();
        for id in ids.into_iter().flatten() {
            if let Some(gs) = self.game_schedules.get(id) {
                cron = cron_expression(gs, start);
            }
        }
        cron
    }

    /// `GetRemainingTime(spawnerId, start)` at `now` (unix seconds, UTC): `TimeSpan.Zero` for
    /// an unknown spawner, `TimeSpan.MaxValue` ([`TimeDelta::MAX`]) when none of its schedules
    /// exists, else the smallest time to the next cron occurrence. An unparsable expression
    /// (e.g. day / month 0) is the `CrontabException` the C# throws.
    pub fn remaining_time(
        &self,
        spawner_id: i32,
        start: bool,
        now: i64,
    ) -> Result<TimeDelta, CronError> {
        let Some(ids) = self.spawner_ids.get(&spawner_id) else {
            return Ok(TimeDelta::zero());
        };
        let now_dt = utc(now);
        let mut remaining = TimeDelta::MAX;
        for id in ids {
            let Some(gs) = self.game_schedules.get(id) else {
                continue;
            };
            let schedule = CronSchedule::parse(&cron_expression(gs, start))?;
            let span = schedule.next_occurrence(now_dt) - now_dt;
            if span <= remaining {
                remaining = span;
            }
        }
        Ok(remaining)
    }

    /// `HasGameScheduleSpawnersData(spawnerTemplateId)`.
    pub fn has_game_schedule_spawners_data(&self, spawner_template_id: u32) -> bool {
        self.game_schedule_spawners
            .iter()
            .any(|g| g.spawner_id as i64 == i64::from(spawner_template_id))
    }

    /// `GetGameScheduleDoodadsData(doodadId)`: the schedule ids of the doodad (the C# stores
    /// them in the `GameScheduleId` property and returns `Count != 0`).
    pub fn game_schedule_doodads_data(&self, doodad_id: u32) -> Vec<i32> {
        self.game_schedule_doodads
            .iter()
            .filter(|g| g.doodad_id as i64 == i64::from(doodad_id))
            .map(|g| g.game_schedule_id)
            .collect()
    }

    /// `GetGameScheduleQuestsData(questId)` (see [`Self::game_schedule_doodads_data`]).
    pub fn game_schedule_quests_data(&self, quest_id: u32) -> Vec<i32> {
        self.game_schedule_quests
            .iter()
            .filter(|g| g.quest_id as i64 == i64::from(quest_id))
            .map(|g| g.game_schedule_id)
            .collect()
    }

    /// The schedule ids of a spawner (`_gameScheduleSpawnerIds[spawnerId]`).
    pub fn spawner_schedule_ids(&self, spawner_id: i32) -> Option<&[i32]> {
        self.spawner_ids.get(&spawner_id).map(Vec::as_slice)
    }

    /// The schedule ids of a doodad template (`_gameScheduleDoodadIds[doodadId]`).
    pub fn doodad_schedule_ids(&self, doodad_id: i32) -> Option<&[i32]> {
        self.doodad_ids.get(&doodad_id).map(Vec::as_slice)
    }
}

/// Unix seconds (UTC) -> naive UTC date time (`DateTime.UtcNow`).
fn utc(now: i64) -> NaiveDateTime {
    DateTime::from_timestamp(now, 0)
        .unwrap_or_default()
        .naive_utc()
}

/// `new DateTime(y, m, d)`; the C# throws for an impossible date (none in the 1.2 data), Rust
/// treats it as "not specified".
fn date(y: i32, m: i32, d: i32) -> Option<NaiveDate> {
    let date = NaiveDate::from_ymd_opt(y, m as u32, d as u32);
    if date.is_none() {
        tracing::warn!("GameScheduleManager: invalid schedule date {y}-{m}-{d}");
    }
    date
}

/// `CheckData(value)` at `now`: `(hasStarted, hasEnded)`.
pub fn check_data(value: &GameSchedule, now: i64) -> (bool, bool) {
    let now = utc(now);
    let current_time = i64::from(now.num_seconds_from_midnight());
    let current_date = now.date();
    // `(int)now.DayOfWeek + 1`: Sunday = 1.
    let current_day_of_week = now.weekday().num_days_from_sunday() as i32 + 1;

    // `None` = `DateTime.MinValue` / `DateTime.MaxValue`.
    let start_date = (value.st_year > 0 && value.st_month > 0 && value.st_day > 0)
        .then(|| date(value.st_year, value.st_month, value.st_day))
        .flatten();
    let end_date = (value.ed_year > 0 && value.ed_month > 0 && value.ed_day > 0)
        .then(|| date(value.ed_year, value.ed_month, value.ed_day))
        .flatten();
    // `new TimeSpan(StartTime, StartTimeMin, 0)` (any hour / minute values are accepted).
    let start_time = i64::from(value.start_time) * 3600 + i64::from(value.start_time_min) * 60;
    let end_time = i64::from(value.end_time) * 3600 + i64::from(value.end_time_min) * 60;

    let mut has_started = false;
    let mut has_ended = false;

    let after_start = match start_date {
        None => true,
        Some(sd) => current_date > sd || (current_date == sd && current_time >= start_time),
    };
    let before_end = match end_date {
        None => true,
        Some(ed) => current_date < ed || (current_date == ed && current_time <= end_time),
    };
    if after_start
        && before_end
        && (current_day_of_week == value.day_of_week_id
            || value.day_of_week_id == day_of_week::INVALID)
    {
        has_started = true;
    }
    if let Some(ed) = end_date {
        if current_date > ed || (current_date == ed && current_time >= end_time) {
            has_ended = true;
        }
    }
    (has_started, has_ended)
}

/// `GetCronExpression(value, start)`.
pub fn cron_expression(value: &GameSchedule, start: bool) -> String {
    // DayOfWeek -> cron (0 = Sunday); anything else is "not specified" (8).
    let day_of_week = match value.day_of_week_id {
        day_of_week::SUNDAY => 0,
        day_of_week::MONDAY => 1,
        day_of_week::TUESDAY => 2,
        day_of_week::WEDNESDAY => 3,
        day_of_week::THURSDAY => 4,
        day_of_week::FRIDAY => 5,
        day_of_week::SATURDAY => 6,
        _ => day_of_week::INVALID,
    };
    // The local `BuildCronExpression` switches on the captured `dayOfWeek`, not its argument.
    let build = |seconds: i32, minute: i32, hour: i32, day: i32, month: i32, dow: i32| {
        if day_of_week == 8 {
            format!("{seconds} {minute} {hour} {day} {month} *")
        } else {
            format!("{seconds} {minute} {hour} {day} {month} {dow}")
        }
    };
    let dow = if value.day_of_week_id == day_of_week::INVALID {
        day_of_week::INVALID
    } else {
        day_of_week
    };
    let v = value;
    let cron = if start {
        let (st_hour, st_minute) = (v.st_hour, v.st_min);
        if v.start_time == 0
            && v.end_time == 0
            && v.st_month == 0
            && v.st_day == 0
            && v.st_hour == 0
        {
            build(0, 0, 0, v.st_day, v.st_month, dow)
        } else if (v.end_time > 0 && v.st_month == 0 && v.st_day == 0 && v.st_hour == 0)
            || (v.end_time > 0 && v.st_month > 0 && v.st_day > 0)
        {
            build(0, v.start_time_min, v.start_time, v.st_day, v.st_month, dow)
        } else {
            build(0, st_minute, st_hour, v.st_day, v.st_month, dow)
        }
    } else {
        // `stHour` / `stMinute` are `EdHour` / `EdMin` when `start == false`.
        if v.start_time == 0
            && v.end_time == 0
            && v.ed_month == 0
            && v.ed_day == 0
            && v.ed_hour == 0
        {
            build(0, 0, 0, v.ed_day, v.ed_month, dow)
        } else if (v.end_time > 0 && v.ed_month == 0 && v.ed_day == 0 && v.ed_hour == 0)
            || (v.end_time > 0 && v.ed_month > 0 && v.ed_day > 0)
        {
            build(0, v.end_time_min, v.end_time, v.ed_day, v.ed_month, dow)
        } else {
            build(0, v.ed_min, v.ed_hour, v.ed_day, v.ed_month, dow)
        }
    };
    // "Crontab doesn't support ?" (never produced).
    cron.replace('?', "*")
}

// ---------------------------------------------------------------------------------------------
// NCrontab (`CrontabSchedule`, `IncludingSeconds = true`)
// ---------------------------------------------------------------------------------------------

/// `CrontabException`.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[error("'{expression}' is not a valid crontab expression: {reason}")]
pub struct CronError {
    pub expression: String,
    pub reason: String,
}

const NIL: i32 = -1;

/// One `CrontabField` (a bit set over `min..=max`).
#[derive(Debug, Clone, PartialEq, Eq)]
struct CronField {
    min: i32,
    bits: Vec<bool>,
    min_set: i32,
    max_set: i32,
}

impl CronField {
    fn parse(text: &str, min: i32, max: i32, expr: &str) -> Result<Self, CronError> {
        let err = |reason: String| CronError {
            expression: expr.to_string(),
            reason,
        };
        let mut bits = vec![false; (max - min + 1) as usize];
        let num = |s: &str| -> Result<i32, CronError> {
            s.parse::<i32>()
                .map_err(|_| err(format!("'{s}' is not a number")))
        };
        for item in text.split(',') {
            let (range, step) = match item.split_once('/') {
                Some((r, s)) => (r, num(s)?),
                None => (item, 1),
            };
            if step <= 0 {
                return Err(err(format!("invalid step {step}")));
            }
            let (lo, hi) = if range == "*" {
                (min, max)
            } else if let Some((a, b)) = range.split_once('-') {
                (num(a)?, num(b)?)
            } else {
                let v = num(range)?;
                // `N/step` means `N-max/step`.
                (v, if item.contains('/') { max } else { v })
            };
            if lo < min || hi > max || lo > hi {
                return Err(err(format!("{lo}-{hi} must be between {min} and {max}")));
            }
            let mut v = lo;
            while v <= hi {
                bits[(v - min) as usize] = true;
                v += step;
            }
        }
        let min_set = bits.iter().position(|b| *b).map_or(NIL, |i| i as i32 + min);
        let max_set = bits
            .iter()
            .rposition(|b| *b)
            .map_or(NIL, |i| i as i32 + min);
        Ok(Self {
            min,
            bits,
            min_set,
            max_set,
        })
    }

    fn contains(&self, v: i32) -> bool {
        v >= self.min
            && ((v - self.min) as usize) < self.bits.len()
            && self.bits[(v - self.min) as usize]
    }

    /// `GetFirst`.
    fn first(&self) -> i32 {
        self.min_set
    }

    /// `Next(start)`.
    fn next(&self, start: i32) -> i32 {
        if start < self.min_set {
            return self.min_set;
        }
        let mut v = start;
        while v <= self.max_set {
            if self.contains(v) {
                return v;
            }
            v += 1;
        }
        NIL
    }
}

/// `CrontabSchedule` with seconds (`s m h dom mon dow`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CronSchedule {
    seconds: CronField,
    minutes: CronField,
    hours: CronField,
    days: CronField,
    months: CronField,
    days_of_week: CronField,
}

/// `DateTime.MaxValue` (the default `endTime`).
fn date_time_max() -> NaiveDateTime {
    NaiveDate::from_ymd_opt(9999, 12, 31)
        .and_then(|d| d.and_hms_nano_opt(23, 59, 59, 999_999_900))
        .expect("max date")
}

fn days_in_month(year: i32, month: i32) -> i32 {
    let (ny, nm) = if month == 12 {
        (year + 1, 1)
    } else {
        (year, month + 1)
    };
    let first = NaiveDate::from_ymd_opt(year, month as u32, 1);
    let next = NaiveDate::from_ymd_opt(ny, nm as u32, 1);
    match (first, next) {
        (Some(a), Some(b)) => (b - a).num_days() as i32,
        _ => 31,
    }
}

impl CronSchedule {
    /// `CrontabSchedule.Parse(expression, new ParseOptions { IncludingSeconds = true })`.
    pub fn parse(expression: &str) -> Result<Self, CronError> {
        let fields: Vec<&str> = expression.split_whitespace().collect();
        if fields.len() != 6 {
            return Err(CronError {
                expression: expression.to_string(),
                reason: format!("expected 6 components, got {}", fields.len()),
            });
        }
        Ok(Self {
            seconds: CronField::parse(fields[0], 0, 59, expression)?,
            minutes: CronField::parse(fields[1], 0, 59, expression)?,
            hours: CronField::parse(fields[2], 0, 23, expression)?,
            days: CronField::parse(fields[3], 1, 31, expression)?,
            months: CronField::parse(fields[4], 1, 12, expression)?,
            days_of_week: CronField::parse(fields[5], 0, 6, expression)?,
        })
    }

    /// `GetNextOccurrence(baseTime)`: the first occurrence strictly after `base` (second
    /// precision), or `DateTime.MaxValue` when there is none.
    pub fn next_occurrence(&self, base: NaiveDateTime) -> NaiveDateTime {
        let end = date_time_max();
        self.try_next_occurrence(base, end).unwrap_or(end)
    }

    /// `TryGetNextOccurrence(baseTime, endTime)` (NCrontab's algorithm, including its
    /// day-of-month / day-of-week AND semantics and the 23:59:59 retry for the week day).
    pub fn try_next_occurrence(
        &self,
        base: NaiveDateTime,
        end: NaiveDateTime,
    ) -> Option<NaiveDateTime> {
        let mut base = base;
        loop {
            let (by, bmo, bd) = (base.year(), base.month() as i32, base.day() as i32);
            let (bh, bmi, bs) = (
                base.hour() as i32,
                base.minute() as i32,
                base.second() as i32,
            );
            let (ey, emo, ed) = (end.year(), end.month() as i32, end.day() as i32);
            let (mut year, mut month, mut day) = (by, bmo, bd);
            let (mut hour, mut minute) = (bh, bmi);
            let s = &self.seconds;
            let mut second = s.next(bs + 1);
            if second == NIL {
                second = s.first();
                minute += 1;
            }
            minute = self.minutes.next(minute);
            if minute == NIL {
                second = s.first();
                minute = self.minutes.first();
                hour += 1;
            } else if minute > bmi {
                second = s.first();
            }
            hour = self.hours.next(hour);
            if hour == NIL {
                minute = self.minutes.first();
                hour = self.hours.first();
                day += 1;
            } else if hour > bh {
                second = s.first();
                minute = self.minutes.first();
            }
            day = self.days.next(day);
            loop {
                // RetryDayMonth:
                if day == NIL {
                    second = s.first();
                    minute = self.minutes.first();
                    hour = self.hours.first();
                    day = self.days.first();
                    month += 1;
                } else if day > bd {
                    second = s.first();
                    minute = self.minutes.first();
                    hour = self.hours.first();
                }
                month = self.months.next(month);
                if month == NIL {
                    second = s.first();
                    minute = self.minutes.first();
                    hour = self.hours.first();
                    day = self.days.first();
                    month = self.months.first();
                    year += 1;
                } else if month > bmo {
                    second = s.first();
                    minute = self.minutes.first();
                    hour = self.hours.first();
                    day = self.days.first();
                }
                if year > 9999 {
                    // `Calendar.GetDaysInMonth` / `new DateTime` would throw past year 9999.
                    return None;
                }
                let date_changed = day != bd || month != bmo || year != by;
                if day > 28 && date_changed && day > days_in_month(year, month) {
                    if year >= ey && month >= emo && day >= ed {
                        return None;
                    }
                    day = NIL;
                    continue;
                }
                break;
            }
            let next = NaiveDate::from_ymd_opt(year, month as u32, day as u32)?.and_hms_opt(
                hour as u32,
                minute as u32,
                second as u32,
            )?;
            if next >= end {
                return None;
            }
            if self
                .days_of_week
                .contains(next.weekday().num_days_from_sunday() as i32)
            {
                return Some(next);
            }
            base = next.date().and_hms_opt(23, 59, 59)?;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ts(s: &str) -> i64 {
        NaiveDateTime::parse_from_str(s, "%Y-%m-%d %H:%M:%S")
            .unwrap()
            .and_utc()
            .timestamp()
    }

    fn dt(s: &str) -> NaiveDateTime {
        NaiveDateTime::parse_from_str(s, "%Y-%m-%d %H:%M:%S").unwrap()
    }

    #[allow(clippy::too_many_arguments)]
    fn gs(
        id: i32,
        dow: i32,
        (start_time, start_time_min, end_time, end_time_min): (i32, i32, i32, i32),
        (st_year, st_month, st_day, st_hour, st_min): (i32, i32, i32, i32, i32),
        (ed_year, ed_month, ed_day, ed_hour, ed_min): (i32, i32, i32, i32, i32),
    ) -> GameSchedule {
        GameSchedule {
            id,
            name: format!("s{id}"),
            day_of_week_id: dow,
            start_time,
            end_time,
            st_year,
            st_month,
            st_day,
            st_hour,
            st_min,
            ed_year,
            ed_month,
            ed_day,
            ed_hour,
            ed_min,
            start_time_min,
            end_time_min,
        }
    }

    /// Real rows of the 1.2 compact DB (ids 10, 100, 108, 115, 135).
    fn real_rows() -> Vec<GameSchedule> {
        vec![
            gs(
                10,
                1,
                (17, 0, 18, 0),
                (2013, 7, 3, 0, 0),
                (2013, 7, 31, 0, 0),
            ),
            gs(
                100,
                8,
                (0, 0, 0, 0),
                (2014, 1, 14, 0, 0),
                (2014, 1, 14, 23, 59),
            ),
            gs(108, 2, (0, 0, 23, 59), (0, 0, 0, 0, 0), (0, 0, 0, 0, 0)),
            gs(
                115,
                8,
                (0, 0, 0, 0),
                (2014, 1, 22, 6, 0),
                (2014, 2, 5, 4, 59),
            ),
            gs(135, 8, (0, 0, 0, 0), (0, 0, 0, 0, 0), (0, 0, 0, 0, 0)),
        ]
    }

    #[test]
    fn cron_strings_match_csharp() {
        let r = real_rows();
        // Sunday, StartTime 17 / EndTime 18, dated.
        assert_eq!(cron_expression(&r[0], true), "0 0 17 3 7 0");
        assert_eq!(cron_expression(&r[0], false), "0 0 18 31 7 0");
        // Invalid day, no start/end time: default branch (StHour/StMin, EdHour/EdMin).
        assert_eq!(cron_expression(&r[1], true), "0 0 0 14 1 *");
        assert_eq!(cron_expression(&r[1], false), "0 59 23 14 1 *");
        // Monday, EndTime 23:59, undated: day / month 0 (NCrontab rejects them).
        assert_eq!(cron_expression(&r[2], true), "0 0 0 0 0 1");
        assert_eq!(cron_expression(&r[2], false), "0 59 23 0 0 1");
        assert_eq!(cron_expression(&r[3], true), "0 0 6 22 1 *");
        assert_eq!(cron_expression(&r[3], false), "0 59 4 5 2 *");
        // All zero: first branch.
        assert_eq!(cron_expression(&r[4], true), "0 0 0 0 0 *");
        assert_eq!(cron_expression(&r[4], false), "0 0 0 0 0 *");
        // Unknown day-of-week id: treated as "not specified".
        let mut odd = r[0].clone();
        odd.day_of_week_id = 0;
        assert_eq!(cron_expression(&odd, true), "0 0 17 3 7 *");
    }

    #[test]
    fn period_status_at_fixed_times() {
        let d = ScheduleData::from_rows(
            real_rows(),
            vec![
                GameScheduleSpawner {
                    id: 1,
                    game_schedule_id: 108,
                    spawner_id: 500,
                },
                GameScheduleSpawner {
                    id: 2,
                    game_schedule_id: 100,
                    spawner_id: 501,
                },
                GameScheduleSpawner {
                    id: 3,
                    game_schedule_id: 115,
                    spawner_id: 501,
                },
                GameScheduleSpawner {
                    id: 4,
                    game_schedule_id: 999,
                    spawner_id: 502,
                },
                GameScheduleSpawner {
                    id: 4,
                    game_schedule_id: 108,
                    spawner_id: 503,
                },
            ],
            vec![GameScheduleDoodad {
                id: 1,
                game_schedule_id: 10,
                doodad_id: 7733,
            }],
            vec![GameScheduleQuest {
                id: 10,
                game_schedule_id: 23,
                quest_id: 5490,
            }],
        );
        // 2026-10-12 is a Monday (DayOfWeek 2).
        let monday = ts("2026-10-12 10:00:00");
        let tuesday = ts("2026-10-13 10:00:00");
        assert_eq!(d.period_status_npc(500, monday), PeriodStatus::InProgress);
        assert_eq!(d.period_status_npc(500, tuesday), PeriodStatus::NotStarted);
        // Dated in 2014: ended.
        assert_eq!(d.period_status_npc(501, monday), PeriodStatus::Ended);
        // 2014-01-14 12:00, inside schedule 100's St/Ed dates and hours: the C# compares the
        // dates with `StartTime` / `EndTime` (both 0), so on its single day it has already
        // ended (mirrored).
        assert_eq!(
            d.period_status_npc(501, ts("2014-01-14 12:00:00")),
            PeriodStatus::Ended
        );
        // Before 2014-01-14 both have not started.
        assert_eq!(
            d.period_status_npc(501, ts("2014-01-13 23:59:59")),
            PeriodStatus::NotStarted
        );
        // Unknown schedule id only.
        assert_eq!(d.period_status_npc(502, monday), PeriodStatus::NotStarted);
        // TryAdd: the duplicate row id 4 is dropped.
        assert_eq!(d.period_status_npc(503, monday), PeriodStatus::NotFound);
        assert_eq!(d.period_status_npc(1, monday), PeriodStatus::NotFound);
        // Doodad schedule 10: Sundays 17:00-18:00 in July 2013, compared with StartTime.
        let sunday_in = ts("2013-07-07 17:30:00");
        assert_eq!(
            d.period_status_doodad(7733, sunday_in),
            PeriodStatus::InProgress
        );
        assert_eq!(
            d.period_status_doodad(7733, ts("2013-07-08 17:30:00")),
            PeriodStatus::NotStarted
        );
        // End date 2013-07-31, EndTime 18:00.
        assert_eq!(
            d.period_status_doodad(7733, ts("2013-07-31 18:00:00")),
            PeriodStatus::Ended
        );
        assert!(d.check_doodad_in_schedule_spawners(7733));
        assert!(d.check_doodad_in_game_schedules(7733));
        assert!(!d.check_doodad_in_game_schedules(1));
        assert!(d.check_spawner_in_schedule_spawners(502));
        assert!(!d.check_spawner_in_game_schedules(502));
        assert!(d.check_spawner_in_game_schedules(501));
        assert!(d.has_game_schedule_spawners_data(501));
        assert!(!d.has_game_schedule_spawners_data(503));
        assert_eq!(d.game_schedule_quests_data(5490), vec![23]);
        assert_eq!(d.game_schedule_doodads_data(7733), vec![10]);
        // Last schedule of the spawner wins.
        assert_eq!(d.cron_remaining_time(501, true), "0 0 6 22 1 *");
        assert_eq!(d.doodad_cron_remaining_time(7733, false), "0 0 18 31 7 0");
        assert_eq!(d.cron_remaining_time(9, true), "");
    }

    #[test]
    fn remaining_time() {
        let d = ScheduleData::from_rows(
            real_rows(),
            vec![
                GameScheduleSpawner {
                    id: 1,
                    game_schedule_id: 115,
                    spawner_id: 501,
                },
                GameScheduleSpawner {
                    id: 2,
                    game_schedule_id: 100,
                    spawner_id: 501,
                },
                GameScheduleSpawner {
                    id: 3,
                    game_schedule_id: 108,
                    spawner_id: 502,
                },
                GameScheduleSpawner {
                    id: 4,
                    game_schedule_id: 999,
                    spawner_id: 503,
                },
            ],
            vec![],
            vec![],
        );
        let now = ts("2026-01-10 00:00:00");
        // min("0 0 6 22 1 *" -> 2026-01-22 06:00, "0 0 0 14 1 *" -> 2026-01-14 00:00).
        assert_eq!(
            d.remaining_time(501, true, now).unwrap(),
            TimeDelta::days(4)
        );
        assert_eq!(d.remaining_time(9, true, now).unwrap(), TimeDelta::zero());
        assert_eq!(d.remaining_time(503, true, now).unwrap(), TimeDelta::MAX);
        // Day 0 / month 0: CrontabException.
        assert!(d.remaining_time(502, true, now).is_err());
    }

    #[test]
    fn ncrontab_next_occurrence() {
        let c = CronSchedule::parse("0 0 6 22 1 *").unwrap();
        assert_eq!(
            c.next_occurrence(dt("2026-01-22 06:00:00")),
            dt("2027-01-22 06:00:00")
        );
        assert_eq!(
            c.next_occurrence(dt("2026-01-22 05:59:59")),
            dt("2026-01-22 06:00:00")
        );
        // Day of month AND day of week: 2026-07-03 is a Friday, the next Sunday 3 July is 2033.
        let c = CronSchedule::parse("0 0 17 3 7 0").unwrap();
        assert_eq!(
            c.next_occurrence(dt("2026-01-01 00:00:00")),
            dt("2033-07-03 17:00:00")
        );
        // Every Monday 23:59.
        let c = CronSchedule::parse("0 59 23 * * 1").unwrap();
        assert_eq!(
            c.next_occurrence(dt("2026-10-10 12:00:00")),
            dt("2026-10-12 23:59:00")
        );
        // 31st skips short months.
        let c = CronSchedule::parse("0 0 0 31 * *").unwrap();
        assert_eq!(
            c.next_occurrence(dt("2026-04-01 00:00:00")),
            dt("2026-05-31 00:00:00")
        );
        // Leap day.
        let c = CronSchedule::parse("30 15 10 29 2 *").unwrap();
        assert_eq!(
            c.next_occurrence(dt("2025-03-01 00:00:00")),
            dt("2028-02-29 10:15:30")
        );
        // Lists, ranges and steps.
        let c = CronSchedule::parse("*/15 0-1 0 1,15 * *").unwrap();
        assert_eq!(
            c.next_occurrence(dt("2026-03-01 00:00:50")),
            dt("2026-03-01 00:01:00")
        );
        assert_eq!(
            c.next_occurrence(dt("2026-03-01 00:01:45")),
            dt("2026-03-15 00:00:00")
        );
        // Never: DateTime.MaxValue.
        let c = CronSchedule::parse("0 0 0 30 2 *").unwrap();
        assert_eq!(
            c.next_occurrence(dt("2026-01-01 00:00:00")),
            date_time_max()
        );
        for bad in [
            "0 0 0 0 0 *",
            "0 0 24 1 1 *",
            "0 0 0 1 1",
            "0 0 0 1 13 *",
            "x 0 0 1 1 *",
        ] {
            assert!(CronSchedule::parse(bad).is_err(), "{bad}");
        }
    }

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_compact_db_schedules() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn = rusqlite::Connection::open(&path).unwrap();
        let files = crate::data::client_files::ClientFileManager::default();
        let ctx = DataLoadCtx {
            conn: &conn,
            app_path: std::path::Path::new("."),
            files: &files,
            language: "en_us",
        };
        let d = ScheduleData::load(&ctx).unwrap();
        let count = |t: &str| -> usize {
            conn.query_row(&format!("SELECT COUNT(*) FROM {t}"), [], |r| {
                r.get::<_, i64>(0)
            })
            .unwrap() as usize
        };
        assert_eq!(d.game_schedules.len(), count("game_schedules"));
        assert_eq!(
            d.game_schedule_spawners.len(),
            count("game_schedule_spawners")
        );
        assert_eq!(
            d.game_schedule_doodads.len(),
            count("game_schedule_doodads")
        );
        assert_eq!(d.game_schedule_quests.len(), count("game_schedule_quests"));
        // Every cron string the spawners would build, and every status, at a fixed time.
        let now = ts("2026-10-12 10:00:00");
        let mut statuses = HashMap::new();
        let mut parsable = 0;
        for g in &d.game_schedule_spawners {
            *statuses
                .entry(format!("{:?}", d.period_status_npc(g.spawner_id, now)))
                .or_insert(0) += 1;
            for start in [true, false] {
                let cron = d.cron_remaining_time(g.spawner_id, start);
                if CronSchedule::parse(&cron).is_ok() {
                    parsable += 1;
                }
            }
        }
        for g in &d.game_schedule_doodads {
            assert_ne!(
                d.period_status_doodad(g.doodad_id, now),
                PeriodStatus::NotFound
            );
        }
        eprintln!(
            "schedules: {} / spawners {} / doodads {} / quests {}; npc statuses {statuses:?}; \
             parsable spawner crons {parsable}",
            d.game_schedules.len(),
            d.game_schedule_spawners.len(),
            d.game_schedule_doodads.len(),
            d.game_schedule_quests.len()
        );
        assert!(!statuses.contains_key("NotFound"));
    }
}
