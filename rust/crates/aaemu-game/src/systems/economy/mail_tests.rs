//! Mail tests: byte-exact packets, C->S decoding, and DB-free mail flows between two players
//! (send with items / money, delivery, listing, reading, taking attachments, deleting, system
//! mails, tax payment, persistence row handling).

use std::sync::Arc;

use aaemu_commons::{Marshal, PacketStream};

use crate::models::char_data::CountUnreadMail;
use crate::models::container::slot_type;
use crate::models::doodad::Doodad;
use crate::models::item::Item;
use crate::models::item_task::{task_type, ItemTask};
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::economy::error_message;
use crate::systems::economy::inventory::{Inv, ItemTxn};
use crate::systems::economy::mail::{
    self, mail_result, mail_status, mail_type, BaseMail, DotNetDict, MailBodyWire, MailHeaderWire,
    MailHousingHooks, MailRow,
};
use crate::systems::economy::mail_character;
use crate::systems::economy::mail_kinds::{
    self, BuildingTaxInfo, CommercialMail, ItemCreationDefinition, MailForAuction,
    MailForSpeciality, MailForTax, TaxMailHouse,
};
use crate::systems::economy::mail_packets::*;
use crate::systems::economy::tests::{give, item_at, run, task_body, World, SINGLE, STACK};
use crate::world::object::Entity;
use crate::world::WorldObject;

fn body<P: Marshal>(p: &P) -> Vec<u8> {
    let mut s = PacketStream::new();
    p.write(&mut s);
    s.into_bytes()
}

fn count(sent: i32, received: i32, mia: i32, commercial: i32) -> CountUnreadMail {
    CountUnreadMail {
        sent,
        received,
        mia_received: mia,
        commercial_received: commercial,
    }
}

fn header() -> MailHeaderWire {
    MailHeaderWire {
        mail_id: 0x0102_0304_0506_0708,
        mail_type: mail_type::EXPRESS,
        status: mail_status::UNREAD,
        title: "Hi".into(),
        sender_name: "Alice".into(),
        attachments: 3,
        receiver_name: "Bob".into(),
        open_date: 1_600_000_000,
        returned: true,
        extra: -2,
    }
}

fn header_bytes(s: &mut PacketStream) {
    s.write_i64(0x0102_0304_0506_0708)
        .write_u8(2)
        .write_u8(0)
        .write_str("Hi")
        .write_str("Alice")
        .write_u8(3)
        .write_str("Bob")
        .write_i64(1_600_000_000)
        .write_bool(true)
        .write_i64(-2);
}

fn count_bytes(s: &mut PacketStream, c: [i32; 4]) {
    for v in c {
        s.write_i32(v);
    }
}

fn test_item(id: u64) -> Item {
    let d = crate::data::ItemData::default();
    let t = crate::data::items::ItemTemplate::plain(1234, 10);
    Item::create(id, &t, 7, 1, 1, &d, 99)
}

fn body_wire() -> MailBodyWire {
    let mut items = vec![None; 10];
    items[0] = Some(test_item(55));
    MailBodyWire {
        mail_id: 9,
        mail_type: mail_type::NORMAL,
        receiver_name: "Bob".into(),
        title: "T".into(),
        text: "X".into(),
        copper_coins: 10,
        billing_amount: 20,
        money_amount2: 30,
        send_date: 100,
        recv_date: 200,
        open_date: 300,
        items,
    }
}

fn body_bytes(s: &mut PacketStream) {
    s.write_i64(9)
        .write_u8(1)
        .write_str("Bob")
        .write_str("T")
        .write_str("X")
        .write_i32(10)
        .write_i32(20)
        .write_i32(30)
        .write_i64(100)
        .write_i64(200)
        .write_i64(300);
    test_item(55).write(s);
    for _ in 1..10 {
        s.write_i32(0);
    }
}

#[test]
fn header_and_body_are_byte_exact() {
    let mut want = PacketStream::new();
    header_bytes(&mut want);
    assert_eq!(body(&header()), want.into_bytes());
    let mut want = PacketStream::new();
    body_bytes(&mut want);
    assert_eq!(body(&body_wire()), want.into_bytes());
}

#[test]
fn server_packets_are_byte_exact() {
    let slots = vec![
        (2u8, 5u8),
        (0, 0),
        (0, 0),
        (0, 0),
        (0, 0),
        (0, 0),
        (0, 0),
        (0, 0),
        (0, 0),
        (3, 1),
    ];
    let mut slot_bytes = PacketStream::new();
    for (a, b) in &slots {
        slot_bytes.write_u8(*a).write_u8(*b);
    }
    let slot_bytes = slot_bytes.into_bytes();

    // SCMailFailedPacket
    let mut want = PacketStream::new();
    want.write_u8(15).write_raw(&slot_bytes).write_bool(false);
    assert_eq!(
        body(&SCMailFailedPacket {
            err: 15,
            items: slots.clone(),
            money: false
        }),
        want.into_bytes()
    );
    // SCMailSentPacket
    let mut want = PacketStream::new();
    header_bytes(&mut want);
    want.write_raw(&slot_bytes);
    assert_eq!(
        body(&SCMailSentPacket {
            mail: header(),
            items: slots.clone()
        }),
        want.into_bytes()
    );
    // SCGotMailPacket without / with body
    let mut want = PacketStream::new();
    header_bytes(&mut want);
    count_bytes(&mut want, [0, 4, 5, 6]);
    want.write_bool(false).write_bool(true);
    assert_eq!(
        body(&SCGotMailPacket {
            mail: header(),
            count: count(0, 4, 5, 6),
            is_cancel: true,
            body: None
        }),
        want.into_bytes()
    );
    let mut want = PacketStream::new();
    header_bytes(&mut want);
    count_bytes(&mut want, [0, 1, 0, 0]);
    want.write_bool(true);
    body_bytes(&mut want);
    want.write_bool(false);
    assert_eq!(
        body(&SCGotMailPacket {
            mail: header(),
            count: count(0, 1, 0, 0),
            is_cancel: false,
            body: Some(body_wire())
        }),
        want.into_bytes()
    );
    // SCMailListPacket
    let mut want = PacketStream::new();
    want.write_bool(true).write_i32(2);
    header_bytes(&mut want);
    header_bytes(&mut want);
    assert_eq!(
        body(&SCMailListPacket {
            is_sent: true,
            mails: vec![header(), header()]
        }),
        want.into_bytes()
    );
    // SCMailListEndPacket
    let mut want = PacketStream::new();
    want.write_i32(3).write_i32(0);
    assert_eq!(
        body(&SCMailListEndPacket {
            total_headers: 3,
            total_bodies: 0
        }),
        want.into_bytes()
    );
    // SCMailBodyPacket
    let mut want = PacketStream::new();
    want.write_bool(false).write_bool(true);
    body_bytes(&mut want);
    want.write_bool(true);
    count_bytes(&mut want, [1, 2, 3, 4]);
    assert_eq!(
        body(&SCMailBodyPacket {
            is_prepare: false,
            is_sent: true,
            body: body_wire(),
            is_open_date_modified: true,
            count: count(1, 2, 3, 4)
        }),
        want.into_bytes()
    );
    // SCMailReceiverOpenedPacket
    let mut want = PacketStream::new();
    want.write_i64(77).write_i64(1234);
    assert_eq!(
        body(&SCMailReceiverOpenedPacket {
            mail_id: 77,
            open_date: 1234
        }),
        want.into_bytes()
    );
    // SCAttachmentTakenPacket
    let mut want = PacketStream::new();
    want.write_i64(77)
        .write_bool(true)
        .write_bool(false)
        .write_bool(true)
        .write_u8(1)
        .write_u64(0xAABB)
        .write_u8(5)
        .write_u8(2);
    for _ in 1..10 {
        want.write_u64(0).write_u8(0).write_u8(0);
    }
    assert_eq!(
        body(&SCAttachmentTakenPacket {
            mail_id: 77,
            money: true,
            aa_point: false,
            take_sequentially: true,
            items: vec![ItemIdAndLocation {
                id: 0xAABB,
                slot_type: 5,
                slot: 2
            }]
        }),
        want.into_bytes()
    );
    // SCChargeMoneyPaidPacket
    let mut want = PacketStream::new();
    want.write_i64(-5);
    assert_eq!(
        body(&SCChargeMoneyPaidPacket { mail_id: -5 }),
        want.into_bytes()
    );
    // SCMailDeletedPacket
    let mut want = PacketStream::new();
    want.write_bool(false).write_i64(77).write_bool(true);
    count_bytes(&mut want, [0, 9, 0, 1]);
    assert_eq!(
        body(&SCMailDeletedPacket {
            is_sent: false,
            mail_id: 77,
            is_unread_mail_count_modified: true,
            count: count(0, 9, 0, 1)
        }),
        want.into_bytes()
    );
    // SCMailReturnedPacket
    let mut want = PacketStream::new();
    want.write_i64(77);
    header_bytes(&mut want);
    assert_eq!(
        body(&SCMailReturnedPacket {
            mail_id: 77,
            mail: header()
        }),
        want.into_bytes()
    );
    // SCMailStatusUpdatedPacket
    let mut want = PacketStream::new();
    want.write_bool(true).write_i64(77).write_u8(2);
    assert_eq!(
        body(&SCMailStatusUpdatedPacket {
            is_sent: true,
            mail_id: 77,
            status: 2
        }),
        want.into_bytes()
    );
    // SCMailRemovedPacket
    let mut want = PacketStream::new();
    want.write_bool(false).write_i64(77);
    assert_eq!(
        body(&SCMailRemovedPacket {
            is_sent: false,
            mail_id: 77
        }),
        want.into_bytes()
    );
    // SCQuestRewardedByMailPacket
    let mut want = PacketStream::new();
    want.write_u8(2).write_u32(10).write_u32(20);
    assert_eq!(
        body(&SCQuestRewardedByMailPacket {
            quests: vec![10, 20]
        }),
        want.into_bytes()
    );
    // SCCountUnreadMailPacket (phase 2)
    let mut want = PacketStream::new();
    count_bytes(&mut want, [0, 1, 2, 3]);
    assert_eq!(
        body(&SCCountUnreadMailPacket {
            count: count(0, 1, 2, 3)
        }),
        want.into_bytes()
    );
}

