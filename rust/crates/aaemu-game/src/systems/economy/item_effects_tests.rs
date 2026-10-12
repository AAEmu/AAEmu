//! Flow tests of the item skill effects and the mate gear packets (no MySQL).

use std::sync::{Arc, Mutex};

use aaemu_commons::{Marshal, PacketStream};

use crate::data::formulas::FormulaManager;
use crate::data::items::{
    EquipTemplate, FullGradeTemplate, ItemCapScale, ItemGradeEnchantingSupport, ItemTemplate,
    TemplateKind, ITEM_CREST_STAMP, ITEM_DAWN_STONE,
};
use crate::data::GameData;
use crate::managers::chat::chat_type;
use crate::models::container::{equip_slot, slot_type};
use crate::models::item::Item;
use crate::models::item_task::{task_type, ItemTask};
use crate::models::mate::Mate;
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::packets::world::{SCChatMessagePacket, SCErrorMsgPacket};
use crate::server::GameServer;
use crate::systems::economy::error_message;
use crate::systems::economy::inventory::Inv;
use crate::systems::economy::item_effect_packets::*;
use crate::systems::economy::item_effects::*;
use crate::systems::economy::item_packets::{
    SCItemTaskSuccessPacket, SCUnitEquipmentsChangedPacket,
};
use crate::systems::economy::items_data::{ItemConversionProduct, ItemConversionReagent};
use crate::systems::economy::loot::ScriptedRng;
use crate::systems::economy::mate_equipment::*;
use crate::systems::economy::tests::{give, item_at, test_data, World, STACK};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::{Entity, PlayerRef};

const ARMOR: u32 = 2100; // chest, no binding, level 10
const SCROLL: u32 = 5000;
const CHARM: u32 = 5100;
const GEM: u32 = 5200;
const POWDER: u32 = 5300;
const SLAVE_ITEM: u32 = 5400;
const BACKPACK: u32 = 5500;
const TL: u16 = 0x77;

fn formulas() -> FormulaManager {
    let conn = rusqlite::Connection::open_in_memory().unwrap();
    conn.execute_batch(
        "CREATE TABLE unit_formulas (id INTEGER, formula TEXT, kind_id INTEGER, owner_type_id INTEGER);
         CREATE TABLE unit_formula_variables (id INTEGER, unit_formula_id INTEGER, variable_kind_id INTEGER, key INTEGER, value REAL);
         CREATE TABLE wearable_formulas (id INTEGER, kind_id INTEGER, formula TEXT);
         CREATE TABLE formulas (id INTEGER, formula TEXT);
         INSERT INTO formulas VALUES (22, 'item_grade * 100 + item_level + equip_slot_enchant_cost');",
    )
    .unwrap();
    FormulaManager::load(&conn).unwrap()
}

fn data() -> GameData {
    let mut d = test_data();
    d.formulas = formulas();
    for g in 0..=12 {
        d.items.insert_grade(FullGradeTemplate {
            grade: g,
            grade_order: g,
            durability: 1.0,
            enchant_success_ratio: 5000,
            enchant_great_success_ratio: 3000,
            enchant_break_ratio: 1000,
            enchant_downgrade_ratio: 2000,
            enchant_downgrade_min: 1,
            enchant_downgrade_max: 3,
            enchant_cost: g + 1,
            ..FullGradeTemplate::default()
        });
    }
    let mut armor = ItemTemplate::new(
        ARMOR,
        TemplateKind::Armor(EquipTemplate {
            slot_type_id: 3,
            coverage: 10,
            durability_ratio: 1.0,
            durability_multiplier: 100,
            ..EquipTemplate::default()
        }),
    );
    armor.max_count = 1;
    armor.level = 10;
    armor.disenchantable = true;
    armor.impl_id = 3;
    d.items.templates.insert(ARMOR, armor);
    for (id, max) in [
        (SCROLL, 10),
        (CHARM, 10),
        (GEM, 10),
        (POWDER, 10),
        (ITEM_DAWN_STONE, 10),
    ] {
        d.items.templates.insert(id, ItemTemplate::plain(id, max));
    }
    d.items.templates.insert(
        ITEM_CREST_STAMP,
        ItemTemplate {
            max_count: 1,
            ..ItemTemplate::new(ITEM_CREST_STAMP, TemplateKind::Ucc)
        },
    );
    d.items.templates.insert(
        SLAVE_ITEM,
        ItemTemplate {
            max_count: 1,
            ..ItemTemplate::new(SLAVE_ITEM, TemplateKind::SummonSlave { slave_id: 3 })
        },
    );
    d.items.templates.insert(
        BACKPACK,
        ItemTemplate {
            max_count: 1,
            ..ItemTemplate::new(
                BACKPACK,
                TemplateKind::Backpack {
                    backpack_type: crate::data::items::backpack_type::GLIDER,
                },
            )
        },
    );
    d.items.enchanting_costs.insert(
        3,
        crate::data::items::EquipSlotEnchantingCost {
            id: 1,
            slot_type_id: 3,
            cost: 5,
        },
    );
    d.items.enchanting_supports.insert(
        CHARM,
        ItemGradeEnchantingSupport {
            id: 1,
            item_id: CHARM,
            require_grade_min: -1,
            require_grade_max: 6,
            add_great_success_grade: 6,
            ..ItemGradeEnchantingSupport::default()
        },
    );
    d.items.socket_chance.insert(1, 10000);
    d.items.socket_chance.insert(2, 0);
    d.items.item_cap_scales.insert(
        900,
        ItemCapScale {
            id: 1,
            skill_id: 900,
            scale_min: 0,
            scale_max: 100,
        },
    );
    let conv = &mut d.systems.economy.items.conversion;
    conv.reagents.push(ItemConversionReagent {
        conversion_id: 8,
        impl_id: 3,
        min_level: 1,
        max_level: 20,
        min_item_grade: 0,
        max_item_grade: 5,
        ..ItemConversionReagent::default()
    });
    conv.products.push(ItemConversionProduct {
        conversion_id: 8,
        chance_rate: 5000,
        output_item_id: STACK,
        weight: 1,
        min_output: 2,
        max_output: 4,
    });
    d.systems
        .economy
        .item_effects
        .repairable_slaves
        .insert(3, 42);
    d
}

/// Two players next to each other (char ids 1 / 2), their containers registered in the item
/// manager (`ItemManager.GetItemByItemId` lookups).
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
    for p in [&pa, &pb] {
        for (_, c) in Inv::of(p).containers {
            server.items.add_container(c);
        }
    }
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

