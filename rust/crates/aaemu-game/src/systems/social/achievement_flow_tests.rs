//! Achievement flow tests: the live producers and the entry snapshot, driven through the real
//! packet / gameplay entry points (no MySQL).

use std::sync::Arc;

use aaemu_commons::PacketStream;

use crate::data::items::ItemTemplate;
use crate::data::GameData;
use crate::models::container::slot_type;
use crate::net::packet::encode_raw;
use crate::opcodes::{cs, sc};
use crate::server::GameServer;
use crate::systems::economy::inventory::Inv;
use crate::systems::skills::progress::{add_exp_world_tail, ExpGain};
use crate::systems::social::achievement::{
    char_record_kind, AchievementGameData, AchievementObjectives, Achievements, CharRecords,
};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::PlayerRef;

/// The reward item the tests grant.
pub(crate) const REWARD_ITEM: u32 = 7001;

/// Content with one achievement (id `achievement_id`, `complete_num` to complete, optional item
/// reward) watching one record (`record_id`, of `kind_id` with `value1` / `value2`).
pub(crate) fn one_achievement(
    achievement_id: u32,
    complete_num: u32,
    reward_item: u32,
    record_id: u32,
    kind_id: u32,
    value1: u32,
    value2: u32,
) -> AchievementGameData {
    let mut data = AchievementGameData::default();
    let objective = AchievementObjectives {
        id: 1,
        achievement_id,
        record_id,
        ..Default::default()
    };
    data.achievements.insert(
        achievement_id,
        Achievements {
            id: achievement_id,
            complete_num,
            // Sum the record values (the shape of the threshold achievements).
            complete_or: false,
            is_active: true,
            item_id: reward_item,
            achievement_objectives: vec![objective],
            ..Default::default()
        },
    );
    data.achievement_objectives
        .insert(achievement_id, vec![objective]);
    data.char_records.insert(
        record_id,
        CharRecords {
            id: record_id,
            kind_id,
            value1,
            value2,
        },
    );
    data
}

/// A server with the reward item template and the given achievement content, the social hooks
/// installed (the producers report through them).
pub(crate) fn server_with_content(data: AchievementGameData) -> Arc<GameServer> {
    let mut game_data = GameData::empty();
    game_data
        .items
        .templates
        .insert(REWARD_ITEM, ItemTemplate::plain(REWARD_ITEM, 1));
    game_data.systems.social.achievements = data;
    let server = Arc::new(test_server_with(game_data));
    server.set_self();
    crate::systems::social::install_hooks(&server);
    server
}

/// A spawned, online character with an active connection and registered bag / mail escrow.
pub(crate) fn player_of(server: &GameServer) -> (TestClient, PlayerRef) {
    server.names.add_character(1, "Tester", 1);
    let mut client = TestClient::new(server, 1, 1001);
    let player = spawn_player(
        server,
        &client,
        test_character(1, "Tester", 100.0, 100.0),
        0x100,
    );
    player.data().is_online = true;
    client.conn.active_char = Some(player.clone());
    let inventory = Inv::of(&player);
    server.items.add_container(inventory.bag());
    if let Some(escrow) = inventory.get(slot_type::MAIL) {
        server.items.add_container(escrow);
    }
    client.drain();
    (client, player)
}

/// The achievement opcodes among `ops`, in order.
pub(crate) fn achievement_ops(ops: Vec<u16>) -> Vec<u16> {
    ops.into_iter()
        .filter(|op| {
            matches!(
                *op,
                sc::SCAchievementsPacket
                    | sc::SCAchievementChangedPacket
                    | sc::SCAchievementCompletedPacket
                    | sc::SCAchievementItemSentPacket
            )
        })
        .collect()
}

pub(crate) fn bag_count(player: &PlayerRef) -> usize {
    Inv::of(player)
        .bag()
        .lock()
        .expect("container poisoned")
        .items
        .len()
}

#[tokio::test]
async fn entering_the_world_sends_the_achievement_snapshot() {
    // Achievement 1 (reach level 5) is back-credited from the character's level on entry; the
    // list carries it and the reward is paid.
    let server = server_with_content(one_achievement(
        1,
        5,
        REWARD_ITEM,
        10,
        char_record_kind::CHAR_LEVEL,
        0,
        0,
    ));
    let (mut client, player) = player_of(&server);
    player.data().unit.level = 7;

    server
        .handle_frame(
            &mut client.conn,
            &encode_raw(1, cs::CSNotifyInGamePacket, PacketStream::new().as_bytes()),
        )
        .await
        .expect("handler");

    assert_eq!(
        achievement_ops(client.drain_ops()),
        [sc::SCAchievementsPacket, sc::SCAchievementItemSentPacket]
    );
    assert_eq!(bag_count(&player), 1);
    assert!(player.data().achievement_state.is_complete(1));
}

