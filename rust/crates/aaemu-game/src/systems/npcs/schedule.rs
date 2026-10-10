//! `GameScheduleManager` + `SchedulesGameData` (`game_schedules`, `game_schedule_spawners`,
//! `game_schedule_doodads`, `game_schedule_quests`) (stub: TODO(npcs) — being ported).

use crate::systems::DataLoadCtx;

/// `GameScheduleManager.PeriodStatus`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PeriodStatus {
    NotFound,
    NotStarted,
    InProgress,
    Ended,
}

/// Schedule tables + queries (`GameScheduleManager`).
#[derive(Debug, Default)]
pub struct ScheduleData {}

impl ScheduleData {
    pub fn load(_ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self::default())
    }

    /// `GetPeriodStatusNpc(spawnerId)` at `now` (unix seconds, UTC).
    pub fn period_status_npc(&self, _spawner_id: i32, _now: i64) -> PeriodStatus {
        PeriodStatus::NotFound
    }

    /// `GetPeriodStatusDoodad(doodadId)` at `now`.
    pub fn period_status_doodad(&self, _doodad_id: i32, _now: i64) -> PeriodStatus {
        PeriodStatus::NotFound
    }

    /// `CheckDoodadInScheduleSpawners(id)`.
    pub fn check_doodad_in_schedule_spawners(&self, _doodad_id: i32) -> bool {
        false
    }

    /// `CheckDoodadInGameSchedules(id)`.
    pub fn check_doodad_in_game_schedules(&self, _doodad_id: u32) -> bool {
        false
    }
}
