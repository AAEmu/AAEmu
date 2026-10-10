//! Port of AAEmu.UnitTests/Game/Core/Managers/UnitManagers/DoodadManagerTests.cs
//!
//! The C# `DoodadManager` lookups are the [`DoodadData`] methods (game data) and
//! `DoodadManager.Create` is [`super::super::manager::create`]. The reflection-set private
//! dictionaries (`_templates`, `_funcsById`, `_funcsByGroups`, `_phaseFuncs`, `_funcTemplates`,
//! `_phaseFuncTemplates`, `_doodadFuncConsumeChangerItem`) are the public `DoodadData` fields.

use super::*;
use crate::data::GameData;
use crate::test_support::test_server_with;

fn template(id: u32, group_id: u32) -> DoodadTemplate {
    DoodadTemplate {
        id,
        group_id,
        ..DoodadTemplate::default()
    }
}

/// `SetPrivateField(manager, "_templates", templates)` (dictionary order kept in
/// `template_order`).
fn with_templates(templates: Vec<DoodadTemplate>) -> DoodadData {
    let mut d = DoodadData::default();
    for t in templates {
        d.template_order.push(t.id);
        d.templates.insert(t.id, t);
    }
    d
}

fn func_group(id: u32, almighty: u32, kind: u32) -> DoodadFuncGroups {
    DoodadFuncGroups {
        id,
        almighty,
        group_kind_id: kind,
        ..DoodadFuncGroups::default()
    }
}

fn func(func_key: u32, group_id: u32) -> DoodadFunc {
    DoodadFunc {
        func_key,
        group_id,
        ..DoodadFunc::default()
    }
}

fn phase_func(group_id: u32, func_id: u32, func_type: &str) -> DoodadPhaseFunc {
    DoodadPhaseFunc {
        group_id,
        func_id,
        func_type: func_type.into(),
    }
}

// ---- Constructor ----

/// `Constructor_DoesNotCallDeps`: the Rust `DoodadManager` data has no injected dependencies;
/// constructing it has no side effects and leaves every table empty.
#[test]
fn constructor_does_not_call_deps() {
    let d = DoodadData::default();
    assert!(d.templates.is_empty());
    assert!(d.template_order.is_empty());
    assert!(d.all_func_groups.is_empty());
    assert!(d.funcs_by_groups.is_empty());
    assert!(d.funcs_by_id.is_empty());
    assert!(d.phase_funcs.is_empty());
    assert!(d.func_templates.is_empty());
    assert!(d.phase_func_templates.is_empty());
    assert!(d.consume_changer_items.is_empty());
    assert!(d.doodad_groups.is_empty());
}

// ---- Exist ----

#[test]
fn exist_template_exists_returns_true() {
    let d = with_templates(vec![template(1, 0)]);
    assert!(d.exist(1));
}

#[test]
fn exist_template_does_not_exist_returns_false() {
    let d = with_templates(vec![]);
    assert!(!d.exist(999));
}

#[test]
fn exist_zero_id_returns_false() {
    let d = with_templates(vec![]);
    assert!(!d.exist(0));
}

// ---- GetTemplate ----

#[test]
fn get_template_template_exists_returns_template() {
    let d = with_templates(vec![template(1, 100)]);
    let result = d.template(1).expect("template");
    assert_eq!(result.id, 1);
    assert_eq!(result.group_id, 100);
}

#[test]
fn get_template_template_does_not_exist_returns_null() {
    let d = with_templates(vec![]);
    assert!(d.template(999).is_none());
}

#[test]
fn get_template_zero_id_returns_null() {
    let d = with_templates(vec![]);
    assert!(d.template(0).is_none());
}

// ---- Create ----

#[tokio::test]
async fn create_with_invalid_template_returns_null() {
    let server = test_server_with(GameData::empty());
    let world = server.world.world(0).unwrap();

    let result = super::super::manager::create(&server, &world, 0, 999, None, true);

    assert!(result.is_none());
}

// `Create_WithNullWorld_ReturnsNull`: not portable — the Rust `manager::create` takes a
// `&Arc<WorldInstance>`, a null world cannot be passed (the type system enforces the C# check).

// ---- GetFunc ----

