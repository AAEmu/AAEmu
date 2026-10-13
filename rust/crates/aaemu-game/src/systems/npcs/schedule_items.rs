//! `ScheduleItemGameData` (`GameData/ScheduleItemGameData.cs`) and the rules of
//! `Models/Game/ScheduleItems/{ScheduleItemDef,ScheduleItemRules}.cs`: the HUD event rows of
//! `schedule_items` (the bottom-bar timer buttons), their authored UTC windows, the claim
//! eligibility of every row kind and the elapsed-online accumulation rules.
//!
//! 1.2 differences from the 10.x reference: the table has `premium_grade_id` where the 10.x
//! upstream has `kind_value`, it carries the `show_wherever` / `show_whenever` / `icon_path` /
//! `*_key_string` UI columns and it has no `mail_title` / `mail_body`. The windows are authored as
//! UTC stamps (`ScheduleItemRules.MakeLocalStamp` returns `DateTimeKind.Utc`); they are kept as
//! authored, so an expired event never becomes active again. [`AuthoredWindow`] resolves them: a
//! side whose year / month / day are not all positive is missing (unbounded, the authored
//! semantics), while a positive but impossible date or an inverted / empty pair disables the row.
//!
//! The HUD snapshot's wire format (`SCScheduleItemUpdatePacket` in `packets::g2c_misc`, opcode
//! `0x1eb`, verified against the 1.2 client through a local proxy) is a `<u8 count>` followed by
//! `count` rows of `<u32 type, u8 gave, i32 cumulated, i64 updated>` ([`ROW_BYTES`] = 17); the
//! serializer caps the list at [`MAX_ITEMS_IN_PACKET`] rows.

use std::collections::HashMap;

use chrono::NaiveDate;

use crate::data::sqlite::for_each_row;
use crate::systems::DataLoadCtx;

/// `ScheduleItemRules.MaxItemsInPacket`: the client reads a `u8` count and its HUD list stops at
/// 32 rows. The serializer truncates anything past it.
pub const MAX_ITEMS_IN_PACKET: usize = 32;

/// One wire row: `type` (4) + `gave` (1) + `cumulated` (4) + `updated` (8). The 1.2 model writes
/// an `int` cumulated where the 10.x `ScheduleItem` writes a `long`, so the 10.x
/// `ScheduleItemRules.RowBytes` (21) does not apply here: the verified 1.2 body
/// `<u8 count, u32 type, u8 gave, i32 cumulated, i64 updated>` is 18 bytes for one row, i.e. 17
/// bytes per row plus the count.
pub const ROW_BYTES: usize = 17;

/// `ScheduleItemRules.BodyBytes(count)`.
pub fn body_bytes(count: usize) -> usize {
    1 + count.min(MAX_ITEMS_IN_PACKET) * ROW_BYTES
}

/// `enum_schedule_item_kinds`: who may see and take a HUD timer row.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum ScheduleItemKind {
    /// 1: every character.
    #[default]
    Every,
    /// 2: PC-bang only.
    PcBang,
    /// 3: premium / ArcheLife only.
    Premium,
    /// 4: free accounts only (not in the 1.2 table).
    Free,
    /// 5: accounts holding the `premium_grade_id` membership (not in the 1.2 table).
    AccountBuff,
    /// A `kind_id` this build does not know (kept as authored).
    Unknown(i32),
}

impl ScheduleItemKind {
    /// Maps the authored `kind_id`; unknown ids stay readable instead of failing the load.
    pub fn from_id(id: i32) -> Self {
        match id {
            1 => Self::Every,
            2 => Self::PcBang,
            3 => Self::Premium,
            4 => Self::Free,
            5 => Self::AccountBuff,
            other => Self::Unknown(other),
        }
    }

    /// The authored `kind_id`.
    pub fn id(self) -> i32 {
        match self {
            Self::Every => 1,
            Self::PcBang => 2,
            Self::Premium => 3,
            Self::Free => 4,
            Self::AccountBuff => 5,
            Self::Unknown(id) => id,
        }
    }
}

/// One authored side of a window ([`ScheduleItemDef::window`]).
enum Bound {
    /// Year / month / day <= 0: the C# `MakeLocalStamp` returns null, so the side is unbounded.
    Missing,
    /// A valid UTC stamp.
    Stamp(i64),
    /// A positive but impossible calendar date (e.g. 30 February): the window is invalid.
    Malformed,
}

/// The authored window of a row, resolved from the `St*` / `Ed*` columns. Missing sides keep the
/// C# "unbounded" meaning; an invalid window is never on air, so the row can never be available
/// while the row itself stays loaded with its authored values.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AuthoredWindow {
    /// Neither side authored: always inside the window.
    Always,
    /// Only a start authored: open-ended from `start`.
    From(i64),
    /// Only an end authored: open until `end`.
    Until(i64),
    /// Both sides authored and ordered (`start < end`).
    Between { start: i64, end: i64 },
    /// A positive but impossible date, or `start >= end`: nothing can ever be taken.
    Invalid,
}

