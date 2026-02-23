using System.Collections.Concurrent;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class AddFloor : ICommand
{
    public string[] CommandNames { get; set; } = ["addfloor"];

    /// <summary>
    /// Tracks in-progress floor mapping sessions per character.
    /// </summary>
    private static readonly ConcurrentDictionary<uint, FloorMappingSession> Sessions = new();

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<name> | cancel";
    }

    public string GetCommandHelpText()
    {
        return "Maps a building floor zone using 4 corner points. " +
               "Usage: /addfloor <name> to start, then walk to each corner and type /addfloor to mark it. " +
               "The Z height of the first point becomes the floor height. " +
               "Use /addfloor cancel to abort.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        // Cancel
        if (args.Length >= 1 && args[0].Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            if (Sessions.TryRemove(character.ObjId, out _))
            {
                CommandManager.SendNormalText(this, messageOutput,
                    "|cFFFFFF00Floor mapping cancelled.|r");
            }
            else
            {
                CommandManager.SendNormalText(this, messageOutput,
                    "No active floor mapping session.");
            }

            return;
        }

        var pos = character.Transform.Local.Position;
        var zoneId = character.Transform.ZoneId;

        // If no active session, start one
        if (!Sessions.TryGetValue(character.ObjId, out var session))
        {
            if (args.Length < 1)
            {
                CommandManager.SendNormalText(this, messageOutput,
                    "|cFFFF0000Usage: /addfloor <name> to start mapping a new floor zone.|r");
                return;
            }

            session = new FloorMappingSession
            {
                Name = args[0],
                ZoneId = zoneId,
                FloorZ = pos.Z
            };
            session.Points.Add(new Vector2(pos.X, pos.Y));
            Sessions[character.ObjId] = session;

            CommandManager.SendNormalText(this, messageOutput,
                $"|cFF00FF00Point 1/4 marked|r at X={pos.X:F1} Y={pos.Y:F1} (FloorZ={pos.Z:F2})");
            CommandManager.SendNormalText(this, messageOutput,
                "Walk to the next corner and type |cFFFFFF00/addfloor|r to mark it.");
            return;
        }

        // Add next point
        session.Points.Add(new Vector2(pos.X, pos.Y));
        var pointNum = session.Points.Count;

        CommandManager.SendNormalText(this, messageOutput,
            $"|cFF00FF00Point {pointNum}/4 marked|r at X={pos.X:F1} Y={pos.Y:F1}");

        // Not done yet
        if (pointNum < 4)
        {
            CommandManager.SendNormalText(this, messageOutput,
                $"Walk to corner {pointNum + 1} and type |cFFFFFF00/addfloor|r.");
            return;
        }

        // All 4 points collected — save the zone
        Sessions.TryRemove(character.ObjId, out _);

        var world = WorldManager.Instance.GetWorldTemplateByZoneKey(session.ZoneId);
        if (world?.BuildingFloors == null)
        {
            CommandManager.SendNormalText(this, messageOutput,
                "|cFFFF0000BuildingFloorManager not available for this world.|r");
            return;
        }

        var zone = world.BuildingFloors.AddZone(
            session.Name,
            [.. session.Points],
            session.FloorZ);

        CommandManager.SendNormalText(this, messageOutput,
            $"|cFF00FF00Floor zone '{session.Name}' saved!|r FloorZ={session.FloorZ:F2}");
        for (var i = 0; i < zone.Points.Count; i++)
        {
            var p = zone.Points[i];
            CommandManager.SendNormalText(this, messageOutput,
                $"  Corner {i + 1}: X={p.X:F1} Y={p.Y:F1}");
        }

        CommandManager.SendNormalText(this, messageOutput,
            $"Total zones in world: {world.BuildingFloors.Count}");
    }

    private class FloorMappingSession
    {
        public string Name { get; set; }
        public uint ZoneId { get; set; }
        public float FloorZ { get; set; }
        public List<Vector2> Points { get; } = [];
    }
}