#[test]
fn get_func_by_func_id_returns_func() {
    let mut d = DoodadData::default();
    let mut f = func(1, 10);
    f.func_id = 100;
    d.funcs_by_id.insert(1, f);

    let result = d.func(1).expect("func");
    assert_eq!(result.func_key, 1);
    assert_eq!(result.group_id, 10);
}

#[test]
fn get_func_by_func_id_not_found_returns_null() {
    let d = DoodadData::default();
    assert!(d.func(999).is_none());
}

#[test]
fn get_func_by_group_id_and_skill_id_returns_func() {
    let mut d = DoodadData::default();
    let mut f = func(1, 10);
    f.skill_id = 50;
    d.funcs_by_groups.insert(10, vec![f]);

    let result = d.func_for_skill(10, 50).expect("func");
    assert_eq!(result.skill_id, 50);
}

#[test]
fn get_func_by_group_id_group_not_found_returns_null() {
    let d = DoodadData::default();
    assert!(d.func_for_skill(999, 0).is_none());
}

// ---- GetFuncsForGroup ----

#[test]
fn get_funcs_for_group_group_exists_returns_funcs() {
    let mut d = DoodadData::default();
    d.funcs_by_groups.insert(10, vec![func(1, 10), func(2, 10)]);
    assert_eq!(d.funcs_for_group(10).len(), 2);
}

#[test]
fn get_funcs_for_group_group_not_found_returns_empty_list() {
    let d = DoodadData::default();
    assert!(d.funcs_for_group(999).is_empty());
}

// ---- GetPhaseFunc ----

#[test]
fn get_phase_func_group_exists_returns_phase_funcs() {
    let mut d = DoodadData::default();
    d.phase_funcs
        .insert(10, vec![phase_func(10, 1, ""), phase_func(10, 2, "")]);
    assert_eq!(d.phase_funcs(10).len(), 2);
}

#[test]
fn get_phase_func_group_not_found_returns_empty_list() {
    let d = DoodadData::default();
    assert!(d.phase_funcs(999).is_empty());
}

// ---- GetDoodadFuncGroups ----

fn template_with_two_groups() -> DoodadData {
    let mut t = template(1, 0);
    t.func_groups
        .push(func_group(10, 1, doodad_func_group_kind::START));
    t.func_groups
        .push(func_group(20, 1, doodad_func_group_kind::NORMAL));
    with_templates(vec![t])
}

#[test]
fn get_doodad_func_groups_template_exists_returns_groups() {
    let d = template_with_two_groups();
    assert_eq!(d.doodad_func_groups(1).len(), 2);
}

#[test]
fn get_doodad_func_groups_template_not_found_returns_empty_list() {
    let d = with_templates(vec![]);
    assert!(d.doodad_func_groups(999).is_empty());
}

// ---- GetDoodadFuncGroupsId ----

#[test]
fn get_doodad_func_groups_id_template_exists_returns_group_ids() {
    let d = template_with_two_groups();
    let result = d.func_groups_ids(1);
    assert_eq!(result.len(), 2);
    assert!(result.contains(&10));
    assert!(result.contains(&20));
}

#[test]
fn get_doodad_func_groups_id_template_not_found_returns_empty_list() {
    let d = with_templates(vec![]);
    assert!(d.func_groups_ids(999).is_empty());
}

// ---- GetDoodadFuncConsumeChangerItemList ----

#[test]
fn get_doodad_func_consume_changer_item_list_items_exist_returns_item_ids() {
    let mut d = DoodadData::default();
    for (id, changer, item) in [(1, 10, 100), (2, 10, 200), (3, 20, 300)] {
        d.consume_changer_items.push(DoodadFuncConsumeChangerItem {
            id,
            doodad_func_consume_changer_id: changer,
            item_id: item,
        });
    }

    let result = d.consume_changer_items(10);
    assert_eq!(result.len(), 2);
    assert!(result.contains(&100));
    assert!(result.contains(&200));
}

#[test]
fn get_doodad_func_consume_changer_item_list_no_items_found_returns_empty_list() {
    let d = DoodadData::default();
    assert!(d.consume_changer_items(999).is_empty());
}

// ---- GetTreasureChestTemplateIds ----

