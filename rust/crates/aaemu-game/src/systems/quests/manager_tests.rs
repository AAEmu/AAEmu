//! Port of AAEmu.UnitTests/Game/Core/Managers/QuestManagerTests.cs
//!
//! `QuestManager` is split in Rust: the template getters are [`QuestData`] (empty = "before
//! Load"), the evaluation queue and quest timers are [`System`] (`server.systems.quests`). The
//! mocked `ITaskManager.Schedule` is observed through its effect: a timer entry with a spawned
//! tokio task (`abort` handle), or the queued evaluation being drained by the scheduled task.

use std::sync::Arc;

use super::data::QuestData;
use super::engine::QuestEnv;
use super::events;
use super::templates::QuestTemplate;
use super::tests::{character, server_with, with_ctx};
use crate::server::GameServer;
use crate::test_support::{spawn_player, test_character, TestClient};

fn manager() -> QuestData {
    QuestData::default()
}

/// A server whose quest system can schedule tasks (`System.server` is set by `start`).
fn scheduling_server() -> Arc<GameServer> {
    let server = Arc::new(
        server_with(|d| {
            d.add_template(QuestTemplate {
                id: 42,
                ..QuestTemplate::default()
            })
        })
        .0,
    );
    let _ = server.systems.quests.server.set(Arc::downgrade(&server));
    server
}

/// `manager.AddQuestTimer(owner, quest { TemplateId = 42 }, limit_time)` for owner `owner_id`.
fn add_quest_timer(server: &GameServer, owner_id: u32, limit_time: i32) -> bool {
    let mut c = character();
    c.id = owner_id;
    with_ctx(server, &mut c, QuestEnv::default(), |ctx| {
        let h = ctx.new_quest(42, owner_id);
        ctx.add_quest_timer(h, limit_time)
    })
}

/// Number of timers of `owner_id` that scheduled a task.
fn scheduled_timers(server: &GameServer, owner_id: u32) -> usize {
    server
        .systems
        .quests
        .timers
        .lock()
        .unwrap()
        .get(&owner_id)
        .map_or(0, |l| l.iter().filter(|t| t.abort.is_some()).count())
}

// ----- existing tests -----

#[test]
fn get_template_before_load_returns_null() {
    assert!(manager().template(999).is_none());
}

#[tokio::test]
async fn add_quest_timer_calls_task_manager_schedule() {
    let server = scheduling_server();

    let result = add_quest_timer(&server, 1, 60_000);

    assert!(result);
    assert_eq!(scheduled_timers(&server, 1), 1);
    server.systems.quests.remove_quest_timer(1, 0);
}

// ----- GetTemplate -----

#[test]
fn get_template_with_non_existent_id_returns_null() {
    assert!(manager().template(9999).is_none());
}

#[test]
fn get_template_with_zero_id_returns_null() {
    assert!(manager().template(0).is_none());
}

// ----- GetSupplies -----

#[test]
fn get_supplies_with_non_existent_level_returns_null() {
    assert!(manager().supplies(100).is_none());
}

#[test]
fn get_supplies_with_zero_level_returns_null() {
    assert!(manager().supplies(0).is_none());
}

// ----- GetActsInComponent -----

#[test]
fn get_acts_in_component_with_non_existent_component_id_returns_empty_list() {
    assert!(manager().acts_in_component(9999).is_empty());
}

#[test]
fn get_acts_in_component_with_zero_id_returns_empty_list() {
    assert!(manager().acts_in_component(0).is_empty());
}

// ----- GetActTemplate -----

#[test]
fn get_act_template_with_non_existent_id_returns_null() {
    assert!(manager()
        .act_template(9999, "QuestActObjMonsterHunt")
        .is_none());
}

#[test]
fn get_act_template_with_non_existent_type_returns_null() {
    assert!(manager().act_template(1, "NonExistentType").is_none());
}

#[test]
fn get_act_template_with_zero_id_and_valid_type_returns_null() {
    assert!(manager()
        .act_template(0, "QuestActObjMonsterHunt")
        .is_none());
}

// `GetActTemplate_WithNullType_ThrowsArgumentNullException`: not ported, a `&str` cannot be
// null.

// ----- CheckGroupItem -----

#[test]
fn check_group_item_with_non_existent_group_id_returns_false() {
    assert!(!manager().group_items.contains(9999, 1));
}

#[test]
fn check_group_item_with_zero_group_id_returns_false() {
    assert!(!manager().group_items.contains(0, 1));
}

#[test]
fn check_group_item_with_non_existent_item_id_returns_false() {
    assert!(!manager().group_items.contains(1, 9999));
}

// ----- CheckGroupNpc -----

#[test]
fn check_group_npc_with_non_existent_group_id_returns_false() {
    assert!(!manager().group_npcs.contains(9999, 1));
}

