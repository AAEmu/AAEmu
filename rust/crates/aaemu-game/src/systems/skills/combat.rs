//! Unit combat behaviour: `Unit.ReduceCurrentHp` (+ the `Character` override),
//! `PostUpdateCurrentHp`, `ReduceCurrentMp`, `DoDie` (+ the npc hooks around it), `IsInBattle`,
//! aggro (`AddUnitAggro`, `ClearAggroOfUnit`, `ClearAllAggro`, `Tagging.AddTagger`),
//! `SetCriminalState`, `SetForceAttack`, `SetFaction`, `SetInvisible`, `OnZoneChange` (zone
//! group buffs).

use crate::models::buff::Buff;
use crate::packets::world::SCUnitPointsPacket;
use crate::packets::world_objects::SCTargetChangedPacket;
use crate::server::GameServer;
use crate::world::{now_ms, PlayerRef, WorldObject};

use super::buffs;
use super::geometry::{distance_to, Target};
use super::packets::{
    SCAiAggroPacket, SCCombatClearedPacket, SCCombatFirstHitPacket, SCForceAttackSetPacket,
    SCUnitDeathPacket, SCUnitFactionChangedPacket, SCUnitInvisiblePacket,
};
use super::state::{Aggro, AggroKind};
use super::types::{buff_constants, tags, BuffEventTriggerKind, KillReason, SkillCaster};
use super::{broadcast, send_to_players};

/// `LootingContainer.MaxLootingRange`.
pub const MAX_LOOTING_RANGE: f32 = 200.0;

fn unit_hp_mp(obj: &WorldObject) -> Option<(i32, i32)> {
    obj.with_unit(|u| (u.unit().hp, u.unit().mp))
}

/// `Unit.IsInBattle = value` (leaving combat broadcasts `SCCombatClearedPacket`).
pub fn set_in_battle(server: &GameServer, obj: &WorldObject, value: bool) {
    let changed = obj
        .with_unit(|u| {
            let u = u.unit_mut();
            if u.is_in_battle == value {
                false
            } else {
                u.is_in_battle = value;
                true
            }
        })
        .unwrap_or(false);
    if changed && !value {
        broadcast(
            server,
            obj,
            &SCCombatClearedPacket {
                obj_id: obj.obj_id(),
            },
            true,
        );
    }
}

/// `Character.ReduceCurrentHp` / `Unit.ReduceCurrentHp(attacker, value, killReason)`.
pub fn reduce_current_hp(
    server: &GameServer,
    target: &WorldObject,
    attacker: Option<&WorldObject>,
    mut value: i32,
    kill_reason: KillReason,
) {
    let cfg = server.systems.skills.config();
    if let Some(p) = target.as_player() {
        if cfg.god_mode {
            tracing::debug!(
                "{}'s damage disabled because of GodMode flag (normal damage: {value})",
                p.data().unit.name
            );
            return;
        }
        let in_duel = p.data().unit.is_in_duel;
        if in_duel {
            let mut c = p.data();
            c.unit.hp = (c.unit.hp - value).max(1);
            value = 0;
        }
    }
    // base.ReduceCurrentHp
    let Some((hp, _)) = unit_hp_mp(target) else {
        return;
    };
    if hp <= 0 {
        return;
    }
    let old_hp = hp;
    for index in buffs::absorption_effects(server, target) {
        value = buffs::consume_charge(server, target, index, value);
    }
    let (new_hp, mp) = target
        .with_unit(|u| {
            let u = u.unit_mut();
            u.hp = (u.hp - value).max(0);
            (u.hp, u.mp)
        })
        .unwrap_or((0, 0));
    broadcast(
        server,
        target,
        &SCUnitPointsPacket {
            obj_id: target.obj_id(),
            hp: new_hp,
            mp: if new_hp > 0 { mp } else { 0 },
        },
        true,
    );
    post_update_current_hp(server, target, attacker, old_hp, new_hp, kill_reason);

    // Character armor durability
    if let Some(p) = target.as_player() {
        use KillReason as K;
        let rate = match kill_reason {
            K::PvpSiege | K::PvpBattleField | K::PvpHonorWar | K::PvpEnemy | K::PvpAlly => {
                cfg.pvp_durability_loss_rate
            }
            K::Unknown | K::Damage => {
                if attacker.is_some_and(|a| a.as_player().is_some()) {
                    cfg.pvp_durability_loss_rate
                } else {
                    cfg.pve_durability_loss_rate
                }
            }
            _ => 0.0,
        };
        if rate > 0.0 {
            let items = server.systems.skills.ext.items();
            let rate = rate * items.durability_decrement_chance(server);
            items.durability_loss(server, p, 1, durability_targets::ALL_ARMOR, rate);
        }
    }
}

