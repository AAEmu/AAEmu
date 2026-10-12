//! Tests of the crafting / store / specialty / expert / item-look ports: byte-exact S->C
//! packets, C->S decoding, flows through the real handlers (no MySQL), gated real-DB loads.

use std::sync::{Arc, Mutex};

use aaemu_commons::{Marshal, PacketStream};

use crate::data::items::{backpack_type, EquipTemplate, ItemTemplate, TemplateKind};
use crate::data::GameData;
use crate::models::container::{equip_slot, slot_type};
use crate::models::item::Item;
use crate::models::item_task::{task_type, ItemTask};
use crate::models::npc::Npc;
use crate::opcodes::sc;
use crate::packets::world::SCErrorMsgPacket;
use crate::server::GameServer;
use crate::systems::economy::craft::{self, CSExecuteCraft, CraftHooks, SCCraftFailedPacket};
use crate::systems::economy::craft_data::{Craft, CraftMaterial, CraftProduct, CraftSkill};
use crate::systems::economy::hooks::{NpcEconomyHooks, NpcShopInfo};
use crate::systems::economy::inventory::Inv;
use crate::systems::economy::item_packets::{
    SCCharacterLaborPowerChangedPacket, SCGamePointChangedPacket,
};
use crate::systems::economy::specialty::{
    self, CSRequestSpecialtyCurrentPacket, CSSpecialtyRatioPacket, SCSpecialtyCurrentPacket,
    SCSpecialtyRatioPacket,
};
use crate::systems::economy::store::{self, *};
use crate::systems::economy::store_data::{
    ExpandExpertLimit, ItemLookConvert, SpecialtyBundleItem, SpecialtyNpc,
};
use crate::systems::economy::tests::{give, item_at, run, task_body, test_data, World, STACK};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::{Entity, NpcRef, PlayerRef};

const PRODUCT: u32 = 5000; // max 10
const PACK: u32 = 6000; // trade pack (backpack, auto-equip)
const GLIDER: u32 = 6001;
const SWORD: u32 = 7000; // weapon with a look convert
const POWDER: u32 = 7100;
const COIN_ITEM: u32 = 8000;
const CRAFT: u32 = 1;
const PACK_CRAFT: u32 = 2;
const SKILL: u32 = 10;
const MERCHANT_NPC: u32 = 0x500;
const TRADER_NPC: u32 = 0x501;

fn data() -> GameData {
    let mut d = test_data();
    d.items
        .templates
        .insert(PRODUCT, ItemTemplate::plain(PRODUCT, 10));
    d.items.templates.insert(
        PACK,
        ItemTemplate {
            refund: 1000,
            ..ItemTemplate::new(PACK, TemplateKind::Backpack { backpack_type: 1 })
        },
    );
    d.items.templates.get_mut(&PACK).unwrap().max_count = 1;
    d.items.templates.insert(
        GLIDER,
        ItemTemplate {
            max_count: 1,
            ..ItemTemplate::new(
                GLIDER,
                TemplateKind::Backpack {
                    backpack_type: backpack_type::GLIDER,
                },
            )
        },
    );
    d.items.templates.insert(
        SWORD,
        ItemTemplate {
            max_count: 1,
            ..ItemTemplate::new(
                SWORD,
                TemplateKind::Weapon(EquipTemplate {
                    slot_type_id: 1,
                    ..EquipTemplate::default()
                }),
            )
        },
    );
    d.items
        .templates
        .insert(POWDER, ItemTemplate::plain(POWDER, 100));
    d.items
        .templates
        .insert(COIN_ITEM, ItemTemplate::plain(COIN_ITEM, 1000));
    {
        let t = d.items.templates.get_mut(&STACK).unwrap();
        t.honor_price = 3;
    }
    let econ = &mut d.systems.economy;
    econ.craft.crafts.insert(
        CRAFT,
        Craft {
            id: CRAFT,
            skill_id: SKILL,
            craft_products: vec![CraftProduct {
                id: 1,
                craft_id: CRAFT,
                item_id: PRODUCT,
                amount: 2,
                ..CraftProduct::default()
            }],
            craft_materials: vec![CraftMaterial {
                id: 1,
                craft_id: CRAFT,
                item_id: STACK,
                amount: 3,
                main_grade: false,
            }],
            ..Craft::default()
        },
    );
    econ.craft.crafts.insert(
        PACK_CRAFT,
        Craft {
            id: PACK_CRAFT,
            skill_id: SKILL,
            craft_products: vec![CraftProduct {
                id: 2,
                craft_id: PACK_CRAFT,
                item_id: PACK,
                amount: 1,
                ..CraftProduct::default()
            }],
            ..Craft::default()
        },
    );
    econ.craft.skills.insert(
        SKILL,
        CraftSkill {
            id: SKILL,
            consume_labor_power: 5,
            actability_group_id: 0,
            cooldown_time: 0,
        },
    );
    let store = &mut econ.store;
    store.refund_multipliers.insert(0, 100);
    store.refund_multipliers.insert(1, 50);
    store.expand_expert_limits = vec![ExpandExpertLimit {
        id: 1,
        expand_count: 1,
        life_point: 10,
        item_id: STACK,
        item_count: 2,
    }];
    store.weapon_holdables.insert(SWORD, 3);
    store.holdable_item_look_converts.insert(3, 9);
    store.item_look_converts.insert(
        9,
        ItemLookConvert {
            id: 9,
            slot_id: 0,
            required_item_id: POWDER,
            required_item_count: 2,
        },
    );
    store.specialty_npcs.insert(
        4321,
        SpecialtyNpc {
            id: 1,
            name: "trader".into(),
            npc_id: 4321,
            specialty_bundle_id: 77,
        },
    );
    let b = SpecialtyBundleItem {
        id: 1,
        item_id: PACK,
        specialty_bundle_id: 77,
        profit: 5000,
        ratio: 1200,
    };
    store.specialty_bundle_items.insert(1, b);
    store
        .specialty_bundle_items_mapped
        .entry(PACK)
        .or_default()
        .insert(77, b);
    store.specialty_packs = vec![(PACK, 5), (GLIDER, 6)];
    d
}