fn body(p: &impl Marshal) -> Vec<u8> {
    let mut s = PacketStream::new();
    p.write(&mut s);
    s.into_bytes()
}

fn tasks(action: u8, t: Vec<ItemTask>) -> Vec<u8> {
    body(&SCItemTaskSuccessPacket {
        action,
        tasks: t,
        force_remove: Vec::new(),
    })
}

fn error(e: i16) -> (u16, Vec<u8>) {
    (
        sc::SCErrorMsgPacket,
        body(&SCErrorMsgPacket::new(e, 0, true)),
    )
}

fn item(w: &World, id: u64) -> Option<Item> {
    Inv::of(&w.pa).item_by_id(id)
}

fn set_grade(w: &World, id: u64, grade: u8) {
    let c = Inv::of(&w.pa).holder_of(id).unwrap();
    c.lock().unwrap().get_item_by_id_mut(id).unwrap().grade = grade;
}

fn scroll() -> Option<SkillItem> {
    Some(SkillItem {
        item_id: 0,
        item_template_id: SCROLL,
    })
}

// ---------------------------------------------------------------------------------------------
// GradeEnchant
// ---------------------------------------------------------------------------------------------

fn enchant(w: &World, id: u64, support: Option<u64>, lucky: bool, rolls: &[i32]) -> ScriptedRng {
    let mut rng = ScriptedRng::new(rolls, &[]);
    grade_enchant(
        &w.server,
        &w.pa,
        scroll(),
        Some(id),
        support,
        [i32::from(lucky), 0, 2, 0],
        TL,
        &mut rng,
    );
    rng
}

#[tokio::test]
async fn grade_enchant_success() {
    let mut w = world();
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, ARMOR, 1, 3);
    give(&w, &w.pa.clone(), slot_type::INVENTORY, SCROLL, 1, 4);
    set_grade(&w, id, 2);
    w.pa.data().money = 1000;
    let rng = enchant(&w, id, None, false, &[4999, 9999, 9999, 9999]);
    assert_eq!(rng.int_calls, vec![(0, 10000); 4]);
    let after = item(&w, id).unwrap();
    assert_eq!(after.grade, 3);
    assert!(after.dirty);
    // cost = item_grade(enchant_cost 3) * 100 + level 10 + slot cost 5
    assert_eq!(w.pa.data().money, 1000 - 315);
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                tasks(
                    task_type::GRADE_ENCHANT,
                    vec![ItemTask::grade_change(&after, 3)]
                )
            ),
            (
                sc::SCItemTaskSuccessPacket,
                tasks(task_type::DEPOSIT_MONEY, vec![ItemTask::MoneyChange(-315)])
            ),
            (
                sc::SCGradeEnchantResultPacket,
                body(&SCGradeEnchantResultPacket {
                    result: grade_enchant_result::SUCCESS,
                    item: after.clone(),
                    type1: 2,
                    type2: 3,
                })
            ),
            (sc::SCSkillEndedPacket, body(&SCSkillEndedPacket { tl: TL })),
        ]
    );
    // Neighbours see the skill end; grade 3 is not announced.
    assert_eq!(w.b.drain_ops(), vec![sc::SCSkillEndedPacket]);
}

#[tokio::test]
async fn grade_enchant_great_success_with_charm_is_announced() {
    let mut w = world();
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, ARMOR, 1, 0);
    give(&w, &w.pa.clone(), slot_type::INVENTORY, SCROLL, 1, 1);
    let charm = give(&w, &w.pa.clone(), slot_type::INVENTORY, CHARM, 2, 2);
    set_grade(&w, id, 1);
    w.pa.data().money = 1000;
    // lucky: success + great success (+2 + charm's 6 grades)
    enchant(&w, id, Some(charm), true, &[0, 0, 0, 0]);
    let after = item(&w, id).unwrap();
    assert_eq!(after.grade, 9);
    let charm_after = item(&w, charm).unwrap();
    assert_eq!(charm_after.count, 1);
    let result = SCGradeEnchantResultPacket {
        result: grade_enchant_result::GREAT_SUCCESS,
        item: after.clone(),
        type1: 1,
        type2: 9,
    };
    let announce = SCGradeEnchantBroadcastPacket {
        char_name: "Alice".into(),
        result: grade_enchant_result::GREAT_SUCCESS,
        item: after.clone(),
        type1: 1,
        type2: 9,
    };
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                tasks(
                    task_type::GRADE_ENCHANT,
                    vec![ItemTask::grade_change(&after, 9)]
                )
            ),
            (
                sc::SCItemTaskSuccessPacket,
                tasks(task_type::DEPOSIT_MONEY, vec![ItemTask::MoneyChange(-215)])
            ),
            (
                sc::SCItemTaskSuccessPacket,
                tasks(
                    task_type::GRADE_ENCHANT,
                    vec![ItemTask::count_update(&charm_after, -1)]
                )
            ),
            (sc::SCGradeEnchantResultPacket, body(&result)),
            (sc::SCSkillEndedPacket, body(&SCSkillEndedPacket { tl: TL })),
            (sc::SCGradeEnchantBroadcastPacket, body(&announce)),
        ]
    );
    assert_eq!(
        w.b.drain(),
        vec![
            (sc::SCSkillEndedPacket, body(&SCSkillEndedPacket { tl: TL })),
            (sc::SCGradeEnchantBroadcastPacket, body(&announce)),
        ]
    );
}

#[tokio::test]
async fn grade_enchant_break_removes_the_item() {
    let mut w = world();
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, ARMOR, 1, 6);
    give(&w, &w.pa.clone(), slot_type::INVENTORY, SCROLL, 1, 1);
    w.pa.data().money = 1000;
    let before = item(&w, id).unwrap();
    enchant(&w, id, None, false, &[9999, 999, 0, 0]);
    assert!(item(&w, id).is_none());
    assert_eq!(w.pa.data().money, 1000 - 115);
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                tasks(
                    task_type::GRADE_ENCHANT,
                    vec![ItemTask::remove_slot(&before)]
                )
            ),
            (
                sc::SCItemTaskSuccessPacket,
                tasks(task_type::DEPOSIT_MONEY, vec![ItemTask::MoneyChange(-115)])
            ),
            (
                sc::SCGradeEnchantResultPacket,
                body(&SCGradeEnchantResultPacket {
                    result: grade_enchant_result::BREAK,
                    item: before.clone(),
                    type1: 0,
                    type2: 0,
                })
            ),
            (sc::SCSkillEndedPacket, body(&SCSkillEndedPacket { tl: TL })),
        ]
    );
}

