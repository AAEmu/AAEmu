//! `DamageEffect`, `HealEffect`, `BuffEffect`, `DispelEffect`, `RestoreManaEffect`,
//! `ManaBurnEffect`, `AggroEffect`, `FlyingStateChangeEffect`, `SkillControllerTemplate.Apply`
//! and `CombatBuffs.TriggerCombatBuffs`.

use crate::packets::world::SCUnitPointsPacket;
use crate::server::GameServer;
use crate::world::{now_ms, WorldObject};

use super::super::buffs;
use super::super::combat::{self as unit_combat, durability_targets};
use super::super::data::effects::{
    AggroEffect, BuffEffect, DamageEffect, DispelEffect, HealEffect, ManaBurnEffect,
    RestoreManaEffect,
};
use super::super::data::{EffectKind, EffectTemplate, SkillControllerTemplate};
use super::super::geometry::Target;
use super::super::packets::{
    CompressedGamePackets, SCAiAggroPacket, SCUnitDamagedPacket, SCUnitHealedPacket,
};
use super::super::relation::{can_attack, owner_character, relation_to};
use super::super::state::AggroKind;
use super::super::stats::{equip_slot, stat, stats, StatKind};
use super::super::types::{
    buff_constants, BuffEventTriggerKind, BuffRemoveOn, CastAction, DamageType, HealHitType,
    HealType, KillReason, RelationState, SkillCaster, SkillHitType,
};
use super::super::{broadcast, send_to_players};
use super::{rand_float, rand_int, EffectArgs};

/// An effect nobody handles yet.
pub fn unrouted_effect(
    _server: &GameServer,
    kind: EffectKind,
    template: &EffectTemplate,
    args: &EffectArgs<'_>,
) {
    tracing::debug!(
        "{} {} on {} not handled (owning subsystem not ported)",
        kind.name(),
        template.id(),
        args.target.obj_id()
    );
}

/// `CombatBuffs.TriggerCombatBuffs(attacker, receiver, type, isHeal)` of `owner`.
pub fn trigger_combat_buffs(
    server: &GameServer,
    owner: &WorldObject,
    attacker: &WorldObject,
    hit_type: SkillHitType,
    is_heal: bool,
) {
    let Some(list) = owner.with_unit(|u| {
        u.unit()
            .combat_buffs
            .by_hit_type
            .get(&(hit_type as u8))
            .cloned()
            .unwrap_or_default()
    }) else {
        return;
    };
    for cb in list {
        if cb.is_heal_spell != is_heal {
            continue;
        }
        let owner_is_attacker = owner.same(attacker);
        if cb.buff_to_source && !owner_is_attacker {
            continue;
        }
        if !cb.buff_to_source && owner_is_attacker {
            continue;
        }
        tracing::warn!(
            "[{}, Req:{}] BTS: {} BFS: {} HT: {}",
            cb.buff_id,
            cb.req_buff_id,
            cb.buff_to_source,
            cb.buff_from_source,
            cb.hit_type
        );
        let Some(t) = server.game_data.systems.skills.buff(cb.buff_id).cloned() else {
            tracing::error!("TriggerCombatBuffs: buff {} has no template", cb.buff_id);
            continue;
        };
        if !buffs::check_buff_immune(server, owner, cb.buff_id) {
            let b = buffs::new_buff(
                server,
                &t,
                Some(owner),
                &SkillCaster::Unit(owner.obj_id()),
                None,
                now_ms(),
            );
            buffs::add_buff(server, owner, b, 0, 0);
        }
    }
}

fn level_range(lvl_md: f32, skill_level: i32, va_start: i32, va_end: i32) -> (f32, f32) {
    let level_modifier = ((skill_level - 1) / 49 * (va_end - va_start) + va_start) as f32 * 0.01;
    (
        lvl_md - level_modifier * lvl_md + 0.5,
        (level_modifier + 1.0) * lvl_md + 0.5,
    )
}