#[test]
fn get_treasure_chest_template_ids_returns_chest_templates() {
    let d = with_templates(vec![
        template(1, 55), // Treasure chest group
        template(2, 56), // Treasure chest group
        template(3, 60), // Not a treasure chest
        template(4, 57), // Treasure chest group
        template(5, 54), // Not a treasure chest
    ]);

    let result = d.treasure_chest_template_ids();
    assert_eq!(result.len(), 3);
    assert!(result.contains(&1));
    assert!(result.contains(&2));
    assert!(result.contains(&4));
    assert!(!result.contains(&3));
    assert!(!result.contains(&5));
}

#[test]
fn get_treasure_chest_template_ids_no_templates_returns_empty_list() {
    let d = with_templates(vec![]);
    assert!(d.treasure_chest_template_ids().is_empty());
}

// ---- GetDoodadPhaseFuncs ----

#[test]
fn get_doodad_phase_funcs_group_exists_returns_phase_funcs() {
    let mut d = DoodadData::default();
    d.phase_funcs.insert(
        10,
        vec![
            phase_func(10, 1, "DoodadFuncTimer"),
            phase_func(10, 2, "DoodadFuncGrowth"),
        ],
    );
    assert_eq!(d.phase_funcs(10).len(), 2);
}

#[test]
fn get_doodad_phase_funcs_group_not_found_returns_empty_list() {
    let d = DoodadData::default();
    assert!(d.phase_funcs(999).is_empty());
}

// ---- GetFuncTemplate ----

#[test]
fn get_func_template_template_exists_returns_template() {
    let mut d = DoodadData::default();
    d.func_templates.entry("DoodadFuncUse").or_default().insert(
        1,
        FuncTemplate::Use(DoodadFuncUse {
            id: 1,
            skill_id: 100,
        }),
    );

    let result = d.func_template(1, "DoodadFuncUse").expect("template");
    assert_eq!(result.id(), 1);
}

#[test]
fn get_func_template_type_not_found_returns_null() {
    let d = DoodadData::default();
    assert!(d.func_template(1, "NonExistentType").is_none());
}

#[test]
fn get_func_template_template_not_found_returns_null() {
    let mut d = DoodadData::default();
    d.func_templates.entry("DoodadFuncUse").or_default();
    assert!(d.func_template(999, "DoodadFuncUse").is_none());
}

// ---- GetPhaseFuncTemplate ----

#[test]
fn get_phase_func_template_template_exists_returns_template() {
    let mut d = DoodadData::default();
    d.phase_func_templates
        .entry("DoodadFuncTimer")
        .or_default()
        .insert(
            1,
            FuncTemplate::Timer(DoodadFuncTimer {
                id: 1,
                delay: 5000,
                ..DoodadFuncTimer::default()
            }),
        );

    let result = d
        .phase_func_template(1, "DoodadFuncTimer")
        .expect("template");
    assert_eq!(result.id(), 1);
}

#[test]
fn get_phase_func_template_type_not_found_returns_null() {
    let d = DoodadData::default();
    assert!(d.phase_func_template(1, "NonExistentType").is_none());
}

// ---- GetDoodadFuncs ----

#[test]
fn get_doodad_funcs_group_exists_returns_funcs() {
    let mut d = DoodadData::default();
    let mut a = func(1, 10);
    a.func_type = "DoodadFuncUse".into();
    let mut b = func(2, 10);
    b.func_type = "DoodadFuncLootItem".into();
    d.funcs_by_groups.insert(10, vec![a, b]);
    assert_eq!(d.doodad_funcs(10).len(), 2);
}

#[test]
fn get_doodad_funcs_group_not_found_returns_empty_list() {
    let d = DoodadData::default();
    assert!(d.doodad_funcs(999).is_empty());
}

// ---- Edge cases ----

#[test]
fn exist_max_u_int32_returns_false() {
    let d = with_templates(vec![]);
    assert!(!d.exist(u32::MAX));
}

#[test]
fn get_template_max_u_int32_returns_null() {
    let d = with_templates(vec![]);
    assert!(d.template(u32::MAX).is_none());
}

#[test]
fn multiple_get_template_calls_return_consistent_results() {
    let d = with_templates(vec![template(1, 100)]);
    let result1 = d.template(1).expect("1");
    let result2 = d.template(1).expect("2");
    let result3 = d.template(1).expect("3");
    assert_eq!(result2.id, result1.id);
    assert_eq!(result3.id, result2.id);
}
