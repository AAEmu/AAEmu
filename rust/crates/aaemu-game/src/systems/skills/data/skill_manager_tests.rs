//! Port of AAEmu.UnitTests/Game/Core/Managers/SkillManagerTests.cs
//!
//! `SkillManager`'s tables are split between `systems::skills::Data` (skills, buffs, effects,
//! tags, modifiers, combat buffs, triggers, reagents / products) and the phase-2
//! `data::skills::SkillData` (default skills, common skills, passive buffs). The C# tests fill
//! the private dictionaries by reflection; here the (public) fields are filled directly. There
//! are no injected `IAnimationManager` / `IPlotManager`: the animation and plot tables are
//! fields of `Data` loaded by `Data::load`.

use std::collections::HashMap;
use std::sync::Arc;

use super::effects::{EffectKind, EffectTemplate, EffectType, HealEffect, SpawnGimmickEffect};
use super::{BuffTemplate, BuffTriggerTemplate, Data, SkillItemAmount, SkillTemplate};
use crate::data::skills::{DefaultSkill, PassiveBuffTemplate, SkillData};
use crate::models::unit::{CombatBuffTemplate, SkillAttribute, SkillModifier, UnitModifierType};
use crate::systems::skills::tests::damage_effect;

fn skills_with(templates: Vec<SkillTemplate>) -> Data {
    let mut d = Data::default();
    for t in templates {
        d.skills.insert(t.id, Arc::new(t));
    }
    d
}

// ---- Constructor ----

/// C#: constructing `SkillManager` with mocked deps calls neither of them
/// (`Mock.VerifyNoOtherCalls`). Rust: the default tables are empty and not loaded.
#[test]
fn constructor_with_mocked_dependencies_does_not_throw() {
    let d = Data::default();
    assert!(!d.is_loaded());
    assert!(d.skills.is_empty() && d.buffs.is_empty() && d.effects.is_empty());
    assert!(d.plots.plots.is_empty() && d.anims.anims.is_empty());
    let s = SkillData::default();
    assert!(s.default_skills.is_empty() && s.common_skills.is_empty());
}

// ---- GetSkillTemplate ----

#[test]
fn get_skill_template_returns_skill_when_exists() {
    let d = skills_with(vec![SkillTemplate {
        id: 1,
        cooldown_time: 5000,
        casting_time: 1000,
        ..SkillTemplate::default()
    }]);
    let result = d.skill(1).expect("skill");
    assert_eq!(result.id, 1);
}

// ---- GetBuffTemplate ----

#[test]
fn get_buff_template_returns_buff_when_exists() {
    let mut d = Data::default();
    d.buffs.insert(
        1,
        Arc::new(BuffTemplate {
            id: 1,
            duration: 30000,
            ..BuffTemplate::default()
        }),
    );
    let result = d.buff(1).expect("buff");
    assert_eq!(result.id, 1);
    assert_eq!(result.duration, 30000);
}

// ---- GetPassiveBuffTemplate ----

#[test]
fn get_passive_buff_template_returns_passive_buff_when_exists() {
    let mut s = SkillData::default();
    s.passive_buffs.insert(
        1,
        PassiveBuffTemplate {
            id: 1,
            buff_id: 100,
            level: 5,
            ..PassiveBuffTemplate::default()
        },
    );
    let result = s.passive_buff(1).expect("passive buff");
    assert_eq!(result.id, 1);
    assert_eq!(result.buff_id, 100);
}

// ---- GetEffectTemplate ----

fn heal_effect(id: u32) -> HealEffect {
    HealEffect {
        id,
        use_fixed_heal: false,
        fixed_min: 0,
        fixed_max: 0,
        use_level_heal: false,
        level_md: 0.0,
        level_va_start: 0,
        level_va_end: 0,
        percent: false,
        use_charged_buff: false,
        charged_buff_id: 0,
        charged_mul: 0.0,
        slave_applicable: false,
        ignore_heal_aggro: false,
        dps_multiplier: 0.0,
        actability_group_id: 0,
        actability_step: 0,
        actability_mul: 0.0,
        actability_add: 0.0,
    }
}