/// `DamageEffect.Apply`.
pub fn damage(
    server: &GameServer,
    e: &DamageEffect,
    args: &EffectArgs<'_>,
    packets: Option<&mut CompressedGamePackets>,
) {
    let Some(trg) = args.target.as_obj().filter(|o| o.is_unit()).cloned() else {
        return;
    };
    if trg.with_unit(|u| u.unit().hp).unwrap_or(0) <= 0 {
        return;
    }
    let Some(caster) = args.caster.clone() else {
        tracing::warn!(
            "No caster defined for DamageEffect {}, with targetObjId {}",
            e.id,
            trg.obj_id()
        );
        return;
    };
    let damage_type = DamageType::from_raw(i64::from(e.damage_type));
    let blocked_fish = crate::systems::economy::fishing::blocks_character_fish_damage(
        server,
        &trg,
        &caster,
        args.source.skill().map(|skill| skill.template.id),
    );
    if !blocked_fish {
        buffs::trigger_remove_on(server, &trg, BuffRemoveOn::AttackedEtc, 0);
        buffs::trigger_remove_on(server, &caster, BuffRemoveOn::AttackEtc, 0);
    }
    if blocked_fish || buffs::check_damage_immune(server, &trg, damage_type) {
        let mut p = SCUnitDamagedPacket::new(
            args.cast,
            args.caster_caster,
            caster.obj_id(),
            trg.obj_id(),
            1,
            0,
        );
        p.hit_type = SkillHitType::Immune;
        broadcast(server, &trg, &p, false);
        return;
    }
    let items = server.systems.skills.ext.items();
    let holdable = if caster.is_unit() {
        items.weapon_holdable(server, &caster, e.weapon_slot_id)
    } else {
        None
    };
    let skill = args.source.skill();
    if let Some(s) = skill {
        if let Some(hit) = s.hit_type(trg.obj_id()) {
            if s.skill_missed(trg.obj_id()) {
                let mut p = SCUnitDamagedPacket::new(
                    args.cast,
                    args.caster_caster,
                    caster.obj_id(),
                    trg.obj_id(),
                    0,
                    0,
                );
                p.holdable_id = holdable.map_or(0, |h| h.holdable_id as u8);
                p.hit_type = hit;
                trigger_combat_buffs(server, &trg, &caster, hit, false);
                trigger_combat_buffs(server, &caster, &caster, hit, false);
                broadcast(server, &caster, &p, true);
                return;
            }
        }
    }
    use StatKind as S;
    let cs = stats(
        server,
        &caster,
        &[
            S::MeleeCritical,
            S::SpellCritical,
            S::RangedCritical,
            S::LevelDps,
            S::DpsInc,
            S::MDps,
            S::MDpsInc,
            S::RangedDpsInc,
            S::Dps,
            S::OffhandDps,
            S::RangedDps,
            S::MeleeDamageMul,
            S::SpellDamageMul,
            S::RangedDamageMul,
            S::MeleeCriticalBonus,
            S::RangedCriticalBonus,
            S::SpellCriticalBonus,
            S::DefensePenetration,
            S::MagicPenetration,
        ],
    );
    let ts = stats(
        server,
        &trg,
        &[
            S::Flexibility,
            S::BattleResist,
            S::Armor,
            S::MagicResistance,
            S::IncomingMeleeDamageMul,
            S::IncomingRangedDamageMul,
            S::IncomingSpellDamageMul,
            S::IncomingDamageMul,
        ],
    );
    let flexibility = ts[0] as i32;
    let flex_rate_mod = (flexibility / 1000 * 3) as f32;
    let hit_type = match damage_type {
        Some(DamageType::Melee) => {
            if rand_float(0.0, 100.0) < cs[0] as f32 - flex_rate_mod {
                SkillHitType::MeleeCritical
            } else {
                SkillHitType::MeleeHit
            }
        }
        Some(DamageType::Magic) => {
            if rand_float(0.0, 100.0) < cs[1] as f32 - flex_rate_mod {
                SkillHitType::SpellCritical
            } else {
                SkillHitType::SpellHit
            }
        }
        Some(DamageType::Ranged) => {
            if rand_float(0.0, 100.0) < cs[2] as f32 - flex_rate_mod {
                SkillHitType::RangedCritical
            } else {
                SkillHitType::RangedHit
            }
        }
        Some(DamageType::Siege) => SkillHitType::RangedHit,
        _ => SkillHitType::Invalid,
    };
    let mut min = 0.0f32;
    let mut max = 0.0f32;
    let (mut level_min, mut level_max) = (0.0f32, 0.0f32);
    if e.use_level_damage {
        let lvl_md = cs[3] as f32 * e.level_md;
        let (a, b) = level_range(
            lvl_md,
            args.source.skill_level(),
            e.level_va_start,
            e.level_va_end,
        );
        level_min += a;
        level_max += b;
    }
    let dps_inc = match damage_type {
        Some(DamageType::Melee) => cs[4] as i32,
        Some(DamageType::Magic) => cs[5] as i32 + cs[6] as i32,
        Some(DamageType::Ranged) => cs[7] as i32,
        _ => 0,
    };
    max += dps_inc as f32 * 0.001 * e.dps_inc_multiplier;
    let mut weapon_damage = 0.0f32;
    if e.use_mainhand_weapon {
        weapon_damage = cs[8] as i32 as f32 * 0.001;
    }
    if e.use_offhand_weapon {
        weapon_damage += cs[9] as i32 as f32 * 0.001;
    }
    if e.use_ranged_weapon {
        weapon_damage += cs[10] as i32 as f32 * 0.001;
    }
    max += e.dps_multiplier * weapon_damage;
    let cast_time_mod = skill.map_or(0, |s| s.template.casting_time);
    let min_cast_bonus = if cast_time_mod <= 1000 {
        if min > 0.0 {
            min
        } else {
            1000.0
        }
    } else {
        cast_time_mod as f32
    };
    let variable_damage = max * min_cast_bonus * 0.001;
    if e.weapon_slot_id < 0 {
        min = variable_damage + level_min;
        max = variable_damage + level_max;
    } else if let Some(h) = holdable {
        let scaled = h.damage_scale * variable_damage * 0.01;
        min = level_min + (variable_damage - scaled);
        max = level_max + (variable_damage + scaled);
    }
    min *= e.multiplier;
    max *= e.multiplier;
    let damage_mul = match damage_type {
        Some(DamageType::Melee) => cs[11] as f32,
        Some(DamageType::Magic) => cs[12] as f32,
        Some(DamageType::Ranged) => cs[13] as f32,
        _ => 1.0,
    };
    min = (min * damage_mul).floor();
    max = (max * damage_mul).ceil();
    if let Some(s) = skill {
        let tags = server
            .game_data
            .systems
            .skills
            .skill_tags(s.template.id)
            .to_vec();
        caster.with_unit(|u| {
            let m = &u.unit().skill_modifiers;
            min = m.apply(
                s.template.id,
                &tags,
                crate::models::unit::SkillAttribute::Damage,
                f64::from(min),
            ) as f32;
            max = m.apply(
                s.template.id,
                &tags,
                crate::models::unit::SkillAttribute::Damage,
                f64::from(max),
            ) as f32;
        });
    }
    if let Some(b) = &args.source.buff {
        if !b.tick_effects.is_empty() {
            if b.duration != 0 {
                min = (f64::from(min) * (b.tick / f64::from(b.duration))) as f32;
                max = (f64::from(max) * (b.tick / f64::from(b.duration))) as f32;
            }
            buffs::trigger_remove_on(server, &caster, BuffRemoveOn::DamageEtcDot, 0);
            buffs::trigger_remove_on(server, &trg, BuffRemoveOn::DamagedEtcDot, 0);
            if damage_type == Some(DamageType::Magic) {
                buffs::trigger_remove_on(server, &caster, BuffRemoveOn::DamageSpellDot, 0);
                buffs::trigger_remove_on(server, &trg, BuffRemoveOn::DamagedSpellDot, 0);
            }
        }
    }
    if let Some(s) = skill {
        let per = |charges: i32| {
            charges as f32 * (e.charged_mul + f32::from(s.level) * e.charged_level_mul)
        };
        if e.use_charged_buff {
            let effect = unit_combat::buff_of(&caster, e.charged_buff_id);
            let charges = effect.as_ref().map_or(0, |b| b.charge);
            min += per(charges);
            max += per(charges);
            if let Some(b) = effect {
                buffs::exit(server, &caster, b.index, false);
            }
        }
        if e.use_target_charged_buff {
            // The C# reads ChargedBuffId (not TargetChargedBuffId) here.
            let effect = unit_combat::buff_of(&trg, e.charged_buff_id);
            let charges = effect.as_ref().map_or(0, |b| b.charge);
            min += per(charges);
            max += per(charges);
            if let Some(b) = effect {
                buffs::exit(server, &trg, b.index, false);
            }
        }
    }
    if e.use_fixed_damage {
        min = e.fixed_min as f32;
        max = e.fixed_max as f32;
    }
    let mut final_damage = rand_float(min, max);
    if e.target_buff_tag_id > 0 && buffs::check_buff_tag(server, &trg, e.target_buff_tag_id) {
        final_damage *= e.target_buff_bonus_mul;
    }
    let battle_resist = ts[1] as i32;
    if caster.as_player().is_some() && trg.as_player().is_some() {
        final_damage *= 1.0 - battle_resist as f32 / (8000.0 + battle_resist as f32);
    }
    let flex_bonus = (flexibility / 100) as f32;
    match hit_type {
        SkillHitType::MeleeCritical => final_damage *= 1.0 + (cs[14] as f32 - flex_bonus) / 100.0,
        SkillHitType::RangedCritical => final_damage *= 1.0 + (cs[15] as f32 - flex_bonus) / 100.0,
        SkillHitType::SpellCritical => final_damage *= 1.0 + (cs[16] as f32 - flex_bonus) / 100.0,
        _ => {}
    }
    let mut reduction_mul = 1.0f32;
    match damage_type {
        Some(DamageType::Melee) => {
            let armor = (ts[2] as f32 - cs[17] as f32).max(0.0);
            reduction_mul = 1.0 - armor / (armor + 5300.0);
            final_damage *= ts[4] as f32;
        }
        Some(DamageType::Ranged) => {
            let armor = (ts[2] as f32 - cs[17] as f32).max(0.0);
            reduction_mul = 1.0 - armor / (armor + 5300.0);
            final_damage *= ts[5] as f32;
        }
        Some(DamageType::Magic) => {
            let armor = (ts[3] as f32 - cs[18] as f32).max(0.0);
            reduction_mul = 1.0 - armor / (armor + 5300.0);
            final_damage *= ts[6] as f32;
        }
        _ => final_damage *= ts[7] as f32,
    }
    let value = (final_damage * reduction_mul) as i32;
    let absorbed = (final_damage * (1.0 - reduction_mul)) as i32;
    let health_stolen = (value as f32 * (e.health_steal_ratio as f32 / 100.0)) as i32;
    let mana_stolen = (value as f32 * (e.mana_steal_ratio as f32 / 100.0)) as i32;
    let caster_t = Target::Obj(caster.clone());
    let trg_t = Target::Obj(trg.clone());
    let test_drive = matches!(args.cast, CastAction::Buff { buff_id: 6151, .. });
    if !test_drive && !can_attack(server, &caster_t, &trg_t) {
        return;
    }
    unit_combat::reduce_current_hp(server, &trg, Some(&caster), value, KillReason::Damage);
    caster.with_unit(|u| u.unit_mut().summarize_damage += value);
    if health_stolen > 0 || mana_stolen > 0 {
        let pts = caster.with_unit(|u| {
            let u = u.unit_mut();
            u.hp = u.max_hp.min(u.hp + health_stolen);
            u.mp = u.max_mp.min(u.mp + mana_stolen);
            (u.hp, u.mp)
        });
        if let Some((hp, mp)) = pts {
            broadcast(
                server,
                &caster,
                &SCUnitPointsPacket {
                    obj_id: caster.obj_id(),
                    hp,
                    mp,
                },
                true,
            );
        }
    }
    if e.check_crime && relation_to(server, &caster_t, &trg_t) == RelationState::Friendly {
        if !trg.with_base_unit(|b| b.buffs().check_buff(buff_constants::RETRIBUTION)) {
            unit_combat::set_criminal_state(server, &caster, true, &trg);
        }
        let target_owner = owner_character(server, &trg);
        let source_owner = owner_character(server, &caster);
        if let (Some(t), Some(s)) = (target_owner, source_owner) {
            let (t_id, s_id) = (t.character_id, s.character_id);
            let fresh = !s.data().skill_char.assaulted_by.contains(&t_id)
                && !t.data().skill_char.assaulted_by.contains(&s_id);
            if fresh {
                t.data().skill_char.assaulted_by.push(s_id);
                s.data().skill_char.assault_on.push(t_id);
                server
                    .systems
                    .skills
                    .ext
                    .social()
                    .evidence_from_damage(server, &caster, &trg);
            }
        }
    }
    let now = now_ms();
    let trg_alive = trg.with_unit(|u| u.unit().hp > 0).unwrap_or(false);
    unit_combat::set_in_battle(server, &trg, trg_alive);
    trg.with_unit(|u| u.unit_mut().last_combat_activity_ms = now);
    if let (Some(tp), Some(_)) = (trg.as_player(), caster.as_player()) {
        let mut c = tp.data();
        let hp = &mut c.skill_char.hostile_players;
        match hp.iter_mut().find(|(id, _)| *id == caster.obj_id()) {
            Some(e) => e.1 = now,
            None => hp.push((caster.obj_id(), now)),
        }
    }
    if caster.is_unit() {
        let was = caster.with_unit(|u| u.unit().is_in_battle).unwrap_or(false);
        unit_combat::set_in_battle(server, &caster, was | trg_alive);
        caster.with_unit(|u| u.unit_mut().last_combat_activity_ms = now);
    }
    trigger_combat_buffs(server, &caster, &caster, hit_type, false);
    trigger_combat_buffs(server, &trg, &caster, hit_type, false);
    let mut p = SCUnitDamagedPacket::new(
        args.cast,
        args.caster_caster,
        caster.obj_id(),
        trg.obj_id(),
        value,
        absorbed,
    );
    p.holdable_id = holdable.map_or(0, |h| h.holdable_id as u8);
    p.hit_type = hit_type;
    match packets {
        Some(b) => b.add(&p),
        None => broadcast(server, &trg, &p, true),
    }
    if let Some(n) = trg.as_npc() {
        let summarize = caster.with_unit(|u| u.unit().summarize_damage).unwrap_or(0);
        send_to_players(
            server,
            &[trg.clone(), caster.clone()],
            &SCAiAggroPacket {
                npc_id: trg.obj_id(),
                count: 1,
                hostile_unit_id: caster.obj_id(),
                summarize_damage: summarize,
                val2: 0,
                val3: 0,
            },
        );
        // Npc.OnDamageReceived: aggro + AI (npc subsystem)
        unit_combat::add_unit_aggro(server, &trg, AggroKind::Damage, &caster, value);
        server
            .systems
            .skills
            .ext
            .npcs()
            .npc_damage_received(server, n, &caster, value);
    }
    buffs::fire_unit_event(server, caster.obj_id(), BuffEventTriggerKind::Attack, 0);
    if value > 0 {
        buffs::fire_unit_event(server, caster.obj_id(), BuffEventTriggerKind::Damage, value);
        buffs::trigger_remove_on(server, &caster, BuffRemoveOn::DamageEtc, 0);
        buffs::fire_unit_event(server, trg.obj_id(), BuffEventTriggerKind::Damaged, value);
        let typed = match damage_type {
            Some(DamageType::Melee) => Some(BuffEventTriggerKind::DamagedMelee),
            Some(DamageType::Ranged) => Some(BuffEventTriggerKind::DamagedRanged),
            Some(DamageType::Magic) => Some(BuffEventTriggerKind::DamagedSpell),
            Some(DamageType::Siege) => Some(BuffEventTriggerKind::DamagedSiege),
            _ => None,
        };
        if let Some(k) = typed {
            buffs::fire_unit_event(server, trg.obj_id(), k, value);
        }
        buffs::trigger_remove_on(server, &trg, BuffRemoveOn::DamagedEtc, 0);
    }
    if let Some(cp) = caster.as_player() {
        let mut target = durability_targets::ALL_WEAPONS;
        if e.use_mainhand_weapon {
            target = durability_targets::PRIMARY_WEAPON;
        }
        if e.use_offhand_weapon {
            target = durability_targets::SECONDARY_WEAPON;
        }
        if e.use_ranged_weapon {
            target = durability_targets::RANGED_WEAPON;
        }
        let cfg = server.systems.skills.config();
        let mut rate = if trg.as_player().is_some() {
            cfg.pvp_durability_loss_rate
        } else {
            cfg.pve_durability_loss_rate
        };
        if rate > 0.0 {
            rate *= items.durability_decrement_chance(server);
            items.durability_loss(server, cp, 1, target, rate);
        }
    }
    let _ = equip_slot::MAINHAND;
}

