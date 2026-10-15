//! Flow tests of the schedule-item world path (`schedule_item_session`): the entry list, the take
//! (exactly once per window), the mail overflow, the save snapshot and the periodic re-send.

use std::collections::BTreeMap;
use std::sync::Arc;

use chrono::Utc;

use super::schedule_item_manager::ScheduleItemManager;
use super::schedule_item_runtime::{AccountScheduleProgress, AccountScheduleState};
use super::schedule_item_session::*;
use super::schedule_items::{ScheduleItemData, ScheduleItemDef, ScheduleItemKind};
use crate::data::items::ItemTemplate;
use crate::data::GameData;
use crate::models::character::Inventory;
use crate::models::container::slot_type;
use crate::models::item::Item;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::economy::inventory::Inv;
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::PlayerRef;

const REWARD_ITEM: u32 = 7001;
/// `test_character(1, ..)` belongs to account `1000 + 1`.
const ACCOUNT: u32 = 1001;

/// A live kind-1 row with a real grant, one term minute and no authored bounds.
fn row(id: i32, active_take: bool) -> ScheduleItemDef {
    ScheduleItemDef {
        id,
        kind: ScheduleItemKind::Every,
        give_term: 1,
        give_max: 2,
        item_id: REWARD_ITEM,
        item_count: 1,
        active_take,
        on_air: true,
        ..ScheduleItemDef::default()
    }
}

struct Fixture {
    server: Arc<GameServer>,
    client: TestClient,
    player: PlayerRef,
}

/// `bag_slots == 0` makes every reward overflow to the mail.
fn fixture(rows: Vec<ScheduleItemDef>, bag_slots: u8) -> Fixture {
    let mut data = GameData::empty();
    data.items
        .templates
        .insert(REWARD_ITEM, ItemTemplate::plain(REWARD_ITEM, 1));
    data.systems.npcs.schedule_items = ScheduleItemData::from_rows(rows);
    let server = Arc::new(test_server_with(data));
    server.set_self();
    server.names.add_character(1, "Tester", 1);
    let mut client = TestClient::new(&server, 1, 1);
    let mut character = test_character(1, "Tester", 0.0, 0.0);
    if bag_slots != 50 {
        character.num_inventory_slots = bag_slots;
        character.inventory = Inventory::standalone(1, bag_slots, 50);
    }
    let player = spawn_player(&server, &client, character, 1000);
    let inventory = Inv::of(&player);
    server.items.add_container(inventory.bag());
    if let Some(escrow) = inventory.get(slot_type::MAIL) {
        server.items.add_container(escrow);
    }
    client.drain();
    Fixture {
        server,
        client,
        player,
    }
}

/// Restores a saved row for the test account, as the startup load would.
fn restore(f: &Fixture, schedule_id: i32, gave: u8, cumulated: i64) {
    let state = AccountScheduleState {
        gave,
        cumulated,
        updated: Utc::now().timestamp(),
    };
    f.server.systems.npcs.schedule_items.lock().insert(
        ACCOUNT,
        AccountScheduleProgress::from_saved(ACCOUNT, [(schedule_id, state)]),
    );
}

fn bag_items(f: &Fixture) -> Vec<Item> {
    Inv::of(&f.player)
        .bag()
        .lock()
        .expect("container poisoned")
        .items
        .clone()
}

/// `(schedule_id, gave, cumulated)` of every row of the `SCScheduleItemUpdatePacket`s in the
/// frames (one list per packet).
fn update_lists(frames: &[(u16, Vec<u8>)]) -> Vec<Vec<(u32, u8, i32)>> {
    frames
        .iter()
        .filter(|(opcode, _)| *opcode == sc::SCScheduleItemUpdatePacket)
        .map(|(_, body)| {
            (0..usize::from(body[0]))
                .map(|i| {
                    let at = 1 + i * 17;
                    (
                        u32::from_le_bytes(body[at..at + 4].try_into().expect("id")),
                        body[at + 4],
                        i32::from_le_bytes(body[at + 5..at + 9].try_into().expect("acc")),
                    )
                })
                .collect()
        })
        .collect()
}

