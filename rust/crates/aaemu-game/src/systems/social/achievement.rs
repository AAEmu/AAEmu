//! `Models/Game/Achievement` data types and enums, and `GameData/AchievementGameData` (the
//! table loader). The 1.2 C# has no achievement manager (the types are only the DB / packet
//! shapes); the S->C packets live in `packets::misc`.

use std::collections::HashMap;
use std::sync::OnceLock;

use chrono::{DateTime, Utc};
use rusqlite::Connection;

use crate::data::sqlite::for_each_row;
use crate::data::GameDataError;

/// `AchievementGameData` (`[GameData]`, `Load(SqliteConnection)`). The C# keeps the tables in
/// private dictionaries without accessors; they are public fields here.
///
/// Deviation from the 1.2 C#: the boolean columns (`complete_or`, `is_active`, `is_hidden`,
/// `or_unit_reqs`) hold the SQLite text `'t'` / `'f'` in the shipped compact.sqlite3. The C# reads
/// them with `GetBoolean(column)` (Microsoft.Data.Sqlite: `GetInt64 != 0`), which coerces the text
/// to 0 and loads every row as `false`; this loader reads them with the
/// `SQLiteWrapperReader.GetBoolean(column, fromString: true)` semantics of
/// `data::sqlite::SqlRow::bool_str` instead, like the 10.x zone loader
/// (`x2game-dev_dedicate.c` `LoadAchievementDescs`, which accepts `'T' | 't' | '1'`). The shared
/// `SqlRow::bool` accessor keeps the faithful `GetInt64 != 0` port untouched.
///
/// `Achievements::achievement_objectives` is filled from the `achievement_objectives` table
/// (the C# never populates its `AchievementObjectives` set) in ascending objective-id order; the
/// grouped [`Self::achievement_objectives`] map is kept as well.
#[derive(Debug, Clone, Default)]
pub struct AchievementGameData {
    /// `_charRecords` (`char_records` by id; `Dictionary.Add`, a duplicate id fails the load).
    pub char_records: HashMap<u32, CharRecords>,
    /// `_achievements` (`achievements` by id; `TryAdd`, the first row of an id wins).
    pub achievements: HashMap<u32, Achievements>,
    /// `_achievementObjectives` (`achievement_objectives` grouped by `achievement_id`, ascending
    /// objective `id`; the same lists as [`Achievements::achievement_objectives`]).
    pub achievement_objectives: HashMap<u32, Vec<AchievementObjectives>>,
    /// `_preCompletedAchievements` (`pre_completed_achievements` grouped by
    /// `completed_achievement_id`).
    pub pre_completed_achievements: HashMap<u32, Vec<PreCompletedAchievements>>,
    /// The derived lookups an [`AchievementIndex`](super::achievement_runtime::AchievementIndex)
    /// serves, built once on first use by [`Self::derived`] and cached for the lifetime of this
    /// value. It takes no part in equality or in the semantic meaning of a value (see the manual
    /// [`PartialEq`] below), and the tables are immutable once an index has been built over them.
    pub(crate) derived: OnceLock<AchievementDerived>,
}

impl PartialEq for AchievementGameData {
    /// Compares the loaded tables only. The derived cache is a pure function of them, so it must
    /// not make two equal values unequal: a value whose cache is warm still equals an equal clone
    /// whose cache was never built (and both still differ from any changed table).
    fn eq(&self, other: &Self) -> bool {
        self.char_records == other.char_records
            && self.achievements == other.achievements
            && self.achievement_objectives == other.achievement_objectives
            && self.pre_completed_achievements == other.pre_completed_achievements
    }
}

impl Eq for AchievementGameData {}

