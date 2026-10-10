//! `SpecialEffect.Apply` and the `SpecialEffectAction`s (`Effects/SpecialEffects/*`).

use crate::server::GameServer;

use super::super::data::effects::SpecialEffect;
use super::EffectArgs;

/// `SpecialType.Projectile` (client-side, silently ignored).
pub const PROJECTILE: i32 = 37;

/// `SpecialEffect.Apply`: runs the action once, or `EffectRepeatCount` times
/// `EffectRepeatTick` ms apart (the C# sleeps on the calling thread; here the repetitions are
/// scheduled).
pub fn apply(server: &GameServer, e: &SpecialEffect, args: &EffectArgs<'_>) {
    let values = [e.value1, e.value2, e.value3, e.value4];
    let repeat = args
        .source
        .skill()
        .map_or(0, |s| s.template.effect_repeat_count);
    if repeat > 1 {
        let tick = args
            .source
            .skill()
            .map_or(0, |s| s.template.effect_repeat_tick);
        execute(server, e.special_effect_type_id, args, values);
        for i in 1..repeat {
            let owned = OwnedArgs::from(args);
            let kind = e.special_effect_type_id;
            super::super::tasks::schedule(server, i64::from(i) * i64::from(tick), move |srv| {
                let so = owned.skill_object.clone();
                let a = owned.to_args(&so);
                execute(&srv, kind, &a, values);
            });
        }
    } else {
        execute(server, e.special_effect_type_id, args, values);
    }
}

/// `EffectArgs` with an owned skill object (for scheduled repetitions).
#[derive(Debug, Clone)]
struct OwnedArgs {
    caster: Option<crate::world::WorldObject>,
    caster_caster: super::super::types::SkillCaster,
    target: super::super::geometry::Target,
    target_caster: super::super::types::SkillCastTarget,
    cast: super::super::types::CastAction,
    source: super::EffectSource,
    skill_object: super::super::types::SkillObject,
}

impl OwnedArgs {
    fn from(a: &EffectArgs<'_>) -> Self {
        Self {
            caster: a.caster.clone(),
            caster_caster: a.caster_caster,
            target: a.target.clone(),
            target_caster: a.target_caster,
            cast: a.cast,
            source: a.source.clone(),
            skill_object: a.skill_object.clone(),
        }
    }

    fn to_args<'a>(&self, so: &'a super::super::types::SkillObject) -> EffectArgs<'a> {
        EffectArgs {
            caster: self.caster.clone(),
            caster_caster: self.caster_caster,
            target: self.target.clone(),
            target_caster: self.target_caster,
            cast: self.cast,
            source: self.source.clone(),
            skill_object: so,
        }
    }
}

/// `SpecialEffectAction.Execute` of one special type.
pub fn execute(server: &GameServer, kind: i32, args: &EffectArgs<'_>, values: [i32; 4]) {
    if server
        .systems
        .skills
        .ext
        .route_special(server, kind, args, values)
    {
        return;
    }
    if kind == PROJECTILE {
        return;
    }
    tracing::warn!("Unknown special effect: {kind}");
}
