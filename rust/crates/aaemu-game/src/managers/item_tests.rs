//! Port of AAEmu.UnitTests/Game/Core/Managers/ItemManagerTests.cs
//!
//! The C# `ItemManager` holds both the template tables (`_templates`, `_grades`,
//! `_armorGradeBuffs`, ... loaded from compact.sqlite3) and the user items (`_allItems`,
//! `_allPersistentContainers`, `_removedItems`). In Rust the tables are `data::items::ItemData`
//! (`server.game_data.items`) and the user items are [`ItemManager`]; the C# reflection setup
//! (`SetPrivateField`) becomes direct writes to the `ItemData` maps and `ItemManager::add_item`.
//! The injected `IItemIdManager` is an explicit `IdManager` argument in Rust.

use crate::data::items::{
    armor_type, bind_type, item_grade, ArmorGradeBuff, EquipItemSet, EquipItemSetBonus,
    FullGradeTemplate, GradeDistributions, ItemConfig, ItemData, ItemProcTemplate, ItemSet,
    ItemTemplate, TemplateKind,
};
use crate::managers::id::{IdManager, IdManagerSpec, ITEM_ID};
use crate::managers::item::ItemManager;
use crate::models::container::slot_type;
use crate::models::item::Item;
use crate::models::unit::{BonusTemplate, UnitAttribute, UnitModifierType};

/// `CreateItemManager()`.
fn create_item_manager() -> ItemManager {
    ItemManager::default()
}

/// `CreateTestItem(id, templateId, count)`.
fn create_test_item(id: u64, template_id: u32, count: i32) -> Item {
    let mut template = ItemTemplate::plain(template_id, 100);
    template.name = format!("Test Item {template_id}");
    template.bind_type = bind_type::NORMAL;
    Item::with_template(1, id, &template, count)
}

fn named(id: u32, name: &str) -> ItemTemplate {
    let mut t = ItemTemplate::plain(id, 0);
    t.name = name.to_string();
    t
}

fn data_with_templates(templates: Vec<ItemTemplate>) -> ItemData {
    let mut d = ItemData::default();
    for t in templates {
        d.templates.insert(t.id, t);
    }
    d
}

// ---- Constructor Tests ----

/// `Constructor_DoesNotCallDependencies`: the Rust manager has no injected dependencies;
/// construction has no side effects (no containers, items or deletions, no ids drawn).
#[test]
fn constructor_does_not_call_dependencies() {
    let manager = ItemManager::default();
    assert!(manager.all_containers().is_empty());
    assert!(manager.removed_items().is_empty());
    assert!(manager.item_by_id(ITEM_ID.first_id.into()).is_none());
    assert!(manager.treasure_maps.get().is_none());
}

// ---- GetTemplate Tests ----

#[test]
fn get_template_template_exists_returns_template() {
    let mut template = named(100, "Test Item");
    template.level = 10;
    template.price = 50;
    let data = data_with_templates(vec![template]);

    let result = data.template(100).expect("template");

    assert_eq!(result.id, 100);
    assert_eq!(result.name, "Test Item");
    assert_eq!(result.level, 10);
    assert_eq!(result.price, 50);
}

#[test]
fn get_template_template_does_not_exist_returns_null() {
    let data = ItemData::default();
    assert!(data.template(999).is_none());
}

#[test]
fn get_template_zero_id_returns_null() {
    let data = ItemData::default();
    assert!(data.template(0).is_none());
}

#[test]
fn get_template_multiple_templates_returns_correct_template() {
    let t1 = named(1, "Item 1");
    let t2 = named(2, "Item 2");
    let t3 = named(3, "Item 3");
    let data = data_with_templates(vec![t1.clone(), t2.clone(), t3.clone()]);

    assert_eq!(data.template(1), Some(&t1));
    assert_eq!(data.template(2), Some(&t2));
    assert_eq!(data.template(3), Some(&t3));
}

// ---- GetItemByItemId Tests ----

