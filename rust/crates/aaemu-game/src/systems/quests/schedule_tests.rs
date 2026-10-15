//! `game_schedule_quests` gating tests: the [`super::quest_schedule_allows`] predicate over
//! synthetic [`ScheduleData`] windows and the accept gate in [`super::engine::Ctx::add_quest`].

use super::quest_schedule_allows;
use super::tests::{character, with_ctx};
use crate::data::GameData;
use crate::server::GameServer;
use crate::systems::npcs::schedule::{
    day_of_week, GameSchedule, GameScheduleQuest, PeriodStatus, ScheduleData,
};
use crate::systems::quests::data::QuestData;
use crate::systems::quests::engine::QuestEnv;
use crate::systems::quests::model::{acceptor, detail, kind};
use crate::systems::quests::templates::{ActKind, QuestComponentTemplate, QuestTemplate};
use crate::test_support::test_server_with;

/// Quest 6036 ("Squash Head") of the 1.2 compact data (`game_schedule_quests` -> schedule 57).
const QUEST_ID: u32 = 6036;
/// 2025-10-09 09:33:20 UTC (fixed so the predicate fixtures stay deterministic).
const NOW: i64 = 1_760_000_000;

/// A schedule with all-zero date / time fields and no day of week: `InProgress` at any time.
fn open_schedule(id: i32) -> GameSchedule {
    GameSchedule {
        id,
        day_of_week_id: day_of_week::INVALID,
        ..GameSchedule::default()
    }
}

/// 2020-01-01 .. 2100-01-01: covers `NOW` (and the real clock the accept gate reads).
fn active_schedule(id: i32) -> GameSchedule {
    GameSchedule {
        st_year: 2020,
        st_month: 1,
        st_day: 1,
        ed_year: 2100,
        ed_month: 1,
        ed_day: 1,
        ..open_schedule(id)
    }
}

/// 2100-01-01 .. 2100-01-02: starts after `NOW`.
fn future_schedule(id: i32) -> GameSchedule {
    GameSchedule {
        st_year: 2100,
        st_month: 1,
        st_day: 1,
        ed_year: 2100,
        ed_month: 1,
        ed_day: 2,
        ..open_schedule(id)
    }
}

/// Ends 2020-01-01: over before `NOW`.
fn expired_schedule(id: i32) -> GameSchedule {
    GameSchedule {
        ed_year: 2020,
        ed_month: 1,
        ed_day: 1,
        ..open_schedule(id)
    }
}

/// `ScheduleData` with one schedule row bound to [`QUEST_ID`].
fn schedule_data(schedule: GameSchedule) -> ScheduleData {
    let game_schedule_id = schedule.id;
    ScheduleData::from_rows(
        vec![schedule],
        vec![],
        vec![],
        vec![GameScheduleQuest {
            id: 1,
            game_schedule_id,
            quest_id: QUEST_ID as i32,
        }],
    )
}

// ----- predicate -----

#[test]
fn unlisted_quest_is_allowed_at_any_time() {
    let d = schedule_data(active_schedule(57));
    assert_eq!(
        d.period_status_quest(QUEST_ID as i32 + 1, NOW),
        PeriodStatus::NotFound
    );
    for now in [NOW - 100_000_000, NOW, NOW + 100_000_000] {
        assert!(quest_schedule_allows(&d, QUEST_ID + 1, now), "{now}");
    }
}

#[test]
fn scheduled_quest_inside_window_is_allowed() {
    let d = schedule_data(active_schedule(57));
    assert_eq!(
        d.period_status_quest(QUEST_ID as i32, NOW),
        PeriodStatus::InProgress
    );
    assert!(quest_schedule_allows(&d, QUEST_ID, NOW));
}

#[test]
fn scheduled_quest_before_window_is_rejected() {
    let d = schedule_data(future_schedule(57));
    assert_eq!(
        d.period_status_quest(QUEST_ID as i32, NOW),
        PeriodStatus::NotStarted
    );
    assert!(!quest_schedule_allows(&d, QUEST_ID, NOW));
}

#[test]
fn scheduled_quest_after_window_is_rejected() {
    let d = schedule_data(expired_schedule(57));
    assert_eq!(
        d.period_status_quest(QUEST_ID as i32, NOW),
        PeriodStatus::Ended
    );
    assert!(!quest_schedule_allows(&d, QUEST_ID, NOW));
}

// ----- accept gate -----

/// A server with one quest (START -> `ConAutoComplete`, then a PROGRESS hunt that stays
/// incomplete) and one `game_schedule_quests` row. The PROGRESS objective keeps the accepted
/// quest active (a START-only quest would complete and drop immediately).
fn scheduled_quest_server(schedule: GameSchedule) -> GameServer {
    let mut q = QuestData::default();
    q.add_template(QuestTemplate {
        id: QUEST_ID,
        detail_id: detail::NORMAL,
        ..QuestTemplate::default()
    });
    q.add_component(QuestComponentTemplate {
        id: 60361,
        quest_id: QUEST_ID,
        kind_id: kind::START,
        ..QuestComponentTemplate::default()
    });
    q.add_act(60361, 1, 1, 0, ActKind::ConAutoComplete);
    q.add_component(QuestComponentTemplate {
        id: 60362,
        quest_id: QUEST_ID,
        kind_id: kind::PROGRESS,
        ..QuestComponentTemplate::default()
    });
    q.add_act(
        60362,
        2,
        1,
        1,
        ActKind::ObjMonsterHunt {
            npc_id: 600,
            use_alias: false,
            alias_id: 0,
            highlight_doodad_id: 0,
            highlight_doodad_phase: -1,
        },
    );
    q.finish();
    let mut data = GameData::empty();
    data.systems.quests = q;
    data.systems.npcs.schedule = schedule_data(schedule);
    test_server_with(data)
}

#[tokio::test]
async fn add_quest_rejects_a_scheduled_quest_outside_its_window() {
    for schedule in [future_schedule(57), expired_schedule(57)] {
        let server = scheduled_quest_server(schedule);
        let mut c = character();
        with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
            assert!(
                !ctx.add_quest(QUEST_ID, false, acceptor::NPC, 14313),
                "a scheduled quest outside its window must be rejected"
            );
            assert!(!ctx.has_quest(QUEST_ID));
        });
    }
}

#[tokio::test]
async fn add_quest_accepts_a_scheduled_quest_inside_its_window() {
    let server = scheduled_quest_server(active_schedule(57));
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        assert!(ctx.add_quest(QUEST_ID, false, acceptor::NPC, 14313));
        assert!(ctx.has_quest(QUEST_ID));
    });
}

/// GM paths (`forcibly`) bypass the schedule gate like the component requirements.
#[tokio::test]
async fn add_quest_forcibly_bypasses_the_schedule_gate() {
    let server = scheduled_quest_server(expired_schedule(57));
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        assert!(ctx.add_quest(QUEST_ID, true, acceptor::NPC, 14313));
        assert!(ctx.has_quest(QUEST_ID));
    });
}