#[test]
fn get_effect_template_returns_effect_when_exists() {
    let mut d = Data::default();
    d.effect_types.insert(
        1,
        EffectType {
            id: 1,
            actual_id: 1,
            actual_type: "DamageEffect".into(),
        },
    );
    d.effects.insert(
        EffectKind::DamageEffect,
        HashMap::from([(1, EffectTemplate::Damage(Box::new(damage_effect(1, 0))))]),
    );
    let (kind, result) = d.effect_by_type_id(1).expect("effect");
    assert_eq!(kind, EffectKind::DamageEffect);
    assert_eq!(result.id(), 1);
}

#[test]
fn get_effect_template_with_type_returns_effect_when_exists() {
    let mut d = Data::default();
    d.effects.insert(
        EffectKind::HealEffect,
        HashMap::from([(1, EffectTemplate::Heal(heal_effect(1)))]),
    );
    let result = d.effect_by_name(1, "HealEffect").expect("effect");
    assert_eq!(result.id(), 1);
}

#[test]
fn get_effect_template_with_type_returns_null_for_non_existent_type() {
    let d = Data::default();
    assert!(d.effect_by_name(1, "NonExistentEffect").is_none());
}

// ---- IsDefaultSkill / IsCommonSkill ----

#[test]
fn is_default_skill_returns_true_when_skill_is_default() {
    let mut s = SkillData::default();
    s.default_skills.push(DefaultSkill {
        skill_id: 1,
        slot: 0,
        add_to_slot: false,
    });
    assert!(s.is_default_skill(1));
}

#[test]
fn is_default_skill_returns_false_when_skill_is_not_default() {
    let s = SkillData::default();
    assert!(!s.is_default_skill(1));
}

#[test]
fn is_common_skill_returns_true_when_skill_is_common() {
    let mut s = SkillData::default();
    s.common_skills = vec![1, 2, 3];
    assert!(s.is_common_skill(1));
}

#[test]
fn is_common_skill_returns_false_when_skill_is_not_common() {
    let mut s = SkillData::default();
    s.common_skills = vec![1, 2, 3];
    assert!(!s.is_common_skill(999));
}

// ---- GetBuffTags ----

#[test]
fn get_buff_tags_returns_tags_when_exists() {
    let mut d = Data::default();
    d.buff_tags.insert(1, vec![10, 20, 30]);
    assert_eq!(d.buff_tags(1).len(), 3);
}

// ---- GetDefaultSkills ----

#[test]
fn get_default_skills_returns_skills_when_loaded() {
    let mut s = SkillData::default();
    s.default_skills.push(DefaultSkill {
        skill_id: 1,
        slot: 0,
        add_to_slot: false,
    });
    s.default_skills.push(DefaultSkill {
        skill_id: 2,
        slot: 1,
        add_to_slot: false,
    });
    assert_eq!(s.default_skills.len(), 2);
}

// ---- GetModifiersByOwnerId ----

#[test]
fn get_modifiers_by_owner_id_returns_modifiers_when_exists() {
    let mut d = Data::default();
    d.skill_modifiers.insert(
        1,
        vec![SkillModifier {
            id: 1,
            owner_id: 1,
            tag_id: 0,
            skill_attribute: SkillAttribute::ManaCost,
            unit_modifier_type: UnitModifierType::Value,
            value: 0,
            skill_id: 0,
            synergy: false,
        }],
    );
    assert_eq!(d.skill_modifiers_by_owner(1).len(), 1);
}

// ---- GetCombatBuffs ----

#[test]
fn get_combat_buffs_returns_buffs_when_exists() {
    let mut d = Data::default();
    d.combat_buffs.insert(
        1,
        vec![CombatBuffTemplate {
            id: 1,
            hit_skill_id: 0,
            hit_type: 0,
            req_skill_id: 0,
            buff_id: 100,
            buff_from_source: false,
            buff_to_source: false,
            req_buff_id: 1,
            is_heal_spell: false,
        }],
    );
    assert_eq!(d.combat_buffs_for(1).len(), 1);
}

// ---- GetSkillReagentsBySkillId / GetSkillProductsBySkillId ----

