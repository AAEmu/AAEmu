//! The skills subsystem's implementations of the other subsystems' hooks:
//!
//! * [`SkillsForNpcs`]: `npcs::hooks::SkillsBridge` (`Skill.Use` from doodad funcs / gimmicks,
//!   buffs, relations, area triggers, gear bonuses of NPCs, skill template facts);
//!
//! installed by [`install`] from `System::start` (`bind`). The skills parts of the quest and
//! social hook sets (`AddExp`, `UseSkill`, `ChangeLabor`, `PostUpdateCurrentHp`, buff tags)
//! are called directly by the quests / social implementations through the public functions of
//! this subsystem ([`super::progress`], [`super::skill`], [`super::buffs`], [`super::combat`]).

use std::sync::Arc;

use crate::server::GameServer;
use crate::systems::npcs::hooks::{DoodadSkillCaster, DoodadSkillTarget, SkillsBridge};
use crate::world::{DoodadRef, PlayerRef, WorldObject};

use super::effects::{EffectArgs, EffectSource};
use super::geometry::Target;
use super::state::SkillRun;
use super::types::{
    skill_result, CastAction, SkillCastTarget, SkillCaster, SkillObject, SkillTargetRelation,
};

/// `SpecialType.GainItem`, `GainItemWithPosImprint`, `GainItemWithEmblemImprint`.
const GAIN_ITEM_SPECIALS: [i32; 3] = [27, 32, 45];
/// `ItemTaskType.Loot`.
const ITEM_TASK_LOOT: u8 = 9;

/// Installs this subsystem's hook implementations into the other subsystems.
pub fn install(server: &GameServer) {
    install_economy(server);
    server
        .systems
        .npcs
        .hooks
        .skills
        .install(Arc::new(SkillsForNpcs));
}

/// `new Skill(template).Use(caster, casterCaster, targetCaster, skillObject, bypassGcd)`;
/// `None` when the skill template does not exist.
pub fn use_skill_with(
    server: &GameServer,
    caster: &WorldObject,
    caster_caster: SkillCaster,
    mut target_caster: SkillCastTarget,
    skill_id: u32,
    skill_object: Option<SkillObject>,
) -> Option<u8> {
    let t = server.game_data.systems.skills.skill(skill_id).cloned()?;
    let skill = SkillRun::new(t, 1);
    Some(
        super::skill::use_skill(
            server,
            &skill,
            caster,
            caster_caster,
            &mut target_caster,
            skill_object,
            false,
        )
        .0,
    )
}

fn relation(raw: u8) -> SkillTargetRelation {
    SkillTargetRelation::from_raw(i64::from(raw)).unwrap_or(SkillTargetRelation::Any)
}

/// `npcs::hooks::SkillsBridge` of the skills subsystem.
pub struct SkillsForNpcs;

impl SkillsBridge for SkillsForNpcs {
    fn use_skill(
        &self,
        server: &GameServer,
        caster: &WorldObject,
        target: &WorldObject,
        skill_id: u32,
    ) -> bool {
        if server.game_data.systems.skills.skill(skill_id).is_none() {
            return false;
        }
        let caster_caster = match caster {
            WorldObject::Doodad(d) => SkillCaster::Doodad(d.obj_id),
            o => SkillCaster::Unit(o.obj_id()),
        };
        let target_caster = if target.as_doodad().is_some() {
            SkillCastTarget::Doodad(target.obj_id())
        } else {
            SkillCastTarget::Unit(target.obj_id())
        };
        use_skill_with(server, caster, caster_caster, target_caster, skill_id, None)
            .is_some_and(|r| r == skill_result::SUCCESS)
    }

    fn skill_crime_point(&self, server: &GameServer, skill_id: u32) -> i32 {
        server
            .game_data
            .systems
            .skills
            .skill(skill_id)
            .map_or(0, |t| t.crime_point)
    }

    fn on_unit_died(&self, server: &GameServer, unit: &WorldObject, killer: Option<&WorldObject>) {
        super::skill::interrupt_skills(server, unit);
        super::buffs::fire_unit_event(
            server,
            unit.obj_id(),
            super::types::BuffEventTriggerKind::Death,
            0,
        );
        if let Some(kp) = killer.and_then(WorldObject::as_player) {
            crate::systems::quests::events::on_kill(server, kp, None, killer, Some(unit));
        }
        super::buffs::remove_effects_on_death(server, unit);
    }

    fn update_gear_bonuses(
        &self,
        server: &GameServer,
        unit: &WorldObject,
        item_added: Option<&crate::models::item::Item>,
        item_removed: Option<&crate::models::item::Item>,
    ) {
        match unit {
            WorldObject::Player(p) => {
                super::stats::update_gear_bonuses(server, p, item_added, item_removed)
            }
            WorldObject::Npc(_) | WorldObject::Mate(_) | WorldObject::Slave(_) => {
                super::stats::update_unit_gear_bonuses(server, unit, item_added, item_removed)
            }
            _ => {}
        }
    }

