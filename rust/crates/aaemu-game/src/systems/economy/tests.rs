//! Flow tests of the core item layer (no MySQL): moves / splits / merges / equips through the
//! real packet handlers, destroy, money, acquire with timers, bag expansion.

use std::sync::Arc;

use aaemu_commons::{Marshal, PacketStream};

use crate::data::items::{bind_type, EquipTemplate, ItemTemplate, TemplateKind};
use crate::data::GameData;
use crate::models::container::{equip_slot, slot_type};
use crate::models::item::{Item, ItemClass, ITEM_FLAG_SOUL_BOUND};
use crate::models::item_task::{task_type, ItemTask};
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::economy::data::Expand;
use crate::systems::economy::inventory::{self, Inv, ItemTxn};
use crate::systems::economy::item_handlers::*;
use crate::systems::economy::item_packets::SCItemTaskSuccessPacket;
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::PlayerRef;

pub const STACK: u32 = 1000; // max 20
pub const ARMOR: u32 = 2000; // chest, bind on equip
pub const TIMED: u32 = 3000; // 60 minutes absolute lifetime
pub const SINGLE: u32 = 4000; // max 1

pub fn test_data() -> GameData {
    let mut d = GameData::empty();
    d.items.grades.insert(
        0,
        crate::data::items::GradeTemplate {
            grade: 0,
            grade_order: 0,
            durability: 1.0,
        },
    );
    d.items.config.durability_const = 1.0;
    let mut t = ItemTemplate::plain(STACK, 20);
    t.price = 10;
    t.refund = 5;
    t.sellable = true;
    d.items.templates.insert(STACK, t);
    d.items.templates.insert(
        ARMOR,
        ItemTemplate {
            bind_type: bind_type::BIND_ON_EQUIP,
            ..ItemTemplate {
                kind: TemplateKind::Armor(EquipTemplate {
                    slot_type_id: 3,
                    coverage: 10,
                    durability_ratio: 1.0,
                    durability_multiplier: 100,
                    ..EquipTemplate::default()
                }),
                ..ItemTemplate::plain(ARMOR, 1)
            }
        },
    );
    d.items.templates.insert(
        TIMED,
        ItemTemplate {
            exp_abs_lifetime: 60,
            ..ItemTemplate::plain(TIMED, 5)
        },
    );
    d.items
        .templates
        .insert(SINGLE, ItemTemplate::plain(SINGLE, 1));
    d.systems.economy.expands.insert(
        0,
        vec![
            Expand {
                is_bank: false,
                step: 0,
                price: 100,
                item_id: 0,
                item_count: 0,
                currency_id: 0,
            },
            Expand {
                is_bank: true,
                step: 0,
                price: 0,
                item_id: STACK,
                item_count: 2,
                currency_id: 0,
            },
        ],
    );
    d
}

#[allow(dead_code)]
pub struct World {
    pub server: Arc<GameServer>,
    pub a: TestClient,
    pub pa: PlayerRef,
    pub b: TestClient,
    pub pb: PlayerRef,
}

/// Two players next to each other (char ids 1 / 2).
pub fn world() -> World {
    let server = Arc::new(test_server_with(test_data()));
    server.set_self();
    let mut a = TestClient::new(&server, 1, 1001);
    let mut b = TestClient::new(&server, 2, 1002);
    let pa = spawn_player(&server, &a, test_character(1, "Alice", 100.0, 100.0), 0x100);
    let pb = spawn_player(&server, &b, test_character(2, "Bob", 101.0, 100.0), 0x101);
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
}

/// Puts a new item of `template` x `count` into `st` at `slot` (no packets).
pub fn give(w: &World, p: &PlayerRef, st: u8, template: u32, count: i32, slot: i32) -> u64 {
    let inv = Inv::of(p);
    let item = w
        .server
        .items
        .create(
            template,
            count,
            0,
            1,
            &w.server.game_data.items,
            &w.server.ids.item,
            0,
        )
        .expect("item");
    let id = item.id;
    let c = inv.get(st).unwrap();
    let mut c = c.lock().unwrap();
    assert!(c
        .add_new_item(item, slot, &w.server.game_data.items)
        .is_ok());
    id
}

pub fn item_at(p: &PlayerRef, st: u8, slot: u8) -> Option<Item> {
    Inv::of(p).item_at(st, slot)
}

/// Decodes the body of an `SCItemTaskSuccessPacket` written by [`SCItemTaskSuccessPacket`].
pub fn task_body(action: u8, tasks: Vec<ItemTask>) -> Vec<u8> {
    let mut s = PacketStream::new();
    SCItemTaskSuccessPacket {
        action,
        tasks,
        force_remove: Vec::new(),
    }
    .write(&mut s);
    s.into_bytes()
}