fn read<P: ClientPacket>(bytes: Vec<u8>) -> P {
    let mut s = PacketStream::from_bytes(bytes);
    P::read(&mut s).expect("decode")
}

#[test]
fn client_packets_decode() {
    let mut s = PacketStream::new();
    s.write_u8(2)
        .write_str("Bob")
        .write_u32(7)
        .write_str("Title")
        .write_str("Text")
        .write_u8(1)
        .write_i32(100)
        .write_i32(2)
        .write_i32(3)
        .write_i64(-9);
    s.write_u8(2).write_u8(4); // item slot
    s.write_u8(0).write_u8(9); // empty, slot ignored
    for _ in 2..10 {
        s.write_u8(0).write_u8(0);
    }
    s.write_bc(0x123);
    let p: CSSendMailPacket = read(s.into_bytes());
    assert_eq!(p.mail_type, 2);
    assert_eq!(p.receiver_char_name, "Bob");
    assert_eq!(p.unk_id, 7);
    assert_eq!((p.title.as_str(), p.text.as_str()), ("Title", "Text"));
    assert_eq!(
        (p.attachments, p.money0, p.money1, p.money2, p.extra),
        (1, 100, 2, 3, -9)
    );
    assert_eq!(p.item_slots[0], (2, 4));
    assert_eq!(p.item_slots[1], (0, 0));
    assert_eq!(p.item_slots.len(), 10);
    assert_eq!(p.doodad_obj_id, 0x123);

    let mut s = PacketStream::new();
    s.write_bool(true).write_i64(42);
    let p: CSReadMailPacket = read(s.into_bytes());
    assert_eq!((p.is_sent, p.mail_id), (true, 42));

    let mut s = PacketStream::new();
    s.write_i64(42).write_bool(true);
    let p: CSDeleteMailPacket = read(s.into_bytes());
    assert_eq!((p.mail_id, p.is_sent), (42, true));

    let mut s = PacketStream::new();
    s.write_i64(42).write_bool(false);
    let p: CSPayChargeMoneyPacket = read(s.into_bytes());
    assert_eq!((p.mail_id, p.auto_use_aa_point), (42, false));

    let mut s = PacketStream::new();
    s.write_i64(42)
        .write_u32(1000)
        .write_u64(0x55)
        .write_u8(1)
        .write_u8(2)
        .write_u32(3)
        .write_u8(0)
        .write_i64(10)
        .write_u32(4)
        .write_u32(5)
        .write_u8(1)
        .write_i64(11)
        .write_i64(12)
        .write_u8(5)
        .write_u8(1);
    let p: CSTakeAttachmentItemPacket = read(s.into_bytes());
    assert_eq!((p.mail_id, p.id, p.slot_type, p.slot), (42, 0x55, 5, 1));
    assert_eq!(
        (p.item_template_id, p.count, p.unpack_date_time),
        (1000, 3, 12)
    );

    let bytes = 42i64.to_le_bytes().to_vec();
    let a: CSTakeAttachmentMoneyPacket = read(bytes.clone());
    let b: CSTakeAttachmentSequentially = read(bytes.clone());
    let c: CSTakeAllAttachmentItemPacket = read(bytes.clone());
    let d: CSReturnMailPacket = read(bytes);
    assert_eq!(
        (a.mail_id, b.mail_id, c.mail_id, d.mail_id),
        (42, 42, 42, 42)
    );
    let _: CSListMailPacket = read(Vec::new());
    let _: CSListMailContinuePacket = read(Vec::new());
}

#[test]
fn dotnet_dict_reuses_freed_slots_lifo() {
    let mut d = DotNetDict::default();
    for k in 1..=5 {
        assert!(d.add(k, k * 10));
    }
    assert!(!d.add(3, 0));
    d.remove(2);
    d.remove(4);
    d.add(6, 60); // takes slot of 4 (last freed)
    d.add(7, 70); // takes slot of 2
    d.add(8, 80);
    assert_eq!(d.keys(), vec![1, 7, 3, 6, 5, 8]);
    assert_eq!(d.get(6), Some(&60));
    assert_eq!(d.len(), 6);
}

#[test]
fn unread_counters_by_type() {
    let mut c = CountUnreadMail::default();
    c.update_received(mail_type::CHARGED, 1);
    c.update_received(mail_type::PROMOTION, 1);
    c.update_received(mail_type::MIA_RECV, 1);
    c.update_received(mail_type::MIA_SEND, 1);
    c.update_received(mail_type::NORMAL, 2);
    assert_eq!(c, count(0, 3, 1, 2));
    c.sent = 4;
    c.reset_received();
    assert_eq!(c, count(4, 0, 0, 0));
}

// ---------------------------------------------------------------------------------------------
// Flows
// ---------------------------------------------------------------------------------------------

/// Two players (Alice = 1, Bob = 2) with registered names and item containers.
fn mail_world() -> World {
    mail_world_with(|_| {})
}

/// [`mail_world`] on modified test data.
fn mail_world_with(f: impl FnOnce(&mut crate::data::GameData)) -> World {
    let w = {
        let mut data = crate::systems::economy::tests::test_data();
        f(&mut data);
        let server = Arc::new(crate::test_support::test_server_with(data));
        server.set_self();
        let mut a = crate::test_support::TestClient::new(&server, 1, 1001);
        let mut b = crate::test_support::TestClient::new(&server, 2, 1002);
        let pa = crate::test_support::spawn_player(
            &server,
            &a,
            crate::test_support::test_character(1, "Alice", 100.0, 100.0),
            0x100,
        );
        let pb = crate::test_support::spawn_player(
            &server,
            &b,
            crate::test_support::test_character(2, "Bob", 101.0, 100.0),
            0x101,
        );
        a.conn.active_char = Some(pa.clone());
        b.conn.active_char = Some(pb.clone());
        a.drain();
        b.drain();
        World {
            server,
            a,
            pa,
            b,
            pb,
        }
    };
    w.server.names.add_character(1, "Alice", 1001);
    w.server.names.add_character(2, "Bob", 1002);
    for p in [&w.pa, &w.pb] {
        for (_, c) in Inv::of(p).containers {
            w.server.items.add_container(c);
        }
    }
    w
}

fn ops(f: &[(u16, Vec<u8>)]) -> Vec<u16> {
    f.iter().map(|x| x.0).collect()
}

fn ten_slots(used: &[(u8, u8)]) -> Vec<(u8, u8)> {
    let mut v = used.to_vec();
    v.resize(10, (0, 0));
    v
}

fn bob_mail_ids(server: &GameServer) -> Vec<i64> {
    server
        .systems
        .economy
        .mail
        .state()
        .all
        .iter()
        .filter(|(_, m)| m.header.receiver_id == 2)
        .map(|(k, _)| k)
        .collect()
}

#[allow(clippy::too_many_arguments)]
fn send_p2p(w: &World, mt: u8, to: &str, money: i32, slots: &[(u8, u8)]) -> u8 {
    let mut txn = ItemTxn::begin(&w.server);
    mail_character::send_mail_to_player(
        &mut txn,
        &w.pa,
        mt,
        to,
        "Hi",
        "Hello Bob",
        0,
        money,
        0,
        0,
        0,
        &ten_slots(slots),
    )
}

