//! Server-side cast restrictions, checked before GCD, buffs, plots or resources change.

use crate::server::GameServer;
use crate::world::WorldObject;

use super::geometry::{world_pos, Target};
use super::state::SkillRun;
use super::unit_reqs::skill_result_for_key;

fn fail(key: &str) -> Result<(), (u8, u32)> {
    Err((skill_result_for_key(key), 0))
}

fn in_water(server: &GameServer, target: &Target) -> bool {
    if target
        .as_obj()
        .and_then(|o| o.with_unit(|u| u.unit().is_under_water))
        .unwrap_or(false)
    {
        return true;
    }
    let pos = world_pos(server, target);
    let instance = match target {
        Target::Obj(obj) => obj.transform().instance_id,
        Target::Pos(pos) => pos.instance_id,
    };
    crate::systems::ai::physics::is_water(
        server,
        instance,
        super::controllers::Vec3::new(pos.x, pos.y, pos.z + 0.1),
    )
    .is_some()
}

pub(super) fn labor_cost(skill: &SkillRun, player: &crate::world::PlayerRef) -> i32 {
    let labor = skill.template.consume_labor_power.max(0);
    let step = player
        .data()
        .actability
        .actabilities
        .iter()
        .find(|ability| i64::from(ability.id) == i64::from(skill.template.actability_group_id))
        .map(|ability| ability.step);
    let cost = step.map_or(labor, |step| {
        (f64::from(labor) * f64::from(super::progress::labor_cost_multiplier(step)))
            .round_ties_even() as i32
    });
    if labor > 0 {
        cost.max(1)
    } else {
        0
    }
}

pub(super) fn check_resources(
    server: &GameServer,
    skill: &SkillRun,
    caster: &WorldObject,
) -> Result<(), (u8, u32)> {
    if super::skill::mana_cost(server, skill, caster)
        > caster.with_unit(|u| u.unit().mp).unwrap_or(0)
    {
        return fail("skill_lack_mana");
    }
    let Some(player) = caster.as_player() else {
        return Ok(());
    };
    if labor_cost(skill, player) > i32::from(player.data().labor_power) {
        return fail("skill_need_labor_power");
    }
    let reagents = server.game_data.systems.skills.reagents_for(skill.id);
    if !reagents.is_empty() {
        let items = server.systems.skills.ext.items();
        let enough = |reagent: &super::data::SkillItemAmount| {
            items.item_count(server, player, reagent.item_id) >= i32::from(reagent.amount)
        };
        let available = if skill.template.first_reagent_only {
            reagents.iter().any(enough)
        } else {
            reagents.iter().all(enough)
        };
        if !available {
            return fail("skill_need_reagent");
        }
    }
    Ok(())
}

pub(super) fn check(
    server: &GameServer,
    skill: &SkillRun,
    caster: &WorldObject,
    target: &Target,
) -> Result<(), (u8, u32)> {
    let template = &skill.template;
    let (hp, buff_ids, moving) = caster
        .with_unit(|u| {
            let unit = u.unit();
            (
                unit.hp,
                unit.buffs
                    .effects
                    .iter()
                    .filter(|b| !b.is_ended())
                    .map(|b| b.template.id)
                    .collect::<Vec<_>>(),
                unit.skill_state.moving,
            )
        })
        .ok_or((skill_result_for_key("skill_invalid_source"), 0))?;
    if hp <= 0 && template.source_alive && !template.source_dead {
        return fail("skill_source_died");
    }
    if hp > 0 && template.source_dead && !template.source_alive {
        return fail("skill_source_alive");
    }
    if template.source_not_swim && in_water(server, &Target::Obj(caster.clone())) {
        return fail("skill_cannot_cast_in_swimming");
    }
    if template.source_cannot_use_while_walk && moving {
        return fail("skill_cannot_cast_while_walking");
    }
    for id in buff_ids {
        let Some(buff) = server.game_data.systems.skills.buff(id) else {
            continue;
        };
        if !template.source_stun && (buff.stun || buff.sleep || buff.knockdown) {
            return fail("skill_cannot_cast_in_stun");
        }
        if buff.blank_minded && !template.source_stun {
            return fail("skill_blank_minded");
        }
    }
    if let Some(target_hp) = target.as_obj().and_then(|o| o.with_unit(|u| u.unit().hp)) {
        if target_hp <= 0 && template.target_alive && !template.target_dead {
            return fail("skill_target_died");
        }
        if target_hp > 0 && template.target_dead && !template.target_alive {
            return fail("skill_target_alive");
        }
    }
    if template.target_only_water && !in_water(server, target) {
        return fail("skill_invalid_location");
    }
    let source = world_pos(server, &Target::Obj(caster.clone()));
    let destination = world_pos(server, target);
    if ![
        source.x,
        source.y,
        source.z,
        destination.x,
        destination.y,
        destination.z,
    ]
    .iter()
    .all(|v| v.is_finite())
    {
        return fail("skill_invalid_target");
    }
    if template.target_angle > 0
        && !super::geometry::in_angle(&source, &destination, template.target_angle as f32)
    {
        return fail("skill_outof_angle");
    }
    let distance = f64::from(super::geometry::distance_to(
        server,
        &Target::Obj(caster.clone()),
        target,
        true,
    ));
    let mut min_range = f64::from(template.min_range);
    let mut max_range = super::skill::apply_skill_modifiers(
        server,
        skill,
        caster,
        crate::models::unit::SkillAttribute::Range,
        f64::from(template.max_range),
    );
    if template.target_type == super::types::SkillTargetType::Doodad as i32
        && template.min_range >= 100
    {
        min_range /= 100.0;
    }
    if template.weapon_slot_for_range_id > 0 {
        let ranges = server
            .systems
            .skills
            .ext
            .items()
            .weapon_holdable(server, caster, template.weapon_slot_for_range_id)
            .map_or((0.0, 3.0), |h| (h.min_range, h.max_range));
        min_range = f64::from(ranges.0);
        max_range = f64::from(ranges.1);
    }
    if distance < min_range {
        return fail("skill_too_close_range");
    }
    let doodad_or_slave = target
        .as_obj()
        .is_some_and(|o| o.as_doodad().is_some() || o.as_slave().is_some());
    if distance > max_range && !doodad_or_slave {
        return fail("skill_too_far_range");
    }
    if template.check_obstacle && !target.as_obj().is_some_and(|o| o.same(caster)) {
        // Doodad transforms anchor crops/furniture at the ground. A ray ending at that
        // anchor hits the terrain itself and prevents normal harvesting and recovery.
        let end_z = if target
            .as_obj()
            .is_some_and(|o| o.is_unit() || o.as_doodad().is_some())
        {
            destination.z + 1.0
        } else {
            destination.z
        };
        let ignored: Vec<_> = target
            .as_obj()
            .and_then(WorldObject::as_doodad)
            .map(|d| {
                (
                    crate::systems::ai::collision::world::ObjKind::Doodad,
                    d.obj_id,
                )
            })
            .into_iter()
            .collect();
        if let Some(Some(hit)) = crate::systems::ai::collision::raycast_ignoring_objects(
            server,
            caster.transform().instance_id,
            [source.x, source.y, source.z + 1.0],
            [destination.x, destination.y, end_z],
            crate::systems::ai::collision::query::mask::STATIC_TERRAIN,
            crate::systems::ai::collision::query::ray_flags::DEFAULT,
            &ignored,
        ) {
            tracing::debug!(skill = template.id, from = ?source, to = ?destination, ?hit, "Skill line of sight blocked");
            return fail("skill_obstacle");
        }
    }
    Ok(())
}