impl AchievementGameData {
    /// `AchievementGameData.Load(connection)`.
    pub fn load(conn: &Connection) -> Result<Self, GameDataError> {
        let mut d = Self::default();
        for_each_row(conn, "SELECT * FROM achievements", [], |r| {
            let t = Achievements {
                id: r.u32("id")?,
                category_id: r.u32_or("category_id", 0)?,
                complete_num: r.u32_or("complete_num", 0)?,
                complete_or: r.bool_str("complete_or")?,
                icon_id: r.u32_or("icon_id", 0)?,
                is_active: r.bool_str("is_active")?,
                is_hidden: r.bool_str("is_hidden")?,
                item_id: r.u32_or("item_id", 0)?,
                or_unit_reqs: r.bool_str("or_unit_reqs")?,
                parent_achievement_id: r.u32_or("parent_achievement_id", 0)?,
                priority: r.u32_or("priority", 0)?,
                sub_category_id: r.u32_or("sub_category_id", 0)?,
                ..Default::default()
            };
            d.achievements.entry(t.id).or_insert(t);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM achievement_objectives", [], |r| {
            let t = AchievementObjectives {
                id: r.u32("id")?,
                achievement_id: r.u32("achievement_id")?,
                or_unit_reqs: r.bool_str("or_unit_reqs")?,
                record_id: r.u32("record_id")?,
            };
            d.achievement_objectives
                .entry(t.achievement_id)
                .or_default()
                .push(t);
            Ok(())
        })?;
        // Link the objectives onto their `Achievements` entry and order every list by objective id:
        // `SELECT *` has no ORDER BY, so the row order of the table is not a stable order.
        for (achievement_id, objectives) in d.achievement_objectives.iter_mut() {
            objectives.sort_by_key(|o| o.id);
            if let Some(a) = d.achievements.get_mut(achievement_id) {
                a.achievement_objectives = objectives.clone();
            }
        }
        for_each_row(conn, "SELECT * FROM pre_completed_achievements", [], |r| {
            let t = PreCompletedAchievements {
                id: r.u32("id")?,
                completed_achievement_id: r.u32("completed_achievement_id")?,
                my_achievement_id: r.u32("my_achievement_id")?,
            };
            d.pre_completed_achievements
                .entry(t.completed_achievement_id)
                .or_default()
                .push(t);
            Ok(())
        })?;
        let mut duplicate = None;
        for_each_row(conn, "SELECT * FROM char_records", [], |r| {
            let t = CharRecords {
                id: r.u32("id")?,
                kind_id: r.u32("kind_id")?,
                value1: r.u32("value1")?,
                value2: r.u32("value2")?,
            };
            if d.char_records.insert(t.id, t).is_some() && duplicate.is_none() {
                duplicate = Some(t.id);
            }
            Ok(())
        })?;
        if let Some(id) = duplicate {
            return Err(GameDataError::File(format!(
                "char_records: duplicate id {id}"
            )));
        }
        Ok(d)
    }

    /// `PostLoad()` (empty in the C#).
    pub fn post_load(&mut self) {}

    /// The objective list one achievement is evaluated against: the grouped table when the loader
    /// filled it, the copy linked onto the row otherwise (hand-built data may have only one).
    pub(crate) fn objectives_for(&self, achievement_id: u32) -> &[AchievementObjectives] {
        let Some(achievement) = self.achievements.get(&achievement_id) else {
            return &[];
        };
        self.achievement_objectives
            .get(&achievement_id)
            .map(Vec::as_slice)
            .unwrap_or(achievement.achievement_objectives.as_slice())
    }

    /// The derived lookups, built once on first use and then shared by every
    /// [`AchievementIndex`](super::achievement_runtime::AchievementIndex) over this value.
    ///
    /// The cache is only valid while the tables are not mutated: build an index after the loader
    /// (or a test fixture) has finished filling the value, and do not change a value an index has
    /// been built over.
    pub(crate) fn derived(&self) -> &AchievementDerived {
        self.derived.get_or_init(|| AchievementDerived::new(self))
    }
}

