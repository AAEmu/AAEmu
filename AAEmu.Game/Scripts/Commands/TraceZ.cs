using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TraceZ : ICommand
{
    public string[] CommandNames { get; set; } = ["tracez", "tz"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[target NPC]";
    }

    public string GetCommandHelpText()
    {
        return "Toggles Z-trace logging on the targeted NPC. " +
               "When enabled, every Z modification (gravity, movement, behavior change) " +
               "is logged to the server console with [TraceZ] prefix, including which " +
               "height source (NavMesh, GeoData, HeightMap, BuildingFloor) provided the value. " +
               "Use this to diagnose NPC flying/clipping issues.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (character.CurrentTarget is not Npc npc)
        {
            CommandManager.SendErrorText(this, messageOutput, "Target an NPC first.");
            return;
        }

        npc.TraceZ = !npc.TraceZ;

        var state = npc.TraceZ ? "|cFF00FF00ON|r" : "|cFFFF0000OFF|r";
        var pos = npc.Transform.Local.Position;
        var behaviorName = npc.Ai?.GetCurrentBehavior()?.GetType().Name ?? "none";

        CommandManager.SendNormalText(this, messageOutput,
            $"TraceZ {state} for NPC {npc.TemplateId} (ObjId:{npc.ObjId})");
        CommandManager.SendNormalText(this, messageOutput,
            $"  Current Z={pos.Z:F2} at ({pos.X:F1},{pos.Y:F1})");
        CommandManager.SendNormalText(this, messageOutput,
            $"  Spawner Z={npc.Spawner?.Position.Z:F2}");
        CommandManager.SendNormalText(this, messageOutput,
            $"  IdlePos Z={npc.Ai?.IdlePosition.Z:F2}");
        CommandManager.SendNormalText(this, messageOutput,
            $"  Behavior: {behaviorName}");
        CommandManager.SendNormalText(this, messageOutput,
            $"  IsInBattle: {npc.IsInBattle}");

        if (npc.TraceZ)
        {
            CommandManager.SendNormalText(this, messageOutput,
                "Watch server console for [TraceZ] logs. Attack this NPC to see Z changes.");
        }
    }
}