#[tokio::test]
async fn grade_enchant_downgrade_and_fail() {
    let mut w = world();
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, ARMOR, 1, 0);
    give(&w, &w.pa.clone(), slot_type::INVENTORY, SCROLL, 2, 1);
    set_grade(&w, id, 4);
    w.pa.data().money = 10_000;
    // Downgrade: Random.Next(1, 3) -> 2
    let rng = enchant(&w, id, None, false, &[9999, 9999, 1999, 9999, 2]);
    assert_eq!(rng.int_calls.last(), Some(&(1, 3)));
    let after = item(&w, id).unwrap();
    assert_eq!(after.grade, 2);
    let f = w.a.drain();
    assert_eq!(
        f[0].1,
        tasks(
            task_type::GRADE_ENCHANT,
            vec![ItemTask::grade_change(&after, 2)]
        )
    );
    assert_eq!(
        f[2].1,
        body(&SCGradeEnchantResultPacket {
            result: grade_enchant_result::DOWNGRADE,
            item: after.clone(),
            type1: 4,
            type2: 2,
        })
    );

    // Fail: nothing changes but money; the grade change task is still sent.
    enchant(&w, id, None, false, &[9999, 9999, 9999, 9999]);
    assert_eq!(item(&w, id).unwrap().grade, 2);
    let f = w.a.drain();
    assert_eq!(f.len(), 4);
    assert_eq!(
        f[2].1,
        body(&SCGradeEnchantResultPacket {
            result: grade_enchant_result::FAIL,
            item: after.clone(),
            type1: 2,
            type2: 2,
        })
    );
}

#[tokio::test]
async fn grade_enchant_refusals() {
    let mut w = world();
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, ARMOR, 1, 0);
    // No money
    w.pa.data().money = 10;
    enchant(&w, id, None, false, &[0, 0, 0, 0]);
    assert_eq!(w.a.drain(), vec![error(error_message::NOT_ENOUGH_MONEY)]);
    // No scroll
    w.pa.data().money = 1000;
    enchant(&w, id, None, false, &[0, 0, 0, 0]);
    assert_eq!(
        w.a.drain(),
        vec![error(error_message::NOT_ENOUGH_REQUIRED_ITEM)]
    );
    // Charm above its max grade
    give(&w, &w.pa.clone(), slot_type::INVENTORY, SCROLL, 1, 1);
    let charm = give(&w, &w.pa.clone(), slot_type::INVENTORY, CHARM, 1, 2);
    set_grade(&w, id, 7);
    enchant(&w, id, Some(charm), false, &[0, 0, 0, 0]);
    assert_eq!(w.a.drain(), vec![error(error_message::GRADE_ENCHANT_MAX)]);
    // Wrong item kind for value3 (2 = armor; a stack is no armor): InvalidCast -> nothing
    let stack = give(&w, &w.pa.clone(), slot_type::INVENTORY, STACK, 1, 3);
    enchant(&w, stack, None, false, &[0, 0, 0, 0]);
    assert!(w.a.drain().is_empty());
    assert_eq!(w.pa.data().money, 1000);
    assert!(w.b.drain().is_empty());
}

#[test]
fn charm_chance_math() {
    assert_eq!(charm_chance(5000, 100, 1000), 5000 + 100 + 50000);
    assert_eq!(charm_chance(333, 0, 75), 333 + 249); // (int)(333 * 0.75) = 249
    assert_eq!(charm_chance(1000, 0, -100), 0);
}

// ---------------------------------------------------------------------------------------------
// Socketing / tempering / looks / conversion
// ---------------------------------------------------------------------------------------------

#[tokio::test]
async fn socketing_success_failure_and_dawnstone() {
    let mut w = world();
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, ARMOR, 1, 0);
    let gem = give(&w, &w.pa.clone(), slot_type::INVENTORY, GEM, 3, 1);
    let dawn = give(
        &w,
        &w.pa.clone(),
        slot_type::INVENTORY,
        ITEM_DAWN_STONE,
        1,
        2,
    );
    let gem_caster = Some(SkillItem {
        item_id: gem,
        item_template_id: GEM,
    });
    // 1st socket: chance 10000
    let mut rng = ScriptedRng::new(&[9999], &[]);
    item_socketing(&w.server, &w.pa, gem_caster, Some(id), [0; 4], TL, &mut rng);
    let after = item(&w, id).unwrap();
    assert_eq!(after.details.gem_ids, [GEM, 0, 0, 0, 0, 0, 0]);
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                tasks(task_type::SOCKETING, vec![ItemTask::update(&after)])
            ),
            (
                sc::SCItemSocketingLunagemResultPacket,
                body(&SCItemSocketingLunagemResultPacket {
                    result: 1,
                    item_id: id,
                    template_id: GEM,
                    install: true,
                })
            ),
            (sc::SCSkillEndedPacket, body(&SCSkillEndedPacket { tl: TL })),
        ]
    );
    assert_eq!(w.b.drain_ops(), vec![sc::SCSkillEndedPacket]);

    // 2nd socket: chance 0 -> every gem is lost
    let mut rng = ScriptedRng::new(&[0], &[]);
    item_socketing(&w.server, &w.pa, gem_caster, Some(id), [0; 4], TL, &mut rng);
    let after = item(&w, id).unwrap();
    assert_eq!(after.details.gem_ids, [0; 7]);
    let f = w.a.drain();
    assert_eq!(
        f[1].1,
        body(&SCItemSocketingLunagemResultPacket {
            result: 0,
            item_id: id,
            template_id: GEM,
            install: true,
        })
    );

    // Dawnstone: clears, result 1, not installed, no roll
    {
        let c = Inv::of(&w.pa).bag();
        c.lock()
            .unwrap()
            .get_item_by_id_mut(id)
            .unwrap()
            .details
            .gem_ids[3] = 77;
    }
    let mut rng = ScriptedRng::new(&[], &[]);
    let dawn_caster = Some(SkillItem {
        item_id: dawn,
        item_template_id: ITEM_DAWN_STONE,
    });
    item_socketing(
        &w.server,
        &w.pa,
        dawn_caster,
        Some(id),
        [0; 4],
        TL,
        &mut rng,
    );
    assert!(rng.int_calls.is_empty());
    assert_eq!(item(&w, id).unwrap().details.gem_ids, [0; 7]);
    let f = w.a.drain();
    assert_eq!(
        f[1].1,
        body(&SCItemSocketingLunagemResultPacket {
            result: 1,
            item_id: id,
            template_id: ITEM_DAWN_STONE,
            install: false,
        })
    );
}