/// The `SCScheduleItemSentPacket`s of the frames as `(id, by_mail)`.
fn sent(frames: &[(u16, Vec<u8>)]) -> Vec<(u32, bool)> {
    frames
        .iter()
        .filter(|(opcode, _)| *opcode == sc::SCScheduleItemSentPacket)
        .map(|(_, body)| {
            (
                u32::from_le_bytes(body[..4].try_into().expect("id")),
                body[4] != 0,
            )
        })
        .collect()
}

#[tokio::test]
async fn entering_the_world_sends_the_update_rows() {
    let mut f = fixture(vec![row(1, true), row(2, false)], 50);
    restore(&f, 1, 1, 30);

    on_enter(&f.server, &f.player);

    // HUD rows first, then the silent rows; the restored counters are reported.
    let frames = f.client.drain();
    assert_eq!(update_lists(&frames), [vec![(1, 1, 30), (2, 0, 0)]]);
    assert!(f
        .server
        .systems
        .npcs
        .schedule_items
        .lock()
        .get(&ACCOUNT)
        .is_some_and(|account| account.is_online(1)));
}

#[tokio::test]
async fn a_take_grants_once_and_the_second_take_in_the_window_is_refused() {
    let mut f = fixture(vec![row(1, true)], 50);
    restore(&f, 1, 0, 60);
    on_enter(&f.server, &f.player);
    f.client.drain();

    assert_eq!(
        take(&f.server, &f.player, 1),
        TakeOutcome::Granted { by_mail: false }
    );
    let bag = bag_items(&f);
    assert_eq!(bag.len(), 1);
    assert_eq!((bag[0].template_id, bag[0].count), (REWARD_ITEM, 1));
    let frames = f.client.drain();
    assert_eq!(sent(&frames), [(1, false)]);
    // The new counters follow the answer.
    assert_eq!(update_lists(&frames), [vec![(1, 1, 0)]]);

    // The term restarted (`cumulated = 0`): a second take is refused and grants nothing.
    assert_eq!(take(&f.server, &f.player, 1), TakeOutcome::Refused);
    assert_eq!(bag_items(&f).len(), 1);
    let frames = f.client.drain();
    assert!(sent(&frames).is_empty());
    // The refusal still corrects the client's list.
    assert_eq!(update_lists(&frames), [vec![(1, 1, 0)]]);
}

#[tokio::test]
async fn a_take_before_the_term_is_ready_or_of_a_silent_row_is_refused() {
    let f = fixture(vec![row(1, true), row(2, false)], 50);
    restore(&f, 1, 0, 0);
    on_enter(&f.server, &f.player);

    assert_eq!(take(&f.server, &f.player, 1), TakeOutcome::Refused);
    // A silent row has no take button, and an unknown row does not exist.
    assert_eq!(take(&f.server, &f.player, 2), TakeOutcome::Refused);
    assert_eq!(take(&f.server, &f.player, 99), TakeOutcome::Refused);
    assert!(bag_items(&f).is_empty());
}

#[tokio::test]
async fn a_take_needs_an_entered_character() {
    let f = fixture(vec![row(1, true)], 50);
    restore(&f, 1, 0, 60);
    // Not entered yet.
    assert_eq!(take(&f.server, &f.player, 1), TakeOutcome::Refused);
    on_enter(&f.server, &f.player);
    on_leave(&f.server, &f.player);
    assert_eq!(take(&f.server, &f.player, 1), TakeOutcome::Refused);
    assert!(bag_items(&f).is_empty());
}

#[tokio::test]
async fn a_full_bag_sends_the_reward_by_mail_once() {
    let mut f = fixture(vec![row(1, true)], 0);
    restore(&f, 1, 0, 60);
    on_enter(&f.server, &f.player);
    f.client.drain();

    assert_eq!(
        take(&f.server, &f.player, 1),
        TakeOutcome::Granted { by_mail: true }
    );
    assert_eq!(sent(&f.client.drain()), [(1, true)]);
    assert!(bag_items(&f).is_empty());
    assert_eq!(f.server.systems.economy.mail.state().all.len(), 1);

    assert_eq!(take(&f.server, &f.player, 1), TakeOutcome::Refused);
    assert_eq!(f.server.systems.economy.mail.state().all.len(), 1);
}

