//! `questitem`: use a quest item's skill on NPCs until the quest objective is counted.
//!
//! Default data: quest 2394 "Searching for Answers" (objective `QuestActObjItemUse` 425: use
//! item 17594 "Detection Orb" 3 times), use skill 16881 (target type AnyUnit, range 5) on the
//! "Field of Honor Participant" / "Fighter Participant" NPCs (7824 / 7823) at Soulreath. The
//! skill needs the quest in progress, buff 3004 on the target (the NPC puts it on itself when
//! it spawns; the orb dispels it) and an NPC-group requirement.
//!
//! Setup uses GM chat commands (`/quest add <quest> Npc <giver>`, `/item add self`, `/move`;
//! without the giver as acceptor the quest stays in its Start step), which need access
//! level 100 on the bot's account (`accounts.access_level` in the game database, see
//! `rust/LIVE-TESTING.md` section 3). Every cast has to be answered: a rejected cast fails the
//! step with its `SkillResult`.

use std::time::Duration;

use aaemu_commons::PacketStream;
use aaemu_game::opcodes::sc;
use aaemu_game::systems::quests::model::status;
use anyhow::{bail, ensure, Context};

use super::{
    own_skill_response, quest_template_id, skill_response_result, BotConfig, Runner, QUIET,
};
use crate::game::{GameClient, Packet, UnitInfo};

/// How long a single cast may stay unanswered (the server ignores casts of items it cannot
/// find without any reply).
const CAST_ANSWER: Duration = Duration::from_secs(8);

/// `SkillResult.SourceDied`.
const SOURCE_DIED: i32 = aaemu_game::systems::skills::types::skill_result::SOURCE_DIED as i32;