fn world() -> World {
    world_with_data(data())
}

fn world_with_data(data: GameData) -> World {
    let server = Arc::new(test_server_with(data));
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

fn bytes(p: &impl Marshal) -> Vec<u8> {
    let mut s = PacketStream::new();
    p.write(&mut s);
    s.into_bytes()
}

fn err_bytes(e1: i16, e2: i16, kind: u32, notify: bool) -> Vec<u8> {
    bytes(&SCErrorMsgPacket {
        error1: e1,
        error2: e2,
        kind,
        is_notify: notify,
    })
}

fn spawn_npc(w: &mut World, obj_id: u32, template: u32, x: f32) -> NpcRef {
    let mut n = Npc::new(template);
    n.unit.transform.set_xyz(x, 100.0, 100.0);
    let n = Entity::new(obj_id, n);
    let world = w.server.world.world(0).unwrap();
    world.spawn(&n, &w.server.view_ctx());
    w.a.drain();
    w.b.drain();
    n
}

// ---------------------------------------------------------------------------------------------
// Byte-exact S->C packets / C->S decoding
// ---------------------------------------------------------------------------------------------

#[test]
fn sc_craft_failed_bytes() {
    let p = SCCraftFailedPacket {
        id: 7,
        craft_id: 0x1234,
        count: 2,
    };
    let mut want = PacketStream::new();
    want.write_u32(7)
        .write_i32(2)
        .write_u32(0x1234)
        .write_u32(0x1234);
    assert_eq!(bytes(&p), want.into_bytes());
}

#[test]
fn sc_expert_expanded_and_specialty_bytes() {
    assert_eq!(bytes(&SCExpertExpandedPacket { next: 3 }), vec![3]);
    assert_eq!(
        bytes(&SCSpecialtyRatioPacket { ratio: 117 }),
        117i32.to_le_bytes().to_vec()
    );
    let p = SCSpecialtyCurrentPacket {
        from_zone_group: 5,
        to_zone_group: 9,
        results: vec![(PACK, 130), (GLIDER, 70)],
    };
    let mut want = PacketStream::new();
    want.write_i32(2)
        .write_u16(5)
        .write_u16(9)
        .write_u32(PACK)
        .write_u32(130)
        .write_u32(GLIDER)
        .write_u32(70);
    assert_eq!(bytes(&p), want.into_bytes());
}

#[test]
fn sc_sold_item_list_keeps_first_twelve() {
    let items: Vec<Item> = (0..14u64)
        .map(|i| {
            let mut it = Item::create(
                100 + i,
                &ItemTemplate::plain(STACK, 20),
                1,
                0,
                1,
                &Default::default(),
                0,
            );
            it.slot = i as i32;
            it
        })
        .collect();
    let p = SCSoldItemListPacket::new(items.clone());
    let mut want = PacketStream::new();
    want.write_i32(12);
    for it in &items[..12] {
        it.write(&mut want);
    }
    assert_eq!(bytes(&p), want.into_bytes());
}

#[test]
fn cs_packets_decode() {
    use crate::net::registry::ClientPacket;
    let mut s = PacketStream::new();
    s.write_u32(55).write_bc(0x300).write_i32(4);
    let p = CSExecuteCraft::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
    assert_eq!(
        p,
        CSExecuteCraft {
            craft_id: 55,
            obj_id: 0x300,
            count: 4
        }
    );

    let mut s = PacketStream::new();
    s.write_bc(0x500)
        .write_bc(0)
        .write_u32(9)
        .write_u8(1)
        .write_u8(2)
        .write_u32(STACK)
        .write_u8(0)
        .write_i32(3)
        .write_u8(1)
        .write_i32(0)
        .write_i32(1)
        .write_bool(true);
    let p = CSBuyItemsPacket::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
    assert_eq!(
        p,
        CSBuyItemsPacket {
            npc_obj_id: 0x500,
            doodad_obj_id: 0,
            unk_id: 9,
            buy: vec![BuyEntry {
                item_id: STACK,
                grade: 0,
                count: 3,
                currency: 1
            }],
            buy_back: vec![0, 1],
            use_aa_point: true,
        }
    );

    let mut s = PacketStream::new();
    s.write_bc(0x500)
        .write_bc(0x10)
        .write_u8(1)
        .write_u8(2)
        .write_u8(7)
        .write_u64(0xAABB)
        .write_u32(1);
    let p = CSSellItemsPacket::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
    assert_eq!(
        p.items,
        vec![SellEntry {
            slot_type: 2,
            slot: 7,
            item_id: 0xAABB,
            unk_id: 1
        }]
    );
    assert_eq!(p.unk_obj_id, 0x10);

    let mut s = PacketStream::new();
    s.write_u8(0)
        .write_u8(1)
        .write_u8(0)
        .write_u8(3)
        .write_u8(0)
        .write_u8(2)
        .write_u8(0)
        .write_u8(4)
        .write_u64(11)
        .write_u64(12);
    let p = CSChangeItemLookPacket::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
    assert_eq!(
        (
            p.slot_type1,
            p.slot1,
            p.slot_type2,
            p.slot2,
            p.item_id,
            p.look_id
        ),
        (1, 3, 2, 4, 11, 12)
    );

    let mut s = PacketStream::new();
    s.write_u16(5).write_u16(9);
    let p = CSRequestSpecialtyCurrentPacket::read(&mut PacketStream::from_bytes(s.into_bytes()))
        .unwrap();
    assert_eq!((p.from_zone_group_id, p.to_zone_group_id), (5, 9));
}

// ---------------------------------------------------------------------------------------------
// Crafting flows
// ---------------------------------------------------------------------------------------------

#[derive(Default)]
struct CraftRecorder {
    casts: Mutex<Vec<(u32, u32, f32)>>,
    cancels: Mutex<u32>,
    crafted: Mutex<Vec<u32>>,
}

impl CraftHooks for CraftRecorder {
    fn start_craft_cast(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        skill_id: u32,
        doodad_obj_id: u32,
        mul: f32,
    ) {
        self.casts
            .lock()
            .unwrap()
            .push((skill_id, doodad_obj_id, mul));
    }

    fn cancel_craft_skill(&self, _server: &GameServer, _player: &PlayerRef) {
        *self.cancels.lock().unwrap() += 1;
    }

    fn on_craft(&self, _server: &GameServer, _player: &PlayerRef, craft_id: u32) {
        self.crafted.lock().unwrap().push(craft_id);
    }
}

fn install_craft_hooks(w: &World) -> Arc<CraftRecorder> {
    let rec = Arc::new(CraftRecorder::default());
    w.server.systems.economy.craft.hooks.install(rec.clone());
    rec
}

fn execute_craft(w: &mut World, craft_id: u32, count: i32) {
    let mut s = PacketStream::new();
    s.write_u32(craft_id).write_bc(0).write_i32(count);
    run::<CSExecuteCraft>(w, 0, s.as_bytes());
}

#[tokio::test]
async fn craft_batch_consumes_materials_adds_products_and_reschedules() {
    let mut w = world();
    let rec = install_craft_hooks(&w);
    let pa = w.pa.clone();
    pa.data().labor_power = 100;
    give(&w, &pa, slot_type::INVENTORY, STACK, 6, 0);

    execute_craft(&mut w, CRAFT, 2);
    assert_eq!(rec.casts.lock().unwrap().clone(), vec![(SKILL, 0, 1.0)]);
    let st = w.server.systems.economy.craft.state(1);
    assert!(st.is_crafting);
    assert_eq!(
        (st.count, st.current_craft, st.consume_labor_power),
        (2, Some(CRAFT), 5)
    );
    assert!(w.a.drain().is_empty());

    // The skills subsystem finishes the cast.
    craft::end_craft(&w.server, &pa);
    let product = item_at(&pa, 2, 1).expect("product");
    assert_eq!((product.template_id, product.count), (PRODUCT, 2));
    assert_eq!(product.made_unit_id, 0); // stackable: no crafter name
    let mat = item_at(&pa, 2, 0).unwrap();
    assert_eq!(mat.count, 3);
    let f = w.a.drain();
    assert_eq!(
        f,
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::CRAFT_ACT_SAVED, vec![ItemTask::add(&product)])
            ),
            (
                sc::SCItemTaskSuccessPacket,
                task_body(
                    task_type::CRAFT_ACT_SAVED,
                    vec![ItemTask::count_update(&mat, -3)]
                )
            ),
        ]
    );
    assert_eq!(rec.crafted.lock().unwrap().clone(), vec![CRAFT]);
    // EndCraft does not touch labor (Skill.EndSkill does).
    assert_eq!(pa.data().labor_power, 100);
    let st = w.server.systems.economy.craft.state(1);
    assert_eq!((st.count, st.is_crafting), (1, false));

    // The CraftTask re-runs Craft after the skill cooldown (0 ms here).
    tokio::time::sleep(std::time::Duration::from_millis(30)).await;
    assert_eq!(rec.casts.lock().unwrap().len(), 2);
    assert!(w.server.systems.economy.craft.is_crafting(1));

    craft::end_craft(&w.server, &pa);
    assert!(item_at(&pa, 2, 0).is_none()); // all materials used
    assert_eq!(item_at(&pa, 2, 1).unwrap().count, 4);
    let st = w.server.systems.economy.craft.state(1);
    // CancelCraft keeps ConsumeLaborPower.
    assert_eq!(
        st,
        craft::CraftState {
            consume_labor_power: 5,
            ..craft::CraftState::default()
        }
    );
    assert_eq!(*rec.cancels.lock().unwrap(), 1);
}

