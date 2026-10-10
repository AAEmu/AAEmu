//! Specialty price / rate computations, packets and the sell flow.

use std::sync::Mutex as StdMutex;

use super::*;
use crate::data::items::{ItemData, ItemTemplate};
use crate::data::world_data::{Zone, ZoneGroup};
use crate::data::GameData;
use crate::models::item::Item;
use crate::models::npc::Npc;
use crate::net::packet::encode;
use crate::opcodes::sc;
use crate::systems::npcs::hooks::EconomyBridge;
use crate::systems::npcs::npc::data::NpcTemplate;
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient, TEST_ZONE};
use crate::world::object::Entity;

fn body(p: &dyn crate::net::packet::ServerPacket) -> Vec<u8> {
    encode(p)[6..].to_vec()
}

const PACK: u32 = 26488;
const SPECIALTY_NPC: u32 = 10691;
const ZONE_GROUP: u32 = 5;

fn data(coin: u32) -> GameData {
    let mut d = GameData::empty();
    d.zones.zones.insert(
        TEST_ZONE,
        Zone {
            id: 1,
            name: "z".into(),
            zone_key: TEST_ZONE,
            group_id: ZONE_GROUP,
            closed: false,
            faction_id: 0,
            zone_climate_id: 0,
        },
    );
    d.zones.groups.insert(
        ZONE_GROUP,
        ZoneGroup {
            id: ZONE_GROUP,
            name: "g".into(),
            ..ZoneGroup::default()
        },
    );
    let s = &mut d.systems.npcs.specialty;
    let bundle = SpecialtyBundleItem {
        id: 1000,
        item_id: PACK,
        specialty_bundle_id: 3,
        profit: 12100,
        ratio: 3273,
    };
    s.bundle_items.insert(bundle.id, bundle);
    s.bundle_items_mapped
        .entry(PACK)
        .or_default()
        .insert(3, bundle);
    s.npcs.insert(
        SPECIALTY_NPC,
        SpecialtyNpc {
            id: 10,
            name: "trader".into(),
            npc_id: SPECIALTY_NPC,
            specialty_bundle_id: 3,
        },
    );
    s.item_refunds.insert(PACK, 250);
    s.specialty_packs = vec![(PACK, 22), (26489, 22), (30000, 23)];
    d.systems.npcs.npc.templates.insert(
        SPECIALTY_NPC,
        NpcTemplate {
            id: SPECIALTY_NPC,
            specialty_coin_id: coin,
            ..NpcTemplate::default()
        },
    );
    d
}

#[test]
fn base_price_and_payout() {
    let b = SpecialtyBundleItem {
        id: 1,
        item_id: PACK,
        specialty_bundle_id: 3,
        profit: 12100,
        ratio: 3273,
    };
    // floor(12100 * 3.273f) + refund.
    assert_eq!(base_price(&b, 250), 39603 + 250);
    // Gold payout at 130 %: 39853 * 1.3 = 51808.9; +5 % interest = 54399.345 -> 54399.
    let m = payout(39853, 130, 0, false);
    assert_eq!(m.item_type_to_deliver, ITEM_COINS);
    assert_eq!(m.amount_of_items_seller, 54399);
    assert_eq!(m.amount_of_items_base, 39853);
    assert_eq!(m.amount_of_items_crafter, 0);
    assert_eq!(m.interest_rate, 5);
    // Coin payout: amounts / 10000 (banker's rounding).
    let m = payout(39853, 130, 26_000, false);
    assert_eq!(m.item_type_to_deliver, 26_000);
    assert_eq!(m.amount_of_items_seller, 5);
    assert_eq!(m.amount_of_items_base, 4);
    // 80 / 20 split with the crafter.
    let m = payout(100_000, 100, 0, true);
    assert_eq!(
        m.amount_of_items_seller + m.amount_of_items_crafter,
        105_000
    );
    assert_eq!(m.amount_of_items_seller, 84_000);
    // Round half to even twice: 23810 + 1190.5 = 25000.5 -> 25000; / 10000 = 2.5 -> 2.
    let m = payout(23810, 100, 1, false);
    assert_eq!(m.amount_of_items_seller, 2);
    let m = payout(20000, 119, 1, false); // 23800 * 1.05 = 24990 -> 2
    assert_eq!(m.amount_of_items_seller, 2);
}