pub fn run<P: ClientPacket>(w: &mut World, which: u8, body: &[u8]) {
    let mut s = PacketStream::from_bytes(body.to_vec());
    let p = P::read(&mut s).expect("read");
    let conn = if which == 0 {
        &mut w.a.conn
    } else {
        &mut w.b.conn
    };
    futures::executor::block_on(p.handle(&w.server, conn)).expect("handle");
}

fn move_body(from: u64, to: u64, fst: u8, fs: u8, tst: u8, ts: u8) -> PacketStream {
    let mut s = PacketStream::new();
    s.write_u64(from)
        .write_u64(to)
        .write_u8(fst)
        .write_u8(fs)
        .write_u8(tst)
        .write_u8(ts);
    s
}

#[tokio::test]
async fn split_merge_and_move_in_bag() {
    let mut w = world();
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, STACK, 10, 0);

    // Split 4 into empty slot 1
    let mut s = move_body(id, 0, 2, 0, 2, 1);
    s.write_i32(4);
    run::<CSSplitBagItemPacket>(&mut w, 0, s.as_bytes());
    let src = item_at(&w.pa, 2, 0).unwrap();
    let new = item_at(&w.pa, 2, 1).unwrap();
    assert_eq!((src.count, new.count), (6, 4));
    // The split item keeps owner 0 (C#: only fromItem / target get their owner re-assigned).
    assert_eq!(new.owner_id, 0);
    let frames = w.a.drain();
    assert_eq!(frames.len(), 1);
    assert_eq!(frames[0].0, sc::SCItemTaskSuccessPacket);
    assert_eq!(
        frames[0].1,
        task_body(
            task_type::SWAP_ITEMS,
            vec![ItemTask::count_update(&src, -4), ItemTask::add(&new)]
        )
    );

    // Merge slot 1 back into slot 0 (all 4)
    let s = move_body(new.id, id, 2, 1, 2, 0);
    run::<CSSwapItemsPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(item_at(&w.pa, 2, 0).unwrap().count, 10);
    assert!(item_at(&w.pa, 2, 1).is_none());
    let frames = w.a.drain();
    assert_eq!(frames.len(), 1);
    let merged = item_at(&w.pa, 2, 0).unwrap();
    assert_eq!(
        frames[0].1,
        task_body(
            task_type::SWAP_ITEMS,
            vec![
                ItemTask::remove(&Item {
                    count: 0,
                    ..new.clone()
                }),
                ItemTask::count_update(&merged, 4)
            ]
        )
    );
    // The merged-away id is released.
    assert!(w.server.items.item_by_id(new.id).is_none());

    // Move everything to slot 5
    let s = move_body(id, 0, 2, 0, 2, 5);
    run::<CSSwapItemsPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(item_at(&w.pa, 2, 5).unwrap().id, id);
    let frames = w.a.drain();
    assert_eq!(
        frames[0].1,
        task_body(
            task_type::SWAP_ITEMS,
            vec![ItemTask::ItemMove {
                from_slot_type: 2,
                from_slot: 0,
                from_item_id: id,
                to_slot_type: 2,
                to_slot: 5,
                to_item_id: 0
            }]
        )
    );

    // Wrong source slot -> InvalidSlot error
    let s = move_body(id, 0, 2, 3, 2, 6);
    run::<CSSwapItemsPacket>(&mut w, 0, s.as_bytes());
    let frames = w.a.drain();
    assert_eq!(frames.len(), 1);
    assert_eq!(frames[0].0, sc::SCErrorMsgPacket);
}