pub(super) async fn run(
    game: &mut GameClient,
    cfg: &BotConfig,
    r: &mut Runner<'_>,
) -> anyhow::Result<()> {
    ensure!(
        !cfg.quest_item_npcs.is_empty() && cfg.quest_item_uses > 0,
        "quest_item_npcs and quest_item_uses must not be empty / zero"
    );

    // ---- GM access ----
    let s = r.begin("game.questitem.gm");
    let reply = gm_command(game, "quest list").await?;
    let texts: Vec<String> = reply.iter().filter_map(chat_text).collect();
    ensure!(
        !texts.iter().any(|t| t.contains("Insufficient privileges")),
        "GM commands are refused: account {} needs access level 100, e.g. \
         `UPDATE <game db>.accounts SET access_level = 100 WHERE account_id = {};` (private MySQL \
         127.0.0.1:3317 only), then run the scenario again",
        game.account_id,
        game.account_id
    );
    ensure!(
        !texts.is_empty(),
        "no reply to the GM command /quest list (commands not loaded?)"
    );
    // The orb angers its target (buff 3003): a previous run's NPCs may have killed the bot,
    // which then logs in dead and every cast fails with SkillResult 2 (source died).
    gm_command(game, "revive").await?;
    // A bot killed by the Soulreath mobs on an earlier run is saved dead and every cast would
    // fail with SourceDied: revive it first (nothing happens to a living bot).
    gm_command(game, "revive").await?;
    r.end(s, "GM commands accepted");

    // ---- teleport next to the NPCs ----
    let s = r.begin("game.questitem.teleport");
    let [x, y, z] = cfg.quest_item_pos;
    game.chat(0, &format!("/move {x} {y} {z}")).await?;
    game.wait_for("SCTeleportUnitPacket", |p| {
        p.is_sc(sc::SCTeleportUnitPacket)
    })
    .await?;
    // `/move` only tells the client to go there: it answers with CSTeleportEnded and the
    // server takes the position from our own movement packets.
    let at = game.state.self_pos;
    game.teleport_ended(at).await?;
    game.move_to(at, 5000).await?;
    let mut waited = 0;
    while candidates(game, &cfg.quest_item_npcs).is_empty() {
        ensure!(
            waited < 20,
            "none of the NPC templates {:?} became visible around {at:?} within 20 s",
            cfg.quest_item_npcs
        );
        game.drain(Duration::from_millis(500), Duration::from_secs(1))
            .await?;
        waited += 1;
    }
    game.drain(QUIET, Duration::from_secs(5)).await?;
    let npcs = candidates(game, &cfg.quest_item_npcs);
    r.end(
        s,
        format!("at {at:?}, {} candidate NPC(s) in sight", npcs.len()),
    );

    // ---- quest and orbs ----
    let s = r.begin("game.questitem.quest");
    let mut reply = gm_command(
        game,
        &format!(
            "quest add {} Npc {}",
            cfg.quest_item_quest, cfg.quest_item_giver
        ),
    )
    .await?;
    // The start step also needs race 4 (Elf) and quest 2392 completed, which the Nuian bot
    // lacks: force the Progress step the use objective belongs to (the server answers with a
    // chat line only, no quest packet).
    let step = gm_command(
        game,
        &format!("quest step {} Progress", cfg.quest_item_quest),
    )
    .await?;
    let step_texts: Vec<String> = step.iter().filter_map(chat_text).collect();
    ensure!(
        step_texts.iter().any(|t| t.contains(&format!(
            "Set Step Progress for Quest {}",
            cfg.quest_item_quest
        ))),
        "quest {} could not be set to its Progress step: {step_texts:?}",
        cfg.quest_item_quest
    );
    reply.extend(step);
    let mut quest = last_quest_state(&reply, cfg.quest_item_quest);
    let mut orb = stack_id(&reply, cfg.quest_item_template);
    r.end(
        s,
        match quest {
            Some(q) => format!(
                "quest {} status {} objectives {:?}",
                cfg.quest_item_quest, q.status, q.objectives
            ),
            None => format!(
                "quest {}: no quest packet (already in the quest log?)",
                cfg.quest_item_quest
            ),
        },
    );

    let s = r.begin("game.questitem.item");
    let want = cfg.quest_item_uses + 2;
    let reply = gm_command(
        game,
        &format!("item add self {} {want}", cfg.quest_item_template),
    )
    .await?;
    orb = stack_id(&reply, cfg.quest_item_template).or(orb);
    let orb = orb.with_context(|| {
        format!(
            "no item {} was added: no SCItemTaskSuccess carried it (GM replies: {:?})",
            cfg.quest_item_template,
            reply.iter().filter_map(chat_text).collect::<Vec<_>>()
        )
    })?;
    r.end(
        s,
        format!("item {} (template {})", orb, cfg.quest_item_template),
    );

    // ---- the casts ----
    let s = r.begin("game.questitem.cast");
    let saved_timeout = game.timeout;
    game.timeout = CAST_ANSWER;
    let cast = casts(game, cfg, orb, &mut quest).await;
    game.timeout = saved_timeout;
    let (ok, results) = cast?;
    let q = quest.context("no quest packet carried the objective progress")?;
    ensure!(
        q.status == status::READY
            || q.status == status::COMPLETED
            || q.objectives[0] >= cfg.quest_item_uses as i32,
        "{ok} successful casts but quest {} objective is {:?} (status {}), expected {}",
        cfg.quest_item_quest,
        q.objectives,
        q.status,
        cfg.quest_item_uses
    );
    r.end(
        s,
        format!(
            "{ok} cast(s) of skill {} OK; SkillResults {results:?}; quest {} status {} \
             objectives {:?}",
            cfg.quest_item_skill, cfg.quest_item_quest, q.status, q.objectives
        ),
    );

    // ---- leave ----
    let s = r.begin("game.leave");
    game.leave_world(0).await?;
    let prep = game.wait_sc(sc::SCPrepareLeaveWorldPacket).await?;
    let logout_ms = prep.stream().read_i32().unwrap_or(0);
    if cfg.wait_leave_granted && logout_ms <= 10_000 {
        game.wait_sc(sc::SCLeaveWorldGrantedPacket).await?;
    }
    game.drain(Duration::from_millis(300), Duration::from_secs(2))
        .await
        .ok();
    r.end(s, "left");
    Ok(())
}

