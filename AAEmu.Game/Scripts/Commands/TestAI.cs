using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.AI.v2.Framework;
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
            character.SendMessage("[AI] No new target behavior set");

            if (npc.Ai == null)
                return;

            var aiBehaviorList = npc.Ai.GetAiBehaviorList();
            foreach (var (behaviorKind, behavior) in aiBehaviorList)
            {
                character.SendMessage($"[AI] {behaviorKind} -> {behavior.ToString()?.Replace("AAEmu.Game.Models.Game","")}");
            }
            
            return;
        }

        if (!Enum.TryParse<BehaviorKind>(args[0], out var newBehavior))
        {
            character.SendMessage($"[AI] Not a valid behavior value {args[0]}");
            return;
        }

        if (npc.Ai == null)
        {
            character.SendMessage($"[AI] installing default behavior for this NPC as it did not have a AI yet");
            npc.Patrol = null;
            npc.Ai = new AlmightyNpcAiCharacter() { Owner = npc, IdlePosition = npc.Transform.CloneDetached() };
            AIManager.Instance.AddAi(npc.Ai);
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
            default:
                character.SendMessage($"[AI] unsupported behavior {newBehavior}");
                return;
        }
        character.SendMessage($"[AI] Target AI set to {newBehavior}");
    }
}
