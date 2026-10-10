//! `Models/Game/Achievement` data types and enums, and `GameData/AchievementGameData` (the
//! table loader). The 1.2 C# has no achievement manager (the types are only the DB / packet
//! shapes); the S->C packets live in `packets::misc`.

use std::collections::HashMap;

use chrono::{DateTime, Utc};
use rusqlite::Connection;

use crate::data::sqlite::for_each_row;
use crate::data::GameDataError;

/// `AchievementGameData` (`[GameData]`, `Load(SqliteConnection)`). The C# keeps the tables in
/// private dictionaries without accessors; they are public fields here.
///
/// C# quirk mirrored: the boolean columns (`complete_or`, `is_active`, `is_hidden`,
/// `or_unit_reqs`) hold the text `'t'` / `'f'` in compact.sqlite3 and are read with
/// `GetBoolean(column)` (Microsoft.Data.Sqlite `GetInt64 != 0`), so they always load as `false`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct AchievementGameData {
    /// `_charRecords` (`char_records` by id; `Dictionary.Add`, a duplicate id fails the load).
    pub char_records: HashMap<u32, CharRecords>,
    /// `_achievements` (`achievements` by id; `TryAdd`, the first row of an id wins).
    pub achievements: HashMap<u32, Achievements>,
    /// `_achievementObjectives` (`achievement_objectives` grouped by `achievement_id`).
    pub achievement_objectives: HashMap<u32, Vec<AchievementObjectives>>,
    /// `_preCompletedAchievements` (`pre_completed_achievements` grouped by
    /// `completed_achievement_id`).
    pub pre_completed_achievements: HashMap<u32, Vec<PreCompletedAchievements>>,
}

impl AchievementGameData {
    /// `AchievementGameData.Load(connection)`.
    pub fn load(conn: &Connection) -> Result<Self, GameDataError> {
        let mut d = Self::default();
        for_each_row(conn, "SELECT * FROM achievements", [], |r| {
            let t = Achievements {
                id: r.u32("id")?,
                category_id: r.u32_or("category_id", 0)?,
                complete_num: r.u32_or("complete_num", 0)?,
                complete_or: r.bool("complete_or")?,
                icon_id: r.u32_or("icon_id", 0)?,
                is_active: r.bool("is_active")?,
                is_hidden: r.bool("is_hidden")?,
                item_id: r.u32_or("item_id", 0)?,
                or_unit_reqs: r.bool("or_unit_reqs")?,
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
                or_unit_reqs: r.bool("or_unit_reqs")?,
                record_id: r.u32("record_id")?,
            };
            d.achievement_objectives
                .entry(t.achievement_id)
                .or_default()
                .push(t);
            Ok(())
        })?;
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
    /// `AchievementObjectives`.
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
