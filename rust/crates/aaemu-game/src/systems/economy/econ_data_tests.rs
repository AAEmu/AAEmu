//! Tests of the item data / item model / loot / repair / timer port: real compact DB loads
//! (gated by `AAEMU_COMPACT_DB`) and flow tests without MySQL.

use std::sync::{Arc, Mutex};

use aaemu_commons::{Marshal, PacketStream};

use crate::data::items::{
    bind_type, EquipTemplate, FullGradeTemplate, GradeDistributions, ItemData, ItemTemplate,
    LootPackDroppingNpc, TemplateKind, ITEM_COINS,
};
use crate::data::GameData;
use crate::models::container::{equip_slot, slot_type};
use crate::models::item::{EquipStat, Item, ItemClass};
use crate::models::item_task::{task_type, ItemTask};
use crate::models::npc::Npc;
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::economy::hooks::{NpcEconomyHooks, NpcShopInfo};
use crate::world::{Entity, NpcRef, PlayerRef, WorldObject};

use super::item_timers;
use super::loot::ScriptedRng;
use super::loot_data::{Loot, LootPack};
use super::loot_packets::*;
use super::looting::*;
use super::repair::{self, DurabilityLossTargets};
use super::tests::{give, item_at, task_body, test_data, World};
use crate::systems::economy::inventory::Inv;
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};

const PRICEY: u32 = 5000; // armor with a price (repair cost)
const BOP: u32 = 5001; // bind on pickup loot item
const NPC_TEMPLATE: u32 = 1234;
const NPC_OBJ: u32 = 0x200;

fn data() -> GameData {
    let mut d = test_data();
    d.items.insert_grade(FullGradeTemplate {
        grade: 0,
        grade_order: 0,
        durability: 1.0,
        stat_multiplier: 100,
        refund_multiplier: 100,
        ..Default::default()
    });
    d.items.config.durability_repair_cost_factor = 25.0;
    d.items.templates.insert(
        PRICEY,
        ItemTemplate {
            price: 1000,
            kind: TemplateKind::Armor(EquipTemplate {
                slot_type_id: 3,
                coverage: 10,
                durability_ratio: 1.0,
                durability_multiplier: 100,
                ..EquipTemplate::default()
            }),
            ..ItemTemplate::plain(PRICEY, 1)
        },
    );
    d.items.templates.insert(
        BOP,
        ItemTemplate {
            bind_type: bind_type::BIND_ON_PICKUP,
            ..ItemTemplate::plain(BOP, 1)
        },
    );
    let mut coins = ItemTemplate::plain(ITEM_COINS, i32::MAX);
    coins.fixed_grade = 0;
    d.items.templates.insert(ITEM_COINS, coins);
    d.items.loot_pack_dropping_npc.insert(
        NPC_TEMPLATE,
        vec![LootPackDroppingNpc {
            id: 1,
            npc_id: NPC_TEMPLATE,
            loot_pack_id: 77,
            default_pack: false,
        }],
    );
    let loot = |id, item, rate, min, max| Loot {
        id,
        group: 0,
        item_id: item,
        drop_rate: rate,
        min_amount: min,
        max_amount: max,
        loot_pack_id: 77,
        grade_id: 0,
        always_drop: false,
    };
    let loots = vec![
        loot(1, super::tests::STACK, 10_000_000, 3, 3),
        loot(2, ITEM_COINS, 10_000_000, 50, 50),
        loot(3, BOP, 10_000_000, 1, 1),
    ];
    d.systems.economy.loot.packs.insert(
        77,
        LootPack {
            id: 77,
            group_count: 0,
            loots_by_group_no: vec![(0, loots.clone())],
            loots,
            ..Default::default()
        },
    );
    d
}

