//! Special effect actions on buffs, cooldowns, mana, plots and casting: `Charge`,
//! `DisturbCasting`, `Resurrection`, `SkillUse`, `ManaCost`, `Cooldown`, `GlobalCooldown`,
//! `ResetCooldown`, `StopManaRegen`, `AddBreath`, `CancelStealth`, `AutoAttack`,
//! `FinishChanneling`, `SetVariable`, `ConsumeLaborPower`.

use crate::models::unit::SkillAttribute;
use crate::server::GameServer;
use crate::world::{now_ms, WorldObject};

use super::super::super::buffs;
use super::super::super::combat as unit_combat;
use super::super::super::packets::SCSkillCooldownResetPacket;
use super::super::super::packets_special::SCNotifyResurrectionPacket;
use super::super::super::progress;
use super::super::super::skill::{apply_skill_modifiers, start_auto_skill, use_skill};
use super::super::super::state::SkillRun;
use super::super::super::stats::{stat, StatKind};
use super::super::super::types::{BuffRemoveOn, SkillCastTarget};
use super::super::{rand_int, EffectArgs};
use super::{caster_is_player, log_values};

/// `(Unit)caster` (`None` + an error log where the C# throws).
fn unit_caster<'a>(args: &'a EffectArgs<'_>, action: &str) -> Option<&'a WorldObject> {
    match &args.caster {
        Some(c) if c.is_unit() => Some(c),
        Some(_) => {
            tracing::error!(
                "Special effects: {action} caster is not a Unit (InvalidCastException)"
            );
            None
        }
        None => {
            tracing::error!("Special effects: {action} without a caster (NullReferenceException)");
            None
        }
    }
}

/// `Charge`: adds charges to a buff on the caster.
///
/// Compute the accumulated charge before AddBuff so refreshes and their packets keep it.
pub fn charge(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    let [buff_id, min_charge, max_charge, unused] = v;
    if caster_is_player(args) {
        tracing::debug!(
            "Special effects: Charge buffId {buff_id}, minCharge {min_charge}, maxCharge {max_charge}, unused {unused}"
        );
    }
    let Some(caster) = &args.caster else {
        tracing::error!("Special effects: Charge without a caster (NullReferenceException)");
        return;
    };
    let buff = unit_combat::buff_of(caster, buff_id as u32);
    let Some(template) = server
        .game_data
        .systems
        .skills
        .buff(buff_id as u32)
        .cloned()
    else {
        tracing::error!(
            "Special effects: Charge buff {buff_id} has no template (NullReferenceException)"
        );
        return;
    };
    let Some(charge_delta) = rand_int(min_charge, max_charge) else {
        tracing::error!(
            "Special effects: Charge minCharge {min_charge} > maxCharge {max_charge} (ArgumentOutOfRangeException)"
        );
        return;
    };
    let old_charge = buff.as_ref().map_or(0, |b| b.charge);
    let mut new_effect = buffs::new_buff(
        server,
        &template,
        Some(caster),
        &args.caster_caster,
        args.source.skill(),
        now_ms(),
    );
    new_effect.charge = old_charge
        .saturating_add(charge_delta)
        .clamp(0, template.max_charge.max(0));
    buffs::add_buff(
        server,
        caster,
        new_effect,
        buff.as_ref().map_or(0, |b| b.index),
        0,
    );
}

/// Interrupt both plot and direct casts, using the configured percentage and delay.
pub fn disturb_casting(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    if caster_is_player(args) {
        tracing::debug!(
            "Special effects: DisturbCasting chance {}, delay {}, value3 {}, value4 {}",
            v[0],
            v[1],
            v[2],
            v[3]
        );
    }
    if !super::super::chance_succeeds(v[0]) {
        return;
    }
    let Some(target) = args.target.as_obj().filter(|t| t.is_unit()).cloned() else {
        return;
    };
    if v[1] > 0 {
        super::super::super::tasks::schedule(server, i64::from(v[1]), move |server| {
            if super::super::super::geometry::is_live_object(&server, &target) {
                super::super::super::skill::interrupt_skills(&server, &target);
            }
        });
    } else {
        super::super::super::skill::interrupt_skills(server, &target);
    }
}

/// `Resurrection`: offers a resurrection to a dead character (`SCNotifyResurrectionPacket`).
pub fn resurrection(_server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    let Some(p) = args.target.as_obj().and_then(WorldObject::as_player) else {
        return;
    };
    if p.data().unit.hp > 0 {
        return;
    }
    log_values(args, "Resurrection", v);
    p.send(SCNotifyResurrectionPacket {
        caster: args.caster_caster,
    });
    let mut c = p.data();
    c.skill_char.resurrect_hp_percent = v[1] as u32;
    c.skill_char.resurrect_mp_percent = v[2] as u32;
}