#[test]
fn get_item_by_item_id_item_exists_returns_item() {
    let manager = create_item_manager();
    assert!(manager.add_item(create_test_item(1000, 100, 5)));

    let result = manager.item_by_id(1000).expect("item");

    assert_eq!(result.id, 1000);
    assert_eq!(result.template_id, 100);
    assert_eq!(result.count, 5);
}

#[test]
fn get_item_by_item_id_item_does_not_exist_returns_null() {
    let manager = create_item_manager();
    assert!(manager.item_by_id(9999).is_none());
}

#[test]
fn get_item_by_item_id_zero_id_returns_null() {
    let manager = create_item_manager();
    assert!(manager.item_by_id(0).is_none());
}

#[test]
fn get_item_by_item_id_multiple_items_returns_correct_item() {
    let manager = create_item_manager();
    let item1 = create_test_item(100, 1, 1);
    let item2 = create_test_item(200, 2, 2);
    let item3 = create_test_item(300, 3, 3);
    for i in [&item1, &item2, &item3] {
        assert!(manager.add_item(i.clone()));
    }

    assert_eq!(manager.item_by_id(100), Some(item1));
    assert_eq!(manager.item_by_id(200), Some(item2));
    assert_eq!(manager.item_by_id(300), Some(item3));
}

// ---- AddItem Tests ----

#[test]
fn add_item_valid_item_adds_to_collection() {
    let manager = create_item_manager();
    let item = create_test_item(2000, 100, 1);

    let result = manager.add_item(item.clone());

    assert!(result);
    assert_eq!(manager.item_by_id(2000), Some(item));
}

#[test]
fn add_item_duplicate_item_returns_false() {
    let manager = create_item_manager();
    let item1 = create_test_item(2001, 100, 1);
    let item2 = create_test_item(2001, 100, 2);
    assert!(manager.add_item(item1.clone()));

    let result = manager.add_item(item2);

    assert!(!result);
    // The first item is kept.
    assert_eq!(manager.item_by_id(2001), Some(item1));
}

// `AddItem_NullItem_ThrowsArgumentNullException`: not portable (`add_item` takes an `Item` by
// value; there is no null item in Rust).

// ---- GetAllItems Tests ----

#[test]
fn get_all_items_has_templates_returns_all_templates() {
    let t1 = named(1, "Item 1");
    let t2 = named(2, "Item 2");
    let t3 = named(3, "Item 3");
    let data = data_with_templates(vec![t1.clone(), t2.clone(), t3.clone()]);

    let result: Vec<&ItemTemplate> = data.all_templates().collect();

    assert_eq!(result.len(), 3);
    assert!(result.contains(&&t1));
    assert!(result.contains(&&t2));
    assert!(result.contains(&&t3));
}

#[test]
fn get_all_items_no_templates_returns_empty_list() {
    let data = ItemData::default();
    assert_eq!(data.all_templates().count(), 0);
}

// ---- GetGradeTemplate Tests ----

#[test]
fn get_grade_template_grade_exists_returns_template() {
    let mut data = ItemData::default();
    data.insert_grade(FullGradeTemplate {
        grade: 5,
        holdable_dps: 100.0,
        stat_multiplier: 2,
        ..FullGradeTemplate::default()
    });

    // `GetGradeTemplate` (the short form the item model uses) and the full row.
    assert_eq!(data.grade(5).expect("grade").grade, 5);
    let result = data.full_grade(5).expect("grade");
    assert_eq!(result.grade, 5);
    assert_eq!(result.holdable_dps, 100.0);
    assert_eq!(result.stat_multiplier, 2);
}

#[test]
fn get_grade_template_grade_does_not_exist_returns_null() {
    let data = ItemData::default();
    assert!(data.grade(99).is_none());
    assert!(data.full_grade(99).is_none());
}

// ---- GetArmorGradeBuff Tests ----

