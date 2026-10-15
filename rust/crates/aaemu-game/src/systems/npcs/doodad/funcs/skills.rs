//! Skill / quest funcs: `DoodadFuncUse`, `FakeUse`, `ConditionalUse`, `SkillHit`, `Buff`,
//! `Quest`.

use super::{
    economy, error_message as err, is_player, quests_hook, send_error, set_to_next_phase,
    skills_hook,
};
use crate::systems::npcs::doodad::coffer;
use crate::systems::npcs::doodad::packets::SCDoodadQuestAcceptPacket;
use crate::systems::npcs::doodad::runtime::DoodadCtx;
use crate::systems::npcs::doodad::templates::{
    DoodadFuncBuff, DoodadFuncConditionalUse, DoodadFuncFakeUse, DoodadFuncQuest,
    DoodadFuncSkillHit, DoodadFuncUse,
};
use crate::systems::npcs::hooks::{DoodadSkillCaster, DoodadSkillTarget};
use crate::systems::npcs::public_farm;
use crate::world::{now_ms, WorldObject};

/// `DoodadFuncUse.Use`.
pub fn use_(
    f: &DoodadFuncUse,
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
    skill_id: u32,
    next_phase: i32,
) {
    if is_player(caster) {
        tracing::debug!(
            "DoodadFuncUse: skillId {skill_id}, nextPhase {next_phase},  SkillId {}",
            f.skill_id
        );
    } else {
        tracing::trace!(
            "DoodadFuncUse: skillId {skill_id}, nextPhase {next_phase},  SkillId {}",
            f.skill_id
        );
    }
    let Some(caster) = caster else {
        return;
    };
    let server = ctx.server;
    let owner = ctx.obj();
    let pos = ctx.world.world_pos_rot(&owner);
    if public_farm::in_public_farm(server, &ctx.world.template, (pos.x, pos.y, pos.z)) {
        let owner_id = ctx.doodad.data().owner_id;
        if public_farm::is_protected_by_public_farm(server, &ctx.doodad, now_ms()) && owner_id != 0
        {
            if let Some(character) = caster.as_player() {
                let (id, name) = {
                    let c = character.data();
                    (c.id, c.unit.name.clone())
                };
                if owner_id != id {
                    send_error(character, err::CANNOT_HARVEST_YET);
                    tracing::debug!(
                        "This should never happen character {name} attempted to bypass harvest protection (clienthacks?)"
                    );
                    skills_hook(server).cancel_skill(server, character);
                    return;
                }
            }
        }
    }
    let (owner_db_id, is_coffer) = {
        let d = ctx.doodad.data();
        (d.owner_db_id, d.coffer.is_some())
    };
    if let (true, Some(player)) = (owner_db_id > 0, caster.as_player()) {
        // If it's on a house, need to check permissions
        if is_coffer {
            // Coffers need their own permissions as they can override the house's settings
            if !coffer::allowed_to_interact(server, &ctx.doodad, player) {
                send_error(player, err::INTERACTION_PERMISSION_DENY);
                return;
            }
        } else {
            match economy(server).house_allowed_to_interact(server, owner_db_id, player) {
                None => {
                    // Fail-safe in case a doodad wasn't properly deleted from a house.
                    let mut d = ctx.doodad.data();
                    d.owner_db_id = 0;
                    d.owner_id = 0;
                    tracing::trace!(
                        "Interaction failed because attached house does not exist for doodad {}, resetting DbHouseId to public",
                        ctx.doodad.obj_id
                    );
                }
                Some(false) => {
                    send_error(player, err::INTERACTION_PERMISSION_DENY);
                    return;
                }
                Some(true) => {}
            }
        }
    }
    // TODO (C#): check skill references and consume items if items are required for skills
    if f.skill_id > 0 {
        if server.game_data.skills.skill(f.skill_id).is_none() {
            return;
        }
        skills_hook(server).use_doodad_func_skill(
            server,
            caster,
            DoodadSkillCaster::Unit(caster.obj_id()),
            Some(&owner),
            DoodadSkillTarget::Doodad(ctx.doodad.obj_id),
            f.skill_id,
            true,
        );
    }
    set_to_next_phase(ctx, skill_id > 0);
}

