//! Player trade: byte-exact packets, C->S decoding and two-player flows through the real
//! packet handlers (no MySQL).

use aaemu_commons::{Marshal, PacketStream};

use crate::models::container::slot_type;
use crate::models::item_task::{task_type, ItemTask};
use crate::net::packet::{encode, PacketDef};
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::systems::economy::inventory::Inv;
use crate::systems::economy::tests::{give, item_at, run, world, World, STACK};
use crate::systems::economy::trade::*;

fn body_of<P: Marshal>(p: &P) -> Vec<u8> {
    let mut s = PacketStream::new();
    p.write(&mut s);
    s.into_bytes()
}

fn bc(v: u32) -> Vec<u8> {
    let mut s = PacketStream::new();
    s.write_bc(v);
    s.into_bytes()
}

fn canceled(reason: i32, me: bool) -> (u16, Vec<u8>) {
    let mut s = PacketStream::new();
    s.write_i32(reason).write_bool(me);
    (sc::SCTradeCanceledPacket, s.into_bytes())
}

fn pair(op: u16, a: bool, b: bool) -> (u16, Vec<u8>) {
    (op, vec![u8::from(a), u8::from(b)])
}

fn i32_frame(op: u16, v: i32) -> (u16, Vec<u8>) {
    (op, v.to_le_bytes().to_vec())
}

/// Alice (0x100) asks Bob (0x101), Bob accepts: Alice is the owner.
fn start(w: &mut World) {
    run::<CSCanStartTradePacket>(w, 0, &bc(0x101));
    assert!(w.a.drain().is_empty());
    assert_eq!(w.b.drain(), vec![(sc::SCCanStartTradePacket, bc(0x100))]);
    run::<CSStartTradePacket>(w, 1, &bc(0x100));
    assert_eq!(w.a.drain(), vec![(sc::SCTradeStartedPacket, bc(0x101))]);
    assert_eq!(w.b.drain(), vec![(sc::SCTradeStartedPacket, bc(0x100))]);
    let t = w.server.systems.economy.trade.trades();
    assert_eq!(t.len(), 1);
    assert_eq!((t[0].owner_obj_id, t[0].target_obj_id), (0x100, 0x101));
}

fn putup_item(w: &mut World, who: u8, st: u8, slot: u8, amount: i32) {
    let mut s = PacketStream::new();
    s.write_u8(st).write_u8(slot).write_i32(amount);
    run::<CSPutupTradeItemPacket>(w, who, s.as_bytes());
}

fn putup_money(w: &mut World, who: u8, amount: i32) {
    run::<CSPutupTradeMoneyPacket>(w, who, &amount.to_le_bytes());
}

fn lock(w: &mut World, who: u8, l: bool) {
    run::<CSTradeLockPacket>(w, who, &[u8::from(l)]);
}

fn ok(w: &mut World, who: u8) {
    run::<CSTradeOkPacket>(w, who, &[]);
}

