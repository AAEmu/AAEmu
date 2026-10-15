//! Ranking flows without MySQL: the client requests, scores recorded at an event, and the reward
//! letters of a closed period.

use std::sync::Arc;

use chrono::{DateTime, NaiveDateTime, Utc};

use aaemu_commons::PacketStream;

use super::packets::body;
use super::ranking_data::{
    day_of_week, RankDefinition, RankKind, RankReward, RankRewardLink, RankScope, RankScopeLink,
    RankingGameData,
};
use super::ranking_packets::{
    SCRankCharacterPacket, SCRankRewardMailPacket, SCRankSnapshotPacket,
    CS_RANK_SNAPSHOT_PACKET_OPCODE,
};
use super::ranking_runtime::{period_or_last, RankEvent};
use crate::data::items::ItemTemplate;
use crate::data::GameData;
use crate::models::container::slot_type;
use crate::net::packet::encode_raw;
use crate::opcodes::{cs, sc};
use crate::server::GameServer;
use crate::systems::economy::inventory::Inv;
use crate::systems::economy::mail::mail_type;
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::PlayerRef;

/// The arena board id of the fixture.
pub(crate) const ARENA: u32 = 1;
/// The longest-catch board of the fixture.
pub(crate) const FISH_LENGTH: u32 = 2;
/// The zone group the fixture's arena board runs in.
pub(crate) const ARENA_ZONE_GROUP: u32 = 77;
/// Reward item of place 1 / of places 2-3.
pub(crate) const FIRST_ITEM: u32 = 7001;
pub(crate) const OTHER_ITEM: u32 = 7002;

pub(crate) fn ts(s: &str) -> DateTime<Utc> {
    NaiveDateTime::parse_from_str(s, "%Y-%m-%d %H:%M:%S")
        .unwrap()
        .and_utc()
}

/// The arena board (4-week periods since 2024-01-01, whole period open, zone group 77), the
/// longest-catch board (weekly, zone group 49) and the rewards of the arena board: place 1 pays
/// [`FIRST_ITEM`] at grade 5, places 2-3 pay [`OTHER_ITEM`].
pub(crate) fn ranking_data() -> RankingGameData {
    let board = |id, kind: RankKind, zone_group_id, reset_week| RankDefinition {
        id,
        name: format!("board {id}"),
        rank_kind_id: kind.id(),
        zone_group_id,
        reset_week,
        day_of_week_id: day_of_week::NO_DAY,
        st_year: 2024,
        st_month: 1,
        st_day: 1,
        ..Default::default()
    };
    let scope = |id, from, to| RankScope {
        id,
        name: format!("scope {id}"),
        scope_from: from,
        scope_to: to,
        ..Default::default()
    };
    let reward = |id, item_id, grade| RankReward {
        id,
        name: format!("reward {id}"),
        item_id,
        item_grade_id: grade,
        item_count: 1,
        weeks: 4,
        ..Default::default()
    };
    let mut data = RankingGameData::default();
    data.ranks = vec![
        board(ARENA, RankKind::Arena, ARENA_ZONE_GROUP, 4),
        board(FISH_LENGTH, RankKind::FishingLength, 49, 1),
        board(4, RankKind::Racing, 49, 1),
    ];
    data.scopes = vec![scope(1, 1, 1), scope(2, 2, 3)];
    data.rewards = vec![reward(1, FIRST_ITEM, 5), reward(2, OTHER_ITEM, 0)];
    data.scope_links = vec![
        RankScopeLink {
            id: 1,
            rank_id: ARENA,
            rank_scope_id: 1,
        },
        RankScopeLink {
            id: 2,
            rank_id: ARENA,
            rank_scope_id: 2,
        },
    ];
    data.reward_links = vec![
        RankRewardLink {
            id: 1,
            rank_id: ARENA,
            rank_scope_id: 1,
            rank_reward_id: 1,
        },
        RankRewardLink {
            id: 2,
            rank_id: ARENA,
            rank_scope_id: 2,
            rank_reward_id: 2,
        },
    ];
    data.index_links();
    data
}