#[tokio::test]
async fn lunastone_cap_scale_and_reset() {
    let mut w = world();
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, ARMOR, 1, 0);
    let caster = Some(SkillItem {
        item_id: 1,
        item_template_id: GEM,
    });
    magical_enchant(&w.server, &w.pa, caster, Some(id));
    let after = item(&w, id).unwrap();
    assert_eq!(after.details.rune_id, GEM);
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemSocketingLunastoneResultPacket,
                body(&SCItemSocketingLunastoneResultPacket {
                    result: true,
                    item_id: id,
                    template_id: GEM,
                })
            ),
            (
                sc::SCItemTaskSuccessPacket,
                tasks(task_type::ENCHANT_MAGICAL, vec![ItemTask::update(&after)])
            ),
        ]
    );

    let mut rng = ScriptedRng::new(&[55, 77], &[]);
    item_cap_scale(&w.server, &w.pa, caster, Some(id), 900, [0; 4], &mut rng);
    assert_eq!(rng.int_calls, vec![(0, 100), (0, 100)]);
    let after = item(&w, id).unwrap();
    assert_eq!(
        (after.details.temper_physical, after.details.temper_magical),
        (55, 77)
    );
    let msg = |m: &str| SCChatMessagePacket {
        chat_type: chat_type::SYSTEM,
        sender: None,
        message: m.into(),
        ability: 0,
        language_type: 0,
    };
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                tasks(task_type::ENCHANT_PHYSICAL, vec![ItemTask::update(&after)])
            ),
            (
                sc::SCChatMessagePacket,
                body(&msg(
                    "Temper:\n |cFFFFFFFF55%|r Physical\n|cFFFFFFFF77%|r Magical"
                ))
            ),
        ]
    );

    item_cap_scale_reset(&w.server, &w.pa, caster, Some(id), [0; 4]);
    let after = item(&w, id).unwrap();
    assert_eq!(after.details.temper_physical, 0);
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                tasks(task_type::ENCHANT_PHYSICAL, vec![ItemTask::update(&after)])
            ),
            (sc::SCChatMessagePacket, body(&msg("Tempering reset"))),
        ]
    );
}

#[tokio::test]
async fn cleanup_look_convert_flow() {
    let mut w = world();
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, ARMOR, 1, 0);
    let powder = give(&w, &w.pa.clone(), slot_type::INVENTORY, POWDER, 2, 1);
    let caster = Some(SkillItem {
        item_id: powder,
        item_template_id: POWDER,
    });
    // No image yet
    assert_eq!(
        cleanup_look_convert(&w.server, &w.pa, caster, Some(id), [0; 4]),
        Outcome::Cancelled
    );
    assert_eq!(
        w.a.drain(),
        vec![error(
            error_message::ITEM_LOOK_CONVERT_AS_INVALID_COMBINATION
        )]
    );
    Inv::of(&w.pa)
        .bag()
        .lock()
        .unwrap()
        .get_item_by_id_mut(id)
        .unwrap()
        .details
        .image_item_template_id = 999;
    assert_eq!(
        cleanup_look_convert(&w.server, &w.pa, caster, Some(id), [0; 4]),
        Outcome::Done
    );
    let after = item(&w, id).unwrap();
    assert_eq!(after.details.image_item_template_id, 0);
    let p = item(&w, powder).unwrap();
    assert_eq!(p.count, 1);
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                tasks(task_type::CONVERT_ITEM_LOOK, vec![ItemTask::update(&after)])
            ),
            (
                sc::SCItemTaskSuccessPacket,
                tasks(
                    task_type::CONVERT_ITEM_LOOK,
                    vec![ItemTask::count_update(&p, -1)]
                )
            ),
        ]
    );
}

#[tokio::test]
async fn item_conversion_flow() {
    let mut w = world();
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, ARMOR, 1, 0);
    let before = item(&w, id).unwrap();
    // Product roll 4999 < 5000, amount Random.Next(2, 5) -> 3
    let mut rng = ScriptedRng::new(&[4999, 3], &[]);
    assert_eq!(
        item_conversion(&w.server, &w.pa, Some(id), [0; 4], &mut rng),
        Outcome::Done
    );
    assert_eq!(rng.int_calls, vec![(0, 10000), (2, 5)]);
    assert!(item(&w, id).is_none());
    let product = item_at(&w.pa, slot_type::INVENTORY, 1).unwrap();
    assert_eq!((product.template_id, product.count), (STACK, 3));
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemTaskSuccessPacket,
                tasks(task_type::CONVERSION, vec![ItemTask::add(&product)])
            ),
            (
                sc::SCItemTaskSuccessPacket,
                tasks(task_type::CONVERSION, vec![ItemTask::remove_slot(&before)])
            ),
            // ConsumeItem's own (empty) task list
            (
                sc::SCItemTaskSuccessPacket,
                tasks(task_type::CONVERSION, vec![])
            ),
        ]
    );

    // Failed product roll: the item is still consumed
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, ARMOR, 1, 5);
    let mut rng = ScriptedRng::new(&[5000], &[]);
    assert_eq!(
        item_conversion(&w.server, &w.pa, Some(id), [0; 4], &mut rng),
        Outcome::Done
    );
    assert!(item(&w, id).is_none());
    assert_eq!(
        w.a.drain_ops(),
        vec![sc::SCItemTaskSuccessPacket, sc::SCItemTaskSuccessPacket]
    );

    // Not in the bag / not a SkillCastItemTarget: cancelled
    assert_eq!(
        item_conversion(&w.server, &w.pa, Some(12345), [0; 4], &mut rng),
        Outcome::Cancelled
    );
    assert_eq!(
        item_conversion(&w.server, &w.pa, None, [0; 4], &mut rng),
        Outcome::Cancelled
    );
}

#[tokio::test]
async fn gain_item_effect() {
    let mut w = world();
    gain_item(&w.server, &w.pa, [STACK as i32, 0, 0, 0]);
    let i = item_at(&w.pa, slot_type::INVENTORY, 0).unwrap();
    assert_eq!((i.template_id, i.count), (STACK, 1));
    assert_eq!(
        w.a.drain(),
        vec![(
            sc::SCItemTaskSuccessPacket,
            tasks(task_type::SKILL_EFFECT_GAIN_ITEM, vec![ItemTask::add(&i)])
        )]
    );
    gain_item(&w.server, &w.pa, [0, 5, 0, 0]);
    assert!(w.a.drain().is_empty());
}