/// Casts the item skill on the candidate NPCs (nearest first, round robin) until
/// `quest_item_uses` casts succeeded. Returns the number of successes and every SkillResult
/// seen; fails with them when the attempts run out.
async fn casts(
    game: &mut GameClient,
    cfg: &BotConfig,
    orb: u64,
    quest: &mut Option<QuestState>,
) -> anyhow::Result<(u32, Vec<String>)> {
    let mut ok = 0;
    let mut results = Vec::new();
    let mut attempt = 0;
    let mut tried: std::collections::HashMap<u32, u32> = std::collections::HashMap::new();
    while ok < cfg.quest_item_uses {
        if attempt >= cfg.quest_item_max_attempts {
            bail!(
                "item {} (skill {}) counted {ok}/{} uses in {attempt} casts; \
                 SkillResults per cast (npc:result): {results:?}. SkillResult 93 is \
                 skill_urk_target_npc_group, 0 a started cast",
                cfg.quest_item_template,
                cfg.quest_item_skill,
                cfg.quest_item_uses
            );
        }
        let npcs = candidates(game, &cfg.quest_item_npcs);
        ensure!(!npcs.is_empty(), "all candidate NPCs left the view");
        // The least used NPC first (nearest among equals): every NPC gets its turn, and one
        // that was used long ago has had time to get its buff back.
        let uses = |u: &UnitInfo| tried.get(&u.obj_id).copied().unwrap_or(0);
        let npc = npcs
            .iter()
            .min_by_key(|u| uses(u))
            .cloned()
            .context("no candidate NPC")?;
        *tried.entry(npc.obj_id).or_insert(0u32) += 1;
        attempt += 1;

        game.change_target(npc.obj_id).await?;
        let pos = npc.pos;
        game.move_to_facing((pos.0 + 1.2, pos.1, pos.2), pos, 20_000 + attempt * 1000)
            .await?;
        game.drain(Duration::from_millis(300), Duration::from_secs(1))
            .await?;
        game.start_item_skill(
            cfg.quest_item_skill,
            orb,
            cfg.quest_item_template,
            npc.obj_id,
        )
        .await?;
        let caster = game.state.self_obj;
        let skill_id = cfg.quest_item_skill;
        let answer = game
            .wait_for("skill response", |p| {
                own_skill_response(p, skill_id, caster) || p.is_sc(sc::SCErrorMsgPacket)
            })
            .await
            .with_context(|| {
                format!(
                    "cast {attempt} of skill {skill_id} on npc {} ({}) got no answer: the \
                     server ignored it (item {orb} not owned, or not the item's use skill)",
                    npc.obj_id, npc.id
                )
            })?;
        let result = if answer.is_sc(sc::SCErrorMsgPacket) {
            i32::from(answer.stream().read_i16().unwrap_or(0)) + 1000
        } else {
            i32::from(skill_response_result(&answer)?)
        };
        results.push(format!("{}:{result}", npc.id));
        let after = game
            .drain(Duration::from_millis(800), Duration::from_secs(3))
            .await?;
        if let Some(q) = last_quest_state(&after, cfg.quest_item_quest) {
            *quest = Some(q);
        }
        if result == 0 {
            ok += 1;
        } else if result == SOURCE_DIED {
            // The orb angers the NPC (buff 3003) and it fights back: the low-level bot can
            // die between casts.
            gm_command(game, "revive").await?;
        } else {
            // The NPC's own buff handling (3004) needs a moment before it can be used again.
            tokio::time::sleep(Duration::from_secs(1)).await;
        }
    }
    Ok((ok, results))
}

/// The wanted NPCs in sight, nearest first.
fn candidates(game: &GameClient, templates: &[u32]) -> Vec<UnitInfo> {
    let me = game.state.self_pos;
    let mut npcs: Vec<UnitInfo> = game
        .state
        .units
        .values()
        .filter(|u| u.unit_type == 1 && templates.contains(&u.id))
        .cloned()
        .collect();
    let d2 = |u: &UnitInfo| {
        let (dx, dy, dz) = (u.pos.0 - me.0, u.pos.1 - me.1, u.pos.2 - me.2);
        dx * dx + dy * dy + dz * dz
    };
    npcs.sort_by(|a, b| d2(a).total_cmp(&d2(b)).then(a.obj_id.cmp(&b.obj_id)));
    npcs
}

