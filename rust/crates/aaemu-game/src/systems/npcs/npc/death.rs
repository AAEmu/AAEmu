//! `Npc.DoDie(killer, killReason)` (`Models/Game/NPChar/Npc.cs`) with the `Unit.DoDie` part
//! it calls (`base.DoDie`), minus combat / AI internals:
//!
//! 1. `DeadTime = UtcNow`;
//! 2. experience and monster-hunt quest events for the players with tagging rights (or the
//!    killing player when nobody has them), with the 1.2 group / level-difference modifiers
//!    (`quests.add_exp`, `quests.on_npc_killed`);
//! 3. `base.DoDie`: `skills.on_unit_died` (interrupt skills, death events, buffs removed on
//!    death), `IsInBattle = false`, `SCUnitDeathPacket` broadcast from the killer, loot
//!    (`economy.on_npc_loot` -> `HasLootLeft`), target cleanup (`SCAiAggroPacket`,
//!    `SCTargetChangedPacket`);
//! 4. taggers cleared, `CurrentAggroTarget = null`, `Spawner?.DoDespawn(this)`
//!    (`spawn_manager::on_npc_died`).
//!
//! Not ported here (later phases / other subsystems): the aggro table clean-up
//! (`ClearAllAggroTargetsAndCheckCombatState`, AI), `Ai.GoToDead`, mate experience
//! (`MateManager.GetActiveMates` + "Pet gained .. XP" messages), the team lookup of a tagging
//! team (`TeamManager.GetActiveTeam`: treated as an invalid team, so the tagger alone is
//! eligible and the raid / full-party modifiers never apply), the `TagShareEnabled` fan-out and
//! `Spawner.RespawnTime` in the death packet (0).
//!
//! Deviation: a `null` killer broadcasts the death packet around the NPC (C#:
//! `NullReferenceException` on `killer.BroadcastPacket`).

use crate::server::GameServer;
use crate::world::{broadcast, NpcRef, PlayerRef, WorldObject};

use super::packets::{SCAiAggroPacket, SCUnitDeathPacket};
use crate::packets::world_objects::SCTargetChangedPacket;

