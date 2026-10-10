//! `Models/Game/NPChar/NpcEvents` (`RegisterNpcEvents` + the handlers): the NPC's `np_skills`
//! of a `SkillUseConditionKind` are used when the AI raises the matching `Npc.Events` event
//! (`OnCombatStarted`, `InIdle`, `OnDeath`, `InAlert`, `OnSpawn`, `OnDespawn`; `OnAlert` and
//! `InDead` are never raised).
//!
//! `RegisterNpcEvents` subscribes the handler once per `np_skills` row of that condition, so a
//! handler runs as many times per event as the template has skills of that kind (the C#
//! multicast delegate); the events are registered only for NPCs spawned by a spawner
//! (`NpcSpawnerNpc.Spawn`, before `GoToSpawn`, so `SpawningBehavior.Enter`'s `OnSpawn` fires).

use crate::systems::skills::types::{SkillCastTarget, SkillCaster};
use crate::world::now_ms;

use super::framework::AiCtx;
use super::skills::{self as ai_skills, SkillUseConditionKind as K};

fn registrations(c: &AiCtx<'_>, kind: K) -> usize {
    if !c.ai.events_registered {
        return 0;
    }
    ai_skills::template_skills(c, kind).len()
}

/// The common body of the handlers: every skill of `kind` not on cooldown is used (a skill
/// without a cooldown time is blocked "forever" first: `AddCooldown(id, uint.MaxValue)`).
fn use_np_skills(c: &mut AiCtx<'_>, kind: K, target_obj_id: u32, bypass_gcd: bool) {
    for id in ai_skills::template_skills(c, kind) {
        let Some(skill) = ai_skills::np_skill(c, id, kind) else {
            continue;
        };
        if ai_skills::on_cooldown(c, skill.id) {
            continue;
        }
        if skill.template.cooldown_time == 0 {
            let now = now_ms();
            c.npc
                .data()
                .unit
                .cooldowns
                .add_cooldown(skill.id, u32::MAX, now);
        }
        tracing::trace!("Npc={} using skill={}", c.npc.obj_id, skill.id);
        let mut t = SkillCastTarget::Unit(target_obj_id);
        ai_skills::use_skill(
            c,
            &skill,
            SkillCaster::Unit(c.npc.obj_id),
            &mut t,
            bypass_gcd,
        );
    }
}

/// `Npc.Events.OnCombatStarted` → `OnCombatStarted` handler(s).
pub fn on_combat_started(c: &mut AiCtx<'_>) {
    for _ in 0..registrations(c, K::InCombat) {
        if c.npc.data().unit.is_in_battle {
            return;
        }
        super::combat::set_in_battle(c.server, &c.obj, true);
        tracing::trace!("Npc={} has started combat.", c.npc.obj_id);
        let id = c.npc.obj_id;
        use_np_skills(c, K::InCombat, id, false);
    }
}

/// `Npc.Events.InIdle` → `InIdle` handler(s).
pub fn in_idle(c: &mut AiCtx<'_>) {
    for _ in 0..registrations(c, K::InIdle) {
        let id = c.npc.obj_id;
        use_np_skills(c, K::InIdle, id, false);
    }
}

/// `Npc.Events.OnDeath` (raised by `DeadBehavior.Enter`) → `OnDeath` handler(s).
pub fn on_death(c: &mut AiCtx<'_>) {
    for _ in 0..registrations(c, K::OnDeath) {
        let id = c.npc.obj_id;
        use_np_skills(c, K::OnDeath, id, true);
    }
}

/// `Npc.Events.OnSpawn` → `OnSpawn` handler(s).
pub fn on_spawn(c: &mut AiCtx<'_>) {
    for _ in 0..registrations(c, K::OnSpawn) {
        let id = c.npc.obj_id;
        use_np_skills(c, K::OnSpawn, id, true);
    }
}

/// `Npc.Events.OnDespawn` → `OnDespawn` handler(s).
pub fn on_despawn(c: &mut AiCtx<'_>) {
    for _ in 0..registrations(c, K::OnDespawn) {
        let id = c.npc.obj_id;
        use_np_skills(c, K::OnDespawn, id, true);
    }
}

/// `Npc.Events.InAlert` → `InAlert` handler(s) (target = the current target).
pub fn in_alert(c: &mut AiCtx<'_>) {
    for _ in 0..registrations(c, K::InAlert) {
        let target = c.npc.data().unit.current_target;
        use_np_skills(c, K::InAlert, target, true);
    }
}