/// `DurabilityLossTargets`.
pub mod durability_targets {
    pub const ALL_ARMOR: u8 = 0;
    pub const ALL_WEAPONS: u8 = 1;
    pub const ALL_MAIN_WEAPONS: u8 = 2;
    pub const PRIMARY_WEAPON: u8 = 3;
    pub const SECONDARY_WEAPON: u8 = 4;
    pub const RANGED_WEAPON: u8 = 5;
    pub const SHIELD: u8 = 6;
}

/// `Unit.PostUpdateCurrentHp(attacker, oldHp, newHp, killReason)`.
pub fn post_update_current_hp(
    server: &GameServer,
    target: &WorldObject,
    attacker: Option<&WorldObject>,
    old_hp: i32,
    new_hp: i32,
    kill_reason: KillReason,
) {
    let Some((triggers, max_hp, hp)) = target.with_unit(|u| {
        let u = u.unit();
        (u.hp_trigger_points_percent.clone(), u.max_hp, u.hp)
    }) else {
        return;
    };
    if !triggers.is_empty() && max_hp != 0 {
        let old_p = (old_hp as f32 * 100.0 / max_hp as f32).round_ties_even() as i32;
        let new_p = (new_hp as f32 * 100.0 / max_hp as f32).round_ties_even() as i32;
        // DoHpChangeTrigger is a no-op for every unit kind ported here (npc AI hooks it).
        if old_p > new_p {
            if let Some(t) = triggers.iter().find(|t| old_p > **t && new_p <= **t) {
                tracing::trace!("hp trigger {t} (down) on {}", target.obj_id());
            }
        }
        if old_p < new_p {
            if let Some(t) = triggers.iter().find(|t| old_p < **t && new_p >= **t) {
                tracing::trace!("hp trigger {t} (up) on {}", target.obj_id());
            }
        }
    }
    if hp > 0 {
        return;
    }
    if attacker.is_some_and(WorldObject::is_unit) {
        // `attackerUnit.Events.OnKill` + dungeon `OnUnitKilled` / `OnUnitCombatEnd` / `OnDeath`
        // (systems::instances).
        crate::systems::instances::events::on_kill_in_reduce_hp(server, target);
    }
    do_die(server, target, attacker, kill_reason);
}

/// `ReduceCurrentMp(unit, value)`.
pub fn reduce_current_mp(server: &GameServer, target: &WorldObject, value: i32) {
    let Some((hp, mp)) = target.with_unit(|u| {
        let u = u.unit_mut();
        if u.hp == 0 {
            return (0, -1);
        }
        u.mp = (u.mp - value).max(0);
        (u.hp, u.mp)
    }) else {
        return;
    };
    if mp < 0 {
        return;
    }
    broadcast(
        server,
        target,
        &SCUnitPointsPacket {
            obj_id: target.obj_id(),
            hp,
            mp,
        },
        true,
    );
}