/// Authored window state at a fixed time (`is_on_air` is the strict boolean form).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WindowStatus {
    /// No start and no end: the row is always inside its window.
    Always,
    /// The authored start is at or after `now`.
    NotStarted,
    /// Inside both authored bounds.
    InProgress,
    /// The authored end is at or before `now`.
    Ended,
    /// A positive but impossible date, or an inverted / empty window: never available.
    Invalid,
}

/// Evidence the claim path has about the character / account. [`ScheduleEligibility::default`]
/// means "nothing known", which is the conservative state: only [`ScheduleItemKind::Every`] rows
/// are eligible.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ScheduleEligibility {
    /// Paid patron (10.x `AccountPatronRules.IsPaidPatron`: ancient / advanced membership or a
    /// graded premium). No 1.2 evidence source is wired yet.
    pub paid_patron: bool,
    /// The character's premium grade (`premium_grades.id`); 0 = unknown.
    pub premium_grade_id: i32,
    /// PC-bang membership; no 1.2 evidence source is wired yet, so callers pass false.
    pub pc_bang: bool,
}

/// `ScheduleItemDef` (one `schedule_items` row).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct ScheduleItemDef {
    pub id: i32,
    /// `name` (raw column, like the 10.x loader; `localized_texts` holds a copy per language).
    pub name: String,
    /// `kind_id` ([`ScheduleItemKind`]).
    pub kind: ScheduleItemKind,
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
    /// `give_term` in minutes (`ScheduleItemRules.SecondsForTerm`).
    pub give_term: i32,
    pub give_max: i32,
    pub item_id: u32,
    pub item_count: i32,
    /// 1.2 `premium_grade_id` (the 10.x `kind_value`); 0 = any grade.
    pub premium_grade_id: i32,
    /// `active_take`: the HUD row has a take button (`GameData.ActiveOnAir`).
    pub active_take: bool,
    /// `on_air`: the event row is enabled (`GameData.ActiveOnAir` / `SilentOnAir`).
    pub on_air: bool,
    pub tool_tip: String,
    pub show_wherever: bool,
    pub show_whenever: bool,
    pub icon_path: String,
    pub enable_key_string: String,
    pub disable_key_string: String,
    pub label_key_string: String,
}

impl ScheduleItemDef {
    /// Resolves the authored window (`ScheduleItemRules.MakeLocalStamp` plus
    /// `IsOnAir`'s bound ordering). A side whose year / month / day are not all positive is
    /// missing / unbounded; a positive but impossible date and an inverted or empty
    /// (`start >= end`) pair report [`AuthoredWindow::Invalid`] instead of silently becoming
    /// unbounded.
    pub fn window(&self) -> AuthoredWindow {
        let start = parse_bound(
            self.st_year,
            self.st_month,
            self.st_day,
            self.st_hour,
            self.st_min,
        );
        let end = parse_bound(
            self.ed_year,
            self.ed_month,
            self.ed_day,
            self.ed_hour,
            self.ed_min,
        );
        match (start, end) {
            (Bound::Malformed, _) | (_, Bound::Malformed) => AuthoredWindow::Invalid,
            (Bound::Missing, Bound::Missing) => AuthoredWindow::Always,
            (Bound::Stamp(start), Bound::Missing) => AuthoredWindow::From(start),
            (Bound::Missing, Bound::Stamp(end)) => AuthoredWindow::Until(end),
            (Bound::Stamp(start), Bound::Stamp(end)) => {
                if start < end {
                    AuthoredWindow::Between { start, end }
                } else {
                    AuthoredWindow::Invalid
                }
            }
        }
    }

    /// `ScheduleItemRules.IsOnAir` at `now` (unix seconds UTC): strict bounds, so the exact start
    /// and end seconds are outside the window. A missing bound is unbounded; an invalid window is
    /// never on air.
    pub fn is_on_air(&self, now: i64) -> bool {
        match self.window() {
            AuthoredWindow::Always => true,
            AuthoredWindow::From(start) => start < now,
            AuthoredWindow::Until(end) => end > now,
            AuthoredWindow::Between { start, end } => start < now && end > now,
            AuthoredWindow::Invalid => false,
        }
    }

    /// The authored window at `now`. Invalid windows report [`WindowStatus::Invalid`], so they are
    /// disabled consistently with [`Self::is_on_air`].
    pub fn window_status(&self, now: i64) -> WindowStatus {
        match self.window() {
            AuthoredWindow::Always => WindowStatus::Always,
            AuthoredWindow::Invalid => WindowStatus::Invalid,
            AuthoredWindow::From(start) => {
                if start < now {
                    WindowStatus::InProgress
                } else {
                    WindowStatus::NotStarted
                }
            }
            AuthoredWindow::Until(end) => {
                if end > now {
                    WindowStatus::InProgress
                } else {
                    WindowStatus::Ended
                }
            }
            AuthoredWindow::Between { start, end } => {
                if start >= now {
                    WindowStatus::NotStarted
                } else if end <= now {
                    WindowStatus::Ended
                } else {
                    WindowStatus::InProgress
                }
            }
        }
    }

