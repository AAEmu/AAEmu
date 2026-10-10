//! The skill side of the AI: `NpcTemplate.Skills` lookups (`SkillUseConditionKind`),
//! `SkillManager.GetNpSkillTemplate`, `new Skill(template)`, `Skill.Use` for an NPC caster with
//! the behavior's `OnSkillEnded` callback, `Unit.InterruptSkills`, cooldown checks.

use std::sync::Arc;

use crate::systems::skills::data::SkillTemplate;
use crate::systems::skills::state::{SkillRef, SkillRun};
use crate::systems::skills::types::{SkillCastTarget, SkillCaster, SkillTargetType};
use crate::world::now_ms;

use super::framework::{AiCtx, BehaviorKind};

/// `SkillUseConditionKind`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum SkillUseConditionKind {
    None,
    InCombat,
    InIdle,
    OnDeath,
    InAlert,
    InDead,
    OnSpawn,
    OnDespawn,
    OnAlert,
}

impl SkillUseConditionKind {
    /// The raw `np_skills.skill_use_condition_id` value.
    pub fn raw(self) -> u32 {
        match self {
            Self::None => u32::MAX, // -1
            Self::InCombat => 0,
            Self::InIdle => 1,
            Self::OnDeath => 2,
            Self::InAlert => 3,
            Self::InDead => 4,
            Self::OnSpawn => 5,
            Self::OnDespawn => 6,
            Self::OnAlert => 7,
        }
    }
}

/// `Ai.Owner.Template.Skills.TryGetValue(kind, ...)`: the skill ids (`None` when the template
/// has no entry for that condition).
pub fn template_skills_if_any(c: &AiCtx<'_>, kind: SkillUseConditionKind) -> Option<Vec<u32>> {
    let t = super::npc::template(c.server, &c.npc)?;
    t.skills
        .iter()
        .find(|(cond, _)| *cond == kind.raw())
        .map(|(_, l)| l.iter().map(|s| s.skill_id).collect())
}

/// The skill ids of `kind` (empty when none).
pub fn template_skills(c: &AiCtx<'_>, kind: SkillUseConditionKind) -> Vec<u32> {
    template_skills_if_any(c, kind).unwrap_or_default()
}

/// `Ai.Owner.Cooldowns.CheckCooldown(skillId)`.
pub fn on_cooldown(c: &AiCtx<'_>, skill_id: u32) -> bool {
    let now = now_ms();
    c.npc.data().unit.cooldowns.check_cooldown(skill_id, now)
}

/// `new Skill(SkillManager.GetSkillTemplate(id))` (`None` when the template is missing; the C#
/// would throw a `NullReferenceException` in the constructor).
pub fn new_skill(c: &AiCtx<'_>, skill_id: u32) -> Option<SkillRef> {
    let t = c.server.game_data.systems.skills.skill(skill_id)?.clone();
    Some(SkillRun::new(t, 1))
}

/// `SkillManager.GetNpSkillTemplate(npcSkill)`: `InCombat` skills must ignore the global
/// cooldown and have no plot ("temporary condition to filter for dungeon OnCombat skills").
pub fn np_skill(c: &AiCtx<'_>, skill_id: u32, kind: SkillUseConditionKind) -> Option<SkillRef> {
    let t: Arc<SkillTemplate> = c.server.game_data.systems.skills.skill(skill_id)?.clone();
    if kind == SkillUseConditionKind::InCombat && (!t.ignore_global_cooldown || t.plot_id.is_some())
    {
        return None;
    }
    Some(SkillRun::new(t, 1))
}

/// `template.TargetType == SkillTargetType.Self`.
pub fn is_self_target(t: &SkillTemplate) -> bool {
    t.target_type == SkillTargetType::SelfTarget as i32
}

/// `template.TargetType == SkillTargetType.Pos`.
pub fn is_pos_target(t: &SkillTemplate) -> bool {
    t.target_type == SkillTargetType::Pos as i32
}

/// `skill.Use(Ai.Owner, caster, target, null, bypassGcd, out _)`.
pub fn use_skill(
    c: &AiCtx<'_>,
    skill: &SkillRef,
    caster: SkillCaster,
    target: &mut SkillCastTarget,
    bypass_gcd: bool,
) -> u8 {
    crate::systems::skills::skill::use_skill(
        c.server, skill, &c.obj, caster, target, None, bypass_gcd,
    )
    .0
}

/// `skill.Callback = OnSkillEnded` of behavior `k` (runs on the AI when the skill ends).
pub fn set_skill_ended_callback(c: &AiCtx<'_>, skill: &SkillRef, k: BehaviorKind) {
    let Some(server) = c.server.self_arc().map(|s| Arc::downgrade(&s)) else {
        return;
    };
    let obj_id = c.npc.obj_id;
    *skill.callback.lock().expect("callback poisoned") = Some(Box::new(move || {
        if let Some(s) = server.upgrade() {
            super::manager::post(&s, obj_id, move |a| super::behaviors::on_skill_ended(a, k));
        }
    }));
}

/// `Unit.InterruptSkills()`.
pub fn interrupt_skills(c: &AiCtx<'_>) {
    crate::systems::skills::skill::interrupt_skills(c.server, &c.obj);
}

/// `unit.Equipment.GetItemBySlot(slot)?.Template is WeaponTemplate` → (min, max) range of its
/// holdable.
pub fn weapon_holdable(
    c: &AiCtx<'_>,
    unit: &crate::world::WorldObject,
    slot: i32,
) -> Option<(f32, f32)> {
    c.server
        .systems
        .skills
        .ext
        .items()
        .weapon_holdable(c.server, unit, slot)
        .map(|h| (h.min_range, h.max_range))
}