#[tokio::test]
async fn trade_departure_cancels_peer_and_reconnect_starts_without_stale_cleanup() {
    use crate::test_support::{spawn_player, test_character, TestClient};
    use crate::world::session;
    for disconnect in [false, true] {
        let mut w = world();
        start(&mut w);
        let old_trade = w.server.systems.economy.trade.trades()[0].id;
        let item_id = give(&w, &w.pa, slot_type::INVENTORY, STACK, 2, 0);
        w.pa.data().money = 100;
        w.pb.data().money = 200;
        putup_item(&mut w, 0, slot_type::INVENTORY, 0, 2);
        putup_money(&mut w, 0, 50);
        lock(&mut w, 0, true);
        lock(&mut w, 1, true);
        let trade = &w.server.systems.economy.trade.trades()[0];
        assert!(trade.lock_owner && trade.lock_target);
        w.a.drain();
        w.b.drain();
        let departed = w.pa.clone();
        if disconnect {
            session::on_disconnect(&w.server, &mut w.a.conn).await;
        } else {
            session::leave_world_task(&w.server, Some(&mut w.a.conn), 1, Some(departed.clone()))
                .await;
        }
        assert!(w.server.systems.economy.trade.trades().is_empty());
        let cancellations = |frames: Vec<(u16, Vec<u8>)>| {
            frames
                .into_iter()
                .filter(|(op, _)| *op == sc::SCTradeCanceledPacket)
                .collect::<Vec<_>>()
        };
        assert_eq!(cancellations(w.a.drain()), vec![canceled(0, true)]);
        assert_eq!(cancellations(w.b.drain()), vec![canceled(0, false)]);
        assert_eq!(
            item_at(&departed, slot_type::INVENTORY, 0).unwrap().id,
            item_id
        );
        assert_eq!((departed.data().money, w.pb.data().money), (100, 200));
        assert!(w
            .server
            .world
            .character_by_obj_id(departed.obj_id)
            .is_none());
        let mut reconnected = TestClient::new(&w.server, 3, 1001);
        let player = spawn_player(
            &w.server,
            &reconnected,
            test_character(1, "Alice", 100.0, 100.0),
            departed.obj_id,
        );
        reconnected.conn.active_char = Some(player.clone());
        w.a = reconnected;
        w.pa = player;
        w.a.drain();
        w.b.drain();
        start(&mut w); // The first fresh invitation/acceptance succeeds without a priming cancel.
        assert_ne!(w.server.systems.economy.trade.trades()[0].id, old_trade);
        on_player_departed(&w.server, &departed); // Late old-session cleanup cannot cancel the new actor's trade.
        assert_eq!(w.server.systems.economy.trade.trades().len(), 1);
        assert!(w.a.drain().is_empty() && w.b.drain().is_empty());
    }
}

#[tokio::test]
async fn departure_prunes_invites_without_touching_unrelated_players_trade() {
    use crate::test_support::{spawn_player, test_character, TestClient};
    for departing_recipient in [false, true] {
        let mut w = world();
        run::<CSCanStartTradePacket>(&mut w, 0, &bc(0x101));
        w.b.drain();
        let departing = if departing_recipient { &w.pb } else { &w.pa };
        on_player_departed(&w.server, departing);
        run::<CSStartTradePacket>(&mut w, 1, &bc(0x100));
        assert!(w.server.systems.economy.trade.trades().is_empty());
        assert!(w.a.drain().is_empty() && w.b.drain().is_empty());
        start(&mut w);
        let outsider = TestClient::new(&w.server, 3, 1003);
        let third = spawn_player(
            &w.server,
            &outsider,
            test_character(3, "Third", 100.0, 100.0),
            0x102,
        );
        w.a.drain();
        w.b.drain();
        let before = w.server.systems.economy.trade.trades();
        on_player_departed(&w.server, &third);
        assert_eq!(w.server.systems.economy.trade.trades(), before);
        assert!(w.a.drain().is_empty() && w.b.drain().is_empty());
        cancel_trade(&w.server, w.pa.obj_id, 0, 0);
        w.a.drain();
        w.b.drain();
        run::<CSCanStartTradePacket>(&mut w, 0, &bc(0x101));
        w.b.drain();
        on_player_departed(&w.server, &third);
        run::<CSStartTradePacket>(&mut w, 1, &bc(0x100));
        assert_eq!(w.a.drain(), vec![(sc::SCTradeStartedPacket, bc(0x101))]);
        assert_eq!(w.b.drain(), vec![(sc::SCTradeStartedPacket, bc(0x100))]);
        assert_eq!(w.server.systems.economy.trade.trades().len(), 1);
    }
}

#[tokio::test]
async fn trade_invitation_decline_notifies_requester_and_cannot_be_replayed() {
    let mut w = world();
    // Accepting an unsolicited invitation cannot open a trade on another player.
    run::<CSStartTradePacket>(&mut w, 1, &bc(0x100));
    assert!(w.server.systems.economy.trade.trades().is_empty());
    assert!(w.a.drain().is_empty() && w.b.drain().is_empty());
    run::<CSCanStartTradePacket>(&mut w, 0, &bc(0x101));
    assert_eq!(w.b.drain(), vec![(sc::SCCanStartTradePacket, bc(0x100))]);
    let decline = |id| {
        let mut s = PacketStream::new();
        s.write_bc(id).write_i32(3);
        s.into_bytes()
    };
    // The requester cannot masquerade as the recipient, even using a valid peer id.
    run::<CSCannotStartTradePacket>(&mut w, 0, &decline(0x101));
    assert!(w.a.drain().is_empty() && w.b.drain().is_empty());
    run::<CSCannotStartTradePacket>(&mut w, 1, &decline(0x100));
    let mut expected = PacketStream::new();
    expected.write_bc(0x101).write_i32(3);
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCCannotStartTradePacket, expected.into_bytes())]
    );
    assert!(w.b.drain().is_empty());
    run::<CSCannotStartTradePacket>(&mut w, 1, &decline(0x100));
    run::<CSStartTradePacket>(&mut w, 1, &bc(0x100));
    assert!(w.a.drain().is_empty() && w.b.drain().is_empty());
    assert!(w.server.systems.economy.trade.trades().is_empty());
    // A fresh invitation still follows the normal handshake.
    start(&mut w);
}