#[test]
fn get_armor_grade_buff_buff_exists_returns_buff() {
    let mut data = ItemData::default();
    let buff = ArmorGradeBuff {
        id: 1,
        armor_type: armor_type::CLOTH,
        item_grade: u32::from(item_grade::HEROIC),
        buff_id: 1000,
    };
    data.armor_grade_buffs
        .entry(armor_type::CLOTH)
        .or_default()
        .insert(u32::from(item_grade::HEROIC), buff);

    let result = data
        .armor_grade_buff(armor_type::CLOTH, u32::from(item_grade::HEROIC))
        .expect("buff");

    assert_eq!(result.id, 1);
    assert_eq!(result.buff_id, 1000);
}

#[test]
fn get_armor_grade_buff_armor_type_does_not_exist_returns_null() {
    let data = ItemData::default();
    assert!(data
        .armor_grade_buff(armor_type::CLOTH, u32::from(item_grade::HEROIC))
        .is_none());
}

#[test]
fn get_armor_grade_buff_grade_does_not_exist_returns_null() {
    let mut data = ItemData::default();
    data.armor_grade_buffs
        .insert(armor_type::CLOTH, Default::default());
    assert!(data
        .armor_grade_buff(armor_type::CLOTH, u32::from(item_grade::HEROIC))
        .is_none());
}

// ---- GetUnitModifiers Tests ----

#[test]
fn get_unit_modifiers_modifiers_exist_returns_modifiers() {
    let mut data = ItemData::default();
    data.item_unit_modifiers.insert(
        100,
        vec![
            BonusTemplate {
                attribute: UnitAttribute::Str,
                value: 10,
                modifier_type: UnitModifierType::Percent,
                linear_level_bonus: 0,
            },
            BonusTemplate {
                attribute: UnitAttribute::Dex,
                value: 5,
                modifier_type: UnitModifierType::Value,
                linear_level_bonus: 0,
            },
        ],
    );

    let result = data.unit_modifiers(100);

    assert_eq!(result.len(), 2);
    assert_eq!(result[0].value, 10);
    assert_eq!(result[0].attribute, UnitAttribute::Str);
}

#[test]
fn get_unit_modifiers_no_modifiers_returns_empty_list() {
    let data = ItemData::default();
    assert!(data.unit_modifiers(999).is_empty());
}

#[test]
fn get_unit_modifiers_empty_modifiers_returns_empty_list() {
    let mut data = ItemData::default();
    data.item_unit_modifiers.insert(100, Vec::new());
    assert!(data.unit_modifiers(100).is_empty());
}

// ---- GetItemProcTemplate Tests ----

#[test]
fn get_item_proc_template_template_exists_returns_template() {
    let mut data = ItemData::default();
    data.item_procs.insert(
        50,
        ItemProcTemplate {
            id: 50,
            skill_id: 100,
            chance_rate: 50,
            cooldown_sec: 30,
            ..ItemProcTemplate::default()
        },
    );

    let result = data.item_proc(50).expect("proc");

    assert_eq!(result.id, 50);
    assert_eq!(result.skill_id, 100);
    assert_eq!(result.chance_rate, 50);
    assert_eq!(result.cooldown_sec, 30);
}

#[test]
fn get_item_proc_template_template_does_not_exist_returns_null() {
    let data = ItemData::default();
    assert!(data.item_proc(999).is_none());
}

// ---- GetEquippedItemSet Tests ----

#[test]
fn get_equipped_item_set_set_exists_returns_set() {
    let mut data = ItemData::default();
    let mut equip_set = EquipItemSet {
        id: 10,
        ..EquipItemSet::default()
    };
    equip_set.bonuses.push(EquipItemSetBonus {
        num_pieces: 3,
        buff_id: 100,
        ..EquipItemSetBonus::default()
    });
    data.equip_item_sets.insert(10, equip_set);

    let result = data.equip_item_set(10).expect("set");

    assert_eq!(result.id, 10);
    assert_eq!(result.bonuses.len(), 1);
    assert_eq!(result.bonuses[0].num_pieces, 3);
}

#[test]
fn get_equipped_item_set_set_does_not_exist_returns_null() {
    let data = ItemData::default();
    assert!(data.equip_item_set(999).is_none());
}

// ---- GetItemSet Tests ----