fn world_with(d: GameData) -> World {
    let server = Arc::new(test_server_with(d));
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

fn spawn_npc(w: &mut World) -> NpcRef {
    let mut n = Npc::new(NPC_TEMPLATE);
    n.unit.name = "Wolf".into();
    n.unit.transform.set_xyz(102.0, 100.0, 100.0);
    let n = Entity::new(NPC_OBJ, n);
    let world = w.server.world.world_of(&WorldObject::from(&w.pa)).unwrap();
    world.spawn(&n, &w.server.view_ctx());
    w.a.drain();
    w.b.drain();
    n
}

fn body(p: &impl Marshal) -> Vec<u8> {
    let mut s = PacketStream::new();
    p.write(&mut s);
    s.into_bytes()
}

fn run<P: ClientPacket>(w: &mut World, which: u8, b: &[u8]) {
    super::tests::run::<P>(w, which, b);
}

// ---------------------------------------------------------------------------------------------
// Loot
// ---------------------------------------------------------------------------------------------

#[tokio::test]
async fn npc_loot_is_generated_and_looted_with_loot_all() {
    let mut w = world_with(data());
    let npc = spawn_npc(&mut w);
    let mut rng = ScriptedRng::new(&[0, 0, 0, 3, 50, 1], &[]);
    LootManager::generate_loot_with(
        &w.server,
        &WorldObject::from(&npc),
        Some(&WorldObject::from(&w.pa)),
        &mut rng,
    );
    // Killer without tagging -> sole eligible player
    let state = SCLootableStatePacket {
        loot_owner_type: loot_owner_type::NPC,
        loot_owner_obj_id: NPC_OBJ,
        has_loot: true,
    };
    assert_eq!(w.a.drain(), vec![(sc::SCLootableStatePacket, body(&state))]);
    assert!(w.b.drain().is_empty());
    assert_eq!(w.server.systems.economy.loot.item_count(NPC_OBJ), 3);
    // Fake ids: (ObjId << 32) + (Npc << 16) + index
    let c = w.server.systems.economy.loot.container(NPC_OBJ);
    let ids: Vec<u64> = c
        .lock()
        .unwrap()
        .items
        .values()
        .map(|e| e.item.id)
        .collect();
    assert_eq!(
        ids,
        vec![
            (u64::from(NPC_OBJ) << 32) + (1 << 16) + 1,
            (u64::from(NPC_OBJ) << 32) + (1 << 16) + 2,
            (u64::from(NPC_OBJ) << 32) + (1 << 16) + 3,
        ]
    );
    // Generating again does nothing
    LootManager::generate_loot_with(&w.server, &WorldObject::from(&npc), None, &mut rng);
    assert_eq!(w.server.systems.economy.loot.item_count(NPC_OBJ), 3);

    // Open with loot all (G)
    let money_before = w.pa.data().money;
    let mut s = PacketStream::new();
    s.write_bc(NPC_OBJ).write_bc(0).write_bool(true);
    run::<CSLootOpenBagPacket>(&mut w, 0, s.as_bytes());
    let ops = w.a.drain_ops();
    assert_eq!(
        ops,
        vec![
            sc::SCItemTaskSuccessPacket, // LootAll: stack x3
            sc::SCLootItemTookPacket,
            sc::SCItemTaskSuccessPacket, // DepositMoney +50
            sc::SCLootItemTookPacket,
            sc::SCItemTaskSuccessPacket, // LootAll: BoP item
            sc::SCLootItemTookPacket,
            sc::SCLootableStatePacket, // no items left
        ]
    );
    assert_eq!(w.pa.data().money, money_before + 50);
    assert_eq!(item_at(&w.pa, slot_type::INVENTORY, 0).unwrap().count, 3);
    assert_eq!(w.server.systems.economy.loot.item_count(NPC_OBJ), 0);
    // Closing the bag is harmless
    let mut s = PacketStream::new();
    s.write_u16(0).write_u16(1).write_bc(NPC_OBJ).write_u8(0);
    run::<CSLootCloseBagPacket>(&mut w, 0, s.as_bytes());
}

fn kill_and_generate(w: &World, npc: &NpcRef) {
    let mut rng = ScriptedRng::new(&[0, 0, 0, 3, 50, 1], &[]);
    LootManager::generate_loot_with(
        &w.server,
        &WorldObject::from(npc),
        Some(&WorldObject::from(&w.pa)),
        &mut rng,
    );
}

#[tokio::test]
async fn releasing_the_object_id_drops_the_loot_container() {
    let mut w = world_with(data());
    super::bridges::install(&w.server);
    let npc = spawn_npc(&mut w);
    kill_and_generate(&w, &npc);
    assert_eq!(w.server.systems.economy.loot.item_count(NPC_OBJ), 3);
    assert!(w.server.systems.economy.loot.get(NPC_OBJ).is_some());

    // Any subsystem's despawn path ends in the object id release.
    w.server
        .world
        .world_of(&WorldObject::from(&npc))
        .unwrap()
        .delete(&npc);
    w.server.ids.object.release_id(NPC_OBJ);
    assert!(w.server.systems.economy.loot.get(NPC_OBJ).is_none());
    assert_eq!(w.server.systems.economy.loot.item_count(NPC_OBJ), 0);
}

#[tokio::test]
async fn npc_on_a_recycled_object_id_drops_loot_again() {
    let mut w = world_with(data());
    super::bridges::install(&w.server);
    let first = spawn_npc(&mut w);
    kill_and_generate(&w, &first);
    assert_eq!(w.server.systems.economy.loot.item_count(NPC_OBJ), 3);
    assert!(
        w.server
            .systems
            .economy
            .loot
            .container(NPC_OBJ)
            .lock()
            .unwrap()
            .already_generated
    );

    w.server
        .world
        .world_of(&WorldObject::from(&first))
        .unwrap()
        .delete(&first);
    w.server.ids.object.release_id(NPC_OBJ);

    let second = spawn_npc(&mut w);
    assert_eq!(second.obj_id, NPC_OBJ);
    assert_eq!(
        w.server.systems.economy.loot.item_count(NPC_OBJ),
        0,
        "no stale items for the new NPC"
    );
    assert!(
        !w.server
            .systems
            .economy
            .loot
            .container(NPC_OBJ)
            .lock()
            .unwrap()
            .already_generated
    );
    kill_and_generate(&w, &second);
    assert_eq!(w.server.systems.economy.loot.item_count(NPC_OBJ), 3);
}

#[tokio::test]
async fn creating_an_object_on_a_reused_id_drops_a_stale_loot_container() {
    use crate::systems::npcs::spawn_manager;

    let mut w = world_with(data());
    super::bridges::install(&w.server);
    let npc = spawn_npc(&mut w);
    kill_and_generate(&w, &npc);
    assert!(w.server.systems.economy.loot.get(NPC_OBJ).is_some());
    // The spawners call this for every freshly created NPC / doodad, even when the id was
    // never released through a path that clears the container.
    spawn_manager::forget_object_fields(&w.server, NPC_OBJ);
    assert!(w.server.systems.economy.loot.get(NPC_OBJ).is_none());
}

#[tokio::test]
async fn open_bag_without_loot_all_sends_bag_data_and_single_take_works() {
    let mut w = world_with(data());
    let npc = spawn_npc(&mut w);
    let mut rng = ScriptedRng::new(&[0, 0, 0, 3, 50, 1], &[]);
    LootManager::generate_loot_with(
        &w.server,
        &WorldObject::from(&npc),
        Some(&WorldObject::from(&w.pa)),
        &mut rng,
    );
    w.a.drain();
    let mut s = PacketStream::new();
    s.write_bc(NPC_OBJ).write_bc(0).write_bool(false);
    run::<CSLootOpenBagPacket>(&mut w, 0, s.as_bytes());
    let f = w.a.drain();
    assert_eq!(f.len(), 1);
    assert_eq!(f[0].0, sc::SCLootBagDataPacket);
    assert_eq!(f[0].1[0], 3); // three items
    assert_eq!(*f[0].1.last().unwrap(), 0); // lootAll false

    // Bob is not eligible but the rule is free for all
    let mut s = PacketStream::new();
    s.write_u16(2)
        .write_u16(1)
        .write_bc(NPC_OBJ)
        .write_u16(0)
        .write_u16(0);
    run::<CSLootItemPacket>(&mut w, 1, s.as_bytes());
    let took = SCLootItemTookPacket {
        item_template_id: ITEM_COINS,
        item_index: 2,
        loot_owner_type: 1,
        loot_owner_id: NPC_OBJ,
        count: 50,
    };
    assert_eq!(
        w.b.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::DEPOSIT_MONEY, vec![ItemTask::MoneyChange(50)])
            ),
            (sc::SCLootItemTookPacket, body(&took)),
        ]
    );
    assert_eq!(w.server.systems.economy.loot.item_count(NPC_OBJ), 2);
}