// ---------------------------------------------------------------------------------------------
// UCC
// ---------------------------------------------------------------------------------------------

fn ucc_world() -> World {
    use crate::systems::skills::data::{EffectKind, EffectTemplate};
    let mut d = data();
    // Authentic 1.2 Crest Ink / Crest Bleach are use-skill reagents. Their effect rows
    // consume_source_item=false; the skill still debits one source after successful apply.
    for (item_id, skill_id) in [(17662, 14433), (19780, 16046)] {
        let mut item = ItemTemplate::plain(item_id, 10);
        item.use_skill_id = skill_id;
        item.use_skill_as_reagent = true;
        d.items.templates.insert(item_id, item);
    }
    d.systems.skills.add_effect(
        EffectKind::ImprintUccEffect,
        EffectTemplate::ImprintUcc { id: 5, item_id: 0 },
        0,
    );
    d.systems.skills.add_effect(
        EffectKind::CleanupUccEffect,
        EffectTemplate::CleanupUcc { id: 1 },
        0,
    );
    let w = world_with_data(d);
    super::bridges::install(&w.server);
    w
}

fn ucc_source_template(cleanup: bool) -> u32 {
    if cleanup {
        19780
    } else {
        17662
    }
}

// UCC and ownership are not both included in every item class's wire details.
fn ucc_item_state(w: &World, id: u64) -> (Vec<u8>, u64, u64, bool) {
    // Unlike the older single-owner `item` helper, this observation must cover Bob's
    // registered containers too, before and after the attempted foreign-item cast.
    let item = w.server.items.item_by_id(id).expect("UCC fixture item");
    (body(&item), item.ucc_id, item.owner_id, item.dirty)
}

/// Exercise the real effect dispatch followed by skill-owned source consumption, rather
/// than directly calling an effect and simulating its debit in the test.
fn cast_ucc(
    w: &World,
    cleanup: bool,
    source: SkillItem,
    target_id: u64,
    target_player: &PlayerRef,
) -> crate::systems::skills::state::SkillRef {
    use crate::systems::skills::data::{EffectKind, SkillEffect, SkillTemplate};
    use crate::systems::skills::geometry::Target;
    use crate::systems::skills::skill::{apply_effects, CastCtx};
    use crate::systems::skills::state::SkillRun;
    use crate::systems::skills::types::{SkillCastTarget, SkillCaster, SkillObject};
    use crate::world::WorldObject;

    let (skill_id, kind, effect_id) = if cleanup {
        (16046, EffectKind::CleanupUccEffect, 1)
    } else {
        (14433, EffectKind::ImprintUccEffect, 5)
    };
    let effect = SkillEffect {
        effect_id,
        template: Some((kind, effect_id)),
        weight: 0,
        start_level: 1,
        end_level: 99,
        friendly: true,
        non_friendly: true,
        target_buff_tag_id: 0,
        target_no_buff_tag_id: 0,
        source_buff_tag_id: 0,
        source_no_buff_tag_id: 0,
        chance: 100,
        front: true,
        back: true,
        target_npc_tag_id: 0,
        application_method: 1,
        consume_source_item: false,
        consume_item_id: 0,
        consume_item_count: 1,
        always_hit: false,
        item_set_id: 0,
        interaction_success_hit: false,
    };
    let skill = SkillRun::new(
        Arc::new(SkillTemplate {
            id: skill_id,
            target_type: 9,
            target_selection: 2,
            effects: vec![effect],
            ..SkillTemplate::default()
        }),
        1,
    );
    apply_effects(
        &w.server,
        &skill,
        &CastCtx {
            caster: WorldObject::from(&w.pa),
            caster_caster: SkillCaster::Item {
                obj_id: w.pa.obj_id,
                item_id: source.item_id,
                item_template_id: source.item_template_id,
                type1: 0,
                type2: 0,
            },
            target: Target::Obj(WorldObject::from(target_player)),
            target_caster: SkillCastTarget::Item {
                obj_id: target_player.obj_id,
                id: target_id,
                type1: 0,
                type2: 0,
            },
            skill_object: SkillObject::None,
        },
    );
    skill
}

#[tokio::test]
async fn ucc_owned_dispatch_consumes_one_source_after_imprint_or_cleanup() {
    for cleanup in [false, true] {
        let mut w = ucc_world();
        let source_id = give(
            &w,
            &w.pa,
            slot_type::INVENTORY,
            ucc_source_template(cleanup),
            2,
            0,
        );
        let target_id = give(&w, &w.pa, slot_type::INVENTORY, ARMOR, 1, 1);
        {
            let bag = Inv::of(&w.pa).bag();
            let mut bag = bag.lock().unwrap();
            bag.get_item_by_id_mut(source_id).unwrap().set_ucc(0xABCD);
            bag.get_item_by_id_mut(target_id).unwrap().set_ucc(0x1234);
        }
        let skill = cast_ucc(
            &w,
            cleanup,
            SkillItem {
                item_id: source_id,
                item_template_id: ucc_source_template(cleanup),
            },
            target_id,
            &w.pa,
        );
        assert!(!skill.cancelled());
        assert_eq!(item(&w, source_id).unwrap().count, 1);
        assert_eq!(
            item(&w, target_id).unwrap().ucc_id,
            if cleanup { 0 } else { 0xABCD }
        );
        let ops = w.a.drain_ops();
        assert!(ops.contains(&sc::SCItemUccDataChangedPacket));
        assert!(ops.contains(&sc::SCItemTaskSuccessPacket));
        assert!(w.b.drain().is_empty());
    }
}

