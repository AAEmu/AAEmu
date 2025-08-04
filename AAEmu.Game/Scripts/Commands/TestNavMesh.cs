using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.AI.v2.Controls;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestNavMesh : ICommand
{
    public string[] CommandNames { get; set; } = ["testnavmesh", "test_navmesh"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "";
    }

    public string GetCommandHelpText()
    {
        return "Shows route to target";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (character.CurrentTarget is not Npc npc)
        {
            messageOutput.SendMessage("You need to have a target selected.");
            return;
        }
        var world = character.ParentWorld;
        var pos = world.Template.GeoData.FindСlosestToTheCurrent(npc.Transform.ZoneId, npc.Transform.World.Position);
        messageOutput.SendMessage($"Closest to {npc.Transform.World.Position} -> {pos}");
        var foundPath = npc.Ai.PathNode.FindPath(npc.ParentWorld, npc.Transform.World.Position, character.Transform.World.Position);
        npc.Ai.PathNode.FoundPath = foundPath;
        foreach (var v3 in foundPath)
        {
            messageOutput.SendMessage($"-> {v3}");
        }
    }
}
