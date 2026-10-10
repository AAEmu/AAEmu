//! Port of AAEmu.UnitTests/Game/Models/Game/Quests/QuestTests.cs
//!
//! `SetupQuest` (a `Quest` on a mocked template with no components and a mocked owner) is a
//! quest on template 42 without components, created through [`Ctx::new_quest`] on a test
//! server. The plain C# properties are fields of [`Quest`]; `Step` goes through the setter
//! ([`Ctx::set_step`]); `ReadData` is the `CharacterQuests.Load` path
//! ([`ActiveQuest::read_data`] + [`Ctx::load_from_rows`]).
//!
//! `Start_WhenQuestStepIsNoneAndComponentIsEmpty_ShouldDoNothing` is commented out in the C#
//! (no `[Test]`), so it has no port.

use aaemu_commons::PacketStream;

use super::data::QuestData;
use super::engine::{Ctx, QuestEnv};
use super::model::{acceptor, kind, status};
use super::state::{Quest, QuestHandle};
use super::templates::QuestTemplate;
use super::tests::{character, server_with, with_ctx};
use crate::models::char_data::ActiveQuest;
use crate::server::GameServer;

const TEMPLATE_ID: u32 = 42;

fn template_without_components(d: &mut QuestData) {
    d.add_template(QuestTemplate {
        id: TEMPLATE_ID,
        ..QuestTemplate::default()
    });
}

fn server() -> GameServer {
    server_with(template_without_components).0
}

/// `SetupQuest(...)`: runs `f` with the new quest.
fn setup_quest<R>(f: impl FnOnce(&mut Ctx<'_>, QuestHandle) -> R) -> R {
    let server = server();
    let mut c = character();
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        let h = ctx.new_quest(TEMPLATE_ID, 1);
        f(ctx, h)
    })
}

fn quest<'a>(ctx: &'a Ctx<'_>, h: QuestHandle) -> &'a Quest {
    ctx.st.quest(h).expect("quest")
}

fn quest_mut<'a>(ctx: &'a mut Ctx<'_>, h: QuestHandle) -> &'a mut Quest {
    ctx.st.quest_mut(h).expect("quest")
}

#[tokio::test]
async fn quest_initialized_sets_initialization_finished() {
    setup_quest(|ctx, h| {
        assert!(!quest(ctx, h).init_finished);
        ctx.quest_initialized(h);
        // `_questInitializationFinished` is private in C# (the test only checks for no throw)
        assert!(quest(ctx, h).init_finished);
    });
}

#[tokio::test]
async fn finalize_quest_acts_calls_finalize_on_acts() {
    setup_quest(|ctx, h| {
        // no acts: completes without panicking
        ctx.finalize_quest_acts(h);
        assert!(ctx.st.quest(h).is_some());
    });
}

#[tokio::test]
async fn write_data_returns_byte_array() {
    setup_quest(|ctx, h| {
        let data = quest(ctx, h).row().write_data();
        assert!(!data.is_empty());
        // 5 objectives, step, acceptor type, component id, acceptor id, time
        assert_eq!(data.len(), 5 * 4 + 1 + 1 + 4 + 4 + 8);
    });
}

#[tokio::test]
async fn read_data_with_valid_data_sets_objectives() {
    let server = server();
    let mut s = PacketStream::new();
    s.write_i32(1) // objective 1
        .write_i32(2) // objective 2
        .write_i32(3) // objective 3
        .write_i32(4) // objective 4
        .write_i32(5) // objective 5
        .write_u8(kind::PROGRESS) // step
        .write_u8(acceptor::NPC) // acceptor type
        .write_u32(100) // component id
        .write_u32(200) // acceptor id
        .write_date_time(chrono::Utc::now()); // time
    let mut row = ActiveQuest {
        id: 1,
        template_id: TEMPLATE_ID,
        status: status::PROGRESS,
        objectives: [0; 5],
        step: kind::INVALID,
        acceptor_type: 0,
        component_id: 0,
        acceptor_id: 0,
        time_ms: ActiveQuest::DATETIME_MIN_MS,
    };

    row.read_data(&s.into_bytes()).unwrap();
    let mut c = character();
    c.quests.active = vec![row];
    with_ctx(&server, &mut c, QuestEnv::default(), |ctx| {
        ctx.st.loaded = false;
        ctx.load_from_rows();
        let h = ctx.st.active_handle(TEMPLATE_ID).expect("quest loaded");
        let q = quest(ctx, h);
        assert_eq!(q.step, kind::PROGRESS);
        assert_eq!(q.acceptor_type, acceptor::NPC);
        assert_eq!(q.objectives, [1, 2, 3, 4, 5]);
        assert_eq!((q.component_id, q.acceptor_id), (100, 200));
    });
}

// ----- property tests -----

#[tokio::test]
async fn status_set_and_get_returns_correct_value() {
    setup_quest(|ctx, h| {
        quest_mut(ctx, h).status = status::COMPLETED;
        assert_eq!(quest(ctx, h).status, status::COMPLETED);
    });
}

#[tokio::test]
async fn step_set_and_get_returns_correct_value() {
    setup_quest(|ctx, h| {
        ctx.set_step(h, kind::REWARD);
        assert_eq!(quest(ctx, h).step, kind::REWARD);
    });
}

#[tokio::test]
async fn objectives_array_access_returns_correct_value() {
    setup_quest(|ctx, h| {
        quest_mut(ctx, h).objectives[0] = 10;
        assert_eq!(quest(ctx, h).objectives[0], 10);
    });
}

#[tokio::test]
async fn component_id_set_and_get_returns_correct_value() {
    setup_quest(|ctx, h| {
        quest_mut(ctx, h).component_id = 123;
        assert_eq!(quest(ctx, h).component_id, 123);
    });
}

#[tokio::test]
async fn doodad_id_set_and_get_returns_correct_value() {
    setup_quest(|ctx, h| {
        quest_mut(ctx, h).doodad_id = 456;
        assert_eq!(quest(ctx, h).doodad_id, 456);
    });
}

#[tokio::test]
async fn quest_acceptor_type_set_and_get_returns_correct_value() {
    setup_quest(|ctx, h| {
        quest_mut(ctx, h).acceptor_type = acceptor::DOODAD;
        assert_eq!(quest(ctx, h).acceptor_type, acceptor::DOODAD);
    });
}

#[tokio::test]
async fn acceptor_id_set_and_get_returns_correct_value() {
    setup_quest(|ctx, h| {
        quest_mut(ctx, h).acceptor_id = 789;
        assert_eq!(quest(ctx, h).acceptor_id, 789);
    });
}

// ----- edge cases -----

#[tokio::test]
async fn objectives_array_bounds_does_not_throw() {
    // C#: `Objectives[10] = 1` throws IndexOutOfRangeException (5 objectives).
    setup_quest(|ctx, h| {
        assert!(quest_mut(ctx, h).objectives.get_mut(10).is_none());
    });
}
