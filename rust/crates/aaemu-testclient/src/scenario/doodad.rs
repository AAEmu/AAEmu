//! `doodad`: interact with a quest doodad the way the 1.2 client does and log the wire.
//!
//! Reproduces the bug report for quest 3447 "Fellow Searchers": the objective
//! `QuestActObjInteraction` on doodad template 4252 "Amber Boulder" (Arach Hollow) cannot be
//! finished. The boulder's start func group is `DoodadFuncFakeUse` with fake skill 16790
//! ("break the boulder", 3 s cast, range 4, main hand tool 1151), phase func
//! `DoodadFuncRequireQuest` 3447.
//!
//! Setup uses GM chat commands like `questitem` (`/revive`, `/move`, `/quest add <quest> Npc
//! <giver>`, `/quest step <quest> Progress`), so the bot's account needs access level 100. The
//! bot teleports next to the boulder, logs every doodad template within [`SIGHT`] metres, finds
//! the boulder, casts the skill with a doodad target (`CSStartSkillPacket`) and logs every
//! packet that arrives during the cast. The step fails when the quest objective stays at 0.

use std::time::{Duration, Instant};

use aaemu_game::opcodes::sc;
use aaemu_game::systems::quests::model::status;
use anyhow::{ensure, Context};

use super::questitem::{chat_text, gm_command, last_quest_state};
use super::{skill_response_result, BotConfig, Runner, QUIET};
use crate::game::{DoodadInfo, GameClient, Packet};

/// Quest 3447 "Fellow Searchers".
const QUEST: u32 = 3447;
/// Quest giver NPC template 7890 (recorded as the acceptor so the start condition passes).
const GIVER: u32 = 7890;
/// Doodad template 4252 "Amber Boulder".
const DOODAD: u32 = 4252;
/// Skill 16790, the boulder's `DoodadFuncFakeUse.FakeSkillId`.
const SKILL: u32 = 16790;
/// GM teleport target next to the boulder (spawn at 21376.94, 8038.77, 372.92).
const POS: [f32; 3] = [21376.0, 8036.0, 373.0];
/// Doodads within this many metres of the bot are listed.
const SIGHT: f32 = 30.0;
/// Packets of the busy world around the bot that are counted, not listed.
const NOISE: [u16; 5] = [
    sc::SCOneUnitMovementPacket,
    sc::SCTargetChangedPacket,
    sc::SCUnitModelPostureChangedPacket,
    sc::SCUnitPointsPacket,
    sc::SCTimeOfDayPacket,
];
/// How long the cast (3 s) and its aftermath are observed.
const CAST_WINDOW: Duration = Duration::from_secs(8);