#[tokio::test]
async fn send_take_all_and_delete() {
    let mut w = mail_world();
    w.pa.data().money = 1000;
    let stack = give(&w, &w.pa.clone(), slot_type::INVENTORY, STACK, 5, 0);
    let single = give(&w, &w.pa.clone(), slot_type::INVENTORY, SINGLE, 1, 1);

    assert_eq!(
        send_p2p(&w, mail_type::EXPRESS, "Bob", 100, &[(2, 0), (2, 1)]),
        mail_result::SUCCESS
    );
    // Items left Alice's bag for her mail container (renumbered 0, 1).
    assert!(item_at(&w.pa, 2, 0).is_none() && item_at(&w.pa, 2, 1).is_none());
    let mc = Inv::of(&w.pa).get(slot_type::MAIL).unwrap();
    {
        let g = mc.lock().unwrap();
        assert_eq!(
            g.get_item_by_id(stack).map(|i| (i.slot_type, i.slot)),
            Some((5, 0))
        );
        assert_eq!(
            g.get_item_by_id(single).map(|i| (i.slot_type, i.slot)),
            Some((5, 1))
        );
    }
    // Fee: express 100 + 2 extra attachments (2 items + coins - 1 free) * 80 = 260, + 100 coins.
    assert_eq!(w.pa.data().money, 1000 - 360);
    let fa = w.a.drain();
    assert_eq!(
        ops(&fa),
        vec![
            sc::SCItemTaskSuccessPacket,
            sc::SCItemTaskSuccessPacket,
            sc::SCMailSentPacket,
            sc::SCItemTaskSuccessPacket
        ]
    );
    let removed = Item {
        slot_type: slot_type::MAIL,
        slot: 0,
        ..mc.lock().unwrap().get_item_by_id(stack).unwrap().clone()
    };
    assert_eq!(
        fa[0].1,
        task_body(task_type::MAIL, vec![ItemTask::remove(&removed)])
    );
    assert_eq!(
        fa[3].1,
        task_body(task_type::DEPOSIT_MONEY, vec![ItemTask::MoneyChange(-360)])
    );
    let ids = bob_mail_ids(&w.server);
    assert_eq!(ids.len(), 1);
    let id = ids[0];
    let m = w.server.systems.economy.mail.get_mail_by_id(id).unwrap();
    assert_eq!(m.header.attachments, 3);
    assert_eq!(m.body.attachments, vec![stack, single]);
    assert!(m.is_delivered);
    let sent = SCMailSentPacket {
        mail: m.header_wire(),
        items: ten_slots(&[(2, 0), (2, 1)]),
    };
    assert_eq!(fa[2].1, body(&sent));

    // Bob was notified (express: instant).
    let fb = w.b.drain();
    assert_eq!(ops(&fb), vec![sc::SCGotMailPacket]);
    let got = SCGotMailPacket {
        mail: m.header_wire(),
        count: count(0, 1, 0, 0),
        is_cancel: false,
        body: None,
    };
    assert_eq!(fb[0].1, body(&got));
    assert_eq!(w.pb.data().unread_mail.received, 1);

    // Bob opens his mailbox: counts recomputed, got-mail again, one header, end.
    run::<CSListMailPacket>(&mut w, 1, &[]);
    let fb = w.b.drain();
    assert_eq!(
        ops(&fb),
        vec![
            sc::SCGotMailPacket,
            sc::SCMailListPacket,
            sc::SCMailListEndPacket
        ]
    );
    assert_eq!(
        fb[1].1,
        body(&SCMailListPacket {
            is_sent: false,
            mails: vec![m.header_wire()]
        })
    );
    assert_eq!(
        fb[2].1,
        body(&SCMailListEndPacket {
            total_headers: 1,
            total_bodies: 0
        })
    );
    // Alice's view: sent tab
    run::<CSListMailPacket>(&mut w, 0, &[]);
    let fa = w.a.drain();
    assert_eq!(
        ops(&fa),
        vec![
            sc::SCGotMailPacket,
            sc::SCMailListPacket,
            sc::SCMailListEndPacket
        ]
    );
    // The sender counts the unread mail as "received" too (C# quirk).
    assert_eq!(w.pa.data().unread_mail.received, 1);
    assert_eq!(&fa[1].1[..1], &[1]); // isSent

    // Bob reads it.
    let mut s = PacketStream::new();
    s.write_bool(false).write_i64(id);
    run::<CSReadMailPacket>(&mut w, 1, s.as_bytes());
    let fb = w.b.drain();
    assert_eq!(
        ops(&fb),
        vec![
            sc::SCMailBodyPacket,
            sc::SCMailStatusUpdatedPacket,
            sc::SCCountUnreadMailPacket
        ]
    );
    let m = w.server.systems.economy.mail.get_mail_by_id(id).unwrap();
    assert_eq!(m.header.status, mail_status::READ);
    assert!(m.open_date > 0);
    let bw = m.body_wire(&w.server);
    assert_eq!(bw.items[0].as_ref().map(|i| i.id), Some(stack));
    assert_eq!(
        fb[0].1,
        body(&SCMailBodyPacket {
            is_prepare: false,
            is_sent: false,
            body: bw,
            is_open_date_modified: true,
            count: count(0, 0, 0, 0)
        })
    );

    // Bob takes everything.
    w.pb.data().money = 5;
    run::<CSTakeAllAttachmentItemPacket>(&mut w, 1, &id.to_le_bytes());
    assert_eq!(w.pb.data().money, 105);
    let b0 = item_at(&w.pb, 2, 0).unwrap();
    let b1 = item_at(&w.pb, 2, 1).unwrap();
    assert_eq!((b0.id, b0.count, b0.owner_id), (stack, 5, 2));
    assert_eq!((b1.id, b1.owner_id), (single, 2));
    assert!(mc.lock().unwrap().items.is_empty());
    let fb = w.b.drain();
    assert_eq!(
        ops(&fb),
        vec![
            sc::SCItemTaskSuccessPacket, // money
            sc::SCItemTaskSuccessPacket, // item 1 add
            sc::SCItemTaskSuccessPacket, // item 2 add
            sc::SCAttachmentTakenPacket, // money
            sc::SCAttachmentTakenPacket,
            sc::SCAttachmentTakenPacket,
            sc::SCMailStatusUpdatedPacket,
            sc::SCMailDeletedPacket,
            sc::SCCountUnreadMailPacket
        ]
    );
    assert_eq!(
        fb[0].1,
        task_body(task_type::DEPOSIT_MONEY, vec![ItemTask::MoneyChange(100)])
    );
    assert_eq!(
        fb[1].1,
        task_body(task_type::MAIL, vec![ItemTask::add(&b0)])
    );
    assert_eq!(
        fb[4].1,
        body(&SCAttachmentTakenPacket {
            mail_id: id,
            money: true,
            aa_point: false,
            take_sequentially: true,
            items: vec![ItemIdAndLocation {
                id: stack,
                slot_type: slot_type::MAIL,
                slot: 0
            }]
        })
    );
    assert_eq!(
        fb[7].1,
        body(&SCMailDeletedPacket {
            is_sent: false,
            mail_id: id,
            is_unread_mail_count_modified: false,
            count: count(0, 0, 0, 0)
        })
    );
    assert!(w.server.systems.economy.mail.get_mail_by_id(id).is_none());
    assert_eq!(w.server.systems.economy.mail.state().deleted, vec![id]);
}

#[tokio::test]
async fn normal_mail_is_delayed_then_delivered() {
    let mut w = mail_world();
    w.pa.data().money = 1000;
    assert_eq!(
        send_p2p(&w, mail_type::NORMAL, "Bob", 0, &[]),
        mail_result::SUCCESS
    );
    // Fee: 50, no attachments.
    assert_eq!(w.pa.data().money, 950);
    assert!(w.b.drain().is_empty());
    let id = bob_mail_ids(&w.server)[0];
    let mails = &w.server.systems.economy.mail;
    let m = mails.get_mail_by_id(id).unwrap();
    assert!(m.body.recv_date >= mail::now() + mail::NORMAL_MAIL_DELAY_SECS - 2);
    assert!(!m.is_delivered);
    assert_eq!(m.receiver_name, "Bob");
    // The receiver name must match exactly (NameManager key).
    assert_eq!(
        send_p2p(&w, mail_type::NORMAL, "bob", 0, &[]),
        mail_result::UNABLE_TO_FIND_RECIPIENT
    );
    // Not listed yet, not delivered by the timer.
    {
        let mut txn = ItemTxn::begin(&w.server);
        assert!(mail::get_current_mail_list(&mut txn, 2).is_empty());
        mail::check_all_mail_timings(&mut txn);
    }
    assert!(w.b.drain().is_empty());
    // Time passes.
    mails.with_mail_mut(id, |m| m.body.recv_date = mail::now() - 1);
    {
        let mut txn = ItemTxn::begin(&w.server);
        mail::check_all_mail_timings(&mut txn);
    }
    assert_eq!(w.b.drain_ops(), vec![sc::SCGotMailPacket]);
    assert!(mails.get_mail_by_id(id).unwrap().is_delivered);
    {
        let mut txn = ItemTxn::begin(&w.server);
        mail::check_all_mail_timings(&mut txn);
    }
    assert!(w.b.drain().is_empty());
}

