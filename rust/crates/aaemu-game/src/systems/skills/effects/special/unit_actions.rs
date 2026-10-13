//! Special actions that change unit state rather than client visuals.

use rand::seq::SliceRandom;

use crate::server::GameServer;
use crate::world::now_ms;

use super::super::super::state::AggroKind;
use super::super::super::types::SkillCaster;
use super::super::super::{buffs, combat};
use super::super::EffectArgs;

/// The native WZFakeDeath handler removes incoming aggro without killing the
/// unit or awarding kill credit. Its packet contains only the target unit ID.
pub fn fake_death(server: &GameServer, args: &EffectArgs<'_>, _values: [i32; 4]) {
    if let Some(target) = args.target.as_obj().filter(|target| target.is_unit()) {
        combat::clear_incoming_aggro(server, target);
    }
}

pub fn buff_steal(server: &GameServer, args: &EffectArgs<'_>, values: [i32; 4]) {
    let (Some(caster), Some(target)) = (&args.caster, args.target.as_obj()) else {
        return;
    };
    if !caster.is_unit() || caster.same(target) || values[0] <= 0 {
        return;
    }
    let mut candidates = target.with_base_unit(|unit| {
        unit.buffs()
            .effects
            .iter()
            .filter(|buff| {
                !buff.passive
                    && !buff.is_ended()
                    && server
                        .game_data
                        .systems
                        .skills
                        .buff(buff.template.id)
                        .is_some_and(|template| template.kind == 1 && !template.system)
            })
            .cloned()
            .collect::<Vec<_>>()
    });
    candidates.shuffle(&mut rand::thread_rng());
    for original in candidates.into_iter().take(values[0] as usize) {
        if buffs::check_buff_immune(server, caster, original.template.id) {
            continue;
        }
        let Some(template) = server
            .game_data
            .systems
            .skills
            .buff(original.template.id)
            .cloned()
        else {
            continue;
        };
        let now = now_ms();
        let remaining = original.time_left(now);
        if remaining == 0.0 {
            continue;
        }
        let mut stolen = buffs::new_buff(
            server,
            &template,
            Some(caster),
            &SkillCaster::Unit(caster.obj_id()),
            args.source.skill(),
            now,
        );
        stolen.ab_level = original.ab_level;
        stolen.charge = original.charge;
        stolen.skill_id = original.skill_id;
        stolen.skill_level = original.skill_level;
        buffs::add_buff(
            server,
            caster,
            stolen,
            0,
            if remaining > 0.0 {
                remaining.ceil().min(i32::MAX as f64) as i32
            } else {
                0
            },
        );
        if caster.with_base_unit(|unit| {
            unit.buffs().effects.iter().any(|buff| {
                buff.template.id == original.template.id
                    && buff.caster_obj_id == caster.obj_id()
                    && buff.start_time_ms >= now
                    && !buff.is_ended()
            })
        }) {
            buffs::exit(server, target, original.index, false);
        }
    }
}

/// WZAggroCopy (native 0x39364EF0 / 0x39321A10) replaces the target table
/// with a snapshot of the source table, leaving kill-credit tagging independent.
pub fn aggro_copy(server: &GameServer, args: &EffectArgs<'_>, _values: [i32; 4]) {
    let (Some(source), Some(target)) = (&args.caster, args.target.as_obj()) else {
        return;
    };
    if source.same(target) || !source.is_unit() || !target.is_unit() {
        return;
    }
    let entries = source
        .with_unit(|unit| unit.unit().skill_state.aggro_table.clone())
        .unwrap_or_default();
    if entries.is_empty() {
        return;
    }
    let previous = target
        .with_unit(|unit| {
            unit.unit()
                .skill_state
                .aggro_table
                .keys()
                .copied()
                .collect::<Vec<_>>()
        })
        .unwrap_or_default();
    for id in previous {
        if let Some(unit) = server.world.object(id) {
            combat::clear_aggro_of_unit(server, target, &unit);
        }
    }
    target.with_unit(|unit| unit.unit_mut().skill_state.aggro_table.clear());
    for (id, entry) in entries {
        let Some(unit) = server.world.object(id) else {
            continue;
        };
        combat::add_unit_aggro(server, target, AggroKind::Etc, &unit, 0);
        target.with_unit(|owner| {
            owner.unit_mut().skill_state.aggro_table.insert(id, entry);
        });
        if entry.total() > 0 {
            if let Some(player) = unit.as_player() {
                let mut character = player.data();
                if !character
                    .skill_char
                    .is_in_aggro_list_of
                    .contains(&target.obj_id())
                {
                    character
                        .skill_char
                        .is_in_aggro_list_of
                        .push(target.obj_id());
                }
            }
        }
    }
}

