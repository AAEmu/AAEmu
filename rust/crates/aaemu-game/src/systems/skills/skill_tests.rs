//! Port of AAEmu.UnitTests/Game/Models/Game/Skills/SkillTests.cs
//!
//! `Skill` is `SkillRun` (shared as `SkillRef = Arc<SkillRun>`): `Id`, `Template` and `Level`
//! are fixed at construction, the per-use members (`TlId`, `Cancelled`, `HitTypes`,
//! `CastTimeMultiplier`, ...) live in `state()`. A Rust skill always has a template: the C#
//! `new Skill()` / `new Skill(null, null)` (null template) is a skill of the default (id 0)
//! template. `new Skill(template, owner)` is `skill::new_skill_for`; the C# `StubUnit` overriding
//! `GetAbLevel` is an NPC with `ab_level_override` (non-character units use their level or that
//! override as ability level).

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

use crate::data::GameData;
use crate::models::doodad::Doodad;
use crate::models::npc::Npc;
use crate::test_support::test_server_with;
use crate::world::{Entity, WorldObject};

use super::data::SkillTemplate;
use super::skill::{new_skill_for, use_skill};
use super::state::{SkillRef, SkillRun};
use super::types::{skill_result, SkillCastTarget, SkillCaster, SkillHitType};

/// `AbilityType.Fight`.
const FIGHT: u8 = 1;

/// `new Skill()`.
fn default_skill() -> SkillRef {
    SkillRun::new(Arc::new(SkillTemplate::default()), 0)
}

/// An NPC whose `GetAbLevel` is `level` (C# `StubUnit`).
fn stub_owner(level: u8) -> WorldObject {
    let mut npc = Npc::new(1);
    npc.unit.skill_state.ab_level_override = Some(level);
    WorldObject::from(&Entity::new(0x400, npc))
}

// ---- Constructor ----

#[test]
fn constructor_default_creates_skill_with_default_values() {
    let skill = default_skill();
    assert_eq!(skill.id, 0);
    assert_eq!(skill.template.id, 0); // C#: Template null
    assert_eq!(skill.level, 0);
    assert_eq!(skill.tl_id(), 0);
    assert!(skill.state().hit_types.is_empty());
}

#[tokio::test]
async fn constructor_with_template_and_owner_creates_skill_with_values() {
    let server = test_server_with(GameData::empty());
    let template = Arc::new(SkillTemplate {
        id: 100,
        ability_id: FIGHT,
        ability_level: 1,
        level_step: 1,
        ..SkillTemplate::default()
    });
    let skill = new_skill_for(&server, template.clone(), Some(&stub_owner(5)));
    assert_eq!(skill.id, 100);
    assert!(Arc::ptr_eq(&skill.template, &template));
    assert_eq!(skill.level, 5); // (5 - 1) / 1 + 1
}

#[tokio::test]
async fn constructor_with_template_and_no_owner_sets_level_to_one() {
    let server = test_server_with(GameData::empty());
    let template = Arc::new(SkillTemplate {
        id: 200,
        ..SkillTemplate::default()
    });
    let skill = new_skill_for(&server, template, None);
    assert_eq!(skill.id, 200);
    assert_eq!(skill.level, 1);
}

// ---- Properties ----

#[test]
fn id_set_and_get_returns_correct_value() {
    let mut skill = default_skill();
    Arc::get_mut(&mut skill).unwrap().id = 123;
    assert_eq!(skill.id, 123);
}

#[test]
fn level_set_and_get_returns_correct_value() {
    let mut skill = default_skill();
    Arc::get_mut(&mut skill).unwrap().level = 5;
    assert_eq!(skill.level, 5);
}

#[test]
fn tl_id_set_and_get_returns_correct_value() {
    let skill = default_skill();
    skill.state().tl_id = 456;
    assert_eq!(skill.tl_id(), 456);
}

#[test]
fn cast_time_multiplier_set_and_get_returns_correct_value() {
    let skill = default_skill();
    assert_eq!(skill.state().cast_time_multiplier, 1.0); // C# default 1f
    skill.state().cast_time_multiplier = 1.5;
    assert_eq!(skill.state().cast_time_multiplier, 1.5);
}

// ---- HitTypes ----

#[test]
fn hit_types_add_and_retrieve_returns_correct_value() {
    let skill = default_skill();
    skill.state().hit_types.insert(1, SkillHitType::MeleeHit);
    assert_eq!(skill.hit_type(1), Some(SkillHitType::MeleeHit));
}

// ---- Cancelled ----

#[test]
fn cancelled_set_and_get_returns_correct_value() {
    let skill = default_skill();
    skill.state().cancelled = true;
    assert!(skill.cancelled());
}

// ---- Callback ----

#[test]
fn callback_set_and_get_returns_correct_value() {
    let skill = default_skill();
    let called = Arc::new(AtomicBool::new(false));
    let flag = called.clone();
    *skill.callback.lock().unwrap() = Some(Box::new(move || flag.store(true, Ordering::SeqCst)));
    skill.invoke_callback();
    assert!(called.load(Ordering::SeqCst));
}

// ---- Use ----

/// C#: a `BaseUnit` that is not a `Unit` is rejected. Rust: a doodad (the only non-unit
/// `BaseUnit` kind).
#[tokio::test]
async fn use_with_non_unit_caster_returns_invalid_source() {
    let server = test_server_with(GameData::empty());
    let skill = default_skill();
    let caster = WorldObject::from(&Entity::new(0x500, Doodad::new(1)));
    let mut target = SkillCastTarget::Unit(0);
    let (result, _) = use_skill(
        &server,
        &skill,
        &caster,
        SkillCaster::Unit(0),
        &mut target,
        None,
        false,
    );
    assert_eq!(result, skill_result::INVALID_SOURCE);
}

// ---- Edge cases ----

#[test]
fn constructor_with_null_template_does_not_throw() {
    let skill = default_skill();
    assert_eq!(skill.id, 0);
}

#[tokio::test]
async fn level_with_zero_ability_level_calculates_correctly() {
    let server = test_server_with(GameData::empty());
    let template = Arc::new(SkillTemplate {
        id: 100,
        ability_id: FIGHT,
        ability_level: 0,
        level_step: 2,
        ..SkillTemplate::default()
    });
    let skill = new_skill_for(&server, template, Some(&stub_owner(10)));
    assert_eq!(skill.level, 6); // (10 - 0) / 2 + 1
}