    /// Whether the row has a real grant (`GameData.ActiveOnAir` / `SilentOnAir` guard).
    pub fn has_grant(&self) -> bool {
        self.item_id > 0 && self.item_count > 0
    }

    /// `AccountPatronRules.IsScheduleEligible` reduced to the 1.2 columns. Kind 1 (`Every`) is
    /// always eligible; 2 (`PcBang`) and 3 (`Premium`) need explicit evidence, and the 1.2 table
    /// has no kind 4 / 5 row to guess about, so those stay not eligible.
    pub fn is_eligible(&self, evidence: &ScheduleEligibility) -> bool {
        match self.kind {
            ScheduleItemKind::Every => true,
            ScheduleItemKind::PcBang => evidence.pc_bang,
            ScheduleItemKind::Premium => {
                evidence.paid_patron
                    && (self.premium_grade_id <= 0
                        || self.premium_grade_id == evidence.premium_grade_id)
            }
            ScheduleItemKind::Free
            | ScheduleItemKind::AccountBuff
            | ScheduleItemKind::Unknown(_) => false,
        }
    }

    /// The manager's pure row filter: enabled, granting, inside a valid authored window and
    /// eligible. An invalid window is never on air, so it is never available.
    pub fn is_available(&self, now: i64, evidence: &ScheduleEligibility) -> bool {
        self.on_air && self.has_grant() && self.is_on_air(now) && self.is_eligible(evidence)
    }
}

/// `ScheduleItemRules.MakeLocalStamp`: the authored fields are UTC stamps; year / month / day
/// <= 0 means "not specified" (unbounded), a positive but impossible date is [`Bound::Malformed`].
/// Hour and minute are clamped like the C#.
fn parse_bound(year: i32, month: i32, day: i32, hour: i32, minute: i32) -> Bound {
    if year <= 0 || month <= 0 || day <= 0 {
        return Bound::Missing;
    }
    let Some(date) = NaiveDate::from_ymd_opt(year, month as u32, day as u32) else {
        return Bound::Malformed;
    };
    match date.and_hms_opt(hour.clamp(0, 23) as u32, minute.clamp(0, 59) as u32, 0) {
        Some(stamp) => Bound::Stamp(stamp.and_utc().timestamp()),
        None => Bound::Malformed,
    }
}

/// `ScheduleItemRules.SecondsForTerm(giveTermMinutes)`.
pub fn seconds_for_term(give_term_minutes: i32) -> i64 {
    i64::from(give_term_minutes.max(0)) * 60
}

/// `ScheduleItemRules.CanTake(gave, giveMax, cumulated, giveTermMinutes)`: the row may pay once
/// `gave < give_max` and a full term of online seconds has accumulated.
pub fn can_take(gave: u8, give_max: i32, cumulated: i64, give_term_minutes: i32) -> bool {
    i64::from(gave) < i64::from(give_max) && cumulated >= seconds_for_term(give_term_minutes)
}

/// `ScheduleItemRules.TickCumulated(cumulated, giveTermMinutes, addSeconds)`: clamped to
/// `0 ..= term`, and a term of 0 never accumulates.
pub fn tick_cumulated(cumulated: i64, give_term_minutes: i32, add_seconds: i64) -> i64 {
    let cap = seconds_for_term(give_term_minutes);
    if cap <= 0 {
        return 0;
    }
    cumulated.saturating_add(add_seconds).clamp(0, cap)
}

/// `ScheduleItemRules.HasSessionTick(lastOnlineTick)`: 0 = the character is not in-world (or has
/// not been sampled yet).
pub fn has_session_tick(last_online_tick: i64) -> bool {
    last_online_tick != 0
}

/// `ScheduleItemRules.SessionAddSeconds(lastOnlineTick, now)`: playtime between two in-world
/// samples. A cleared session tick (logout) adds nothing, so a reconnect cannot count the offline
/// gap.
pub fn session_add_seconds(last_online_tick: i64, now: i64) -> i64 {
    if !has_session_tick(last_online_tick) {
        return 0;
    }
    now.saturating_sub(last_online_tick).max(0)
}

/// `ScheduleItemGameData` (`_items`).
#[derive(Debug, Default)]
pub struct ScheduleItemData {
    /// Rows in table order (`_items`).
    pub items: Vec<ScheduleItemDef>,
    /// `id -> items` index keeping the first row, like `Get(id)` (`FirstOrDefault`).
    by_id: HashMap<i32, usize>,
}

