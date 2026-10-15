//! `Npc.DoDie(killer, killReason)` (`Models/Game/NPChar/Npc.cs`). The common `Unit.DoDie`
//! part (`base.DoDie`: interrupt skills, death events, buffs, `SCUnitDeathPacket`, loot, target
//! cleanup) is the skills subsystem's `combat::do_die`, which calls the two NPC halves here
//! through `NpcSkillHooks::npc_before_death` / `npc_after_death`:
//!
//! 1. [`before_death`] (before `base.DoDie`): `DeadTime`, the players with tagging rights
//!    (`CharacterTagging`: the tagging team's members within `MaxLootingRange`, else the
//!    tagger), experience with the 1.2 raid / full-party / group / level-difference modifiers
//!    (`quests.add_exp` -> `Character.AddExp`), monster-hunt quest events
//!    (`quests.on_npc_killed`), and the `TagShareEnabled` quest fan-out to every damage
//!    contributor and their team mates in range;
//! 2. [`generate_loot`] (`LootingContainer.GenerateLoot(killer)` inside `base.DoDie`, economy);
//! 3. [`after_death`] (after `base.DoDie`): `ClearAllAggroTargetsAndCheckCombatState`,
//!    `CharacterTagging.ClearAllTaggers()`, `CurrentAggroTarget = null`,
//!    `Spawner?.DoDespawn(this)` (`spawn_manager::on_npc_died`). `Ai?.GoToDead()` is the AI's.
//!
//! Not ported here: mate experience (`MateManager.GetActiveMates` + "Pet gained .. XP"
//! messages, owned by the mates subsystem).

use crate::server::GameServer;
use crate::systems::skills::combat::MAX_LOOTING_RANGE;
use crate::systems::skills::geometry::{distance_to, Target};
use crate::systems::social::achievement::char_record_kind;
use crate::systems::social::achievement_hooks::{
    lowest_party_level, AchievementInput, RecordMatch,
};
use crate::world::{NpcRef, PlayerRef, WorldObject};

/// The 1.2 player experience modifier (`plMod`) for `eligible` players with tagging rights.
pub fn player_exp_modifier(eligible: usize, is_raid: bool, is_full_team: bool) -> f32 {
    if is_raid {
        0.33
    } else if is_full_team {
        0.66
    } else {
        match eligible {
            2 => 0.90,
            3 => 0.875,
            _ => 1.0,
        }
    }
}

/// `(int)(KillExp * plMod * levDif)` for one eligible player, `None` when the level gap is 10 or
/// more (no experience).
pub fn kill_exp_for(kill_exp: i32, pl_mod: f32, player_level: u8, npc_level: u8) -> Option<i32> {
    let (pl, lv) = (i32::from(player_level), i32::from(npc_level));
    if pl >= lv + 10 || pl <= lv - 10 {
        return None;
    }
    let diff = pl - lv;
    let lev_dif: f32 = match diff.cmp(&0) {
        std::cmp::Ordering::Greater => 1.0 - 0.1 * diff as f32,
        std::cmp::Ordering::Less => 1.0 + 0.1 * (-diff) as f32,
        std::cmp::Ordering::Equal => 1.0,
    };
    Some((kill_exp as f32 * pl_mod * lev_dif) as i32)
}

/// [`kill_exp_for`] with the group modifier of `eligible` players outside a team.
pub fn kill_exp_share(
    kill_exp: i32,
    eligible: usize,
    player_level: u8,
    npc_level: u8,
) -> Option<i32> {
    kill_exp_for(
        kill_exp,
        player_exp_modifier(eligible, false, false),
        player_level,
        npc_level,
    )
}

/// `GetDistanceTo(npc, true) <= LootingContainer.MaxLootingRange`.
fn in_looting_range(server: &GameServer, p: &PlayerRef, npc: &WorldObject) -> bool {
    distance_to(
        server,
        &Target::Obj(WorldObject::from(p)),
        &Target::Obj(npc.clone()),
        true,
    ) <= MAX_LOOTING_RANGE
}