#[tokio::test]
async fn full_bag_fails_and_restores_the_entry() {
    let mut w = world_with(data());
    let npc = spawn_npc(&mut w);
    // Fill the bag with single-stack items
    let size = Inv::of(&w.pa).bag().lock().unwrap().free_slot_count();
    for slot in 0..size {
        give(
            &w,
            &w.pa.clone(),
            slot_type::INVENTORY,
            super::tests::SINGLE,
            1,
            slot,
        );
    }
    Inv::of(&w.pa)
        .bag()
        .lock()
        .unwrap()
        .update_free_slot_count();
    let mut rng = ScriptedRng::new(&[0, 0, 0, 3, 50, 1], &[]);
    LootManager::generate_loot_with(
        &w.server,
        &WorldObject::from(&npc),
        Some(&WorldObject::from(&w.pa)),
        &mut rng,
    );
    w.a.drain();
    assert!(!LootManager::try_take_loot(
        &w.server, &w.pa, NPC_OBJ, 1, false
    ));
    let failed = SCLootItemFailedPacket {
        error_message: super::error_message::BAG_FULL,
        loot_owner_type: 1,
        loot_owner_obj_id: NPC_OBJ,
        item_index: 1,
        item_template_id: super::tests::STACK,
    };
    assert_eq!(
        w.a.drain(),
        vec![(sc::SCLootItemFailedPacket, body(&failed))]
    );
    assert_eq!(w.server.systems.economy.loot.item_count(NPC_OBJ), 3);
}

struct TeamOf(Vec<PlayerRef>);
impl LootTeamHooks for TeamOf {
    fn active_team(&self, _: &GameServer, id: u32) -> Option<TeamLootInfo> {
        (id == 5).then(|| TeamLootInfo {
            team_id: 5,
            members: self.0.clone(),
            looting_rule: LootingRule {
                loot_method: looting_rule_method::FREE_FOR_ALL,
                minimum_grade: 0,
                loot_master: 0,
                roll_for_bind_on_pickup: true,
            },
        })
    }
}

struct Tagged;
impl LootNpcHooks for Tagged {
    fn tagging(&self, _: &GameServer, _: &NpcRef) -> NpcTagging {
        NpcTagging {
            tag_team: 5,
            tagger: None,
        }
    }
}

#[tokio::test]
async fn bind_on_pickup_loot_is_rolled_by_the_team() {
    let mut w = world_with(data());
    let npc = spawn_npc(&mut w);
    let lm = &w.server.systems.economy.loot;
    lm.hooks
        .teams
        .install(Arc::new(TeamOf(vec![w.pa.clone(), w.pb.clone()])));
    lm.hooks.npcs.install(Arc::new(Tagged));
    let mut rng = ScriptedRng::new(&[0, 0, 0, 3, 50, 1], &[]);
    LootManager::generate_loot_with(&w.server, &WorldObject::from(&npc), None, &mut rng);
    w.a.drain();
    w.b.drain();
    // The BoP item (index 3) needs a roll from both members
    assert!(!LootManager::try_take_loot(
        &w.server, &w.pa, NPC_OBJ, 3, false
    ));
    assert_eq!(w.a.drain_ops(), vec![sc::SCLootDicePacket]);
    assert_eq!(w.b.drain_ops(), vec![sc::SCLootDicePacket]);
    // Alice rolls 60, Bob passes
    let mut rng = ScriptedRng::new(&[60], &[]);
    LootManager::do_player_roll(&w.server, &w.pa, NPC_OBJ, 3, true, &mut rng);
    assert_eq!(w.a.drain_ops(), vec![sc::SCLootDiceNotifyPacket]);
    assert_eq!(w.b.drain_ops(), vec![sc::SCLootDiceNotifyPacket]);
    let mut rng = ScriptedRng::new(&[0], &[]);
    LootManager::do_player_roll(&w.server, &w.pb, NPC_OBJ, 3, false, &mut rng);
    let summary = SCLootDiceSummaryPacket {
        loot_owner_type: 1,
        loot_owner: NPC_OBJ,
        item_index: 3,
        dice_list: vec![(1, 60), (2, -1)],
    };
    let fb = w.b.drain();
    assert_eq!(fb[0].0, sc::SCLootDiceNotifyPacket);
    assert_eq!(fb[1], (sc::SCLootDiceSummaryPacket, body(&summary)));
    let fa = w.a.drain_ops();
    assert_eq!(
        fa,
        vec![
            sc::SCLootDiceNotifyPacket,
            sc::SCLootDiceSummaryPacket,
            sc::SCItemTaskSuccessPacket,
            sc::SCLootItemTookPacket,
        ]
    );
    assert_eq!(
        item_at(&w.pa, slot_type::INVENTORY, 0).unwrap().template_id,
        BOP
    );
    // Not BoP: free for all, Bob takes the stack
    assert!(LootManager::try_take_loot(
        &w.server, &w.pb, NPC_OBJ, 1, false
    ));
}