#[tokio::test]
async fn a_level_up_progresses_and_completes_an_achievement_and_pays_the_reward() {
    let server = server_with_content(one_achievement(
        1,
        10,
        REWARD_ITEM,
        10,
        char_record_kind::CHAR_LEVEL,
        0,
        0,
    ));
    let (mut client, player) = player_of(&server);
    player.data().unit.level = 10;
    let gain = ExpGain {
        exp: 100,
        should_add_ability_exp: false,
        new_level: Some(10),
    };

    add_exp_world_tail(&server, &player, &gain);

    assert_eq!(
        achievement_ops(client.drain_ops()),
        [
            sc::SCAchievementChangedPacket,
            sc::SCAchievementCompletedPacket,
            sc::SCAchievementItemSentPacket,
        ]
    );
    assert_eq!(bag_count(&player), 1);
    assert_eq!(player.data().achievement_state.amount(1), 10);
}

#[tokio::test]
async fn a_used_item_counts_towards_its_record() {
    let server = server_with_content(one_achievement(
        1,
        2,
        0,
        10,
        char_record_kind::USE_ITEM,
        555,
        0,
    ));
    let (mut client, player) = player_of(&server);

    crate::systems::quests::events::on_item_use(&server, &player, 555);
    assert_eq!(
        achievement_ops(client.drain_ops()),
        [sc::SCAchievementChangedPacket]
    );
    // Another item is not this record.
    crate::systems::quests::events::on_item_use(&server, &player, 556);
    assert!(achievement_ops(client.drain_ops()).is_empty());

    crate::systems::quests::events::on_item_use(&server, &player, 555);
    assert_eq!(
        achievement_ops(client.drain_ops()),
        [
            sc::SCAchievementChangedPacket,
            sc::SCAchievementCompletedPacket
        ]
    );
}

#[tokio::test]
async fn an_event_under_the_economy_lock_is_queued_until_the_transaction_ends() {
    use crate::systems::economy::inventory::ItemTxn;
    use crate::systems::social::achievement_hooks::{AchievementInput, RecordMatch};

    let server = server_with_content(one_achievement(
        1,
        1,
        REWARD_ITEM,
        10,
        char_record_kind::USE_SKILL,
        77,
        0,
    ));
    let (mut client, player) = player_of(&server);

    let txn = ItemTxn::begin(&server);
    // The economy operation lock is not re-entrant: reporting (and paying the reward, which
    // opens its own transaction) here would deadlock, so the event waits for the release.
    crate::systems::report_achievement(
        &server,
        &player,
        AchievementInput::Increment {
            kind_id: char_record_kind::USE_SKILL,
            amount: 1,
            matcher: RecordMatch::Value1(77),
        },
    );
    assert!(achievement_ops(client.drain_ops()).is_empty());
    drop(txn);

    assert_eq!(
        achievement_ops(client.drain_ops()),
        [
            sc::SCAchievementChangedPacket,
            sc::SCAchievementCompletedPacket,
            sc::SCAchievementItemSentPacket,
        ]
    );
    assert_eq!(bag_count(&player), 1);
}

#[tokio::test]
async fn a_queued_refresh_runs_when_the_packet_is_handled() {
    let server = server_with_content(one_achievement(
        1,
        4,
        0,
        10,
        char_record_kind::CHAR_LEVEL,
        0,
        0,
    ));
    let (mut client, player) = player_of(&server);
    player.data().unit.level = 4;

    // A caller under the team manager's lock cannot report; it queues, and the next handled
    // packet flushes the queue.
    crate::systems::refresh_achievements_later(&server, &player);
    assert!(achievement_ops(client.drain_ops()).is_empty());
    server
        .handle_frame(
            &mut client.conn,
            &encode_raw(1, cs::CSNotifyInGameCompletedPacket, &[]),
        )
        .await
        .expect("handler");

    assert_eq!(
        achievement_ops(client.drain_ops()),
        [
            sc::SCAchievementChangedPacket,
            sc::SCAchievementCompletedPacket
        ]
    );
}
