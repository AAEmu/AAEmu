//! Character progression used by the skill code and its packets: `Character.AddExp`
//! (`Abilities.AddActiveExp`, level up), `ChangeLabor` (`Actability.AddPoint`, labor exp),
//! `ChangeGamePoints`, `CharacterSkills.AddSkill` / `AddBuff` / `Reset`,
//! `CharacterAbilities.Swap` and `PassiveBuff.Apply` / `Remove`.

use std::collections::HashMap;

use aaemu_commons::{Marshal, PacketStream};

use crate::data::formulas::FormulaKind;
use crate::models::char_data::{ability, Skill};
use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;
use crate::packets::packet_def;
use crate::server::GameServer;
use crate::world::{now_ms, PlayerRef, WorldObject};

use super::broadcast;
use super::packets::{
    SCAbilitySwappedPacket, SCBuffLearnedPacket, SCExpChangedPacket, SCSkillLearnedPacket,
    SCSkillsResetPacket,
};
use super::types::SkillCaster;

/// `GamePointKind`.
pub const GAME_POINT_HONOR: u8 = 0;
pub const GAME_POINT_VOCATION: u8 = 1;

/// `SCCharacterLaborPowerChangedPacket(amount, action, point, step)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCCharacterLaborPowerChangedPacket {
    pub amount: i32,
    pub action: i32,
    pub point: i32,
    pub step: u8,
}
packet_def!(
    SCCharacterLaborPowerChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharacterLaborPowerChangedPacket
);
impl Marshal for SCCharacterLaborPowerChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.amount)
            .write_i32(self.action)
            .write_i32(self.point)
            .write_u8(self.step);
    }
}

/// `SCGamePointChangedPacket(kind, amount)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCGamePointChangedPacket {
    pub kind: u8,
    pub amount: i32,
}
packet_def!(
    SCGamePointChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCGamePointChangedPacket
);
impl Marshal for SCGamePointChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.kind).write_i32(self.amount);
    }
}

/// `SCLevelChangedPacket(objId, level)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCLevelChangedPacket {
    pub obj_id: u32,
    pub level: u8,
}
packet_def!(
    SCLevelChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLevelChangedPacket
);
impl Marshal for SCLevelChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_u8(self.level);
    }
}

/// `Actability.GetLaborCostMultiplier()` (1.2 values by rank).
pub fn labor_cost_multiplier(step: u8) -> f32 {
    const M: [f32; 8] = [1.00, 1.00, 0.95, 0.90, 0.85, 0.80, 0.77, 0.77];
    M[usize::from(step).min(M.len() - 1)]
}

/// `Actability.GetExpMultiplier()` (all 1.0 in 1.2).
fn actability_exp_multiplier(_step: u8) -> f32 {
    1.0
}

/// `Character.AddExp(expDelta, shouldAddAbilityExp)`.
pub fn add_exp(server: &GameServer, p: &PlayerRef, mut exp_delta: i32, add_ability_exp: bool) {
    if exp_delta == 0 {
        return;
    }
    if exp_delta > 0 {
        exp_delta = (f64::from(exp_delta) * server.config.world.exp_rate) as i32;
    }
    let exp_table = &server.game_data.experience;
    let (leveled_up, level) = {
        let mut c = p.data();
        let mut new_exp = c.experience.wrapping_add(exp_delta);
        let (new_level, overflow) = match exp_table.level_from_exp(new_exp, c.unit.level, false) {
            Ok(v) => v,
            Err(e) => {
                tracing::error!("AddExp for {}: {e}", c.id);
                return;
            }
        };
        let leveled = new_level > c.unit.level;
        if new_level >= exp_table.max_player_level() {
            new_exp -= overflow;
        }
        c.experience = new_exp;
        c.unit.level = new_level;
        if add_ability_exp {
            // Abilities.AddActiveExp
            let max_exp = exp_table.exp_for_level(exp_table.max_player_level(), false);
            let actives = [c.ability1, c.ability2, c.ability3];
            for a in actives {
                if a == ability::NONE {
                    continue;
                }
                if let Some(ab) = c.abilities.get_mut(a) {
                    ab.exp = (ab.exp + exp_delta).min(max_exp);
                }
            }
        }
        (leveled, new_level)
    };
    p.send(SCExpChangedPacket {
        obj_id: p.obj_id,
        exp: exp_delta,
        should_add_ability_exp: add_ability_exp,
    });
    if leveled_up {
        // Expedition?.OnCharacterRefresh: guild member refresh (social subsystem)
        let obj = WorldObject::from(p);
        broadcast(
            server,
            &obj,
            &SCLevelChangedPacket {
                obj_id: p.obj_id,
                level,
            },
            true,
        );
        super::stats::refresh_cached_stats(server, &obj);
    }
    // QuestManager.DoOnLevelUpEvents: quests subsystem
}