#[tokio::test]
async fn ucc_foreign_source_target_or_resolved_player_cancels_without_debit() {
    for cleanup in [false, true] {
        for case in [
            "foreign_source",
            "foreign_target",
            "foreign_player",
            "both_foreign",
        ] {
            let mut w = ucc_world();
            let source_owner = if matches!(case, "foreign_source" | "both_foreign") {
                &w.pb
            } else {
                &w.pa
            };
            let target_owner = if matches!(case, "foreign_target" | "both_foreign") {
                &w.pb
            } else {
                &w.pa
            };
            let source_id = give(
                &w,
                source_owner,
                slot_type::INVENTORY,
                ucc_source_template(cleanup),
                2,
                0,
            );
            let target_id = give(&w, target_owner, slot_type::INVENTORY, ARMOR, 1, 1);
            Inv::of(source_owner)
                .bag()
                .lock()
                .unwrap()
                .get_item_by_id_mut(source_id)
                .unwrap()
                .set_ucc(0xABCD);
            Inv::of(target_owner)
                .bag()
                .lock()
                .unwrap()
                .get_item_by_id_mut(target_id)
                .unwrap()
                .set_ucc(0x1234);
            let source_before = ucc_item_state(&w, source_id);
            let target_before = ucc_item_state(&w, target_id);
            let resolved_player = if matches!(case, "foreign_player" | "both_foreign") {
                &w.pb
            } else {
                &w.pa
            };
            let skill = cast_ucc(
                &w,
                cleanup,
                SkillItem {
                    item_id: source_id,
                    item_template_id: ucc_source_template(cleanup),
                },
                target_id,
                resolved_player,
            );
            assert!(skill.cancelled(), "{case}, cleanup={cleanup}");
            assert_eq!(ucc_item_state(&w, source_id), source_before, "{case}");
            assert_eq!(ucc_item_state(&w, target_id), target_before, "{case}");
            assert!(w.a.drain().is_empty(), "{case}");
            assert!(w.b.drain().is_empty(), "{case}");
        }
    }
}

#[tokio::test]
async fn ucc_invalid_source_count_template_or_same_item_cancels_without_mutation() {
    for cleanup in [false, true] {
        for case in [
            "empty_source",
            "forged_template",
            "same_item",
            "wrong_owner_metadata",
        ] {
            let mut w = ucc_world();
            let source_id = give(
                &w,
                &w.pa,
                slot_type::INVENTORY,
                ucc_source_template(cleanup),
                2,
                0,
            );
            let target_id = give(&w, &w.pa, slot_type::INVENTORY, ARMOR, 1, 1);
            {
                let bag = Inv::of(&w.pa).bag();
                let mut bag = bag.lock().unwrap();
                let source = bag.get_item_by_id_mut(source_id).unwrap();
                source.set_ucc(0xABCD);
                if case == "empty_source" {
                    source.count = 0;
                }
                if case == "wrong_owner_metadata" {
                    source.owner_id = u64::from(w.pb.character_id);
                }
                bag.get_item_by_id_mut(target_id).unwrap().set_ucc(0x1234);
            }
            let source_before = ucc_item_state(&w, source_id);
            let target_before = ucc_item_state(&w, target_id);
            let skill = cast_ucc(
                &w,
                cleanup,
                SkillItem {
                    item_id: source_id,
                    item_template_id: if case == "forged_template" {
                        POWDER
                    } else {
                        ucc_source_template(cleanup)
                    },
                },
                if case == "same_item" {
                    source_id
                } else {
                    target_id
                },
                &w.pa,
            );
            assert!(skill.cancelled(), "{case}, cleanup={cleanup}");
            assert_eq!(ucc_item_state(&w, source_id), source_before, "{case}");
            assert_eq!(ucc_item_state(&w, target_id), target_before, "{case}");
            assert!(w.a.drain().is_empty(), "{case}");
            assert!(w.b.drain().is_empty(), "{case}");
        }
    }
}

#[tokio::test]
async fn ucc_imprint_cleanup_and_stamp() {
    let mut w = world();
    let stamp = give(&w, &w.pa.clone(), slot_type::INVENTORY, STACK, 1, 0);
    let target = give(&w, &w.pa.clone(), slot_type::INVENTORY, ARMOR, 1, 1);
    Inv::of(&w.pa)
        .bag()
        .lock()
        .unwrap()
        .get_item_by_id_mut(stamp)
        .unwrap()
        .set_ucc(0xABCD);
    let caster = Some(SkillItem {
        item_id: stamp,
        item_template_id: STACK,
    });
    imprint_ucc(&w.server, Some(&w.pa), caster, Some(target));
    let after = item(&w, target).unwrap();
    assert_eq!(after.ucc_id, 0xABCD);
    assert_ne!(after.flags & crate::models::item::ITEM_FLAG_HAS_UCC, 0);
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemUccDataChangedPacket,
                body(&SCItemUccDataChangedPacket {
                    ucc_id: 0xABCD,
                    player_id: 1,
                    target_item_id: target,
                })
            ),
            (
                sc::SCItemTaskSuccessPacket,
                tasks(
                    task_type::GAIN_ITEM_WITH_UCC,
                    vec![ItemTask::update_bits(&after)]
                )
            ),
        ]
    );
    // The stamp is not consumed by the effect.
    assert!(item(&w, stamp).is_some());

    cleanup_ucc(&w.server, Some(&w.pa), caster, Some(target));
    let after = item(&w, target).unwrap();
    assert_eq!(
        (
            after.ucc_id,
            after.flags & crate::models::item::ITEM_FLAG_HAS_UCC
        ),
        (0, 0)
    );
    assert_eq!(
        w.a.drain(),
        vec![
            (
                sc::SCItemUccDataChangedPacket,
                body(&SCItemUccDataChangedPacket {
                    ucc_id: 0,
                    player_id: 1,
                    target_item_id: target,
                })
            ),
            (
                sc::SCItemTaskSuccessPacket,
                tasks(
                    task_type::GAIN_ITEM_WITH_UCC,
                    vec![ItemTask::update_bits(&after)]
                )
            ),
        ]
    );

    // Emblem imprint: a new crest stamp carrying the ink's UCC in the target's bag
    gain_item_with_emblem_imprint(&w.server, Some(&w.pb), caster, [0; 4]);
    let crest = item_at(&w.pb, slot_type::INVENTORY, 0).unwrap();
    assert_eq!(
        (crest.template_id, crest.ucc_id),
        (ITEM_CREST_STAMP, 0xABCD)
    );
    assert_eq!(
        w.b.drain(),
        vec![(
            sc::SCItemTaskSuccessPacket,
            tasks(task_type::GAIN_ITEM_WITH_UCC, vec![ItemTask::add(&crest)])
        )]
    );
}

// ---------------------------------------------------------------------------------------------
// Slaves / backpacks
// ---------------------------------------------------------------------------------------------

