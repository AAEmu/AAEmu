using System;
using System.IO;
using System.Net.Mime;
using System.Reflection;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestAI : ICommand
{
    public void OnLoad()
    {
        string[] name = { "testai", "ai" };
        CommandManager.Instance.Register(name, this);
    }

    public string GetCommandLineHelp()
    {
        return "<behavior>";
    }

    public string GetCommandHelpText()
    {
        return "Forces the AI toa given state";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (character.CurrentTarget == null)
        {
            character.SendMessage("[AI] You don't have anything selected");
            return;
        }

        if (character.CurrentTarget is not Npc npc)
        {
            character.SendMessage("[AI] You don't have a NPC selected");
            return;
        }

        if (args.Length <= 0)
        {
            character.SendMessage($"[AI] No action provided, allowed actions");
            character.SendMessage($"[AI] set, list, info, queue_skill, follow_npc, clear_path_cache, set_stance, anim");
            return;
        }

        if (npc.Ai == null)
        {
            character.SendMessage($"[AI] Target has no AI attached");
            return;
        }
        
        var action = args[0].ToLower();

        switch (action)
        {
            case "set":
                if (args.Length <= 1 || !Enum.TryParse<BehaviorKind>(args[1], out var newBehavior))
                {
                    character.SendMessage($"[AI] Not a valid behavior value {args[0]}");
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
                        character.SendMessage($"[AI] unsupported behavior {newBehavior} for /testai");
                        return;
                }

                character.SendMessage($"[AI] Target AI set to {newBehavior}");
                break;
            case "list":
                character.SendMessage($"[AI] Available behaviors for {npc.ObjId}");
                var aiBehaviorList = npc.Ai.GetAiBehaviorList();
                foreach (var (behaviorKind, behavior) in aiBehaviorList)
                {
                    character.SendMessage($"[AI] {behaviorKind} -> {behavior.ToString()?.Replace("AAEmu.Game.Models.Game","")}");
                }
                break;
            case "info":
                character.SendMessage($"[AI] Using AI: {npc.Ai.GetType().Name.Replace("AiCharacter", "")}, CurrentBehavior: {npc.Ai.GetCurrentBehavior().ToString()?.Replace("AAEmu.Game.Models.Game.AI.","")}");
                character.SendMessage($"[AI] AI Path has {npc.Ai.PathHandler.AiPathPoints.Count} points ({npc.Ai.PathHandler.AiPathPointsRemaining.Count} remaining in queue)");
                character.SendMessage($"[AI] AI Commands has {npc.Ai.AiCommandsQueue.Count} actions in queue");
                if (npc.Spawner != null)
                {
                    character.SendMessage($"[AI] SpawnerId {npc.Spawner.Id}, SpawnerTemplateId {npc.Spawner.Template.Id} @ {npc.Spawner.Position}");
                    if (npc.Spawner.FollowNpc > 0)
                        character.SendMessage($"[AI] Spawner set to follow @NPC_NAME({npc.Spawner.FollowNpc}) ({npc.Spawner.FollowNpc})");
                }

                if (npc.Ai.AiFollowUnitObj is Npc followingNpc)
                    character.SendMessage($"[AI] Currently following ObjId {followingNpc.ObjId} @NPC_NAME({followingNpc.TemplateId}) ({followingNpc.TemplateId})");
                character.SendMessage($"[AI] AI Commands has {npc.Ai.AiCommandsQueue.Count} actions in queue");
                break;
            case "load_path":
                if (args.Length <= 1)
                {
                    character.SendMessage($"[AI] No path file provided");
                    return;
                }

                if (!npc.Ai.LoadAiPathPoints(args[1], false))
                {
                    character.SendMessage($"[AI] Failed to load path file {args[1]}");
                    return;
                }
                npc.Ai.GoToFollowPath();
                
                character.SendMessage($"[AI] Loaded path file {args[1]}, containing {npc.Ai.PathHandler.AiPathPoints.Count} points");
                break;
            case "qs":
            case "queue_skill":
                if (args.Length <= 1 || !uint.TryParse(args[1], out var skillId))
                {
                    character.SendMessage($"[AI] No skill Id provided to add");
                    return;
                }
                npc.Ai.EnqueueAiCommands(new []{ new AiCommands() { CmdId = AiCommandCategory.UseSkill, Param1 = skillId }});
                break;
            case "follow_npc":
                if (args.Length <= 1 || !uint.TryParse(args[1], out var npcId))
                {
                    character.SendMessage($"[AI] No NPC TemplateId provided to follow");
                    return;
                }

                if (!npc.Ai.DoFollowNearestNpc(npcId, 100f))
                {
                    character.SendMessage($"[AI] {npc.ObjId} is now following {npc.Ai.AiFollowUnitObj?.ToString() ?? "nothing"} @NPC_NAME({npcId}) ({npcId}) to follow.");
                }
                else
                {
                    character.SendMessage($"[AI] Could not find a @NPC_NAME({npcId}) ({npcId}) to follow.");
                }
                break;
            case "clear_path_cache":
            case "clear_cache":
                AiPathsManager.Instance.ClearCache();
                character.SendMessage($"[AI] Cleared AI Path cache.");
                break;
            case "stance":
            case "set_stance":
                if (args.Length <= 1)
                {
                    character.SendMessage($"[AI] No stance provided");
                    return;
                }

                if (!Enum.TryParse<GameStanceType>(args[1], out var newStance))
                {
                    character.SendMessage($"[AI] Not a valid stance");
                    return;
                }

                npc.CurrentGameStance = newStance;
                character.SendMessage($"[AI] {npc.ObjId}: @NPC_NAME({npc.TemplateId}) ({npc.TemplateId}) is now using stance {newStance}");
                break;
            case "set_anim":
            case "anim":
                if (args.Length <= 1)
                {
                    character.SendMessage($"[AI] No AnimActionId provided");
                    return;
                }

                if (!int.TryParse(args[1], out var newAnimVal))
                {
                    character.SendMessage($"[AI] Not a valid AnimActionId");
                    return;
                }

                var newAnim = (uint)Math.Abs(newAnimVal);
                var enableAnim = newAnimVal > 0;

                npc.BroadcastPacket(new SCUnitModelPostureChangedPacket(npc, newAnim, enableAnim), false);
                character.SendMessage($"[AI] {npc.ObjId}: @NPC_NAME({npc.TemplateId}) ({npc.TemplateId}) is {(enableAnim ? "now" : "no longer")} using AnimActionId {newAnim}");
                break;
            default:
                character.SendMessage($"[AI] No valid action provided");
                break;
        }
    }
}