#[test]
fn get_item_set_set_exists_returns_set() {
    let mut data = ItemData::default();
    data.item_sets.insert(
        20,
        ItemSet {
            id: 20,
            name: "Test Set".into(),
            kind_id: 1,
            ..ItemSet::default()
        },
    );

    let result = data.item_set(20).expect("set");

    assert_eq!(result.id, 20);
    assert_eq!(result.name, "Test Set");
}

#[test]
fn get_item_set_set_does_not_exist_returns_null() {
    let data = ItemData::default();
    assert!(data.item_set(999).is_none());
}

// ---- GetSocketChance Tests ----

#[test]
fn get_socket_chance_chance_exists_returns_chance() {
    let mut data = ItemData::default();
    data.socket_chance.extend([(1, 100), (2, 80), (3, 60)]);

    // 0 + 1 = 1
    assert_eq!(data.socket_chance(0), 100);
}

#[test]
fn get_socket_chance_chance_does_not_exist_returns_zero() {
    let data = ItemData::default();
    assert_eq!(data.socket_chance(10), 0);
}

// ---- GetItemIdsFromDoodad Tests ----

#[test]
fn get_item_ids_from_doodad_doodad_exists_returns_item_ids() {
    let mut data = ItemData::default();
    data.item_doodads.insert(100, vec![1, 2, 3]);

    let result = data.item_ids_from_doodad(100);

    assert_eq!(result.len(), 3);
    assert!(result.contains(&1));
    assert!(result.contains(&2));
    assert!(result.contains(&3));
}

#[test]
fn get_item_ids_from_doodad_doodad_does_not_exist_returns_empty_list() {
    let data = ItemData::default();
    assert!(data.item_ids_from_doodad(999).is_empty());
}

// ---- GetGradeDistributions Tests ----

#[test]
fn get_grade_distributions_distribution_exists_returns_distribution() {
    let mut data = ItemData::default();
    let mut weights = [0; 12];
    weights[0] = 50;
    weights[1] = 30;
    weights[2] = 20;
    data.grade_distributions.insert(
        1,
        GradeDistributions {
            id: 1,
            name: "Common".into(),
            weights,
        },
    );

    let result = data.grade_distributions(1).expect("distribution");

    assert_eq!(result.id, 1);
    assert_eq!(result.name, "Common");
    assert_eq!(result.weights[0], 50);
}

#[test]
fn get_grade_distributions_distribution_does_not_exist_returns_null() {
    let data = ItemData::default();
    assert!(data.grade_distributions(99).is_none());
}

// ---- GetItemTemplateFromItemId Tests ----

#[test]
fn get_item_template_from_item_id_template_exists_returns_template() {
    let data = data_with_templates(vec![named(100, "Test Item")]);
    assert_eq!(data.template(100).expect("template").id, 100);
}

#[test]
fn get_item_template_from_item_id_template_does_not_exist_returns_null() {
    let data = ItemData::default();
    assert!(data.template(999).is_none());
}

// ---- GetItemContainerByDbId Tests ----

#[test]
fn get_item_container_by_db_id_container_does_not_exist_returns_null() {
    let manager = create_item_manager();
    assert!(manager.container_by_id(999).is_none());
}

// ---- ReleaseId Tests ----

#[test]
fn release_id_valid_id_removes_from_items() {
    // The `IItemIdManager` mock: a manager whose first id is 3000 and that has 3000 in use, so
    // the `ReleaseId(3000)` call is observable (3000 becomes the next free id again).
    let item_ids = IdManager::new(IdManagerSpec {
        name: "ItemIdManager",
        first_id: 3000,
        last_id: 0xFFFF_FFFF,
        obj_tables: &[],
        distinct: false,
    });
    item_ids.initialize_with(&[3000]);
    let manager = create_item_manager();
    assert!(manager.add_item(create_test_item(3000, 100, 1)));

    manager.release_id(3000, &item_ids);

    assert!(manager.item_by_id(3000).is_none());
    assert!(manager.removed_items().contains(&3000));
    // itemIdManager.ReleaseId(3000) was called once.
    assert_eq!(item_ids.get_next_id().unwrap(), 3000);
    assert_eq!(item_ids.get_next_id().unwrap(), 3001);
}