#[tokio::test]
async fn repair_slave_item_flow() {
    let mut w = world();
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, SLAVE_ITEM, 1, 0);
    Inv::of(&w.pa)
        .bag()
        .lock()
        .unwrap()
        .get_item_by_id_mut(id)
        .unwrap()
        .details
        .slave_is_destroyed = 1;
    // Wrong repair effect
    repair_slave_item(&w.server, 41, Some((id, w.pa.obj_id)));
    assert_eq!(w.a.drain(), vec![error(error_message::ITEM_FAILED_REPAIR)]);
    repair_slave_item(&w.server, 42, Some((id, w.pa.obj_id)));
    let after = item(&w, id).unwrap();
    assert_eq!(after.details.slave_is_destroyed, 0);
    assert!(after.dirty);
    assert_eq!(
        w.a.drain(),
        vec![(
            sc::SCItemTaskSuccessPacket,
            tasks(task_type::REPAIR_SLAVES, vec![ItemTask::update(&after)])
        )]
    );
}

#[derive(Default)]
struct RecordingHooks(Mutex<Vec<BackpackDoodadSpawn>>);

impl ItemEffectHooks for RecordingHooks {
    fn spawn_backpack_doodad(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        spawn: &BackpackDoodadSpawn,
    ) -> bool {
        self.0.lock().unwrap().push(*spawn);
        true
    }
}

#[tokio::test]
async fn put_down_backpack_drops_and_reequips_the_glider() {
    let mut w = world();
    let pack = give(
        &w,
        &w.pa.clone(),
        slot_type::EQUIPMENT,
        BACKPACK,
        1,
        equip_slot::BACKPACK,
    );
    let glider = give(&w, &w.pa.clone(), slot_type::INVENTORY, BACKPACK, 1, 4);
    w.server.systems.economy.set_previous_backpack(1, glider);
    let hooks = Arc::new(RecordingHooks::default());
    w.server
        .systems
        .economy
        .item_effects
        .hooks
        .install(hooks.clone());
    let caster = Some(SkillItem {
        item_id: pack,
        item_template_id: BACKPACK,
    });
    put_down_backpack(&w.server, &w.pa, 4321, caster);
    let spawned = hooks.0.lock().unwrap().clone();
    assert_eq!(spawned.len(), 1);
    assert_eq!(spawned[0].doodad_template_id, 4321);
    assert_eq!(spawned[0].item_id, pack);
    // yaw 0: one metre north
    assert_eq!((spawned[0].x, spawned[0].y), (100.0, 101.0));
    // The pack is in the system container, the glider is back on the back.
    let sys = Inv::of(&w.pa).get(slot_type::SYSTEM).unwrap();
    assert!(sys.lock().unwrap().get_item_by_id(pack).is_some());
    assert_eq!(
        Inv::of(&w.pa).equipped(equip_slot::BACKPACK).unwrap().id,
        glider
    );
    let ops = w.a.drain_ops();
    assert_eq!(ops[0], sc::SCItemTaskSuccessPacket); // DropBackpack
    assert_eq!(*ops.last().unwrap(), sc::SCItemTaskSuccessPacket); // SwapItems
                                                                   // Bob: the equipment leave event, the explicit broadcast, then the glider equip.
    assert_eq!(
        w.b.drain(),
        vec![
            (
                sc::SCUnitEquipmentsChangedPacket,
                body(&SCUnitEquipmentsChangedPacket {
                    obj_id: w.pa.obj_id,
                    items: vec![(equip_slot::BACKPACK as u8, None)],
                })
            ),
            (
                sc::SCUnitEquipmentsChangedPacket,
                body(&SCUnitEquipmentsChangedPacket {
                    obj_id: w.pa.obj_id,
                    items: vec![(equip_slot::BACKPACK as u8, None)],
                })
            ),
            (
                sc::SCUnitEquipmentsChangedPacket,
                body(&SCUnitEquipmentsChangedPacket {
                    obj_id: w.pa.obj_id,
                    items: vec![(
                        equip_slot::BACKPACK as u8,
                        Inv::of(&w.pa).equipped(equip_slot::BACKPACK)
                    )],
                })
            ),
        ]
    );
}

// ---------------------------------------------------------------------------------------------
// Mate gear
// ---------------------------------------------------------------------------------------------

fn spawn_mate(w: &mut World) -> crate::world::MateRef {
    install_default_summon_hooks(&w.server);
    let mut m = Mate::new(77, 500);
    m.unit.tl_id = TL;
    m.owner_obj_id = w.pa.obj_id;
    m.unit.transform.set_xyz(100.5, 100.0, 100.0);
    let mate = Entity::new(0x300, m);
    let world = w.server.world.world(0).unwrap();
    world.spawn(&mate, &w.server.view_ctx());
    w.a.drain();
    w.b.drain();
    mate
}

fn mate_body(player_item: Option<&Item>, mate_item: Option<&Item>, ps: u8, ms: u8) -> Vec<u8> {
    let mut s = PacketStream::new();
    s.write_u32(1)
        .write_u16(TL)
        .write_u32(0)
        .write_bool(false)
        .write_u8(1);
    match player_item {
        Some(i) => i.write(&mut s),
        None => {
            s.write_u32(0);
        }
    }
    match mate_item {
        Some(i) => i.write(&mut s),
        None => {
            s.write_u32(0);
        }
    }
    s.write_u8(slot_type::INVENTORY)
        .write_u8(ps)
        .write_u8(slot_type::EQUIPMENT_MATE)
        .write_u8(ms);
    s.into_bytes()
}

fn run_mate(w: &mut World, body: &[u8]) {
    let mut s = PacketStream::from_bytes(body.to_vec());
    let p = CSChangeMateEquipmentPacket::read(&mut s).expect("read");
    futures::executor::block_on(p.handle(&w.server, &mut w.a.conn)).expect("handle");
}

#[tokio::test]
async fn mate_equipment_rejects_another_characters_mate() {
    let mut w = world();
    let mate = spawn_mate(&mut w);
    mate.data().owner_obj_id = w.pb.obj_id;
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, ARMOR, 1, 3);
    let wire = item(&w, id).unwrap();
    run_mate(
        &mut w,
        &mate_body(Some(&wire), None, 3, equip_slot::CHEST as u8),
    );
    assert_eq!(item_at(&w.pa, slot_type::INVENTORY, 3).unwrap().id, id);
    assert!(mate.data().equipment.iter().all(Option::is_none));
    assert!(w.a.drain().is_empty());
    assert!(w.b.drain().is_empty());
}