/// Sends `text` as a say-chat line starting with `/` (a GM command) and collects the replies.
pub(super) async fn gm_command(game: &mut GameClient, text: &str) -> anyhow::Result<Vec<Packet>> {
    game.chat(0, &format!("/{text}")).await?;
    game.drain(Duration::from_millis(800), Duration::from_secs(4))
        .await
}

/// Text of an `SCChatMessagePacket` (system lines carry the command output).
pub(super) fn chat_text(p: &Packet) -> Option<String> {
    if !p.is_sc(sc::SCChatMessagePacket) {
        return None;
    }
    let mut s = p.stream();
    s.read_i16().ok()?; // chat type
    s.read_i16().ok()?;
    s.read_u32().ok()?;
    s.read_bc().ok()?; // sender obj
    s.read_u32().ok()?; // sender id
    s.read_u8().ok()?; // language
    s.read_u8().ok()?; // race
    s.read_u32().ok()?; // faction
    s.read_string().ok()?; // sender name
    s.read_string().ok()
}

/// Status and objective counters of an `SCQuestContextStarted/UpdatedPacket` (the leading
/// `Quest.Write` block).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(super) struct QuestState {
    pub(super) status: u8,
    pub(super) objectives: [i32; 5],
}

fn quest_state(p: &Packet) -> Option<(u32, QuestState)> {
    if !p.is_sc(sc::SCQuestContextStartedPacket) && !p.is_sc(sc::SCQuestContextUpdatedPacket) {
        return None;
    }
    let template = quest_template_id(p).ok()?;
    let mut s = p.stream();
    s.read_i64().ok()?;
    s.read_u32().ok()?;
    let status = s.read_u8().ok()?;
    let mut objectives = [0; 5];
    for o in &mut objectives {
        *o = s.read_i32().ok()?;
    }
    Some((template, QuestState { status, objectives }))
}

/// The last quest state of `quest_id` among `packets`.
pub(super) fn last_quest_state(packets: &[Packet], quest_id: u32) -> Option<QuestState> {
    packets
        .iter()
        .filter_map(quest_state)
        .filter(|(t, _)| *t == quest_id)
        .map(|(_, q)| q)
        .next_back()
}

/// Size of the fixed part of an item entry after the leading `slot_type`/`slot` bytes
/// (`write_task_item_details`): template, id, grade, flags, count, details (short length +
/// 128 bytes), create time, lifespan, made unit, world, unsecure and unpack time.
fn skip_task_item(s: &mut PacketStream) -> anyhow::Result<(u32, u64)> {
    let template = s.read_u32()?;
    let id = s.read_u64()?;
    s.read_u8()?;
    s.read_u8()?;
    s.read_i32()?;
    let details = s.read_i16()?;
    ensure!(details >= 0, "negative item detail length");
    s.read_bytes(details as usize)?;
    s.read_i64()?;
    s.read_i32()?;
    s.read_u32()?;
    s.read_u8()?;
    s.read_i64()?;
    s.read_i64()?;
    Ok((template, id))
}