/// `HealEffect.Apply`.
pub fn heal(
    server: &GameServer,
    e: &HealEffect,
    args: &EffectArgs<'_>,
    packets: Option<&mut CompressedGamePackets>,
) {
    let Some(trg) = args.target.as_obj().filter(|o| o.is_unit()).cloned() else {
        return;
    };
    if trg.with_unit(|u| u.unit().hp).unwrap_or(0) <= 0 {
        return;
    }
    let Some(caster) = args.caster.clone() else {
        tracing::error!(
            "HealEffect {} without a caster (NullReferenceException)",
            e.id
        );
        return;
    };
    use StatKind as S;
    let cs = stats(
        server,
        &caster,
        &[
            S::LevelDps,
            S::HDps,
            S::HealCritical,
            S::HealCriticalBonus,
            S::HealMul,
        ],
    );
    let mut min = 0.0f32;
    let mut max = 0.0f32;
    let (mut level_min, mut level_max) = (0.0f32, 0.0f32);
    if e.use_level_heal {
        let lvl_md = cs[0] as f32 * e.level_md;
        let (a, b) = level_range(
            lvl_md,
            args.source.skill_level(),
            e.level_va_start,
            e.level_va_end,
        );
        level_min += a;
        level_max += b;
    }
    max += cs[1] as i32 as f32 * 0.001 * e.dps_multiplier;
    let cast_time_mod = args.source.skill().map_or(0, |s| s.template.casting_time);
    let min_cast_bonus = if cast_time_mod <= 1000 {
        if min > 0.0 {
            min
        } else {
            1000.0
        }
    } else {
        cast_time_mod as f32
    };
    let variable = max * min_cast_bonus * 0.001;
    min = variable + level_min;
    max = variable + level_max;
    let mut tick_modifier = 1.0f32;
    if let Some(b) = &args.source.buff {
        if !b.tick_effects.is_empty() && b.duration != 0 {
            tick_modifier = (b.tick / f64::from(b.duration)) as f32;
        }
    }
    min *= tick_modifier;
    max *= tick_modifier;
    if e.use_charged_buff {
        if let Some(b) = unit_combat::buff_of(&caster, e.charged_buff_id) {
            min += e.charged_mul * b.charge as f32;
            max += e.charged_mul * b.charge as f32;
            buffs::exit(server, &caster, b.index, false);
        }
    }
    let critical = rand_float(0.0, 100.0) < cs[2] as f32;
    let mut value = rand_float(min, max) as i32;
    if critical {
        value = (value as f32 * (1.0 + cs[3] as f32 / 100.0)) as i32;
        trigger_combat_buffs(server, &caster, &caster, SkillHitType::SpellCritical, true);
    }
    let incoming = stat(server, &trg, S::IncomingHealMul) as f32;
    value = (value as f32 * incoming) as i32;
    if e.use_fixed_heal {
        let Some(v) = rand_int(e.fixed_min, e.fixed_max) else {
            tracing::error!(
                "HealEffect {}: fixed_min > fixed_max (ArgumentOutOfRangeException)",
                e.id
            );
            return;
        };
        value = v;
        if args.source.buff.is_some() && args.source.is_trigger {
            value = (value as f32 / 1000.0 * args.source.amount as f32) as i32;
        } else {
            value = (value as f32 * tick_modifier) as i32;
        }
    }
    value = (value as f32 * cs[4] as f32) as i32;
    if let Some(p) = caster.as_player() {
        if e.actability_group_id > 0 {
            let act = p
                .data()
                .actability
                .actabilities
                .iter()
                .find(|a| a.id == e.actability_group_id)
                .copied();
            if let Some(a) = act {
                if e.actability_step != 0 {
                    let steps = a.point / e.actability_step;
                    if e.actability_add != 0.0 {
                        value += (steps as f32 * e.actability_add) as i32;
                    }
                    if e.actability_mul != 0.0 {
                        value += (value as f32 * steps as f32 * e.actability_mul) as i32;
                    }
                } else {
                    tracing::error!(
                        "HealEffect {}: actability_step 0 (DivideByZeroException)",
                        e.id
                    );
                }
            }
        }
    }
    let hit = if critical {
        HealHitType::CriticalHealHit
    } else {
        HealHitType::HealHit
    };
    let packet = SCUnitHealedPacket {
        cast_action: args.cast,
        skill_caster: args.caster_caster,
        target_id: trg.obj_id(),
        heal_type: HealType::Health,
        heal_hit_type: hit,
        value,
    };
    match packets {
        Some(b) => b.add(&packet),
        None => broadcast(server, &trg, &packet, true),
    }
    let Some((old, hp, mp)) = trg.with_unit(|u| {
        let u = u.unit_mut();
        let old = u.hp;
        u.hp = (u.hp + value).min(u.max_hp);
        (old, u.hp, u.mp)
    }) else {
        return;
    };
    broadcast(
        server,
        &trg,
        &SCUnitPointsPacket {
            obj_id: trg.obj_id(),
            hp,
            mp,
        },
        true,
    );
    // PvP assist tracking: a healer on the target's side earns assist credit for the target's
    // next PvP kill (within 30s window).
    if let (Some(healer), Some(target)) = (caster.as_player(), trg.as_player()) {
        if healer.character_id != target.character_id && target.data().unit.is_in_battle {
            unit_combat::record_pvp_heal_from(target, healer.character_id);
        }
    }
    unit_combat::on_abuser_healed(server, &trg, &caster, value);
    unit_combat::post_update_current_hp(server, &trg, Some(&trg), old, hp, KillReason::Unknown);
}

