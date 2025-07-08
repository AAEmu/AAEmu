using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
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
        if (character.CurrentTarget == null)
        {
            messageOutput.SendMessage("You need to have a target selected.");
            return;
        }
        var world = character.ParentWorld;
        var (idx, pos) = world.Template.GeoData.FindСlosestToTheCurrent(character.Transform.ZoneId, character.CurrentTarget.Transform.World.Position);
        messageOutput.SendMessage($"Closest to {character.CurrentTarget.Transform.World.Position} -> Idx: {idx}, Pos: {pos}");
    }
}