/// `Unit.DoDie(killer, killReason)` with the `Npc` override around it.
pub fn do_die(
    server: &GameServer,
    victim: &WorldObject,
    killer: Option<&WorldObject>,
    kill_reason: KillReason,
) {
    let npc_hooks = server.systems.skills.ext.npcs();
    if let Some(n) = victim.as_npc() {
        n.data().dead_time_ms = now_ms();
        npc_hooks.npc_before_death(server, n, killer);
    }
    super::skill::interrupt_skills(server, victim);
    set_in_battle(server, victim, false);
    // Events.OnDeath (buff triggers listening to the victim)
    buffs::fire_unit_event(server, victim.obj_id(), BuffEventTriggerKind::Death, 0);
    // ParentWorld.Events.OnUnitKilled + killerUnit?.Events.OnKill (systems::instances).
    crate::systems::instances::events::on_do_die(server, victim, killer);
    // Abusers that died leave the aggro tables they are in (OnAbuserDied).
    on_abuser_died(server, victim);
    buffs::remove_effects_on_death(server, victim);

    let mut lost_exp = 0;
    let mut durability_loss = 0u8;
    let mut wait = 0;
    if let Some(p) = victim.as_player() {
        let c = p.data();
        wait = c.rez_wait_duration;
        lost_exp = 0; // LastExpLoss: exp loss on death is a later phase
        durability_loss = 0;
        let _ = &mut durability_loss;
        let _ = &mut lost_exp;
    }
    if let Some(n) = victim.as_npc() {
        wait = npc_hooks.npc_respawn_time(server, n);
    }
    let Some(killer) = killer else {
        tracing::error!(
            "DoDie of {} without a killer (NullReferenceException in C#)",
            victim.obj_id()
        );
        if let Some(n) = victim.as_npc() {
            npc_hooks.npc_after_death(server, n, None);
        }
        return;
    };
    let killer_info = killer.is_unit().then(|| {
        (
            killer.obj_id(),
            killer.with_base_unit(|b| b.name().to_string()),
        )
    });
    broadcast(
        server,
        killer,
        &SCUnitDeathPacket {
            obj_id: victim.obj_id(),
            kill_reason,
            resurrect_wait_time: wait,
            lost_exp,
            durability_loss_rate: durability_loss,
            killer: killer_info,
        },
        true,
    );
    if killer.same(victim) {
        // DespawnMate (mates subsystem)
        if let Some(n) = victim.as_npc() {
            npc_hooks.npc_after_death(server, n, Some(killer));
        }
        return;
    }
    // LootingContainer.GenerateLoot(killer): part of the npc hooks (loot of NPCs).
    let current_target = victim.with_unit(|u| u.unit().current_target).unwrap_or(0);
    if current_target != 0 {
        send_to_players(
            server,
            &[victim.clone(), killer.clone()],
            &SCAiAggroPacket {
                npc_id: killer.obj_id(),
                count: 0,
                hostile_unit_id: 0,
                summarize_damage: 0,
                val2: 0,
                val3: 0,
            },
        );
        if killer.is_unit() {
            let killer_target = killer
                .with_unit(|u| {
                    u.unit_mut().summarize_damage = 0;
                    u.unit().current_target
                })
                .unwrap_or(0);
            if killer_target != 0 {
                if let Some(t) = server
                    .world
                    .object(killer_target)
                    .filter(WorldObject::is_unit)
                {
                    set_in_battle(server, &t, false);
                }
            }
            set_in_battle(server, killer, false);
        }
        broadcast(
            server,
            killer,
            &SCTargetChangedPacket {
                obj_id: killer.obj_id(),
                target_obj_id: 0,
            },
            true,
        );
        if let Some(p) = victim.as_player() {
            stop_auto_skill(p);
            set_in_battle(server, victim, false);
            p.data().dead_time = chrono::Utc::now().timestamp();
        }
        if let Some(kp) = killer.as_player() {
            let kt = kp.data().unit.current_target;
            if victim.as_player().is_some() && kt == victim.obj_id() {
                stop_auto_skill(kp);
                set_in_battle(server, killer, false);
            }
        }
        killer.with_unit(|u| u.unit_mut().current_target = 0);
    }
    if let Some(n) = victim.as_npc() {
        npc_hooks.npc_after_death(server, n, Some(killer));
    }
}

/// `StopAutoSkill(unit)`: cancels the auto attack task of a character.
pub fn stop_auto_skill(p: &PlayerRef) {
    let c = p.data();
    if let Some(a) = &c.unit.skill_state.auto_attack {
        a.cancel.cancel();
    }
}

// ------------------------------------------------------------------ aggro