/// `DoodadFuncFakeUse.Use`.
pub fn fake_use(
    f: &DoodadFuncFakeUse,
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
    skill_id: u32,
    next_phase: i32,
) {
    let msg = format!(
        "DoodadFuncFakeUse: skillId {skill_id}, nextPhase {next_phase},  SkillId {}, FakeSkillId {}, TargetParent {}",
        f.skill_id, f.fake_skill_id, f.target_parent
    );
    if is_player(caster) {
        tracing::debug!("{msg}");
    } else {
        tracing::trace!("{msg}");
    }
    let Some(caster) = caster else {
        return;
    };
    if f.skill_id != 0 {
        let target = if f.target_parent {
            // target owner/doodad
            DoodadSkillTarget::Doodad(ctx.doodad.data().parent_obj_id)
        } else {
            DoodadSkillTarget::Unit(caster.obj_id())
        };
        skills_hook(ctx.server).use_doodad_func_skill(
            ctx.server,
            caster,
            DoodadSkillCaster::Doodad(ctx.doodad.obj_id),
            None,
            target,
            f.skill_id,
            false,
        );
        set_to_next_phase(ctx, true);
    } else if f.fake_skill_id != 0 {
        if f.fake_skill_id == skill_id && next_phase > 0 {
            set_to_next_phase(ctx, true);
        }
        // TODO (C#): quest ID=3357, Harvest the Cotton didn't work
        if skill_id > 0 && next_phase > 0 {
            set_to_next_phase(ctx, true);
        }
    }
    if f.fake_skill_id == 0 && f.skill_id == 0 {
        // TODO (C#): otherwise quest ID=1970, Stolen Glory didn't work
        set_to_next_phase(ctx, true);
    }
}

/// `DoodadFuncConditionalUse.Use`.
pub fn conditional_use(
    f: &DoodadFuncConditionalUse,
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
) {
    tracing::debug!(
        "DoodadFuncConditionalUse: skillId {}, fakeSkillId {}, questId {}, questTriggerPhase {}, itemId {}, itemTriggerPhase {}",
        f.skill_id,
        f.fake_skill_id,
        f.quest_id,
        f.quest_trigger_phase,
        f.item_id,
        f.item_trigger_phase
    );
    // TODO (C#): for quest ID=3124, "A Familiar Machine"
    if f.fake_skill_id != 0 || f.skill_id != 0 {
        set_to_next_phase(ctx, true);
    }
    let Some(player) = DoodadCtx::as_player(caster) else {
        return;
    };
    let server = ctx.server;
    if economy(server).item_count(server, player, f.item_id) > 0 {
        let mut d = ctx.doodad.data();
        d.phase.override_phase = f.item_trigger_phase as i32;
        d.phase.to_next_phase = true;
        return;
    }
    if quests_hook(server).has_quest(server, player, f.quest_id) {
        let mut d = ctx.doodad.data();
        d.phase.override_phase = f.quest_trigger_phase as i32;
        d.phase.to_next_phase = true;
    }
}

/// `DoodadFuncSkillHit.Use`.
pub fn skill_hit(
    f: &DoodadFuncSkillHit,
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
    skill_id: u32,
) {
    if let Some(player) = DoodadCtx::as_player(caster) {
        if f.skill_id > 0 {
            skills_hook(ctx.server).doodad_func_skill_hit(
                ctx.server,
                player,
                &ctx.obj(),
                f.skill_id,
            );
        }
    }
    set_to_next_phase(ctx, f.skill_id == skill_id);
}

/// `WorldManager.GetAround<BaseUnit>(obj, radius, true)`: every base unit (other than `obj`)
/// within `radius` widened by the model sizes (2D, strict) in the region neighbourhood.
pub fn around_base_units(
    server: &crate::server::GameServer,
    obj: &WorldObject,
    radius: f32,
) -> Vec<WorldObject> {
    use crate::systems::skills::geometry::{around_radius, Target};
    around_radius(server, &Target::Obj(obj.clone()), radius, true)
        .into_iter()
        .filter(|o| !matches!(o, WorldObject::Gimmick(_)))
        .collect()
}

/// `DoodadFuncBuff.Use`.
pub fn buff(f: &DoodadFuncBuff, ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>) {
    tracing::trace!("DoodadFuncBuff");
    // TODO (C#): ImplementRelationShipId; Count is maybe the maximum number of targets
    let Some(caster) = caster else {
        return; // NullReferenceException
    };
    let hook = skills_hook(ctx.server);
    if f.radius <= 0.0 {
        // Caster only
        hook.add_buff(ctx.server, caster, f.buff_id, caster);
    } else {
        for target in around_base_units(ctx.server, caster, f.radius) {
            hook.add_buff(ctx.server, &target, f.buff_id, caster);
        }
    }
}

/// `DoodadFuncQuest.Use`.
pub fn quest(
    f: &DoodadFuncQuest,
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
    skill_id: u32,
) {
    tracing::trace!(
        "DoodadFuncQuest : skillId {skill_id}, QuestKindId {}, QuestId {}",
        f.quest_kind_id,
        f.quest_id
    );
    let Some(player) = DoodadCtx::as_player(caster) else {
        return;
    };
    let quests = quests_hook(ctx.server);
    if !quests.has_quest(ctx.server, player, f.quest_id) {
        // A scheduled quest is offered (marker packet) only while its window is in progress.
        if !crate::systems::quests::quest_schedule_allows(
            &ctx.server.game_data.systems.npcs.schedule,
            f.quest_id,
            crate::systems::npcs::spawn_manager::unix_now(),
        ) {
            return;
        }
        player.send(SCDoodadQuestAcceptPacket {
            doodad_obj_id: ctx.doodad.obj_id,
            quest_id: f.quest_id,
        });
    } else {
        quests.do_report_events(ctx.server, player, f.quest_id, 0, ctx.doodad.obj_id, 0);
    }
}