/// `SkillUse`: rolls `chance`, then uses skill `value1` on the target after `delay` ms
/// (`UseSkillTask`, `bypassGcd`).
pub fn skill_use(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    let [skill_id, delay, chance, value4] = v;
    if caster_is_player(args) {
        tracing::debug!(
            "Special effects: SkillUse skillId {skill_id}, delay {delay}, value3 {chance}, value4 {value4}"
        );
    }
    let Some(caster) = unit_caster(args, "SkillUse") else {
        return;
    };
    if chance != 0 && !super::super::chance_succeeds(chance) {
        caster.with_unit(|u| u.unit_mut().skill_state.condition_chance = false);
        return;
    }
    caster.with_unit(|u| u.unit_mut().skill_state.condition_chance = true);
    let Some(template) = server
        .game_data
        .systems
        .skills
        .skill(skill_id as u32)
        .cloned()
    else {
        tracing::error!(
            "Special effects: SkillUse skill {skill_id} has no template (NullReferenceException)"
        );
        return;
    };
    let skill = SkillRun::new(template, 1);
    let target_caster = SkillCastTarget::Unit(args.target.obj_id());
    buffs::trigger_remove_on(server, caster, BuffRemoveOn::UseSkill, 0);
    let caster = caster.clone();
    let target = args.target.as_obj().cloned();
    let originating_cast = args
        .source
        .skill
        .as_ref()
        .map(|skill| (skill.clone(), skill.state().generation));
    let caster_caster = args.caster_caster;
    let skill_object = args.skill_object.clone();
    super::super::super::tasks::schedule(server, i64::from(delay), move |srv| {
        if !super::super::super::geometry::is_live_object(&srv, &caster)
            || target
                .as_ref()
                .is_some_and(|target| !super::super::super::geometry::is_live_object(&srv, target))
        {
            return;
        }
        if originating_cast
            .as_ref()
            .is_some_and(|(skill, generation)| {
                let state = skill.state();
                state.cancelled || state.generation != *generation
            })
        {
            return;
        }
        let mut tc = target_caster;
        use_skill(
            &srv,
            &skill,
            &caster,
            caster_caster,
            &mut tc,
            Some(skill_object),
            true,
        );
    });
}

/// `ManaCost`: `value1 + value2 / 6.35` (skill modifiers applied) is taken from the character.
pub fn mana_cost(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "ManaCost", v);
    let Some(caster) = args.caster.as_ref() else {
        return;
    };
    let Some(p) = caster.as_player() else {
        return;
    };
    let Some(skill) = args.source.skill() else {
        tracing::error!("Special effects: ManaCost without a skill (NullReferenceException)");
        return;
    };
    let mana = apply_skill_modifiers(
        server,
        skill,
        caster,
        SkillAttribute::ManaCost,
        f64::from(v[0]) + f64::from(v[1]) / 6.35,
    );
    unit_combat::reduce_current_mp(server, caster, mana as i32);
    let mut c = p.data();
    c.skill_char.last_cast_ms = now_ms();
    c.skill_char.is_in_post_cast = true;
}

/// `Cooldown`: puts the skill on cooldown for `value1` ms (skill modifiers applied).
pub fn cooldown(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    if caster_is_player(args) {
        tracing::debug!(
            "Special effects: Cooldown cooldownTime {}, value2 {}, value3 {}, value4 {}",
            v[0],
            v[1],
            v[2],
            v[3]
        );
    }
    let Some(caster) = unit_caster(args, "Cooldown") else {
        return;
    };
    let Some(skill) = args.source.skill() else {
        tracing::error!("Special effects: Cooldown without a skill (NullReferenceException)");
        return;
    };
    let duration = apply_skill_modifiers(
        server,
        skill,
        caster,
        SkillAttribute::Cooldown,
        f64::from(v[0]),
    );
    let skill_id = skill.template.id;
    caster.with_unit(|u| {
        u.unit_mut()
            .cooldowns
            .add_cooldown(skill_id, duration as u32, now_ms())
    });
}

/// `GlobalCooldown`: `GlobalCooldown = now + value1 * (GlobalCooldownMul / 100)`.
pub fn global_cooldown(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "GlobalCooldown", v);
    let Some(caster) = unit_caster(args, "GlobalCooldown") else {
        return;
    };
    let mul = stat(server, caster, StatKind::GlobalCooldownMul) as f32;
    let ms = v[0] as f32 * (mul / 100.0);
    let until = now_ms() + f64::from(ms) as i64;
    caster.with_unit(|u| u.unit_mut().global_cooldown_ms = until);
}

/// `ResetCooldown`: `SCSkillCooldownResetPacket` for skill `value1` and / or tag `value2`
/// (characters only).
pub fn reset_cooldown(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    if caster_is_player(args) {
        tracing::debug!(
            "Special effects: ResetCooldown skillId {}, tagId {}, gcd {}, value4 {}",
            v[0],
            v[1],
            v[2],
            v[3]
        );
    }
    let gcd = v[2] == 1;
    let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) else {
        return;
    };
    {
        let mut player = p.data();
        if gcd {
            player.unit.global_cooldown_ms = 0;
        }
        if v[0] > 0 {
            player.unit.cooldowns.remove_cooldown(v[0] as u32);
        }
        for id in server.game_data.systems.skills.skills_by_tag(v[1] as u32) {
            player.unit.cooldowns.remove_cooldown(*id);
        }
    }
    if v[0] != 0 {
        // Character.ResetSkillCooldown(skillId, gcd)
        p.send(SCSkillCooldownResetPacket {
            obj_id: p.obj_id,
            skill_id: v[0] as u32,
            tag_id: 0,
            gcd,
        });
    }
    if v[1] != 0 {
        p.send(SCSkillCooldownResetPacket {
            obj_id: p.obj_id,
            skill_id: 0,
            tag_id: v[1] as u32,
            gcd,
        });
    }
}

