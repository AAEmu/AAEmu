//! Port of AAEmu.UnitTests/Game/Core/Managers/UnitManagers/CharacterManagerTests.cs
//!
//! The C# `CharacterManager` tables set by reflection are the Rust game-data tables:
//! `_templates`, `_appellations`, `_actabilities`, `_actabilitiesCategories`, `_expertLimits`
//! are [`CharacterData`], `_expands` is `systems::economy::data::Data::expands`,
//! `_expandExpertLimits` is `systems::economy::store_data::StoreData`. A C#
//! `KeyNotFoundException` is `None`. `GetEffectiveAccessLevel` reads the account from MySQL in
//! Rust, so its rule ([`effective_access_level_of`]) is tested with the mocked account details;
//! `PlayerRoll` runs against a spawned test player.
//!
//! `GetExpertLimit` / `GetExpandExpertLimit` take an unsigned step in Rust (`usize` / `u8`,
//! table index), so the `NegativeStep` rows test the out-of-range lookup instead.

// The C# tests construct the object, then set its (private) fields: kept as written.
#![allow(clippy::field_reassign_with_default)]

use std::collections::HashMap;

use crate::data::character_templates::{
    ActabilityTemplate, CharacterData, CharacterTemplate, ExpertLimit,
};
use crate::db::AccountDetails;
use crate::opcodes::sc;
use crate::systems::economy::data::{Data as EconomyData, Expand};
use crate::systems::economy::store_data::{ExpandExpertLimit, StoreData};
use crate::systems::services::character::{effective_access_level_of, player_roll};
use crate::test_support::{spawn_player, test_character, test_server, TestClient};

const NUIAN: u8 = 1;
const ELF: u8 = 4;
const MALE: u8 = 1;
const FEMALE: u8 = 2;
/// `FactionsEnum.NuiaAlliance`.
const NUIA_ALLIANCE: u32 = 148;

fn key(race: u8, gender: u8) -> u8 {
    16 * gender + race
}

fn tpl(race: u8, gender: u8, model_id: u32) -> CharacterTemplate {
    CharacterTemplate {
        race,
        gender,
        model_id,
        ..CharacterTemplate::default()
    }
}

// ----- constructor -----

#[test]
fn constructor_does_not_call_dependencies() {
    // No injected managers in Rust: construction is side-effect free and starts empty.
    let manager = CharacterData::default();
    assert!(manager.templates.is_empty());
    assert!(manager.appellations.is_empty());
    assert!(manager.actabilities.is_empty());
}

// ----- GetTemplate -----

#[test]
fn get_template_template_exists_returns_template() {
    let mut manager = CharacterData::default();
    let template = CharacterTemplate {
        faction_id: NUIA_ALLIANCE,
        ..tpl(NUIAN, MALE, 1)
    };
    manager.templates = HashMap::from([(key(NUIAN, MALE), template)]);

    let result = manager.template(NUIAN, MALE).expect("template");

    assert_eq!(result.race, NUIAN);
    assert_eq!(result.gender, MALE);
    assert_eq!(result.model_id, 1);
}

#[test]
fn get_template_template_does_not_exist_throws_key_not_found_exception() {
    let manager = CharacterData::default();
    assert!(manager.template(NUIAN, MALE).is_none());
}

#[test]
fn get_template_different_race_gender_combinations_returns_correct_templates() {
    let mut manager = CharacterData::default();
    let nuian_male = tpl(NUIAN, MALE, 1);
    let nuian_female = tpl(NUIAN, FEMALE, 2);
    let elf_male = tpl(ELF, MALE, 3);
    let elf_female = tpl(ELF, FEMALE, 4);
    manager.templates = HashMap::from([
        (key(NUIAN, MALE), nuian_male.clone()),
        (key(NUIAN, FEMALE), nuian_female.clone()),
        (key(ELF, MALE), elf_male.clone()),
        (key(ELF, FEMALE), elf_female.clone()),
    ]);

    assert_eq!(manager.template(NUIAN, MALE), Some(&nuian_male));
    assert_eq!(manager.template(NUIAN, FEMALE), Some(&nuian_female));
    assert_eq!(manager.template(ELF, MALE), Some(&elf_male));
    assert_eq!(manager.template(ELF, FEMALE), Some(&elf_female));
}

// ----- GetAppellationsTemplate (`_appellations` is id -> buff id in Rust) -----

