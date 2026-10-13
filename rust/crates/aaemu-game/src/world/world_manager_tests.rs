//! Port of AAEmu.UnitTests/Game/Core/Managers/World/WorldManagerTests.cs
//!
//! The C# `WorldManager` is split in the Rust port: the runtime part (instances, the
//! server-wide character list, `IsSnowing`) is [`WorldManager`]; the template / zone-key caches
//! of `WorldManager.Load` are [`WorldTemplates`] (`game_data.worlds`); `_areaShapes` /
//! `_worldInteractionGroups` are [`AreaTriggerData`] (`game_data.systems.npcs.area_trigger`).
//! The C# reflection setup of the private dictionaries becomes the matching Rust constructors.
//!
//! C# tests passing `null` for a `string` / `Character` parameter
//! (`GetWorldTemplateByName_NullName_ThrowsArgumentNullException`,
//! `GetCharacter_NullName_ReturnsNull`, `TryAddCharacter_NullCharacter_ThrowsNullReferenceException`)
//! have no Rust equivalent: the parameters are `&str` / `&PlayerRef`, which cannot be null.

use std::collections::HashMap;
use std::net::SocketAddr;
use std::sync::atomic::Ordering;
use std::sync::Arc;

use tokio::sync::mpsc;

use super::{PlayerRef, WorldInstance, WorldManager};
use crate::data::world_templates::{
    WorldTemplate, WorldTemplates, CELL_SIZE, REGION_SIZE, SECTORS_PER_CELL,
};
use crate::models::character::Character;
use crate::net::connection::GameConnection;
use crate::systems::npcs::area_trigger::{
    world_interaction_group, AreaShape, AreaTriggerData, AREA_SHAPE_SPHERE,
};
use crate::world::Player;

// ----- helpers (`CreateWorldManager`, `CreateWorldTemplate`) -----

/// `CreateWorldManager()`: the Rust manager has no injected dependencies.
fn create_world_manager() -> WorldManager {
    WorldManager::default()
}

/// `CreateWorldTemplate(id, name)`: 2x2 cells, empty `ZoneKeys`, every region zone 0.
fn create_world_template(id: u32, name: &str) -> WorldTemplate {
    let mut t = WorldTemplate::synthetic(id, name, 2, 2, 0);
    t.zone_keys.clear();
    t
}

/// `new WorldInstance(template, 0, true, instanceId)`.
fn world_instance(template: WorldTemplate, instance_id: u32) -> WorldInstance {
    WorldInstance::new(instance_id, Arc::new(template))
}

/// `new Character(new UnitCustomModelParams()) { ObjId, Id, Name }` as a world player.
fn character(obj_id: u32, id: u32, name: &str) -> PlayerRef {
    let mut c = Character::new(id, 1, Default::default());
    c.obj_id = obj_id;
    c.unit.name = name.into();
    let (out_tx, _out) = crate::net::outbox::outbox();
    let (cmd_tx, _cmd) = mpsc::unbounded_channel();
    let peer: SocketAddr = "127.0.0.1:50000".parse().expect("addr");
    let (_conn, handle) = GameConnection::new(obj_id, peer, out_tx, cmd_tx);
    Player::new(c, handle)
}

/// A manager whose `_worlds` holds `worlds` (`SetPrivateField(manager, "_worlds", ...)`).
fn manager_with_worlds(worlds: Vec<WorldInstance>) -> WorldManager {
    let manager = create_world_manager();
    for w in worlds {
        let template = w.template.clone();
        let id = w.id;
        manager.create_world_instance(template, id);
    }
    manager
}

// ----- Constructor -----

#[test]
fn constructor_does_not_call_dependencies() {
    let manager = create_world_manager();
    assert!(manager.worlds().is_empty());
    assert!(manager.all_characters().is_empty());
    assert!(!manager.is_snowing.load(Ordering::Relaxed));
}

// ----- GetWorld -----

#[test]
fn get_world_existing_world_returns_world_instance() {
    let manager = manager_with_worlds(vec![world_instance(
        create_world_template(1, "test_world"),
        1,
    )]);
    let result = manager.world(1).expect("world 1");
    assert_eq!(result.id, 1);
    assert_eq!(result.template.name, "test_world");
}

#[test]
fn get_world_non_existing_world_returns_null() {
    let manager = manager_with_worlds(Vec::new());
    assert!(manager.world(999).is_none());
}