#[tokio::test]
async fn packets_layout() {
    let check = |f: Vec<u8>, op: u16, body: &[u8]| {
        assert_eq!(u16::from_le_bytes([f[4], f[5]]), op);
        assert_eq!(&f[6..], body);
    };
    check(
        encode(&SCCanStartTradePacket { obj_id: 0x0a0b0c }),
        sc::SCCanStartTradePacket,
        &[0x0c, 0x0b, 0x0a],
    );
    check(
        encode(&SCCannotStartTradePacket {
            obj_id: 0x0a0b0c,
            reason: 7,
        }),
        sc::SCCannotStartTradePacket,
        &[0x0c, 0x0b, 0x0a, 7, 0, 0, 0],
    );
    check(
        encode(&SCTradeStartedPacket { obj_id: 0x0102 }),
        sc::SCTradeStartedPacket,
        &[2, 1, 0],
    );
    check(
        encode(&SCTradeCanceledPacket {
            reason: -1,
            caused_by_me: true,
        }),
        sc::SCTradeCanceledPacket,
        &[0xff, 0xff, 0xff, 0xff, 1],
    );
    check(
        encode(&SCTradeItemPutupPacket {
            slot_type: 2,
            slot: 5,
            amount: 3,
        }),
        sc::SCTradeItemPutupPacket,
        &[2, 5, 3, 0, 0, 0],
    );
    check(
        encode(&SCTradeItemTookdownPacket {
            slot_type: 2,
            slot: 5,
        }),
        sc::SCTradeItemTookdownPacket,
        &[2, 5],
    );
    check(
        encode(&SCTradeMoneyPutupPacket {
            money_amount: 0x01020304,
        }),
        sc::SCTradeMoneyPutupPacket,
        &[4, 3, 2, 1],
    );
    check(
        encode(&SCOtherTradeMoneyPutupPacket { money_amount: 5 }),
        sc::SCOtherTradeMoneyPutupPacket,
        &[5, 0, 0, 0],
    );
    check(
        encode(&SCTradeLockUpdatePacket {
            my_lock: true,
            other_lock: false,
        }),
        sc::SCTradeLockUpdatePacket,
        &[1, 0],
    );
    check(
        encode(&SCTradeOkUpdatePacket {
            my_ok: false,
            other_ok: true,
        }),
        sc::SCTradeOkUpdatePacket,
        &[0, 1],
    );
    // Items are written with `Item.Write`.
    let w = world();
    let pa = w.pa.clone();
    give(&w, &pa, slot_type::INVENTORY, STACK, 3, 4);
    let item = item_at(&pa, 2, 4).unwrap();
    let mut want = PacketStream::new();
    item.write(&mut want);
    check(
        encode(&SCOtherTradeItemPutupPacket { item: item.clone() }),
        sc::SCOtherTradeItemPutupPacket,
        want.as_bytes(),
    );
    check(
        encode(&SCOtherTradeItemTookdownPacket { item: item.clone() }),
        sc::SCOtherTradeItemTookdownPacket,
        want.as_bytes(),
    );
    // SCTradeMadePacket: action, count, tasks, forceRemove count + ids, u32 0.
    let p = SCTradeMadePacket {
        action: task_type::TRADE,
        tasks: vec![
            ItemTask::MoneyChange(-5),
            ItemTask::ItemRemove {
                slot_type: 2,
                slot: 4,
                item_id: 9,
                template_id: 10,
            },
        ],
        force_remove: vec![11],
    };
    let mut w = PacketStream::new();
    w.write_u8(48)
        .write_u8(2)
        .write_u8(1)
        .write_i32(-5)
        .write_u8(7)
        .write_u8(2)
        .write_u8(4)
        .write_u64(9)
        .write_u32(10)
        .write_u8(1)
        .write_u64(11)
        .write_u32(0);
    check(encode(&p), sc::SCTradeMadePacket, w.as_bytes());
    assert_eq!(SCTradeMadePacket::OPCODE, 0x16a);
}