#[tokio::test]
async fn craft_refused_without_materials_or_labor() {
    let mut w = world();
    let rec = install_craft_hooks(&w);
    let pa = w.pa.clone();
    give(&w, &pa, slot_type::INVENTORY, STACK, 2, 0);
    execute_craft(&mut w, CRAFT, 1);
    assert!(rec.casts.lock().unwrap().is_empty());
    assert_eq!(*rec.cancels.lock().unwrap(), 1);
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCErrorMsgPacket, err_bytes(148, 722, 0, false))]
    );

    // Enough materials, not enough labor at the end: error, no product, batch over.
    give(&w, &pa, slot_type::INVENTORY, STACK, 5, 1);
    pa.data().labor_power = 4;
    execute_craft(&mut w, CRAFT, 1);
    assert_eq!(rec.casts.lock().unwrap().len(), 1);
    craft::end_craft(&w.server, &pa);
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCErrorMsgPacket, err_bytes(148, 29, 0, false))]
    );
    assert_eq!(Inv::of(&pa).items_count(PRODUCT, -1), 0);
    assert_eq!(*rec.cancels.lock().unwrap(), 2);

    // Unknown craft: nothing happens (C# KeyNotFoundException).
    execute_craft(&mut w, 999, 1);
    assert!(w.a.drain().is_empty());
}

#[tokio::test]
async fn craft_trade_pack_equips_into_backpack_slot() {
    let mut w = world();
    let _rec = install_craft_hooks(&w);
    let pa = w.pa.clone();
    pa.data().labor_power = 100;
    // A glider is on the back: it moves to the bag for the pack.
    give(
        &w,
        &pa,
        slot_type::EQUIPMENT,
        GLIDER,
        1,
        equip_slot::BACKPACK,
    );
    execute_craft(&mut w, PACK_CRAFT, 1);
    craft::end_craft(&w.server, &pa);
    let pack = Inv::of(&pa).equipped(equip_slot::BACKPACK).expect("pack");
    assert_eq!(pack.template_id, PACK);
    assert_eq!(pack.made_unit_id, 1); // single-stack product carries the crafter
    assert_eq!(item_at(&pa, 2, 0).unwrap().template_id, GLIDER);
    let ops = w.a.drain_ops();
    assert_eq!(
        ops,
        vec![sc::SCItemTaskSuccessPacket, sc::SCItemTaskSuccessPacket]
    );

    // A second pack while one is worn: CanReplaceGliderInBackpackSlot refuses.
    execute_craft(&mut w, PACK_CRAFT, 1);
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCErrorMsgPacket, err_bytes(148, 315, 0, false))]
    );
}