/// `StopManaRegen`: `IsInPostCast = true`, `LastCast = now`.
pub fn stop_mana_regen(_server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "StopManaRegen", v);
    if let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) {
        let mut c = p.data();
        c.skill_char.is_in_post_cast = true;
        c.skill_char.last_cast_ms = now_ms();
    }
}

/// `AddBreath`: under water, `Breath = min(LungCapacity, Breath + value1)` (no packet, as the
/// C#). Negative changes clamp at zero.
pub fn add_breath(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "AddBreath", v);
    let Some(caster) = args.caster.as_ref() else {
        return;
    };
    let Some(p) = caster.as_player() else {
        return;
    };
    if !p.data().unit.is_under_water {
        return;
    }
    let lung = stat(server, caster, StatKind::LungCapacity) as u32;
    let mut c = p.data();
    let sum = i64::from(c.breath) + i64::from(v[0]);
    c.breath = sum.clamp(0, i64::from(lung)) as u32;
}

/// `CancelStealth`: `caster.Buffs.RemoveStealth()`.
pub fn cancel_stealth(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "CancelStealth", v);
    let Some(caster) = &args.caster else {
        tracing::error!("Special effects: CancelStealth without a caster (NullReferenceException)");
        return;
    };
    buffs::remove_stealth(server, caster);
}

/// `AutoAttack`: starts the auto attack with skill `value1` (default 2 = melee).
pub fn auto_attack(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    let Some(caster) = args.caster.as_ref() else {
        return;
    };
    let Some(p) = caster.as_player() else {
        return;
    };
    if p.data().unit.is_auto_attack {
        return;
    }
    let attack_skill_id = if v[0] > 0 { v[0] as u32 } else { 2 };
    let Some(template) = server
        .game_data
        .systems
        .skills
        .skill(attack_skill_id)
        .cloned()
    else {
        return;
    };
    let skill = SkillRun::new(template, 1);
    p.data().unit.is_auto_attack = true;
    start_auto_skill(server, caster, &skill);
}

/// `FinishChanneling`: `unit.ActivePlotState?.FinishChanneling()` of the target.
pub fn finish_channeling(_server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "FinishChanneling", v);
    if let Some(t) = args.target.as_obj() {
        if let Some(Some(plot)) = t.with_unit(|u| u.unit().skill_state.active_plot.clone()) {
            plot.0.finish_channeling();
        }
    }
}

/// `SetVariable`: plot variable `value1` += `value2` (operation 1) or = `value2` (operation 11)
/// on the skill's active plot state.
pub fn set_variable(_server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    let [index, value, operation, value4] = v;
    if caster_is_player(args) {
        tracing::debug!(
            "Special effects: SetVariable index {index}, value {value}, operation {operation}, value4 {value4}"
        );
    }
    let Some(skill) = args.source.skill() else {
        tracing::error!("Special effects: SetVariable without a skill (NullReferenceException)");
        return;
    };
    let plot = skill.state().active_plot.clone();
    match plot {
        Some(plot) => {
            if operation == 1 || operation == 11 {
                let mut inner = plot.inner();
                let Some(slot) = usize::try_from(index)
                    .ok()
                    .and_then(|i| inner.variables.get_mut(i))
                else {
                    tracing::error!(
                        "Special effects: SetVariable index {index} out of range (IndexOutOfRangeException)"
                    );
                    return;
                };
                if operation == 1 {
                    *slot = slot.wrapping_add(value);
                } else {
                    *slot = value;
                }
            } else {
                tracing::error!("Invalid Plot Variable Operation Kind.");
            }
        }
        None => tracing::error!("No active plot state located."),
    }
    tracing::trace!(
        "value1 {}, value2 {}, value3 {}, value4 {}",
        v[0],
        v[1],
        v[2],
        v[3]
    );
}

/// `ConsumeLaborPower`: the skill's `ConsumeLaborPower` is taken from the character.
pub fn consume_labor_power(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "ConsumeLaborPower", v);
    let Some(skill) = args.source.skill() else {
        tracing::error!(
            "Special effects: ConsumeLaborPower without a skill (NullReferenceException)"
        );
        return;
    };
    let t = &skill.template;
    if t.consume_labor_power > 0 {
        let Some(p) = args.caster.as_ref().and_then(WorldObject::as_player) else {
            tracing::error!(
                "Special effects: ConsumeLaborPower caster is not a Character (InvalidCastException)"
            );
            return;
        };
        progress::change_labor(
            server,
            p,
            (-t.consume_labor_power) as i16,
            t.actability_group_id,
        );
    }
}