/// `Character.ChangeLabor(change, actabilityId)`.
pub fn change_labor(server: &GameServer, p: &PlayerRef, change: i16, actability_id: i32) {
    let mut actability_change = 0;
    let mut actability_step = 0u8;
    let mut exp_mul = 1.0f32;
    if actability_id > 0 {
        let limits = &server.game_data.characters.expert_limits;
        let mut c = p.data();
        match c
            .actability
            .actabilities
            .iter_mut()
            .find(|a| a.id == actability_id as u32)
        {
            Some(a) => {
                exp_mul = actability_exp_multiplier(a.step);
                let add = (f64::from(change).abs() * server.systems.skills.config().actability_rate)
                    as i32;
                actability_step = a.step;
                // CharacterActability.AddPoint
                let prev = a.point;
                a.point += add;
                if let Some(l) = limits.iter().find(|l| l.id == u32::from(a.step)) {
                    if a.point > l.up_limit {
                        a.point = l.up_limit;
                    }
                }
                actability_change = a.point - prev;
            }
            None => {
                tracing::error!(
                    "ChangeLabor: actability {actability_id} not found (KeyNotFoundException)"
                );
                return;
            }
        }
    }
    if change < 0 {
        let level = p.data().unit.level;
        let params = HashMap::from([
            ("labor_power", f64::from(-change)),
            ("pc_level", f64::from(level)),
        ]);
        let xp = server
            .game_data
            .formulas
            .get_formula_by_kind(FormulaKind::ExpByLaborPower)
            .map_or(0.0, |f| f.evaluate(&params));
        add_exp(server, p, (xp * f64::from(exp_mul)) as i32, true);
    }
    {
        let mut c = p.data();
        c.labor_power = c.labor_power.wrapping_add(change);
        c.labor_power_modified = chrono::Utc::now().timestamp();
    }
    p.send(SCCharacterLaborPowerChangedPacket {
        amount: i32::from(change),
        action: actability_id,
        point: actability_change,
        step: actability_step,
    });
}

/// `Character.ChangeGamePoints(kind, change)`.
pub fn change_game_points(server: &GameServer, p: &PlayerRef, kind: u8, mut change: i32) {
    match kind {
        GAME_POINT_HONOR => p.data().honor_point += change,
        GAME_POINT_VOCATION => {
            let obj = WorldObject::from(p);
            let (add, mul) = obj
                .with_unit(|u| {
                    let now = now_ms();
                    (
                        u.unit().calculate_with_bonuses(
                            0.0,
                            crate::models::unit::UnitAttribute::LivingPointGain,
                            now,
                        ) as f32,
                        u.unit().calculate_with_bonuses(
                            0.0,
                            crate::models::unit::UnitAttribute::LivingPointGainMul,
                            now,
                        ) as f32,
                    )
                })
                .unwrap_or((0.0, 0.0));
            change = (change as f32 + add).round_ties_even() as i32;
            let voc_mul = mul + 100.0;
            change = (change as f32 * (voc_mul / 100.0)).round_ties_even() as i32;
            p.data().vocation_point += change;
            let _ = server;
        }
        _ => {
            tracing::error!("ChangeGamePoints - Unknown Game Point Type {kind}");
            return;
        }
    }
    p.send(SCGamePointChangedPacket {
        kind,
        amount: change,
    });
}