    fn check_buff_tag(&self, server: &GameServer, unit: &WorldObject, tag_id: u32) -> bool {
        super::buffs::check_buff_tag(server, unit, tag_id)
    }

    fn is_relation_valid(
        &self,
        server: &GameServer,
        relation_raw: u8,
        caster: &WorldObject,
        target: &WorldObject,
    ) -> bool {
        super::relation::is_relation_valid(
            server,
            relation(relation_raw),
            &Target::Obj(caster.clone()),
            &Target::Obj(target.clone()),
        )
    }

    fn filter_with_relation(
        &self,
        server: &GameServer,
        relation_raw: u8,
        caster: &WorldObject,
        units: Vec<WorldObject>,
    ) -> Vec<WorldObject> {
        super::relation::filter_with_relation(
            server,
            relation(relation_raw),
            &Target::Obj(caster.clone()),
            units,
        )
    }

    fn apply_area_buff(
        &self,
        server: &GameServer,
        caster: &WorldObject,
        unit: &WorldObject,
        buff_id: u32,
    ) {
        let Some(t) = server.game_data.systems.skills.buff(buff_id).cloned() else {
            return;
        };
        super::buffs::apply_template(
            server,
            &t,
            Some(caster),
            &SkillCaster::Unit(caster.obj_id()),
            unit,
            None,
        );
        crate::systems::dispatch_buff_events(server, unit);
    }

    fn apply_area_effects(
        &self,
        server: &GameServer,
        caster: &WorldObject,
        unit: &WorldObject,
        buff_id: u32,
        skill_id: u32,
        effect_ids: &[u32],
    ) {
        let data = &server.game_data.systems.skills;
        let skill_object = SkillObject::default();
        for &id in effect_ids {
            let Some((kind, template)) = data.effect_by_type_id(id) else {
                continue;
            };
            if let super::data::EffectTemplate::Buff(b) = template {
                if b.buff_id
                    .is_some_and(|id| unit.with_base_unit(|u| u.buffs().check_buff(id)))
                {
                    continue;
                }
            }
            let cast = unit
                .with_base_unit(|u| {
                    u.buffs()
                        .effects
                        .iter()
                        .find(|e| e.template.id == buff_id)
                        .map(|e| e.index)
                })
                .map_or(CastAction::Skill { skill_id, tl_id: 0 }, |index| {
                    CastAction::Buff {
                        buff_id,
                        owner_obj_id: unit.obj_id(),
                        index,
                    }
                });
            let args = EffectArgs {
                caster: Some(caster.clone()),
                caster_caster: SkillCaster::Unit(caster.obj_id()),
                target: Target::Obj(unit.clone()),
                target_caster: SkillCastTarget::Unit(unit.obj_id()),
                cast,
                source: EffectSource::default(),
                skill_object: &skill_object,
            };
            super::effects::apply(server, kind, template, &args, None);
        }
        crate::systems::dispatch_buff_events(server, unit);
    }

    fn use_gimmick_skill(
        &self,
        server: &GameServer,
        caster: Option<&WorldObject>,
        gimmick: &WorldObject,
        skill_id: u32,
        pos: (f32, f32, f32),
    ) {
        // UseSkillTask(skill, caster, SkillDoodad(gimmick), gimmick, SkillCastPositionTarget)
        let Some(caster) = caster.cloned() else {
            return; // Skill.Use(null, ..): InvalidSource
        };
        let gimmick_id = gimmick.obj_id();
        super::tasks::schedule(server, 0, move |srv| {
            use_skill_with(
                &srv,
                &caster,
                SkillCaster::Doodad(gimmick_id),
                SkillCastTarget::Position {
                    obj_id: 0,
                    x: pos.0,
                    y: pos.1,
                    z: pos.2,
                    rot: 0.0,
                    obj_id1: 0,
                    obj_id2: 0,
                },
                skill_id,
                Some(SkillObject::default()),
            );
        });
    }