#[tokio::test]
async fn send_errors() {
    let mut w = mail_world();
    w.pa.data().money = 120;
    give(&w, &w.pa.clone(), slot_type::INVENTORY, STACK, 5, 0);
    assert_eq!(
        send_p2p(&w, mail_type::EXPRESS, "Nobody", 0, &[]),
        mail_result::UNABLE_TO_FIND_RECIPIENT
    );
    assert_eq!(
        send_p2p(&w, mail_type::EXPRESS, " ", 0, &[]),
        mail_result::UNABLE_TO_FIND_RECIPIENT
    );
    // Empty bag slot
    assert_eq!(
        send_p2p(&w, mail_type::EXPRESS, "Bob", 0, &[(2, 3)]),
        mail_result::INVALID_SLOT
    );
    // 100 fee + 21 coins > 120
    assert_eq!(
        send_p2p(&w, mail_type::EXPRESS, "Bob", 21, &[]),
        mail_result::INSUFFICIENT_COINS
    );
    assert!(item_at(&w.pa, 2, 0).is_some());
    assert!(w.a.drain().is_empty());

    // Through the packet: no mailbox next to Alice.
    let mut s = PacketStream::new();
    s.write_u8(2)
        .write_str("Bob")
        .write_u32(0)
        .write_str("t")
        .write_str("x")
        .write_u8(0)
        .write_i32(0)
        .write_i32(0)
        .write_i32(0)
        .write_i64(0);
    for _ in 0..10 {
        s.write_u8(0).write_u8(0);
    }
    s.write_bc(0x999);
    run::<CSSendMailPacket>(&mut w, 0, s.as_bytes());
    let f = w.a.drain();
    assert_eq!(ops(&f), vec![sc::SCErrorMsgPacket]);
    let err = crate::packets::world::SCErrorMsgPacket::new(
        error_message::MAIL_FAIL_MAILBOX_NOT_FOUND,
        0,
        true,
    );
    assert_eq!(f[0].1, body(&err));
}

fn spawn_mailbox(w: &World, x: f32, func: &str) -> u32 {
    let mut d = Doodad::new(777);
    d.transform.set_xyz(x, 100.0, 100.0);
    d.transform.zone_id = crate::test_support::TEST_ZONE;
    d.current_func_types = vec![func.to_string()];
    let obj_id = 0x500 + x as u32;
    let d = Entity::new(obj_id, d);
    let world = w.server.world.world(0).unwrap();
    world.spawn(WorldObject::from(&d), &w.server.view_ctx());
    obj_id
}

fn send_packet_body(doodad: u32, receiver: &str, mt: u8) -> Vec<u8> {
    let mut s = PacketStream::new();
    s.write_u8(mt)
        .write_str(receiver)
        .write_u32(0)
        .write_str("t")
        .write_str("x")
        .write_u8(0)
        .write_i32(0)
        .write_i32(0)
        .write_i32(0)
        .write_i64(0);
    for _ in 0..10 {
        s.write_u8(0).write_u8(0);
    }
    s.write_bc(doodad);
    s.into_bytes()
}

#[tokio::test]
async fn send_through_mailbox_packet() {
    let mut w = mail_world();
    w.pa.data().money = 1000;
    let far = spawn_mailbox(&w, 110.0, "DoodadFuncNaviOpenMailbox");
    let wrong = spawn_mailbox(&w, 102.0, "DoodadFuncSomethingElse");
    let near = spawn_mailbox(&w, 103.0, "DoodadFuncNaviOpenMailbox");
    w.a.drain();
    w.b.drain();
    for d in [far, wrong] {
        run::<CSSendMailPacket>(&mut w, 0, &send_packet_body(d, "Bob", mail_type::EXPRESS));
        assert_eq!(w.a.drain_ops(), vec![sc::SCErrorMsgPacket]);
    }
    run::<CSSendMailPacket>(
        &mut w,
        0,
        &send_packet_body(near, "Bob", mail_type::EXPRESS),
    );
    let f = w.a.drain();
    assert_eq!(
        ops(&f),
        vec![
            sc::SCMailSentPacket,
            sc::SCItemTaskSuccessPacket,
            sc::SCErrorMsgPacket
        ]
    );
    let ok = crate::packets::world::SCErrorMsgPacket::new(error_message::MAIL_SUCCESS, 0, true);
    assert_eq!(f[2].1, body(&ok));
    assert_eq!(w.b.drain_ops(), vec![sc::SCGotMailPacket]);
    // Failure result goes back as SCMailFailedPacket.
    run::<CSSendMailPacket>(
        &mut w,
        0,
        &send_packet_body(near, "Nobody", mail_type::EXPRESS),
    );
    let f = w.a.drain();
    assert_eq!(ops(&f), vec![sc::SCMailFailedPacket]);
    assert_eq!(
        f[0].1,
        body(&SCMailFailedPacket {
            err: mail_result::UNABLE_TO_FIND_RECIPIENT,
            items: ten_slots(&[]),
            money: false
        })
    );
}

#[tokio::test]
async fn take_stacks_bag_full_and_single_item() {
    let mut w = mail_world();
    w.pa.data().money = 1000;
    let s1 = give(&w, &w.pa.clone(), slot_type::INVENTORY, STACK, 5, 0);
    let s2 = give(&w, &w.pa.clone(), slot_type::INVENTORY, SINGLE, 1, 1);
    assert_eq!(
        send_p2p(&w, mail_type::EXPRESS, "Bob", 0, &[(2, 0), (2, 1)]),
        mail_result::SUCCESS
    );
    let id = bob_mail_ids(&w.server)[0];
    w.b.drain();
    // Bob: STACK x10 in slot 3, and only one free slot left in his bag.
    let own = give(&w, &w.pb.clone(), slot_type::INVENTORY, STACK, 10, 3);
    let bag = Inv::of(&w.pb).bag();
    bag.lock().unwrap().set_container_size(2);
    give(&w, &w.pb.clone(), slot_type::INVENTORY, SINGLE, 1, 0);
    assert_eq!(bag.lock().unwrap().free_slot_count(), 0);

    // Take the stackable attachment alone: merges into slot 3.
    let mut s = PacketStream::new();
    s.write_i64(id).write_u32(STACK).write_u64(s1);
    s.write_raw(&[0u8; 2 + 4 + 1 + 8 + 4 + 4 + 1 + 8 + 8 + 2]);
    run::<CSTakeAttachmentItemPacket>(&mut w, 1, s.as_bytes());
    assert_eq!(
        item_at(&w.pb, 2, 3).map(|i| (i.id, i.count)),
        Some((own, 15))
    );
    let f = w.b.drain();
    assert_eq!(
        ops(&f),
        vec![
            sc::SCItemTaskSuccessPacket,
            sc::SCAttachmentTakenPacket,
            sc::SCMailStatusUpdatedPacket,
            sc::SCCountUnreadMailPacket
        ]
    );
    let m = w.server.systems.economy.mail.get_mail_by_id(id).unwrap();
    assert_eq!(
        (m.header.attachments, m.body.attachments.clone()),
        (1, vec![s2])
    );
    assert_eq!(m.header.status, mail_status::READ);
    // The merged stack object is kept as a loose item (C# `_allItems`).
    // Now the single item: bag full.
    run::<CSTakeAllAttachmentItemPacket>(&mut w, 1, &id.to_le_bytes());
    let f = w.b.drain();
    assert_eq!(ops(&f), vec![sc::SCErrorMsgPacket]);
    assert_eq!(
        f[0].1,
        body(&crate::packets::world::SCErrorMsgPacket::new(
            error_message::BAG_FULL,
            0,
            true
        ))
    );
    assert!(w.server.systems.economy.mail.get_mail_by_id(id).is_some());
    // Delete is refused while attachments remain.
    let mut s = PacketStream::new();
    s.write_i64(id).write_bool(false);
    run::<CSDeleteMailPacket>(&mut w, 1, s.as_bytes());
    assert!(w.b.drain().is_empty());
    assert!(w.server.systems.economy.mail.get_mail_by_id(id).is_some());
}

#[tokio::test]
async fn sequential_take_checks_receiver_and_auction_labor() {
    let mut w = mail_world();
    // An auction payout mail for Alice (seller), coins only.
    let mut txn = ItemTxn::begin(&w.server);
    assert!(mail::send_system_mail(
        &mut txn,
        mail_type::AUC_OFF_SUCCESS,
        ".auctionOffSuccess",
        1,
        "Successful Auction Notice",
        "body",
        360,
        Vec::new()
    ));
    drop(txn);
    let id = w
        .server
        .systems
        .economy
        .mail
        .state()
        .all
        .iter()
        .find(|(_, m)| m.header.receiver_id == 1)
        .map(|(k, _)| k)
        .unwrap();
    w.a.drain();
    // Bob tries to take Alice's mail sequentially.
    run::<CSTakeAttachmentSequentially>(&mut w, 1, &id.to_le_bytes());
    assert_eq!(w.b.drain_ops(), vec![sc::SCErrorMsgPacket]);
    // Alice without labor: no money.
    w.pa.data().money = 0;
    w.pa.data().labor_power = 0;
    run::<CSTakeAttachmentSequentially>(&mut w, 0, &id.to_le_bytes());
    let f = w.a.drain();
    assert_eq!(ops(&f), vec![sc::SCErrorMsgPacket]);
    assert_eq!(w.pa.data().money, 0);
    // With labor: -1 labor, money taken.
    w.pa.data().labor_power = 5;
    w.pa.data()
        .actability
        .actabilities
        .push(crate::models::char_data::Actability {
            id: mail::ACTABILITY_COMMERCE as u32,
            point: 0,
            step: 0,
        });
    run::<CSTakeAttachmentMoneyPacket>(&mut w, 0, &id.to_le_bytes());
    assert_eq!(w.pa.data().money, 360);
    assert_eq!(w.pa.data().labor_power, 4);
    let f = w.a.drain_ops();
    assert_eq!(
        f,
        vec![
            sc::SCCharacterLaborPowerChangedPacket,
            sc::SCItemTaskSuccessPacket,
            sc::SCAttachmentTakenPacket,
            sc::SCMailStatusUpdatedPacket,
            sc::SCCountUnreadMailPacket
        ]
    );
    let m = w.server.systems.economy.mail.get_mail_by_id(id).unwrap();
    assert_eq!((m.body.copper_coins, m.header.attachments), (0, 0));
}