#[test]
fn check_group_npc_with_zero_group_id_returns_false() {
    assert!(!manager().group_npcs.contains(0, 1));
}

#[test]
fn check_group_npc_with_non_existent_npc_id_returns_false() {
    assert!(!manager().group_npcs.contains(1, 9999));
}

// ----- RemoveQuestTimer -----

#[test]
fn remove_quest_timer_with_non_existent_owner_id_returns_zero() {
    let s = super::System::default();
    assert_eq!(s.remove_quest_timer(9999, 1), 0);
}

#[test]
fn remove_quest_timer_with_zero_quest_id_and_non_existent_owner_returns_zero() {
    let s = super::System::default();
    assert_eq!(s.remove_quest_timer(9999, 0), 0);
}

#[test]
fn remove_quest_timer_with_non_existent_quest_id_returns_zero() {
    let s = super::System::default();
    assert_eq!(s.remove_quest_timer(1, 9999), 0);
}

// ----- GetGroupItems -----

#[test]
fn get_group_items_with_non_existent_group_id_returns_empty_list() {
    assert!(manager().group_items.members(9999).is_empty());
}

#[test]
fn get_group_items_with_zero_group_id_returns_empty_list() {
    assert!(manager().group_items.members(0).is_empty());
}

// ----- GetComponent -----

#[test]
fn get_component_with_non_existent_component_id_returns_null() {
    assert!(manager().component(9999).is_none());
}

#[test]
fn get_component_with_zero_id_returns_null() {
    assert!(manager().component(0).is_none());
}

// ----- EnqueueEvaluation -----

#[tokio::test]
async fn enqueue_evaluation_with_valid_quest_schedules_task() {
    let server = scheduling_server();
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(
        &server,
        &client,
        test_character(1, "TestPlayer", 10.0, 10.0),
        0x100,
    );
    let h = events::with_quests(&server, &player, None, |ctx| ctx.new_quest(42, 1)).unwrap();

    server.systems.quests.enqueue_evaluation(&player, h);
    assert_eq!(server.systems.quests.queued_count(), 1);

    // the scheduled QuestManagerRunQueueTask (1 ms) drains the queue
    for _ in 0..200 {
        if server.systems.quests.queued_count() == 0 {
            break;
        }
        tokio::time::sleep(std::time::Duration::from_millis(5)).await;
    }
    assert_eq!(server.systems.quests.queued_count(), 0);
}

// ----- quest timeout task management -----

#[tokio::test]
async fn add_quest_timer_existing_timer_returns_false() {
    let server = scheduling_server();

    assert!(add_quest_timer(&server, 1, 60_000));
    // second timer for the same quest
    assert!(!add_quest_timer(&server, 1, 60_000));
    assert_eq!(scheduled_timers(&server, 1), 1);
    server.systems.quests.remove_quest_timer(1, 0);
}

#[tokio::test]
async fn add_quest_timer_with_zero_time_schedules_task() {
    let server = scheduling_server();

    let result = add_quest_timer(&server, 1, 0);

    assert!(result);
    assert_eq!(scheduled_timers(&server, 1), 1);
    server.systems.quests.remove_quest_timer(1, 0);
}

// ----- edge cases and boundary tests -----

#[test]
fn get_template_with_max_uint_id_returns_null() {
    assert!(manager().template(u32::MAX).is_none());
}

#[test]
fn check_group_item_with_max_uint_ids_returns_false() {
    assert!(!manager().group_items.contains(u32::MAX, u32::MAX));
}

#[test]
fn remove_quest_timer_with_max_uint_ids_returns_zero() {
    let s = super::System::default();
    assert_eq!(s.remove_quest_timer(u32::MAX, u32::MAX), 0);
}

// ----- quest objective types -----

#[test]
fn get_acts_in_component_returns_list_that_can_be_modified() {
    // C#: a new empty list on every call (`[]`); Rust hands out an empty shared slice, so
    // only the "empty on every call" part is observable.
    let m = manager();
    let result = m.acts_in_component(1);
    let result2 = m.acts_in_component(1);
    assert!(result.is_empty());
    assert!(result2.is_empty());
}

// ----- quest timer multiple owners -----

#[tokio::test]
async fn add_quest_timer_multiple_owners_creates_separate_timers() {
    let server = scheduling_server();

    let result1 = add_quest_timer(&server, 1, 60_000);
    let result2 = add_quest_timer(&server, 2, 60_000);

    assert!(result1);
    assert!(result2);
    assert_eq!(scheduled_timers(&server, 1), 1);
    assert_eq!(scheduled_timers(&server, 2), 1);
    server.systems.quests.remove_quest_timer(1, 0);
    server.systems.quests.remove_quest_timer(2, 0);
}