pub(super) async fn run(
    game: &mut GameClient,
    _cfg: &BotConfig,
    r: &mut Runner<'_>,
) -> anyhow::Result<()> {
    // ---- GM access ----
    let s = r.begin("game.doodad.gm");
    let reply = gm_command(game, "quest list").await?;
    let texts: Vec<String> = reply.iter().filter_map(chat_text).collect();
    ensure!(
        !texts.iter().any(|t| t.contains("Insufficient privileges")),
        "GM commands are refused: account {} needs access level 100",
        game.account_id
    );
    ensure!(!texts.is_empty(), "no reply to the GM command /quest list");
    gm_command(game, "revive").await?;
    r.end(s, "GM commands accepted");

    // ---- teleport next to the boulder ----
    let s = r.begin("game.doodad.teleport");
    let [x, y, z] = POS;
    game.chat(0, &format!("/move {x} {y} {z}")).await?;
    game.wait_for("SCTeleportUnitPacket", |p| {
        p.is_sc(sc::SCTeleportUnitPacket)
    })
    .await?;
    let at = game.state.self_pos;
    game.teleport_ended(at).await?;
    game.move_to(at, 5000).await?;
    // Objects of the new region arrive within a few seconds.
    game.drain(QUIET, Duration::from_secs(8)).await?;
    let mut waited = 0;
    while !game.state.doodads.values().any(|d| d.template_id == DOODAD) && waited < 10 {
        game.drain(Duration::from_millis(500), Duration::from_secs(1))
            .await?;
        waited += 1;
    }
    let boulder = report_near(game, "after the teleport");
    // Server-side view (GM `/findobject doodad <template>`): is the boulder in the world at all?
    for template in [DOODAD, 5007] {
        let reply = gm_command(game, &format!("findobject doodad {template} 1")).await?;
        let lines: Vec<String> = reply.iter().filter_map(chat_text).collect();
        println!("[doodad] /findobject doodad {template}: {lines:#?}");
    }
    r.end(
        s,
        format!(
            "{} doodad(s) within {SIGHT} m, boulder {}",
            doodads_near(game).len(),
            boulder.as_ref().map_or_else(
                || "NOT visible".to_string(),
                |b| format!("obj {} group {} at {:?}", b.obj_id, b.func_group_id, b.pos)
            )
        ),
    );

    // ---- quest ----
    let s = r.begin("game.doodad.quest");
    let mut reply = gm_command(game, &format!("quest add {QUEST} Npc {GIVER}")).await?;
    let add_texts: Vec<String> = reply.iter().filter_map(chat_text).collect();
    let step = gm_command(game, &format!("quest step {QUEST} Progress")).await?;
    let step_texts: Vec<String> = step.iter().filter_map(chat_text).collect();
    println!("[doodad] quest add replies {add_texts:?}; step replies {step_texts:?}");
    reply.extend(step);
    let quest = last_quest_state(&reply, QUEST);
    ensure!(
        step_texts
            .iter()
            .any(|t| t.contains(&format!("Set Step Progress for Quest {QUEST}"))),
        "quest {QUEST} could not be set to its Progress step: {step_texts:?}"
    );
    r.end(
        s,
        match quest {
            Some(q) => format!("status {} objectives {:?}", q.status, q.objectives),
            None => "no quest packet (already in the quest log?)".into(),
        },
    );
    // The doodad may only be announced to players with the quest: look again, then leave the
    // area and come back so the region is announced afresh.
    game.drain(QUIET, Duration::from_secs(5)).await?;
    let mut boulder = boulder.or_else(|| report_near(game, "after the quest was added"));
    if boulder.is_none() {
        for (label, pos) in [
            ("far away", [POS[0] - 600.0, POS[1], POS[2]]),
            ("back at the boulder", POS),
        ] {
            game.chat(0, &format!("/move {} {} {}", pos[0], pos[1], pos[2]))
                .await?;
            game.wait_for("SCTeleportUnitPacket", |p| {
                p.is_sc(sc::SCTeleportUnitPacket)
            })
            .await?;
            let at = game.state.self_pos;
            game.teleport_ended(at).await?;
            game.move_to(at, 7000).await?;
            game.drain(QUIET, Duration::from_secs(8)).await?;
            println!("[doodad] teleported {label} to {at:?}");
        }
        boulder = report_near(game, "after leaving and returning");
    }
    // The level data's own spawn of the boulder is missing (see the findobject output above):
    // spawn one with the GM command so the interaction itself can still be observed.
    let (boulder, gm_spawned) = match boulder {
        Some(b) => (b, false),
        None => {
            println!(
                "[doodad] FINDING: doodad {DOODAD} is not in the world although main_world doodad_spawns.json has it at (21376.94, 8038.77, 372.92); spawning one with /spawn doodad {DOODAD}"
            );
            let reply = gm_command(game, &format!("spawn doodad {DOODAD}")).await?;
            let lines: Vec<String> = reply.iter().filter_map(chat_text).collect();
            println!("[doodad] /spawn doodad {DOODAD}: {lines:?}");
            game.drain(QUIET, Duration::from_secs(4)).await?;
            let b = report_near(game, "after /spawn doodad").with_context(|| {
                format!("doodad {DOODAD} was not announced even after /spawn doodad {DOODAD}")
            })?;
            (b, true)
        }
    };
    println!("[doodad] boulder: {boulder:?} (GM spawned: {gm_spawned})");

    // ---- the cast ----
    let s = r.begin("game.doodad.cast");
    game.change_target(boulder.obj_id).await?;
    let me = game.state.self_pos;
    // Stay put (about 3 m away, range 4) but face the boulder.
    game.move_to_facing(me, boulder.pos, 6000).await?;
    game.drain(Duration::from_millis(300), Duration::from_secs(1))
        .await?;
    println!(
        "[doodad] casting skill {SKILL} on doodad obj {} from {me:?} (distance {:.2} m)",
        boulder.obj_id,
        dist(me, boulder.pos)
    );
    game.start_doodad_skill(SKILL, boulder.obj_id).await?;
    let started = Instant::now();
    let mut seen = Vec::new();
    let mut noise = 0;
    while started.elapsed() < CAST_WINDOW {
        if let Some(p) = game.recv_within(Duration::from_millis(250)).await? {
            if NOISE.iter().any(|&o| p.is_sc(o)) {
                noise += 1;
                seen.push(p);
                continue;
            }
            println!(
                "[doodad] +{:>5} ms {} ({} bytes) {}",
                started.elapsed().as_millis(),
                p.name(),
                p.body.len(),
                describe(&p, SKILL, game.state.self_obj)
            );
            seen.push(p);
        }
    }
    println!("[doodad] ({noise} movement / target / posture packets of other units not listed)");
    let after = game.state.doodads.get(&boulder.obj_id).cloned();
    println!("[doodad] boulder after the cast: {after:?}");
    let result = seen
        .iter()
        .filter(|p| p.is_sc(sc::SCSkillStartedPacket))
        .find_map(|p| skill_response_result(p).ok());
    let q = last_quest_state(&seen, QUEST).or(quest);
    let objective = q.map_or(0, |q| q.objectives[0]);
    let done = q.is_some_and(|q| {
        q.status == status::READY || q.status == status::COMPLETED || q.objectives[0] >= 1
    });
    let note = format!(
        "SCSkillStarted result {result:?}; {} packet(s); quest status {:?} objectives {:?}; \
         boulder {}",
        seen.len(),
        q.map(|q| q.status),
        q.map(|q| q.objectives),
        match after {
            Some(d) => format!("still present (group {})", d.func_group_id),
            None => "removed".into(),
        }
    );
    ensure!(
        done,
        "casting skill {SKILL} on the Amber Boulder did not count the objective \
         (objective {objective}): {note}"
    );
    r.end(s, note);

    // ---- tidy up ----
    // A boulder owned by the bot is a GM spawn (the level's own one has no owner): remove it so
    // the next run sees the world as the server built it.
    if boulder.owner_obj_id == game.state.self_obj {
        let reply = gm_command(game, &format!("spawn remove {}", boulder.obj_id)).await?;
        let lines: Vec<String> = reply.iter().filter_map(chat_text).collect();
        println!("[doodad] /spawn remove {}: {lines:?}", boulder.obj_id);
    }

    // ---- leave ----
    let s = r.begin("game.leave");
    game.leave_world(0).await?;
    game.wait_sc(sc::SCPrepareLeaveWorldPacket).await?;
    game.drain(Duration::from_millis(300), Duration::from_secs(2))
        .await
        .ok();
    r.end(s, "left");

    // The interaction works, but only on a doodad spawned by hand: report the level data's
    // missing spawn as the failure it is.
    if gm_spawned {
        let s = r.begin("game.doodad.level_spawn");
        r.fail(
            s,
            format!(
                "doodad {DOODAD} (Amber Boulder) from doodad_spawns.json is not in the world; the interaction only worked on a /spawn doodad {DOODAD} copy"
            ),
        );
    }
    Ok(())
}