/// The tagging state of an NPC: (tag team, tagger obj id, damage dealers in first-hit order).
/// The skills subsystem's `CharacterTagging` is authoritative; the `Npc` mirror fields are
/// used when it is empty (NPCs whose tagging was set directly).
fn tagging_of(npc: &NpcRef) -> (u32, u32, Vec<u32>) {
    let n = npc.data();
    let t = &n.unit.skill_state.tagging;
    let team = if t.tag_team != 0 {
        t.tag_team
    } else {
        n.tag_team
    };
    let tagger = if t.tagger != 0 {
        t.tagger
    } else {
        n.tagger_obj_id
    };
    (team, tagger, t.taggers.iter().map(|(id, _)| *id).collect())
}

/// `Npc.DoDie` before `base.DoDie`: kill experience and monster-hunt quest events.
pub fn before_death(server: &GameServer, npc: &NpcRef, killer: Option<&WorldObject>) {
    let me = WorldObject::from(npc);
    let gd = &server.game_data;
    let (npc_level, kill_exp) = {
        let mut n = npc.data();
        n.dead_time_ms = chrono::Utc::now().timestamp_millis();
        let kill_exp = super::stats::npc_stats(gd, &n).map_or(0, |s| s.kill_exp);
        (n.unit.level, kill_exp)
    };
    let (tag_team, tagger_id, dealers) = tagging_of(npc);
    let quests = server.systems.npcs.hooks.quests.get();
    let teams = &server.systems.social.teams;

    // Eligible players (CharacterTagging).
    let mut eligible: Vec<PlayerRef> = Vec::new();
    let tagger = (tagger_id != 0)
        .then(|| server.world.character_by_obj_id(tagger_id))
        .flatten();
    let team = teams.active_team(tag_team);
    if tag_team != 0 {
        match &team {
            // Just to check the team is still a valid team.
            Some(team) => {
                for m in team.members.iter().flatten() {
                    if in_looting_range(server, &m.character, &me)
                        && !eligible.iter().any(|e| e.obj_id == m.character.obj_id)
                    {
                        eligible.push(m.character.clone());
                    }
                }
            }
            // A player has tag rights, but the team is not valid.
            None => eligible.extend(tagger.clone()),
        }
    } else if let Some(t) = &tagger {
        // A player has tag rights
        eligible.push(t.clone());
    }

    let killer_player = killer.and_then(|k| k.as_player().cloned());
    if eligible.is_empty() {
        if let Some(ck) = &killer_player {
            if let Some(q) = &quests {
                q.on_npc_killed(server, ck, npc);
                q.add_exp(server, ck, kill_exp, npc.obj_id);
            }
            report_kill(server, ck, npc);
            // The killer's active mates (vehicles subsystem).
            crate::systems::vehicles::mates::reward_kill_exp(server, ck, kill_exp);
        }
    } else {
        let (is_raid, is_full_team) = match (tag_team != 0, &team) {
            (true, Some(t)) if !t.is_party => (true, false),
            (true, Some(t)) => (false, t.members_count() > 3),
            _ => (false, false),
        };
        let pl_mod = player_exp_modifier(eligible.len(), is_raid, is_full_team);
        for pl in &eligible {
            let level = pl.data().unit.level;
            if let Some(exp) = kill_exp_for(kill_exp, pl_mod, level, npc_level) {
                if let Some(q) = &quests {
                    q.add_exp(server, pl, exp, npc.obj_id);
                }
            }
            // mateKillXp: mateMod is 0.66 in a raid, else plMod.
            let mate_mod = if is_raid { 0.66 } else { pl_mod };
            if let Some(mate_exp) = kill_exp_for(kill_exp, mate_mod, level, npc_level) {
                crate::systems::vehicles::mates::reward_kill_exp(server, pl, mate_exp);
            }
            if let Some(q) = &quests {
                q.on_npc_killed(server, pl, npc);
            }
            report_kill(server, pl, npc);
        }
    }

    // Tag Share: quest progress (not XP / loot) for every damage contributor and their
    // party / raid mates in range, after both branches above.
    if server.config.world.tag_share_enabled {
        let mut credited: Vec<u32> = eligible.iter().map(|p| p.obj_id).collect();
        if eligible.is_empty() {
            if let Some(ck) = &killer_player {
                credited.push(ck.obj_id);
            }
        }
        for contributor in all_contributors(server, &me, &dealers) {
            if credited.contains(&contributor.obj_id) {
                continue;
            }
            credited.push(contributor.obj_id);
            if let Some(q) = &quests {
                q.on_npc_killed(server, &contributor, npc);
            }
            report_kill(server, &contributor, npc);
        }
    }
}