/// The reverse lookups every [`AchievementIndex`](super::achievement_runtime::AchievementIndex)
/// serves, derived once from an [`AchievementGameData`] and cached on it.
///
/// Everything here is **owned**: no lookup refers to the value it was built from, so the cache is
/// not a self-referential struct and an index can hold the data reference and the cached lookups
/// side by side. The lists are in the same order and with the same contents the index built
/// per call before: ascending ids, sorted and deduplicated where the same id can be reached twice.
#[derive(Debug, Clone, Default)]
pub(crate) struct AchievementDerived {
    /// The active achievements, ascending, which is the order the client list and a full refresh
    /// walk.
    active_ids: Vec<u32>,
    /// The achievements whose objectives watch a record, ascending.
    watching_record: HashMap<u32, Vec<u32>>,
    /// What an achievement requires first: `pre_completed_achievements.my_achievement_id` rows,
    /// ascending.
    prerequisites: HashMap<u32, Vec<u32>>,
    /// What completing an achievement unlocks: `pre_completed_achievements.completed_achievement_id`
    /// rows, ascending.
    gated_by_prerequisite: HashMap<u32, Vec<u32>>,
    /// The record counting an achievement's completion, by achievement id.
    completion_record: HashMap<u32, u32>,
    /// The achievement a kind-9 record counts, by record id (the reset path reads it).
    achievement_for_completion_record: HashMap<u32, u32>,
}

impl AchievementDerived {
    /// `AchievementGameData.PostLoad`: builds the lookups from the loaded tables.
    pub(crate) fn new(data: &AchievementGameData) -> Self {
        let mut active_ids: Vec<u32> = data
            .achievements
            .values()
            .filter(|achievement| achievement.is_active)
            .map(|achievement| achievement.id)
            .collect();
        active_ids.sort_unstable();

        let mut watching_record: HashMap<u32, Vec<u32>> = HashMap::new();
        for achievement_id in data.achievements.keys() {
            for objective in data.objectives_for(*achievement_id) {
                watching_record
                    .entry(objective.record_id)
                    .or_default()
                    .push(*achievement_id);
            }
        }

        // `my_achievement_id` is the gated row; `completed_achievement_id` is what it requires
        // first (compact row 474 is workbench 2074 requiring the level-30 row 2052, not a credit
        // back onto the level row).
        let mut prerequisites: HashMap<u32, Vec<u32>> = HashMap::new();
        let mut gated_by_prerequisite: HashMap<u32, Vec<u32>> = HashMap::new();
        for rules in data.pre_completed_achievements.values() {
            for rule in rules {
                prerequisites
                    .entry(rule.my_achievement_id)
                    .or_default()
                    .push(rule.completed_achievement_id);
                gated_by_prerequisite
                    .entry(rule.completed_achievement_id)
                    .or_default()
                    .push(rule.my_achievement_id);
            }
        }

        for list in watching_record
            .values_mut()
            .chain(prerequisites.values_mut())
            .chain(gated_by_prerequisite.values_mut())
        {
            list.sort_unstable();
            list.dedup();
        }

        // The record that counts an achievement's completion: `COMPLETE_ACHIEVEMENT` (kind 9) with
        // the achievement in `value1`. The C# `TryAdd`s in row order, so the first record id wins;
        // sorting by record id makes that first-wins deterministic.
        let mut records: Vec<&CharRecords> = data.char_records.values().collect();
        records.sort_unstable_by_key(|record| record.id);
        let mut completion_record: HashMap<u32, u32> = HashMap::new();
        let mut achievement_for_completion_record: HashMap<u32, u32> = HashMap::new();
        for record in records {
            if record.kind_id != char_record_kind::COMPLETE_ACHIEVEMENT || record.value1 == 0 {
                continue;
            }
            completion_record.entry(record.value1).or_insert(record.id);
            achievement_for_completion_record
                .entry(record.id)
                .or_insert(record.value1);
        }

        Self {
            active_ids,
            watching_record,
            prerequisites,
            gated_by_prerequisite,
            completion_record,
            achievement_for_completion_record,
        }
    }