#[test]
fn get_world_zero_id_returns_null() {
    let manager = manager_with_worlds(Vec::new());
    assert!(manager.world(0).is_none());
}

#[test]
fn get_world_multiple_worlds_returns_correct_world() {
    let manager = manager_with_worlds(vec![
        world_instance(create_world_template(1, "world_1"), 1),
        world_instance(create_world_template(2, "world_2"), 2),
    ]);
    let result1 = manager.world(1).expect("world 1");
    let result2 = manager.world(2).expect("world 2");
    assert_eq!(result1.template.name, "world_1");
    assert_eq!(result2.template.name, "world_2");
}

// ----- GetWorlds -----

#[test]
fn get_worlds_no_worlds_returns_empty_array() {
    let manager = manager_with_worlds(Vec::new());
    assert!(manager.worlds().is_empty());
}

#[test]
fn get_worlds_multiple_worlds_returns_all_worlds() {
    let manager = manager_with_worlds(vec![
        world_instance(create_world_template(1, "world_1"), 1),
        world_instance(create_world_template(2, "world_2"), 2),
    ]);
    assert_eq!(manager.worlds().len(), 2);
}

#[test]
fn get_worlds_single_world_returns_array_with_one_element() {
    let manager = manager_with_worlds(vec![world_instance(
        create_world_template(1, "test_world"),
        1,
    )]);
    assert_eq!(manager.worlds().len(), 1);
}

// ----- GetWorldTemplateByName -----

#[test]
fn get_world_template_by_name_existing_template_returns_template() {
    let templates =
        WorldTemplates::from_templates(vec![create_world_template(1, "main_world")], Vec::new())
            .expect("templates");
    let result = templates
        .get_world_template_by_name("main_world")
        .expect("template");
    assert_eq!(result.id, 1);
    assert_eq!(result.name, "main_world");
}

#[test]
fn get_world_template_by_name_non_existing_template_returns_null() {
    let templates = WorldTemplates::default();
    assert!(templates
        .get_world_template_by_name("non_existent")
        .is_none());
}

// ----- GetWorldTemplateByZoneKey -----

#[test]
fn get_world_template_by_zone_key_existing_zone_returns_template() {
    // ZoneKeys = [100], _worldIdByZoneKey = {100: 1}, WorldNames = ["", "main_world"].
    let mut t = create_world_template(1, "main_world");
    t.zone_keys.push(100);
    let templates = WorldTemplates::from_templates(vec![t], Vec::new()).expect("templates");
    assert_eq!(
        templates.world_names().to_vec(),
        vec![String::new(), "main_world".to_string()]
    );
    let result = templates
        .get_world_template_by_zone_key(100)
        .expect("template");
    assert_eq!(result.name, "main_world");
}

#[test]
fn get_world_template_by_zone_key_non_existing_zone_returns_null() {
    let templates = WorldTemplates::default();
    assert!(templates.get_world_template_by_zone_key(999).is_none());
}

// ----- GetZoneId -----

#[test]
fn get_zone_id_valid_position_returns_zone_id() {
    let templates = WorldTemplates::default();
    let mut t = create_world_template(1, "main_world");
    assert!(t.set_zone_key_at(1, 1, 100));
    // Position (64, 64) is in region (1, 1) with REGION_SIZE = 64.
    assert_eq!(templates.get_zone_id(Some(&t), 64.0, 64.0), 100);
}

#[test]
fn get_zone_id_null_template_returns_zero() {
    let templates = WorldTemplates::default();
    assert_eq!(templates.get_zone_id(None, 100.0, 100.0), 0);
}

#[test]
fn get_zone_id_out_of_bounds_returns_zero() {
    let templates = WorldTemplates::default();
    let t = create_world_template(1, "main_world");
    assert_eq!(templates.get_zone_id(Some(&t), 100000.0, 100000.0), 0);
}

// ----- GetZoneKeysByWorldId -----

#[test]
fn get_zone_keys_by_world_id_existing_world_returns_zone_keys() {
    let mut t = create_world_template(1, "main_world");
    t.zone_keys.extend([100, 101, 102]);
    let templates = WorldTemplates::from_templates(vec![t], Vec::new()).expect("templates");
    let result = templates.get_zone_keys_by_world_id(1);
    assert_eq!(result.len(), 3);
    assert!(result.contains(&100));
}

