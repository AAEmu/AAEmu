using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestAI : ICommand
{
    public string[] CommandNames { get; set; } = ["testai", "ai"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<action> [[options]]";
    }

    public string GetCommandHelpText()
    {
        return
            "Various AI related actions, allowed actions: list, info, set_behavior, load_path, queue_skill, follow_npc, clear_path_cache, set_stance, set_anim";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (character.CurrentTarget == null)
        {
            CommandManager.SendErrorText(this, messageOutput, "You don't have anything selected");
            return;
        }

        if (character.CurrentTarget is not Npc npc)
        {
            CommandManager.SendErrorText(this, messageOutput, "You don't have a NPC selected");
            return;
        }

        if (args.Length <= 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (npc.Ai == null)
        {
            CommandManager.SendErrorText(this, messageOutput, "Target has no AI attached");
            return;
        }

        var action = args[0].ToLower();

        switch (action)
        {
            case "set_behavior":
            case "set":
                if (args.Length <= 1 || !Enum.TryParse<BehaviorKind>(args[1], out var newBehavior))
                {
                    CommandManager.SendErrorText(this, messageOutput, $"Not a valid behavior value {args[0]}");
                    return;
                }

                switch (newBehavior)
                {
                    case BehaviorKind.Alert:
                        npc.Ai.GoToAlert();
                        break;
                    case BehaviorKind.Despawning:
                        npc.Ai.GoToDespawn();
                        break;
                    case BehaviorKind.Idle:
                        npc.Ai.GoToIdle();
                        break;
                    case BehaviorKind.ReturnState:
                        npc.Ai.GoToReturn();
                        break;
                    case BehaviorKind.RunCommandSet:
                        npc.Ai.GoToRunCommandSet();
                        break;
                    case BehaviorKind.Spawning:
                        npc.Ai.GoToSpawn();
                        break;
                    case BehaviorKind.Talk:
                        npc.Ai.GoToTalk();
                        break;
                    case BehaviorKind.Dummy:
                        npc.Ai.GoToDummy();
                        break;
                    case BehaviorKind.FollowPath:
                        npc.Ai.GoToFollowPath();
                        break;
                    case BehaviorKind.FollowUnit:
                        npc.Ai.GoToFollowUnit();
                        break;
                    default:
                        CommandManager.SendErrorText(this, messageOutput,
                            $"Unsupported behavior {newBehavior} for /testai");
                        return;
                }

                CommandManager.SendNormalText(this, messageOutput, $"Target AI set to {newBehavior}");
                break;
            case "list":
                CommandManager.SendNormalText(this, messageOutput, $"Available behaviors for {npc.ObjId}");
                var aiBehaviorList = npc.Ai.GetAiBehaviorList();
                foreach (var (behaviorKind, behavior) in aiBehaviorList)
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        $"{behaviorKind} -> {behavior.ToString()?.Replace("AAEmu.Game.Models.Game", "")}");
                }

                break;
            case "info":
                CommandManager.SendNormalText(this, messageOutput,
                    $"Using AI: {npc.Ai.GetType().Name.Replace("AiCharacter", "")}, CurrentBehavior: {npc.Ai.GetCurrentBehavior().ToString()?.Replace("AAEmu.Game.Models.Game.AI.", "")}");
                CommandManager.SendNormalText(this, messageOutput,
                    $"AI Path has {npc.Ai.PathHandler.AiPathPoints.Count} points ({npc.Ai.PathHandler.AiPathPointsRemaining.Count} remaining in queue)");
                CommandManager.SendNormalText(this, messageOutput,
                    $"AI Commands has {npc.Ai.AiCommandsQueue.Count} actions in queue");
                if (npc.Spawner != null)
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        $"SpawnerId {npc.Spawner.Id}, SpawnerTemplateId {npc.Spawner.Template.Id} @ {npc.Spawner.Position}");
                    if (npc.Spawner.FollowNpc > 0)
                    {
                        CommandManager.SendNormalText(this, messageOutput,
                            $"Spawner set to follow @NPC_NAME({npc.Spawner.FollowNpc}) ({npc.Spawner.FollowNpc})");
                    }
                }

                if (npc.Ai.AiFollowUnitObj is Npc followingNpc)
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        $"Currently following ObjId {followingNpc.ObjId} @NPC_NAME({followingNpc.TemplateId}) ({followingNpc.TemplateId})");
                }

                CommandManager.SendNormalText(this, messageOutput,
                    $"AI Commands has {npc.Ai.AiCommandsQueue.Count} actions in queue");
                break;
            case "load_path":
                if (args.Length <= 1)
                {
                    CommandManager.SendErrorText(this, messageOutput, $"No path file provided");
                    return;
                }

                if (!npc.Ai.LoadAiPathPoints(args[1], false))
                {
                    CommandManager.SendErrorText(this, messageOutput, $"Failed to load path file {args[1]}");
                    return;
                }

                npc.Ai.GoToFollowPath();

                CommandManager.SendNormalText(this, messageOutput,
                    $"Loaded path file {args[1]}, containing {npc.Ai.PathHandler.AiPathPoints.Count} points");
                break;
            case "qs":
            case "queue_skill":
                if (args.Length <= 1 || !uint.TryParse(args[1], out var skillId))
                {
                    CommandManager.SendErrorText(this, messageOutput, $"No skill Id provided to add");
                    return;
                }

                npc.Ai.EnqueueAiCommands(new[]
                {
                    new AiCommands() { CmdId = AiCommandCategory.UseSkill, Param1 = skillId }
                });
                break;
            case "follow_npc":
                if (args.Length <= 1 || !uint.TryParse(args[1], out var npcId))
                {
                    CommandManager.SendErrorText(this, messageOutput, $"No NPC TemplateId provided to follow");
                    return;
                }

                if (!npc.Ai.DoFollowNearestNpc(npcId, 100f))
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        $"{npc.ObjId} is now following {npc.Ai.AiFollowUnitObj?.ToString() ?? "nothing"} @NPC_NAME({npcId}) ({npcId}) to follow.");
                }
                else
                {
                    CommandManager.SendErrorText(this, messageOutput,
                        $"Could not find a @NPC_NAME({npcId}) ({npcId}) to follow.");
                }

                break;
            case "clear_path_cache":
            case "clear_cache":
                AiPathsManager.Instance.ClearCache();
                CommandManager.SendNormalText(this, messageOutput, $"Cleared AI Path cache.");
                break;
            case "stance":
            case "set_stance":
                if (args.Length <= 1)
                {
                    CommandManager.SendErrorText(this, messageOutput, $"No stance provided");
                    return;
                }

                if (!Enum.TryParse<GameStanceType>(args[1], out var newStance))
                {
                    CommandManager.SendErrorText(this, messageOutput, $"Not a valid stance");
                    return;
                }

                npc.CurrentGameStance = newStance;
                CommandManager.SendNormalText(this, messageOutput,
                    $"{npc.ObjId}: @NPC_NAME({npc.TemplateId}) ({npc.TemplateId}) is now using stance {newStance}");
                break;
            case "set_anim":
            case "anim":
                if (args.Length <= 1)
                {
                    CommandManager.SendErrorText(this, messageOutput, "No AnimActionId provided");
                    return;
                }

                if (!int.TryParse(args[1], out var newAnimVal))
                {
                    CommandManager.SendErrorText(this, messageOutput, "Not a valid AnimActionId");
                    return;
                }

                var newAnim = (uint)Math.Abs(newAnimVal);
                var enableAnim = newAnimVal > 0;

                npc.BroadcastPacket(new SCUnitModelPostureChangedPacket(npc, newAnim, enableAnim), false);
                CommandManager.SendNormalText(this, messageOutput,
                    $"{npc.ObjId}: @NPC_NAME({npc.TemplateId}) ({npc.TemplateId}) is {(enableAnim ? "now" : "no longer")} using AnimActionId {newAnim}");
                break;
            default:
                CommandManager.SendErrorText(this, messageOutput, "No valid action provided");
                break;
        }
    }
}
