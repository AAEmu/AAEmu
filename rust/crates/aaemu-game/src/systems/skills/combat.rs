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
        // PvP assist tracking: remember who hit us recently
        if let Some(enemy) = attacker.and_then(WorldObject::as_player) {
            if value > 0 && enemy.character_id != p.character_id {
                record_pvp_damage_from(p, enemy.character_id);
            }
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

/// `DurabilityLossTargets` (the C# enum values).
pub mod durability_targets {
    pub const ALL: u8 = 0;
    pub const ALL_ARMOR: u8 = 1;
    pub const ALL_WEAPONS: u8 = 2;
    pub const ALL_MAIN_WEAPONS: u8 = 3;
    pub const PRIMARY_WEAPON: u8 = 4;
    pub const SECONDARY_WEAPON: u8 = 5;
    pub const RANGED_WEAPON: u8 = 6;
    pub const SHIELD: u8 = 7;
    pub const FEET: u8 = 8;
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
        // DoHpChangeTrigger: the vehicle overrides (slave repair points); npc AI hooks it.
        if old_p > new_p {
            if let Some(t) = triggers.iter().find(|t| old_p > **t && new_p <= **t) {
                tracing::trace!("hp trigger {t} (down) on {}", target.obj_id());
                crate::systems::vehicles::on_hp_change_trigger(server, target, *t);
            }
        }
        if old_p < new_p {
            if let Some(t) = triggers.iter().find(|t| old_p < **t && new_p >= **t) {
                tracing::trace!("hp trigger {t} (up) on {}", target.obj_id());
                crate::systems::vehicles::on_hp_change_trigger(server, target, *t);
            }
        }
    }
    if old_hp != new_hp {
        crate::systems::housing::dominion::runtime::on_hp_changed(
            server, target, attacker, old_hp, new_hp,
        );
    }
    if let Some(house) = target.as_house() {
        crate::systems::housing::manager::mark_hp_changed(house, old_hp, new_hp);
        let model_max_hp = {
            let h = house.data();
            crate::systems::ai::collision::objects::house_collision_max_hp(&h)
        };
        let objects = crate::systems::ai::collision::objects::house_model_state;
        if objects(old_hp, model_max_hp) != objects(new_hp, model_max_hp) {
            crate::systems::ai::collision::objects::sync_house(server, house);
        }
    }
    if hp > 0 {
        return;
    }
    // attackerUnit.Events.OnKill(attackerUnit, { Target = attackerUnit }) (sic, C#); the
    // dungeon `OnUnitKilled` world event is the instances subsystem's.
    if let Some(ap) = attacker.and_then(WorldObject::as_player) {
        crate::systems::quests::events::on_kill(server, ap, attacker, None, None);
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
    if crate::systems::housing::dominion::runtime::handle_tower_death(server, victim) {
        return;
    }
    // `Slave.DoDie` replaces the base implementation (vehicles subsystem).
    if crate::systems::vehicles::on_do_die(server, victim, killer, kill_reason) {
        return;
    }
    let npc_hooks = server.systems.skills.ext.npcs();
    if let Some(n) = victim.as_npc() {
        n.data().dead_time_ms = now_ms();
        npc_hooks.npc_before_death(server, n, killer);
    }
    super::skill::interrupt_skills(server, victim);
    set_in_battle(server, victim, false);
    // Events.OnDeath (buff triggers listening to the victim, `Shipyard.OnDeath`)
    buffs::fire_unit_event(server, victim.obj_id(), BuffEventTriggerKind::Death, 0);
    // killerUnit?.Events.OnKill(this, { Killer, Victim }) (quest subscribers of a player)
    if let Some(kp) = killer.and_then(WorldObject::as_player) {
        crate::systems::quests::events::on_kill(server, kp, None, killer, Some(victim));
    }
    // ParentWorld.Events.OnUnitKilled + killerUnit?.Events.OnKill (systems::instances).
    crate::systems::instances::events::on_do_die(server, victim, killer);
    // House.OnDeath
    crate::systems::housing::on_unit_died(server, victim);
    crate::systems::vehicles::on_death_event(server, victim);
    // Abusers that died leave the aggro tables they are in (OnAbuserDied).
    on_abuser_died(server, victim);
    buffs::remove_effects_on_death(server, victim);

    let mut lost_exp = 0;
    let mut durability_loss = 0u8;
    let mut wait = 0;
    if let Some(p) = victim.as_player() {
        // Escalating respawn timer — runs BEFORE base.DoDie sends SCUnitDeathPacket
        compute_death_wait_time(p, now_ms());
        (lost_exp, durability_loss) = character_death_losses(server, p);
        wait = p.data().rez_wait_duration;
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
    do_die_base(
        server,
        victim,
        killer,
        kill_reason,
        wait,
        lost_exp,
        durability_loss,
    );
    if let Some(n) = victim.as_npc() {
        npc_hooks.npc_after_death(server, n, Some(killer));
    }
    if let Some(p) = victim.as_player() {
        // ApplyDurabilityLossToEquipment(LastDurabilityLoss, All, -1f)
        if durability_loss > 0 {
            server.systems.skills.ext.items().durability_loss(
                server,
                p,
                i32::from(durability_loss),
                durability_targets::ALL,
                -1.0,
            );
        }
        character_death_tail(server, p, victim, killer);
    }
}

/// `DeathWaitTimesSeconds`: escalating death respawn wait times; resets after
/// `DeathCountResetMinutes` (5) without dying.
const DEATH_WAIT_TIMES_SECONDS: [i32; 9] = [15, 30, 60, 90, 120, 150, 180, 210, 240];
const DEATH_COUNT_RESET_MS: i64 = 5 * 60_000;
/// `PvpAssistWindowSeconds`.
const PVP_ASSIST_WINDOW_MS: i64 = 30_000;
/// `WarZoneHonorLoss` (clamped to the victim's current honor).
const WAR_ZONE_HONOR_LOSS: i32 = 10;

/// `RecordPvpDamageFrom(attacker)`.
pub fn record_pvp_damage_from(victim: &PlayerRef, attacker_id: u32) {
    record_at(
        &mut victim.data().skill_char.pvp_damage_history,
        attacker_id,
        now_ms(),
    );
}

/// `RecordPvpHealFrom(healer)`.
pub fn record_pvp_heal_from(target: &PlayerRef, healer_id: u32) {
    record_at(
        &mut target.data().skill_char.pvp_heal_history,
        healer_id,
        now_ms(),
    );
}

fn record_at(history: &mut Vec<(u32, i64)>, id: u32, now: i64) {
    match history.iter_mut().find(|(i, _)| *i == id) {
        Some(e) => e.1 = now,
        None => history.push((id, now)),
    }
}

/// `ComputeDeathWaitTime()`: stores the escalating wait in `RezWaitDuration` and `DeadTime`.
pub fn compute_death_wait_time(p: &PlayerRef, now: i64) {
    let mut c = p.data();
    let s = &mut c.skill_char;
    if s.last_death_time_ms != 0 && now - s.last_death_time_ms >= DEATH_COUNT_RESET_MS {
        s.consecutive_death_count = 0;
    }
    let index = s
        .consecutive_death_count
        .min(DEATH_WAIT_TIMES_SECONDS.len() - 1);
    let wait_seconds = DEATH_WAIT_TIMES_SECONDS[index];
    s.consecutive_death_count += 1;
    s.last_death_time_ms = now;
    let count = s.consecutive_death_count;
    c.rez_wait_duration = wait_seconds * 1000;
    c.dead_time = now.div_euclid(1000);
    tracing::debug!(
        "Death #{count} for {}: respawn wait = {wait_seconds}s",
        c.unit.name
    );
}

/// The loss part of `Character.DoDie` before `base.DoDie`: `LastDurabilityLoss`
/// (`GetDeathDurabilityLossRatio()` from `MinimumExpLossLevel` on) and, below the max level in
/// the main world, `LastExpLoss` (`GetExpLoss(level, ExpLossRateAtDeath)` capped to the exp of
/// the current level, 80 % of it recoverable; a free resurrection offer below
/// `MinimumExpLossLevel`). Returns `(LastExpLoss, LastDurabilityLoss)`.
fn character_death_losses(server: &GameServer, p: &PlayerRef) -> (i32, u8) {
    let world = &server.config.get().world;
    let exp_table = &server.game_data.experience;
    let (level, experience, instance_id) = {
        let c = p.data();
        (c.unit.level, c.experience, c.unit.transform.instance_id)
    };
    let qualifies = i32::from(level) >= world.minimum_exp_loss_level;
    let durability_loss = if qualifies {
        server.game_data.items.config.death_durability_loss_ratio
    } else {
        0
    };
    let mut lost_exp = 0;
    let mut recoverable = p.data().recoverable_exp;
    if level < exp_table.max_player_level()
        && instance_id == crate::models::character::DEFAULT_INSTANCE_ID
    {
        if qualifies {
            // ExperienceManager.GetExpLoss(level, rate)
            let this_level = exp_table.exp_for_level(level, false);
            let next_level = exp_table.exp_for_level(level.saturating_add(1), false);
            let loss = (f64::from(next_level - this_level)
                * f64::from(world.exp_loss_rate_at_death))
            .floor() as i32;
            lost_exp = loss.min(experience - this_level);
            recoverable = (lost_exp as f32 * 0.80f32).round() as i32;
            p.data().recoverable_exp = recoverable;
        } else {
            // Free resurrect below level 10
            p.send(super::packets_special::SCNotifyResurrectionPacket {
                caster: SkillCaster::Unit(p.obj_id),
            });
        }
    }
    if lost_exp > 0 || recoverable > 0 {
        p.data().experience -= lost_exp;
        p.send(super::progress::SCRecoverableExpPacket {
            obj_id: p.obj_id,
            recoverable_exp: recoverable,
            penaltied_exp: lost_exp,
            reason: 1, // KillReason.Damage
        });
        if lost_exp > 0 {
            p.send(super::packets::SCExpChangedPacket {
                obj_id: p.obj_id,
                exp: -lost_exp,
                should_add_ability_exp: false,
            });
        }
    }
    (lost_exp, durability_loss)
}

/// The part of `Unit.DoDie` after the death events (the death packet, loot, target cleanup).
fn do_die_base(
    server: &GameServer,
    victim: &WorldObject,
    killer: &WorldObject,
    kill_reason: KillReason,
    wait: i32,
    lost_exp: i32,
    durability_loss: u8,
) {
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
        // DespawnMate(owner of the mate / the character) (vehicles subsystem)
        crate::systems::vehicles::mates::despawn_mate_on_death(server, victim);
        return;
    }
    // LootingContainer.GenerateLoot(killer) (NPC loot: npcs -> economy).
    if let Some(n) = victim.as_npc() {
        crate::systems::npcs::npc::death::generate_loot(server, n, Some(killer));
    }
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
            crate::systems::vehicles::mates::despawn_mate_on_death(server, victim);
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
}

/// `Character.DoDie` after `base.DoDie` (and the durability loss): PvP kill bookkeeping
/// (`HostileFactionKills`, `AwardPvpHonor`, the `DiedInPvp` / `DiedInPvpWarZone` flags,
/// `SCUnitPvPPointsChangedPacket`s, the War-zone honor loss of the victim) or crime evidence
/// for a friendly-fire kill (unless it was retaliation), `DropTradePackToFloor`,
/// `ClearAllAggro()`, the damage history reset and the arrest of a wanted character killed by
/// another character.
fn character_death_tail(
    server: &GameServer,
    victim_p: &PlayerRef,
    victim: &WorldObject,
    killer: &WorldObject,
) {
    use crate::data::world_data::conflict_type;
    // Resolve the victim's zone-conflict state once for both PvP-honor award and War-zone
    // honor-loss
    let zone_key = victim_p.data().unit.transform.zone_id;
    let victim_zone_group = server
        .game_data
        .zones
        .zone_by_key(zone_key)
        .map(|z| z.group_id);
    let conflict = victim_zone_group.and_then(|g| server.systems.social.conflicts.get(g));
    let zone_state = conflict
        .as_ref()
        .map_or(conflict_type::PEACE, |c| c.current_state);

    let relation = super::relation::relation_to(
        server,
        &Target::Obj(killer.clone()),
        &Target::Obj(victim.clone()),
    );
    let arrestor = killer.as_player().cloned();
    if let Some(enemy) = &arrestor {
        if relation != super::types::RelationState::Friendly {
            enemy.data().hostile_faction_kills += 1;
            award_pvp_honor(
                server,
                victim_p,
                enemy,
                victim_zone_group,
                conflict.as_ref(),
                zone_state,
            );
            // Mark victim as PvP death (prevents Weakened Body debuff on temple-revive)
            {
                let mut c = victim_p.data();
                c.died_in_pvp = true;
                if zone_state == conflict_type::WAR {
                    c.died_in_pvp_war_zone = true;
                }
            }
            // Broadcast PvP stats: kind=0 → HonorGainedInCombat, kind=1 → HostileFactionKills
            let (honor, kills) = {
                let c = enemy.data();
                (c.honor_gained_in_combat, c.hostile_faction_kills)
            };
            let enemy_obj = WorldObject::from(enemy);
            for (kind, point) in [(0u8, honor), (1u8, kills)] {
                broadcast(
                    server,
                    &enemy_obj,
                    &crate::packets::g2c_misc::SCUnitPvPPointsChangedPacket {
                        unit_id: enemy.obj_id,
                        kind,
                        point: point as i32,
                    },
                    true,
                );
            }
            // Victim loses honor in War zone (clamped >= 0)
            let honor_point = victim_p.data().honor_point;
            if zone_state == conflict_type::WAR && honor_point > 0 {
                let loss = WAR_ZONE_HONOR_LOSS.min(honor_point);
                super::progress::change_game_points(
                    server,
                    victim_p,
                    super::progress::GAME_POINT_HONOR,
                    -loss,
                );
                tracing::debug!(
                    "PvP Death: {} lost {loss} honor (War zone death)",
                    victim_p.data().unit.name
                );
            }
        } else {
            // Friendly-fire kill -> generate crime evidence (unless retaliation)
            let killer_owner = super::relation::owner_character(server, killer);
            if let Some(owner) = killer_owner {
                let retaliation = victim_p
                    .data()
                    .skill_char
                    .assaulted_by
                    .contains(&owner.character_id);
                if !retaliation {
                    server.systems.social.crimes.generate_evidence_from_kill(
                        server,
                        killer,
                        Some(&owner),
                        Some(victim_p),
                    );
                }
            }
        }
    }
    server
        .systems
        .skills
        .ext
        .items()
        .drop_trade_pack_to_floor(server, victim_p);
    clear_all_aggro(server, victim);
    // Clear damage history on death (heal history is intentionally kept)
    victim_p.data().skill_char.pvp_damage_history.clear();
    // Arrest if wanted
    let Some(arrestor) = arrestor else {
        return;
    };
    if !buffs::check_buff_tag(server, victim, tags::WANTED) {
        return;
    }
    let contemptuous =
        victim.with_base_unit(|b| b.buffs().check_buff(buff_constants::CONTEMPTUOUS));
    let arrest = if !contemptuous {
        // If not a pirate arrest regardless
        true
    } else {
        // If a pirate, only arrest in faction zones
        let zones = &server.game_data.zones;
        let desperado = zones
            .zone_by_key(zone_key)
            .and_then(|z| zones.group(z.group_id))
            .is_some_and(|g| g.pirate_desperado);
        !desperado
    };
    if arrest {
        server
            .systems
            .social
            .trials
            .arrest_criminal(server, victim_p, Some(&arrestor));
    }
}

/// `CollectAssists(killer)`: players who damaged the victim recently, players who healed the
/// killer recently (both within `PvpAssistWindowSeconds`, the killer excluded) and the
/// character casters of active crowd-control debuffs on the victim.
pub fn collect_assists(
    server: &GameServer,
    victim: &PlayerRef,
    killer: &PlayerRef,
    now: i64,
) -> Vec<u32> {
    let mut assists: Vec<u32> = Vec::new();
    let mut add = |id: u32| {
        if !assists.contains(&id) {
            assists.push(id);
        }
    };
    let cutoff = now - PVP_ASSIST_WINDOW_MS;
    let killer_id = killer.character_id;
    let (damage, bad_casters) = {
        let c = victim.data();
        let damage = c.skill_char.pvp_damage_history.clone();
        let bad: Vec<(u32, u32)> = c
            .unit
            .buffs
            .all_buffs(false)
            .map(|(_, bad, _)| {
                bad.iter()
                    .filter_map(|b| b.caster.map(|(id, _)| (id, b.template.id)))
                    .collect()
            })
            .unwrap_or_default();
        (damage, bad)
    };
    // 1) Players who damaged the victim recently (excluding the killer)
    for (id, time) in damage {
        if id != killer_id && time >= cutoff {
            add(id);
        }
    }
    // 2) Players who healed the killer recently (excluding the killer themselves)
    let heals = killer.data().skill_char.pvp_heal_history.clone();
    for (id, time) in heals {
        if id != killer_id && time >= cutoff {
            add(id);
        }
    }
    // 3) Active CC-debuff casters on the victim
    let skills = &server.game_data.systems.skills;
    for (caster_id, buff_id) in bad_casters {
        // `buff.Caster is not Character ccCaster` (a non-character caster has no character id)
        if caster_id == 0 || caster_id == killer_id {
            continue;
        }
        if let Some(t) = skills.buff(buff_id) {
            if t.stun || t.root || t.sleep || t.silence || t.crippled {
                add(caster_id);
            }
        }
    }
    assists
}

/// `AwardPvpHonor(killer, victimZone, conflictData, zoneState)`: Conflict 10 solo (6 killer +
/// 4 each assist), War 20 solo (16 killer + 4 each assist), nothing in other states; the kill
/// also counts for the zone conflict.
fn award_pvp_honor(
    server: &GameServer,
    victim: &PlayerRef,
    killer: &PlayerRef,
    victim_zone_group: Option<u32>,
    conflict: Option<&crate::data::world_data::ZoneConflict>,
    zone_state: u8,
) {
    use crate::data::world_data::conflict_type;
    let (solo, killer_share, assist_share) = match zone_state {
        conflict_type::CONFLICT => (10, 6, 4),
        conflict_type::WAR => (20, 16, 4),
        // No honor outside Conflict/War zones
        _ => return,
    };
    // Register zone kill (drives zone state escalation)
    if let Some(c) = conflict {
        server
            .systems
            .social
            .conflicts
            .add_zone_kill(server, c.zone_group_id);
    }
    let rate = server.config.get().world.pvp_honor_rate;
    let assist_ids = collect_assists(server, victim, killer, now_ms());
    // Resolve to online assistants before choosing the kill path (offline assists fall back
    // to the solo award).
    let online: Vec<PlayerRef> = assist_ids
        .iter()
        .filter_map(|id| server.world.character_by_id(*id))
        .collect();
    let round = |share: i32| (f64::from(share) * rate).round_ties_even() as i32;
    let gain = |p: &PlayerRef, honor: i32| {
        super::progress::change_game_points(server, p, super::progress::GAME_POINT_HONOR, honor);
        let mut c = p.data();
        c.honor_gained_in_combat = c.honor_gained_in_combat.wrapping_add(honor as u32);
        c.honor_gained_in_combat
    };
    if !online.is_empty() {
        let killer_honor = round(killer_share);
        if killer_honor > 0 {
            gain(killer, killer_honor);
        }
        let assist_honor = round(assist_share);
        if assist_honor > 0 {
            for assistant in &online {
                let total = gain(assistant, assist_honor);
                broadcast(
                    server,
                    &WorldObject::from(assistant),
                    &crate::packets::g2c_misc::SCUnitPvPPointsChangedPacket {
                        unit_id: assistant.obj_id,
                        kind: 0,
                        point: total as i32,
                    },
                    true,
                );
            }
        }
    } else {
        let honor = round(solo);
        if honor > 0 {
            gain(killer, honor);
        }
    }
    if let Some(group) = victim_zone_group {
        // The conflict-zone honor UI keys on ZoneGroupId.
        let sum = killer.data().honor_gained_in_combat;
        killer.send(
            crate::packets::g2c_misc::SCConflictZoneHonorPointSumPacket {
                zone_id: group as u16,
                sum: sum as i32,
            },
        );
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
    drop(watch);
    // Character.ClearAllAggro: AggroTable.Clear() + ClearAssaultList()
    if let Some(p) = owner.as_player() {
        p.data().unit.skill_state.aggro_table.clear();
        clear_assault_list(server, p);
    }
}

/// `List<uint>.Remove(value)` (first occurrence).
fn remove_first(list: &mut Vec<u32>, value: u32) {
    if let Some(i) = list.iter().position(|v| *v == value) {
        list.remove(i);
    }
}

/// `Character.ClearAssaultList()`: this character leaves the assault lists of the characters
/// it assaulted / was assaulted by, then both of its lists are cleared.
pub fn clear_assault_list(server: &GameServer, p: &PlayerRef) {
    let (me, assaulted_by, assault_on) = {
        let mut c = p.data();
        (
            c.id,
            std::mem::take(&mut c.skill_char.assaulted_by),
            std::mem::take(&mut c.skill_char.assault_on),
        )
    };
    for criminal_id in assaulted_by {
        if let Some(criminal) = server.world.character_by_id(criminal_id) {
            remove_first(&mut criminal.data().skill_char.assault_on, me);
        }
    }
    for victim_id in assault_on {
        if let Some(victim) = server.world.character_by_id(victim_id) {
            remove_first(&mut victim.data().skill_char.assaulted_by, me);
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
                unit_name: name,
                id: old,
                id2: faction_id,
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
    // Npc attacks the characters around it (quest 2486): systems::ai
    if let (Some(n), Some(ai)) = (unit.as_npc(), server.systems.hooks.ai.get()) {
        ai.npc_faction_set(server, n);
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