#[tokio::test]
async fn free_regrade_rolls() {
    let w = world();
    // test data: grade 0 only (max grade order 0) -> never upgraded
    assert_eq!(craft::free_regrade_with(&w.server, 0, 0), 0);
    assert_eq!(craft::free_regrade_with(&w.server, 3, 0), 4);
    assert_eq!(craft::free_regrade_with(&w.server, 3, 1), 3);
}

// ---------------------------------------------------------------------------------------------
// Store flows
// ---------------------------------------------------------------------------------------------

struct Shops;
impl NpcEconomyHooks for Shops {
    fn shop_info(&self, _server: &GameServer, npc: &NpcRef) -> Option<NpcShopInfo> {
        Some(NpcShopInfo {
            merchant: npc.data().unit.template_id == 1234,
            merchant_pack_id: 7,
            ..NpcShopInfo::default()
        })
    }

    fn merchant_sells(&self, _server: &GameServer, pack_id: u32, item_id: u32) -> bool {
        pack_id == 7 && item_id == STACK
    }
}

fn buy_body(npc: u32, buy: &[(u32, i32, u8)], buy_back: &[i32]) -> Vec<u8> {
    let mut s = PacketStream::new();
    s.write_bc(npc)
        .write_bc(0)
        .write_u32(0)
        .write_u8(buy.len() as u8)
        .write_u8(buy_back.len() as u8);
    for (id, count, currency) in buy {
        s.write_u32(*id)
            .write_u8(0)
            .write_i32(*count)
            .write_u8(*currency);
    }
    for i in buy_back {
        s.write_i32(*i);
    }
    s.write_bool(false);
    s.into_bytes()
}

#[tokio::test]
async fn shop_checkout_rejects_money_only_and_mixed_currency_shortages() {
    for (money, honor, vocation, purchases) in [
        (5, 100, 100, vec![(STACK, 1, 0)]),
        (100, 0, 100, vec![(STACK, 1, 0), (STACK, 1, 1)]),
        (100, 100, 0, vec![(STACK, 1, 2)]),
    ] {
        let mut d = data();
        d.items
            .templates
            .get_mut(&STACK)
            .unwrap()
            .living_point_price = 4;
        let mut w = world_with_data(d);
        w.server.systems.economy.hooks.npcs.install(Arc::new(Shops));
        spawn_npc(&mut w, MERCHANT_NPC, 1234, 101.0);
        {
            let mut c = w.pa.data();
            c.money = money;
            c.honor_point = honor;
            c.vocation_point = vocation;
        }
        let before = Inv::of(&w.pa).bag().lock().unwrap().items.clone();
        run::<CSBuyItemsPacket>(&mut w, 0, &buy_body(MERCHANT_NPC, &purchases, &[]));
        let c = w.pa.data();
        assert_eq!(
            (c.money, c.honor_point, c.vocation_point),
            (money, honor, vocation)
        );
        drop(c);
        assert_eq!(Inv::of(&w.pa).bag().lock().unwrap().items, before);
        assert!(w.a.drain().is_empty());
    }
}

#[tokio::test]
async fn shop_checkout_rejects_invalid_counts_unpriced_currency_and_overflow() {
    for (price, honor, purchases) in [
        (10, 3, vec![(STACK, 1, 255)]),
        (10, 0, vec![(STACK, 1, 1)]),
        (10, 3, vec![(STACK, 1, 2)]),
        (10, 3, vec![(STACK, 0, 0)]),
        (10, 3, vec![(STACK, -1, 0)]),
        (10, 3, vec![(STACK, i32::MAX, 0)]),
        (1_500_000_000, 3, vec![(STACK, 1, 0), (STACK, 1, 0)]),
    ] {
        let mut d = data();
        let template = d.items.templates.get_mut(&STACK).unwrap();
        template.price = price;
        template.honor_price = honor;
        let mut w = world_with_data(d);
        w.server.systems.economy.hooks.npcs.install(Arc::new(Shops));
        spawn_npc(&mut w, MERCHANT_NPC, 1234, 101.0);
        {
            let mut c = w.pa.data();
            c.money = 4_000_000_000;
            c.honor_point = 100;
            c.vocation_point = 100;
        }
        let pa = w.pa.clone();
        give(&w, &pa, 2, STACK, 4, 0);
        let before = Inv::of(&pa).bag().lock().unwrap().items.clone();
        run::<CSBuyItemsPacket>(&mut w, 0, &buy_body(MERCHANT_NPC, &purchases, &[]));
        let c = pa.data();
        assert_eq!(
            (c.money, c.honor_point, c.vocation_point),
            (4_000_000_000, 100, 100)
        );
        drop(c);
        assert_eq!(Inv::of(&pa).bag().lock().unwrap().items, before);
        assert!(w.a.drain().is_empty());
    }
}

#[tokio::test]
async fn shop_checkout_capacity_failure_rolls_back_all_goods_and_buyback() {
    for with_buyback in [false, true] {
        let mut w = world();
        w.server.systems.economy.hooks.npcs.install(Arc::new(Shops));
        spawn_npc(&mut w, MERCHANT_NPC, 1234, 101.0);
        w.pa.data().money = 1000;
        Inv::of(&w.pa).bag().lock().unwrap().set_container_size(1);
        let buyback = w.server.systems.economy.store.buyback(w.pa.character_id);
        let back = w
            .server
            .items
            .create(
                STACK,
                1,
                0,
                1,
                &w.server.game_data.items,
                &w.server.ids.item,
                0,
            )
            .unwrap();
        buyback
            .lock()
            .unwrap()
            .add_new_item(back, 0, &w.server.game_data.items)
            .unwrap();
        let before_back = buyback.lock().unwrap().items.clone();
        let purchases = if with_buyback {
            vec![(STACK, 1, 0)]
        } else {
            vec![(STACK, 20, 0), (STACK, 1, 0)]
        };
        let indices = if with_buyback { vec![0] } else { vec![] };
        run::<CSBuyItemsPacket>(&mut w, 0, &buy_body(MERCHANT_NPC, &purchases, &indices));
        assert!(Inv::of(&w.pa).bag().lock().unwrap().items.is_empty());
        assert_eq!(buyback.lock().unwrap().items, before_back);
        assert_eq!(w.pa.data().money, 1000);
        assert!(w.a.drain().is_empty());
    }
}

