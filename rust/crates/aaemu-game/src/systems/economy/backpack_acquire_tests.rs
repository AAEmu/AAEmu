//! Backpack acquisition boundaries shared by GM commands, crafting and NPC loot.

use std::sync::Arc;

use aaemu_commons::PacketStream;

use crate::data::items::{backpack_type, ItemTemplate, LootPackDroppingNpc, TemplateKind};
use crate::models::container::{equip_slot, slot_type};
use crate::models::item_task::task_type;
use crate::models::npc::Npc;
use crate::opcodes::sc;
use crate::systems::economy::loot::ScriptedRng;
use crate::systems::economy::loot_data::{Loot, LootPack};
use crate::systems::economy::looting::{CSLootOpenBagPacket, LootManager};
use crate::systems::economy::tests::{give, run, test_data, World, SINGLE};
use crate::systems::services::commands::output::{CharacterMessageOutput, PlayerCharacter};
use crate::systems::services::commands::scripts::sub::items::ItemAddSubCommand;
use crate::systems::services::commands::subcommand::pre_execute;
use crate::systems::services::commands::CommandContext;
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::{Entity, WorldObject};

use super::{try_add_new_item, Inv, ItemTxn};

const FISH: u32 = 27602;
const GLIDER: u32 = 99991;
const NPC: u32 = 0x200;

