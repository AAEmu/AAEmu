//! `Scripts/Commands/QuestCmd.cs` + `Utils/QuestCommandUtil.cs`.

use super::prelude::*;
use crate::systems::quests::engine::Ctx;
use crate::systems::quests::events::{self as quest_events, with_quests};
use crate::systems::quests::model::{kind, status};
use crate::systems::quests::packets::SCQuestContextUpdatedPacket;

/// `QuestComponentKind` member names.
const KIND_NAMES: [&str; 9] = [
    "Invalid", "None", "Start", "Supply", "Progress", "Fail", "Ready", "Drop", "Reward",
];
/// `QuestStatus` member names.
const STATUS_NAMES: [&str; 7] = [
    "Invalid",
    "Progress",
    "Failed",
    "Ready",
    "Dropped",
    "Completed",
    "DailyCompleted",
];
/// `QuestAcceptorType` member names.
const ACCEPTOR_NAMES: [&str; 7] = [
    "Unknown", "Npc", "Doodad", "Sphere", "Item", "Skill", "Buff",
];

const INDEX_OUT_OF_RANGE: &str = "Index was outside the bounds of the array.";

/// `args[i]` (`IndexOutOfRangeException` past the end).
fn arg(args: &[String], i: usize) -> anyhow::Result<&str> {
    args.get(i)
        .map(String::as_str)
        .ok_or_else(|| anyhow::anyhow!(INDEX_OUT_OF_RANGE))
}