#[tokio::test]
async fn received_mail_operations_reject_the_sender() {
    let mut w = mail_world();
    w.pa.data().money = 1000;
    assert_eq!(
        send_p2p(&w, mail_type::EXPRESS, "Bob", 100, &[]),
        mail_result::SUCCESS
    );
    let id = bob_mail_ids(&w.server)[0];
    let before = w.server.systems.economy.mail.get_mail_by_id(id).unwrap();
    let sender_money = w.pa.data().money;
    let receiver_count = w.pb.data().unread_mail;
    w.a.drain();
    w.b.drain();
    let mut s = PacketStream::new();
    s.write_bool(false).write_i64(id);
    run::<CSReadMailPacket>(&mut w, 0, s.as_bytes());
    let mut s = PacketStream::new();
    s.write_i64(id);
    run::<CSTakeAttachmentMoneyPacket>(&mut w, 0, s.as_bytes());
    run::<CSTakeAllAttachmentItemPacket>(&mut w, 0, s.as_bytes());
    assert!(w.a.drain().is_empty());
    assert_eq!(w.pa.data().money, sender_money);
    assert_eq!(w.pb.data().unread_mail, receiver_count);
    let after = w.server.systems.economy.mail.get_mail_by_id(id).unwrap();
    assert_eq!(after.header.status, before.header.status);
    assert_eq!(after.body.copper_coins, 100);

    // An empty received mail also cannot be deleted through its sender's connection.
    assert_eq!(
        send_p2p(&w, mail_type::EXPRESS, "Bob", 0, &[]),
        mail_result::SUCCESS
    );
    let empty_id = *bob_mail_ids(&w.server).last().unwrap();
    w.a.drain();
    let mut s = PacketStream::new();
    s.write_i64(empty_id).write_bool(false);
    run::<CSDeleteMailPacket>(&mut w, 0, s.as_bytes());
    assert!(w.a.drain().is_empty());
    assert!(w
        .server
        .systems
        .economy
        .mail
        .get_mail_by_id(empty_id)
        .is_some());
}

#[tokio::test]
async fn delete_unread_mail_updates_counter() {
    let mut w = mail_world();
    w.pa.data().money = 1000;
    send_p2p(&w, mail_type::EXPRESS, "Bob", 0, &[]);
    send_p2p(&w, mail_type::EXPRESS, "Bob", 0, &[]);
    assert_eq!(w.pb.data().unread_mail.received, 2);
    w.b.drain();
    let id = bob_mail_ids(&w.server)[0];
    let mut s = PacketStream::new();
    s.write_i64(id).write_bool(false);
    run::<CSDeleteMailPacket>(&mut w, 1, s.as_bytes());
    let f = w.b.drain();
    assert_eq!(
        f[0].1,
        body(&SCMailDeletedPacket {
            is_sent: false,
            mail_id: id,
            is_unread_mail_count_modified: true,
            count: count(0, 1, 0, 0)
        })
    );
    // isSent: ignored
    let id2 = bob_mail_ids(&w.server)[0];
    let mut s = PacketStream::new();
    s.write_i64(id2).write_bool(true);
    run::<CSDeleteMailPacket>(&mut w, 1, s.as_bytes());
    assert!(w.b.drain().is_empty());
    // Mail id recycling: the freed id is reused and leaves the deleted list.
    let st = w.server.systems.economy.mail.state();
    assert_eq!(st.deleted, vec![id]);
}

#[tokio::test]
async fn old_save_ack_preserves_new_deletion_of_recycled_mail_id() {
    let mut w = mail_world();
    w.pa.data().money = 1000;
    assert_eq!(
        send_p2p(&w, mail_type::EXPRESS, "Bob", 0, &[]),
        mail_result::SUCCESS
    );
    let id = bob_mail_ids(&w.server)[0];
    let mut request = PacketStream::new();
    request.write_i64(id).write_bool(false);
    run::<CSDeleteMailPacket>(&mut w, 1, request.as_bytes());
    assert_eq!(w.server.systems.economy.mail.state().deleted, vec![id]);
    let old = w.server.systems.economy.mail.prepare_save();

    // A new message reuses the deleted ID before the old SQL transaction commits.
    assert_eq!(
        send_p2p(&w, mail_type::EXPRESS, "Bob", 0, &[]),
        mail_result::SUCCESS
    );
    assert_eq!(bob_mail_ids(&w.server), vec![id]);
    assert!(w.server.systems.economy.mail.state().deleted.is_empty());
    run::<CSDeleteMailPacket>(&mut w, 1, request.as_bytes());
    assert!(w.server.systems.economy.mail.get_mail_by_id(id).is_none());

    w.server.systems.economy.mail.acknowledge_committed(old);
    assert_eq!(w.server.systems.economy.mail.state().deleted, vec![id]);
    // A failed/retried newer save must retain the deletion until that version commits.
    drop(w.server.systems.economy.mail.prepare_save());
    assert_eq!(w.server.systems.economy.mail.state().deleted, vec![id]);
    let latest = w.server.systems.economy.mail.prepare_save();
    w.server.systems.economy.mail.acknowledge_committed(latest);
    assert!(w.server.systems.economy.mail.state().deleted.is_empty());
}

#[tokio::test]
async fn system_mails_auction_commercial_speciality() {
    let w = mail_world();
    let mut b = w.b;
    // Auction: the sold item (loose here) goes to the buyer.
    let item = w
        .server
        .items
        .create(
            SINGLE,
            1,
            0,
            1,
            &w.server.game_data.items,
            &w.server.ids.item,
            0,
        )
        .unwrap();
    let item_id = item.id;
    let snapshot = item.clone();
    w.server.items.add_loose(item);
    let mut buy = MailForAuction::new(&w.server, &snapshot, 1, 400, 10);
    assert!(buy.finalize_for_sale_buyer(&w.server, 2));
    assert_eq!(buy.mail.body.text, format!("body('Item:{SINGLE}', 1, 400)"));
    assert_eq!(buy.mail.mail_type, mail_type::AUC_BID_WIN);
    assert_eq!(buy.mail.header.sender_name, ".auctionBidWin");
    {
        let mut txn = ItemTxn::begin(&w.server);
        assert!(buy.send(&mut txn));
    }
    assert_eq!(b.drain_ops(), vec![sc::SCGotMailPacket]);
    let it = w.server.items.item_by_id(item_id).unwrap();
    assert_eq!(
        (it.owner_id, it.slot_type, it.slot),
        (2, slot_type::MAIL, 0)
    );
    // Bob takes it from the loose items.
    let id = *bob_mail_ids(&w.server).last().unwrap();
    {
        let mut txn = ItemTxn::begin(&w.server);
        assert_eq!(
            mail_character::get_attached(&mut txn, &w.pb, id, true, true, true, 0),
            Ok(true)
        );
    }
    assert_eq!(item_at(&w.pb, 2, 0).map(|i| i.id), Some(item_id));
    b.drain();

    // Bid fail with the template-only constructor.
    let mut fail = MailForAuction::new_from_template(&w.server, SINGLE, 1, 400, 0);
    assert!(fail.finalize_for_bid_fail(&w.server, 2, 250));
    assert_eq!(fail.mail.body.copper_coins, 250);
    assert_eq!(fail.mail.header.attachments, 1);
    assert_eq!(fail.mail.mail_type, mail_type::AUC_OFF_FAIL);
    // ...the other finalizers need the item (C# NRE).
    let mut cancel = MailForAuction::new_from_template(&w.server, SINGLE, 1, 400, 0);
    assert!(!cancel.finalize_for_cancel(&w.server));
    assert!(
        !MailForAuction::new_from_template(&w.server, SINGLE, 99, 1, 0)
            .finalize_for_fail(&w.server)
    );

    // Commercial mail to online Bob: items into his mail container.
    let gift = w
        .server
        .items
        .create(
            STACK,
            3,
            0,
            1,
            &w.server.game_data.items,
            &w.server.ids.item,
            0,
        )
        .unwrap();
    let gift_id = gift.id;
    let mut cm = CommercialMail::new(2, "Bob", "Alice", vec![gift], true, false, "Bob's Taffy");
    {
        let mut txn = ItemTxn::begin(&w.server);
        cm.finalize_mail(&mut txn);
        assert_eq!(cm.mail.title, "title('Bob\\'s Taffy')");
        assert_eq!(
            cm.mail.body.text,
            "body(true,'Alice','Bob\\'s Taffy')|gift:1;|refund:0;|limit:2100,12,31,00,00,00;"
        );
        assert_eq!(cm.mail.open_date, 946_684_800);
        assert!(cm.send(&mut txn));
    }
    let f = b.drain();
    assert_eq!(ops(&f), vec![sc::SCGotMailPacket]);
    // Charged mails carry the body; commercial counter.
    let got_count_offset = f[0].1.len();
    assert!(got_count_offset > 100);
    assert_eq!(w.pb.data().unread_mail.commercial_received, 1);
    let mc = Inv::of(&w.pb).get(slot_type::MAIL).unwrap();
    assert!(mc.lock().unwrap().get_item_by_id(gift_id).is_some());

    // Speciality: coins for the seller, delayed by the trade pack delay.
    let mut sp = MailForSpeciality::new(
        &w.server,
        &w.pb,
        0,
        9000,
        130,
        mail::ITEM_COINS,
        1000,
        50,
        1350,
        0,
        5,
    );
    // No coin template in the test data -> FinalizeForSeller returns false.
    assert!(!sp.finalize_for_seller(&mut ItemTxn::begin(&w.server)));
    let mut sp = MailForSpeciality::new(&w.server, &w.pb, 0, 9000, 130, STACK, 10, 2, 15, 0, 5);
    {
        let mut txn = ItemTxn::begin(&w.server);
        assert!(sp.finalize_for_seller(&mut txn));
    }
    assert_eq!(sp.mail.body.text, "body('', 130, 0, 0, 0, 0, 2, 0, 0, 15)");
    assert_eq!(sp.mail.title, "Speciality Payment");
    assert_eq!(sp.mail.mail_type, mail_type::SYS_SELL_BACKPACK);
    assert!(sp.mail.body.recv_date >= mail::now() + 480 * 60 - 2);
    let att = w
        .server
        .items
        .item_by_id(sp.mail.body.attachments[0])
        .unwrap();
    assert_eq!(
        (att.template_id, att.count, att.owner_id, att.slot_type),
        (STACK, 15, 2, 5)
    );
}