/// The id of the item stack of `template_id` that an `SCItemTaskSuccessPacket` among `packets`
/// added (`ItemAdd`) or grew (`ItemCountUpdate`); the last one wins.
fn stack_id(packets: &[Packet], template_id: u32) -> Option<u64> {
    let mut found = None;
    for p in packets
        .iter()
        .filter(|p| p.is_sc(sc::SCItemTaskSuccessPacket))
    {
        let mut s = p.stream();
        let parsed = (|| -> anyhow::Result<()> {
            s.read_u8()?; // task type
            let count = s.read_u8()?;
            for _ in 0..count {
                match s.read_u8()? {
                    // Create, Take, ChangeOwner: slot type, slot, full item.
                    5 | 6 | 15 => {
                        s.read_u8()?;
                        s.read_u8()?;
                        let (template, id) = skip_task_item(&mut s)?;
                        if template == template_id {
                            found = Some(id);
                        }
                    }
                    // AddStack: slot type, slot, id, count, template.
                    4 => {
                        s.read_u8()?;
                        s.read_u8()?;
                        let id = s.read_u64()?;
                        s.read_i32()?;
                        if s.read_u32()? == template_id {
                            found = Some(id);
                        }
                    }
                    other => bail!("unsupported item task action {other}"),
                }
            }
            Ok(())
        })();
        if let Err(e) = parsed {
            tracing::debug!("item task not fully parsed: {e}");
        }
    }
    found
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::game::LEVEL_GAME;
    use aaemu_commons::Marshal;
    use aaemu_game::models::char_data::ActiveQuest;
    use aaemu_game::models::item::Item;
    use aaemu_game::models::item_task::ItemTask;
    use aaemu_game::packets::world::SCChatMessagePacket;
    use aaemu_game::systems::economy::item_packets::SCItemTaskSuccessPacket;
    use aaemu_game::systems::quests::model::{acceptor, kind};
    use aaemu_game::systems::quests::packets::SCQuestContextUpdatedPacket;

    fn packet(opcode: u16, body: Vec<u8>) -> Packet {
        Packet {
            level: LEVEL_GAME,
            opcode,
            body,
        }
    }

    fn item(template_id: u32, id: u64) -> Item {
        let mut i = Item::new(1);
        i.template_id = template_id;
        i.id = id;
        i.count = 5;
        i.slot_type = 2;
        i.slot = 4;
        i
    }

    fn task_packet(tasks: Vec<ItemTask>) -> Packet {
        let mut body = PacketStream::new();
        SCItemTaskSuccessPacket {
            action: 11,
            tasks,
            force_remove: Vec::new(),
        }
        .write(&mut body);
        packet(sc::SCItemTaskSuccessPacket, body.into_bytes())
    }

    #[test]
    fn chat_text_reads_the_system_line() {
        let mut body = PacketStream::new();
        SCChatMessagePacket {
            chat_type: -2,
            sender: None,
            message: "|cFFFF0000Insufficient privileges.|r".into(),
            ability: 0,
            language_type: 0,
        }
        .write(&mut body);
        let p = packet(sc::SCChatMessagePacket, body.into_bytes());
        assert_eq!(
            chat_text(&p).as_deref(),
            Some("|cFFFF0000Insufficient privileges.|r")
        );
        assert_eq!(chat_text(&packet(sc::SCUnitStatePacket, vec![1, 2])), None);
    }

    #[test]
    fn item_add_and_stack_growth_yield_the_stack_id() {
        let added = task_packet(vec![ItemTask::add(&item(17594, 0x77))]);
        assert_eq!(stack_id(&[added], 17594), Some(0x77));

        let other = task_packet(vec![ItemTask::add(&item(1, 0x55))]);
        assert_eq!(stack_id(&[other], 17594), None);

        let grown = task_packet(vec![
            ItemTask::add(&item(1, 0x55)),
            ItemTask::count_update(&item(17594, 0x88), 5),
        ]);
        assert_eq!(stack_id(&[grown], 17594), Some(0x88));
    }

    #[test]
    fn quest_state_reads_status_and_objective_counters() {
        let quest = ActiveQuest {
            id: 9,
            template_id: 2394,
            status: status::PROGRESS,
            objectives: [2, 0, 0, 0, 0],
            step: kind::PROGRESS,
            acceptor_type: acceptor::NPC,
            component_id: 10289,
            acceptor_id: 1,
            time_ms: ActiveQuest::DATETIME_MIN_MS,
        };
        let mut body = PacketStream::new();
        SCQuestContextUpdatedPacket::new(quest, 10289, 0).write(&mut body);
        let p = packet(sc::SCQuestContextUpdatedPacket, body.into_bytes());
        let (template, state) = quest_state(&p).unwrap();
        assert_eq!(template, 2394);
        assert_eq!(state.status, status::PROGRESS);
        assert_eq!(state.objectives, [2, 0, 0, 0, 0]);
        assert_eq!(last_quest_state(&[p], 2394), Some(state));
        assert_eq!(last_quest_state(&[], 2394), None);
    }
}