#[tokio::test]
async fn make_loot_public_finishes_pending_rolls() {
    let mut w = world_with(data());
    let npc = spawn_npc(&mut w);
    let lm = &w.server.systems.economy.loot;
    lm.hooks
        .teams
        .install(Arc::new(TeamOf(vec![w.pa.clone(), w.pb.clone()])));
    lm.hooks.npcs.install(Arc::new(Tagged));
    let mut rng = ScriptedRng::new(&[0, 0, 0, 3, 50, 1], &[]);
    LootManager::generate_loot_with(&w.server, &WorldObject::from(&npc), None, &mut rng);
    assert!(!LootManager::try_take_loot(
        &w.server, &w.pa, NPC_OBJ, 3, false
    ));
    let c = lm.container(NPC_OBJ);
    {
        let mut g = c.lock().unwrap();
        assert!(!g.can_make_public(chrono::Utc::now().timestamp_millis()));
        g.creation_time_ms -= 181_000;
        assert!(g.can_make_public(chrono::Utc::now().timestamp_millis()));
    }
    assert_eq!(lm.npcs_to_make_public(&[NPC_OBJ, 999]), vec![NPC_OBJ]);
    w.a.drain();
    LootManager::make_loot_public(&w.server, NPC_OBJ);
    // Everybody passed: the entry stays, rolls are cleared; then public broadcast
    let ops = w.a.drain_ops();
    assert_eq!(
        ops,
        vec![sc::SCLootDiceSummaryPacket, sc::SCLootableStatePacket]
    );
    let g = c.lock().unwrap();
    assert_eq!(
        g.team_looting_rule.unwrap().loot_method,
        looting_rule_method::PUBLIC
    );
    assert!(g.items[&3].player_rolls.is_empty());
}

#[test]
fn loot_c2s_packets_decode() {
    let mut s = PacketStream::new();
    s.write_u16(3)
        .write_u16(1)
        .write_bc(0x123456)
        .write_u8(9)
        .write_bool(true)
        .write_u8(0xaa);
    let mut r = PacketStream::from_bytes(s.into_bytes());
    let p = <CSLootDicePacket as ClientPacket>::read(&mut r).unwrap();
    assert_eq!(
        p,
        CSLootDicePacket {
            item_index: 3,
            loot_owner_type: 1,
            loot_owner_obj_id: 0x123456,
            b: 9,
            roll_request: true,
            remaining: vec![0xaa],
        }
    );
    let mut s = PacketStream::new();
    s.write_u32(7)
        .write_u8(2)
        .write_u8(3)
        .write_u32(11)
        .write_bool(false)
        .write_u8(1);
    let mut r = PacketStream::from_bytes(s.into_bytes());
    let p = <CSChangeLootingRulePacket as ClientPacket>::read(&mut r).unwrap();
    assert_eq!(p.team_id, 7);
    assert_eq!(p.looting_rule.loot_master, 11);
    assert_eq!(p.change_flags, 1);
}

struct RuleRecorder(Mutex<Vec<(u32, u8, LootingRule)>>);
impl LootTeamHooks for RuleRecorder {
    fn change_looting_rule(
        &self,
        _: &GameServer,
        _: &PlayerRef,
        team: u32,
        flags: u8,
        rule: LootingRule,
    ) {
        self.0.lock().unwrap().push((team, flags, rule));
    }
}

#[tokio::test]
async fn change_looting_rule_goes_to_the_team_hook() {
    let mut w = world_with(data());
    let rec = Arc::new(RuleRecorder(Mutex::new(Vec::new())));
    w.server
        .systems
        .economy
        .loot
        .hooks
        .teams
        .install(rec.clone());
    let mut s = PacketStream::new();
    s.write_u32(7)
        .write_u8(2)
        .write_u8(3)
        .write_u32(11)
        .write_bool(true)
        .write_u8(4);
    run::<CSChangeLootingRulePacket>(&mut w, 0, s.as_bytes());
    let got = rec.0.lock().unwrap().clone();
    assert_eq!(got.len(), 1);
    assert_eq!(got[0].0, 7);
    assert_eq!(got[0].1, 4);
    assert_eq!(got[0].2.loot_method, looting_rule_method::LOOT_MASTER);
}

// ---------------------------------------------------------------------------------------------
// Repair / durability
// ---------------------------------------------------------------------------------------------

struct Smith;
impl NpcEconomyHooks for Smith {
    fn shop_info(&self, _: &GameServer, _: &NpcRef) -> Option<NpcShopInfo> {
        Some(NpcShopInfo {
            blacksmith: true,
            ..Default::default()
        })
    }
}

#[test]
fn repair_cost_formula() {
    let d = data();
    let mut i = Item::create(1, d.items.template(PRICEY).unwrap(), 1, 0, 1, &d.items, 0);
    assert_eq!(i.max_durability(&d.items), 10);
    assert_eq!(i.details.durability, 10);
    assert_eq!(i.repair_cost(&d.items), Some(0));
    i.details.durability = 4;
    // 25 * 0.01 * (1 - 4/10) * 1000 = 150; * 100 * 0.01 = 150 (float rounding: ceil)
    assert_eq!(i.repair_cost(&d.items), Some(150));
    i.details.durability = 0;
    assert_eq!(i.repair_cost(&d.items), Some(250));
    assert!(!i.is_not_destroyed(&d.items));
    // missing full grade -> None (C# NullReferenceException)
    i.grade = 3;
    assert_eq!(i.repair_cost(&d.items), None);
}

#[tokio::test]
async fn repair_single_equipment_at_a_blacksmith() {
    let mut w = world_with(data());
    let npc = spawn_npc(&mut w);
    w.server.systems.economy.hooks.npcs.install(Arc::new(Smith));
    let id = give(
        &w,
        &w.pa.clone(),
        slot_type::EQUIPMENT,
        PRICEY,
        1,
        equip_slot::CHEST,
    );
    {
        let eq = Inv::of(&w.pa).equipment();
        eq.lock()
            .unwrap()
            .get_item_by_id_mut(id)
            .unwrap()
            .details
            .durability = 4;
    }
    w.pa.data().money = 1000;
    // Not interacting with anyone: nothing repaired, empty Repair task packet
    let mut s = PacketStream::new();
    s.write_u8(slot_type::EQUIPMENT)
        .write_u8(equip_slot::CHEST as u8)
        .write_bool(false);
    run::<repair::CSRepairSingleEquipmentPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(
        w.a.drain(),
        vec![(
            sc::SCItemTaskSuccessPacket,
            task_body(task_type::REPAIR, vec![])
        )]
    );
    w.pa.data().unit.current_interaction_object = npc.obj_id;
    run::<repair::CSRepairSingleEquipmentPacket>(&mut w, 0, s.as_bytes());
    let item = Inv::of(&w.pa).equipped(equip_slot::CHEST).unwrap();
    assert_eq!(item.details.durability, 10);
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::DEPOSIT_MONEY, vec![ItemTask::MoneyChange(-150)])
            ),
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::REPAIR, vec![ItemTask::update(&item)])
            ),
        ]
    );
    assert_eq!(w.pa.data().money, 850);
    // Too far away: error message
    {
        let eq = Inv::of(&w.pa).equipment();
        eq.lock()
            .unwrap()
            .get_item_by_id_mut(id)
            .unwrap()
            .details
            .durability = 1;
    }
    npc.data().unit.transform.set_xyz(200.0, 100.0, 100.0);
    let mut s = PacketStream::new();
    s.write_bool(false);
    run::<repair::CSRepairAllEquipmentsPacket>(&mut w, 0, s.as_bytes());
    assert_eq!(
        w.a.drain_ops(),
        vec![sc::SCErrorMsgPacket, sc::SCItemTaskSuccessPacket]
    );
}