#[tokio::test]
async fn shop_checkout_mixed_payment_and_configured_free_goods_succeed() {
    let mut d = data();
    d.items
        .templates
        .get_mut(&STACK)
        .unwrap()
        .living_point_price = 4;
    let mut w = world_with_data(d);
    w.server.systems.economy.hooks.npcs.install(Arc::new(Shops));
    spawn_npc(&mut w, MERCHANT_NPC, 1234, 101.0);
    {
        let mut c = w.pa.data();
        c.money = 100;
        c.honor_point = 10;
        c.vocation_point = 10;
    }
    run::<CSBuyItemsPacket>(
        &mut w,
        0,
        &buy_body(
            MERCHANT_NPC,
            &[(STACK, 1, 0), (STACK, 1, 1), (STACK, 1, 2)],
            &[],
        ),
    );
    assert_eq!(item_at(&w.pa, 2, 0).unwrap().count, 3);
    let c = w.pa.data();
    assert_eq!((c.money, c.honor_point, c.vocation_point), (90, 7, 6));
    drop(c);
    let frames = w.a.drain();
    assert_eq!(frames.len(), 7);
    assert_eq!(
        frames[3],
        (
            sc::SCGamePointChangedPacket,
            bytes(&SCGamePointChangedPacket {
                kind: 0,
                amount: -3
            })
        )
    );
    assert_eq!(
        frames[4],
        (
            sc::SCGamePointChangedPacket,
            bytes(&SCGamePointChangedPacket {
                kind: 1,
                amount: -4
            })
        )
    );
    assert_eq!(
        frames[5],
        (
            sc::SCItemTaskSuccessPacket,
            task_body(task_type::DEPOSIT_MONEY, vec![ItemTask::MoneyChange(-10)])
        )
    );

    let mut d = data();
    d.items.templates.get_mut(&STACK).unwrap().price = 0;
    let mut w = world_with_data(d);
    w.server.systems.economy.hooks.npcs.install(Arc::new(Shops));
    spawn_npc(&mut w, MERCHANT_NPC, 1234, 101.0);
    run::<CSBuyItemsPacket>(&mut w, 0, &buy_body(MERCHANT_NPC, &[(STACK, 1, 0)], &[]));
    assert_eq!(item_at(&w.pa, 2, 0).unwrap().count, 1);
    assert_eq!(w.pa.data().money, 0);
}

#[tokio::test]
async fn shop_checkout_vocation_earnings_bonuses_do_not_change_configured_price() {
    use crate::models::unit::{Bonus, BonusTemplate, UnitAttribute, UnitModifierType};
    // Additive gains could reverse a debit; multipliers could overcharge or
    // discount it. Neither changes the price shown by the 1.2 shop UI.
    for (add, multiplier) in [(0, 0), (100, 0), (0, 200), (0, -50), (100, 200)] {
        for available in [49, 50, 100] {
            let mut d = data();
            d.items
                .templates
                .get_mut(&STACK)
                .unwrap()
                .living_point_price = 50;
            let mut w = world_with_data(d);
            w.server.systems.economy.hooks.npcs.install(Arc::new(Shops));
            spawn_npc(&mut w, MERCHANT_NPC, 1234, 101.0);
            {
                let mut c = w.pa.data();
                c.vocation_point = available;
                for (index, attribute, value) in [
                    (1, UnitAttribute::LivingPointGain, add),
                    (2, UnitAttribute::LivingPointGainMul, multiplier),
                ] {
                    c.unit.bonuses.add_bonus(
                        index,
                        Bonus {
                            template: BonusTemplate {
                                attribute,
                                modifier_type: UnitModifierType::Value,
                                value,
                                linear_level_bonus: 0,
                            },
                            value,
                        },
                    );
                }
            }
            run::<CSBuyItemsPacket>(&mut w, 0, &buy_body(MERCHANT_NPC, &[(STACK, 1, 2)], &[]));
            assert_eq!(w.pa.data().money, 0);
            assert_eq!(w.pa.data().honor_point, 0);
            if available < 50 {
                assert_eq!(w.pa.data().vocation_point, available);
                assert!(Inv::of(&w.pa).bag().lock().unwrap().items.is_empty());
                assert!(w.a.drain().is_empty());
            } else {
                assert_eq!(w.pa.data().vocation_point, available - 50);
                assert_eq!(item_at(&w.pa, 2, 0).unwrap().count, 1);
                let point_changes: Vec<_> =
                    w.a.drain()
                        .into_iter()
                        .filter(|(opcode, _)| *opcode == sc::SCGamePointChangedPacket)
                        .collect();
                assert_eq!(
                    point_changes,
                    vec![(
                        sc::SCGamePointChangedPacket,
                        bytes(&SCGamePointChangedPacket {
                            kind: 1,
                            amount: -50,
                        }),
                    )]
                );
            }
        }
    }
}