    pub(crate) fn active_ids(&self) -> &[u32] {
        &self.active_ids
    }

    pub(crate) fn watching_record(&self, record_id: u32) -> &[u32] {
        self.watching_record
            .get(&record_id)
            .map_or(&[], Vec::as_slice)
    }

    pub(crate) fn prerequisites(&self, achievement_id: u32) -> &[u32] {
        self.prerequisites
            .get(&achievement_id)
            .map_or(&[], Vec::as_slice)
    }

    pub(crate) fn gated_by_prerequisite(&self, prerequisite_id: u32) -> &[u32] {
        self.gated_by_prerequisite
            .get(&prerequisite_id)
            .map_or(&[], Vec::as_slice)
    }

    pub(crate) fn completion_record(&self, achievement_id: u32) -> Option<u32> {
        self.completion_record.get(&achievement_id).copied()
    }

    pub(crate) fn achievement_for_completion_record(&self, record_id: u32) -> Option<u32> {
        self.achievement_for_completion_record
            .get(&record_id)
            .copied()
    }
}

/// `AchievementInfo` (one entry of `SCAchievementsPacket`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct AchievementInfo {
    pub id: u32,
    pub amount: u32,
    pub complete: DateTime<Utc>,
}

/// `Achievements` (the `achievements` table).
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct Achievements {
    pub id: u32,
    pub category_id: u32,
    pub complete_num: u32,
    pub complete_or: bool,
    pub description: String,
    pub icon_id: u32,
    pub is_active: bool,
    pub is_hidden: bool,
    /// `ItemId` (`Item` navigates to the item template).
    pub item_id: u32,
    pub name: String,
    pub or_unit_reqs: bool,
    pub parent_achievement_id: u32,
    pub priority: u32,
    pub sub_category_id: u32,
    pub summary: String,
    /// `AchievementObjectives` (linked by [`AchievementGameData::load`], ascending objective `id`).
    pub achievement_objectives: Vec<AchievementObjectives>,
}

/// `AchievementObjectives` (`Achievement` back-reference by `achievement_id`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct AchievementObjectives {
    pub id: u32,
    pub achievement_id: u32,
    pub or_unit_reqs: bool,
    pub record_id: u32,
}

/// `CharRecords` (`KindId`: [`char_record_kind`]).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct CharRecords {
    pub id: u32,
    pub kind_id: u32,
    pub value1: u32,
    pub value2: u32,
}

/// `PreCompletedAchievements`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct PreCompletedAchievements {
    pub id: u32,
    pub completed_achievement_id: u32,
    pub my_achievement_id: u32,
}

/// `AchievementCategory`.
pub mod achievement_category {
    pub const INVALID: u32 = 0;
    pub const NORMAL: u32 = 1;
    pub const COMBAT: u32 = 2;
    pub const LIFE: u32 = 3;
    pub const ADVENTURE: u32 = 4;
    pub const SOCIETY: u32 = 5;
    pub const LEVEL: u32 = 6;
    pub const SEASON: u32 = 7;
    pub const EVENT: u32 = 8;
}

/// `AchievementAdventureCategory`.
pub mod achievement_adventure_category {
    pub const INVALID: u32 = 0;
    pub const QUEST: u32 = 1;
    pub const INDUN: u32 = 2;
    pub const GET: u32 = 3;
}

/// `AchievementCombatCategory`.
pub mod achievement_combat_category {
    pub const INVALID: u32 = 0;
    pub const PVP: u32 = 1;
    pub const SIEGE: u32 = 2;
    pub const BATTLE: u32 = 3;
    pub const GET: u32 = 4;
}

/// `AchievementEventCategory`.
pub mod achievement_event_category {
    pub const INVALID: u32 = 0;
    pub const EVENT: u32 = 1;
}

