//! `Scripts/Commands/TestAI.cs`.

use super::prelude::*;
use crate::models::movement::GameStanceType;
use crate::systems::ai::framework::BehaviorKind;
use crate::systems::ai::manager::{has_ai, run_now};
use crate::systems::ai::params::{AiCommandCategory, AiCommands};
use crate::systems::npcs::npc::packets::SCUnitModelPostureChangedPacket;

/// `BehaviorKind` members in declaration order (value = index).
const BEHAVIOR_KINDS: [BehaviorKind; 21] = [
    BehaviorKind::Alert,
    BehaviorKind::AlmightyAttack,
    BehaviorKind::Attack,
    BehaviorKind::Dead,
    BehaviorKind::Despawning,
    BehaviorKind::DoNothing,
    BehaviorKind::Dummy,
    BehaviorKind::FollowPath,
    BehaviorKind::FollowUnit,
    BehaviorKind::HoldPosition,
    BehaviorKind::Idle,
    BehaviorKind::ReturnState,
    BehaviorKind::Roaming,
    BehaviorKind::RunCommandSet,
    BehaviorKind::Spawning,
    BehaviorKind::Talk,
    BehaviorKind::ArcherAttack,
    BehaviorKind::BigMonsterAttack,
    BehaviorKind::FlytrapAlert,
    BehaviorKind::FlytrapAttack,
    BehaviorKind::WildBoarAttack,
];

/// `GameStanceType` member names (value = index).
const GAME_STANCE_NAMES: [&str; 9] = [
    "Combat", "Relaxed", "Swim", "CoSwim", "Combat2", "Stealth", "Crouch", "Prone", "Fly",
];

fn behavior_names() -> Vec<String> {
    BEHAVIOR_KINDS.iter().map(|k| format!("{k:?}")).collect()
}

/// The full type name of the behavior class of a kind (`behavior.ToString()`).
fn behavior_type_name(kind: BehaviorKind) -> String {
    let folder = match kind {
        BehaviorKind::ArcherAttack => "Archer",
        BehaviorKind::BigMonsterAttack => "BigMonster",
        BehaviorKind::FlytrapAlert | BehaviorKind::FlytrapAttack => "Flytrap",
        BehaviorKind::WildBoarAttack => "WildBoar",
        _ => "Common",
    };
    format!("AAEmu.Game.Models.Game.AI.v2.Behaviors.{folder}.{kind:?}Behavior")
}

/// `TestAI`.
pub struct TestAI;