fn active_abilities(p: &PlayerRef) -> [u8; 3] {
    let c = p.data();
    [c.ability1, c.ability2, c.ability3]
}

/// `CharacterSkills.AddSkill(skillId)` (`CSLearnSkillPacket`).
pub fn learn_skill(server: &GameServer, p: &PlayerRef, skill_id: u32) {
    let skills = &server.game_data.skills;
    let Some(t) = skills.skill(skill_id) else {
        tracing::error!("AddSkill: skill {skill_id} has no template (NullReferenceException)");
        return;
    };
    let actives = active_abilities(p);
    if t.ability_id > 0 && !actives.contains(&t.ability_id) {
        return;
    }
    let (level, used) = {
        let c = p.data();
        (c.unit.level, c.skills.used_skill_points(skills, 0))
    };
    let points = server.game_data.experience.skill_points_for_level(level) - used;
    if t.skill_points > points {
        return;
    }
    let existing = p
        .data()
        .skills
        .skills
        .iter()
        .find(|s| s.id == skill_id)
        .copied();
    let skill = match existing {
        Some(s) => s,
        None => {
            let ab = {
                let c = p.data();
                crate::db_character::ab_level(server, &c, t.ability_id)
            };
            let level = if t.level_step > 0 {
                ((ab - t.ability_level) / t.level_step + 1) as u8
            } else {
                1
            };
            let s = Skill { id: t.id, level };
            p.data().skills.skills.push(s);
            s
        }
    };
    p.send(SCSkillLearnedPacket {
        skill_id: skill.id,
        level: skill.level,
    });
}

/// `CharacterSkills.AddBuff(buffId)` (`CSLearnBuffPacket`).
pub fn learn_buff(server: &GameServer, p: &PlayerRef, buff_id: u32) {
    let skills = &server.game_data.skills;
    let Some(t) = skills.passive_buff(buff_id).copied() else {
        tracing::error!("AddBuff: passive buff {buff_id} has no template (NullReferenceException)");
        return;
    };
    let actives = active_abilities(p);
    if t.ability_id > 0 && !actives.contains(&t.ability_id) {
        return;
    }
    let (level, used_all, used_tree, known) = {
        let c = p.data();
        (
            c.unit.level,
            c.skills.used_skill_points(skills, 0),
            c.skills.used_skill_points(skills, t.ability_id),
            c.skills.passive_buffs.contains(&buff_id),
        )
    };
    let points = server.game_data.experience.skill_points_for_level(level) - used_all;
    if points < 1 || used_tree < t.req_points || known {
        return;
    }
    p.data().skills.passive_buffs.push(buff_id);
    let obj = WorldObject::from(p);
    broadcast(
        server,
        &obj,
        &SCBuffLearnedPacket {
            obj_id: p.obj_id,
            buff_id,
        },
        true,
    );
    apply_passive_buff(server, p, buff_id);
}

/// `PassiveBuff.Apply(owner)`.
pub fn apply_passive_buff(server: &GameServer, p: &PlayerRef, passive_id: u32) {
    let Some(pt) = server.game_data.skills.passive_buff(passive_id).copied() else {
        return;
    };
    let Some(t) = server.game_data.systems.skills.buff(pt.buff_id).cloned() else {
        tracing::error!("PassiveBuff {passive_id}: no buff template {}", pt.buff_id);
        return;
    };
    let obj = WorldObject::from(p);
    let mut b = super::buffs::new_buff(
        server,
        &t,
        Some(&obj),
        &SkillCaster::Unit(0),
        None,
        now_ms(),
    );
    b.passive = true;
    super::buffs::add_buff(server, &obj, b, 0, 0);
}