/// `AchievementFilter`.
pub mod achievement_filter {
    pub const INVALID: u32 = 0;
    pub const ALL: u32 = 1;
    pub const COMPLETE: u32 = 2;
    pub const UNCOMPLETE: u32 = 3;
    pub const PROGRESS: u32 = 4;
    pub const TRACING: u32 = 5;
}

/// `AchievementKind`.
pub mod achievement_kind {
    pub const GENERAL: u32 = 0;
    pub const RACIAL: u32 = 1;
}

/// `AchievementLevelCategory` (`Level_1_5` = 1 .. `Level_51_55` = 11).
pub mod achievement_level_category {
    pub const INVALID: u32 = 0;
    pub const LEVEL_1_5: u32 = 1;
    pub const LEVEL_6_10: u32 = 2;
    pub const LEVEL_11_15: u32 = 3;
    pub const LEVEL_16_20: u32 = 4;
    pub const LEVEL_21_25: u32 = 5;
    pub const LEVEL_26_30: u32 = 6;
    pub const LEVEL_31_35: u32 = 7;
    pub const LEVEL_36_40: u32 = 8;
    pub const LEVEL_41_45: u32 = 9;
    pub const LEVEL_46_50: u32 = 10;
    pub const LEVEL_51_55: u32 = 11;
}

/// `AchievementLifeCategory`.
pub mod achievement_life_category {
    pub const INVALID: u32 = 0;
    pub const MAKE: u32 = 1;
    pub const ANIMAL_HUSBANDRY: u32 = 2;
    pub const TRADE: u32 = 3;
    pub const FURNITURE: u32 = 4;
    pub const GOAL: u32 = 5;
}

/// `AchievementNormalCategory`.
pub mod achievement_normal_category {
    pub const INVALID: u32 = 0;
    pub const CHARACTER: u32 = 1;
    pub const PET: u32 = 2;
    pub const COLLECT: u32 = 3;
    pub const ETC: u32 = 4;
}

/// `AchievementSeasonCategory`.
pub mod achievement_season_category {
    pub const INVALID: u32 = 0;
    pub const SPRING: u32 = 1;
    pub const SUMMER: u32 = 2;
    pub const AUTUMN: u32 = 3;
    pub const WINTER: u32 = 4;
}

/// `AchievementSocietyCategory`.
pub mod achievement_society_category {
    pub const INVALID: u32 = 0;
    pub const CRIME: u32 = 1;
    pub const RELATION: u32 = 2;
    pub const NPC: u32 = 3;
}

/// `RewardItemReason`.
pub mod reward_item_reason {
    pub const NONE: u32 = 0;
    pub const ACCOUNT_ATTENDANCE: u32 = 1;
    pub const ACHIEVEMENT: u32 = 2;
    pub const TODAY_ASSIGNMENT_GOAL: u32 = 3;
    pub const SIEGE_AUCTION: u32 = 4;
}