pub(crate) struct Pc {
    pub(crate) c: TestClient,
    pub(crate) p: PlayerRef,
}

pub(crate) fn game_data() -> GameData {
    let mut data = GameData::empty();
    for id in [FIRST_ITEM, OTHER_ITEM] {
        data.items.templates.insert(id, ItemTemplate::plain(id, 1));
    }
    data.systems.social.ranking = ranking_data();
    data
}

fn pc(server: &GameServer, id: u32, name: &str) -> Pc {
    server.names.add_character(id, name, 1);
    let c = TestClient::new(server, id, 1000 + id);
    let p = spawn_player(server, &c, test_character(id, name, 0.0, 0.0), 0x100 + id);
    p.data().is_online = true;
    // The item manager resolves mail attachments through the registered containers.
    let inventory = Inv::of(&p);
    server.items.add_container(inventory.bag());
    if let Some(escrow) = inventory.get(slot_type::MAIL) {
        server.items.add_container(escrow);
    }
    let mut c = c;
    c.conn.active_char = Some(p.clone());
    c.drain();
    Pc { c, p }
}

fn fixture() -> (Arc<GameServer>, Pc, Pc, Pc) {
    let server = Arc::new(test_server_with(game_data()));
    server.set_self();
    let a = pc(&server, 1, "Alpha");
    let b = pc(&server, 2, "Bravo");
    let c = pc(&server, 3, "Charlie");
    // Drop the frames of the spawns (the players seeing each other).
    let (mut a, mut b, mut c) = (a, b, c);
    for p in [&mut a, &mut b, &mut c] {
        p.c.drain();
    }
    (server, a, b, c)
}

fn arena(server: &GameServer, p: &Pc, points: i32, wins: i32, now: DateTime<Utc>) -> usize {
    server.systems.social.ranking.record(
        server,
        &p.p,
        RankEvent::Arena { points, wins },
        ARENA_ZONE_GROUP,
        now,
    )
}

async fn send(server: &GameServer, p: &mut Pc, opcode: u16, body: &[u8]) {
    server
        .handle_frame(&mut p.c.conn, &encode_raw(1, opcode, body))
        .await
        .expect("handler");
}

#[tokio::test]
async fn an_event_scores_only_the_boards_it_belongs_to() {
    let (server, a, _b, _c) = fixture();
    let now = ts("2024-01-10 12:00:00");
    let ranking = &server.systems.social.ranking;
    // The arena result lands on the arena board only.
    assert_eq!(arena(&server, &a, 4, 1, now), 1);
    let period = ts("2024-01-01 00:00:00").timestamp();
    let score = ranking.score_of(1, ARENA, period).expect("arena score");
    assert_eq!((score.v1, score.v2), (4, 1));
    assert_eq!(score.account, "Alpha");
    assert_eq!(score.world_id, 1);
    // A second result adds up; a different zone group is refused.
    assert_eq!(arena(&server, &a, 1, 0, now), 1);
    assert_eq!(
        server.systems.social.ranking.record(
            &server,
            &a.p,
            RankEvent::Arena { points: 9, wins: 1 },
            49,
            now
        ),
        0
    );
    let score = ranking.score_of(1, ARENA, period).unwrap();
    assert_eq!((score.v1, score.v2), (5, 1));
    // A catch outside the fishing board's zone group does not score, in its group it does.
    let catch = RankEvent::Catch {
        length: 12.4,
        weight: 3.0,
    };
    assert_eq!(ranking.record(&server, &a.p, catch, 77, now), 0);
    assert_eq!(ranking.record(&server, &a.p, catch, 49, now), 1);
}