#[tokio::test]
async fn repair_all_respects_total_available_money() {
    let mut d = data();
    let mut boots = d.items.templates[&PRICEY].clone();
    boots.id = 5002;
    if let TemplateKind::Armor(equip) = &mut boots.kind {
        equip.slot_type_id = 7;
    }
    d.items.templates.insert(5002, boots);
    let mut w = world_with(d);
    let npc = spawn_npc(&mut w);
    w.server.systems.economy.hooks.npcs.install(Arc::new(Smith));
    w.pa.data().unit.current_interaction_object = npc.obj_id;
    let mut ids = Vec::new();
    for (slot, template) in [(equip_slot::CHEST, PRICEY), (equip_slot::FEET, 5002)] {
        let id = give(&w, &w.pa.clone(), slot_type::EQUIPMENT, template, 1, slot);
        Inv::of(&w.pa)
            .equipment()
            .lock()
            .unwrap()
            .get_item_by_id_mut(id)
            .unwrap()
            .details
            .durability = 4;
        ids.push(id);
    }
    w.pa.data().money = 200; // Each costs 150; only one can be paid for.
    run::<repair::CSRepairAllEquipmentsPacket>(&mut w, 0, &[0]);
    assert_eq!(w.pa.data().money, 50);
    let repaired = Inv::of(&w.pa).equipped(equip_slot::FEET).unwrap();
    assert_eq!(repaired.id, ids[1]);
    assert_eq!(repaired.details.durability, 10);
    assert_eq!(
        Inv::of(&w.pa)
            .equipped(equip_slot::CHEST)
            .unwrap()
            .details
            .durability,
        4
    );
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::DEPOSIT_MONEY, vec![ItemTask::MoneyChange(-150)])
            ),
            (
                sc::SCItemTaskSuccessPacket,
                task_body(task_type::REPAIR, vec![ItemTask::update(&repaired)])
            ),
        ]
    );
}

#[tokio::test]
async fn durability_loss_and_death() {
    let w = world_with(data());
    let id = give(
        &w,
        &w.pa.clone(),
        slot_type::EQUIPMENT,
        PRICEY,
        1,
        equip_slot::CHEST,
    );
    let mut a = w.a;
    // 30% chance, roll 0.5 * 100 = 50 > 30: nothing
    let mut rng = ScriptedRng::new(&[], &[0.5]);
    let destroyed = repair::apply_durability_loss_to_equipment(
        &w.server,
        &w.pa,
        1,
        DurabilityLossTargets::AllArmor,
        30.0,
        &mut rng,
    );
    assert!(destroyed.is_empty());
    assert!(a.drain().is_empty());
    // Weapons only: armor is filtered out
    repair::apply_durability_loss_to_equipment(
        &w.server,
        &w.pa,
        1,
        DurabilityLossTargets::AllWeapons,
        -1.0,
        &mut rng,
    );
    assert!(a.drain().is_empty());
    let mut rng = ScriptedRng::new(&[], &[0.1]);
    repair::apply_durability_loss_to_equipment(
        &w.server,
        &w.pa,
        3,
        DurabilityLossTargets::AllArmor,
        30.0,
        &mut rng,
    );
    let item = Inv::of(&w.pa).equipped(equip_slot::CHEST).unwrap();
    assert_eq!(item.details.durability, 7);
    assert_eq!(
        a.drain(),
        vec![(
            sc::SCItemTaskSuccessPacket,
            task_body(task_type::DURABILITY_LOSS, vec![ItemTask::update(&item)])
        )]
    );
    // Death: config ratio 9 -> destroyed
    assert_eq!(repair::death_durability_loss(&w.server, false), 0);
    repair::on_death(&w.server, &w.pa, 9);
    let item = Inv::of(&w.pa).equipped(equip_slot::CHEST).unwrap();
    assert_eq!(item.details.durability, 0);
    assert_eq!(item.id, id);
    a.drain();
    // Destroyed items are ignored afterwards
    repair::on_death(&w.server, &w.pa, 9);
    assert!(a.drain().is_empty());
}

// ---------------------------------------------------------------------------------------------
// Timers
// ---------------------------------------------------------------------------------------------