/// One `character.Quests` operation.
fn quest_op<R>(
    server: &GameServer,
    player: &PlayerRef,
    f: impl FnOnce(&mut Ctx<'_>) -> R,
) -> anyhow::Result<R> {
    with_quests(server, player, None, f)
        .ok_or_else(|| anyhow::anyhow!("nested quest operation for {}", player.character_id))
}

/// The current `Step` of an active quest.
fn step_of(ctx: &Ctx<'_>, quest_id: u32) -> u8 {
    ctx.st
        .active_quest(quest_id)
        .map_or(kind::INVALID, |q| q.step)
}

/// `QuestCmd`.
pub struct QuestCmd;

impl QuestCmd {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for QuestCmd {
    fn command_names(&self) -> Vec<String> {
        names(&["quest"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }
        get_command_choice(self, out, ctx, &args[0], args)
    }

    fn get_command_line_help(&self) -> String {
        "<list||template||add||remove||step||prog||uncomplete||resetdaily||progress||objective>"
            .into()
    }

    fn get_command_help_text(&self) -> String {
        "[Quest] /quest <add||remove||list||prog||reward||resetdaily>".into()
    }
}

/// `QuestCommandUtil.GetCommandChoice(command, messageOutput, character, choice, args)`.
pub fn get_command_choice(
    command: &dyn Command,
    out: &mut dyn MessageOutput,
    ctx: &CommandContext<'_>,
    choice: &str,
    args: &[String],
) -> anyhow::Result<()> {
    match choice {
        "add" => {
            if args.len() >= 2 {
                let server = ctx.server()?;
                let player = ctx.player()?;
                let mut acceptor_type = 0i64;
                let mut acceptor_id = 0u32;
                if args.len() >= 3 {
                    if let Some(v) = enum_try_parse(&args[2], &ACCEPTOR_NAMES) {
                        acceptor_type = v;
                    }
                }
                if args.len() >= 4 {
                    if let Some(v) = try_parse::<u32>(&args[3]) {
                        acceptor_id = v;
                    }
                } else if acceptor_type == 1 {
                    // character.CurrentTarget is Npc npc
                    if let Some(npc) =
                        current_target(server, &player).and_then(|t| t.as_npc().cloned())
                    {
                        acceptor_id = npc.data().unit.template_id;
                    }
                }
                if let Some(quest_id) = try_parse::<u32>(&args[1]) {
                    // Deviation: `QuestAcceptorType` is stored as a byte.
                    let acceptor_type = acceptor_type as u8;
                    quest_op(server, &player, |q| {
                        q.add_quest(quest_id, true, acceptor_type, acceptor_id);
                        if let Some(h) = q.st.active_handle(quest_id) {
                            q.request_evaluation(h);
                        }
                    })?;
                }
            } else {
                CommandManager::send_error_text(
                    command,
                    out,
                    "Proper usage: /quest add <questId> [<questAcceptorType> <acceptorId>]\nYou can also target the quest giver instead of providing the acceptor type and id",
                );
            }
        }
        "list" => {
            let server = ctx.server()?;
            let player = ctx.player()?;
            CommandManager::send_normal_text(command, out, "LIST");
            let lines = quest_op(server, &player, |q| {
                q.st.active
                    .values()
                    .filter_map(|h| q.st.quest(*h))
                    .map(|quest| {
                        let objectives: Vec<String> =
                            quest.objectives.iter().map(i32::to_string).collect();
                        format!(
                            "Quest {}: Step({}), Status({}), ComponentId({}), Objectives({}) - @QUEST_NAME({})",
                            quest.template_id,
                            kind::name(quest.step),
                            enum_name(i64::from(quest.status), &STATUS_NAMES),
                            quest.component_id,
                            objectives.join(", "),
                            quest.template_id
                        )
                    })
                    .collect::<Vec<_>>()
            })?;
            for line in lines {
                out.send_message(&line);
            }
        }
        "step" => {
            if args.len() >= 2 {
                if let Some(quest_id) = try_parse::<u32>(&args[1]) {
                    let server = ctx.server()?;
                    let player = ctx.player()?;
                    let step = if args.len() >= 3 {
                        enum_try_parse(&args[2], &KIND_NAMES)
                    } else {
                        None
                    };
                    // None = no quest; Some(None) = no step given; Some(Some(ok)).
                    let res = quest_op(server, &player, |q| {
                        if !q.has_quest(quest_id) {
                            return None;
                        }
                        // `(uint)stepId`
                        Some(step.map(|s| q.gm_set_step(quest_id, s as u32, -1)))
                    })?;
                    match res {
                        None => CommandManager::send_error_text(
                            command,
                            out,
                            &format!("You do not have the quest {quest_id}"),
                        ),
                        Some(None) => {}
                        Some(Some(true)) => CommandManager::send_normal_text(
                            command,
                            out,
                            &format!(
                                "Set Step {} for Quest {quest_id}",
                                enum_name(step.unwrap_or_default(), &KIND_NAMES)
                            ),
                        ),
                        Some(Some(false)) => CommandManager::send_error_text(
                            command,
                            out,
                            "Proper usage: /quest step <questId> <stepId>",
                        ),
                    }
                }
            } else {
                CommandManager::send_error_text(
                    command,
                    out,
                    "Proper usage: /quest step <questId> <stepId>",
                );
            }
        }
        "prog" => {
            if args.len() >= 2 {
                if let Some(quest_id) = try_parse::<u32>(&args[1]) {
                    let server = ctx.server()?;
                    let player = ctx.player()?;
                    // None = no quest; Some(None) = dropped (break); Some(Some(step)).
                    let res = quest_op(server, &player, |q| {
                        let h = q.st.active_handle(quest_id)?;
                        if step_of(q, quest_id) == kind::NONE {
                            q.set_step(h, kind::START);
                        }
                        let step = q.st.quest(h).map_or(kind::INVALID, |x| x.step);
                        match step {
                            kind::START => q.set_step(h, kind::SUPPLY),
                            kind::SUPPLY => q.set_step(h, kind::PROGRESS),
                            kind::PROGRESS => q.set_step(h, kind::READY),
                            kind::READY => q.set_step(h, kind::REWARD),
                            s if s > kind::REWARD => {
                                q.quest_drop(h, true);
                                return Some(None);
                            }
                            _ => {}
                        }
                        Some(Some(q.st.quest(h).map_or(kind::INVALID, |x| x.step)))
                    })?;
                    match res {
                        None => CommandManager::send_error_text(
                            command,
                            out,
                            &format!("You do not have the quest {quest_id}"),
                        ),
                        Some(None) => {}
                        Some(Some(step)) => CommandManager::send_normal_text(
                            command,
                            out,
                            &format!("Perform step {} for quest {quest_id}", kind::name(step)),
                        ),
                    }
                }
            } else {
                CommandManager::send_error_text(
                    command,
                    out,
                    "Proper usage: /quest update <questId>",
                );
            }
        }
        "remove" => {
            if args.len() >= 2 {
                if let Some(quest_id) = try_parse::<u32>(&args[1]) {
                    let server = ctx.server()?;
                    let player = ctx.player()?;
                    let had = quest_op(server, &player, |q| {
                        if q.has_quest(quest_id) {
                            // Remove from CompletedQuests
                            q.drop_quest(quest_id, true, true);
                            true
                        } else {
                            false
                        }
                    })?;
                    if !had {
                        CommandManager::send_error_text(
                            command,
                            out,
                            &format!("You do not have the quest {quest_id}"),
                        );
                    }
                    // send packets to keep the client up-to-date on quests
                    quest_events::send_quests(&player);
                    quest_events::send_completed_quests(&player);
                }
            } else {
                ctx.character
                    .send_message("[Quest] Proper usage: /quest remove <questId>");
            }
        }
        "uncomplete" => {
            // Drops a quest if active, and completely removes it, as if it was never started
            if args.len() >= 2 {
                if let Some(quest_id) = try_parse::<u32>(&args[1]) {
                    let server = ctx.server()?;
                    let player = ctx.player()?;
                    let dropped = quest_op(server, &player, |q| {
                        let had = q.has_quest(quest_id);
                        if had {
                            q.drop_quest(quest_id, true, true);
                        }
                        had
                    })?;
                    if dropped {
                        CommandManager::send_normal_text(
                            command,
                            out,
                            &format!("Quest @QUEST_NAME({quest_id}) ({quest_id}) has been dropped"),
                        );
                    }
                    let uncompleted = quest_op(server, &player, |q| {
                        let complete = q.has_quest_completed(quest_id);
                        if complete {
                            q.set_completed_quest_flag(quest_id, false);
                        }
                        complete
                    })?;
                    if uncompleted {
                        CommandManager::send_normal_text(
                            command,
                            out,
                            &format!(
                                "Quest @QUEST_NAME({quest_id}) ({quest_id}) has been been marked as not completed. You will need to log in your character again to correctly reflect these changes (return to character select)"
                            ),
                        );
                    }
                    quest_events::send_quests(&player);
                    quest_events::send_completed_quests(&player);
                }
            } else {
                CommandManager::send_error_text(
                    command,
                    out,
                    "Proper usage: /quest remove <questId>",
                );
            }
        }
        "resetdaily" => {
            let server = ctx.server()?;
            let player = ctx.player()?;
            quest_op(server, &player, |q| q.reset_daily_quests(true))?;
            CommandManager::send_normal_text(command, out, "Your daily quests have been reset");
        }
        "objective" => {
            if args.len() >= 8 {
                let Some(quest_val) = try_parse::<u32>(&args[1]) else {
                    return Ok(());
                };
                let server = ctx.server()?;
                let player = ctx.player()?;
                let has = quest_op(server, &player, |q| q.st.active_handle(quest_val).is_some())?;
                if !has {
                    CommandManager::send_error_text(
                        command,
                        out,
                        &format!("No active quest Id {quest_val}"),
                    );
                    return Ok(());
                }
                let mut paras = [0i32; 5];
                for (i, p) in paras.iter_mut().enumerate() {
                    let Some(v) = try_parse::<i32>(&args[2 + i]) else {
                        return Ok(());
                    };
                    *p = v;
                }
                let Some(new_status) = enum_try_parse(&args[7], &STATUS_NAMES) else {
                    return Ok(());
                };
                // C#: `args[8]` with only 8 arguments throws IndexOutOfRangeException.
                let Some(comp) = try_parse::<u32>(arg(args, 8)?) else {
                    return Ok(());
                };
                quest_op(server, &player, |q| {
                    let Some(h) = q.st.active_handle(quest_val) else {
                        return;
                    };
                    let row = {
                        let Some(quest) = q.st.quest_mut(h) else {
                            return;
                        };
                        quest.objectives = paras;
                        // Deviation: `QuestStatus` is stored as a byte.
                        quest.status = new_status as u8;
                        quest.component_id = comp;
                        quest.row()
                    };
                    let now = q.now_ms;
                    q.send(SCQuestContextUpdatedPacket::new(row, comp, now));
                })?;
            } else {
                CommandManager::send_error_text(
                    command,
                    out,
                    "/quest objective <questId> <obj1> <obj2> <obj3> <obj4> <obj5> <questStatus> <componentId>",
                );
            }
        }
        "progress" => {
            // C#: `args.Length >= 1` is always true: `args[1]` throws without a quest id.
            let Some(quest_val) = try_parse::<u32>(arg(args, 1)?) else {
                return Ok(());
            };
            let server = ctx.server()?;
            let player = ctx.player()?;
            let res = quest_op(server, &player, |q| {
                let h = q.st.active_handle(quest_val)?;
                let mut loop_counter = 0;
                while q.st.quest(h).map_or(kind::INVALID, |x| x.step) < kind::PROGRESS
                    && loop_counter < 10
                {
                    loop_counter += 1;
                    let mut res = false;
                    let has_step =
                        q.st.quest(h)
                            .is_some_and(|x| x.step_index(x.step).is_some());
                    if has_step {
                        res = q.run_current_step(h);
                    }
                    if !res {
                        q.go_to_next_step(h);
                    }
                }
                let row = {
                    let quest = q.st.quest_mut(h)?;
                    quest.objectives = [0; 5];
                    quest.status = status::PROGRESS;
                    quest.component_id = 0;
                    quest.row()
                };
                q.request_evaluation(h);
                let now = q.now_ms;
                q.send(SCQuestContextUpdatedPacket::new(row.clone(), 0, now));
                Some((row.template_id, row.step))
            })?;
            match res {
                None => CommandManager::send_error_text(
                    command,
                    out,
                    &format!("No active quest Id {quest_val}"),
                ),
                Some((template_id, step)) => CommandManager::send_normal_text(
                    command,
                    out,
                    &format!(
                        "Reset objectives of quest @QUEST_NAME({template_id}) ({template_id}) and moved to {} step",
                        kind::name(step)
                    ),
                ),
            }
        }
        "template" => {
            if args.len() >= 2 {
                let Some(quest_val) = try_parse::<u32>(&args[1]) else {
                    return Ok(());
                };
                let server = ctx.server()?;
                let player = ctx.player()?;
                let character = &ctx.character;
                let data = &server.game_data.systems.quests;
                let Some(quest_template) = data.template(quest_val) else {
                    character.send_message_typed(
                        chat_type::SYSTEM,
                        &format!("[Quest] No such quest {quest_val}"),
                        Some(Color::RED),
                    );
                    return Ok(());
                };
                character.send_message(&format!("[Quest] {quest_val} -- Template"));
                for component_id in &quest_template.components {
                    let Some(component_template) = data.component(*component_id) else {
                        continue;
                    };
                    character.send_message(&format!(
                        "-- Component({component_id}), Step {}",
                        kind::name(component_template.kind_id)
                    ));
                    for &a in &component_template.acts {
                        let act_template = &data.acts[a];
                        // C#: the detail act templates never get a `DetailType` (null).
                        character.send_message(&format!(
                            "---- Act({}) => ({})",
                            act_template.act_id, act_template.detail_id
                        ));
                    }
                }
                character.send_message(&format!("[Quest] {quest_val} -- End of Template"));

                let active = quest_op(server, &player, |q| {
                    q.st.active_quest(quest_val).map(|quest| {
                        quest
                            .steps
                            .iter()
                            .map(|s| {
                                (
                                    s.kind,
                                    s.components
                                        .iter()
                                        .map(|c| {
                                            (
                                                c.template_id,
                                                c.acts
                                                    .iter()
                                                    .map(|a| {
                                                        let t = &data.acts[a.template];
                                                        (t.act_id, t.detail_id)
                                                    })
                                                    .collect::<Vec<_>>(),
                                            )
                                        })
                                        .collect::<Vec<_>>(),
                                )
                            })
                            .collect::<Vec<_>>()
                    })
                })?;
                match active {
                    Some(steps) => {
                        character.send_message(&format!("[Quest] {quest_val} -- Active"));
                        for (step_id, components) in steps {
                            character.send_message(&format!("Step {}", kind::name(step_id)));
                            for (component_id, acts) in components {
                                character.send_message(&format!("-- Component({component_id})"));
                                for (id, detail_id) in acts {
                                    // C#: `QuestAct.DetailType` is never set (null).
                                    character
                                        .send_message(&format!("---- Act({id}) => ({detail_id})"));
                                }
                            }
                        }
                        character
                            .send_message(&format!("[Quest] {quest_val} -- End of Active Quest"));
                    }
                    None => character.send_message(&format!("[Quest] {quest_val} is not active")),
                }
            } else {
                ctx.character
                    .send_message("[Quest] /quest template <questId>");
            }
        }
        _ => CommandManager::send_default_help_text(command, out),
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::data::GameData;
    use crate::opcodes::sc;
    use crate::systems::quests::model::{detail, kind};
    use crate::systems::quests::templates::{ActKind, QuestComponentTemplate, QuestTemplate};
    use crate::world::PlayerRef;

    const QUEST: u32 = 100;
    const DAILY: u32 = 101;

    /// Quest `qid`: Start (auto) -> Progress (hunt NPC 200) -> Ready (report to NPC 1).
    fn quest(d: &mut crate::systems::quests::data::QuestData, qid: u32, detail_id: u32) {
        d.add_template(QuestTemplate {
            id: qid,
            detail_id,
            ..QuestTemplate::default()
        });
        let component = |id: u32, kind_id: u8| QuestComponentTemplate {
            id,
            quest_id: qid,
            kind_id,
            npc_ai_id: 1,
            ..QuestComponentTemplate::default()
        };
        d.add_component(component(qid * 10 + 1, kind::START));
        d.add_act(
            qid * 10 + 1,
            qid * 10 + 1,
            qid * 10 + 1,
            0,
            ActKind::ConAutoComplete,
        );
        d.add_component(component(qid * 10 + 2, kind::PROGRESS));
        d.add_act(
            qid * 10 + 2,
            qid * 10 + 2,
            qid * 10 + 2,
            1,
            ActKind::ObjMonsterHunt {
                npc_id: 200,
                use_alias: false,
                alias_id: 0,
                highlight_doodad_id: 0,
                highlight_doodad_phase: -1,
            },
        );
        d.add_component(component(qid * 10 + 3, kind::READY));
        d.add_act(
            qid * 10 + 3,
            qid * 10 + 3,
            qid * 10 + 3,
            0,
            ActKind::ConReportNpc {
                npc_id: 1,
                use_alias: false,
                alias_id: 0,
            },
        );
    }

    fn data() -> GameData {
        let mut gd = GameData::empty();
        quest(&mut gd.systems.quests, QUEST, detail::NORMAL);
        quest(&mut gd.systems.quests, DAILY, detail::DAILY);
        gd.systems.quests.finish();
        gd
    }

    /// (step, status, objectives[0]) of an active quest after the evaluation queue ran.
    fn row(server: &crate::server::GameServer, p: &PlayerRef, id: u32) -> Option<(u8, u8, i32)> {
        server.systems.quests.run_queued_evaluations(server);
        p.data()
            .quests
            .active
            .iter()
            .find(|q| q.template_id == id)
            .map(|q| (q.step, q.status, q.objectives[0]))
    }

    #[tokio::test]
    async fn quest_add_list_and_help() {
        let server = harness::server_with(data());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "quest").await;
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/quest|r |cFFEEEEAA<list||template||add||remove||step||prog||uncomplete||resetdaily||progress||objective>|r\n|cFF888888[Quest] /quest <add||remove||list||prog||reward||resetdaily>|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "quest nothing").await;
        assert_eq!(out.len(), 1);
        let (_, out) = harness::run(&server, &gm.p, "quest add").await;
        assert_eq!(
            out,
            [harness::error(
                "quest",
                "Proper usage: /quest add <questId> [<questAcceptorType> <acceptorId>]\nYou can also target the quest giver instead of providing the acceptor type and id"
            )]
        );
        gm.frames();
        let (_, out) = harness::run(&server, &gm.p, "quest add 100 Npc 77").await;
        assert!(out.is_empty());
        assert!(gm
            .packets()
            .iter()
            .any(|(op, _)| *op == sc::SCQuestContextStartedPacket));
        assert_eq!(row(&server, &gm.p, QUEST), Some((kind::PROGRESS, 1, 0)));
        let acceptor = {
            let c = gm.p.data();
            let q = c
                .quests
                .active
                .iter()
                .find(|q| q.template_id == QUEST)
                .cloned();
            q.map(|q| (q.acceptor_type, q.acceptor_id))
        };
        assert_eq!(acceptor, Some((1, 77)));
        let (_, out) = harness::run(&server, &gm.p, "quest list").await;
        assert_eq!(
            out,
            [
                harness::normal("quest", "LIST"),
                "Quest 100: Step(Progress), Status(Progress), ComponentId(0), Objectives(0, 0, 0, 0, 0) - @QUEST_NAME(100)".to_string(),
            ]
        );
    }

    #[tokio::test]
    async fn quest_step_prog_objective_and_progress() {
        let server = harness::server_with(data());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "quest step 100 Ready").await;
        assert_eq!(
            out,
            [harness::error("quest", "You do not have the quest 100")]
        );
        harness::run(&server, &gm.p, "quest add 100").await;
        assert_eq!(
            row(&server, &gm.p, QUEST).map(|r| r.0),
            Some(kind::PROGRESS)
        );
        let (_, out) = harness::run(&server, &gm.p, "quest step 100 Ready").await;
        assert_eq!(
            out,
            [harness::normal("quest", "Set Step Ready for Quest 100")]
        );
        assert_eq!(row(&server, &gm.p, QUEST).map(|r| r.0), Some(kind::READY));
        let (_, out) = harness::run(&server, &gm.p, "quest step 100 9").await;
        assert_eq!(
            out,
            [harness::error(
                "quest",
                "Proper usage: /quest step <questId> <stepId>"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "quest step").await;
        assert_eq!(
            out,
            [harness::error(
                "quest",
                "Proper usage: /quest step <questId> <stepId>"
            )]
        );
        // step 4 = Progress
        let (_, out) = harness::run(&server, &gm.p, "quest step 100 4").await;
        assert_eq!(
            out,
            [harness::normal("quest", "Set Step Progress for Quest 100")]
        );
        assert_eq!(
            row(&server, &gm.p, QUEST).map(|r| r.0),
            Some(kind::PROGRESS)
        );

        // prog: Progress -> Ready
        let (_, out) = harness::run(&server, &gm.p, "quest prog 100").await;
        assert_eq!(
            out,
            [harness::normal("quest", "Perform step Ready for quest 100")]
        );
        let (_, out) = harness::run(&server, &gm.p, "quest prog 555").await;
        assert_eq!(
            out,
            [harness::error("quest", "You do not have the quest 555")]
        );
        let (_, out) = harness::run(&server, &gm.p, "quest prog").await;
        assert_eq!(
            out,
            [harness::error(
                "quest",
                "Proper usage: /quest update <questId>"
            )]
        );

        // objective
        gm.frames();
        let (_, out) =
            harness::run(&server, &gm.p, "quest objective 100 3 0 0 0 0 Ready 1002").await;
        assert!(out.is_empty());
        assert_eq!(
            gm.packets()
                .iter()
                .filter(|(op, _)| *op == sc::SCQuestContextUpdatedPacket)
                .count(),
            1
        );
        let st =
            gm.p.data()
                .quests
                .active
                .iter()
                .find(|q| q.template_id == QUEST)
                .map(|q| (q.objectives, q.status, q.component_id));
        assert_eq!(st, Some(([3, 0, 0, 0, 0], 3, 1002)));
        let (_, out) = harness::run(&server, &gm.p, "quest objective 7 0 0 0 0 0 Ready 1").await;
        assert_eq!(out, [harness::error("quest", "No active quest Id 7")]);
        let (_, out) = harness::run(&server, &gm.p, "quest objective 100").await;
        assert_eq!(
            out,
            [harness::error(
                "quest",
                "/quest objective <questId> <obj1> <obj2> <obj3> <obj4> <obj5> <questStatus> <componentId>"
            )]
        );
        // C#: 8 arguments read args[8].
        gm.chat();
        let (_, out) = harness::run(&server, &gm.p, "quest objective 100 0 0 0 0 0 Ready").await;
        assert!(out.is_empty());
        assert_eq!(
            gm.chat(),
            ["|cFFFF0000Index was outside the bounds of the array.|r"]
        );

        // progress: objectives reset, status Progress
        let (_, out) = harness::run(&server, &gm.p, "quest progress 100").await;
        assert_eq!(
            out,
            [harness::normal(
                "quest",
                "Reset objectives of quest @QUEST_NAME(100) (100) and moved to Ready step"
            )]
        );
        let st =
            gm.p.data()
                .quests
                .active
                .iter()
                .find(|q| q.template_id == QUEST)
                .map(|q| (q.objectives, q.status, q.component_id));
        assert_eq!(st.map(|s| (s.0, s.1)), Some(([0; 5], 1)));
        let (_, out) = harness::run(&server, &gm.p, "quest progress 9").await;
        assert_eq!(out, [harness::error("quest", "No active quest Id 9")]);
        gm.chat();
        harness::run(&server, &gm.p, "quest progress").await;
        assert_eq!(
            gm.chat(),
            ["|cFFFF0000Index was outside the bounds of the array.|r"]
        );
    }

    #[tokio::test]
    async fn quest_remove_uncomplete_resetdaily_and_template() {
        let server = harness::server_with(data());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        harness::run(&server, &gm.p, "quest add 100").await;
        gm.frames();
        let (_, out) = harness::run(&server, &gm.p, "quest remove 100").await;
        assert!(out.is_empty());
        assert_eq!(row(&server, &gm.p, QUEST), None);
        let ops: Vec<u16> = gm.packets().into_iter().map(|f| f.0).collect();
        assert!(ops.ends_with(&[sc::SCQuestsPacket, sc::SCCompletedQuestsPacket]));
        let (_, out) = harness::run(&server, &gm.p, "quest remove 100").await;
        assert_eq!(
            out,
            [harness::error("quest", "You do not have the quest 100")]
        );
        gm.chat();
        let (_, out) = harness::run(&server, &gm.p, "quest remove").await;
        assert!(out.is_empty());
        assert_eq!(gm.chat(), ["[Quest] Proper usage: /quest remove <questId>"]);

        // uncomplete: an active quest, then a completed one.
        harness::run(&server, &gm.p, "quest add 100").await;
        let (_, out) = harness::run(&server, &gm.p, "quest uncomplete 100").await;
        assert_eq!(
            out,
            [harness::normal(
                "quest",
                "Quest @QUEST_NAME(100) (100) has been dropped"
            )]
        );
        crate::systems::quests::events::with_quests(&server, &gm.p, None, |q| {
            q.set_completed_quest_flag(DAILY, true);
        });
        let (_, out) = harness::run(&server, &gm.p, "quest uncomplete 101").await;
        assert_eq!(
            out,
            [harness::normal(
                "quest",
                "Quest @QUEST_NAME(101) (101) has been been marked as not completed. You will need to log in your character again to correctly reflect these changes (return to character select)"
            )]
        );
        assert!(!crate::systems::quests::events::has_quest_completed_in(
            &gm.p.data(),
            DAILY
        ));
        let (_, out) = harness::run(&server, &gm.p, "quest uncomplete").await;
        assert_eq!(
            out,
            [harness::error(
                "quest",
                "Proper usage: /quest remove <questId>"
            )]
        );

        // resetdaily
        crate::systems::quests::events::with_quests(&server, &gm.p, None, |q| {
            q.set_completed_quest_flag(DAILY, true);
        });
        gm.frames();
        let (_, out) = harness::run(&server, &gm.p, "quest resetdaily").await;
        assert_eq!(
            out,
            [harness::normal(
                "quest",
                "Your daily quests have been reset"
            )]
        );
        assert!(!crate::systems::quests::events::has_quest_completed_in(
            &gm.p.data(),
            DAILY
        ));
        assert!(gm
            .packets()
            .iter()
            .any(|(op, _)| *op == sc::SCQuestContextResetPacket));

        // template
        gm.chat();
        harness::run(&server, &gm.p, "quest template 100").await;
        assert_eq!(
            gm.chat(),
            [
                "[Quest] 100 -- Template",
                "-- Component(1001), Step Start",
                "---- Act(1001) => (1001)",
                "-- Component(1002), Step Progress",
                "---- Act(1002) => (1002)",
                "-- Component(1003), Step Ready",
                "---- Act(1003) => (1003)",
                "[Quest] 100 -- End of Template",
                "[Quest] 100 is not active",
            ]
        );
        harness::run(&server, &gm.p, "quest add 100").await;
        gm.chat();
        harness::run(&server, &gm.p, "quest template 100").await;
        let chat = gm.chat();
        assert_eq!(chat[8], "[Quest] 100 -- Active");
        assert_eq!(chat[9], "Step Start");
        assert_eq!(chat[10], "-- Component(1001)");
        assert_eq!(chat[11], "---- Act(1001) => (1001)");
        assert_eq!(chat.last().unwrap(), "[Quest] 100 -- End of Active Quest");
        harness::run(&server, &gm.p, "quest template 5").await;
        assert_eq!(gm.chat(), ["|cFFFF0000[Quest] No such quest 5|r"]);
        harness::run(&server, &gm.p, "quest template").await;
        assert_eq!(gm.chat(), ["[Quest] /quest template <questId>"]);
    }
}