/// `PassiveBuff.Remove(owner)`.
pub fn remove_passive_buff(server: &GameServer, p: &PlayerRef, passive_id: u32) {
    if let Some(pt) = server.game_data.skills.passive_buff(passive_id).copied() {
        super::buffs::remove_buff(server, &WorldObject::from(p), pt.buff_id);
    }
}

/// `CharacterSkills.Reset(abilityId)` (`CSResetSkillsPacket`).
pub fn reset_skills(server: &GameServer, p: &PlayerRef, ability_id: u8) {
    let skills = &server.game_data.skills;
    let removed_buffs: Vec<u32> = {
        let mut c = p.data();
        let mut removed = Vec::new();
        let keep: Vec<Skill> = c
            .skills
            .skills
            .iter()
            .filter(|s| {
                let ab = skills.skill(s.id).map_or(0, |t| t.ability_id);
                if ab == ability_id {
                    removed.push(s.id);
                    false
                } else {
                    true
                }
            })
            .copied()
            .collect();
        c.skills.skills = keep;
        let mut buffs = Vec::new();
        let keep_buffs: Vec<u32> = c
            .skills
            .passive_buffs
            .iter()
            .filter(|b| {
                let ab = skills.passive_buff(**b).map_or(0, |t| t.ability_id);
                if ab == ability_id {
                    buffs.push(**b);
                    false
                } else {
                    true
                }
            })
            .copied()
            .collect();
        c.skills.passive_buffs = keep_buffs;
        c.skills.removed.extend(removed);
        c.skills.removed.extend(buffs.iter().copied());
        buffs
    };
    for b in removed_buffs {
        remove_passive_buff(server, p, b);
    }
    let obj = WorldObject::from(p);
    broadcast(
        server,
        &obj,
        &SCSkillsResetPacket {
            obj_id: p.obj_id,
            ability: ability_id,
        },
        true,
    );
}

/// `CharacterAbilities.Swap(oldAbilityId, abilityId)` (`CSSwapAbilityPacket`).
pub fn swap_ability(server: &GameServer, p: &PlayerRef, old: u8, new: u8) {
    reset_skills(server, p, old);
    {
        let mut c = p.data();
        if c.ability1 == old {
            c.ability1 = new;
            if let Some(a) = c.abilities.get_mut(new) {
                a.order = 0;
            }
        } else if c.ability2 == old {
            c.ability2 = new;
            if let Some(a) = c.abilities.get_mut(new) {
                a.order = 1;
            }
            if old == ability::NONE {
                let exp1 = c
                    .abilities
                    .abilities
                    .iter()
                    .find(|a| a.id == c.ability1)
                    .map_or(0, |a| a.exp);
                let a2 = c.ability2;
                if let Some(a) = c.abilities.get_mut(a2) {
                    a.exp = exp1;
                }
            }
        } else if c.ability3 == old {
            c.ability3 = new;
            if let Some(a) = c.abilities.get_mut(new) {
                a.order = 2;
            }
            if old == ability::NONE {
                let exp1 = c
                    .abilities
                    .abilities
                    .iter()
                    .find(|a| a.id == c.ability1)
                    .map_or(0, |a| a.exp);
                let a3 = c.ability3;
                if let Some(a) = c.abilities.get_mut(a3) {
                    a.exp = exp1;
                }
                let actives = [c.ability1, c.ability2, c.ability3];
                for a in c.abilities.abilities.iter_mut() {
                    if !actives.contains(&a.id) {
                        a.exp = 42000;
                    }
                }
            }
        }
        if old != ability::NONE {
            if let Some(a) = c.abilities.get_mut(old) {
                a.order = 255;
            }
        }
    }
    let obj = WorldObject::from(p);
    broadcast(
        server,
        &obj,
        &SCAbilitySwappedPacket {
            obj_id: p.obj_id,
            old_ability: old,
            ability: new,
        },
        true,
    );
}
