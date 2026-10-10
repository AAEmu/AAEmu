//! Port of AAEmu.UnitTests/Game/Models/Game/Units/UnitTests.cs
//!
//! `Unit` is `UnitBase` (the members shared by every unit kind), `Character` / `Npc` embed it.
//! `ObjId` lives on the world handle (`Entity<D>::obj_id`, fixed at creation), so the ObjId
//! "set and get" tests create the handle with the value. `Name` is a `String` (C# `null` is the
//! empty string).

// The C# tests set properties on a default instance and read them back.
#![allow(clippy::field_reassign_with_default)]

use super::character::Character;
use super::npc::Npc;
use super::unit::{UnitAttribute, UnitBase};
use super::unit_model::UnitCustomModelParams;
use crate::systems::skills::stats::StatKind;
use crate::world::Entity;

// ---- UnitAttribute ----

/// C#: no `UnitAttribute` is bound (`[UnitAttribute]`) to more than one property of `Unit`,
/// `Character` or `Npc`. The Rust binding is the single function
/// `StatKind::from_unit_attribute` (shared by every unit kind), so each attribute resolves to at
/// most one getter by construction; this checks every attribute value resolves without
/// ambiguity and that the only getter carrying two attributes is the C# one
/// (`MeleeAccuracy`: `MeleeAntiMiss` + `MeleeAntiMissMul`).
#[test]
fn no_duplicate_attributes() {
    let mut by_kind: std::collections::HashMap<StatKind, Vec<UnitAttribute>> =
        std::collections::HashMap::new();
    for v in 0..=u8::MAX {
        let Some(attr) = UnitAttribute::from_u8(v) else {
            continue;
        };
        if let Some(kind) = StatKind::from_unit_attribute(attr) {
            by_kind.entry(kind).or_default().push(attr);
        }
    }
    assert!(!by_kind.is_empty());
    for (kind, attrs) in &by_kind {
        if *kind == StatKind::MeleeAccuracy {
            assert_eq!(
                attrs,
                &vec![
                    UnitAttribute::MeleeAntiMiss,
                    UnitAttribute::MeleeAntiMissMul
                ]
            );
        } else {
            assert_eq!(attrs.len(), 1, "{kind:?} is bound to {attrs:?}");
        }
    }
}

// ---- Unit model ----

#[test]
fn unit_default_constructor_creates_instance() {
    let unit = UnitBase::default();
    assert_eq!(unit.template_id, 0);
}

#[test]
fn unit_obj_id_set_and_get_returns_correct_value() {
    let unit = Entity::new(12345, UnitBase::default());
    assert_eq!(unit.obj_id, 12345);
}

#[test]
fn unit_template_id_set_and_get_returns_correct_value() {
    let mut unit = UnitBase::default();
    unit.template_id = 100;
    assert_eq!(unit.template_id, 100);
}

#[test]
fn unit_name_set_and_get_returns_correct_value() {
    let mut unit = UnitBase::default();
    unit.name = "TestUnit".into();
    assert_eq!(unit.name, "TestUnit");
}

#[test]
fn unit_level_set_and_get_returns_correct_value() {
    let mut unit = UnitBase::default();
    unit.level = 50;
    assert_eq!(unit.level, 50u8);
}

#[test]
fn unit_hp_set_and_get_returns_correct_value() {
    let mut unit = UnitBase::default();
    unit.hp = 1000;
    unit.max_hp = 2000;
    assert_eq!(unit.hp, 1000);
    assert_eq!(unit.max_hp, 2000);
}

#[test]
fn unit_mp_set_and_get_returns_correct_value() {
    let mut unit = UnitBase::default();
    unit.mp = 500;
    unit.max_mp = 1000;
    assert_eq!(unit.mp, 500);
    assert_eq!(unit.max_mp, 1000);
}

// ---- Character ----

#[test]
fn character_default_constructor_creates_instance() {
    let character = Character::new(0, 0, UnitCustomModelParams::default());
    assert_eq!(character.account_id, 0);
}

#[test]
fn character_account_id_set_and_get_returns_correct_value() {
    let mut character = Character::new(0, 0, UnitCustomModelParams::default());
    character.account_id = 45678;
    assert_eq!(character.account_id, 45678);
}

#[test]
fn character_level_set_and_get_returns_correct_value() {
    let mut character = Character::new(0, 0, UnitCustomModelParams::default());
    character.unit.level = 25;
    assert_eq!(character.unit.level, 25u8);
}

#[test]
fn character_obj_id_extreme_values() {
    for value in [0u32, u32::MAX] {
        let character = Entity::new(
            value,
            Character::new(0, 0, UnitCustomModelParams::default()),
        );
        assert_eq!(character.obj_id, value);
    }
}

// ---- Npc ----

#[test]
fn npc_default_constructor_creates_instance() {
    let npc = Npc::new(0);
    assert_eq!(npc.unit.template_id, 0);
}

// ---- Edge cases ----

#[test]
fn unit_obj_id_extreme_values() {
    for value in [0u32, u32::MAX] {
        let unit = Entity::new(value, UnitBase::default());
        assert_eq!(unit.obj_id, value);
    }
}

#[test]
fn unit_level_valid_values() {
    for level in [0u8, 1, 100] {
        let mut unit = UnitBase::default();
        unit.level = level;
        assert_eq!(unit.level, level);
    }
}

#[test]
fn unit_default_values_are_zero_or_null() {
    let unit = Entity::new(0, UnitBase::default());
    assert_eq!(unit.obj_id, 0);
    let unit = unit.data();
    assert_eq!(unit.template_id, 0);
    assert!(unit.name.is_empty()); // C#: null
    assert_eq!(unit.level, 0);
    assert_eq!(unit.hp, 0);
    assert_eq!(unit.max_hp, 0);
    assert_eq!(unit.mp, 0);
    assert_eq!(unit.max_mp, 0);
}