/// `Tagging.AddTagger(unit, damage)` on `owner`.
fn add_tagger(server: &GameServer, owner: &WorldObject, unit: &WorldObject, damage: i32) {
    let mut check = unit.clone();
    if let Some(m) = unit.as_mate() {
        let owner_obj = m.data().owner_obj_id;
        if let Some(c) = server.world.character_by_obj_id(owner_obj) {
            check = WorldObject::from(c);
        }
    }
    let Some(player) = check.as_player().cloned() else {
        return;
    };
    let social = server.systems.skills.ext.social();
    let team = social.team_of(server, player.obj_id);
    let max_hp = owner.with_unit(|u| u.unit().max_hp).unwrap_or(0);
    let owner_target = Target::Obj(owner.clone());
    // members of the team within looting range (their damage counts for the team)
    let team_members: Vec<u32> = if team != 0 {
        let (_, members) = social.team_members(server, team);
        members
            .into_iter()
            .filter(|m| {
                server.world.character_by_obj_id(*m).is_some_and(|c| {
                    distance_to(
                        server,
                        &Target::Obj(WorldObject::from(c)),
                        &owner_target,
                        true,
                    ) <= MAX_LOOTING_RANGE
                })
            })
            .collect()
    } else {
        Vec::new()
    };
    owner.with_unit(|u| {
        let t = &mut u.unit_mut().skill_state.tagging;
        match t.taggers.iter_mut().find(|(id, _)| *id == player.obj_id) {
            Some((_, d)) => *d += damage,
            None => {
                t.taggers.push((player.obj_id, damage));
                if t.tagger == 0 {
                    t.tagger = player.obj_id;
                }
            }
        }
        if team != 0 {
            let party_damage: i32 = t
                .taggers
                .iter()
                .filter(|(id, _)| team_members.contains(id))
                .map(|(_, d)| *d)
                .sum();
            if f64::from(party_damage) > f64::from(max_hp) * 0.5 {
                t.tag_team = team;
            }
        } else if let Some((_, d)) = t.taggers.iter().find(|(id, _)| *id == player.obj_id) {
            if f64::from(*d) > f64::from(max_hp) * 0.5 {
                t.tagger = player.obj_id;
            }
        }
    });
}

/// `Unit.AddUnitAggro(kind, unit, amount)` on `owner`: `true` when the unit was already in the
/// table (the C# returns `isNewAggro = true` for an existing entry).
pub fn add_unit_aggro(
    server: &GameServer,
    owner: &WorldObject,
    kind: AggroKind,
    unit: &WorldObject,
    amount: i32,
) -> bool {
    if buffs::check_buff_tag(server, owner, tags::NO_FIGHT)
        || buffs::check_buff_tag(server, owner, tags::RETURNING)
    {
        clear_aggro_of_unit(server, owner, unit);
        return false;
    }
    if buffs::check_buff_tag(server, unit, tags::NO_FIGHT)
        || buffs::check_buff_tag(server, unit, tags::RETURNING)
    {
        clear_aggro_of_unit(server, owner, unit);
        return false;
    }
    if kind == AggroKind::Damage {
        add_tagger(server, owner, unit, amount);
    }
    let aggro_mul = super::stats::stat(server, unit, super::stats::StatKind::AggroMul);
    let incoming = super::stats::stat(server, owner, super::stats::StatKind::IncomingAggroMul);
    let amount = (amount as f32 * (aggro_mul as f32 / 100.0)) as i32;
    let amount = (amount as f32 * (incoming as f32 / 100.0)) as i32;
    let unit_id = unit.obj_id();
    let existed = owner
        .with_unit(|u| {
            let table = &mut u.unit_mut().skill_state.aggro_table;
            match table.get_mut(&unit_id) {
                Some(a) => {
                    a.add(kind, amount);
                    true
                }
                None => {
                    let mut a = Aggro::default();
                    a.add(kind, amount);
                    table.insert(unit_id, a);
                    false
                }
            }
        })
        .unwrap_or(false);
    let player = unit.as_player().cloned();
    if !existed {
        server
            .systems
            .skills
            .aggro_watch
            .lock()
            .expect("aggro watch poisoned")
            .entry(unit_id)
            .or_default()
            .insert(owner.obj_id());
        if let (Some(n), Some(p)) = (owner.as_npc(), &player) {
            let quest = {
                let npc = n.data();
                server
                    .systems
                    .skills
                    .ext
                    .npcs()
                    .npc_engage_combat_quest(server, &npc)
            };
            if quest > 0 {
                server
                    .systems
                    .skills
                    .ext
                    .quests()
                    .engage_combat_quest(server, p, quest, n.obj_id);
            }
        }
        send_to_players(
            server,
            &[owner.clone(), unit.clone()],
            &SCCombatFirstHitPacket {
                victim: owner.obj_id(),
                hitter: unit_id,
                hit_type: 0,
            },
        );
    }
    let Some(player) = player else {
        return existed;
    };
    let (total, dead) = owner
        .with_unit(|u| {
            let u = u.unit();
            (
                u.skill_state
                    .aggro_table
                    .get(&unit_id)
                    .map_or(0, Aggro::total),
                u.is_dead(),
            )
        })
        .unwrap_or((0, true));
    if total > 0 && !dead {
        let mut c = player.data();
        if !c.skill_char.is_in_aggro_list_of.contains(&owner.obj_id()) {
            c.skill_char.is_in_aggro_list_of.push(owner.obj_id());
        }
    }
    if let Some(n) = owner.as_npc() {
        server
            .systems
            .skills
            .ext
            .quests()
            .on_aggro(server, &player, n);
    }
    existed
}