#[tokio::test]
async fn the_character_request_answers_with_the_requesters_own_rows() {
    let (server, mut a, _b, _c) = fixture();
    let now = Utc::now();
    arena(&server, &a, 4, 1, now);

    send(&server, &mut a, cs::CSRankCharacterPacket, &[]).await;
    let frames = a.c.drain();
    assert_eq!(frames.len(), 1, "{frames:?}");
    assert_eq!(frames[0].0, sc::SCRankCharacterPacket);

    // The answer is the packet the manager builds for that moment: take the stamp from the frame.
    let ack_time = u64::from_le_bytes(frames[0].1[6..14].try_into().unwrap());
    let ack_at = DateTime::from_timestamp(ack_time as i64, 0).unwrap();
    let expected = server
        .systems
        .social
        .ranking
        .personal_packet(&server, &a.p, ack_at);
    assert_eq!(frames[0].1, body(&expected));

    // One row per supported board (never the racing one); the arena row carries the score.
    assert_eq!(expected.type_id, 0);
    assert_eq!(expected.error, 0);
    let ids: Vec<u32> = expected.rows.iter().map(|r| r.rank_id).collect();
    assert_eq!(ids, [ARENA, FISH_LENGTH]);
    let row = &expected.rows[0];
    assert_eq!((row.v1, row.v2), (4, 1));
    assert_eq!((row.sum_v1, row.sum_v2), (true, true));
    assert_eq!(row.character_id, 1);
    assert_eq!(row.account, "Alpha");
    assert!(row.recorded > 0);
    let empty = &expected.rows[1];
    assert_eq!((empty.v1, empty.v2, empty.recorded), (0, 0, 0));
    assert!(!empty.sum_v1);
}

#[tokio::test]
async fn the_snapshot_request_answers_with_the_standings() {
    let (server, mut a, b, c) = fixture();
    let now = Utc::now();
    arena(&server, &a, 4, 1, now);
    arena(&server, &b, 9, 2, now);
    arena(&server, &c, 1, 0, now);

    send(
        &server,
        &mut a,
        CS_RANK_SNAPSHOT_PACKET_OPCODE,
        &ARENA.to_le_bytes(),
    )
    .await;
    let frames = a.c.drain();
    assert_eq!(frames.len(), 1, "{frames:?}");
    assert_eq!(frames[0].0, sc::SCRankSnapshotPacket);

    let taken = u64::from_le_bytes(frames[0].1[14..22].try_into().unwrap());
    let taken_at = DateTime::from_timestamp(taken as i64, 0).unwrap();
    let expected = server
        .systems
        .social
        .ranking
        .snapshot_packet(&server, ARENA, taken_at)
        .expect("arena snapshot");
    assert_eq!(frames[0].1, body(&expected));

    // Best first: Bravo (9), Alpha (4), Charlie (1); the requester's own line is among them.
    let places: Vec<(u32, u32, i32)> = expected
        .entries
        .iter()
        .map(|e| (e.ranking, e.character_id, e.v1))
        .collect();
    assert_eq!(places, [(1, 2, 9), (2, 1, 4), (3, 3, 1)]);
    assert_eq!(expected.rank_id, ARENA);
    assert_eq!(expected.error, 0);
    let board = server.game_data.systems.social.ranking.rank(ARENA).unwrap();
    assert_eq!(
        expected.reset_updated,
        period_or_last(board, taken_at).unwrap().end.timestamp() as u64
    );

    // The racing board and an unknown board are not answered.
    send(
        &server,
        &mut a,
        CS_RANK_SNAPSHOT_PACKET_OPCODE,
        &4u32.to_le_bytes(),
    )
    .await;
    send(
        &server,
        &mut a,
        CS_RANK_SNAPSHOT_PACKET_OPCODE,
        &99u32.to_le_bytes(),
    )
    .await;
    assert!(a.c.drain().is_empty());
}

