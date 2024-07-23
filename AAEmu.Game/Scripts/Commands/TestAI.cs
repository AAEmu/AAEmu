using System;
using System.IO;
using System.Net.Mime;
using System.Reflection;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.AI.v2.Params;
using AAEmu.Game.Models.Game.Char;
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
            character.SendMessage($"[AI] set, list, info, queue_skill");
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
                character.SendMessage($"[AI] AI Path has {npc.Ai.AiPathPoints.Count} points ({npc.Ai.AiPathPointsRemaining.Count} remaining in queue)");
                character.SendMessage($"[AI] AI Commands has {npc.Ai.AiCommandsQueue.Count} actions in queue");
                if (npc.Spawner?.FollowNpc > 0)
                    character.SendMessage($"[AI] Spawner set to follow @NPC_NAME({npc.Spawner.FollowNpc}) ({npc.Spawner.FollowNpc})");
                if (npc.Ai.AiFollowUnitObj is Npc followingNpc)
                    character.SendMessage($"[AI] Currently following ObjId {followingNpc.ObjId} @NPC_NAME({followingNpc.TemplateId}) ({followingNpc.TemplateId})");
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
                
                character.SendMessage($"[AI] Loaded path file {args[1]}, containing {npc.Ai.AiPathPoints.Count} points");
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
            default:
                character.SendMessage($"[AI] No valid action provided");
                break;
        }
    }
}