#[tokio::test]
async fn item_timers_expire_items() {
    let w = world_with(data());
    let id1 = give(
        &w,
        &w.pa.clone(),
        slot_type::INVENTORY,
        super::tests::SINGLE,
        1,
        0,
    );
    let id2 = give(
        &w,
        &w.pa.clone(),
        slot_type::INVENTORY,
        super::tests::SINGLE,
        1,
        1,
    );
    let mut a = w.a;
    let bag = Inv::of(&w.pa).bag();
    {
        let mut g = bag.lock().unwrap();
        g.get_item_by_id_mut(id1).unwrap().expiration_time = 1000; // long expired
        g.get_item_by_id_mut(id2)
            .unwrap()
            .expiration_online_minutes_left = 1.0;
    }
    let now_ms = 2_000_000;
    let n = item_timers::update_item_container_timers(&w.server, &w.pa, &bag, false, 0.5, now_ms);
    assert_eq!(n, 1);
    let f = a.drain();
    assert_eq!(f.len(), 2);
    let mut lifespan = PacketStream::new();
    lifespan
        .write_bool(false)
        .write_u64(id1)
        .write_u32(super::tests::SINGLE)
        .write_i64(0);
    assert_eq!(f[0], (sc::SCSyncItemLifespanPacket, lifespan.into_bytes()));
    assert_eq!(f[1].0, sc::SCItemTaskSuccessPacket);
    assert_eq!(f[1].1[0], task_type::LIFESPAN_EXPIRATION);
    assert!(
        (bag.lock()
            .unwrap()
            .get_item_by_id(id2)
            .unwrap()
            .expiration_online_minutes_left
            - 0.5)
            .abs()
            < 1e-9
    );
    // Online minutes run out
    let n = item_timers::update_item_container_timers(&w.server, &w.pa, &bag, false, 0.6, now_ms);
    assert_eq!(n, 1);
    assert!(bag.lock().unwrap().items.is_empty());
    // Whole-server tick with a clock: 0 left
    w.server.systems.economy.item_timers.reset(now_ms);
    assert_eq!(item_timers::update_item_timers(&w.server, now_ms + 1000), 0);
}

#[test]
fn initial_expiration_packets() {
    let d = ItemData::default();
    let mut i = Item::create(9, &ItemTemplate::plain(3, 1), 1, 0, 1, &d, 100);
    i.expiration_time = 5000;
    i.expiration_online_minutes_left = 2.0;
    let p = item_timers::initial_item_expiration_packets([None, Some(&i)], 10_500);
    assert_eq!(p.len(), 2);
    assert_eq!((p[0].added, p[0].expire_time), (true, 5000));
    assert_eq!(p[1].expire_time, 10 + 120);
}

// ---------------------------------------------------------------------------------------------
// Item model
// ---------------------------------------------------------------------------------------------

#[test]
fn treasure_map_generation_and_flags() {
    let d = ItemData::default();
    let t = ItemTemplate {
        fixed_grade: -1,
        ..ItemTemplate::new(24581, TemplateKind::TreasureMap)
    };
    let i = Item::create(1, &t, 1, 3, 1, &d, 0);
    // No chest spawners: Mythic
    assert_eq!(i.grade, 11);
    let i = Item::create_ex(
        1,
        &t,
        1,
        3,
        1,
        &d,
        0,
        Some(crate::models::item::TreasureMapTarget {
            x: 1.5,
            y: 2.0,
            chest_group_id: Some(57),
        }),
    );
    assert_eq!(i.grade, 3);
    assert_eq!(i.details.map_position_x, 6144);
    assert_eq!(i.map_position_y(), 2.0);
    let mut i = i;
    i.set_flag(crate::models::item::ITEM_FLAG_SECURE);
    assert!(i.has_flag(crate::models::item::ITEM_FLAG_SECURE));
    i.remove_flag(crate::models::item::ITEM_FLAG_SECURE);
    assert!(!i.has_flag(crate::models::item::ITEM_FLAG_SECURE));
}

#[test]
fn unknown_item_type_uses_template_class_for_details() {
    let t = ItemTemplate {
        bind_type: 1,
        fixed_grade: -1,
        ..ItemTemplate::new(
            50,
            TemplateKind::Weapon(EquipTemplate {
                ..EquipTemplate::default()
            }),
        )
    };
    let mut blob = PacketStream::new();
    blob.write_u32(0).write_u8(77).write_i16(0).write_u32(0);
    blob.write_i64(0).write_u32(0);
    for _ in 0..7 {
        blob.write_u32(0);
    }
    blob.write_u16(0).write_u16(0);
    let row = crate::models::item::ItemRow {
        type_name: "AAEmu.Game.Models.Game.Items.Gone".into(),
        template_id: 50,
        details: blob.into_bytes(),
        ..Default::default()
    };
    let i = Item::from_row(row, Some(&t), 1);
    assert_eq!(i.class, ItemClass::Weapon);
    assert_eq!(i.details.durability, 77);
}

#[test]
fn auction_search_helpers() {
    let mut d = ItemData::default();
    for (id, name, a) in [
        (1u32, "Iron Sword", 1),
        (2, "Iron Shield", 2),
        (3, "Bread", 1),
    ] {
        let mut t = ItemTemplate::plain(id, 1);
        t.name = name.into();
        t.auction_category_a = a;
        d.templates.insert(id, t);
    }
    let mut names = super::items_data::ItemsData::default();
    names.default_item_names.insert(3, "Fresh Bread".into());
    names.item_name_cache = vec![(
        "en_us".into(),
        [
            (1i64, "iron sword".to_string()),
            (3, "fresh bread".to_string()),
        ]
        .into_iter()
        .collect(),
    )];
    assert_eq!(names.item_ids_by_search_name(&d, "iron"), vec![1, 2]);
    // The C# does not lower-case the keyword
    assert!(names.item_ids_by_search_name(&d, "Iron").is_empty());
    assert_eq!(names.search_string(&d, 3), Some("bread fresh bread"));
    let r: Vec<u32> = names
        .item_templates_for_auction_search(&d, "iron", 1, 0, 0)
        .iter()
        .map(|t| t.id)
        .collect();
    assert_eq!(r, vec![1]);
    let r: Vec<u32> = names
        .item_templates_for_auction_search(&d, "", 1, 0, 0)
        .iter()
        .map(|t| t.id)
        .collect();
    assert_eq!(r, vec![1, 3]);
    assert!(names.match_item_name(3, "Bread fre", false));
    assert!(names.match_item_name(3, "fresh bread", true));
    assert!(!names.match_item_name(3, "fresh", true));
    assert!(!names.match_item_name(2, "iron", false));
}