#[test]
fn ratio_consume_and_regen() {
    let m = SpecialtyManager::default();
    m.set_config(SpecialtyConfig::default());
    assert_eq!(m.price_ratio(PACK, 1), 130.0);
    for _ in 0..3 {
        m.add_sold_pack(PACK, 1);
    }
    assert_eq!(m.sold_in_tick(PACK, 1), 3);
    m.consume_ratio();
    // ceil(3 * 0.5) = 2.
    assert_eq!(m.price_ratio(PACK, 1), 128.0);
    assert_eq!(m.sold_in_tick(PACK, 1), 0);
    for _ in 0..200 {
        m.add_sold_pack(PACK, 1);
    }
    m.consume_ratio();
    assert_eq!(m.price_ratio(PACK, 1), 70.0);
    m.regen_ratio();
    assert_eq!(m.price_ratio(PACK, 1), 75.0);
    for _ in 0..20 {
        m.regen_ratio();
    }
    assert_eq!(m.price_ratio(PACK, 1), 130.0);
    // Items that never sold are not regenerated (and keep the default).
    assert_eq!(m.price_ratio(1, 1), 130.0);
}

#[test]
fn config_section_is_read() {
    let dir = std::env::temp_dir().join(format!("aaemu-specialty-{}", std::process::id()));
    std::fs::create_dir_all(dir.join("Configurations")).unwrap();
    std::fs::write(
        dir.join("Configurations").join("Specialty.json"),
        "{ \"Specialty\": { \"MaxSpecialtyRatio\": 120, // comment\n \"RatioRegenTickMinutes\": 30.0, } }",
    )
    .unwrap();
    let c = SpecialtyConfig::load(&dir);
    assert_eq!(c.max_specialty_ratio, 120);
    assert_eq!(c.ratio_regen_tick_minutes, 30.0);
    assert_eq!(c.min_specialty_ratio, 70);
    let _ = std::fs::remove_dir_all(&dir);
    assert_eq!(SpecialtyConfig::load(&dir), SpecialtyConfig::default());
}

#[test]
fn packets_are_byte_exact() {
    assert_eq!(
        body(&SCSpecialtyRatioPacket { ratio: 128 }),
        128i32.to_le_bytes().to_vec()
    );
    let mut e = Vec::new();
    e.extend_from_slice(&2i32.to_le_bytes());
    e.extend_from_slice(&22u16.to_le_bytes());
    e.extend_from_slice(&5u16.to_le_bytes());
    for v in [PACK, 130, 26489, 99] {
        e.extend_from_slice(&v.to_le_bytes());
    }
    assert_eq!(
        body(&SCSpecialtyCurrentPacket {
            from_zone_group: 22,
            to_zone_group: 5,
            results: vec![(PACK, 130), (26489, 99)],
        }),
        e
    );
    let mut s = PacketStream::from_bytes(vec![1u8, 2, 3]);
    assert_eq!(
        CSSellBackpackGoodsPacket::read(&mut s).unwrap(),
        CSSellBackpackGoodsPacket { obj_id: 0x030201 }
    );
    let mut s = PacketStream::from_bytes(vec![7u8, 0, 0, 0]);
    assert_eq!(
        CSSpecialtyRatioPacket::read(&mut s).unwrap(),
        CSSpecialtyRatioPacket { id: 7 }
    );
    let mut s = PacketStream::from_bytes(vec![9u8, 0, 0]);
    assert_eq!(CSListSpecialtyGoodsPacket::read(&mut s).unwrap().obj_id, 9);
    let mut s = PacketStream::from_bytes(vec![22u8, 0, 5, 0]);
    assert_eq!(
        CSRequestSpecialtyCurrentPacket::read(&mut s).unwrap(),
        CSRequestSpecialtyCurrentPacket {
            from_zone_group_id: 22,
            to_zone_group_id: 5
        }
    );
    let mut s = PacketStream::from_bytes(vec![1u8, 0, 0, 0, 2, 0, 0, 0, 3, 4, 5, 6, 7]);
    assert_eq!(
        CSBuySpecialtyItemPacket::read(&mut s).unwrap(),
        CSBuySpecialtyItemPacket {
            id: 1,
            refund: 2,
            currency: 3,
            kind: 4,
            obj_id: 0x070605
        }
    );
    let mut s = PacketStream::from_bytes(vec![0xffu8, 0xff, 0xff, 0xff, 2, 0, 0, 0]);
    assert_eq!(
        CSSpecialtyRecordLoadPacket::read(&mut s).unwrap(),
        CSSpecialtyRecordLoadPacket { zone_id: -1, id: 2 }
    );
}

#[derive(Default)]
struct Eco {
    mails: StdMutex<Vec<(SpecialtyMail, bool)>>,
    consumed: StdMutex<Vec<(u8, u32, i32, u64)>>,
    labor: StdMutex<Vec<(i16, i32)>>,
}

