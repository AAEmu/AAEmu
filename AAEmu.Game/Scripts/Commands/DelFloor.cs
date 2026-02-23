using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class DelFloor : ICommand
{
    public string[] CommandNames { get; set; } = ["delfloor"];

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
        return "Removes the closest building floor zone to your position (within 50 units).";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var pos = character.Transform.Local.Position;
        var zoneId = character.Transform.ZoneId;

        var world = WorldManager.Instance.GetWorldTemplateByZoneKey(zoneId);
        if (world?.BuildingFloors == null)
        {
            CommandManager.SendNormalText(this, messageOutput,
                "|cFFFF0000BuildingFloorManager not available for this world.|r");
            return;
        }

        var removed = world.BuildingFloors.RemoveClosest(pos.X, pos.Y);
        if (removed != null)
        {
            CommandManager.SendNormalText(this, messageOutput,
                $"|cFF00FF00Removed floor zone '{removed.Name}'|r (FloorZ={removed.FloorZ:F2})");
            CommandManager.SendNormalText(this, messageOutput,
                $"  Remaining zones: {world.BuildingFloors.Count}");
        }
        else
        {
            CommandManager.SendNormalText(this, messageOutput,
                "|cFFFFFF00No floor zones found within 50 units.|r");
        }
    }
}