#[test]
fn client_packets_decode() {
    let mut s = PacketStream::from_bytes(vec![0x0c, 0x0b, 0x0a, 3, 0, 0, 0]);
    assert_eq!(
        CSCannotStartTradePacket::read(&mut s).unwrap(),
        CSCannotStartTradePacket {
            obj_id: 0x0a0b0c,
            reason: 3
        }
    );
    let mut s = PacketStream::from_bytes(vec![2, 7, 9, 0, 0, 0]);
    assert_eq!(
        CSPutupTradeItemPacket::read(&mut s).unwrap(),
        CSPutupTradeItemPacket {
            slot_type: 2,
            slot: 7,
            amount: 9
        }
    );
    let mut s = PacketStream::from_bytes(vec![1, 4]);
    assert_eq!(
        CSTakedownTradeItemPacket::read(&mut s).unwrap(),
        CSTakedownTradeItemPacket {
            slot_type: 1,
            slot: 4
        }
    );
    let mut s = PacketStream::from_bytes(vec![0xfe, 0xff, 0xff, 0xff]);
    assert_eq!(
        CSCancelTradePacket::read(&mut s).unwrap(),
        CSCancelTradePacket { reason: -2 }
    );
    let mut s = PacketStream::from_bytes(vec![1]);
    assert!(CSTradeLockPacket::read(&mut s).unwrap().lock);
    let mut s = PacketStream::from_bytes(vec![2, 1, 0]);
    assert_eq!(
        CSStartTradePacket::read(&mut s).unwrap(),
        CSStartTradePacket { obj_id: 0x102 }
    );
}