#[tokio::test]
async fn speciality_coin_text_and_crafter() {
    let server = Arc::new(crate::test_support::test_server_with({
        let mut d = crate::systems::economy::tests::test_data();
        d.items.templates.insert(
            mail::ITEM_COINS,
            crate::data::items::ItemTemplate::plain(mail::ITEM_COINS, 1_000_000),
        );
        d
    }));
    server.names.add_character(1, "Alice", 1001);
    server.names.add_character(3, "Carl", 1003);
    let client = crate::test_support::TestClient::new(&server, 1, 1001);
    let alice = crate::test_support::spawn_player(
        &server,
        &client,
        crate::test_support::test_character(1, "Alice", 100.0, 100.0),
        0x100,
    );
    let mut txn = ItemTxn::begin(&server);
    // payout = (int)(1000 * 130 / 100f) + 50 = 1350; with interest 5% -> 1417
    let mut sp = MailForSpeciality::new(
        &server,
        &alice,
        0,
        9000,
        130,
        mail::ITEM_COINS,
        1000,
        50,
        1350,
        0,
        5,
    );
    assert!(sp.finalize_for_seller(&mut txn));
    assert_eq!(
        sp.mail.body.text,
        "body('', 130, 1350, 1417, 50, 1350, 2, 1, 0, 0)"
    );
    assert_eq!(sp.mail.body.copper_coins, 1350);
    let mut cr = MailForSpeciality::new(
        &server,
        &alice,
        3,
        9000,
        130,
        mail::ITEM_COINS,
        1000,
        50,
        900,
        450,
        5,
    );
    assert!(cr.finalize_for_seller(&mut txn));
    assert_eq!(cr.mail.title, "Speciality Payment [Delivery]");
    assert_eq!(
        cr.mail.body.text,
        "body('', 130, 1350, 1000, 50, 900, 1, 1, 0, 0)"
    );
    let mut cr = MailForSpeciality::new(
        &server,
        &alice,
        3,
        9000,
        130,
        mail::ITEM_COINS,
        1000,
        50,
        900,
        450,
        5,
    );
    assert!(cr.finalize_for_crafter(&mut txn));
    assert_eq!(cr.mail.receiver_name, "Carl");
    assert_eq!(cr.mail.header.receiver_id, 3);
    assert_eq!(cr.mail.title, "Speciality Payment [Crafter]");
    assert_eq!(
        cr.mail.body.text,
        "body('', 130, 1350, 1000, 50, 450, 0, 1, 0, 0)"
    );
}

#[tokio::test]
async fn quest_reward_mails_split_by_ten() {
    let w = mail_world();
    let defs = [
        ItemCreationDefinition::new(SINGLE, 12, -1),
        ItemCreationDefinition::new(STACK, 30, -1),
    ];
    let mut txn = ItemTxn::begin(&w.server);
    let mails = mail_kinds::create_quest_reward_mails(&mut txn, &w.pb, 4242, &defs, 777);
    // 12 singles + 2 stacks (20 + 10) = 14 items -> 2 mails
    assert_eq!(mails.len(), 2);
    assert_eq!(mails[0].mail.body.attachments.len(), 10);
    assert_eq!(mails[1].mail.body.attachments.len(), 4);
    assert_eq!(mails[0].mail.body.copper_coins, 777);
    assert_eq!(mails[1].mail.body.copper_coins, 0);
    assert_eq!(mails[0].mail.title, "4242");
    assert_eq!(mails[0].mail.body.text, "Reward for quest 4242.");
    assert_eq!(mails[0].mail.header.sender_name, ".questReward");
    assert_eq!(mails[0].mail.header.sender_id, 0);
    assert_eq!(mails[0].mail.mail_type, mail_type::SYS_EXPRESS);
    for m in mails {
        assert!(m.send(&mut txn));
    }
    drop(txn);
    let mc = Inv::of(&w.pb).get(slot_type::MAIL).unwrap();
    assert_eq!(mc.lock().unwrap().items.len(), 14);
    assert_eq!(bob_mail_ids(&w.server).len(), 2);
    let mut b = w.b;
    let f = b.drain_ops();
    // 14 ItemRemove packets (FinalizeAttachments) then the 2 notifications.
    assert_eq!(
        f.iter()
            .filter(|o| **o == sc::SCItemTaskSuccessPacket)
            .count(),
        14
    );
    assert_eq!(f.iter().filter(|o| **o == sc::SCGotMailPacket).count(), 2);
}

fn tax_property_data(data: &mut crate::data::GameData, tax: u32) {
    data.zones.zones.insert(
        crate::test_support::TEST_ZONE,
        crate::data::world_data::Zone {
            id: 1,
            zone_key: crate::test_support::TEST_ZONE,
            group_id: 25,
            ..Default::default()
        },
    );
    data.systems
        .housing
        .add_template(crate::systems::housing::data::HousingTemplate {
            id: 77,
            hp: 100,
            taxation: Some(crate::systems::housing::data::Taxation {
                id: 1,
                tax,
                show: true,
            }),
            ..Default::default()
        });
}

fn spawn_tax_property(w: &mut World) {
    let mut house = crate::models::house::House::new(77, 77);
    house.account_id = 1002;
    house.unit.owner_id = 2;
    house.unit.hp = 100;
    house.unit.max_hp = 100;
    house.unit.transform.set_xyz(101.0, 100.0, 100.0);
    house.unit.transform.zone_id = crate::test_support::TEST_ZONE;
    house.place_date = mail::now() - 4 * 86400;
    house.protection_end_date = mail::now() + 3 * 86400;
    house.current_step = -1;
    let house = Entity::new(0x102, house);
    assert!(w.server.systems.housing.manager.insert(&house));
    w.server
        .world
        .world(0)
        .unwrap()
        .spawn(&house, &w.server.view_ctx());
    w.a.drain();
    w.b.drain();
}