#[test]
fn get_skill_reagents_by_skill_id_returns_reagents_when_exists() {
    let d = Data {
        skill_reagents: vec![SkillItemAmount {
            id: 1,
            skill_id: 1,
            item_id: 100,
            amount: 5,
        }],
        ..Data::default()
    };
    assert_eq!(d.reagents_for(1).len(), 1);
}

#[test]
fn get_skill_products_by_skill_id_returns_products_when_exists() {
    let d = Data {
        skill_products: vec![SkillItemAmount {
            id: 1,
            skill_id: 1,
            item_id: 200,
            amount: 1,
        }],
        ..Data::default()
    };
    assert_eq!(d.products_for(1).len(), 1);
}

// ---- GetBuffTriggerTemplates ----

#[test]
fn get_buff_trigger_templates_returns_triggers_when_exists() {
    let mut d = Data::default();
    d.buff_triggers.insert(
        1,
        vec![BuffTriggerTemplate {
            id: 1,
            kind: 0,
            effect_on_source: false,
            effect: (EffectKind::DamageEffect, 0),
            use_damage_amount: false,
            use_original_source: false,
            target_buff_tag_id: 0,
            target_no_buff_tag_id: 0,
            synergy: false,
        }],
    );
    assert_eq!(d.buff_triggers_for(1).len(), 1);
}

// ---- GetSkillActAbility ----

/// `ActabilityType.None`.
const ACTABILITY_NONE: i32 = 0;
/// `ActabilityType.Alchemy`.
const ACTABILITY_ALCHEMY: i32 = 1;

#[test]
fn get_skill_act_ability_returns_none_when_skill_not_found() {
    let d = Data::default();
    assert_eq!(d.skill_actability(1), ACTABILITY_NONE);
}

#[test]
fn get_skill_act_ability_returns_act_ability_when_skill_exists() {
    let d = skills_with(vec![SkillTemplate {
        id: 1,
        actability_group_id: ACTABILITY_ALCHEMY,
        ..SkillTemplate::default()
    }]);
    assert_eq!(d.skill_actability(1), ACTABILITY_ALCHEMY);
}

// ---- GetNpSkillTemplate ----

/// `SkillUseConditionKind.None`.
const SKILL_USE_CONDITION_NONE: i32 = -1;

#[test]
fn get_np_skill_template_returns_null_when_skill_not_found() {
    let d = Data::default();
    assert!(d.np_skill(1, SKILL_USE_CONDITION_NONE).is_none());
}

#[test]
fn get_np_skill_template_returns_skill_when_valid() {
    let d = skills_with(vec![SkillTemplate {
        id: 1,
        ignore_global_cooldown: true,
        ..SkillTemplate::default()
    }]);
    let result = d.np_skill(1, SKILL_USE_CONDITION_NONE).expect("skill");
    assert_eq!(result.id, 1);
}

// ---- GetSpawnGimmickEffect ----

#[test]
fn get_spawn_gimmick_effect_returns_effect_when_found() {
    let mut d = Data::default();
    d.effects.insert(
        EffectKind::SpawnGimmickEffect,
        HashMap::from([(
            1,
            EffectTemplate::SpawnGimmick(SpawnGimmickEffect {
                id: 1,
                gimmick_id: 100,
                offset_from_source: false,
                offset_coordinate_id: 0,
                offset: [0.0; 3],
                scale: 0.0,
                velocity_coordinate_id: 0,
                velocity: [0.0; 3],
                ang_vel_coordinate_id: 0,
                ang_vel: [0.0; 3],
            }),
        )]),
    );
    let result = d.spawn_gimmick_effect(100).expect("effect");
    assert_eq!(result.gimmick_id, 100);
}

// ---- Edge cases ----

#[test]
fn get_skill_template_handles_max_u_int32() {
    let d = Data::default();
    assert!(d.skill(u32::MAX).is_none());
}

#[test]
fn multiple_get_calls_return_consistent_results() {
    let d = skills_with(vec![SkillTemplate {
        id: 1,
        cooldown_time: 5000,
        ..SkillTemplate::default()
    }]);
    let r1 = d.skill(1).expect("1");
    let r2 = d.skill(1).expect("2");
    let r3 = d.skill(1).expect("3");
    assert_eq!(r2.id, r1.id);
    assert_eq!(r3.id, r2.id);
}