/// `Unit.ClearAggroOfUnit(unit)` on `owner`.
pub fn clear_aggro_of_unit(server: &GameServer, owner: &WorldObject, unit: &WorldObject) {
    if let Some(tp) = unit.as_player() {
        tp.data()
            .skill_char
            .is_in_aggro_list_of
            .retain(|id| *id != owner.obj_id());
        if let Some(me) = owner.as_player() {
            let (me_id, target_id) = (me.character_id, tp.character_id);
            me.data()
                .skill_char
                .assault_on
                .retain(|id| *id != target_id);
            tp.data().skill_char.assaulted_by.retain(|id| *id != me_id);
        }
    }
    let unit_id = unit.obj_id();
    let (last, removed, now_count) = owner
        .with_unit(|u| {
            let table = &mut u.unit_mut().skill_state.aggro_table;
            let last = table.len();
            let removed = table.remove(&unit_id).is_some();
            (last, removed, table.len())
        })
        .unwrap_or((0, false, 0));
    if last == 0 {
        return;
    }
    if removed {
        if let Some(set) = server
            .systems
            .skills
            .aggro_watch
            .lock()
            .expect("aggro watch poisoned")
            .get_mut(&unit_id)
        {
            set.remove(&owner.obj_id());
        }
    } else {
        tracing::warn!(
            "Failed to remove unit[{unit_id}] aggro from NPC[{}]",
            owner.obj_id()
        );
    }
    if now_count != last {
        if let Some(n) = owner.as_npc() {
            server
                .systems
                .skills
                .ext
                .npcs()
                .npc_aggro_removed(server, n, unit_id);
        }
    }
}

/// `Unit.ClearAllAggro()` (taggers cleared, abuser event subscriptions dropped; the table
/// itself stays, as in the C# base implementation).
pub fn clear_all_aggro(server: &GameServer, owner: &WorldObject) {
    let ids: Vec<u32> = owner
        .with_unit(|u| {
            let s = &mut u.unit_mut().skill_state;
            s.tagging = Default::default();
            s.aggro_table.keys().copied().collect()
        })
        .unwrap_or_default();
    let mut watch = server
        .systems
        .skills
        .aggro_watch
        .lock()
        .expect("aggro watch poisoned");
    for id in ids {
        if let Some(set) = watch.get_mut(&id) {
            set.remove(&owner.obj_id());
        }
    }
}

/// `OnAbuserHealed`: every unit that has `healed` in its aggro table gains heal aggro from the
/// healer.
pub fn on_abuser_healed(
    server: &GameServer,
    healed: &WorldObject,
    healer: &WorldObject,
    amount: i32,
) {
    let holders: Vec<u32> = server
        .systems
        .skills
        .aggro_watch
        .lock()
        .expect("aggro watch poisoned")
        .get(&healed.obj_id())
        .map(|s| s.iter().copied().collect())
        .unwrap_or_default();
    for h in holders {
        if let Some(holder) = server.world.object(h) {
            add_unit_aggro(server, &holder, AggroKind::Heal, healer, amount);
        }
    }
}