#[test]
fn get_zone_keys_by_world_id_non_existing_world_returns_empty_list() {
    let templates = WorldTemplates::default();
    assert!(templates.get_zone_keys_by_world_id(999).is_empty());
}

// ----- GetWorldIdByZoneKey -----

#[test]
fn get_world_id_by_zone_key_existing_zone_returns_world_id() {
    let mut t = create_world_template(1, "main_world");
    t.zone_keys.push(100);
    let templates = WorldTemplates::from_templates(vec![t], Vec::new()).expect("templates");
    assert_eq!(templates.get_world_id_by_zone_key(100), 1);
}

#[test]
fn get_world_id_by_zone_key_non_existing_zone_returns_max_value() {
    let templates = WorldTemplates::default();
    assert_eq!(templates.get_world_id_by_zone_key(999), u32::MAX);
}

// ----- Character management -----

#[test]
fn try_add_character_new_character_returns_true() {
    let manager = create_world_manager();
    assert!(manager.try_add_character(&character(100, 1, "")));
}

#[test]
fn try_add_character_duplicate_character_returns_false() {
    let manager = create_world_manager();
    let c = character(100, 1, "");
    manager.try_add_character(&c);
    assert!(!manager.try_add_character(&c));
}

#[test]
fn try_remove_character_existing_character_returns_true() {
    let manager = create_world_manager();
    manager.try_add_character(&character(100, 1, ""));
    assert!(manager.try_remove_character(100));
}

#[test]
fn try_remove_character_non_existing_character_returns_false() {
    let manager = create_world_manager();
    assert!(!manager.try_remove_character(999));
}

#[test]
fn get_character_by_obj_id_existing_character_returns_character() {
    let manager = create_world_manager();
    manager.try_add_character(&character(100, 1, "TestPlayer"));
    let result = manager.character_by_obj_id(100).expect("character");
    assert_eq!(result.data().unit.name, "TestPlayer");
}

#[test]
fn get_character_by_obj_id_non_existing_character_returns_null() {
    let manager = create_world_manager();
    assert!(manager.character_by_obj_id(999).is_none());
}

#[test]
fn get_character_existing_character_by_name_returns_character() {
    let manager = create_world_manager();
    manager.try_add_character(&character(100, 1, "TestPlayer"));
    let result = manager.character_by_name("TestPlayer").expect("character");
    assert_eq!(result.data().unit.name, "TestPlayer");
}

#[test]
fn get_character_existing_character_by_name_case_insensitive_returns_character() {
    let manager = create_world_manager();
    manager.try_add_character(&character(100, 1, "TestPlayer"));
    let result = manager.character_by_name("testplayer").expect("character");
    assert_eq!(result.data().unit.name, "TestPlayer");
}

#[test]
fn get_character_non_existing_character_returns_null() {
    let manager = create_world_manager();
    assert!(manager.character_by_name("NonExistent").is_none());
}

#[test]
fn get_all_characters_with_characters_returns_all_characters() {
    let manager = create_world_manager();
    manager.try_add_character(&character(1, 1, "Player1"));
    manager.try_add_character(&character(2, 2, "Player2"));
    assert_eq!(manager.all_characters().len(), 2);
}

#[test]
fn get_all_characters_no_characters_returns_empty_list() {
    let manager = create_world_manager();
    assert!(manager.all_characters().is_empty());
}

// ----- World interaction groups -----

#[test]
fn get_world_interaction_group_existing_group_returns_group() {
    let data = AreaTriggerData {
        world_interaction_groups: HashMap::from([(1, world_interaction_group::CRAFT)]),
        ..Default::default()
    };
    assert_eq!(
        data.get_world_interaction_group(1),
        Some(world_interaction_group::CRAFT)
    );
}

#[test]
fn get_world_interaction_group_non_existing_group_returns_null() {
    let data = AreaTriggerData::default();
    assert_eq!(data.get_world_interaction_group(999), None);
}

// ----- GetWorldsByTemplate -----

#[test]
fn get_worlds_by_template_existing_worlds_returns_matching_worlds() {
    let manager = manager_with_worlds(vec![
        world_instance(create_world_template(1, "template_1"), 1),
        world_instance(create_world_template(2, "template_2"), 2),
        world_instance(create_world_template(1, "template_1"), 3),
    ]);
    assert_eq!(manager.worlds_by_template(1).len(), 2);
}

#[test]
fn get_worlds_by_template_no_matching_worlds_returns_empty_list() {
    let manager = manager_with_worlds(Vec::new());
    assert!(manager.worlds_by_template(999).is_empty());
}