#[test]
fn fish_size_and_big_fish() {
    let mut names = super::items_data::ItemsData::default();
    names.fish_details.insert(
        40,
        super::items_data::FishDetails {
            id: 1,
            name: "Tuna".into(),
            item_id: 40,
            min_weight: 10,
            max_weight: 20,
            min_length: 50,
            max_length: 100,
        },
    );
    assert_eq!(names.fish_size(40, 50), Some((50.0, 15.0)));
    let mut d = ItemData::default();
    d.templates.insert(41, ItemTemplate::plain(41, 1));
    let mut trophy = Item::create(5, d.template(41).unwrap(), 3, 0, 1, &d, 0);
    trophy.made_unit_id = 40;
    let mut rng = ScriptedRng::new(&[100], &[]);
    let fish = names.create_big_fish(&d, &trophy, 1, &mut rng, 77).unwrap();
    assert_eq!(fish.class, ItemClass::BigFish);
    assert_eq!(
        (fish.count, fish.create_time, fish.made_unit_id),
        (1, 77, 0)
    );
    // Next(50, 100) is clamped to 99 by the scripted rng
    assert_eq!(fish.details.fish_length, 99.0);
    let mut s = PacketStream::new();
    fish.write_details_to(&mut s);
    let mut want = PacketStream::new();
    want.write_f32(fish.details.fish_weight)
        .write_f32(99.0)
        .write_i64(77);
    assert_eq!(s.into_bytes(), want.into_bytes());
}

#[test]
fn grade_distribution_lookup() {
    let mut d = ItemData::default();
    d.grade_distributions.insert(
        2,
        GradeDistributions {
            id: 2,
            name: "a".into(),
            weights: [0; 12],
        },
    );
    assert!(d.grade_distributions(2).is_some());
    assert_eq!(d.socket_chance(0), 0);
    d.socket_chance.insert(1, 9000);
    assert_eq!(d.socket_chance(0), 9000);
}

// ---------------------------------------------------------------------------------------------
// Real compact DB
// ---------------------------------------------------------------------------------------------