/// `OnAbuserDied`: the dead unit leaves every aggro table it is in.
fn on_abuser_died(server: &GameServer, victim: &WorldObject) {
    let holders: Vec<u32> = server
        .systems
        .skills
        .aggro_watch
        .lock()
        .expect("aggro watch poisoned")
        .get(&victim.obj_id())
        .map(|s| s.iter().copied().collect())
        .unwrap_or_default();
    for h in holders {
        if let Some(holder) = server.world.object(h) {
            clear_aggro_of_unit(server, &holder, victim);
        }
    }
}

// ------------------------------------------------------------------ states

/// `Unit.SetCriminalState(criminalState, attackedTarget)`.
pub fn set_criminal_state(
    server: &GameServer,
    unit: &WorldObject,
    criminal: bool,
    attacked: &WorldObject,
) {
    if criminal {
        if attacked.as_npc().is_some_and(|n| n.data().portal.is_none()) {
            return;
        }
        buffs::add_buff_by_id(server, unit, buff_constants::RETRIBUTION, unit, 0);
    } else {
        buffs::remove_buff(server, unit, buff_constants::RETRIBUTION);
    }
}

/// `Unit.SetForceAttack(value)`.
pub fn set_force_attack(server: &GameServer, unit: &WorldObject, value: bool) {
    unit.with_unit(|u| u.unit_mut().force_attack = value);
    if value {
        buffs::add_buff_by_id(server, unit, buff_constants::BLOODLUST, unit, 0);
    } else {
        buffs::remove_buff(server, unit, buff_constants::BLOODLUST);
    }
    broadcast(
        server,
        unit,
        &SCForceAttackSetPacket {
            obj_id: unit.obj_id(),
            on: value,
        },
        true,
    );
}

/// `Unit.SetInvisible(value)`.
pub fn set_invisible(server: &GameServer, unit: &WorldObject, value: bool) {
    unit.with_unit(|u| u.unit_mut().invisible = value);
    broadcast(
        server,
        unit,
        &SCUnitInvisiblePacket {
            obj_id: unit.obj_id(),
            invisible: value,
        },
        true,
    );
}

/// `Unit.SetFaction(factionId)` (the `Character` override returns early for the same faction
/// and kicks from foreign guilds / parties: social subsystem).
pub fn set_faction(server: &GameServer, unit: &WorldObject, faction_id: u32) {
    let Some((old, name)) = unit.with_unit(|u| {
        let u = u.unit_mut();
        let old = u.faction_id;
        u.origin_faction_id = old;
        (old, u.name.clone())
    }) else {
        return;
    };
    if old == faction_id {
        tracing::info!("SetFaction: faction has already been established factionId={faction_id}");
    } else {
        unit.with_unit(|u| u.unit_mut().faction_id = faction_id);
        broadcast(
            server,
            unit,
            &SCUnitFactionChangedPacket {
                obj_id: unit.obj_id(),
                name,
                old_faction: old,
                new_faction: faction_id,
                temp: false,
            },
            true,
        );
        if faction_id == 161 {
            // FactionsEnum.Pirate
            buffs::add_buff_by_id(server, unit, buff_constants::CONTEMPTUOUS, unit, 0);
        } else {
            buffs::remove_buff(server, unit, buff_constants::CONTEMPTUOUS);
        }
    }
}