#[tokio::test]
async fn mate_equipment_equip_unequip_and_refusal() {
    let mut w = world();
    let mate = spawn_mate(&mut w);
    let id = give(&w, &w.pa.clone(), slot_type::INVENTORY, ARMOR, 1, 3);
    let wire = item(&w, id).unwrap();

    // Equip bag slot 3 -> chest of the mate
    run_mate(
        &mut w,
        &mate_body(Some(&wire), None, 3, equip_slot::CHEST as u8),
    );
    let equipped = w
        .server
        .items
        .item_by_id(id)
        .expect("still known to the item manager");
    assert_eq!(
        (equipped.slot_type, equipped.slot),
        (slot_type::EQUIPMENT_MATE, equip_slot::CHEST)
    );
    assert!(item_at(&w.pa, slot_type::INVENTORY, 3).is_none());
    assert_eq!(
        mate.data().equipment[equip_slot::CHEST as usize]
            .as_ref()
            .map(|i| i.id),
        Some(id)
    );
    let mate_gear = SCUnitEquipmentsChangedPacket {
        obj_id: mate.obj_id,
        items: vec![(equip_slot::CHEST as u8, Some(equipped.clone()))],
    };
    assert_eq!(
        w.a.drain(),
        vec![
            (sc::SCUnitEquipmentsChangedPacket, body(&mate_gear)),
            (
                sc::SCMateEquipmentChangedPacket,
                body(&SCMateEquipmentChangedPacket {
                    item_on_pet: ItemAndLocation {
                        item: Some(equipped.clone()),
                        slot_type: slot_type::INVENTORY,
                        slot: 3,
                    },
                    item_in_bag: ItemAndLocation {
                        item: None,
                        slot_type: slot_type::EQUIPMENT_MATE,
                        slot: equip_slot::CHEST as u8,
                    },
                    mate_tl_id: TL,
                    character_id: 1,
                    passenger_id: 0,
                    bts: false,
                })
            ),
        ]
    );
    assert_eq!(
        w.b.drain(),
        vec![(sc::SCUnitEquipmentsChangedPacket, body(&mate_gear))]
    );

    // Un-equip the chest into bag slot 7
    run_mate(
        &mut w,
        &mate_body(None, Some(&equipped), 7, equip_slot::CHEST as u8),
    );
    let back = item_at(&w.pa, slot_type::INVENTORY, 7).unwrap();
    assert_eq!(back.id, id);
    assert!(mate.data().equipment[equip_slot::CHEST as usize].is_none());
    let mate_gear = SCUnitEquipmentsChangedPacket {
        obj_id: mate.obj_id,
        items: vec![(equip_slot::CHEST as u8, None)],
    };
    assert_eq!(
        w.a.drain(),
        vec![
            (sc::SCUnitEquipmentsChangedPacket, body(&mate_gear)),
            (
                sc::SCMateEquipmentChangedPacket,
                body(&SCMateEquipmentChangedPacket {
                    item_on_pet: ItemAndLocation {
                        item: None,
                        slot_type: slot_type::INVENTORY,
                        slot: 7,
                    },
                    item_in_bag: ItemAndLocation {
                        item: Some(back.clone()),
                        slot_type: slot_type::EQUIPMENT_MATE,
                        slot: equip_slot::CHEST as u8,
                    },
                    mate_tl_id: TL,
                    character_id: 1,
                    passenger_id: 0,
                    bts: false,
                })
            ),
        ]
    );

    assert_eq!(
        w.b.drain(),
        vec![(sc::SCUnitEquipmentsChangedPacket, body(&mate_gear))]
    );

    // Refused (armor into the head slot): the failure packet, bag side first
    run_mate(
        &mut w,
        &mate_body(Some(&back), None, 7, equip_slot::HEAD as u8),
    );
    assert_eq!(item_at(&w.pa, slot_type::INVENTORY, 7).unwrap().id, id);
    assert_eq!(
        w.a.drain(),
        vec![(
            sc::SCMateEquipmentChangedPacket,
            body(&SCMateEquipmentChangedPacket {
                item_on_pet: ItemAndLocation {
                    item: Some(back.clone()),
                    slot_type: slot_type::INVENTORY,
                    slot: 7,
                },
                item_in_bag: ItemAndLocation {
                    item: None,
                    slot_type: slot_type::EQUIPMENT_MATE,
                    slot: equip_slot::HEAD as u8,
                },
                mate_tl_id: TL,
                character_id: 1,
                passenger_id: 0,
                bts: false,
            })
        )]
    );
    assert!(w.b.drain().is_empty());

    // Unknown mate: nothing
    let mut body = mate_body(Some(&back), None, 7, equip_slot::CHEST as u8);
    body[4] = 0x99;
    run_mate(&mut w, &body);
    assert!(w.a.drain().is_empty());
}

#[test]
fn slave_and_repair_packets_decode() {
    let mut s = PacketStream::new();
    s.write_u32(5).write_u16(6).write_u32(7).write_bool(true);
    let p = CSChangeSlaveEquipmentPacket::read(&mut s).unwrap();
    assert_eq!(
        p,
        CSChangeSlaveEquipmentPacket {
            id: 5,
            tl: 6,
            db_slave_id: 7,
            bts: true,
        }
    );
    let mut s = PacketStream::new();
    s.write_bc(0x123456);
    assert_eq!(
        CSRepairPetItemsPacket::read(&mut s).unwrap().npc_id,
        0x123456
    );
    let mut s = PacketStream::new();
    s.write_bc(0x10);
    assert_eq!(CSRepairSlaveItemsPacket::read(&mut s).unwrap().npc_id, 0x10);
}

#[test]
#[ignore]
fn real_compact_db_item_effect_tables() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn = rusqlite::Connection::open(path).unwrap();
    let d = ItemEffectsData::load(&conn).unwrap();
    let with_fish = d
        .fishing_loot_packs
        .values()
        .filter(|p| p.sea > 0 || p.land > 0)
        .count();
    assert_eq!(with_fish, 40);
    assert!(!d.repairable_slaves.is_empty());
    let f = FormulaManager::load(&conn).unwrap();
    let formula = f
        .get_formula_by_kind(crate::data::formulas::FormulaKind::GradeEnchantCost)
        .unwrap();
    let params: std::collections::HashMap<&str, f64> = [
        ("item_grade", 1.0),
        ("item_level", 30.0),
        ("equip_slot_enchant_cost", 5.0),
    ]
    .into_iter()
    .collect();
    // (((30/3)^5) * (1/50000) + 150000 + 5000) * 0.5
    assert_eq!(formula.evaluate(&params) as i32, 77_501);
}