#[tokio::test]
async fn equip_into_empty_slot_binds_and_broadcasts() {
    let mut w = world();
    let pa = w.pa.clone();
    let id = give(&w, &pa, slot_type::INVENTORY, ARMOR, 1, 3);
    let armor = item_at(&pa, 2, 3).unwrap();
    assert_eq!(armor.class, ItemClass::Armor);

    // The client sends FROM the empty equipment slot TO the bag item.
    let s = move_body(0, id, 1, equip_slot::CHEST as u8, 2, 3);
    run::<CSSwapItemsPacket>(&mut w, 0, s.as_bytes());
    let equipped = item_at(&pa, 1, equip_slot::CHEST as u8).expect("equipped");
    assert_eq!(equipped.id, id);
    assert_ne!(equipped.flags & ITEM_FLAG_SOUL_BOUND, 0);
    assert!(item_at(&pa, 2, 3).is_none());

    let frames = w.a.drain();
    let ops: Vec<u16> = frames.iter().map(|f| f.0).collect();
    assert_eq!(
        ops,
        vec![sc::SCItemTaskSuccessPacket, sc::SCItemTaskSuccessPacket]
    );
    assert_eq!(
        frames[0].1,
        task_body(
            task_type::SWAP_ITEMS,
            vec![ItemTask::ItemMove {
                from_slot_type: 1,
                from_slot: 2,
                from_item_id: 0,
                to_slot_type: 2,
                to_slot: 3,
                to_item_id: id
            }]
        )
    );
    // ApplyBindRules on the equipment
    assert_eq!(
        frames[1].1,
        task_body(
            task_type::SWAP_ITEMS,
            vec![ItemTask::update_bits(&equipped)]
        )
    );
    // Bob sees the equipment change (the event carries the item as it was moved, before the
    // bind rules ran).
    let fb = w.b.drain();
    assert_eq!(fb.len(), 1);
    assert_eq!(fb[0].0, sc::SCUnitEquipmentsChangedPacket);
    let mut want = PacketStream::new();
    want.write_bc(0x100).write_u8(1).write_u8(2);
    Item {
        flags: 0,
        ..equipped.clone()
    }
    .write(&mut want);
    assert_eq!(fb[0].1, want.into_bytes());

    // Un-equip back to the bag (equipment -> bag slot 7)
    let s = move_body(id, 0, 1, 2, 2, 7);
    run::<CSSwapItemsPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(item_at(&pa, 2, 7).unwrap().id, id);
    let fb = w.b.drain();
    assert_eq!(fb.len(), 1);
    let mut want = PacketStream::new();
    want.write_bc(0x100).write_u8(1).write_u8(2).write_i32(0);
    assert_eq!(fb[0].1, want.into_bytes());
}

#[tokio::test]
async fn destroy_partial_and_full() {
    let mut w = world();
    let pa = w.pa.clone();
    let id = give(&w, &pa, slot_type::INVENTORY, STACK, 10, 0);
    let mut s = PacketStream::new();
    s.write_u64(id).write_u8(2).write_u8(0).write_i32(3);
    run::<CSDestroyItemPacket>(&mut w, 0, s.as_bytes());
    let it = item_at(&pa, 2, 0).unwrap();
    assert_eq!(it.count, 7);
    let f = w.a.drain();
    assert_eq!(
        f[0].1,
        task_body(task_type::DESTROY, vec![ItemTask::count_update(&it, -3)])
    );

    let mut s = PacketStream::new();
    s.write_u64(id).write_u8(2).write_u8(0).write_i32(7);
    run::<CSDestroyItemPacket>(&mut w, 0, s.as_bytes());
    assert!(item_at(&pa, 2, 0).is_none());
    let f = w.a.drain();
    assert_eq!(
        f[0].1,
        task_body(task_type::DESTROY, vec![ItemTask::remove_slot(&it)])
    );
    assert!(w.server.items.item_by_id(id).is_none());

    // Too many -> nothing
    let id = give(&w, &pa, slot_type::INVENTORY, STACK, 1, 0);
    let mut s = PacketStream::new();
    s.write_u64(id).write_u8(2).write_u8(0).write_i32(2);
    run::<CSDestroyItemPacket>(&mut w, 0, s.as_bytes());
    assert!(w.a.drain().is_empty());
}

#[tokio::test]
async fn deposit_and_withdraw_money() {
    let mut w = world();
    w.pa.data().money = 500;
    let mut s = PacketStream::new();
    s.write_i32(200).write_i32(0);
    run::<CSDepositMoneyPacket>(&mut w, 0, s.as_bytes());
    let m = w.pa.data().money;
    let m2 = w.pa.data().money2;
    assert_eq!((m, m2), (300, 200));
    let f = w.a.drain();
    assert_eq!(
        f[0].1,
        task_body(
            task_type::DEPOSIT_MONEY,
            vec![ItemTask::MoneyChange(-200), ItemTask::MoneyChangeBank(200)]
        )
    );
    let mut s = PacketStream::new();
    s.write_i32(500).write_i32(0);
    run::<CSWithdrawMoneyPacket>(&mut w, 0, s.as_bytes());
    let f = w.a.drain();
    assert_eq!(f.len(), 1);
    assert_eq!(f[0].0, sc::SCErrorMsgPacket);
    let mut s = PacketStream::new();
    s.write_i32(50).write_i32(0);
    run::<CSWithdrawMoneyPacket>(&mut w, 0, s.as_bytes());
    let m = w.pa.data().money;
    let m2 = w.pa.data().money2;
    assert_eq!((m, m2), (350, 150));
    let f = w.a.drain();
    // The withdraw keeps the default task type DepositMoney (C#).
    assert_eq!(
        f[0].1,
        task_body(
            task_type::DEPOSIT_MONEY,
            vec![ItemTask::MoneyChangeBank(-50), ItemTask::MoneyChange(50)]
        )
    );
}