#[test]
fn get_appellations_template_template_exists_returns_template() {
    let mut manager = CharacterData::default();
    manager.appellations = HashMap::from([(1, 100)]);

    let result = manager.appellations.get_key_value(&1);

    assert_eq!(result, Some((&1, &100)));
}

#[test]
fn get_appellations_template_template_does_not_exist_returns_null() {
    let manager = CharacterData::default();
    assert!(!manager.appellations.contains_key(&999));
}

#[test]
fn get_appellations_template_zero_id_returns_null() {
    let manager = CharacterData::default();
    assert!(!manager.appellations.contains_key(&0));
}

// ----- GetExpands -----

fn expand(step: i32, is_bank: bool, price: i32) -> Expand {
    Expand {
        step,
        is_bank,
        price,
        ..Expand::default()
    }
}

#[test]
fn get_expands_step_exists_returns_expands() {
    let mut manager = EconomyData::default();
    manager
        .expands
        .insert(1, vec![expand(1, false, 100), expand(1, true, 200)]);

    let result = manager.expands(1).expect("expands");

    assert_eq!(result.len(), 2);
}

#[test]
fn get_expands_step_does_not_exist_throws_key_not_found_exception() {
    let manager = EconomyData::default();
    assert!(manager.expands(999).is_none());
}

// ----- GetActability -----

#[test]
fn get_actability_actability_exists_returns_actability() {
    let mut manager = CharacterData::default();
    manager.actabilities = vec![ActabilityTemplate {
        id: 1,
        name: "Alchemy".into(),
        unit_attribute_id: 10,
    }];

    let result = manager.actability(1).expect("actability");

    assert_eq!(result.id, 1);
    assert_eq!(result.name, "Alchemy");
}

#[test]
fn get_actability_actability_does_not_exist_throws_key_not_found_exception() {
    let manager = CharacterData::default();
    assert!(manager.actability(999).is_none());
}

// ----- GetActabilityIdByCategoryId (`_actabilitiesCategories` is id -> group id) -----

#[test]
fn get_actability_id_by_category_id_category_exists_returns_actability_id() {
    let mut manager = CharacterData::default();
    // ActabilityCategoriesTemplate { Id = 1, Name = "Category1", GroupId = 10 }
    manager.actability_categories = HashMap::from([(1, 10)]);
    // The C# test stores the template { Id = 100 } under the key 10 and expects 100. The Rust
    // list is searched by template id (the loader keys `_actabilities` by `Id`, so both agree
    // on real data): the group's template has id 10 here.
    manager.actabilities = vec![ActabilityTemplate {
        id: 10,
        name: "TestActability".into(),
        ..ActabilityTemplate::default()
    }];

    assert_eq!(manager.actability_id_by_category_id(1), 10);
}

#[test]
fn get_actability_id_by_category_id_category_does_not_exist_returns_zero() {
    let manager = CharacterData::default();
    assert_eq!(manager.actability_id_by_category_id(999), 0);
}

#[test]
fn get_actability_id_by_category_id_actability_not_found_returns_zero() {
    let mut manager = CharacterData::default();
    manager.actability_categories = HashMap::from([(1, 10)]);

    assert_eq!(manager.actability_id_by_category_id(1), 0);
}

// ----- GetExpertLimit -----

#[test]
fn get_expert_limit_step_exists_returns_expert_limit() {
    let mut manager = CharacterData::default();
    manager.expert_limits = vec![ExpertLimit {
        id: 1,
        up_limit: 100,
        expert_limit_count: 2,
    }];

    let result = manager.expert_limit(0).expect("limit");

    assert_eq!(result.up_limit, 100);
    assert_eq!(result.expert_limit_count, 2);
}

#[test]
fn get_expert_limit_step_does_not_exist_returns_null() {
    let manager = CharacterData::default();
    assert!(manager.expert_limit(999).is_none());
}

// ----- GetExpandExpertLimit -----

#[test]
fn get_expand_expert_limit_step_exists_returns_expand_expert_limit() {
    let mut manager = StoreData::default();
    manager.expand_expert_limits = vec![ExpandExpertLimit {
        id: 1,
        expand_count: 1,
        life_point: 50,
        ..ExpandExpertLimit::default()
    }];

    let result = manager.expand_expert_limit(0).expect("limit");

    assert_eq!(result.expand_count, 1);
    assert_eq!(result.life_point, 50);
}