    fn use_doodad_func_skill(
        &self,
        server: &GameServer,
        caster: &WorldObject,
        caster_obj: DoodadSkillCaster,
        _target: Option<&WorldObject>,
        target_obj: DoodadSkillTarget,
        skill_id: u32,
        scheduled: bool,
    ) {
        let caster_caster = match caster_obj {
            DoodadSkillCaster::Unit(id) => SkillCaster::Unit(id),
            DoodadSkillCaster::Doodad(id) => SkillCaster::Doodad(id),
        };
        let target_caster = match target_obj {
            DoodadSkillTarget::Unit(id) => SkillCastTarget::Unit(id),
            DoodadSkillTarget::Doodad(id) => SkillCastTarget::Doodad(id),
        };
        if scheduled {
            let caster = caster.clone();
            super::tasks::schedule(server, 0, move |srv| {
                use_skill_with(&srv, &caster, caster_caster, target_caster, skill_id, None);
            });
        } else {
            use_skill_with(server, caster, caster_caster, target_caster, skill_id, None);
        }
    }

    fn doodad_func_skill_hit(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        doodad: &WorldObject,
        skill_id: u32,
    ) {
        let data = &server.game_data.systems.skills;
        let Some(template) = data.skill(skill_id).cloned() else {
            return;
        };
        let Ok(obj_id) = server.ids.object.get_next_id() else {
            return;
        };
        let caster = WorldObject::from(player);
        let skill = SkillRun::new(template.clone(), 1);
        for se in &template.effects {
            let Some((_, super::data::EffectTemplate::Special(special))) =
                data.effect_by_type_id(se.effect_id)
            else {
                continue;
            };
            if !GAIN_ITEM_SPECIALS.contains(&special.special_effect_type_id) {
                continue;
            }
            let item_template_id = special.value1 as u32;
            // Inventory.Bag.AcquireDefaultItem(Loot, item, 1, grade) (item subsystem)
            server.systems.skills.ext.items().acquire_default_item(
                server,
                player,
                ITEM_TASK_LOOT,
                item_template_id,
                1,
            );
            let mut target = SkillCastTarget::Doodad(doodad.obj_id());
            super::skill::use_skill(
                server,
                &skill,
                &caster,
                SkillCaster::Item {
                    obj_id,
                    item_id: 0,
                    item_template_id,
                    type1: 0,
                    type2: 0,
                },
                &mut target,
                None,
                false,
            );
        }
    }

    fn add_buff(
        &self,
        server: &GameServer,
        target: &WorldObject,
        buff_id: u32,
        caster: &WorldObject,
    ) {
        super::buffs::add_buff_by_id(server, target, buff_id, caster, 0);
        crate::systems::dispatch_buff_events(server, target);
    }

    fn cancel_skill(&self, _server: &GameServer, player: &PlayerRef) {
        player.data().skill_char.skill_cancelled = true;
    }

    fn interrupt_skills(&self, server: &GameServer, unit: &WorldObject) {
        super::skill::interrupt_skills(server, unit);
    }

    fn generate_theft_evidence(
        &self,
        server: &GameServer,
        criminal: &PlayerRef,
        doodad: &DoodadRef,
    ) {
        server
            .systems
            .social
            .crimes
            .generate_evidence_from_theft(server, Some(criminal), doodad);
    }

    fn skill_consume_labor_power(&self, server: &GameServer, skill_id: u32) -> i32 {
        server
            .game_data
            .systems
            .skills
            .skill(skill_id)
            .map_or(0, |t| t.consume_labor_power)
    }
}

// ---------------------------------------------------------------------------------------------
// Economy hooks (gear bonuses, progression)
// ---------------------------------------------------------------------------------------------

/// The skills subsystem's implementations of the economy hooks: `GearHooks`
/// (`Unit.UpdateGearBonuses` after an equipment change) and `ProgressionHooks`
/// (`Character.AddExp` from `ChangeLabor`).
pub struct SkillsForEconomy;

/// Installs the skills subsystem's implementations of the economy hooks.
pub fn install_economy(server: &GameServer) {
    let e = &server.systems.economy;
    let me = Arc::new(SkillsForEconomy);
    e.hooks.gear.install(me.clone());
    e.hooks.progression.install(me);
}

impl crate::systems::economy::hooks::GearHooks for SkillsForEconomy {
    fn update_gear_bonuses(
        &self,
        server: &GameServer,
        unit: &WorldObject,
        equipped: Option<&crate::models::item::Item>,
        unequipped: Option<&crate::models::item::Item>,
    ) {
        match unit {
            WorldObject::Player(p) => {
                super::stats::update_gear_bonuses(server, p, equipped, unequipped)
            }
            WorldObject::Npc(_) | WorldObject::Mate(_) | WorldObject::Slave(_) => {
                super::stats::update_unit_gear_bonuses(server, unit, equipped, unequipped)
            }
            _ => super::stats::refresh_cached_stats(server, unit),
        }
    }
}

impl crate::systems::economy::hooks::ProgressionHooks for SkillsForEconomy {
    fn add_exp(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        exp: i32,
        should_add_ability_exp: bool,
    ) {
        super::progress::add_exp(server, player, exp, should_add_ability_exp);
    }
}