impl EconomyBridge for Eco {
    fn send_specialty_mail(
        &self,
        _: &GameServer,
        _: &PlayerRef,
        mail: &SpecialtyMail,
        for_crafter: bool,
    ) -> bool {
        self.mails.lock().unwrap().push((*mail, for_crafter));
        true
    }
    fn consume_equipment_item(
        &self,
        _: &GameServer,
        _: &PlayerRef,
        task: u8,
        template_id: u32,
        count: i32,
        item: u64,
    ) -> i32 {
        self.consumed
            .lock()
            .unwrap()
            .push((task, template_id, count, item));
        count
    }
    fn change_labor(&self, _: &GameServer, _: &PlayerRef, change: i16, actability: i32) {
        self.labor.lock().unwrap().push((change, actability));
    }
}

fn equip_pack(me: &PlayerRef, made_by: u32) {
    let mut item = Item::create(
        555,
        &ItemTemplate::plain(PACK, 1),
        1,
        0,
        1,
        &ItemData::default(),
        0,
    );
    item.slot = equip_slot::BACKPACK;
    item.made_unit_id = made_by;
    let eq = me.data().inventory.equipment().clone();
    eq.lock().unwrap().items.push(item);
}

#[tokio::test]
async fn sell_flow() {
    let server = test_server_with(data(0));
    let eco = Arc::new(Eco::default());
    server.systems.npcs.hooks.economy.install(eco.clone());
    let mut client = TestClient::new(&server, 1, 1);
    let mut c = test_character(1, "Alice", 100.0, 100.0);
    c.labor_power = 100;
    let me = spawn_player(&server, &client, c, 0x10);
    client.conn.active_char = Some(me.clone());
    let mut npc = Npc::new(SPECIALTY_NPC);
    npc.unit.transform.set_xyz(101.0, 101.0, 100.0);
    let npc = Entity::new(server.ids.object.get_next_id().unwrap(), npc);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&npc, &server.view_ctx());
    client.drain();

    // No pack.
    assert_eq!(sell_specialty(&server, &me, npc.obj_id), 0);
    assert_eq!(
        client.drain(),
        vec![(
            sc::SCErrorMsgPacket,
            body(&SCErrorMsgPacket::new(
                error_message::STORE_BACKPACK_NOGOODS,
                0,
                true
            ))
        )]
    );
    // Rate request without a pack: 0.
    CSSpecialtyRatioPacket { id: 0 }
        .handle(&server, &mut client.conn)
        .await
        .unwrap();
    assert_eq!(client.drain()[0].1, 0i32.to_le_bytes().to_vec());

    equip_pack(&me, 2);
    // Too far: 2.5 m (2D).
    npc.data().unit.transform.set_xyz(103.0, 101.0, 100.0);
    server
        .world
        .world(0)
        .unwrap()
        .add_visible_object(&npc, &server.view_ctx());
    client.drain();
    assert_eq!(sell_specialty(&server, &me, npc.obj_id), 0);
    assert_eq!(
        client.drain()[0].1,
        body(&SCErrorMsgPacket::new(error_message::TOO_FAR_AWAY, 0, true))
    );
    npc.data().unit.transform.set_xyz(101.0, 101.0, 100.0);
    server
        .world
        .world(0)
        .unwrap()
        .add_visible_object(&npc, &server.view_ctx());
    client.drain();

    CSSpecialtyRatioPacket { id: 0 }
        .handle(&server, &mut client.conn)
        .await
        .unwrap();
    assert_eq!(client.drain()[0].1, 130i32.to_le_bytes().to_vec());

    // Sell: crafter 2 gets 20 % (BackpackProfitShare is on in the default feature set?).
    let share = FeatureSet::default().check(Feature::BackpackProfitShare);
    CSSellBackpackGoodsPacket { obj_id: npc.obj_id }
        .handle(&server, &mut client.conn)
        .await
        .unwrap();
    assert!(client.drain().is_empty());
    let mails = eco.mails.lock().unwrap().clone();
    let expected = payout(39853, 130, 0, share);
    assert_eq!(
        mails[0].0.amount_of_items_seller,
        expected.amount_of_items_seller
    );
    assert_eq!(mails[0].0.crafter_id, 2);
    assert_eq!(mails[0].0.backpack_template_id, PACK);
    assert_eq!(mails[0].0.mail_delay_minutes, 480.0); // default config (`start` did not run)
    assert!(!mails[0].1);
    assert_eq!(mails.len(), if share { 2 } else { 1 });
    assert_eq!(
        eco.consumed.lock().unwrap().clone(),
        vec![(ITEM_TASK_SELL_BACKPACK, PACK, 1, 555)]
    );
    assert_eq!(
        eco.labor.lock().unwrap().clone(),
        vec![(-60, ACTABILITY_COMMERCE)]
    );
    assert_eq!(
        server.systems.npcs.specialty.sold_in_tick(PACK, ZONE_GROUP),
        1
    );

    // Not enough labor.
    me.data().labor_power = 59;
    assert_eq!(sell_specialty(&server, &me, npc.obj_id), 0);
    assert_eq!(
        client.drain()[0].1,
        body(&SCErrorMsgPacket::new(
            error_message::NOT_ENOUGH_LABOR_POWER,
            0,
            true
        ))
    );

    // Route rates.
    CSRequestSpecialtyCurrentPacket {
        from_zone_group_id: 22,
        to_zone_group_id: ZONE_GROUP as u16,
    }
    .handle(&server, &mut client.conn)
    .await
    .unwrap();
    let frames = client.drain();
    assert_eq!(frames[0].0, sc::SCSpecialtyCurrentPacket);
    assert_eq!(
        frames[0].1,
        body(&SCSpecialtyCurrentPacket {
            from_zone_group: 22,
            to_zone_group: ZONE_GROUP as u16,
            results: vec![(PACK, 130), (26489, 130)],
        })
    );
}