#[test]
fn release_id_zero_id_does_not_add_to_removed() {
    let item_ids = IdManager::new(ITEM_ID);
    let manager = create_item_manager();

    manager.release_id(0, &item_ids);

    assert!(!manager.removed_items().contains(&0));
}

// ---- IsAutoEquipTradePack Tests ----

fn backpack(id: u32, name: &str, bind: u32) -> ItemTemplate {
    ItemTemplate {
        name: name.into(),
        bind_type: bind,
        ..ItemTemplate::new(id, TemplateKind::Backpack { backpack_type: 0 })
    }
}

#[test]
fn is_auto_equip_trade_pack_valid_trade_pack_returns_true() {
    let data = data_with_templates(vec![backpack(100, "Trade Pack", bind_type::BIND_ON_PICKUP)]);
    assert!(data.is_auto_equip_trade_pack(100));
}

#[test]
fn is_auto_equip_trade_pack_not_backpack_returns_false() {
    let mut t = named(100, "Regular Item");
    t.bind_type = bind_type::NORMAL;
    let data = data_with_templates(vec![t]);
    assert!(!data.is_auto_equip_trade_pack(100));
}

#[test]
fn is_auto_equip_trade_pack_bind_on_equip_returns_false() {
    let data = data_with_templates(vec![backpack(100, "Trade Pack", bind_type::BIND_ON_EQUIP)]);
    assert!(!data.is_auto_equip_trade_pack(100));
}

#[test]
fn is_auto_equip_trade_pack_invalid_template_returns_false() {
    let data = ItemData::default();
    assert!(!data.is_auto_equip_trade_pack(999));
}

// ---- GetContainerSlotTypeByContainerId Tests ----

#[test]
fn get_container_slot_type_by_container_id_container_does_not_exist_returns_none() {
    let manager = create_item_manager();
    assert_eq!(manager.container_slot_type(999), slot_type::NONE);
}

// ---- Config Value Tests ----

#[test]
fn get_durability_repair_cost_factor_returns_factor() {
    let data = ItemData {
        config: ItemConfig {
            durability_repair_cost_factor: 0.5,
            ..ItemConfig::default()
        },
        ..ItemData::default()
    };
    assert_eq!(data.config.durability_repair_cost_factor, 0.5f32);
}

#[test]
fn get_durability_const_returns_const() {
    let data = ItemData {
        config: ItemConfig {
            durability_const: 100.0,
            ..ItemConfig::default()
        },
        ..ItemData::default()
    };
    assert_eq!(data.config.durability_const, 100f32);
}

#[test]
fn get_item_stat_const_returns_const() {
    let data = ItemData {
        config: ItemConfig {
            item_stat_const: 50,
            ..ItemConfig::default()
        },
        ..ItemData::default()
    };
    // `GetItemStatConst()` returns the int column as a float.
    assert_eq!(data.config.item_stat_const as f32, 50f32);
}

// ---- Edge Case Tests ----

#[test]
fn get_template_max_u_int_id_returns_null() {
    let data = ItemData::default();
    assert!(data.template(u32::MAX).is_none());
}

#[test]
fn get_item_by_item_id_max_u_long_id_returns_null() {
    let manager = create_item_manager();
    assert!(manager.item_by_id(u64::MAX).is_none());
}

#[test]
fn add_item_max_count_item_adds_successfully() {
    let manager = create_item_manager();
    let item = create_test_item(8000, 100, i32::MAX);

    let result = manager.add_item(item);

    assert!(result);
    assert_eq!(manager.item_by_id(8000).expect("item").count, i32::MAX);
}

#[test]
fn get_all_items_large_collection_returns_all_items() {
    let data = data_with_templates((1..=1000).map(|i| named(i, &format!("Item {i}"))).collect());
    assert_eq!(data.all_templates().count(), 1000);
}