// ----- RemoveWorld -----

#[test]
fn remove_world_existing_world_removes_world() {
    let manager = manager_with_worlds(vec![world_instance(
        create_world_template(1, "test_world"),
        1,
    )]);
    manager.remove_world(1);
    assert!(manager.world(1).is_none());
}

#[test]
fn remove_world_non_existing_world_does_not_throw() {
    let manager = manager_with_worlds(Vec::new());
    // C# logs and returns; the Rust port reports `false`.
    assert!(!manager.remove_world(999));
}

// ----- GetCharacterById -----

#[test]
fn get_character_by_id_existing_character_returns_character() {
    let manager = create_world_manager();
    manager.try_add_character(&character(100, 50, "TestPlayer"));
    let result = manager.character_by_id(50).expect("character");
    assert_eq!(result.data().unit.name, "TestPlayer");
}

#[test]
fn get_character_by_id_non_existing_character_returns_null() {
    let manager = create_world_manager();
    assert!(manager.character_by_id(999).is_none());
}

// ----- GetAllNpcsFromWorld / GetAllDoodadsFromWorld / GetAllSlavesFromWorld -----

#[test]
fn get_all_npcs_from_world_existing_world_returns_npcs() {
    let manager = manager_with_worlds(vec![world_instance(
        create_world_template(1, "test_world"),
        1,
    )]);
    // Empty because no NPCs were added.
    assert!(manager.all_npcs_from_world(1).is_empty());
}

#[test]
fn get_all_npcs_from_world_non_existing_world_returns_empty_list() {
    let manager = manager_with_worlds(Vec::new());
    assert!(manager.all_npcs_from_world(999).is_empty());
}

#[test]
fn get_all_doodads_from_world_existing_world_returns_doodads() {
    let manager = manager_with_worlds(vec![world_instance(
        create_world_template(1, "test_world"),
        1,
    )]);
    assert!(manager.all_doodads_from_world(1).is_empty());
}

#[test]
fn get_all_doodads_from_world_non_existing_world_returns_empty_list() {
    let manager = manager_with_worlds(Vec::new());
    assert!(manager.all_doodads_from_world(999).is_empty());
}

#[test]
fn get_all_slaves_from_world_existing_world_returns_slaves() {
    let manager = manager_with_worlds(vec![world_instance(
        create_world_template(1, "test_world"),
        1,
    )]);
    assert!(manager.all_slaves_from_world(1).is_empty());
}

#[test]
fn get_all_slaves_from_world_non_existing_world_returns_empty_list() {
    let manager = manager_with_worlds(Vec::new());
    assert!(manager.all_slaves_from_world(999).is_empty());
}

// ----- IsSnowing -----

#[test]
fn is_snowing_set_and_get_returns_correct_value() {
    let manager = create_world_manager();
    manager.is_snowing.store(true, Ordering::Relaxed);
    assert!(manager.is_snowing.load(Ordering::Relaxed));
}

#[test]
fn is_snowing_default_value_returns_false() {
    let manager = create_world_manager();
    assert!(!manager.is_snowing.load(Ordering::Relaxed));
}

// ----- AreaShape -----

#[test]
fn get_area_shape_by_id_existing_shape_returns_shape() {
    let shape = AreaShape {
        id: 1,
        kind: AREA_SHAPE_SPHERE,
        value1: 100.0,
        value2: 0.0,
        value3: 0.0,
    };
    let data = AreaTriggerData {
        shapes: HashMap::from([(1, shape)]),
        ..Default::default()
    };
    let result = data.get_area_shape_by_id(1).expect("shape");
    assert_eq!(result.id, 1);
    assert_eq!(result.kind, AREA_SHAPE_SPHERE);
}

#[test]
fn get_area_shape_by_id_non_existing_shape_returns_null() {
    let data = AreaTriggerData::default();
    assert!(data.get_area_shape_by_id(999).is_none());
}

// ----- GetTargetOrSelf -----

#[test]
fn get_target_or_self_with_target_name_returns_target() {
    let manager = create_world_manager();
    let source = character(100, 1, "SourcePlayer");
    manager.try_add_character(&character(200, 2, "TargetPlayer"));
    let (result, first_non_name_argument) = manager.target_or_self(&source, Some("TargetPlayer"));
    assert_eq!(result.data().unit.name, "TargetPlayer");
    assert_eq!(first_non_name_argument, 1);
}