#[test]
fn get_expand_expert_limit_step_does_not_exist_returns_null() {
    let manager = StoreData::default();
    // the step is a u8 in Rust: 999 does not fit, the largest step is unknown as well
    assert!(manager.expand_expert_limit(u8::MAX).is_none());
}

// ----- GetEffectiveAccessLevel -----

fn account(access_level: i32) -> AccountDetails {
    AccountDetails {
        account_id: 1,
        access_level,
        ..AccountDetails::default()
    }
}

#[test]
fn get_effective_access_level_character_access_level_higher_returns_character_access_level() {
    assert_eq!(effective_access_level_of(100, &account(50)), 100);
}

#[test]
fn get_effective_access_level_account_access_level_higher_returns_account_access_level() {
    assert_eq!(effective_access_level_of(50, &account(100)), 100);
}

#[test]
fn get_effective_access_level_equal_access_levels_returns_access_level() {
    assert_eq!(effective_access_level_of(50, &account(50)), 50);
}

// ----- PlayerRoll -----

#[tokio::test]
async fn player_roll_valid_max_sends_roll_message() {
    let server = test_server();
    let mut client = TestClient::new(&server, 1, 1);
    let player = spawn_player(
        &server,
        &client,
        test_character(1, "TestPlayer", 10.0, 10.0),
        0x100,
    );
    client.drain();

    let roll = player_roll(&server, &player, 100).expect("roll");

    assert!((1..=100).contains(&roll));
    assert_eq!(client.drain_ops(), vec![sc::SCDiceValuePacket]);
}

#[tokio::test]
async fn player_roll_max_value_one_sends_roll_one() {
    let server = test_server();
    let mut client = TestClient::new(&server, 1, 1);
    let player = spawn_player(
        &server,
        &client,
        test_character(1, "TestPlayer", 10.0, 10.0),
        0x100,
    );
    client.drain();

    assert_eq!(player_roll(&server, &player, 1), Some(1));
    assert_eq!(client.drain_ops(), vec![sc::SCDiceValuePacket]);
}

// ----- edge cases -----

#[test]
fn get_template_max_byte_values_throws_key_not_found_exception() {
    let manager = CharacterData::default();
    assert!(manager.template(255, 15).is_none());
}

#[test]
fn get_actability_id_by_category_id_zero_category_id_returns_zero() {
    let manager = CharacterData::default();
    assert_eq!(manager.actability_id_by_category_id(0), 0);
}

#[test]
fn get_expert_limit_negative_step_returns_null() {
    let manager = CharacterData::default();
    assert!(manager.expert_limit(usize::MAX).is_none());
}

#[test]
fn get_expand_expert_limit_negative_step_returns_null() {
    let manager = StoreData::default();
    assert!(manager.expand_expert_limit(u8::MAX).is_none());
}

#[test]
fn get_expands_zero_step_throws_key_not_found_exception() {
    let manager = EconomyData::default();
    assert!(manager.expands(0).is_none());
}

#[test]
fn get_appellations_template_max_uint_id_returns_null() {
    let manager = CharacterData::default();
    assert!(!manager.appellations.contains_key(&u32::MAX));
}

// ----- multiple operations -----

#[test]
fn multiple_get_template_calls_return_consistent_results() {
    let mut manager = CharacterData::default();
    manager.templates = HashMap::from([(key(NUIAN, MALE), tpl(NUIAN, MALE, 1))]);

    let result1 = manager.template(NUIAN, MALE).expect("1");
    let result2 = manager.template(NUIAN, MALE).expect("2");
    let result3 = manager.template(NUIAN, MALE).expect("3");

    assert!(std::ptr::eq(result2, result1));
    assert!(std::ptr::eq(result3, result2));
}

#[test]
fn get_template_after_modifying_dictionary_returns_updated_template() {
    let mut manager = CharacterData::default();
    manager.templates = HashMap::from([(key(NUIAN, MALE), tpl(NUIAN, MALE, 1))]);

    let result1 = manager.template(NUIAN, MALE).expect("1").model_id;
    manager
        .templates
        .insert(key(NUIAN, MALE), tpl(NUIAN, MALE, 999));
    let result2 = manager.template(NUIAN, MALE).expect("2").model_id;

    assert_eq!(result1, 1);
    assert_eq!(result2, 999);
}