/// WZAggroReset (native 0x39320E60): flags select damage/heal/etc components,
/// value4 is their replacement value. All zero clears the table.
pub fn aggro_reset(server: &GameServer, args: &EffectArgs<'_>, values: [i32; 4]) {
    let Some(owner) = args.target.as_obj().filter(|o| o.is_unit()) else {
        return;
    };
    let clear_all = values == [0; 4];
    let ids = owner
        .with_unit(|unit| {
            let table = &mut unit.unit_mut().skill_state.aggro_table;
            for entry in table.values_mut() {
                if values[0] != 0 {
                    entry.damage_aggro = values[3];
                }
                if values[1] != 0 {
                    entry.heal_aggro = values[3];
                }
                if values[2] != 0 {
                    entry.etc_aggro = values[3];
                }
            }
            table
                .iter()
                .filter(|(_, entry)| clear_all || entry.total() <= 0)
                .map(|(id, _)| *id)
                .collect::<Vec<_>>()
        })
        .unwrap_or_default();
    for id in ids {
        if let Some(unit) = server.world.object(id) {
            combat::clear_aggro_of_unit(server, owner, &unit);
        } else {
            owner.with_unit(|unit| {
                unit.unit_mut().skill_state.aggro_table.remove(&id);
            });
        }
    }
}

pub fn lose_target(server: &GameServer, args: &EffectArgs<'_>, _values: [i32; 4]) {
    let Some(unit) = args.target.as_obj().filter(|o| o.is_unit()) else {
        return;
    };
    unit.with_unit(|u| u.unit_mut().current_target = 0);
    if let Some(player) = unit.as_player() {
        combat::stop_auto_skill(player);
    }
    super::super::super::broadcast(
        server,
        unit,
        &crate::packets::world_objects::SCTargetChangedPacket {
            obj_id: unit.obj_id(),
            target_obj_id: 0,
        },
        true,
    );
}

pub fn cancel_ongoing_buff(server: &GameServer, args: &EffectArgs<'_>, values: [i32; 4]) {
    let Some(unit) = args.target.as_obj().or(args.caster.as_ref()) else {
        return;
    };
    if values[0] > 0 {
        buffs::remove_buff(server, unit, values[0] as u32);
    } else {
        buffs::trigger_remove_on(
            server,
            unit,
            super::super::super::types::BuffRemoveOn::StartSkill,
            0,
        );
        buffs::trigger_remove_on(
            server,
            unit,
            super::super::super::types::BuffRemoveOn::UseSkill,
            0,
        );
    }
}

pub fn npc_despawn(server: &GameServer, args: &EffectArgs<'_>, _values: [i32; 4]) {
    let Some(unit) = args.target.as_obj().filter(|o| o.as_npc().is_some()) else {
        return;
    };
    super::super::super::skill::interrupt_skills(server, unit);
    aggro_reset(server, args, [0; 4]);
    combat::clear_all_aggro(server, unit);
    crate::systems::npcs::spawn_manager::delete_object(server, unit, None);
}

/// 1.2 skill15220 (Pet Dual-Recovery Potion) restores value1/value2 percent HP/MP.
pub fn heal_pet(server: &GameServer, args: &EffectArgs<'_>, values: [i32; 4]) {
    let Some(unit) = args.target.as_obj().filter(|o| o.as_mate().is_some()) else {
        return;
    };
    let Some(points) = unit
        .with_unit(|u| {
            let unit = u.unit_mut();
            if unit.hp <= 0 {
                return None;
            }
            let hp = i64::from(unit.max_hp.max(0)) * i64::from(values[0].clamp(0, 100)) / 100;
            let mp = i64::from(unit.max_mp.max(0)) * i64::from(values[1].clamp(0, 100)) / 100;
            unit.hp = (i64::from(unit.hp) + hp).min(i64::from(unit.max_hp.max(0))) as i32;
            unit.mp = (i64::from(unit.mp) + mp).min(i64::from(unit.max_mp.max(0))) as i32;
            Some((unit.hp, unit.mp))
        })
        .flatten()
    else {
        return;
    };
    super::super::super::broadcast(
        server,
        unit,
        &crate::packets::world::SCUnitPointsPacket {
            obj_id: unit.obj_id(),
            hp: points.0,
            mp: points.1,
        },
        true,
    );
}