#[test]
#[ignore = "needs AAEMU_COMPACT_DB"]
fn real_compact_db_items_and_loot() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let d = ItemData::load(&conn).unwrap();
    println!(
        "templates {} (unused {}), grades {}, holdables {}, wearables {}, kinds {}, slots {}, dyes {}, \
         modifiers {}, procs {}, equip sets {}, look converts {}/{}/{}, runes {}, backpacks {}, body parts {}, \
         enchant costs {}, supports {}, socket {}, cap scales {}, distributions {}, npc loot {}, fish {}, \
         doodads {}, unit modifiers {} items / {} rows, armor grade buffs {}, item sets {}",
        d.templates.len(),
        d.templates.values().filter(|t| t.name.starts_with("invalid_item_")).count(),
        d.full_grades.len(),
        d.holdables.len(),
        d.wearables.len(),
        d.wearable_kinds.len(),
        d.wearable_slots.len(),
        d.default_dye_ids.len(),
        d.modifiers.len(),
        d.item_procs.len(),
        d.equip_item_sets.len(),
        d.item_look_converts.len(),
        d.holdable_item_look_converts.len(),
        d.wearable_item_look_converts.len(),
        d.runes.len(),
        d.backpacks.len(),
        d.body_parts.len(),
        d.enchanting_costs.len(),
        d.enchanting_supports.len(),
        d.socket_chance.len(),
        d.item_cap_scales.len(),
        d.grade_distributions.len(),
        d.loot_pack_dropping_npc.len(),
        d.loot_pack_convert_fish.len(),
        d.item_doodads.len(),
        d.item_unit_modifiers.len(),
        d.item_unit_modifiers.values().map(Vec::len).sum::<usize>(),
        d.armor_grade_buffs.values().map(|m| m.len()).sum::<usize>(),
        d.item_sets.len(),
    );
    assert_eq!(d.templates.len(), 27036);
    assert_eq!(d.template_order.len(), 27036);
    assert_eq!(
        d.templates
            .values()
            .filter(|t| t.name.starts_with("invalid_item_"))
            .count(),
        5554
    );
    assert_eq!(d.full_grades.len(), 12);
    assert_eq!((d.max_grade_id, d.max_grade_value), (11, 11));
    assert_eq!(d.holdables.len(), 27);
    assert_eq!(d.wearables.len(), 70);
    assert_eq!(d.wearable_kinds.len(), 5);
    assert_eq!(d.wearable_slots.len(), 14);
    assert_eq!(d.default_dye_ids.len(), 37);
    assert_eq!(d.modifiers.len(), 79);
    assert_eq!(d.item_procs.len(), 99);
    assert_eq!(d.equip_item_sets.len(), 225);
    assert_eq!(
        d.equip_item_sets
            .values()
            .map(|s| s.bonuses.len())
            .sum::<usize>(),
        433
    );
    assert_eq!(d.enchanting_costs.len(), 20);
    assert_eq!(d.enchanting_supports.len(), 12);
    assert_eq!(d.socket_chance.len(), 8);
    assert_eq!(d.item_cap_scales.len(), 225);
    assert_eq!(d.grade_distributions.len(), 25);
    assert_eq!(d.loot_pack_dropping_npc.len(), 3988);
    assert_eq!(
        d.loot_pack_dropping_npc
            .values()
            .map(Vec::len)
            .sum::<usize>(),
        14977
    );
    assert_eq!(d.loot_pack_convert_fish.len(), 32);
    assert_eq!(d.item_doodads.len(), 335);
    assert_eq!(d.item_unit_modifiers.len(), 2358);
    assert_eq!(d.armor_grade_buffs.len(), 3);
    assert_eq!(d.item_sets.len(), 7);
    assert_eq!(
        d.item_sets.values().map(|s| s.items.len()).sum::<usize>(),
        13
    );
    assert_eq!(d.runes.len(), 280);
    assert_eq!(d.backpacks.len(), 466);

    // Config + spot values
    assert_eq!(d.config.death_durability_loss_ratio, 2);
    assert_eq!(d.config.stat_value_const, 110);
    let coins = d.template(ITEM_COINS).unwrap();
    assert_eq!(coins.max_count, i32::MAX);
    assert!(d.is_auto_equip_trade_pack(
        d.backpacks
            .keys()
            .copied()
            .find(|id| {
                d.template(*id).is_some_and(|t| {
                    t.bind_type & bind_type::BIND_ON_EQUIP != bind_type::BIND_ON_EQUIP
                })
            })
            .unwrap()
    ));
    // Armor 18605 (leather head, mod set 53 = dex 6 / sta 4, level 40, price 18480)
    let t = d.template(18605).unwrap();
    let e = *t.equip().unwrap();
    assert_eq!(
        (e.slot_type_id, e.coverage, e.mod_set_id, e.armor_type_id),
        (1, 15, 53, 2)
    );
    assert_eq!(d.max_durability(t, 0), Some(60));
    assert_eq!(d.max_durability(t, 2), Some(65));
    let mut armor = Item::create(1, t, 1, 2, 1, &d, 0);
    assert_eq!(armor.class, ItemClass::Armor);
    assert_eq!(armor.details.durability, 60); // constructor runs at grade 0
    assert_eq!(armor.max_durability(&d), 65);
    assert_eq!(armor.stat(&d, EquipStat::Dex), Some(35));
    assert_eq!(armor.stat(&d, EquipStat::Sta), Some(24));
    assert_eq!(armor.stat(&d, EquipStat::Str), Some(0));
    armor.details.durability = 10;
    assert_eq!(armor.repair_cost(&d), Some(5864));
    // Weapon 5832 (holdable 16, mod set 72 = dex only, level 36)
    let wt = d.template(5832).unwrap();
    assert_eq!(d.max_durability(wt, 0), Some(145));
    assert_eq!(d.max_durability(wt, 2), Some(155));
    let weapon = Item::create(2, wt, 1, 2, 1, &d, 0);
    assert_eq!(weapon.stat(&d, EquipStat::Dex), Some(56));
    assert!(weapon.dps(&d).is_some_and(|v| v > 0.0));
    assert!(d.holdable(16).unwrap().formula_dps.is_prepared());

    // The economy's own tables
    let econ = super::items_data::ItemsData::load(&conn, "en_us").unwrap();
    println!(
        "item grade buffs {} items, conversions: {} reagents, {} sets, {} products; fish {}; \
         item names {} (locales {})",
        econ.item_grade_buffs.len(),
        econ.conversion.reagents.len(),
        econ.conversion.conversions.len(),
        econ.conversion.products.len(),
        econ.fish_details.len(),
        econ.default_item_names.len(),
        econ.item_name_cache.len()
    );
    assert_eq!(econ.item_grade_buffs.len(), 268);
    assert_eq!(
        econ.item_grade_buffs
            .values()
            .map(|m| m.len())
            .sum::<usize>(),
        3084
    );
    assert_eq!(econ.conversion.reagents.len(), 1324);
    assert_eq!(econ.conversion.conversions.len(), 317);
    assert_eq!(econ.conversion.products.len(), 305);
    assert_eq!(econ.fish_details.len(), 34);
    assert_eq!(
        econ.default_item_names.get(&ITEM_COINS).map(String::as_str),
        Some("Coin")
    );
    assert!(econ.match_item_name(ITEM_COINS, "coin", true));
    assert!(econ
        .item_ids_by_search_name(&d, "coin")
        .contains(&ITEM_COINS));

    let loot = super::loot_data::LootData::load(&conn).unwrap();
    println!(
        "loot: {} packs, {} loots, {} groups, {} actability groups",
        loot.packs.len(),
        loot.loot_count,
        loot.group_count,
        loot.actability_group_count
    );
    assert_eq!(loot.packs.len(), 3325);
    assert_eq!(loot.loot_count, 13823);
    assert_eq!(loot.group_count, 1298);
    assert_eq!(loot.actability_group_count, 263);
    assert_eq!(
        loot.packs.values().map(|p| p.loots.len()).sum::<usize>(),
        13823
    );
    // Loot items without a template (C#: GenerateLoot aborts with a NullReferenceException in
    // RegisterItems when one drops); every distribution used exists
    let mut missing = std::collections::BTreeSet::new();
    for p in loot.packs.values() {
        for l in &p.loots {
            if d.template(l.item_id).is_none() {
                missing.insert(l.item_id);
            }
            assert!(l.drop_rate > 1);
        }
        for (_, g) in &p.groups {
            if g.item_grade_distribution_id > 0 {
                assert!(d
                    .grade_distributions(g.item_grade_distribution_id)
                    .is_some());
            }
        }
    }
    println!(
        "loot items without a template: {} {:?}",
        missing.len(),
        missing
    );
    assert_eq!(missing.len(), 24);
    // Roll every NPC pack once with the real data: no errors besides the C# quest-only case
    let mut rng = super::loot::SharedRandom;
    let mut rolled = 0;
    for p in loot.packs.values() {
        super::loot::generate_pack_v2(
            p,
            &d,
            super::loot::LootConfig::default(),
            1.0,
            1.0,
            None,
            0,
            None,
            &mut rng,
        )
        .unwrap();
        rolled += 1;
    }
    assert_eq!(rolled, 3325);
}

#[test]
fn item_proc_checks_mirror_the_csharp() {
    let mut d = ItemData::default();
    d.item_procs.insert(
        4,
        crate::data::items::ItemProcTemplate {
            id: 4,
            skill_id: 900,
            chance_rate: 30,
            cooldown_sec: 10,
            ..Default::default()
        },
    );
    let p = crate::models::item::ItemProc::new(4);
    assert_eq!(p.apply(&d, 0, false, || 30), Some(900));
    assert_eq!(p.apply(&d, 0, false, || 31), None);
    // ignoreRoll never procs (C# condition is inverted)
    assert_eq!(p.apply(&d, 0, true, || 0), None);
    let cooling = crate::models::item::ItemProc {
        last_proc_ms: 5_000,
        ..p
    };
    assert_eq!(cooling.apply(&d, 14_999, false, || 0), None);
    assert_eq!(cooling.apply(&d, 15_000, false, || 0), Some(900));
    assert_eq!(
        crate::models::item::ItemProc::new(5).apply(&d, 0, false, || 0),
        None
    );
}