/// `BuffEffect.Apply`.
pub fn buff_effect(server: &GameServer, e: &BuffEffect, args: &EffectArgs<'_>) {
    let Some(target) = args.target.as_obj().cloned() else {
        return;
    };
    let Some(buff_id) = e.buff_id else {
        tracing::error!(
            "BuffEffect {} has no buff template (NullReferenceException)",
            e.id
        );
        return;
    };
    let Some(t) = server.game_data.systems.skills.buff(buff_id).cloned() else {
        return;
    };
    if target.is_unit() {
        if let Some(s) = args.source.skill() {
            if s.hit_type(target.obj_id()).is_some() && s.skill_missed(target.obj_id()) {
                return;
            }
        }
    }
    if let Some(c) = &args.caster {
        let roll = rand::Rng::gen_range(&mut rand::thread_rng(), 0..101);
        let ok = roll <= e.chance;
        c.with_unit(|u| u.unit_mut().skill_state.condition_chance = ok);
        if !ok {
            return;
        }
    }
    if t.require_buff_id > 0 && !target.with_base_unit(|b| b.buffs().check_buff(t.require_buff_id))
    {
        return;
    }
    if buffs::check_buff_immune(server, &target, t.id) {
        return;
    }
    let ab_level = buffs::buff_ab_level(server, args.caster.as_ref(), args.source.skill());
    let mut b = buffs::new_buff(
        server,
        &t,
        args.caster.as_ref(),
        &args.caster_caster,
        args.source.skill(),
        now_ms(),
    );
    b.ab_level = ab_level;
    buffs::add_buff(server, &target, b, 0, 0);
    let relation = args.caster.as_ref().map_or(RelationState::Neutral, |c| {
        relation_to(server, &Target::Obj(c.clone()), &args.target)
    });
    if t.kind == 2
        && target.as_npc().is_none()
        && relation == RelationState::Friendly
        && args.caster.as_ref().is_some_and(|c| !c.same(&target))
        && !target.with_base_unit(|b| b.buffs().check_buff(buff_constants::RETRIBUTION))
    {
        if let Some(c) = args.caster.as_ref().filter(|c| c.is_unit()) {
            unit_combat::set_criminal_state(server, c, true, &target);
        }
    }
}

