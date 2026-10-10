//! `EffectTemplate.Apply` for every effect type. The combat effects (damage, heal, buff,
//! dispel, mana, aggro) and the special effects of this subsystem are ported here; effects that
//! belong to another subsystem (spawns, loot, crafting, quests, ...) are routed through
//! `ext::ExtHooks::route_effect` and logged when no subsystem handles them.

pub mod combat;
pub mod special;
pub mod unit_fx;

use std::sync::Arc;

use rand::Rng;

use crate::server::GameServer;
use crate::world::WorldObject;

use super::data::{BuffTemplate, EffectKind, EffectTemplate};
use super::geometry::Target;
use super::packets::CompressedGamePackets;
use super::state::{SkillRef, SkillRun};
use super::types::{CastAction, SkillCastTarget, SkillCaster, SkillObject};

/// `EffectSource`.
#[derive(Debug, Clone, Default)]
pub struct EffectSource {
    pub skill: Option<SkillRef>,
    pub buff: Option<Arc<BuffTemplate>>,
    pub amount: i32,
    pub is_trigger: bool,
}

impl EffectSource {
    /// `new EffectSource(skill)`.
    pub fn from_skill(skill: SkillRef) -> Self {
        Self {
            skill: Some(skill),
            ..Self::default()
        }
    }

    /// `new EffectSource(buffTemplate)`.
    pub fn from_buff(buff: Arc<BuffTemplate>) -> Self {
        Self {
            buff: Some(buff),
            ..Self::default()
        }
    }

    pub fn skill(&self) -> Option<&SkillRun> {
        self.skill.as_deref()
    }

    /// `source.Skill?.Level ?? 1`.
    pub fn skill_level(&self) -> i32 {
        self.skill.as_ref().map_or(1, |s| i32::from(s.level))
    }
}

/// The arguments of `EffectTemplate.Apply(caster, casterObj, target, targetObj, castObj,
/// source, skillObject, time, packetBuilder)`.
#[derive(Debug, Clone)]
pub struct EffectArgs<'a> {
    pub caster: Option<WorldObject>,
    pub caster_caster: SkillCaster,
    pub target: Target,
    pub target_caster: SkillCastTarget,
    pub cast: CastAction,
    pub source: EffectSource,
    pub skill_object: &'a SkillObject,
}

impl EffectArgs<'_> {
    pub fn caster_target(&self) -> Option<Target> {
        self.caster.clone().map(Target::Obj)
    }
}

/// `Random.Shared.Next(min, max)` (ints, `max` exclusive). `None` where the C# throws
/// (`min > max`).
pub fn rand_int(min: i32, max: i32) -> Option<i32> {
    match min.cmp(&max) {
        std::cmp::Ordering::Less => Some(rand::thread_rng().gen_range(min..max)),
        std::cmp::Ordering::Equal => Some(min),
        std::cmp::Ordering::Greater => None,
    }
}

/// `Random.Shared.Next(float min, float max)` (`NextSingle() * (max - min) + min`).
pub fn rand_float(min: f32, max: f32) -> f32 {
    rand::thread_rng().gen::<f32>() * (max - min) + min
}

/// `EffectTemplate.Apply` dispatch.
pub fn apply(
    server: &GameServer,
    kind: EffectKind,
    template: &EffectTemplate,
    args: &EffectArgs<'_>,
    packets: Option<&mut CompressedGamePackets>,
) {
    use EffectTemplate as T;
    match template {
        T::Damage(t) => combat::damage(server, t, args, packets),
        T::Heal(t) => combat::heal(server, t, args, packets),
        T::Buff(t) => combat::buff_effect(server, t, args),
        T::Dispel(t) => combat::dispel(server, t, args),
        T::RestoreMana(t) => combat::restore_mana(server, t, args),
        T::ManaBurn(t) => combat::mana_burn(server, t, args),
        T::Aggro(t) => combat::aggro(server, t, args),
        T::Special(t) => special::apply(server, t, args),
        T::FlyingStateChange { .. } => combat::flying_state_change(server, template, args),
        T::ResetAoeDiminishing { .. } => {}
        T::SkillController(t) => combat::skill_controller(server, t, args),
        T::Bubble { id, kind_id } => unit_fx::bubble(server, *id, *kind_id, args),
        T::Impulse(t) => unit_fx::impulse(server, t, args),
        T::MoveToRezPoint { .. }
        | T::OpenPortal { .. }
        | T::AccountAttribute(_)
        | T::AcceptQuest { .. }
        | T::Cinema { .. }
        | T::CleanupUcc { .. }
        | T::Conversion(_)
        | T::Craft { .. }
        | T::GainLootPackItem(_)
        | T::ImprintUcc { .. }
        | T::Interaction { .. }
        | T::KillNpcWithoutCorpse(_)
        | T::NpcControl(_)
        | T::NpcSpawnerSpawn(_)
        | T::NpcSpawnerDespawn { .. }
        | T::PhysicalExplosion(_)
        | T::PutDownBackpack { .. }
        | T::RecoverExp(_)
        | T::RepairSlave { .. }
        | T::ReportCrime { .. }
        | T::ScopedF { .. }
        | T::Spawn(_)
        | T::SpawnFish { .. }
        | T::SpawnGimmick(_)
        | T::TrainCraft { .. }
        | T::TrainCraftRank { .. } => {
            if !server
                .systems
                .skills
                .ext
                .route_effect(server, kind, template, args)
            {
                combat::unrouted_effect(server, kind, template, args);
            }
        }
    }
}