#[tokio::test]
async fn buy_sell_and_buy_back() {
    let mut w = world();
    w.server.systems.economy.hooks.npcs.install(Arc::new(Shops));
    spawn_npc(&mut w, MERCHANT_NPC, 1234, 101.0);
    let pa = w.pa.clone();
    pa.data().money = 1000;

    // Buy 3 x STACK (price 10) + an item the merchant does not sell (ignored).
    run::<CSBuyItemsPacket>(
        &mut w,
        0,
        &buy_body(MERCHANT_NPC, &[(STACK, 3, 0), (PRODUCT, 1, 0)], &[]),
    );
    let bought = item_at(&pa, 2, 0).unwrap();
    assert_eq!((bought.template_id, bought.count), (STACK, 3));
    assert_eq!(pa.data().money, 970);
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::STORE_BUY, vec![ItemTask::add(&bought)])
            ),
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::DEPOSIT_MONEY, vec![ItemTask::MoneyChange(-30)])
            ),
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::STORE_BUY, vec![])
            ),
        ]
    );

    // Honor currency: points packet instead of money.
    pa.data().honor_point = 100;
    run::<CSBuyItemsPacket>(&mut w, 0, &buy_body(MERCHANT_NPC, &[(STACK, 2, 1)], &[]));
    assert_eq!(pa.data().honor_point, 94);
    let f = w.a.drain();
    assert_eq!(
        f.iter().map(|x| x.0).collect::<Vec<_>>(),
        vec![
            sc::SCItemTaskSuccessPacket,
            sc::SCGamePointChangedPacket,
            sc::SCItemTaskSuccessPacket
        ]
    );
    assert_eq!(
        f[1].1,
        bytes(&SCGamePointChangedPacket {
            kind: 0,
            amount: -6
        })
    );

    // Sell the stack (refund 5 * 100% * 5 units = 25).
    let stack = item_at(&pa, 2, 0).unwrap();
    assert_eq!(stack.count, 5);
    let mut s = PacketStream::new();
    s.write_bc(MERCHANT_NPC)
        .write_bc(0)
        .write_u8(1)
        .write_u8(2)
        .write_u8(0)
        .write_u64(stack.id)
        .write_u32(0);
    run::<CSSellItemsPacket>(&mut w, 0, s.as_bytes());
    assert!(item_at(&pa, 2, 0).is_none());
    assert_eq!(pa.data().money, 995);
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::STORE_SELL, vec![ItemTask::remove_slot(&stack)])
            ),
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::DEPOSIT_MONEY, vec![ItemTask::MoneyChange(25)])
            ),
        ]
    );
    let bb = w.server.systems.economy.store.buyback(1);
    assert_eq!(bb.lock().unwrap().items.len(), 1);
    assert_eq!(bb.lock().unwrap().items[0].slot_type, slot_type::NONE);

    // List: renumbered, sent with Item.Write.
    let mut s = PacketStream::new();
    s.write_bc(MERCHANT_NPC);
    run::<CSListSoldItemPacket>(&mut w, 0, s.as_bytes());
    let sold = bb.lock().unwrap().items.clone();
    let f = w.a.drain();
    assert_eq!(f.len(), 1);
    assert_eq!(f[0].0, sc::SCSoldItemListPacket);
    assert_eq!(f[0].1, bytes(&SCSoldItemListPacket::new(sold.clone())));
    assert_eq!(sold[0].slot, 0);

    // Buy it back (index 0): back in the bag for the refund price.
    run::<CSBuyItemsPacket>(&mut w, 0, &buy_body(MERCHANT_NPC, &[], &[0]));
    let back = item_at(&pa, 2, 0).expect("bought back");
    assert_eq!(back.id, stack.id);
    assert_eq!(pa.data().money, 970);
    assert!(bb.lock().unwrap().items.is_empty());
    let f = w.a.drain();
    assert_eq!(
        f,
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::STORE_BUY, vec![ItemTask::add(&back)])
            ),
            (
                sc::SCItemTaskSuccessPacket,
                task_body(
                    task_type::STORE_BUY,
                    vec![ItemTask::ItemRemoveSlot {
                        item_id: stack.id,
                        slot_type: slot_type::NONE,
                        slot: 0
                    }]
                )
            ),
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::DEPOSIT_MONEY, vec![ItemTask::MoneyChange(-25)])
            ),
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::STORE_BUY, vec![ItemTask::buyback(&back)])
            ),
        ]
    );

    // Too far from the merchant.
    spawn_npc(&mut w, MERCHANT_NPC + 1, 1234, 120.0);
    run::<CSBuyItemsPacket>(
        &mut w,
        0,
        &buy_body(MERCHANT_NPC + 1, &[(STACK, 1, 0)], &[]),
    );
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCErrorMsgPacket, err_bytes(154, 154, 0, true))]
    );

    // Not a merchant: ignored.
    spawn_npc(&mut w, MERCHANT_NPC + 2, 99, 101.0);
    run::<CSBuyItemsPacket>(
        &mut w,
        0,
        &buy_body(MERCHANT_NPC + 2, &[(STACK, 1, 0)], &[]),
    );
    assert!(w.a.drain().is_empty());

    // Leaving the world wipes the buy-back items.
    let mut s = PacketStream::new();
    s.write_bc(MERCHANT_NPC)
        .write_bc(0)
        .write_u8(1)
        .write_u8(2)
        .write_u8(0)
        .write_u64(back.id)
        .write_u32(0);
    run::<CSSellItemsPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(bb.lock().unwrap().items.len(), 1);
    w.server.systems.economy.store.wipe_buyback(&w.server, 1);
    assert!(bb.lock().unwrap().items.is_empty());
}

#[tokio::test]
async fn expand_expert_flow() {
    let mut w = world();
    let pa = w.pa.clone();
    pa.data().vocation_point = 5;
    run::<CSExpandExpertPacket>(&mut w, 0, &[]);
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCErrorMsgPacket, err_bytes(687, 687, 0, true))]
    );
    pa.data().vocation_point = 20;
    run::<CSExpandExpertPacket>(&mut w, 0, &[]);
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCErrorMsgPacket, err_bytes(686, 686, 0, true))]
    );
    give(&w, &pa, slot_type::INVENTORY, STACK, 3, 0);
    run::<CSExpandExpertPacket>(&mut w, 0, &[]);
    let rest = item_at(&pa, 2, 0).unwrap();
    assert_eq!(rest.count, 1);
    assert_eq!(pa.data().expanded_expert, 1);
    // C# bug mirrored: the vocation cost is added.
    assert_eq!(pa.data().vocation_point, 30);
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCGamePointChangedPacket,
                bytes(&SCGamePointChangedPacket {
                    kind: 1,
                    amount: 10
                })
            ),
            (
                sc::SCItemTaskSuccessPacket,
                task_body(
                    task_type::EXPAND_EXPERT,
                    vec![ItemTask::count_update(&rest, -2)]
                )
            ),
            (sc::SCExpertExpandedPacket, vec![1]),
        ]
    );
    // No further step: silently ignored.
    run::<CSExpandExpertPacket>(&mut w, 0, &[]);
    assert!(w.a.drain().is_empty());
}