fn dist(a: (f32, f32, f32), b: (f32, f32, f32)) -> f32 {
    let (dx, dy, dz) = (a.0 - b.0, a.1 - b.1, a.2 - b.2);
    (dx * dx + dy * dy + dz * dz).sqrt()
}

/// Doodads within [`SIGHT`] metres, nearest first.
fn doodads_near(game: &GameClient) -> Vec<(f32, DoodadInfo)> {
    let me = game.state.self_pos;
    let mut v: Vec<(f32, DoodadInfo)> = game
        .state
        .doodads
        .values()
        .map(|d| (dist(me, d.pos), d.clone()))
        .filter(|(d, _)| *d <= SIGHT)
        .collect();
    v.sort_by(|a, b| a.0.total_cmp(&b.0).then(a.1.obj_id.cmp(&b.1.obj_id)));
    v
}

/// Logs the doodads within [`SIGHT`] metres and returns the Amber Boulder when it is among them.
fn report_near(game: &GameClient, when: &str) -> Option<DoodadInfo> {
    let near = doodads_near(game);
    println!(
        "[doodad] {when}: bot at {:?}; {} doodad(s) known, {} within {SIGHT} m:",
        game.state.self_pos,
        game.state.doodads.len(),
        near.len()
    );
    for (dist, d) in &near {
        println!(
            "[doodad]   template {:>6} obj {:>8} group {:>6} glow {} at ({:.2}, {:.2}, {:.2}) {dist:.1} m{}",
            d.template_id,
            d.obj_id,
            d.func_group_id,
            d.quest_glow,
            d.pos.0,
            d.pos.1,
            d.pos.2,
            if d.template_id == DOODAD { "  <== Amber Boulder" } else { "" }
        );
    }
    near.into_iter()
        .find(|(_, d)| d.template_id == DOODAD)
        .map(|(_, d)| d)
}