#[tokio::test]
async fn the_receipt_persists_through_the_save_snapshot() {
    let f = fixture(vec![row(1, true)], 50);
    restore(&f, 1, 0, 60);
    on_enter(&f.server, &f.player);
    assert_eq!(
        take(&f.server, &f.player, 1),
        TakeOutcome::Granted { by_mail: false }
    );

    // The snapshot the financial save writes carries the receipt (`gave + 1`, term restarted).
    let manager = &f.server.systems.npcs.schedule_items;
    let snapshot = manager.prepare_save();
    assert_eq!(snapshot.accounts.len(), 1);
    assert_eq!(snapshot.accounts[0].account_id, ACCOUNT);
    let (schedule_id, state) = snapshot.accounts[0].rows[0];
    assert_eq!((schedule_id, state.gave, state.cumulated), (1, 1, 0));

    // A restart restores that row: the take counter survives in the same window.
    let restored = ScheduleItemManager::from_accounts(BTreeMap::from([(
        ACCOUNT,
        AccountScheduleProgress::from_saved(ACCOUNT, [(schedule_id, state)]),
    )]));
    let now = Utc::now().timestamp();
    let data = &f.server.game_data.systems.npcs.schedule_items;
    let rows = restored
        .lock()
        .get(&ACCOUNT)
        .expect("account")
        .build_update_rows(data, now, &eligibility(now));
    assert_eq!((rows[0].gave, rows[0].acumulated), (1, 0));
}

#[tokio::test]
async fn the_tick_resends_only_when_the_list_changed() {
    let mut f = fixture(vec![row(1, true)], 50);
    on_enter(&f.server, &f.player);
    f.client.drain();

    // Nothing moved since the entry list.
    assert_eq!(tick(&f.server), 0);
    assert!(update_lists(&f.client.drain()).is_empty());

    // Progress changed (here through the account row, as an accrual would): one new list.
    {
        let now = Utc::now().timestamp();
        let mut map = f.server.systems.npcs.schedule_items.lock();
        let account = map.get_mut(&ACCOUNT).expect("account");
        *account = AccountScheduleProgress::from_saved(
            ACCOUNT,
            [(
                1,
                AccountScheduleState {
                    gave: 0,
                    cumulated: 30,
                    updated: now,
                },
            )],
        );
        account.join(1, now);
    }
    assert_eq!(tick(&f.server), 1);
    // The tick itself may credit a second if the wall clock rolled over meanwhile.
    let lists = update_lists(&f.client.drain());
    assert_eq!(lists.len(), 1);
    let (id, gave, cumulated) = lists[0][0];
    assert_eq!((id, gave), (1, 0));
    assert!((30..=32).contains(&cumulated), "cumulated {cumulated}");
}

#[tokio::test]
async fn the_client_packet_takes_through_the_registry() {
    let mut f = fixture(vec![row(1, true)], 50);
    restore(&f, 1, 0, 60);
    on_enter(&f.server, &f.player);
    f.client.conn.active_char = Some(f.player.clone());
    f.client.drain();

    // `CSTakeScheduleItemPacket` (0x13b): level-1 frame, one u32 schedule type.
    let mut frame = vec![0xDD, 0x01, 0x5A, 0x03];
    frame.extend_from_slice(&crate::opcodes::cs::CSTakeScheduleItemPacket.to_le_bytes());
    frame.extend_from_slice(&1u32.to_le_bytes());
    f.server
        .handle_frame(&mut f.client.conn, &frame)
        .await
        .expect("take frame");
    assert_eq!(sent(&f.client.drain()), [(1, false)]);
    assert_eq!(bag_items(&f).len(), 1);

    // Replaying the same frame in the same window grants nothing.
    f.server
        .handle_frame(&mut f.client.conn, &frame)
        .await
        .expect("replayed frame");
    assert!(sent(&f.client.drain()).is_empty());
    assert_eq!(bag_items(&f).len(), 1);
}

#[tokio::test]
async fn an_entry_with_no_event_on_air_sends_nothing_and_the_tick_stays_quiet() {
    let mut f = fixture(Vec::new(), 50);

    on_enter(&f.server, &f.player);

    assert!(update_lists(&f.client.drain()).is_empty());
    assert_eq!(tick(&f.server), 0);
    assert!(update_lists(&f.client.drain()).is_empty());
}