#[tokio::test]
async fn tax_mail_and_pay_charge_money() {
    let mut w = mail_world_with(|d| {
        tax_property_data(d, 20_000);
        for t in [mail::ITEM_TAX_CERTIFICATE, mail::ITEM_BOUND_TAX_CERTIFICATE] {
            d.items
                .templates
                .insert(t, crate::data::items::ItemTemplate::plain(t, 1000));
        }
    });
    let house = TaxMailHouse {
        id: 77,
        owner_id: 2,
        account_id: 1002,
        name: "Cottage".into(),
        zone_id: 999_999, // unknown zone -> UpdateTaxInfo fails after setting the receiver
        place_date: 1000,
        protection_end_date: mail::now() + 3 * 24 * 3600,
        base_tax: 250_000,
        heavy_tax: false,
    };
    let tax = BuildingTaxInfo {
        total_tax_amount_due: 500,
        heavy_tax_house_count: 3,
        normal_tax_house_count: 1,
        hostile_tax_rate: 50,
    };
    let mut tm = MailForTax::new(house.clone());
    assert!(!tm.finalize_mail(&w.server, Some(tax)));
    assert_eq!(tm.mail.receiver_name, "Bob");
    // Build the stored tax mail like UpdateTaxInfo would for a known zone.
    let mut m = BaseMail {
        mail_type: mail_type::BILLING,
        title: "title(25)".into(),
        receiver_name: "Bob".into(),
        ..BaseMail::default()
    };
    m.header.sender_name = ".houseTax".into();
    m.header.receiver_id = 2;
    m.header.status = mail_status::UNPAID;
    m.header.extra = (25i64 << 48) + 77;
    m.body.billing_amount = 500;
    m.body.recv_date = mail::now();
    {
        let mut txn = ItemTxn::begin(&w.server);
        assert!(mail::send(&mut txn, m));
    }
    let id = bob_mail_ids(&w.server)[0];
    assert_eq!(
        w.server.systems.economy.mail.get_my_house_mails(77),
        vec![id]
    );
    assert_eq!(mail::extract_extra_for_house((25i64 << 48) + 77), (25, 77));
    w.b.drain();

    let pay = |id: i64| {
        let mut s = PacketStream::new();
        s.write_i64(id).write_bool(false);
        s.into_bytes()
    };
    // No housing hook -> invalid house info.
    run::<CSPayChargeMoneyPacket>(&mut w, 1, &pay(id));
    assert_eq!(w.b.drain_ops(), vec![sc::SCErrorMsgPacket]);
    spawn_tax_property(&mut w);
    w.server
        .systems
        .economy
        .mail
        .hooks
        .housing
        .install(Arc::new(crate::systems::economy::bridges::EconomyComposite));
    // A current property quote charges two certificates, none in the bag.
    w.server
        .systems
        .economy
        .mail
        .with_mail_mut(id, |m| m.body.billing_amount = 20_000);
    run::<CSPayChargeMoneyPacket>(&mut w, 1, &pay(id));
    let f = w.b.drain();
    assert_eq!(
        f[0].1,
        body(&crate::packets::world::SCErrorMsgPacket::new(
            error_message::MAIL_NOT_ENOUGH_MONEY_TO_PAY_TAXES,
            0,
            true
        ))
    );
    // Wrong mail id
    run::<CSPayChargeMoneyPacket>(&mut w, 1, &pay(id + 1000));
    assert_eq!(w.b.drain_ops(), vec![sc::SCErrorMsgPacket]);
    // Pay with one bound + one normal certificate (bound first).
    let bound = give(
        &w,
        &w.pb.clone(),
        slot_type::INVENTORY,
        mail::ITEM_BOUND_TAX_CERTIFICATE,
        1,
        0,
    );
    let normal = give(
        &w,
        &w.pb.clone(),
        slot_type::INVENTORY,
        mail::ITEM_TAX_CERTIFICATE,
        3,
        1,
    );
    let bound_item = item_at(&w.pb, 2, 0).unwrap();
    let before = w.pb.data().unread_mail.received;
    run::<CSPayChargeMoneyPacket>(&mut w, 1, &pay(id));
    assert!(item_at(&w.pb, 2, 0).is_none());
    let normal_item = item_at(&w.pb, 2, 1).unwrap();
    assert_eq!((normal_item.id, normal_item.count), (normal, 2));
    assert_eq!(bound_item.id, bound);
    assert_eq!(w.pb.data().unread_mail.received, before - 1);
    let f = w.b.drain();
    assert_eq!(
        ops(&f),
        vec![
            sc::SCItemTaskSuccessPacket,
            sc::SCItemTaskSuccessPacket,
            sc::SCItemTaskSuccessPacket,
            sc::SCChargeMoneyPaidPacket,
            sc::SCMailDeletedPacket,
            sc::SCCountUnreadMailPacket
        ]
    );
    // ConsumeItem: the removal (RemoveItem), the (empty) consume list, then the count update.
    assert_eq!(
        f[0].1,
        task_body(task_type::MAIL, vec![ItemTask::remove_slot(&bound_item)])
    );
    assert_eq!(f[1].1, task_body(task_type::MAIL, Vec::new()));
    assert_eq!(
        f[2].1,
        task_body(
            task_type::MAIL,
            vec![ItemTask::count_update(&normal_item, -1)]
        )
    );
    assert!(w.server.systems.economy.mail.get_mail_by_id(id).is_none());
}

#[tokio::test]
async fn tax_paid_with_gold_without_tax_item_feature() {
    let mut w = mail_world_with(|d| {
        d.features.set(crate::data::Feature::TaxItem, false);
        tax_property_data(d, 500);
    });
    spawn_tax_property(&mut w);
    w.server
        .systems
        .economy
        .mail
        .hooks
        .housing
        .install(Arc::new(crate::systems::economy::bridges::EconomyComposite));
    let mut m = BaseMail {
        mail_type: mail_type::BILLING,
        receiver_name: "Bob".into(),
        ..BaseMail::default()
    };
    m.header.receiver_id = 2;
    m.header.sender_name = ".houseTax".into();
    m.header.status = mail_status::UNPAID;
    m.header.extra = 77;
    m.body.billing_amount = 500;
    m.body.recv_date = mail::now();
    {
        let mut txn = ItemTxn::begin(&w.server);
        assert!(mail::send(&mut txn, m));
    }
    let id = bob_mail_ids(&w.server)[0];
    w.b.drain();
    let mut s = PacketStream::new();
    s.write_i64(id).write_bool(false);
    w.pb.data().money = 499;
    run::<CSPayChargeMoneyPacket>(&mut w, 1, s.as_bytes());
    assert_eq!(w.b.drain_ops(), vec![sc::SCErrorMsgPacket]);
    w.pb.data().money = 600;
    run::<CSPayChargeMoneyPacket>(&mut w, 1, s.as_bytes());
    assert_eq!(w.pb.data().money, 100);
    let f = w.b.drain();
    assert_eq!(
        ops(&f),
        vec![
            sc::SCItemTaskSuccessPacket,
            sc::SCChargeMoneyPaidPacket,
            sc::SCMailDeletedPacket,
            sc::SCCountUnreadMailPacket
        ]
    );
    assert_eq!(
        f[0].1,
        task_body(task_type::DEPOSIT_MONEY, vec![ItemTask::MoneyChange(-500)])
    );
}

fn send_bob_tax_bill(w: &World, amount: i32) -> i64 {
    let mut bill = BaseMail {
        mail_type: mail_type::BILLING,
        receiver_name: "Bob".into(),
        ..BaseMail::default()
    };
    bill.header.receiver_id = 2;
    bill.header.sender_name = ".houseTax".into();
    bill.header.status = mail_status::UNPAID;
    bill.header.extra = 77;
    bill.body.billing_amount = amount;
    bill.body.recv_date = mail::now();
    let mut txn = ItemTxn::begin(&w.server);
    assert!(mail::send(&mut txn, bill));
    bob_mail_ids(&w.server)[0]
}

#[tokio::test]
async fn tax_bill_rejects_other_recipient_negative_or_read_bill_and_replay() {
    let mut w = mail_world_with(|d| {
        d.features.set(crate::data::Feature::TaxItem, false);
        tax_property_data(d, 500);
    });
    spawn_tax_property(&mut w);
    w.server
        .systems
        .economy
        .mail
        .hooks
        .housing
        .install(Arc::new(crate::systems::economy::bridges::EconomyComposite));
    let id = send_bob_tax_bill(&w, 500);
    w.a.drain();
    w.b.drain();
    w.pa.data().money = 1000;
    w.pb.data().money = 1000;
    let unread_before = w.pb.data().unread_mail;
    let mut body = PacketStream::new();
    body.write_i64(id).write_bool(false);
    run::<CSPayChargeMoneyPacket>(&mut w, 0, body.as_bytes());
    assert_eq!(w.a.drain_ops(), vec![sc::SCErrorMsgPacket]);
    assert_eq!(w.pa.data().money, 1000);
    assert_eq!(w.pb.data().unread_mail, unread_before);
    assert!(w.server.systems.economy.mail.get_mail_by_id(id).is_some());
    w.server
        .systems
        .economy
        .mail
        .with_mail_mut(id, |bill| bill.header.sender_id = 1);
    assert!(w
        .server
        .systems
        .economy
        .mail
        .get_my_house_mails(77)
        .is_empty());
    run::<CSPayChargeMoneyPacket>(&mut w, 1, body.as_bytes());
    assert_eq!(w.b.drain_ops(), vec![sc::SCErrorMsgPacket]);
    assert_eq!(w.pb.data().money, 1000);
    w.server
        .systems
        .economy
        .mail
        .with_mail_mut(id, |bill| bill.header.sender_id = 0);
    for (amount, status, recv_date) in [
        (-1, mail_status::UNPAID, mail::now()),
        (500, mail_status::READ, mail::now()),
        (500, mail_status::UNPAID, mail::now() + 60),
    ] {
        w.server.systems.economy.mail.with_mail_mut(id, |bill| {
            bill.body.billing_amount = amount;
            bill.header.status = status;
            bill.body.recv_date = recv_date;
        });
        run::<CSPayChargeMoneyPacket>(&mut w, 1, body.as_bytes());
        assert_eq!(w.b.drain_ops(), vec![sc::SCErrorMsgPacket]);
        assert_eq!(w.pb.data().money, 1000);
        assert_eq!(w.pb.data().unread_mail, unread_before);
    }
    w.server.systems.economy.mail.with_mail_mut(id, |bill| {
        bill.body.billing_amount = 500;
        bill.header.status = mail_status::UNPAID;
        bill.body.recv_date = mail::now();
    });
    run::<CSPayChargeMoneyPacket>(&mut w, 1, body.as_bytes());
    assert_eq!(w.pb.data().money, 500);
    assert!(w.server.systems.economy.mail.get_mail_by_id(id).is_none());
    w.b.drain();
    run::<CSPayChargeMoneyPacket>(&mut w, 1, body.as_bytes());
    assert_eq!(w.b.drain_ops(), vec![sc::SCErrorMsgPacket]);
    assert_eq!(w.pb.data().money, 500);
}