#[tokio::test]
async fn acquire_stacks_then_creates_and_sends_timers() {
    let mut w = world();
    let pa = w.pa.clone();
    give(&w, &pa, slot_type::INVENTORY, STACK, 15, 0);
    let inv = Inv::of(&pa);
    {
        let mut txn = ItemTxn::begin(&w.server);
        assert!(inventory::try_add_new_item(
            &mut txn,
            &inv,
            task_type::GM,
            STACK,
            30,
            -1,
            0
        ));
    }
    assert_eq!(item_at(&pa, 2, 0).unwrap().count, 20);
    assert_eq!(item_at(&pa, 2, 1).unwrap().count, 20);
    assert_eq!(item_at(&pa, 2, 2).unwrap().count, 5);
    let f = w.a.drain();
    assert_eq!(f.len(), 1);
    let first = item_at(&pa, 2, 0).unwrap();
    assert_eq!(
        f[0].1,
        task_body(
            task_type::GM,
            vec![
                ItemTask::count_update(&first, 5),
                ItemTask::add(&item_at(&pa, 2, 1).unwrap()),
                ItemTask::add(&item_at(&pa, 2, 2).unwrap()),
            ]
        )
    );

    {
        let mut txn = ItemTxn::begin(&w.server);
        assert!(inventory::try_add_new_item(
            &mut txn,
            &inv,
            task_type::GM,
            TIMED,
            1,
            -1,
            0
        ));
    }
    let timed = item_at(&pa, 2, 3).unwrap();
    assert!(timed.expiration_time > 0);
    let f = w.a.drain();
    assert_eq!(
        f.iter().map(|x| x.0).collect::<Vec<_>>(),
        vec![sc::SCItemTaskSuccessPacket, sc::SCSyncItemLifespanPacket]
    );

    // Not enough room: nothing happens.
    {
        let mut txn = ItemTxn::begin(&w.server);
        assert!(!inventory::try_add_new_item(
            &mut txn,
            &inv,
            task_type::GM,
            SINGLE,
            100,
            -1,
            0
        ));
    }
    assert!(w.a.drain().is_empty());
}

#[tokio::test]
async fn consume_prefers_lowest_slots() {
    let w = world();
    let pa = w.pa.clone();
    give(&w, &pa, slot_type::INVENTORY, STACK, 5, 4);
    give(&w, &pa, slot_type::INVENTORY, STACK, 5, 2);
    let inv = Inv::of(&pa);
    {
        let mut txn = ItemTxn::begin(&w.server);
        let used = inventory::consume_item(
            &mut txn,
            &inv,
            None,
            task_type::SKILL_REAGENTS,
            STACK,
            7,
            None,
        );
        assert_eq!(used, 7);
    }
    assert!(item_at(&pa, 2, 2).is_none());
    assert_eq!(item_at(&pa, 2, 4).unwrap().count, 3);
}

#[tokio::test]
async fn expand_bag_and_bank() {
    let mut w = world();
    w.pa.data().money = 150;
    let mut s = PacketStream::new();
    s.write_i32(2).write_bool(false);
    run::<CSExpandSlotsPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(w.pa.data().money, 50);
    assert_eq!(w.pa.data().num_inventory_slots, 60);
    assert_eq!(Inv::of(&w.pa).bag().lock().unwrap().container_size(), 60);
    let f = w.a.drain();
    assert_eq!(f[0].0, sc::SCInvenExpandedPacket);
    assert_eq!(f[0].1, vec![2, 60]);

    // Bank needs 2 x STACK in the bag
    let mut s = PacketStream::new();
    s.write_i32(3).write_bool(false);
    run::<CSExpandSlotsPacket>(&mut w, 0, s.as_bytes());
    assert!(w.a.drain().is_empty());
    let pa = w.pa.clone();
    give(&w, &pa, slot_type::INVENTORY, STACK, 3, 0);
    run::<CSExpandSlotsPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(w.pa.data().num_bank_slots, 60);
    assert_eq!(item_at(&pa, 2, 0).unwrap().count, 1);
    let f = w.a.drain();
    assert_eq!(
        f.iter().map(|x| x.0).collect::<Vec<_>>(),
        vec![sc::SCItemTaskSuccessPacket, sc::SCInvenExpandedPacket]
    );
}

#[tokio::test]
async fn swap_between_bag_and_bank() {
    let mut w = world();
    let pa = w.pa.clone();
    let a = give(&w, &pa, slot_type::INVENTORY, STACK, 3, 0);
    let b = give(&w, &pa, slot_type::BANK, SINGLE, 1, 4);
    let s = move_body(a, b, 2, 0, 3, 4);
    run::<CSSwapItemsPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(item_at(&pa, 3, 4).unwrap().id, a);
    assert_eq!(item_at(&pa, 2, 0).unwrap().id, b);
    assert_eq!(item_at(&pa, 3, 4).unwrap().slot_type, slot_type::BANK);
    let f = w.a.drain();
    assert_eq!(f.len(), 1);
}