/// A short human description of a packet (hex of the small, relevant ones).
fn describe(p: &Packet, skill: u32, caster: u32) -> String {
    let hex = |n: usize| {
        p.body
            .iter()
            .take(n)
            .map(|b| format!("{b:02x}"))
            .collect::<String>()
    };
    if p.is_sc(sc::SCSkillStartedPacket) {
        return format!(
            "own={} result={:?} {}",
            super::own_skill_response(p, skill, caster),
            skill_response_result(p).ok(),
            hex(96)
        );
    }
    if p.is_sc(sc::SCChatMessagePacket) {
        return format!("{:?}", chat_text(p));
    }
    if p.is_sc(sc::SCErrorMsgPacket) {
        return format!("code {:?}", p.stream().read_i16().ok());
    }
    if [
        sc::SCSkillFiredPacket,
        sc::SCSkillEndedPacket,
        sc::SCCastingStoppedPacket,
        sc::SCDoodadPhaseChangedPacket,
        sc::SCDoodadRemovedPacket,
        sc::SCDoodadChangedPacket,
        sc::SCQuestContextUpdatedPacket,
        sc::SCQuestContextStartedPacket,
        sc::SCQuestContextCompletedPacket,
        sc::SCQuestContextFailedPacket,
        sc::SCItemTaskSuccessPacket,
    ]
    .iter()
    .any(|&o| p.is_sc(o))
    {
        return hex(96);
    }
    String::new()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::game::LEVEL_GAME;
    use aaemu_commons::{Marshal, PacketStream};
    use aaemu_game::packets::world_objects::SCDoodadRemovedPacket;

    #[test]
    fn describe_reads_error_codes_and_stays_empty_for_noise() {
        let p = Packet {
            level: LEVEL_GAME,
            opcode: sc::SCErrorMsgPacket,
            body: vec![3, 0, 0, 0],
        };
        assert_eq!(describe(&p, SKILL, 1), "code Some(3)");
        let mut body = PacketStream::new();
        SCDoodadRemovedPacket { obj_id: 5 }.write(&mut body);
        let removed = Packet {
            level: LEVEL_GAME,
            opcode: sc::SCDoodadRemovedPacket,
            body: body.into_bytes(),
        };
        assert!(!describe(&removed, SKILL, 1).is_empty());
        let moved = Packet {
            level: LEVEL_GAME,
            opcode: sc::SCOneUnitMovementPacket,
            body: vec![],
        };
        assert_eq!(describe(&moved, SKILL, 1), "");
    }

    #[test]
    fn distance_is_euclidean() {
        assert!((dist((0.0, 0.0, 0.0), (3.0, 4.0, 0.0)) - 5.0).abs() < 1e-6);
    }
}