/// `KILL_NPC` achievement credit for one player who shares the kill (same eligibility as the
/// monster-hunt quest event). The record's party-level cap is matched against the lowest level in
/// the player's party / raid.
fn report_kill(server: &GameServer, player: &PlayerRef, npc: &NpcRef) {
    let template_id = npc.data().unit.template_id;
    crate::systems::report_achievement(
        server,
        player,
        AchievementInput::Increment {
            kind_id: char_record_kind::KILL_NPC,
            amount: 1,
            matcher: RecordMatch::NpcKill {
                template_id,
                lowest_party_level: Some(lowest_party_level(server, player)),
            },
        },
    );
}

/// `Tagging.GetAllContributors(MaxLootingRange)`: every online damage dealer in range plus
/// their online team members in range (each team resolved once).
pub fn all_contributors(server: &GameServer, npc: &WorldObject, dealers: &[u32]) -> Vec<PlayerRef> {
    let teams = &server.systems.social.teams;
    let mut out: Vec<PlayerRef> = Vec::new();
    let mut visited: Vec<u32> = Vec::new();
    let add = |out: &mut Vec<PlayerRef>, p: PlayerRef| {
        if !out.iter().any(|o| o.obj_id == p.obj_id) {
            out.push(p);
        }
    };
    for &id in dealers {
        let Some(dealer) = server.world.character_by_obj_id(id) else {
            continue;
        };
        let (online, in_party) = {
            let c = dealer.data();
            (c.is_online, c.in_party)
        };
        if !online || !in_looting_range(server, &dealer, npc) {
            continue;
        }
        add(&mut out, dealer.clone());
        if !in_party {
            continue;
        }
        let Some(team) = teams.team_by_obj_id(dealer.obj_id) else {
            continue;
        };
        if visited.contains(&team.id) {
            continue;
        }
        visited.push(team.id);
        for m in team.members.iter().flatten() {
            if m.character.data().is_online && in_looting_range(server, &m.character, npc) {
                add(&mut out, m.character.clone());
            }
        }
    }
    out
}

/// `LootingContainer.GenerateLoot(killer)` (inside `base.DoDie`, economy): sets
/// `HasLootLeft`.
pub fn generate_loot(server: &GameServer, npc: &NpcRef, killer: Option<&WorldObject>) {
    let killer_player = killer.and_then(|k| k.as_player().cloned());
    if let Some(e) = server.systems.npcs.hooks.economy.get() {
        let has_loot = e.on_npc_loot(server, npc, killer_player.as_ref());
        npc.data().unit.has_loot_left = has_loot;
    }
}

