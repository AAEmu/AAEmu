using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class ListFloor : ICommand
{
    public string[] CommandNames { get; set; } = ["listfloor"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[radius=100]";
    }

    public string GetCommandHelpText()
    {
        return "Lists building floor zones near your position.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var radius = 100f;
        if (args.Length >= 1 && float.TryParse(args[0], out var r))
            radius = r;

        var pos = character.Transform.Local.Position;
        var zoneId = character.Transform.ZoneId;

        var world = WorldManager.Instance.GetWorldTemplateByZoneKey(zoneId);
        if (world?.BuildingFloors == null)
        {
            CommandManager.SendNormalText(this, messageOutput,
                "|cFFFF0000BuildingFloorManager not available for this world.|r");
            return;
        }

        var zones = world.BuildingFloors.GetNearbyZones(pos.X, pos.Y, radius);

        CommandManager.SendNormalText(this, messageOutput,
            $"|cFF00FFFF=== Floor Zones within {radius:F0}u ({zones.Count} found) ===|r");

        foreach (var zone in zones)
        {
            var center = zone.GetCenter();
            var dx = pos.X - center.X;
            var dy = pos.Y - center.Y;
            var dist = MathF.Sqrt(dx * dx + dy * dy);

            CommandManager.SendNormalText(this, messageOutput,
                $"  |cFFFFFF00{zone.Name}|r FloorZ={zone.FloorZ:F2} dist={dist:F1}");
            for (var i = 0; i < zone.Points.Count; i++)
            {
                var p = zone.Points[i];
                CommandManager.SendNormalText(this, messageOutput,
                    $"    P{i + 1}: X={p.X:F1} Y={p.Y:F1}");
            }
        }

        CommandManager.SendNormalText(this, messageOutput,
            $"Total zones in world: {world.BuildingFloors.Count}");
    }
}
