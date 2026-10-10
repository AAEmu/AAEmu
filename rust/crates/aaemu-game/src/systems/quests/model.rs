//! `Models/Game/Quests/Static/*` and the small value types of the quest system.
//!
//! The C# enums are cast from bytes read from the DB / the client (`(QuestComponentKind)reader
//! .GetByte(..)`, `(QuestComponentKind)stream.ReadByte()`), so any value is possible: they are
//! kept as plain integers with named constants.

/// `QuestComponentKind`.
pub mod kind {
    pub const INVALID: u8 = 0;
    pub const NONE: u8 = 1;
    pub const START: u8 = 2;
    pub const SUPPLY: u8 = 3;
    pub const PROGRESS: u8 = 4;
    pub const FAIL: u8 = 5;
    pub const READY: u8 = 6;
    pub const DROP: u8 = 7;
    pub const REWARD: u8 = 8;

    /// `Enum.GetValues<QuestComponentKind>()`.
    pub const ALL: [u8; 9] = [
        INVALID, NONE, START, SUPPLY, PROGRESS, FAIL, READY, DROP, REWARD,
    ];

    /// The C# enum member name (`ToString()`), for log lines.
    pub fn name(k: u8) -> String {
        match k {
            INVALID => "Invalid".into(),
            NONE => "None".into(),
            START => "Start".into(),
            SUPPLY => "Supply".into(),
            PROGRESS => "Progress".into(),
            FAIL => "Fail".into(),
            READY => "Ready".into(),
            DROP => "Drop".into(),
            REWARD => "Reward".into(),
            other => other.to_string(),
        }
    }
}

/// `QuestStatus` (same values as `QuestContextStatus`).
pub mod status {
    pub const INVALID: u8 = 0;
    pub const PROGRESS: u8 = 1;
    pub const FAILED: u8 = 2;
    pub const READY: u8 = 3;
    pub const DROPPED: u8 = 4;
    pub const COMPLETED: u8 = 5;
    pub const DAILY_COMPLETED: u8 = 6;
}

/// `QuestAcceptorType`.
pub mod acceptor {
    pub const UNKNOWN: u8 = 0;
    pub const NPC: u8 = 1;
    pub const DOODAD: u8 = 2;
    pub const SPHERE: u8 = 3;
    pub const ITEM: u8 = 4;
    pub const SKILL: u8 = 5;
    pub const BUFF: u8 = 6;
}

/// `QuestConditionObj`.
pub mod condition {
    pub const COMPLETE: u8 = 1;
    pub const FAIL: u8 = 2;
    pub const READY: u8 = 3;
    pub const PROGRESS: u8 = 4;
}

/// `QuestDetail`.
pub mod detail {
    pub const NORMAL: u32 = 1;
    pub const MAIN: u32 = 2;
    pub const SAGA: u32 = 3;
    pub const TUTORIAL: u32 = 4;
    pub const HIDDEN: u32 = 5;
    pub const TASK: u32 = 6;
    pub const DAILY: u32 = 7;
    pub const LIVELIHOOD: u32 = 8;
    pub const GROUP: u32 = 9;
    pub const DAILY_HUNT: u32 = 10;
    pub const DAILY_LIVELIHOOD: u32 = 11;
    pub const DAILY_GROUP: u32 = 12;
    pub const TODAY: u32 = 13;
}

/// `QuestNpcAiName`.
pub mod npc_ai {
    pub const NONE: u32 = 1;
    pub const FOLLOW_UNIT: u32 = 2;
    pub const FOLLOW_PATH: u32 = 3;
    pub const ATTACK_UNIT: u32 = 4;
    pub const GO_AWAY: u32 = 5;
    pub const RUN_COMMAND_SET: u32 = 6;
}

/// `QuestTrigger` (`sphere_quests.quest_trigger_id`).
pub mod trigger {
    pub const COMPLETE: u32 = 1;
    pub const DROP: u32 = 2;
    pub const ACCEPT_FORCE: u32 = 3;
    pub const ACCEPT_CONDITIONAL: u32 = 4;
    pub const FAIL: u32 = 5;
}

/// `QuestObjectiveStatus` (ordered: the C# compares them with `<` / `>=`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
#[repr(u8)]
pub enum ObjectiveStatus {
    NotReady = 0,
    CanEarlyComplete = 1,
    QuestComplete = 2,
    ExtraProgress = 3,
    Overachieved = 4,
}

/// `AreaSphereTriggerCondition`.
pub mod sphere_trigger {
    pub const NONE: u32 = 0;
    pub const TRIGGER_ONCE_AT_ALL: u32 = 1;
    pub const TRIGGER_ONCE_IN_RUNTIME: u32 = 2;
    pub const TRIGGER_EVERY_N_TIME_AFTER: u32 = 3;
}

/// `ItemTaskType` values the quest code passes to the item hooks.
pub mod item_task {
    pub const QUEST_COMPLETE: u8 = 37;
    pub const QUEST_SUPPLY_ITEMS: u8 = 38;
    pub const QUEST_REMOVE_SUPPLIES: u8 = 39;
}

/// `GamePointKind` values used by the supply acts.
pub mod game_point {
    pub const HONOR: u8 = 0;
    pub const VOCATION: u8 = 1;
}

/// `ErrorMessageType` values the quest code sends.
pub mod error_msg {
    pub const MAIL_UNKNOWN_FAILURE: i16 = 169;
    pub const BACKPACK_OCCUPIED: i16 = 315;
    pub const QUEST_DAILY_LIMIT: i16 = 621;
}

/// `FactionsEnum.Monstrosity` (`SetNpcAggro`).
pub const FACTION_MONSTROSITY: u32 = 115;

/// `ItemCreationDefinition(templateId, count, gradeId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ItemCreationDefinition {
    pub template_id: u32,
    pub count: i32,
    pub grade_id: u8,
}

/// `QuestSupplies` (`quest_supplies`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct QuestSupplies {
    pub id: u32,
    pub level: u8,
    pub exp: i32,
    pub copper: i32,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn objective_status_order_matches_csharp() {
        assert!(ObjectiveStatus::NotReady < ObjectiveStatus::CanEarlyComplete);
        assert!(ObjectiveStatus::QuestComplete < ObjectiveStatus::ExtraProgress);
        assert_eq!(ObjectiveStatus::Overachieved as u8, 4);
        assert_eq!(kind::name(kind::REWARD), "Reward");
        assert_eq!(kind::name(42), "42");
    }
}