#[tokio::test]
async fn convert_item_look_flow() {
    let mut w = world();
    let pa = w.pa.clone();
    let base = give(&w, &pa, slot_type::INVENTORY, SWORD, 1, 0);
    let look = give(&w, &pa, slot_type::INVENTORY, SWORD, 1, 1);
    let mut s = PacketStream::new();
    s.write_u64(base).write_u64(look);
    // No powder: NotEnoughRequiredItem(type = powder id)
    run::<store::CSConvertItemLookPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCErrorMsgPacket, err_bytes(722, 722, POWDER, true))]
    );
    give(&w, &pa, slot_type::INVENTORY, POWDER, 5, 2);
    run::<store::CSConvertItemLookPacket>(&mut w, 0, s.as_bytes());
    let converted = item_at(&pa, 2, 0).unwrap();
    assert_eq!(converted.details.image_item_template_id, SWORD);
    let look_item = Item {
        id: look,
        slot: 1,
        ..converted.clone()
    };
    let powder = item_at(&pa, 2, 2).unwrap();
    assert_eq!(powder.count, 3);
    assert!(item_at(&pa, 2, 1).is_none());
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                task_body(
                    task_type::SKILL_REAGENTS,
                    vec![ItemTask::count_update(&powder, -2)]
                )
            ),
            (
                sc::SCItemTaskSuccessPacket,
                task_body(
                    task_type::CONVERT_ITEM_LOOK,
                    vec![ItemTask::update(&converted)]
                )
            ),
            (
                sc::SCItemTaskSuccessPacket,
                task_body(
                    task_type::SKILL_REAGENTS,
                    vec![ItemTask::remove_slot(&look_item)]
                )
            ),
        ]
    );
    // Unknown item: FailedToUseItem
    let mut s = PacketStream::new();
    s.write_u64(base).write_u64(look);
    run::<store::CSConvertItemLookPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCErrorMsgPacket, err_bytes(207, 207, 0, true))]
    );
}

// ---------------------------------------------------------------------------------------------
// Specialty
// ---------------------------------------------------------------------------------------------

#[derive(Default)]
struct Mailer(Mutex<Vec<SpecialtyMail>>);
impl StoreHooks for Mailer {
    fn send_specialty_mail(
        &self,
        _server: &GameServer,
        _seller: &PlayerRef,
        mail: &SpecialtyMail,
    ) -> bool {
        self.0.lock().unwrap().push(*mail);
        true
    }
}

#[tokio::test]
async fn sell_trade_pack_to_trader() {
    let mut w = world();
    let mailer = Arc::new(Mailer::default());
    w.server.systems.economy.store.hooks.install(mailer.clone());
    spawn_npc(&mut w, TRADER_NPC, 4321, 102.0);
    let pa = w.pa.clone();

    // Ratio without a pack: 0
    let mut s = PacketStream::new();
    s.write_u32(0);
    run::<CSSpecialtyRatioPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCSpecialtyRatioPacket, 0i32.to_le_bytes().to_vec())]
    );

    // Not enough labor
    pa.data().labor_power = 10;
    let mut s = PacketStream::new();
    s.write_bc(TRADER_NPC);
    run::<CSSellBackpackGoodsPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCErrorMsgPacket, err_bytes(29, 29, 0, true))]
    );

    pa.data().labor_power = 100;
    // ChangeLabor indexes Actabilities[31] (C# KeyNotFoundException without it).
    pa.data()
        .actability
        .actabilities
        .push(crate::models::char_data::Actability {
            id: 31,
            point: 0,
            step: 0,
        });
    let pack = give(&w, &pa, slot_type::EQUIPMENT, PACK, 1, equip_slot::BACKPACK);
    let mut s = PacketStream::new();
    s.write_u32(0);
    run::<CSSpecialtyRatioPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCSpecialtyRatioPacket, 130i32.to_le_bytes().to_vec())]
    );

    let pack_item = Inv::of(&pa).equipped(equip_slot::BACKPACK).unwrap();
    let mut s = PacketStream::new();
    s.write_bc(TRADER_NPC);
    run::<CSSellBackpackGoodsPacket>(&mut w, 0, s.as_bytes());
    // base = floor(5000 * 1.2) + refund 1000 = 7000; * 130% = 9100; + 5% = 9555
    let mails = mailer.0.lock().unwrap().clone();
    assert_eq!(
        mails,
        vec![SpecialtyMail {
            seller_character_id: 1,
            crafter_id: 0,
            trade_pack_template_id: PACK,
            trade_rate: 130,
            item_reward_template_id: crate::data::items::ITEM_COINS,
            item_count_base: 7000,
            item_count_bonus: 0,
            item_count_for_seller: 9555,
            item_count_for_crafter: 0,
            interest_rate: 5,
            for_crafter: false,
        }]
    );
    assert!(Inv::of(&pa).equipped(equip_slot::BACKPACK).is_none());
    assert_eq!(pa.data().labor_power, 40);
    let f = w.a.drain();
    assert_eq!(
        f,
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                task_body(
                    task_type::SELL_BACKPACK,
                    vec![ItemTask::remove_slot(&Item {
                        id: pack,
                        ..pack_item
                    })]
                )
            ),
            // ConsumeItem always sends its own (here empty) task list.
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::SELL_BACKPACK, vec![])
            ),
            (
                sc::SCCharacterLaborPowerChangedPacket,
                bytes(&SCCharacterLaborPowerChangedPacket {
                    amount: -60,
                    action: 31,
                    point: 60,
                    step: 0
                })
            ),
        ]
    );
    let sp = &w.server.systems.economy.specialty;
    assert_eq!(sp.sold_in_tick(PACK, 0), 1);
    sp.consume_ratio();
    assert_eq!(sp.ratio(PACK, 0), 129.0);
    assert_eq!(sp.sold_in_tick(PACK, 0), 0);
    sp.regen_ratio();
    assert_eq!(sp.ratio(PACK, 0), 130.0);

    // Route rates (packs made in zone group 5, traded in 0).
    let mut s = PacketStream::new();
    s.write_u16(5).write_u16(0);
    run::<CSRequestSpecialtyCurrentPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(
        w.a.drain(),
        vec![(
            sc::SCSpecialtyCurrentPacket,
            bytes(&SCSpecialtyCurrentPacket {
                from_zone_group: 5,
                to_zone_group: 0,
                results: vec![(PACK, 130)]
            })
        )]
    );
    assert_eq!(
        specialty::get_ratios_for_target_route(&w.server, 6, 2),
        vec![(GLIDER, 130)]
    );
}