/// `CharRecordKind`.
pub mod char_record_kind {
    pub const PVP_KILL_ENEMY: u32 = 1;
    pub const PVP_KILL_HONOR_WAR: u32 = 2;
    pub const PVP_KILL_SIEGE: u32 = 3;
    pub const PVP_KILL_ALLY: u32 = 4;
    pub const PVP_DEATH_ENEMY: u32 = 5;
    pub const PVP_DEATH_HONOR_WAR: u32 = 6;
    pub const PVP_DEATH_SIEGE: u32 = 7;
    pub const PVP_DEATH_ALLY: u32 = 8;
    pub const COMPLETE_ACHIEVEMENT: u32 = 9;
    pub const CHAR_LEVEL: u32 = 10;
    pub const PET_LEVEL: u32 = 11;
    pub const PLAY_TIME: u32 = 12;
    pub const WIN_ZONE_BATTLE: u32 = 13;
    pub const MY_GOLD: u32 = 14;
    pub const SPEND_GOLD: u32 = 15;
    pub const CHANGE_LOOK: u32 = 16;
    pub const GET_LIFE_POINT: u32 = 17;
    pub const GET_ACTABILITY: u32 = 18;
    pub const GET_HONOR_POINT: u32 = 19;
    pub const SPEND_LABOR: u32 = 20;
    pub const ABILITY_LEVEL: u32 = 21;
    pub const ABILITY_CHANGE: u32 = 22;
    pub const DEAD_BY_PVP: u32 = 23;
    pub const DEAD_BY_NPC: u32 = 24;
    pub const KILL_NPC: u32 = 25;
    pub const KILL_SLAVE: u32 = 26;
    pub const COMPLETE_QUEST_CATEGORY: u32 = 27;
    pub const COMPLETE_QUEST_TYPE: u32 = 28;
    pub const GET_ITEM_TYPE: u32 = 29;
    pub const MAKE_ITEM_TYPE: u32 = 30;
    pub const GET_ITEM_IMPL: u32 = 31;
    pub const MAKE_ITEM_IMPL: u32 = 32;
    pub const GET_LOOTPACK: u32 = 33;
    pub const GET_LOOTITEM: u32 = 34;
    pub const USE_ITEM: u32 = 35;
    pub const ITEM_FIX: u32 = 36;
    pub const SELL_ITEM: u32 = 37;
    pub const MAKE_HOUSING: u32 = 38;
    pub const AUCTION_BUY: u32 = 39;
    pub const AUCTION_SOLD: u32 = 40;
    pub const USE_SKILL: u32 = 41;
    pub const GET_JURY_POINT: u32 = 42;
    pub const REPORT_CRIME: u32 = 43;
    pub const JUDGEMENT: u32 = 44;
    pub const WIN_DUEL: u32 = 45;
    pub const KILL_WANTED: u32 = 46;
    pub const NPC_EMOTION: u32 = 47;
    pub const GET_FACTION: u32 = 48;
    pub const MY_DISHONORABLE_POINT: u32 = 49;
    pub const ENCHANT_ITEM: u32 = 50;
    pub const ENCHANT_FAILURE: u32 = 51;
    pub const ENROLL_PARTY: u32 = 52;
    pub const ENROLL_RAID_GROUP: u32 = 53;
    pub const ENROLL_FAMILY: u32 = 54;
    pub const ENROLL_GUILD: u32 = 55;
    pub const ENROLL_NATION: u32 = 56;
    pub const WIN_ATTACK_SIEGE: u32 = 57;
    pub const WIN_DEFENCE_SIEGE: u32 = 58;
    pub const LOSE_ATTACK_SIEGE: u32 = 59;
    pub const LOSE_DEFENCE_SIEGE: u32 = 60;
    pub const TOP_GEAR_SCORE: u32 = 61;
    pub const BE_HERO: u32 = 62;
    pub const BE_HERO_GRADE: u32 = 63;
    pub const GET_LEADERSHIP: u32 = 64;
    pub const HERO_VOTE: u32 = 65;
    pub const GET_REPUTATION: u32 = 66;
    pub const SET_REPUTATION: u32 = 67;
    pub const REBUILD_HOUSING: u32 = 68;
    pub const USE_MOBILIZATION_ORDER: u32 = 69;
    pub const INCREASED_FAVORITE_PORTAL_LIMIT: u32 = 70;
    pub const BATTLE_FIELD_WIN: u32 = 71;
    pub const BATTLE_FIELD_LOSE: u32 = 72;
    pub const BATTLE_FIELD_DRAW: u32 = 73;
    pub const BATTLE_FIELD_KILL: u32 = 74;
    pub const BATTLE_FIELD_DEATH: u32 = 75;
    pub const BATTLE_FIELD_ASSIST: u32 = 76;
}