/// `Unit.OnZoneChange(lastZoneKey, newZoneKey)`: zone group buffs.
pub fn on_zone_change(server: &GameServer, unit: &WorldObject, last_zone: u32, new_zone: u32) {
    let zones = &server.game_data.zones;
    let last = zones.zone_by_key(last_zone).map_or(0, |z| z.group_id);
    let new = zones.zone_by_key(new_zone).map_or(0, |z| z.group_id);
    if last as i16 == new as i16 {
        return;
    }
    let data = &server.game_data.systems.skills;
    if zones.zone_by_key(last_zone).is_some() {
        if let Some(&buff) = data.zone_group_buffs.get(&last) {
            buffs::remove_buff(server, unit, buff);
        }
    }
    if zones.zone_by_key(new_zone).is_some() {
        if let Some(&buff) = data.zone_group_buffs.get(&new) {
            if let Some(t) = data.buff(buff).cloned() {
                let b = buffs::new_buff(
                    server,
                    &t,
                    Some(unit),
                    &SkillCaster::Unit(unit.obj_id()),
                    None,
                    now_ms(),
                );
                buffs::add_buff(server, unit, b, 0, 0);
            }
        }
    }
}

/// `Unit.CombatTick`: leaves combat `DefaultCombatTimeout` after the last combat activity;
/// characters also end their post-cast state 5 s after the last cast.
pub fn combat_tick(server: &GameServer, unit: &WorldObject) {
    let now = now_ms();
    let leave = unit
        .with_unit(|u| {
            let u = u.unit();
            u.is_in_battle
                && u.last_combat_activity_ms + super::relation::DEFAULT_COMBAT_TIMEOUT_MS < now
        })
        .unwrap_or(false);
    if leave {
        set_in_battle(server, unit, false);
    }
    if let Some(p) = unit.as_player() {
        let mut c = p.data();
        if c.skill_char.is_in_post_cast && c.skill_char.last_cast_ms + 5000 < now {
            c.skill_char.is_in_post_cast = false;
        }
    }
}

/// A buff of `unit` by id (helper).
pub fn buff_of(unit: &WorldObject, buff_id: u32) -> Option<Buff> {
    unit.with_base_unit(|b| b.buffs().effect_from_buff_id(buff_id).cloned())
}

/// `Unit.DoFallDamage(fallVel)` (+ the `Character` override that ignores fall damage from
/// `IgnoreFallDamageAccessLevel` up). Returns the damage dealt. Called by the movement code.
pub fn do_fall_damage(server: &GameServer, unit: &WorldObject, fall_vel: u16) -> i32 {
    if let Some(p) = unit.as_player() {
        let access = p.data().access_level;
        if access >= server.config.world.ignore_fall_damage_access_level {
            tracing::debug!("FallDamage negated because of IgnoreFallDamageAccessLevel settings");
            return 0;
        }
    }
    let Some((hp, max_hp, mul)) = unit.with_unit(|u| {
        let u = u.unit();
        (
            u.hp,
            u.max_hp,
            u.calculate_with_bonuses(
                0.0,
                crate::models::unit::UnitAttribute::FallDamageMul,
                now_ms(),
            ) / 100.0,
        )
    }) else {
        return 0;
    };
    let mut fall_dmg =
        max_hp.min((max_hp as f32 * ((i32::from(fall_vel) - 8600) as f32 / 15000.0)) as i32);
    let min_hp_left = max_hp / 20;
    let max_dmg_left = hp - min_hp_left;
    fall_dmg = (f64::from(fall_dmg) + f64::from(fall_dmg) * mul) as i32;
    if fall_vel >= 32000 {
        reduce_current_hp(server, unit, Some(unit), hp, KillReason::Damage);
    } else if fall_dmg < max_dmg_left {
        reduce_current_hp(server, unit, Some(unit), fall_dmg, KillReason::Damage);
    } else {
        if min_hp_left == 0 {
            tracing::error!("DoFallDamage: MaxHp / 20 == 0 (DivideByZeroException)");
            return fall_dmg;
        }
        let duration = 500 * (fall_dmg / min_hp_left);
        buffs::add_buff_by_id(server, unit, buff_constants::FALL_STUN, unit, duration);
        if hp > min_hp_left {
            reduce_current_hp(server, unit, Some(unit), max_dmg_left, KillReason::Damage);
        }
    }
    broadcast(
        server,
        unit,
        &super::packets::SCEnvDamagePacket {
            source: super::packets::env_source::FALLING,
            target: unit.obj_id(),
            amount: fall_dmg as u32,
            gimmick_id: 0,
        },
        true,
    );
    fall_dmg
}