fn world(loot_count: i32) -> World {
    let mut data = test_data();
    for (id, kind) in [(FISH, 6), (GLIDER, backpack_type::GLIDER)] {
        data.items.templates.insert(
            id,
            ItemTemplate {
                kind: TemplateKind::Backpack {
                    backpack_type: kind,
                },
                ..ItemTemplate::plain(id, 1)
            },
        );
    }
    data.items.loot_pack_dropping_npc.insert(
        12835,
        vec![LootPackDroppingNpc {
            id: 1,
            npc_id: 12835,
            loot_pack_id: 77,
            default_pack: false,
        }],
    );
    let entry = Loot {
        id: 1,
        item_id: FISH,
        drop_rate: 10_000_000,
        min_amount: loot_count,
        max_amount: loot_count,
        loot_pack_id: 77,
        ..Default::default()
    };
    data.systems.economy.loot.packs.insert(
        77,
        LootPack {
            id: 77,
            loots: vec![entry],
            loots_by_group_no: vec![(0, vec![entry])],
            ..Default::default()
        },
    );
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

fn acquire(w: &World, count: i32) -> bool {
    let mut txn = ItemTxn::begin(&w.server);
    try_add_new_item(&mut txn, &Inv::of(&w.pa), task_type::GM, FISH, count, 0, 0)
}

fn generate_fish_loot(w: &mut World) {
    let mut npc = Npc::new(12835);
    npc.unit.transform.set_xyz(102.0, 100.0, 100.0);
    let npc = Entity::new(NPC, npc);
    w.server
        .world
        .world_of(&WorldObject::from(&w.pa))
        .unwrap()
        .spawn(&npc, &w.server.view_ctx());
    let mut rng = ScriptedRng::default();
    LootManager::generate_loot_with(
        &w.server,
        &WorldObject::from(&npc),
        Some(&WorldObject::from(&w.pa)),
        &mut rng,
    );
    assert_eq!(w.server.systems.economy.loot.item_count(NPC), 1);
    w.a.drain();
    w.b.drain();
}

fn loot_all(w: &mut World) {
    let mut s = PacketStream::new();
    s.write_bc(NPC).write_bc(0).write_bool(true);
    run::<CSLootOpenBagPacket>(w, 0, s.as_bytes());
}

#[tokio::test]
async fn gm_backpack_quantity_two_is_rejected_without_partial_acquisition() {
    let mut w = world(1);
    let character = PlayerCharacter::arc(&w.pa);
    let ctx = CommandContext {
        server: Some(&w.server),
        character: character.clone(),
    };
    let mut out = CharacterMessageOutput::new(character);
    pre_execute(
        &ItemAddSubCommand::default(),
        &ctx,
        "add",
        &["self".into(), FISH.to_string(), "2".into(), "0".into()],
        &mut out,
    );
    let inv = Inv::of(&w.pa);
    assert_eq!(inv.items_count(FISH, -1), 0);
    assert!(inv.equipped(equip_slot::BACKPACK).is_none());
    assert!(!w.a.drain_ops().contains(&sc::SCItemTaskSuccessPacket));
    pre_execute(
        &ItemAddSubCommand::default(),
        &ctx,
        "add",
        &["self".into(), FISH.to_string(), "1".into(), "0".into()],
        &mut out,
    );
    let pack = inv.equipped(equip_slot::BACKPACK).unwrap();
    assert_eq!((pack.template_id, pack.count, pack.slot), (FISH, 1, 26));
    assert_eq!(inv.items_count(FISH, -1), 1);
}

#[tokio::test]
async fn equipment_backpack_placement_uses_26_and_rejects_stacks_and_occupied_slot() {
    let w = world(1);
    let inv = Inv::of(&w.pa);
    let make = |count| {
        w.server
            .items
            .create_detached(FISH, count, 0, 1, &w.server.game_data.items, 0)
            .unwrap()
    };
    let equipment = inv.equipment();
    let mut eq = equipment.lock().unwrap();
    assert!(eq
        .add_new_item(make(2), equip_slot::BACKPACK, &w.server.game_data.items)
        .is_err());
    assert!(eq.items.is_empty());
    assert_eq!(
        eq.add_new_item(make(1), -1, &w.server.game_data.items),
        Ok(true)
    );
    let before = eq.items.clone();
    for slot in [-1, equip_slot::BACKPACK] {
        assert!(eq
            .add_new_item(make(1), slot, &w.server.game_data.items)
            .is_err());
        assert_eq!(eq.items, before);
    }
    let mut env = super::server_env(&w.server);
    assert!(!eq.acquire_default_item(task_type::GM, FISH, 1, 0, 0, &mut env));
    assert_eq!(eq.items, before);
    assert!(env.fx.is_empty());
}

#[tokio::test]
async fn normal_acquire_equips_empty_back_even_when_bag_is_full() {
    let mut w = world(1);
    let inv = Inv::of(&w.pa);
    inv.bag().lock().unwrap().set_container_size(1);
    let filler = give(&w, &w.pa, slot_type::INVENTORY, SINGLE, 1, 0);
    assert!(acquire(&w, 1));
    let pack = inv.equipped(equip_slot::BACKPACK).unwrap();
    assert_eq!((pack.template_id, pack.count), (FISH, 1));
    assert_eq!(inv.item_at(slot_type::INVENTORY, 0).unwrap().id, filler);
    w.a.drain();
    assert!(!acquire(&w, 1));
    assert_eq!(inv.equipped(equip_slot::BACKPACK).unwrap(), pack);
    assert_eq!(inv.items_count(FISH, -1), 1);
    assert!(w.a.drain().is_empty());
}

#[tokio::test]
async fn normal_acquire_validates_before_moving_glider_and_preserves_full_bag() {
    let mut w = world(1);
    let inv = Inv::of(&w.pa);
    let glider = give(
        &w,
        &w.pa,
        slot_type::EQUIPMENT,
        GLIDER,
        1,
        equip_slot::BACKPACK,
    );
    inv.bag().lock().unwrap().set_container_size(1);
    let filler = give(&w, &w.pa, slot_type::INVENTORY, SINGLE, 1, 0);
    for count in [0, 2, 1] {
        assert!(!acquire(&w, count));
        assert_eq!(inv.equipped(equip_slot::BACKPACK).unwrap().id, glider);
        assert_eq!(inv.item_at(slot_type::INVENTORY, 0).unwrap().id, filler);
        assert_eq!(inv.items_count(FISH, -1), 0);
        assert!(w.a.drain().is_empty());
    }
    inv.bag().lock().unwrap().set_container_size(2);
    assert!(!acquire(&w, 2));
    assert_eq!(inv.equipped(equip_slot::BACKPACK).unwrap().id, glider);
    assert!(acquire(&w, 1));
    assert_eq!(
        inv.equipped(equip_slot::BACKPACK).unwrap().template_id,
        FISH
    );
    assert_eq!(inv.item_at(slot_type::INVENTORY, 1).unwrap().id, glider);
    assert_eq!(inv.items_count(GLIDER, -1), 1);
}

#[tokio::test]
async fn bag_acquisition_of_gliders_still_creates_separate_items() {
    let w = world(1);
    let inv = Inv::of(&w.pa);
    let mut txn = ItemTxn::begin(&w.server);
    assert!(inv.bag().lock().unwrap().acquire_default_item(
        task_type::GM,
        GLIDER,
        2,
        0,
        0,
        &mut txn.env,
    ));
    assert_eq!(inv.items_count(GLIDER, -1), 2);
    assert_eq!(inv.item_at(slot_type::INVENTORY, 0).unwrap().count, 1);
    assert_eq!(inv.item_at(slot_type::INVENTORY, 1).unwrap().count, 1);
    assert!(inv.equipped(equip_slot::BACKPACK).is_none());
}

#[tokio::test]
async fn npc_fish_loot_equips_into_empty_back_with_full_bag() {
    let mut w = world(1);
    let inv = Inv::of(&w.pa);
    inv.bag().lock().unwrap().set_container_size(1);
    let filler = give(&w, &w.pa, slot_type::INVENTORY, SINGLE, 1, 0);
    generate_fish_loot(&mut w);
    loot_all(&mut w);
    assert_eq!(
        inv.equipped(equip_slot::BACKPACK).unwrap().template_id,
        FISH
    );
    assert_eq!(inv.item_at(slot_type::INVENTORY, 0).unwrap().id, filler);
    assert_eq!(w.server.systems.economy.loot.item_count(NPC), 0);
    let ops = w.a.drain_ops();
    assert!(ops.contains(&sc::SCLootItemTookPacket));
    assert!(!ops.contains(&sc::SCLootItemFailedPacket));
}

#[tokio::test]
async fn failed_npc_backpack_loot_is_retained_and_can_be_retried() {
    for count in [1, 2] {
        let mut w = world(count);
        let inv = Inv::of(&w.pa);
        let glider = give(
            &w,
            &w.pa,
            slot_type::EQUIPMENT,
            GLIDER,
            1,
            equip_slot::BACKPACK,
        );
        inv.bag().lock().unwrap().set_container_size(1);
        let filler = give(&w, &w.pa, slot_type::INVENTORY, SINGLE, 1, 0);
        generate_fish_loot(&mut w);
        let loot = w.server.systems.economy.loot.container(NPC);
        let original = loot.lock().unwrap().items[&1].item.clone();
        loot_all(&mut w);
        assert_eq!(loot.lock().unwrap().items[&1].item, original);
        assert_eq!(inv.equipped(equip_slot::BACKPACK).unwrap().id, glider);
        assert_eq!(inv.item_at(slot_type::INVENTORY, 0).unwrap().id, filler);
        assert_eq!(inv.items_count(FISH, -1), 0);
        assert!(w.a.drain_ops().contains(&sc::SCLootItemFailedPacket));
        inv.bag().lock().unwrap().set_container_size(2);
        if count == 2 {
            loot_all(&mut w);
            assert_eq!(loot.lock().unwrap().items[&1].item, original);
            assert_eq!(inv.equipped(equip_slot::BACKPACK).unwrap().id, glider);
            // Correct the malformed test loot, then retry the normal capture path.
            loot.lock().unwrap().items.get_mut(&1).unwrap().item.count = 1;
        }
        loot_all(&mut w);
        assert_eq!(
            inv.equipped(equip_slot::BACKPACK).unwrap().template_id,
            FISH
        );
        assert_eq!(inv.item_at(slot_type::INVENTORY, 1).unwrap().id, glider);
        assert_eq!(w.server.systems.economy.loot.item_count(NPC), 0);
        assert_eq!(inv.items_count(FISH, -1), 1);
    }
}