/// `DispelEffect.Apply`.
pub fn dispel(server: &GameServer, e: &DispelEffect, args: &EffectArgs<'_>) {
    let Some(target) = args.target.as_obj().cloned() else {
        return;
    };
    if e.buff_tag_id > 0 && !buffs::check_buffs_by_tag(server, &target, e.buff_tag_id) {
        return;
    }
    let Some(caster) = args.caster_target() else {
        tracing::error!(
            "DispelEffect {} without a caster (NullReferenceException)",
            e.id
        );
        return;
    };
    let attackable = can_attack(server, &caster, &args.target);
    if e.dispel_count > 0 && attackable {
        buffs::remove_buffs_by_kind(server, &target, 1, e.dispel_count, e.buff_tag_id);
    }
    if e.cure_count > 0 && !can_attack(server, &caster, &args.target) {
        buffs::remove_buffs_by_kind(server, &target, 2, e.cure_count, e.buff_tag_id);
    }
}

/// `RestoreManaEffect.Apply`.
pub fn restore_mana(server: &GameServer, e: &RestoreManaEffect, args: &EffectArgs<'_>) {
    let Some(trg) = args.target.as_obj().filter(|o| o.is_unit()).cloned() else {
        return;
    };
    let mut min = 0i32;
    let mut max = 0i32;
    if e.use_fixed_value {
        min += e.fixed_min;
        max += e.fixed_max;
    }
    let mut unk = 0.0f32;
    let mut skill_level = 1;
    if let Some(s) = args.source.skill() {
        skill_level = (i32::from(s.level) - 1) * s.template.level_step + s.template.ability_level;
        if skill_level >= s.template.ability_level {
            unk = 0.15 * (skill_level - s.template.ability_level + 1) as f32;
        }
    }
    if e.use_level_value {
        let Some(c) = &args.caster else {
            tracing::error!("RestoreManaEffect {} without a caster", e.id);
            return;
        };
        let level_dps = stat(server, c, StatKind::LevelDps) as f32;
        let level_md = (unk + 1.0) * e.level_md;
        min += (level_dps * level_md + 0.5) as i32;
        max += ((((skill_level - 1) as f32
            * 0.020408163
            * (e.level_va_end - e.level_va_start) as f32
            + e.level_va_start as f32)
            * 0.01
            + 1.0)
            * level_dps
            * level_md
            + 0.5) as i32;
    }
    let Some(value) = rand_int(min, max) else {
        tracing::error!(
            "RestoreManaEffect {}: min > max (ArgumentOutOfRangeException)",
            e.id
        );
        return;
    };
    broadcast(
        server,
        &trg,
        &SCUnitHealedPacket {
            cast_action: args.cast,
            skill_caster: args.caster_caster,
            target_id: trg.obj_id(),
            heal_type: HealType::Mana,
            heal_hit_type: HealHitType::HealHit,
            value,
        },
        true,
    );
    let Some((hp, mp)) = trg.with_unit(|u| {
        let u = u.unit_mut();
        u.mp = (u.mp + value).min(u.max_mp);
        (u.hp, u.mp)
    }) else {
        return;
    };
    broadcast(
        server,
        &trg,
        &SCUnitPointsPacket {
            obj_id: trg.obj_id(),
            hp,
            mp,
        },
        true,
    );
}