impl ScheduleItemData {
    /// `ScheduleItemGameData.Load` (`SELECT ... FROM schedule_items`).
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        let mut data = ScheduleItemData::default();
        for_each_row(ctx.conn, "SELECT * FROM schedule_items", [], |r| {
            let def = ScheduleItemDef {
                id: r.i32("id")?,
                name: r.string_or("name", "")?,
                kind: ScheduleItemKind::from_id(r.i32("kind_id")?),
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
                give_term: r.i32("give_term")?,
                give_max: r.i32("give_max")?,
                item_id: r.u32("item_id")?,
                item_count: r.i32("item_count")?,
                premium_grade_id: r.i32("premium_grade_id")?,
                active_take: r.bool_str("active_take")?,
                on_air: r.bool_str("on_air")?,
                tool_tip: r.string_or("tool_tip", "")?,
                show_wherever: r.bool_str("show_wherever")?,
                show_whenever: r.bool_str("show_whenever")?,
                icon_path: r.string_or("icon_path", "")?,
                enable_key_string: r.string_or("enable_key_string", "")?,
                disable_key_string: r.string_or("disable_key_string", "")?,
                label_key_string: r.string_or("label_key_string", "")?,
            };
            let (id, index) = (def.id, data.items.len());
            data.items.push(def);
            data.by_id.entry(id).or_insert(index);
            Ok(())
        })?;
        tracing::info!(
            "Loaded {} schedule items ({} on-air active-take)",
            data.items.len(),
            data.items
                .iter()
                .filter(|x| x.on_air && x.active_take)
                .count()
        );
        Ok(data)
    }

    /// Builds the data from rows (tests / synthetic data).
    pub fn from_rows(items: Vec<ScheduleItemDef>) -> Self {
        let mut data = ScheduleItemData::default();
        for def in items {
            let (id, index) = (def.id, data.items.len());
            data.items.push(def);
            data.by_id.entry(id).or_insert(index);
        }
        data
    }

    /// `ScheduleItemGameData.Get(id)` (the first row with that id).
    pub fn get(&self, id: i32) -> Option<&ScheduleItemDef> {
        self.by_id.get(&id).and_then(|index| self.items.get(*index))
    }

    /// `ScheduleItemGameData.ActiveOnAir(utcNow)`: the HUD list rows (take button, real grant,
    /// inside the authored window). An expired window is never included.
    pub fn active_on_air(&self, now: i64) -> Vec<&ScheduleItemDef> {
        self.items
            .iter()
            .filter(|x| x.on_air && x.active_take && x.has_grant() && x.is_on_air(now))
            .collect()
    }

    /// `ScheduleItemGameData.SilentOnAir(utcNow)`: on-air rows without a take button.
    pub fn silent_on_air(&self, now: i64) -> Vec<&ScheduleItemDef> {
        self.items
            .iter()
            .filter(|x| x.on_air && !x.active_take && x.has_grant() && x.is_on_air(now))
            .collect()
    }

    pub fn len(&self) -> usize {
        self.items.len()
    }

    pub fn is_empty(&self) -> bool {
        self.items.is_empty()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::data::client_files::ClientFileManager;

    fn ts(s: &str) -> i64 {
        chrono::NaiveDateTime::parse_from_str(s, "%Y-%m-%d %H:%M:%S")
            .unwrap()
            .and_utc()
            .timestamp()
    }

    /// A plain kind-1 row with a real grant.
    fn def(id: i32) -> ScheduleItemDef {
        ScheduleItemDef {
            id,
            kind: ScheduleItemKind::Every,
            item_id: 1,
            item_count: 1,
            give_max: 1,
            ..Default::default()
        }
    }

    /// The 1.2 `schedule_items` columns (values are read by name, `SELECT *`).
    const SCHEMA: &str = "CREATE TABLE schedule_items (
        id int PRIMARY KEY, name TEXT, kind_id INT,
        st_year INT, st_month INT, st_day INT, st_hour INT, st_min INT,
        ed_year INT, ed_month INT, ed_day INT, ed_hour INT, ed_min INT,
        give_term INT, give_max INT, item_id INT, item_count INT, premium_grade_id INT,
        active_take NUM, on_air NUM, tool_tip TEXT, show_wherever NUM, show_whenever NUM,
        icon_path TEXT, enable_key_string TEXT, disable_key_string TEXT, label_key_string TEXT)";

    /// Loads `conn` through the real loader (the `ClientFileManager` is unused by this table).
    fn load(conn: &rusqlite::Connection) -> ScheduleItemData {
        let files = ClientFileManager::default();
        let ctx = DataLoadCtx {
            conn,
            app_path: std::path::Path::new("."),
            files: &files,
            language: "en_us",
        };
        ScheduleItemData::load(&ctx).unwrap()
    }

    #[test]
    fn loader_reads_the_1_2_columns_and_bool_strings() {
        let conn = rusqlite::Connection::open_in_memory().unwrap();
        conn.execute_batch(SCHEMA).unwrap();
        // Row 28-shaped row: text 't' / 'f' flags, real window and UI metadata.
        conn.execute(
            "INSERT INTO schedule_items VALUES (
                28, 'row', 1, 2013, 11, 27, 5, 59, 2013, 12, 18, 5, 59, 30, 10, 31198, 1, 1,
                't', 't', 'tip', 'f', 't', 'icon', 'enable', 'disable', 'label')",
            [],
        )
        .unwrap();
        // Unknown kind, NULL strings and flags, integer bool, an unbounded window.
        conn.execute(
            "INSERT INTO schedule_items VALUES (
                7, NULL, 9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, 0, 2,
                1, 'f', NULL, NULL, NULL, NULL, NULL, NULL, NULL)",
            [],
        )
        .unwrap();

        let d = load(&conn);
        assert_eq!(d.len(), 2);
        assert!(!d.is_empty());

        let row = d.get(28).unwrap();
        assert_eq!(row.name, "row");
        assert_eq!(row.kind, ScheduleItemKind::Every);
        assert_eq!(row.kind.id(), 1);
        assert_eq!(
            row.window(),
            AuthoredWindow::Between {
                start: ts("2013-11-27 05:59:00"),
                end: ts("2013-12-18 05:59:00"),
            }
        );
        assert_eq!(row.give_term, 30);
        assert_eq!(row.give_max, 10);
        assert_eq!(row.item_id, 31198);
        assert_eq!(row.item_count, 1);
        assert_eq!(row.premium_grade_id, 1);
        assert!(row.active_take);
        assert!(row.on_air);
        assert_eq!(row.tool_tip, "tip");
        assert!(!row.show_wherever);
        assert!(row.show_whenever);
        assert_eq!(row.icon_path, "icon");
        assert_eq!(row.enable_key_string, "enable");
        assert_eq!(row.disable_key_string, "disable");
        assert_eq!(row.label_key_string, "label");

        // NULL strings fall back to "", NULL / real flags to false, integer 1 to true.
        let odd = d.get(7).unwrap();
        assert_eq!(odd.name, "");
        assert_eq!(odd.tool_tip, "");
        assert_eq!(odd.kind, ScheduleItemKind::Unknown(9));
        assert_eq!(odd.kind.id(), 9);
        assert_eq!(odd.window(), AuthoredWindow::Always);
        assert_eq!(
            odd.window_status(ts("2026-01-01 00:00:00")),
            WindowStatus::Always
        );
        assert!(odd.active_take);
        assert!(!odd.on_air);
        assert!(!odd.show_wherever);
        assert!(!odd.show_whenever);
        assert!(!odd.has_grant());

        assert_eq!(d.get(28).unwrap().name, "row");
        assert!(d.get(29).is_none());
    }

    #[test]
    fn windows_are_strict_and_expired_events_stay_closed() {
        let mut row = def(28);
        row.st_year = 2013;
        row.st_month = 11;
        row.st_day = 27;
        row.st_hour = 5;
        row.st_min = 59;
        row.ed_year = 2013;
        row.ed_month = 12;
        row.ed_day = 18;
        row.ed_hour = 5;
        row.ed_min = 59;
        let start = ts("2013-11-27 05:59:00");
        let end = ts("2013-12-18 05:59:00");
        assert_eq!(row.window(), AuthoredWindow::Between { start, end });
        // Strict bounds: the start and end seconds themselves are outside the window.
        assert_eq!(row.window_status(start), WindowStatus::NotStarted);
        assert!(!row.is_on_air(start));
        assert!(row.is_on_air(start + 1));
        assert_eq!(row.window_status(start + 1), WindowStatus::InProgress);
        assert!(row.is_on_air(end - 1));
        assert_eq!(row.window_status(end), WindowStatus::Ended);
        assert!(!row.is_on_air(end));
        // Expired: stays closed and is never reactivated by a later `now`.
        let now = ts("2026-01-01 00:00:00");
        assert_eq!(row.window_status(now), WindowStatus::Ended);
        assert!(!row.is_on_air(now));
        assert!(!row.is_available(now, &ScheduleEligibility::default()));

        // Start only: open-ended from the authored start.
        let mut open_end = row.clone();
        open_end.ed_year = 0;
        open_end.ed_month = 0;
        open_end.ed_day = 0;
        open_end.ed_hour = 0;
        open_end.ed_min = 0;
        assert_eq!(open_end.window(), AuthoredWindow::From(start));
        assert!(!open_end.is_on_air(start));
        assert!(open_end.is_on_air(start + 1));
        assert_eq!(open_end.window_status(now), WindowStatus::InProgress);

        // End only: open from the beginning until the authored end.
        let mut open_start = row.clone();
        open_start.st_year = 0;
        open_start.st_month = 0;
        open_start.st_day = 0;
        open_start.st_hour = 0;
        open_start.st_min = 0;
        assert_eq!(open_start.window(), AuthoredWindow::Until(end));
        assert!(open_start.is_on_air(start));
        assert!(open_start.is_on_air(end - 1));
        assert!(!open_start.is_on_air(end));
        assert_eq!(open_start.window_status(now), WindowStatus::Ended);

        // No bounds at all: always inside the window.
        let mut always = row.clone();
        always.st_year = 0;
        always.st_month = 0;
        always.st_day = 0;
        always.ed_year = 0;
        always.ed_month = 0;
        always.ed_day = 0;
        assert_eq!(always.window(), AuthoredWindow::Always);
        assert!(always.is_on_air(now));
        assert_eq!(always.window_status(now), WindowStatus::Always);
    }

    #[test]
    fn malformed_and_inverted_windows_are_disabled() {
        let start = ts("2013-11-27 05:59:00");
        let end = ts("2013-12-18 05:59:00");
        let now = ts("2013-12-10 00:00:00");

        // A positive but impossible start date (30 February) is not "unbounded": the whole
        // window is invalid, even while `now` would be inside the end side.
        let mut malformed_start = def(1);
        malformed_start.st_year = 2013;
        malformed_start.st_month = 2;
        malformed_start.st_day = 30;
        malformed_start.ed_year = 2013;
        malformed_start.ed_month = 12;
        malformed_start.ed_day = 18;
        assert_eq!(malformed_start.window(), AuthoredWindow::Invalid);
        assert!(!malformed_start.is_on_air(now));
        assert_eq!(malformed_start.window_status(now), WindowStatus::Invalid);

        // Month 13, day 32 and 29 February of a non-leap year are malformed too.
        for (month, day) in [(13, 1), (1, 32), (2, 29)] {
            let mut malformed = malformed_start.clone();
            malformed.st_month = month;
            malformed.st_day = day;
            assert_eq!(malformed.window(), AuthoredWindow::Invalid, "{month}-{day}");
        }

        // The same 29 February in a leap year is a valid window.
        let mut leap = malformed_start.clone();
        leap.st_year = 2016;
        leap.st_month = 2;
        leap.st_day = 29;
        leap.ed_year = 2016;
        leap.ed_month = 3;
        leap.ed_day = 1;
        assert_eq!(
            leap.window(),
            AuthoredWindow::Between {
                start: ts("2016-02-29 00:00:00"),
                end: ts("2016-03-01 00:00:00"),
            }
        );

        // A malformed end date disables the row as well.
        let mut malformed_end = def(2);
        malformed_end.st_year = 2013;
        malformed_end.st_month = 11;
        malformed_end.st_day = 27;
        malformed_end.ed_year = 2013;
        malformed_end.ed_month = 13;
        malformed_end.ed_day = 1;
        assert_eq!(malformed_end.window(), AuthoredWindow::Invalid);
        assert!(!malformed_end.is_on_air(now));
        assert_eq!(
            malformed_end.window_status(start + 1),
            WindowStatus::Invalid
        );

        // Inverted (`start > end`) and empty (`start == end`) windows are invalid too.
        let mut inverted = def(3);
        inverted.st_year = 2013;
        inverted.st_month = 12;
        inverted.st_day = 18;
        inverted.ed_year = 2013;
        inverted.ed_month = 11;
        inverted.ed_day = 27;
        assert_eq!(inverted.window(), AuthoredWindow::Invalid);
        assert!(!inverted.is_on_air(now));
        assert_eq!(inverted.window_status(now), WindowStatus::Invalid);

        let mut empty = def(4);
        empty.st_year = 2013;
        empty.st_month = 11;
        empty.st_day = 27;
        empty.ed_year = 2013;
        empty.ed_month = 11;
        empty.ed_day = 27;
        assert_eq!(empty.window(), AuthoredWindow::Invalid);
        assert!(!empty.is_on_air(start));

        // A partial triple (positive year, zero month) is still the authored "missing" side.
        let mut partial = def(5);
        partial.st_year = 2013;
        partial.st_month = 0;
        partial.st_day = 27;
        partial.ed_year = 2013;
        partial.ed_month = 12;
        partial.ed_day = 18;
        partial.ed_hour = 5;
        partial.ed_min = 59;
        assert_eq!(partial.window(), AuthoredWindow::Until(end));

        // A malformed row stays loaded but is excluded from every window-driven list, even with
        // the enabled / take flags and a real grant.
        let mut malformed = def(6);
        malformed.st_year = 2013;
        malformed.st_month = 2;
        malformed.st_day = 30;
        malformed.ed_year = 2013;
        malformed.ed_month = 12;
        malformed.ed_day = 18;
        malformed.on_air = true;
        malformed.active_take = true;
        let d = ScheduleItemData::from_rows(vec![malformed, def(7)]);
        assert_eq!(d.len(), 2);
        assert!(d.active_on_air(start).is_empty());
        assert!(d.silent_on_air(start).is_empty());
        let row = d.get(6).unwrap();
        assert!(!row.is_available(now, &ScheduleEligibility::default()));
        assert!(!row.is_available(start + 1, &ScheduleEligibility::default()));
    }

    #[test]
    fn on_air_filters_need_the_flags_a_grant_and_an_open_window() {
        /// 27 Nov - 18 Dec of the `start_year` / `end_year` pair.
        fn windowed(row: &mut ScheduleItemDef, start_year: i32, end_year: i32) {
            row.st_year = start_year;
            row.st_month = 11;
            row.st_day = 27;
            row.ed_year = end_year;
            row.ed_month = 12;
            row.ed_day = 18;
        }

        let now = ts("2013-12-10 00:00:00");
        let mut hud = def(1);
        windowed(&mut hud, 2013, 2013);
        hud.on_air = true;
        hud.active_take = true;
        let mut silent = def(2);
        windowed(&mut silent, 2013, 2013);
        silent.on_air = true;
        let mut expired = def(3);
        windowed(&mut expired, 2012, 2012);
        expired.on_air = true;
        expired.active_take = true;
        let mut no_item = def(4);
        windowed(&mut no_item, 2013, 2013);
        no_item.on_air = true;
        no_item.active_take = true;
        no_item.item_id = 0;
        let mut off_air = def(5);
        windowed(&mut off_air, 2013, 2013);
        off_air.active_take = true; // on_air stays false

        let d = ScheduleItemData::from_rows(vec![hud, silent, expired, no_item, off_air]);
        let none = ScheduleEligibility::default();
        let ids = |v: Vec<&ScheduleItemDef>| v.iter().map(|x| x.id).collect::<Vec<_>>();
        let available = |id: i32| d.get(id).unwrap().is_available(now, &none);
        assert_eq!(ids(d.active_on_air(now)), vec![1]);
        assert_eq!(ids(d.silent_on_air(now)), vec![2]);
        // The HUD row is available inside its window; the expired event never is.
        assert!(available(1));
        assert!(!available(3));
        assert!(!available(4));
        assert!(!available(5));
        // The expired fixture row was closed before the current time.
        assert!(!d.get(3).unwrap().is_on_air(now));
        // Every authored window in this fixture is over in 2026.
        assert!(d.active_on_air(ts("2026-01-01 00:00:00")).is_empty());
        assert!(d.silent_on_air(ts("2026-01-01 00:00:00")).is_empty());
    }

    #[test]
    fn eligibility_needs_evidence_for_pc_bang_and_premium() {
        let mut pc_bang = def(2);
        pc_bang.kind = ScheduleItemKind::PcBang;
        let mut premium = def(3);
        premium.kind = ScheduleItemKind::Premium;
        premium.premium_grade_id = 2;
        let mut free = def(4);
        free.kind = ScheduleItemKind::Free;
        let mut account_buff = def(5);
        account_buff.kind = ScheduleItemKind::AccountBuff;
        let mut unknown = def(6);
        unknown.kind = ScheduleItemKind::Unknown(9);

        let none = ScheduleEligibility::default();
        assert!(def(1).is_eligible(&none));
        assert!(!pc_bang.is_eligible(&none));
        assert!(!premium.is_eligible(&none));
        assert!(!free.is_eligible(&none));
        assert!(!account_buff.is_eligible(&none));
        assert!(!unknown.is_eligible(&none));

        let pc_bang_evidence = ScheduleEligibility {
            pc_bang: true,
            ..none
        };
        assert!(pc_bang.is_eligible(&pc_bang_evidence));
        assert!(!premium.is_eligible(&pc_bang_evidence));

        // Premium needs a paid patron and (unless the row says 0 = any) the matching grade.
        let paid = |premium_grade_id| ScheduleEligibility {
            paid_patron: true,
            premium_grade_id,
            pc_bang: false,
        };
        assert!(!premium.is_eligible(&paid(0)));
        assert!(!premium.is_eligible(&paid(5)));
        assert!(premium.is_eligible(&paid(2)));
        let mut any_grade = premium.clone();
        any_grade.premium_grade_id = 0;
        assert!(any_grade.is_eligible(&paid(7)));
        assert!(!any_grade.is_eligible(&none));
    }

    #[test]
    fn accumulation_counts_online_time_only_and_stops_at_the_term_cap() {
        // `give_term` is minutes.
        assert_eq!(seconds_for_term(30), 1800);
        assert_eq!(seconds_for_term(0), 0);
        assert_eq!(seconds_for_term(-5), 0);

        // A logout clears the session tick, so the offline gap adds nothing.
        assert!(!has_session_tick(0));
        assert!(has_session_tick(1_000_000));
        assert_eq!(session_add_seconds(0, 1_000_000), 0);
        assert_eq!(session_add_seconds(1_000_000, 1_000_060), 60);
        // A clock that moves backwards never subtracts.
        assert_eq!(session_add_seconds(1_000_060, 1_000_000), 0);

        // Accumulation: clamped into 0 ..= term, term 0 never accumulates.
        assert_eq!(tick_cumulated(0, 30, 60), 60);
        assert_eq!(tick_cumulated(60, 30, 60), 120);
        assert_eq!(tick_cumulated(1799, 30, 60), 1800);
        assert_eq!(tick_cumulated(0, 30, 99_999), 1800);
        assert_eq!(tick_cumulated(0, 30, -10), 0);
        assert_eq!(tick_cumulated(500, 0, 60), 0);

        // Take: `gave < give_max` with a full term accumulated.
        assert!(!can_take(0, 10, 1799, 30));
        assert!(can_take(0, 10, 1800, 30));
        assert!(can_take(0, 10, 3600, 30));
        assert!(!can_take(10, 10, 1800, 30));
        assert!(!can_take(0, 0, 3600, 30));
        // give_term 0 pays as soon as a grant is left (mirrors the C#).
        assert!(can_take(0, 1, 0, 0));
    }

    #[test]
    fn packet_row_geometry_matches_the_verified_client() {
        assert_eq!(MAX_ITEMS_IN_PACKET, 32);
        // type 4 + gave 1 + cumulated 4 + updated 8, i.e. 17 bytes per row.
        assert_eq!(ROW_BYTES, 17);
        assert_eq!(body_bytes(0), 1);
        assert_eq!(body_bytes(1), 18);
        assert_eq!(body_bytes(32), 545);
        assert_eq!(body_bytes(40), 545);
    }

    #[test]
    fn get_keeps_the_first_row_of_a_duplicate_id() {
        let mut first = def(28);
        first.give_max = 10;
        let mut second = def(28);
        second.give_max = 99;
        let d = ScheduleItemData::from_rows(vec![first, second]);
        assert_eq!(d.len(), 2);
        assert_eq!(d.items[1].give_max, 99);
        assert_eq!(d.get(28).unwrap().give_max, 10);
    }

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_compact_db_schedule_items() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn = rusqlite::Connection::open(&path).unwrap();
        let d = load(&conn);
        let count: i64 = conn
            .query_row("SELECT COUNT(*) FROM schedule_items", [], |r| r.get(0))
            .unwrap();
        assert_eq!(d.len(), count as usize);
        assert_eq!(d.len(), 32);
        assert_eq!(d.items.iter().filter(|x| x.on_air).count(), 8);
        assert_eq!(
            d.items
                .iter()
                .filter(|x| x.kind == ScheduleItemKind::Every)
                .count(),
            18
        );
        assert_eq!(
            d.items
                .iter()
                .filter(|x| x.kind == ScheduleItemKind::PcBang)
                .count(),
            8
        );
        assert_eq!(
            d.items
                .iter()
                .filter(|x| x.kind == ScheduleItemKind::Premium)
                .count(),
            6
        );

        // Row 28 is the live-client verified HUD row (`GetScheduleItemInfo(28)`: give 0,
        // giveMax 10, giveTerm 30, activeTake false, onAir false).
        let row = d.get(28).unwrap();
        assert_eq!(row.kind, ScheduleItemKind::Every);
        assert_eq!(row.give_term, 30);
        assert_eq!(row.give_max, 10);
        assert_eq!(row.item_id, 31198);
        assert_eq!(row.item_count, 1);
        assert_eq!(row.premium_grade_id, 1);
        assert!(!row.active_take);
        assert!(!row.on_air);
        assert_eq!(
            row.window(),
            AuthoredWindow::Between {
                start: ts("2013-11-27 05:59:00"),
                end: ts("2013-12-18 05:59:00"),
            }
        );
        assert!(row.is_on_air(ts("2013-12-01 00:00:00")));
        // Every shipped row has a valid authored window (no malformed / inverted dates).
        assert!(d
            .items
            .iter()
            .all(|x| x.window() != AuthoredWindow::Invalid));
        // Authored windows are preserved: the 2013-2014 events are closed today and the rows the
        // runtime would consider are the enabled ones.
        let now = ts("2026-01-01 00:00:00");
        assert!(!row.is_on_air(now));
        assert_eq!(row.window_status(now), WindowStatus::Ended);
        assert_eq!(d.active_on_air(now).len(), 0);
        // Only row 8000001 (`VIP 서비스`) has no authored bounds, so it is the single silent row.
        let silent = d.silent_on_air(now);
        assert_eq!(silent.len(), 1);
        assert_eq!(silent[0].id, 8000001);
        assert_eq!(silent[0].window_status(now), WindowStatus::Always);
        // Row 48 is authored with `ed_year` 2222 and `on_air` off: the window stays as authored,
        // the disabled flag keeps it out of every list.
        let long = d.get(48).unwrap();
        assert!(!long.on_air);
        assert_eq!(
            long.window(),
            AuthoredWindow::Between {
                start: ts("2014-01-15 05:59:00"),
                end: ts("2222-01-15 06:00:00"),
            }
        );
        assert!(long.is_on_air(now));
        eprintln!(
            "schedule items: {} rows, {} on-air, {} kind1, {} kind2, {} kind3",
            d.len(),
            d.items.iter().filter(|x| x.on_air).count(),
            d.items
                .iter()
                .filter(|x| x.kind == ScheduleItemKind::Every)
                .count(),
            d.items
                .iter()
                .filter(|x| x.kind == ScheduleItemKind::PcBang)
                .count(),
            d.items
                .iter()
                .filter(|x| x.kind == ScheduleItemKind::Premium)
                .count()
        );
    }
}