#[tokio::test]
async fn players_cannot_forge_system_housing_bill_sale_or_demolition_mail() {
    let w = mail_world();
    w.pa.data().money = 1000;
    for kind in [
        mail_type::BILLING,
        mail_type::HOUSING_SALE,
        mail_type::DEMOLISH,
    ] {
        assert_eq!(
            send_p2p(&w, kind, "Bob", 0, &[]),
            mail_result::INVALID_LETTER_FORMAT
        );
        assert_eq!(w.pa.data().money, 1000);
        assert!(bob_mail_ids(&w.server).is_empty());
    }
}

#[derive(Default)]
struct RefusedHousing {
    attempts: std::sync::atomic::AtomicUsize,
}
impl MailHousingHooks for RefusedHousing {
    fn house_exists(&self, _s: &GameServer, id: u32) -> bool {
        id == 77
    }
    fn house_owner_id(&self, _s: &GameServer, id: u32) -> Option<u32> {
        (id == 77).then_some(2)
    }
    fn pay_weekly_tax(&self, _s: &GameServer, _id: u32) -> bool {
        self.attempts
            .fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        false
    }
}

#[tokio::test]
async fn rejected_house_payment_keeps_both_gold_and_certificates_and_unpaid_bill() {
    for certificates in [false, true] {
        let mut w = mail_world_with(|d| {
            d.features.set(crate::data::Feature::TaxItem, certificates);
            tax_property_data(d, 500);
            d.items.templates.insert(
                mail::ITEM_TAX_CERTIFICATE,
                crate::data::items::ItemTemplate::plain(mail::ITEM_TAX_CERTIFICATE, 1000),
            );
        });
        spawn_tax_property(&mut w);
        let housing = Arc::new(RefusedHousing::default());
        w.server
            .systems
            .economy
            .mail
            .hooks
            .housing
            .install(housing.clone());
        let id = send_bob_tax_bill(&w, if certificates { 10_000 } else { 500 });
        let bob = w.pb.clone();
        give(
            &w,
            &bob,
            slot_type::INVENTORY,
            mail::ITEM_TAX_CERTIFICATE,
            3,
            0,
        );
        w.pb.data().money = 1000;
        w.b.drain();
        let before = w.pb.data().unread_mail;
        let mut body = PacketStream::new();
        body.write_i64(id).write_bool(false);
        run::<CSPayChargeMoneyPacket>(&mut w, 1, body.as_bytes());
        assert_eq!(
            housing.attempts.load(std::sync::atomic::Ordering::SeqCst),
            1
        );
        assert_eq!(w.b.drain_ops(), vec![sc::SCErrorMsgPacket]);
        assert_eq!(w.pb.data().money, 1000);
        assert_eq!(item_at(&w.pb, slot_type::INVENTORY, 0).unwrap().count, 3);
        assert_eq!(w.pb.data().unread_mail, before);
        assert_eq!(
            w.server
                .systems
                .economy
                .mail
                .get_mail_by_id(id)
                .unwrap()
                .header
                .status,
            mail_status::UNPAID
        );
    }
}

#[tokio::test]
async fn large_tax_certificate_cost_rounds_up_without_f32_precision_loss() {
    let mut w = mail_world_with(|d| {
        tax_property_data(d, 1_000_000_001);
        d.items.templates.insert(
            mail::ITEM_TAX_CERTIFICATE,
            crate::data::items::ItemTemplate::plain(mail::ITEM_TAX_CERTIFICATE, 1_000_000),
        );
    });
    spawn_tax_property(&mut w);
    w.server
        .systems
        .economy
        .mail
        .hooks
        .housing
        .install(Arc::new(crate::systems::economy::bridges::EconomyComposite));
    let id = send_bob_tax_bill(&w, 1_000_010_000);
    let bob = w.pb.clone();
    give(
        &w,
        &bob,
        slot_type::INVENTORY,
        mail::ITEM_TAX_CERTIFICATE,
        100_001,
        0,
    );
    let mut body = PacketStream::new();
    body.write_i64(id).write_bool(false);
    run::<CSPayChargeMoneyPacket>(&mut w, 1, body.as_bytes());
    assert!(item_at(&w.pb, slot_type::INVENTORY, 0).is_none());
    assert!(w.server.systems.economy.mail.get_mail_by_id(id).is_none());
}

#[tokio::test]
async fn delete_house_mails_notifies_receiver() {
    let w = mail_world();
    let mut m = BaseMail {
        mail_type: mail_type::BILLING,
        receiver_name: "Bob".into(),
        ..BaseMail::default()
    };
    m.header.receiver_id = 2;
    m.header.sender_name = ".houseTax".into();
    m.header.extra = 88;
    m.body.recv_date = mail::now();
    let mut other = m.clone();
    other.header.extra = 89;
    {
        let mut txn = ItemTxn::begin(&w.server);
        mail::send(&mut txn, m);
        mail::send(&mut txn, other);
        mail::delete_house_mails(&mut txn, 88);
    }
    let mut b = w.b;
    assert_eq!(
        b.drain_ops(),
        vec![
            sc::SCGotMailPacket,
            sc::SCGotMailPacket,
            sc::SCMailDeletedPacket
        ]
    );
    assert_eq!(bob_mail_ids(&w.server).len(), 1);
    assert_eq!(w.pb.data().unread_mail.received, 1);
}

#[tokio::test]
async fn send_rejects_name_mismatch_and_returns_to_sender_fails() {
    let w = mail_world();
    let mut m = BaseMail {
        mail_type: mail_type::EXPRESS,
        receiver_name: "Alice".into(),
        ..BaseMail::default()
    };
    m.header.receiver_id = 2; // Bob's id with Alice's name
    let mut txn = ItemTxn::begin(&w.server);
    assert!(!mail::send(&mut txn, m));
    // A deliverable player mail can't be "returned" once delivered.
    w.pa.data().money = 1000;
    drop(txn);
    send_p2p(&w, mail_type::NORMAL, "Bob", 0, &[]);
    let id = bob_mail_ids(&w.server)[0];
    let mut txn = ItemTxn::begin(&w.server);
    // Undelivered normal mail: the swap happens, then the C# Dictionary.Add throws.
    assert!(!mail::return_to_sender(&mut txn, id));
    drop(txn);
    let m = w.server.systems.economy.mail.get_mail_by_id(id).unwrap();
    assert_eq!(
        (m.header.receiver_id, m.receiver_name.as_str()),
        (1, "Alice")
    );
    assert_eq!(
        (m.header.sender_id, m.header.sender_name.as_str()),
        (2, "Bob")
    );
}

#[tokio::test]
async fn load_rows_restore_mails() {
    let w = mail_world();
    let item = give(&w, &w.pa.clone(), slot_type::MAIL, STACK, 2, 0);
    let mut ids = [0u64; 10];
    ids[0] = item;
    ids[1] = 999_999; // orphan
    let rows = vec![
        MailRow {
            id: 5,
            mail_type: 2,
            status: 0,
            title: "t".into(),
            text: "x".into(),
            sender_id: 1,
            sender_name: "Alice".into(),
            attachment_count: 9,
            receiver_id: 2,
            receiver_name: "Bob".into(),
            send_date: 10,
            received_date: 20,
            money1: 5,
            attachment_ids: ids,
            ..MailRow::default()
        },
        MailRow {
            id: 3,
            mail_type: 1,
            receiver_id: 2,
            receiver_name: "Bob".into(),
            received_date: mail::now() + 1000,
            ..MailRow::default()
        },
    ];
    let mails = &w.server.systems.economy.mail;
    mails.load_rows(&w.server, rows, mail::now());
    let m = mails.get_mail_by_id(5).unwrap();
    assert_eq!(m.body.attachments, vec![item]);
    assert_eq!(m.header.attachments, 2); // 1 item + coins
    assert!(m.is_delivered && !m.is_dirty);
    assert!(!mails.get_mail_by_id(3).unwrap().is_delivered);
    assert_eq!(mails.state().all.keys(), vec![5, 3]);
    // Owner reassigned to the receiver.
    assert_eq!(w.server.items.item_by_id(item).unwrap().owner_id, 2);
    // Bob logging in: only delivered mails are listed.
    assert_eq!(mail::get_current_mail_list_for(&w.server, 2), vec![5]);
}

#[test]
#[ignore = "needs AAEMU_COMPACT_DB"]
fn real_compact_db_mail_localization() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let app = std::path::Path::new(&path)
        .parent()
        .unwrap()
        .parent()
        .unwrap();
    let files = crate::data::client_files::ClientFileManager::from_sources(&[], app);
    let d = crate::systems::economy::mail_data::MailData::load(&crate::systems::DataLoadCtx {
        conn: &conn,
        app_path: app,
        files: &files,
        language: "en_us",
    })
    .unwrap();
    eprintln!(
        "mail localization: {} item names, {} quest names",
        d.item_names.len(),
        d.quest_names.len()
    );
    assert_eq!(d.item_name(17675, "x"), "Ewer of Juice");
    assert_eq!(d.item_name(u32::MAX, "Item:1"), "Item:1");
    assert!(d.item_names.len() > 30_000);
    assert!(d.quest_names.len() > 4_000);
}