#[tokio::test]
async fn full_trade_items_and_money() {
    let mut w = world();
    let pa = w.pa.clone();
    let pb = w.pb.clone();
    pa.data().money = 1000;
    pb.data().money = 500;
    let id = give(&w, &pa, slot_type::INVENTORY, STACK, 5, 3);
    let bob_stack = give(&w, &pb, slot_type::INVENTORY, STACK, 2, 0);
    start(&mut w);

    // Alice puts up her stack (whole item changes hands, the amount is only checked)
    let item = item_at(&pa, 2, 3).unwrap();
    putup_item(&mut w, 0, 2, 3, 2);
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCTradeItemPutupPacket, vec![2, 3, 2, 0, 0, 0])]
    );
    assert_eq!(
        w.b.drain(),
        vec![(sc::SCOtherTradeItemPutupPacket, body_of(&item))]
    );
    // Bob puts up money and an item, then takes the item down again
    putup_money(&mut w, 1, 100);
    assert_eq!(
        w.a.drain(),
        vec![i32_frame(sc::SCOtherTradeMoneyPutupPacket, 100)]
    );
    assert_eq!(
        w.b.drain(),
        vec![i32_frame(sc::SCTradeMoneyPutupPacket, 100)]
    );
    putup_item(&mut w, 1, 2, 0, 1);
    w.a.drain();
    w.b.drain();
    run::<CSTakedownTradeItemPacket>(&mut w, 1, &[2, 0]);
    let bob_item = item_at(&pb, 2, 0).unwrap();
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCOtherTradeItemTookdownPacket, body_of(&bob_item))]
    );
    assert_eq!(
        w.b.drain(),
        vec![(sc::SCTradeItemTookdownPacket, vec![2, 0])]
    );
    assert!(w.server.systems.economy.trade.trades()[0]
        .target_items
        .is_empty());

    // Lock: each side sees (mine, other)
    lock(&mut w, 0, true);
    assert_eq!(
        w.a.drain(),
        vec![pair(sc::SCTradeLockUpdatePacket, true, false)]
    );
    assert_eq!(
        w.b.drain(),
        vec![pair(sc::SCTradeLockUpdatePacket, false, true)]
    );
    lock(&mut w, 0, true); // already locked: nothing
    assert!(w.a.drain().is_empty());
    lock(&mut w, 1, true);
    assert_eq!(
        w.a.drain(),
        vec![pair(sc::SCTradeLockUpdatePacket, true, true)]
    );
    assert_eq!(
        w.b.drain(),
        vec![pair(sc::SCTradeLockUpdatePacket, true, true)]
    );

    ok(&mut w, 0);
    assert_eq!(
        w.a.drain(),
        vec![pair(sc::SCTradeOkUpdatePacket, true, false)]
    );
    assert_eq!(
        w.b.drain(),
        vec![pair(sc::SCTradeOkUpdatePacket, false, true)]
    );
    ok(&mut w, 1);

    // Exchanged
    assert_eq!(pa.data().money, 1100);
    assert_eq!(pb.data().money, 400);
    assert!(item_at(&pa, 2, 3).is_none());
    let moved = Inv::of(&pb).item_by_id(id).expect("bob has the stack");
    assert_eq!(moved.slot_type, slot_type::INVENTORY);
    assert_eq!(moved.slot, 1); // slot 0 holds Bob's own stack
    assert_eq!(moved.owner_id, 2);
    assert_eq!(moved.count, 5);
    assert_eq!(item_at(&pb, 2, 0).unwrap().id, bob_stack);

    let fa = w.a.drain();
    let fb = w.b.drain();
    assert_eq!(fa.len(), 2);
    assert_eq!(fa[0], pair(sc::SCTradeOkUpdatePacket, true, true));
    assert_eq!(fb[0], pair(sc::SCTradeOkUpdatePacket, true, true));
    // The C# builds ItemRemove from the moved item: Alice's removal names Bob's bag slot.
    let made = |tasks: Vec<ItemTask>| {
        body_of(&SCTradeMadePacket {
            action: task_type::TRADE,
            tasks,
            force_remove: Vec::new(),
        })
    };
    assert_eq!(fa[1].0, sc::SCTradeMadePacket);
    assert_eq!(
        fa[1].1,
        made(vec![
            ItemTask::MoneyChange(100),
            ItemTask::ItemRemove {
                slot_type: 2,
                slot: 1,
                item_id: id,
                template_id: STACK
            }
        ])
    );
    assert_eq!(fb.len(), 2);
    assert_eq!(fb[1].0, sc::SCTradeMadePacket);
    assert_eq!(
        fb[1].1,
        made(vec![ItemTask::MoneyChange(-100), ItemTask::add(&moved)])
    );
    assert!(w.server.systems.economy.trade.trades().is_empty());
    assert_eq!(item.id, id);
}

#[tokio::test]
async fn changes_unlock_and_ok_needs_a_lock() {
    let mut w = world();
    w.pa.data().money = 50;
    start(&mut w);
    // Ok without any lock: ignored
    ok(&mut w, 0);
    assert!(w.a.drain().is_empty());
    lock(&mut w, 0, true);
    w.a.drain();
    w.b.drain();
    // Money change opens the lockers
    putup_money(&mut w, 0, 10);
    assert_eq!(
        w.a.drain(),
        vec![
            i32_frame(sc::SCTradeMoneyPutupPacket, 10),
            pair(sc::SCTradeLockUpdatePacket, false, false)
        ]
    );
    assert_eq!(
        w.b.drain(),
        vec![
            i32_frame(sc::SCOtherTradeMoneyPutupPacket, 10),
            pair(sc::SCTradeLockUpdatePacket, false, false)
        ]
    );
    // Unlock request clears both
    lock(&mut w, 1, true);
    w.a.drain();
    w.b.drain();
    lock(&mut w, 0, false);
    assert_eq!(
        w.a.drain(),
        vec![pair(sc::SCTradeLockUpdatePacket, false, false)]
    );
    // More money than owned: the trade is canceled (caused by Alice)
    w.b.drain();
    putup_money(&mut w, 0, 51);
    assert_eq!(w.a.drain(), vec![canceled(0, true)]);
    assert_eq!(w.b.drain(), vec![canceled(0, false)]);
    assert!(w.server.systems.economy.trade.trades().is_empty());
}