/// `(int)(KillExp * plMod * levDif)` per eligible player, `None` when the level gap is 10 or more
/// (no experience). `eligible` = number of eligible players.
pub fn kill_exp_share(
    kill_exp: i32,
    eligible: usize,
    player_level: u8,
    npc_level: u8,
) -> Option<i32> {
    // isRaid / isFullTeam need the team (not resolved here, see the module docs).
    let pl_mod: f32 = match eligible {
        2 => 0.90,
        3 => 0.875,
        _ => 1.0,
    };
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

/// `Npc.DoDie(killer, killReason)`.
pub fn do_die(server: &GameServer, npc: &NpcRef, killer: Option<&WorldObject>, kill_reason: u8) {
    let me = WorldObject::from(npc);
    let gd = &server.game_data;
    let (tag_team, tagger, npc_level, current_target, kill_exp) = {
        let mut n = npc.data();
        n.dead_time_ms = chrono::Utc::now().timestamp_millis();
        let kill_exp = super::stats::npc_stats(gd, &n).map_or(0, |s| s.kill_exp);
        (
            n.tag_team,
            n.tagger_obj_id,
            n.unit.level,
            n.unit.current_target,
            kill_exp,
        )
    };
    let quests = server.systems.npcs.hooks.quests.get();

    // Eligible players (CharacterTagging).
    let mut eligible: Vec<PlayerRef> = Vec::new();
    let tagger = (tagger != 0)
        .then(|| server.world.character_by_obj_id(tagger))
        .flatten();
    // `TagTeam != 0`: `TeamManager.GetActiveTeam` is not resolvable here -> the
    // "team is not valid" branch (the tagger alone), which is also the no-team branch.
    let _ = tag_team;
    if let Some(t) = tagger {
        eligible.push(t);
    }

    let killer_player = killer.and_then(|k| k.as_player().cloned());
    if eligible.is_empty() {
        if let Some(ck) = &killer_player {
            if let Some(q) = &quests {
                q.on_npc_killed(server, ck, npc);
                q.add_exp(server, ck, kill_exp, npc.obj_id);
            }
        }
    } else {
        for pl in &eligible {
            let level = pl.data().unit.level;
            if let Some(exp) = kill_exp_share(kill_exp, eligible.len(), level, npc_level) {
                if let Some(q) = &quests {
                    q.add_exp(server, pl, exp, npc.obj_id);
                }
            }
            if let Some(q) = &quests {
                q.on_npc_killed(server, pl, npc);
            }
        }
    }

    // ---- base.DoDie (Unit.DoDie)
    if let Some(s) = server.systems.npcs.hooks.skills.get() {
        s.on_unit_died(server, &me, killer);
    }
    npc.data().unit.is_in_battle = false;
    let killer_unit = killer.and_then(|k| k.with_unit(|u| (k.obj_id(), u.unit().name.clone())));
    let death = SCUnitDeathPacket {
        obj_id: npc.obj_id,
        kill_reason,
        resurrect_wait_time: 0, // Spawner.RespawnTime: see the module docs
        lost_exp: 0,
        durability_loss_rate: 0,
        killer: killer_unit,
    };
    let world = server.world.world_of(&me);
    if let Some(w) = &world {
        match killer {
            Some(k) => broadcast(w, k.clone(), &death, true),
            None => broadcast(w, me.clone(), &death, true),
        }
    }
    let killed_by_self = killer.is_some_and(|k| k.obj_id() == npc.obj_id);
    if !killed_by_self {
        // LootingContainer.GenerateLoot(killer)
        if let Some(e) = server.systems.npcs.hooks.economy.get() {
            let has_loot = e.on_npc_loot(server, npc, killer_player.as_ref());
            npc.data().unit.has_loot_left = has_loot;
        }
        if current_target != 0 {
            if let Some(k) = killer {
                // killer.SendPacketToPlayers([this, killer], ..): only players receive it.
                if let Some(p) = k.as_player() {
                    p.send(SCAiAggroPacket::new(k.obj_id(), 0));
                }
                let killer_target = k.with_unit(|u| {
                    let u = u.unit_mut();
                    u.summarize_damage = 0;
                    u.current_target
                });
                if let Some(t) = killer_target.filter(|&t| t != 0) {
                    // `killerUnit.CurrentTarget is Unit unitTarget` -> not in battle.
                    let target = world.as_ref().and_then(|w| w.unit(t));
                    if let Some(t) = target {
                        t.with_unit(|u| u.unit_mut().is_in_battle = false);
                    }
                }
                k.with_unit(|u| u.unit_mut().is_in_battle = false);
                if let Some(w) = &world {
                    broadcast(
                        w,
                        k.clone(),
                        &SCTargetChangedPacket {
                            obj_id: k.obj_id(),
                            target_obj_id: 0,
                        },
                        true,
                    );
                }
                k.with_unit(|u| u.unit_mut().current_target = 0);
            }
        }
    }

    // ---- Npc.DoDie tail
    {
        let mut n = npc.data();
        n.tag_team = 0;
        n.tagger_obj_id = 0;
        n.current_aggro_target = 0;
    }
    super::super::spawn_manager::on_npc_died(server, npc);
    // ClearAllAggroTargetsAndCheckCombatState() + Ai?.GoToDead() (systems::ai)
    if let Some(ai) = server.systems.hooks.ai.get() {
        ai.npc_died(server, npc);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::opcodes::sc;
    use crate::systems::npcs::hooks::QuestsBridge;
    use crate::test_support::*;
    use aaemu_commons::{Marshal, PacketStream};
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
        let death = SCUnitDeathPacket {
            obj_id: 0x200,
            kill_reason: 1,
            resurrect_wait_time: 0,
            lost_exp: 0,
            durability_loss_rate: 0,
            killer: Some((0x100, "Alpha".into())),
        };
        let body = |p: &dyn Marshal| {
            let mut s = PacketStream::new();
            p.write(&mut s);
            s.into_bytes()
        };
        assert_eq!(
            a.drain(),
            vec![
                // broadcast from the killer: around (nobody else) + self
                (sc::SCUnitDeathPacket, body(&death)),
                (sc::SCAiAggroPacket, vec![0x00, 0x01, 0x00, 0, 0, 0, 0]),
                (sc::SCTargetChangedPacket, vec![0x00, 0x01, 0x00, 0, 0, 0]),
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
        do_die(&server, &npc2, None, 1);
        // level 10 player vs level 10 NPC: (10*5 + 90) * 1 * 1.0 = 140, full share
        assert_eq!(
            *rec.0.lock().unwrap(),
            vec!["exp 256 140 513".to_string(), "kill 256 513".to_string()]
        );
        assert_eq!(npc2.data().tagger_obj_id, 0);
    }

    #[test]
    fn exp_share_modifiers() {
        assert_eq!(kill_exp_share(1000, 1, 20, 20), Some(1000));
        assert_eq!(kill_exp_share(1000, 2, 20, 20), Some(900));
        assert_eq!(kill_exp_share(1000, 3, 20, 20), Some(875));
        assert_eq!(kill_exp_share(1000, 4, 20, 20), Some(1000));
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