/// `Npc.DoDie` after `base.DoDie`.
pub fn after_death(server: &GameServer, npc: &NpcRef, _killer: Option<&WorldObject>) {
    let me = WorldObject::from(npc);
    // ClearAllAggroTargetsAndCheckCombatState
    let aggro_players: Vec<PlayerRef> = {
        let ids: Vec<u32> = npc
            .data()
            .unit
            .skill_state
            .aggro_table
            .keys()
            .copied()
            .collect();
        ids.into_iter()
            .filter_map(|id| server.world.character_by_obj_id(id))
            .collect()
    };
    npc.data().unit.skill_state.aggro_table.clear();
    for p in &aggro_players {
        let po = WorldObject::from(p);
        crate::systems::skills::combat::clear_aggro_of_unit(server, &me, &po);
        let none_left = p.data().skill_char.is_in_aggro_list_of.is_empty();
        if none_left {
            // Cancel combat
            p.data().unit.is_in_battle = false;
        }
    }
    {
        // CharacterTagging.ClearAllTaggers(); CurrentAggroTarget = null
        let mut n = npc.data();
        n.unit.skill_state.tagging = Default::default();
        n.tag_team = 0;
        n.tagger_obj_id = 0;
        n.current_aggro_target = 0;
    }
    if let Some(quests) = server.systems.npcs.hooks.quests.get() {
        quests.on_npc_died(server, npc);
    }
    super::super::spawn_manager::on_npc_died(server, npc);
    // ClearAllAggroTargetsAndCheckCombatState() + Ai?.GoToDead() (systems::ai)
    if let Some(ai) = server.systems.hooks.ai.get() {
        ai.npc_died(server, npc);
    }
}