/// `ManaBurnEffect.Apply`.
pub fn mana_burn(server: &GameServer, e: &ManaBurnEffect, args: &EffectArgs<'_>) {
    let Some(caster) = &args.caster else {
        tracing::error!("ManaBurnEffect {} without a caster", e.id);
        return;
    };
    let mut min = e.base_min as f32;
    let mut max = e.base_max as f32;
    let lvl_md = stat(server, caster, StatKind::LevelDps) as f32 * e.level_md;
    let (a, b) = level_range(
        lvl_md,
        args.source.skill_level(),
        e.level_va_start,
        e.level_va_end,
    );
    min += a;
    max += b;
    if let Some(bt) = &args.source.buff {
        if !bt.tick_effects.is_empty() && bt.duration != 0 {
            min = (f64::from(min) * (bt.tick / f64::from(bt.duration))) as f32;
            max = (f64::from(max) * (bt.tick / f64::from(bt.duration))) as f32;
        }
    }
    let final_damage = rand_float(min, max);
    if let Some(trg) = args.target.as_obj().filter(|o| o.is_unit()) {
        unit_combat::reduce_current_mp(server, trg, final_damage as i32);
        let mut p = SCUnitDamagedPacket::new(
            args.cast,
            args.caster_caster,
            caster.obj_id(),
            trg.obj_id(),
            0,
            0,
        );
        p.mana_burn = final_damage as i32;
        broadcast(server, trg, &p, true);
    }
}