#[tokio::test]
async fn a_closed_period_mails_each_place_its_reward_once() {
    let (server, mut a, mut b, c) = fixture();
    let ranking = &server.systems.social.ranking;
    let played = ts("2024-01-10 12:00:00");
    arena(&server, &a, 4, 1, played);
    arena(&server, &b, 9, 2, played);
    arena(&server, &c, 1, 0, played);
    // Charlie logs out: offline winners are mailed too.
    server.world.try_remove_character(c.p.obj_id);
    a.c.drain();
    b.c.drain();

    // The period [2024-01-01, 2024-01-29) is still running: nothing is due.
    assert_eq!(ranking.pay_closed(&server, ts("2024-01-28 23:59:59")), 0);
    assert!(!ranking.is_paid(ARENA, ts("2024-01-01 00:00:00").timestamp()));

    // Closed: Bravo placed 1st (FIRST_ITEM, grade 5), Alpha and Charlie 2nd / 3rd.
    assert_eq!(ranking.pay_closed(&server, ts("2024-01-29 00:00:01")), 3);
    assert!(ranking.is_paid(ARENA, ts("2024-01-01 00:00:00").timestamp()));

    let letters: Vec<_> = {
        let mails = server.systems.economy.mail.state();
        let mut letters: Vec<_> = mails
            .all
            .iter()
            .map(|(_, m)| {
                (
                    m.header.receiver_id,
                    m.mail_type,
                    m.header.sender_name.clone(),
                    m.body.attachments.clone(),
                )
            })
            .collect();
        letters.sort_by_key(|l| l.0);
        letters
    };
    assert_eq!(letters.len(), 3);
    let expected_item = [OTHER_ITEM, FIRST_ITEM, OTHER_ITEM];
    for ((receiver, kind, sender, attachments), (id, template)) in
        letters.iter().zip([1, 2, 3].into_iter().zip(expected_item))
    {
        assert_eq!(*receiver, id);
        assert_eq!(*kind, mail_type::SYS_EXPRESS);
        assert_eq!(sender, super::ranking_manager::REWARD_MAIL_SENDER);
        assert_eq!(attachments.len(), 1);
        let item = server
            .items
            .item_by_id(attachments[0])
            .expect("reward item is tracked");
        assert_eq!(item.template_id, template);
        assert_eq!(item.count, 1);
        if template == FIRST_ITEM {
            assert_eq!(item.grade, 5);
        }
    }

    // Online winners are told with the rank reward notice, the offline one is not.
    let notice = body(&SCRankRewardMailPacket { rank_id: ARENA });
    let sent = |frames: Vec<(u16, Vec<u8>)>| {
        frames
            .into_iter()
            .filter(|(op, _)| *op == sc::SCRankRewardMailPacket)
            .map(|(_, b)| b)
            .collect::<Vec<_>>()
    };
    assert_eq!(sent(a.c.drain()), std::slice::from_ref(&notice));
    assert_eq!(sent(b.c.drain()), [notice]);

    // The period is settled: a later pass (or a restart reading the ledger) pays nothing more.
    assert_eq!(ranking.pay_closed(&server, ts("2024-02-01 00:00:00")), 0);
    assert_eq!(server.systems.economy.mail.state().all.len(), 3);
}

#[test]
fn the_wire_types_still_use_the_verified_opcodes() {
    // The handlers answer with these three; a rename of an opcode must fail here, not live.
    use crate::net::packet::PacketDef;
    assert_eq!(SCRankCharacterPacket::OPCODE, sc::SCRankCharacterPacket);
    assert_eq!(SCRankSnapshotPacket::OPCODE, sc::SCRankSnapshotPacket);
    assert_eq!(SCRankRewardMailPacket::OPCODE, sc::SCRankRewardMailPacket);
    let mut s = PacketStream::new();
    s.write_u32(1);
    assert_eq!(body(&SCRankRewardMailPacket { rank_id: 1 }), s.into_bytes());
}