#[test]
fn get_target_or_self_with_non_existing_target_name_returns_self() {
    let manager = create_world_manager();
    let source = character(100, 1, "SourcePlayer");
    let (result, first_non_name_argument) = manager.target_or_self(&source, Some("NonExisting"));
    assert_eq!(result.data().unit.name, "SourcePlayer");
    assert_eq!(first_non_name_argument, 0);
}

#[test]
fn get_target_or_self_with_null_target_name_returns_self() {
    let manager = create_world_manager();
    let source = character(100, 1, "SourcePlayer");
    let (result, first_non_name_argument) = manager.target_or_self(&source, None);
    assert_eq!(result.data().unit.name, "SourcePlayer");
    assert_eq!(first_non_name_argument, 0);
}

// ----- Edge cases -----

#[test]
fn get_world_max_uint_id_returns_null() {
    let manager = manager_with_worlds(Vec::new());
    assert!(manager.world(u32::MAX).is_none());
}

#[test]
fn get_character_empty_name_returns_null() {
    let manager = create_world_manager();
    assert!(manager.character_by_name("").is_none());
}

#[test]
fn get_zone_id_negative_coordinates_returns_zero() {
    let templates = WorldTemplates::default();
    let t = create_world_template(1, "main_world");
    assert_eq!(templates.get_zone_id(Some(&t), -100.0, -100.0), 0);
}

#[test]
fn try_remove_character_zero_id_returns_false() {
    let manager = create_world_manager();
    assert!(!manager.try_remove_character(0));
}

// ----- Multiple operations -----

#[test]
fn multiple_operations_add_and_remove_character_character_removed() {
    let manager = create_world_manager();
    let c = character(100, 1, "TestPlayer");
    assert!(manager.try_add_character(&c));
    assert!(manager.character_by_obj_id(100).is_some());
    assert!(manager.try_remove_character(100));
    assert!(manager.character_by_obj_id(100).is_none());
}

#[test]
fn multiple_operations_add_multiple_worlds_all_worlds_accessible() {
    let manager = manager_with_worlds(
        (1..=5)
            .map(|i| world_instance(create_world_template(i, &format!("world_{i}")), i))
            .collect(),
    );
    assert_eq!(manager.worlds().len(), 5);
    for i in 1..=5 {
        assert!(manager.world(i).is_some());
    }
}

#[test]
fn multiple_operations_remove_all_worlds_worlds_empty() {
    let manager = manager_with_worlds(vec![world_instance(
        create_world_template(1, "test_world"),
        1,
    )]);
    manager.remove_world(1);
    assert!(manager.worlds().is_empty());
}

// ----- Constants -----

#[test]
fn constants_cell_size_returns_correct_value() {
    assert_eq!(CELL_SIZE, 1024);
}

#[test]
fn constants_region_size_returns_correct_value() {
    assert_eq!(REGION_SIZE, 64);
}

#[test]
fn constants_sectors_per_cell_returns_correct_value() {
    assert_eq!(SECTORS_PER_CELL, 16); // 1024 / 64 = 16
}

#[test]
fn constants_default_combat_timeout_returns_correct_value() {
    // `WorldManager.DefaultCombatTimeout = 15f` (seconds) is kept in milliseconds.
    assert_eq!(
        crate::systems::skills::relation::DEFAULT_COMBAT_TIMEOUT_MS,
        15_000
    );
}

// ----- Initialize -----

/// `Initialize` subscribes `ActiveRegionTick` (1 s, `useAsync: true`). The Rust port runs it as
/// a 1 s Tokio interval task (`spawn_manager::start_active_region_tick`), i.e. off the tick
/// thread like the C# async subscriber, started once per server.
#[tokio::test]
async fn initialize_subscribes_active_region_tick_as_async() {
    use crate::systems::npcs::spawn_manager;
    let server = Arc::new(crate::test_support::test_server());
    assert!(!server.systems.npcs.spawns.active_tick_started());
    spawn_manager::start_active_region_tick(&server);
    assert!(server.systems.npcs.spawns.active_tick_started());
    // A second Initialize does not start a second ticker.
    spawn_manager::start_active_region_tick(&server);
    assert!(server.systems.npcs.spawns.active_tick_started());
    // Not on the synchronous tick-manager thread.
    assert_eq!(server.systems.services.tick.on_tick.subscriber_count(), 0);
}