/// `AggroEffect.Apply`.
pub fn aggro(server: &GameServer, e: &AggroEffect, args: &EffectArgs<'_>) {
    let Some(caster) = args.caster.as_ref().filter(|c| c.as_player().is_some()) else {
        return;
    };
    let Some(npc) = args.target.as_obj().filter(|t| t.as_npc().is_some()) else {
        return;
    };
    let mut min = 0.0f32;
    let mut max = 0.0f32;
    if e.use_level_aggro {
        let lvl_md = stat(server, caster, StatKind::LevelDps) as f32 * e.level_md;
        let (a, b) = level_range(
            lvl_md,
            args.source.skill_level(),
            e.level_va_start,
            e.level_va_end,
        );
        min += a;
        max += b;
    }
    if e.use_charged_buff {
        if let Some(b) = unit_combat::buff_of(caster, e.charged_buff_id) {
            min += e.charged_mul * b.charge as f32;
            max += e.charged_mul * b.charge as f32;
            buffs::exit(server, caster, b.index, false);
        }
    }
    if e.use_fixed_aggro {
        min += e.fixed_min as f32;
        max += e.fixed_max as f32;
    }
    let value = rand_float(min, max) as i32;
    send_to_players(
        server,
        &[caster.clone(), npc.clone()],
        &SCAiAggroPacket {
            npc_id: npc.obj_id(),
            count: 1,
            hostile_unit_id: caster.obj_id(),
            summarize_damage: value,
            val2: 0,
            val3: 0,
        },
    );
    unit_combat::add_unit_aggro(server, npc, AggroKind::Damage, caster, value);
}