#[tokio::test]
async fn sell_without_mail_support_fails_like_a_failed_send() {
    let server = test_server_with(data(26_000));
    let client = TestClient::new(&server, 1, 1);
    let mut c = test_character(1, "Alice", 100.0, 100.0);
    c.labor_power = 100;
    let me = spawn_player(&server, &client, c, 0x10);
    let mut npc = Npc::new(SPECIALTY_NPC);
    npc.unit.transform.set_xyz(101.0, 101.0, 100.0);
    let npc = Entity::new(server.ids.object.get_next_id().unwrap(), npc);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&npc, &server.view_ctx());
    equip_pack(&me, 1);
    let mut client = client;
    client.drain();
    assert_eq!(sell_specialty(&server, &me, npc.obj_id), 39853);
    assert_eq!(
        client.drain(),
        vec![(
            sc::SCErrorMsgPacket,
            body(&SCErrorMsgPacket::new(
                error_message::MAIL_UNKNOWN_FAILURE,
                0,
                true
            ))
        )]
    );
    assert_eq!(
        server.systems.npcs.specialty.sold_in_tick(PACK, ZONE_GROUP),
        0
    );
}

#[test]
#[ignore = "needs AAEMU_COMPACT_DB"]
fn real_compact_db_specialties() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn = rusqlite::Connection::open(&path).unwrap();
    let files =
        crate::data::client_files::ClientFileManager::from_sources(&[], std::path::Path::new("."));
    let ctx = DataLoadCtx {
        conn: &conn,
        app_path: std::path::Path::new("."),
        files: &files,
        language: "en_us",
    };
    let d = SpecialtyData::load(&ctx).unwrap();
    let count =
        |sql: &str| -> usize { conn.query_row(sql, [], |r| r.get::<_, i64>(0)).unwrap() as usize };
    assert_eq!(
        d.specialties.len(),
        count("SELECT COUNT(*) FROM specialties")
    );
    assert_eq!(
        d.bundle_items.len(),
        count("SELECT COUNT(*) FROM specialty_bundle_items")
    );
    assert_eq!(d.npcs.len(), count("SELECT COUNT(*) FROM specialty_npcs"));
    assert_eq!(d.item_refunds.len(), count("SELECT COUNT(*) FROM items"));
    assert_eq!(
        d.specialty_packs.len(),
        count("SELECT COUNT(*) FROM items WHERE specialty_zone_id IS NOT NULL AND specialty_zone_id <> 0")
    );
    let mapped: usize = d.bundle_items_mapped.values().map(HashMap::len).sum();
    assert_eq!(mapped, d.bundle_items.len());
    assert!(d
        .specialties
        .values()
        .all(|s| s.vendor_exist == (s.id == 1)));
    // Sample price: the first bundle item.
    let b = d.bundle_items[&1000];
    eprintln!(
        "specialties {} bundle items {} npcs {} items {} packs {}; bundle 1000 base price {}",
        d.specialties.len(),
        d.bundle_items.len(),
        d.npcs.len(),
        d.item_refunds.len(),
        d.specialty_packs.len(),
        base_price(&b, d.item_refunds.get(&b.item_id).copied().unwrap_or(0))
    );
}