#[tokio::test]
async fn cancel_paths() {
    let mut w = world();
    start(&mut w);
    // Bob cancels: Alice (owner) gets causedByMe = false
    run::<CSCancelTradePacket>(&mut w, 1, &5i32.to_le_bytes());
    assert_eq!(w.a.drain(), vec![canceled(5, false)]);
    assert_eq!(w.b.drain(), vec![canceled(5, true)]);
    assert!(w.server.systems.economy.trade.trades().is_empty());

    // Without a trade: only the sender is told
    run::<CSCancelTradePacket>(&mut w, 0, &1i32.to_le_bytes());
    assert_eq!(w.a.drain(), vec![canceled(1, true)]);
    assert!(w.b.drain().is_empty());
    putup_money(&mut w, 1, 0);
    assert_eq!(w.b.drain(), vec![canceled(0, true)]);
    ok(&mut w, 1);
    assert_eq!(w.b.drain(), vec![canceled(0, true)]);

    // Asking again while trading cancels the running trade (IsTrading) and stops
    start(&mut w);
    run::<CSCanStartTradePacket>(&mut w, 0, &bc(0x101));
    assert_eq!(w.a.drain(), vec![canceled(0, true)]);
    assert_eq!(w.b.drain(), vec![canceled(0, false)]);
    assert!(w.server.systems.economy.trade.trades().is_empty());

    // Putting up an item the player does not have: amount check fails -> cancel
    start(&mut w);
    let pa = w.pa.clone();
    give(&w, &pa, slot_type::INVENTORY, STACK, 2, 0);
    putup_item(&mut w, 0, 2, 0, 3);
    assert_eq!(w.a.drain(), vec![canceled(0, true)]);
    assert_eq!(w.b.drain(), vec![canceled(0, false)]);
    // Empty slot with a running trade: the C# throws, nothing happens
    start(&mut w);
    putup_item(&mut w, 0, 2, 9, 1);
    assert!(w.a.drain().is_empty());
    assert_eq!(w.server.systems.economy.trade.trades().len(), 1);
    // Public cancel (for leave-world wiring)
    cancel_trade(&w.server, 0x100, 0, 0);
    assert_eq!(w.a.drain(), vec![canceled(0, true)]);
    assert_eq!(w.b.drain(), vec![canceled(0, false)]);
}

#[tokio::test]
async fn bag_full_cancels() {
    let mut w = world();
    let pa = w.pa.clone();
    let pb = w.pb.clone();
    let id = give(&w, &pa, slot_type::INVENTORY, STACK, 1, 0);
    for slot in 0..50 {
        give(&w, &pb, slot_type::INVENTORY, STACK, 20, slot);
    }
    assert_eq!(Inv::of(&pb).free_slot_count(slot_type::INVENTORY), 0);
    start(&mut w);
    putup_item(&mut w, 0, 2, 0, 1);
    lock(&mut w, 0, true);
    lock(&mut w, 1, true);
    ok(&mut w, 0);
    w.a.drain();
    w.b.drain();
    ok(&mut w, 1);
    // Bob (target) has no room: canceled on his behalf
    assert_eq!(
        w.a.drain(),
        vec![
            pair(sc::SCTradeOkUpdatePacket, true, true),
            canceled(0, false)
        ]
    );
    assert_eq!(
        w.b.drain(),
        vec![
            pair(sc::SCTradeOkUpdatePacket, true, true),
            canceled(0, true)
        ]
    );
    assert_eq!(item_at(&pa, 2, 0).unwrap().id, id);
    assert!(w.server.systems.economy.trade.trades().is_empty());
}

#[tokio::test]
async fn money_spent_after_putup_cancels() {
    let mut w = world();
    let pa = w.pa.clone();
    pa.data().money = 100;
    start(&mut w);
    putup_money(&mut w, 0, 100);
    lock(&mut w, 0, true);
    lock(&mut w, 1, true);
    ok(&mut w, 0);
    pa.data().money = 50;
    w.a.drain();
    w.b.drain();
    ok(&mut w, 1);
    assert_eq!(
        w.a.drain(),
        vec![
            pair(sc::SCTradeOkUpdatePacket, true, true),
            canceled(0, true)
        ]
    );
    assert_eq!(
        w.b.drain(),
        vec![
            pair(sc::SCTradeOkUpdatePacket, true, true),
            canceled(0, false)
        ]
    );
    assert_eq!(pa.data().money, 50);
    assert_eq!(w.pb.data().money, 0);
}