/// `FlyingStateChangeEffect.Apply` (`SCUnitFlyingStateChangedPacket`).
pub fn flying_state_change(server: &GameServer, template: &EffectTemplate, args: &EffectArgs<'_>) {
    let EffectTemplate::FlyingStateChange { flying_state, .. } = template else {
        return;
    };
    let Some(caster) = &args.caster else {
        return;
    };
    broadcast(
        server,
        caster,
        &super::super::packets::SCUnitFlyingStateChangedPacket {
            obj_id: caster.obj_id(),
            flying: *flying_state,
        },
        true,
    );
}

/// `SkillControllerTemplate.Apply`: runs the controller on a non-character caster (the moved
/// unit; the target is the reference point).
pub fn skill_controller(server: &GameServer, t: &SkillControllerTemplate, args: &EffectArgs<'_>) {
    let Some(caster) = args.caster.as_ref().filter(|c| c.is_unit()) else {
        return;
    };
    if caster.as_player().is_some() {
        return;
    }
    if args.target.as_obj().is_none() && !matches!(args.target, Target::Pos(_)) {
        tracing::warn!(
            "SkillControllerTemplate.Apply: target is null for sc_id={} kind={}, skipping",
            t.id,
            t.kind_id
        );
        return;
    }
    match super::super::controllers::create(server, t, caster, &args.target, 0.0, 0.0, 0.0, 0.0) {
        Some(sc) => super::super::controllers::activate(server, &sc),
        None => tracing::warn!(
            "SkillControllerTemplate.Apply: factory returned null for sc_id={} kind={} — controller class not implemented",
            t.id,
            t.kind_id
        ),
    }
}

/// Damage effect helper for the tests: the level-damage range of `DamageEffect`.
pub fn level_damage_range(
    level_dps: f32,
    level_md: f32,
    skill_level: i32,
    va_start: i32,
    va_end: i32,
) -> (f32, f32) {
    level_range(level_dps * level_md, skill_level, va_start, va_end)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn level_range_matches_csharp_integer_math() {
        // (Level - 1) / 49 is an integer division: levels 1..49 use VaStart only.
        let (lo, hi) = level_damage_range(100.0, 1.0, 10, 80, 120);
        assert!((lo - (100.0 - 0.8 * 100.0 + 0.5)).abs() < 1e-4);
        assert!((hi - (1.8 * 100.0 + 0.5)).abs() < 1e-4);
        let (lo, hi) = level_damage_range(100.0, 1.0, 50, 80, 120);
        assert!((lo - (100.0 - 1.2 * 100.0 + 0.5)).abs() < 1e-4);
        assert!((hi - (2.2 * 100.0 + 0.5)).abs() < 1e-4);
    }
}