#[tokio::test]
async fn sell_trade_pack_shares_profit_with_crafter() {
    let mut w = world();
    let mailer = Arc::new(Mailer::default());
    w.server.systems.economy.store.hooks.install(mailer.clone());
    spawn_npc(&mut w, TRADER_NPC, 4321, 102.0);
    let pa = w.pa.clone();
    pa.data().labor_power = 100;
    give(&w, &pa, slot_type::EQUIPMENT, PACK, 1, equip_slot::BACKPACK);
    Inv::of(&pa)
        .equipment()
        .lock()
        .unwrap()
        .get_item_by_slot_mut(equip_slot::BACKPACK)
        .unwrap()
        .made_unit_id = 2;
    let mut s = PacketStream::new();
    s.write_bc(TRADER_NPC);
    run::<CSSellBackpackGoodsPacket>(&mut w, 0, s.as_bytes());
    let mails = mailer.0.lock().unwrap().clone();
    assert_eq!(mails.len(), 2);
    // 9555 * 0.8 = 7644 for the seller, 1911 for the crafter
    assert_eq!(
        (
            mails[0].item_count_for_seller,
            mails[0].item_count_for_crafter
        ),
        (7644, 1911)
    );
    assert!(!mails[0].for_crafter && mails[1].for_crafter);
    assert_eq!(mails[1].crafter_id, 2);
}

#[tokio::test]
async fn sell_trade_pack_refused_without_mail() {
    let mut w = world();
    spawn_npc(&mut w, TRADER_NPC, 4321, 102.0);
    let pa = w.pa.clone();
    pa.data().labor_power = 100;
    give(&w, &pa, slot_type::EQUIPMENT, PACK, 1, equip_slot::BACKPACK);
    let mut s = PacketStream::new();
    s.write_bc(TRADER_NPC);
    run::<CSSellBackpackGoodsPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCErrorMsgPacket, err_bytes(169, 169, 0, true))]
    );
    assert!(Inv::of(&pa).equipped(equip_slot::BACKPACK).is_some());
}

// ---------------------------------------------------------------------------------------------
// Real compact.sqlite3
// ---------------------------------------------------------------------------------------------

#[test]
#[ignore]
fn real_compact_db_craft_and_store_tables() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let files =
        crate::data::client_files::ClientFileManager::from_sources(&[], std::path::Path::new("."));
    let ctx = crate::systems::DataLoadCtx {
        conn: &conn,
        app_path: std::path::Path::new("."),
        files: &files,
        language: "en_us",
    };
    let craft = crate::systems::economy::craft_data::CraftData::load(&ctx).unwrap();
    assert_eq!(craft.crafts.len(), 7010);
    let products: usize = craft.crafts.values().map(|c| c.craft_products.len()).sum();
    let materials: usize = craft.crafts.values().map(|c| c.craft_materials.len()).sum();
    assert_eq!(products, 6938 - 10);
    assert_eq!(materials, 23475 - 54);
    assert_eq!(craft.crafts.values().filter(|c| c.is_pack).count(), 2153);
    // The 't'/'f' columns read through GetBoolean(column) are all false.
    assert!(craft
        .crafts
        .values()
        .all(|c| !c.need_bind && !c.show_upper_craft));
    let with_skill = craft.crafts.values().filter(|c| c.skill_id != 0).count();
    assert_eq!(with_skill, 7010 - 5);
    assert!(craft
        .crafts
        .values()
        .filter(|c| c.skill_id != 0)
        .all(|c| craft.skills.contains_key(&c.skill_id)));

    let store = crate::systems::economy::store_data::StoreData::load(&ctx).unwrap();
    assert_eq!(store.specialties.len(), 2162);
    assert_eq!(
        store
            .specialties
            .values()
            .filter(|s| s.vendor_exist)
            .count(),
        1
    );
    assert_eq!(store.specialty_bundle_items.len(), 3311);
    assert_eq!(store.specialty_bundle_items_mapped.len(), 231);
    assert_eq!(store.specialty_npcs.len(), 37);
    assert_eq!(store.taxations.len(), 11);
    assert_eq!(store.taxations.values().filter(|t| t.show).count(), 10);
    assert_eq!(store.expand_expert_limits.len(), 14);
    assert_eq!(
        store
            .expand_expert_limits
            .iter()
            .map(|e| e.expand_count)
            .collect::<Vec<_>>(),
        (1..=14).collect::<Vec<u8>>()
    );
    assert_eq!(store.item_look_converts.len(), 27);
    assert_eq!(store.holdable_item_look_converts.len(), 22);
    assert_eq!(store.wearable_item_look_converts.len(), 7); // 8 rows, one duplicate slot (TryAdd)
    assert_eq!(store.refund_multipliers.len(), 12);
    assert_eq!(store.refund_multiplier(0), Some(100));
    assert_eq!(store.specialty_packs.len(), 255);
    println!(
        "crafts {} products {products} materials {materials} skills {} packs {}",
        craft.crafts.len(),
        craft.skills.len(),
        store.specialty_packs.len()
    );
}