impl TestAI {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestAI {
    fn command_names(&self) -> Vec<String> {
        names(&["testai", "ai"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let Some(target) = current_target(server, &character) else {
            CommandManager::send_error_text(self, out, "You don't have anything selected");
            return Ok(());
        };
        let Some(npc) = target.as_npc().cloned() else {
            CommandManager::send_error_text(self, out, "You don't have a NPC selected");
            return Ok(());
        };

        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        if !has_ai(server, npc.obj_id) {
            CommandManager::send_error_text(self, out, "Target has no AI attached");
            return Ok(());
        }

        let id = npc.obj_id;
        let template_id = npc.data().unit.template_id;
        // `npc.Ai.X` (the AI can disappear between calls: `Ai == null` then throws)
        let nre = || anyhow::anyhow!(NULL_REFERENCE);
        let action = args[0].to_lowercase();

        match action.as_str() {
            "set_behavior" | "set" => {
                let new_behavior = if args.len() <= 1 {
                    None
                } else {
                    enum_try_parse(
                        &args[1],
                        &behavior_names()
                            .iter()
                            .map(String::as_str)
                            .collect::<Vec<_>>(),
                        i64::from(i32::MIN),
                        i64::from(i32::MAX),
                    )
                };
                let Some(new_behavior) = new_behavior else {
                    // C#: the message shows the action (args[0]), not the value
                    CommandManager::send_error_text(
                        self,
                        out,
                        &format!("Not a valid behavior value {}", args[0]),
                    );
                    return Ok(());
                };
                let kind = usize::try_from(new_behavior)
                    .ok()
                    .and_then(|i| BEHAVIOR_KINDS.get(i).copied());
                let name = enum_name(
                    new_behavior,
                    &behavior_names()
                        .iter()
                        .map(String::as_str)
                        .collect::<Vec<_>>(),
                );
                let go: fn(&mut crate::systems::ai::framework::AiCtx<'_>) = match kind {
                    Some(BehaviorKind::Alert) => |c| c.go_to_alert(),
                    Some(BehaviorKind::Despawning) => |c| c.go_to_despawn(),
                    Some(BehaviorKind::Idle) => |c| c.go_to_idle(),
                    Some(BehaviorKind::ReturnState) => |c| c.go_to_return(),
                    Some(BehaviorKind::RunCommandSet) => |c| c.go_to_run_command_set(),
                    Some(BehaviorKind::Spawning) => |c| c.go_to_spawn(),
                    Some(BehaviorKind::Talk) => |c| c.go_to_talk(),
                    Some(BehaviorKind::Dummy) => |c| c.go_to_dummy(),
                    Some(BehaviorKind::FollowPath) => |c| c.go_to_follow_path(),
                    Some(BehaviorKind::FollowUnit) => |c| c.go_to_follow_unit(),
                    _ => {
                        CommandManager::send_error_text(
                            self,
                            out,
                            &format!("Unsupported behavior {name} for /testai"),
                        );
                        return Ok(());
                    }
                };
                run_now(server, id, go).ok_or_else(nre)?;

                CommandManager::send_normal_text(self, out, &format!("Target AI set to {name}"));
            }
            "list" => {
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("Available behaviors for {id}"),
                );
                let list = run_now(server, id, |c| c.ai.behavior_list()).ok_or_else(nre)?;
                for kind in list {
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!(
                            "{kind:?} -> {}",
                            behavior_type_name(kind).replace("AAEmu.Game.Models.Game", "")
                        ),
                    );
                }
            }
            "info" => {
                let (ai_kind, current, points, remaining, commands, follow) =
                    run_now(server, id, |c| {
                        (
                            c.ai.kind,
                            c.ai.current_behavior(),
                            c.ai.path_handler.ai_path_points.len(),
                            c.ai.path_handler.ai_path_points_remaining.len(),
                            c.ai.commands_queue.len(),
                            c.ai.follow_unit.clone(),
                        )
                    })
                    .ok_or_else(nre)?;
                // C#: `GetCurrentBehavior().ToString()` throws without a current behavior
                let current = current.ok_or_else(nre)?;
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "Using AI: {}, CurrentBehavior: {}",
                        ai_kind.class_name().replace("AiCharacter", ""),
                        behavior_type_name(current).replace("AAEmu.Game.Models.Game.AI.", "")
                    ),
                );
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("AI Path has {points} points ({remaining} remaining in queue)"),
                );
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("AI Commands has {commands} actions in queue"),
                );
                if let Some(spawner) = server.systems.npcs.spawns.npc_spawner_of(id) {
                    let template = spawner.template().ok_or_else(nre)?;
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!(
                            "SpawnerId {}, SpawnerTemplateId {} @ {}",
                            spawner.id,
                            template.id,
                            world_spawn_position_string(&spawner.position())
                        ),
                    );
                    if spawner.follow_npc > 0 {
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!(
                                "Spawner set to follow @NPC_NAME({0}) ({0})",
                                spawner.follow_npc
                            ),
                        );
                    }
                }

                if let Some(WorldObject::Npc(following)) = &follow {
                    let tid = following.data().unit.template_id;
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!(
                            "Currently following ObjId {} @NPC_NAME({tid}) ({tid})",
                            following.obj_id
                        ),
                    );
                }

                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("AI Commands has {commands} actions in queue"),
                );
            }
            "load_path" => {
                if args.len() <= 1 {
                    CommandManager::send_error_text(self, out, "No path file provided");
                    return Ok(());
                }

                let file = args[1].clone();
                let loaded = run_now(server, id, move |c| c.load_ai_path_points(&file, false))
                    .ok_or_else(nre)?;
                if !loaded {
                    CommandManager::send_error_text(
                        self,
                        out,
                        &format!("Failed to load path file {}", args[1]),
                    );
                    return Ok(());
                }

                let points = run_now(server, id, |c| {
                    c.go_to_follow_path();
                    c.ai.path_handler.ai_path_points.len()
                })
                .ok_or_else(nre)?;

                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("Loaded path file {}, containing {points} points", args[1]),
                );
            }
            "qs" | "queue_skill" => {
                let skill_id = if args.len() <= 1 {
                    None
                } else {
                    try_parse::<u32>(&args[1])
                };
                let Some(skill_id) = skill_id else {
                    CommandManager::send_error_text(self, out, "No skill Id provided to add");
                    return Ok(());
                };

                run_now(server, id, move |c| {
                    c.enqueue_ai_commands(
                        &[AiCommands {
                            id: 0,
                            cmd_set_id: 0,
                            cmd_id: AiCommandCategory::UseSkill,
                            param1: skill_id,
                            param2: String::new(),
                        }],
                        false,
                    )
                })
                .ok_or_else(nre)?;
            }
            "follow_npc" => {
                let npc_id = if args.len() <= 1 {
                    None
                } else {
                    try_parse::<u32>(&args[1])
                };
                let Some(npc_id) = npc_id else {
                    CommandManager::send_error_text(
                        self,
                        out,
                        "No NPC TemplateId provided to follow",
                    );
                    return Ok(());
                };

                let (found, follow) = run_now(server, id, move |c| {
                    let found = c.do_follow_nearest_npc(npc_id, 100.0);
                    (found, c.ai.follow_unit.clone())
                })
                .ok_or_else(nre)?;
                // C#: the two messages are swapped (success reports "Could not find").
                if !found {
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!(
                            "{id} is now following {} @NPC_NAME({npc_id}) ({npc_id}) to follow.",
                            follow.as_ref().map_or("nothing", dotnet_type_name)
                        ),
                    );
                } else {
                    CommandManager::send_error_text(
                        self,
                        out,
                        &format!("Could not find a @NPC_NAME({npc_id}) ({npc_id}) to follow."),
                    );
                }
            }
            "clear_path_cache" | "clear_cache" => {
                server.systems.ai.paths.clear_cache();
                CommandManager::send_normal_text(self, out, "Cleared AI Path cache.");
            }
            "stance" | "set_stance" => {
                if args.len() <= 1 {
                    CommandManager::send_error_text(self, out, "No stance provided");
                    return Ok(());
                }

                let Some(new_stance) = enum_try_parse(&args[1], &GAME_STANCE_NAMES, 0, 255) else {
                    CommandManager::send_error_text(self, out, "Not a valid stance");
                    return Ok(());
                };

                npc.data().current_game_stance = GameStanceType(new_stance as u8);
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "{id}: @NPC_NAME({template_id}) ({template_id}) is now using stance {}",
                        enum_name(new_stance, &GAME_STANCE_NAMES)
                    ),
                );
            }
            "set_anim" | "anim" => {
                if args.len() <= 1 {
                    CommandManager::send_error_text(self, out, "No AnimActionId provided");
                    return Ok(());
                }

                let Some(new_anim_val) = try_parse::<i32>(&args[1]) else {
                    CommandManager::send_error_text(self, out, "Not a valid AnimActionId");
                    return Ok(());
                };

                // Math.Abs(int.MinValue) throws
                let Some(abs) = new_anim_val.checked_abs() else {
                    anyhow::bail!(
                        "Negating the minimum value of a twos complement number is invalid."
                    );
                };
                let new_anim = abs as u32;
                let enable_anim = new_anim_val > 0;

                let packet = {
                    let n = npc.data();
                    SCUnitModelPostureChangedPacket {
                        obj_id: id,
                        posture: crate::models::unit::UnitData::model_posture_type(&*n),
                        has_loot_left: n.unit.has_loot_left,
                        anim_action_id: new_anim,
                        activate_animation: enable_anim,
                    }
                };
                crate::systems::skills::broadcast(server, &WorldObject::from(&npc), &packet, false);
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "{id}: @NPC_NAME({template_id}) ({template_id}) is {} using AnimActionId {new_anim}",
                        if enable_anim { "now" } else { "no longer" }
                    ),
                );
            }
            _ => {
                CommandManager::send_error_text(self, out, "No valid action provided");
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<action> [[options]]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Various AI related actions, allowed actions: list, info, set_behavior, load_path, queue_skill, follow_npc, clear_path_cache, set_stance, set_anim".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::models::movement::GameStanceType;
    use crate::models::npc::Npc;
    use crate::opcodes::sc;
    use crate::systems::ai::framework::{AiKind, BehaviorKind};
    use crate::systems::ai::path_handler::{AiPathPoint, AiPathPointAction};
    use crate::systems::skills::controllers::Vec3;
    use crate::world::{Entity, NpcRef};

    fn spawn(server: &crate::server::GameServer, obj_id: u32, template: u32, x: f32) -> NpcRef {
        let mut n = Npc::new(template);
        n.unit.hp = 100;
        n.unit.max_hp = 100;
        n.unit.transform.set_xyz(x, 200.0, 100.0);
        let npc = Entity::new(obj_id, n);
        server
            .world
            .world(0)
            .unwrap()
            .spawn(&npc, &server.view_ctx());
        npc
    }

    #[tokio::test]
    async fn test_ai_actions() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 200.0);
        let (handled, out) = harness::run(&server, &gm.p, "testai list").await;
        assert!(handled);
        assert_eq!(
            out,
            [harness::error("testai", "You don't have anything selected")]
        );
        let other = harness::pc(&server, 2, "Bob", 120.0, 200.0);
        gm.p.data().unit.current_target = other.p.obj_id;
        let (_, out) = harness::run(&server, &gm.p, "ai list").await;
        assert_eq!(
            out,
            [harness::error("testai", "You don't have a NPC selected")]
        );

        let plain = spawn(&server, 0x900, 50, 110.0);
        gm.p.data().unit.current_target = plain.obj_id;
        let (_, out) = harness::run(&server, &gm.p, "ai list").await;
        assert_eq!(out, [harness::error("testai", "Target has no AI attached")]);
        let (_, out) = harness::run(&server, &gm.p, "ai").await;
        assert!(out[0].starts_with("Help for |cFFFFFFFF/testai|r"));

        let npc = spawn(&server, 0x901, 51, 112.0);
        server.systems.ai.ais.add_ai(&npc, AiKind::HoldPosition);
        gm.p.data().unit.current_target = npc.obj_id;

        let (_, out) = harness::run(&server, &gm.p, "ai list").await;
        assert_eq!(out.len(), 13);
        assert_eq!(
            out[0],
            harness::normal("testai", "Available behaviors for 2305")
        );
        assert_eq!(
            out[1],
            harness::normal(
                "testai",
                "Spawning -> .AI.v2.Behaviors.Common.SpawningBehavior"
            )
        );
        assert_eq!(
            out[12],
            harness::normal("testai", "Idle -> .AI.v2.Behaviors.Common.IdleBehavior")
        );

        // No current behavior yet: GetCurrentBehavior().ToString() throws
        gm.chat();
        let (_, out) = harness::run(&server, &gm.p, "ai info").await;
        assert!(out.is_empty());
        assert_eq!(
            gm.chat(),
            ["|cFFFF0000Object reference not set to an instance of an object.|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "ai set Talk").await;
        assert_eq!(out, [harness::normal("testai", "Target AI set to Talk")]);
        assert_eq!(
            server
                .systems
                .ai
                .ais
                .inspect(npc.obj_id, |a| a.current_behavior()),
            Some(Some(BehaviorKind::Talk))
        );
        let (_, out) = harness::run(&server, &gm.p, "ai set_behavior 2").await;
        assert_eq!(
            out,
            [harness::error(
                "testai",
                "Unsupported behavior Attack for /testai"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "ai set 99").await;
        assert_eq!(
            out,
            [harness::error(
                "testai",
                "Unsupported behavior 99 for /testai"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "ai set talk").await;
        assert_eq!(
            out,
            [harness::error("testai", "Not a valid behavior value set")]
        );

        let (_, out) = harness::run(&server, &gm.p, "ai info").await;
        assert_eq!(
            out,
            [
                harness::normal(
                    "testai",
                    "Using AI: HoldPosition, CurrentBehavior: v2.Behaviors.Common.TalkBehavior"
                ),
                harness::normal("testai", "AI Path has 0 points (0 remaining in queue)"),
                harness::normal("testai", "AI Commands has 0 actions in queue"),
                harness::normal("testai", "AI Commands has 0 actions in queue"),
            ]
        );

        // load_path from the cache
        server.systems.ai.paths.insert(
            "route",
            vec![
                AiPathPoint {
                    position: Vec3::new(110.0, 200.0, 100.0),
                    action: AiPathPointAction::None,
                    param: String::new(),
                };
                3
            ],
        );
        let (_, out) = harness::run(&server, &gm.p, "ai load_path route").await;
        assert_eq!(
            out,
            [harness::normal(
                "testai",
                "Loaded path file route, containing 3 points"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "ai load_path missing").await;
        assert_eq!(
            out,
            [harness::error("testai", "Failed to load path file missing")]
        );
        let (_, out) = harness::run(&server, &gm.p, "ai clear_cache").await;
        assert_eq!(out, [harness::normal("testai", "Cleared AI Path cache.")]);

        // queue_skill
        let (_, out) = harness::run(&server, &gm.p, "ai qs 123").await;
        assert!(out.is_empty());
        let (_, out) = harness::run(&server, &gm.p, "ai qs").await;
        assert_eq!(
            out,
            [harness::error("testai", "No skill Id provided to add")]
        );

        // follow_npc (C# swaps the messages)
        let (_, out) = harness::run(&server, &gm.p, "ai follow_npc 50").await;
        assert_eq!(
            out,
            [harness::error(
                "testai",
                "Could not find a @NPC_NAME(50) (50) to follow."
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "ai follow_npc 77").await;
        assert_eq!(
            out,
            [harness::normal(
                "testai",
                "2305 is now following AAEmu.Game.Models.Game.NPChar.Npc @NPC_NAME(77) (77) to follow."
            )]
        );

        // stance
        let (_, out) = harness::run(&server, &gm.p, "ai stance Fly").await;
        assert_eq!(
            out,
            [harness::normal(
                "testai",
                "2305: @NPC_NAME(51) (51) is now using stance Fly"
            )]
        );
        assert_eq!(npc.data().current_game_stance, GameStanceType::FLY);
        let (_, out) = harness::run(&server, &gm.p, "ai stance nope").await;
        assert_eq!(out, [harness::error("testai", "Not a valid stance")]);

        // anim: broadcast to the players around
        gm.packets();
        let (_, out) = harness::run(&server, &gm.p, "ai anim -42").await;
        assert_eq!(
            out,
            [harness::normal(
                "testai",
                "2305: @NPC_NAME(51) (51) is no longer using AnimActionId 42"
            )]
        );
        let frames = gm.packets();
        assert!(frames
            .iter()
            .any(|(op, _)| *op == sc::SCUnitModelPostureChangedPacket));
        let (_, out) = harness::run(&server, &gm.p, "ai anim x").await;
        assert_eq!(out, [harness::error("testai", "Not a valid AnimActionId")]);
        gm.chat();
        let (_, out) = harness::run(&server, &gm.p, "ai anim -2147483648").await;
        assert!(out.is_empty());
        assert_eq!(
            gm.chat(),
            ["|cFFFF0000Negating the minimum value of a twos complement number is invalid.|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "ai dance").await;
        assert_eq!(out, [harness::error("testai", "No valid action provided")]);
    }
}