/// `Npc.DoDie(killer, killReason)`: the whole death through the skills subsystem's
/// `Unit.DoDie` (which calls [`before_death`], [`generate_loot`] and [`after_death`]).
pub fn do_die(server: &GameServer, npc: &NpcRef, killer: Option<&WorldObject>, kill_reason: u8) {
    use crate::systems::skills::types::KillReason;
    let reason = KillReason::from_raw(i64::from(kill_reason)).unwrap_or(KillReason::Damage);
    crate::systems::skills::combat::do_die(server, &WorldObject::from(npc), killer, reason);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::opcodes::sc;
    use crate::systems::npcs::hooks::QuestsBridge;
    use crate::test_support::*;
    use std::sync::{Arc, Mutex};

    #[derive(Default)]
    struct Recorder(Mutex<Vec<String>>);
    impl QuestsBridge for Recorder {
        fn on_npc_killed(&self, _s: &GameServer, p: &PlayerRef, n: &NpcRef) {
            self.0
                .lock()
                .unwrap()
                .push(format!("kill {} {}", p.obj_id, n.obj_id));
        }
        fn add_exp(&self, _s: &GameServer, p: &PlayerRef, exp: i32, target: u32) {
            self.0
                .lock()
                .unwrap()
                .push(format!("exp {} {exp} {target}", p.obj_id));
        }
    }

    #[tokio::test]
    async fn player_kills_npc() {
        let server = test_server_with(super::super::manager::tests::npc_game_data());
        crate::systems::npcs::install_hooks(&server);
        let rec = Arc::new(Recorder::default());
        server.systems.npcs.hooks.quests.install(rec.clone());
        let mut a = TestClient::new(&server, 1, 1001);
        let pa = spawn_player(&server, &a, test_character(1, "Alpha", 100.0, 100.0), 0x100);
        let npc = super::super::manager::create(&server, 0x200, 100).unwrap();
        npc.data().unit.transform.set_xyz(105.0, 100.0, 100.0);
        let world = server.world.world(0).unwrap();
        world.spawn(&npc, &server.view_ctx());
        a.drain();
        npc.data().unit.current_target = 0x100;
        npc.data().current_aggro_target = 0x100;
        pa.data().unit.current_target = 0x200;
        pa.data().unit.is_in_battle = true;
        npc.data().unit.hp = 0;

        let killer = WorldObject::from(&pa);
        do_die(
            &server,
            &npc,
            Some(&killer),
            super::super::packets::kill_reason::DAMAGE,
        );

        // KillExp of a level 50 grade 1 NPC: (50*5 + 90) * 1 * 1.0 + 0 = 340
        assert_eq!(
            *rec.0.lock().unwrap(),
            vec!["kill 256 512".to_string(), "exp 256 340 512".to_string()]
        );
        let ops = a.drain_ops();
        assert_eq!(
            ops,
            vec![
                // broadcast from the killer: around (nobody else) + self
                sc::SCUnitDeathPacket,
                sc::SCAiAggroPacket,
                // killerUnit.IsInBattle = false
                sc::SCCombatClearedPacket,
                sc::SCTargetChangedPacket,
            ]
        );
        assert!(npc.data().dead_time_ms > 0);
        assert_eq!(npc.data().current_aggro_target, 0);
        assert_eq!(pa.data().unit.current_target, 0);
        assert!(!pa.data().unit.is_in_battle);

        // With a tagger, the tagger gets the experience share instead.
        rec.0.lock().unwrap().clear();
        let npc2 = super::super::manager::create(&server, 0x201, 200).unwrap();
        npc2.data().tagger_obj_id = 0x100;
        before_death(&server, &npc2, None);
        after_death(&server, &npc2, None);
        // level 10 player vs level 10 NPC: (10*5 + 90) * 1 * 1.0 = 140, full share
        assert_eq!(
            *rec.0.lock().unwrap(),
            vec!["exp 256 140 513".to_string(), "kill 256 513".to_string()]
        );
        assert_eq!(npc2.data().tagger_obj_id, 0);
    }

    #[tokio::test]
    async fn a_tagged_kill_progresses_the_kill_npc_achievement() {
        use crate::systems::social::achievement::char_record_kind;
        use crate::systems::social::achievement_flow_tests::{
            achievement_ops, one_achievement, player_of,
        };
        // Killing npc template 200 twice completes the achievement.
        let mut gd = super::super::manager::tests::npc_game_data();
        gd.systems.social.achievements =
            one_achievement(1, 2, 0, 10, char_record_kind::KILL_NPC, 200, 0);
        let server = Arc::new(test_server_with(gd));
        server.set_self();
        crate::systems::npcs::install_hooks(&server);
        crate::systems::social::install_hooks(&server);
        let (mut client, _player) = player_of(&server);

        let kill = |obj_id| {
            let npc = super::super::manager::create(&server, obj_id, 200).unwrap();
            npc.data().tagger_obj_id = 0x100;
            before_death(&server, &npc, None);
        };
        kill(0x201);
        assert_eq!(
            achievement_ops(client.drain_ops()),
            [sc::SCAchievementChangedPacket]
        );
        kill(0x202);
        assert_eq!(
            achievement_ops(client.drain_ops()),
            [
                sc::SCAchievementChangedPacket,
                sc::SCAchievementCompletedPacket
            ]
        );
    }

    #[test]
    fn exp_share_modifiers() {
        assert_eq!(kill_exp_share(1000, 1, 20, 20), Some(1000));
        assert_eq!(kill_exp_share(1000, 2, 20, 20), Some(900));
        assert_eq!(kill_exp_share(1000, 3, 20, 20), Some(875));
        assert_eq!(kill_exp_share(1000, 4, 20, 20), Some(1000));
        // raid / full party
        assert_eq!(
            kill_exp_for(1000, player_exp_modifier(2, true, false), 20, 20),
            Some((1000f32 * 0.33) as i32)
        );
        assert_eq!(
            kill_exp_for(1000, player_exp_modifier(4, false, true), 20, 20),
            Some(660)
        );
        // 3 levels above: 1 - 0.3 (f32) -> 0.7 * 1000 = 700 (699.99994 -> 699)
        let three_above = (1000f32 * 1.0 * (1.0 - 0.1 * 3.0)) as i32;
        assert_eq!(kill_exp_share(1000, 1, 23, 20), Some(three_above));
        assert_eq!(
            kill_exp_share(1000, 1, 17, 20),
            Some((1000f32 * (1.0 + 0.1 * 3.0)) as i32)
        );
        assert_eq!(kill_exp_share(1000, 1, 30, 20), None);
        assert_eq!(kill_exp_share(1000, 1, 10, 20), None);
        assert_eq!(
            kill_exp_share(1000, 1, 29, 20),
            Some((1000f32 * (1.0 - 0.1 * 9.0)) as i32)
        );
    }
}
