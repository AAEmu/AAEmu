using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class ShowFloor : ICommand
{
    public string[] CommandNames { get; set; } = ["showfloor", "sf"];

    private static readonly List<BaseUnit> Markers = [];

    private const uint StonePostDoodad = 5622u;      // Custom floor zone corners
    private const uint CrescentFlagDoodad = 4763u;   // BAI NavModifier corners
    private const uint CombatFlagDoodad = 5014u;     // BAI type-4 interior nodes

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[radius=50] | clear";
    }

    public string GetCommandHelpText()
    {
        return "Visualizes building floor zones with doodad markers. " +
               "Stone posts = custom floor zone corners, " +
               "Crescent flags = BAI NavigationModifier corners, " +
               "Combat flags = BAI type-4 interior nodes. " +
               "Use /showfloor clear to remove markers.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        // Clear markers
        if (args.Length >= 1 && args[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            ClearMarkers();
            CommandManager.SendNormalText(this, messageOutput,
                "|cFF00FF00All floor markers cleared.|r");
            return;
        }

        var radius = 50f;
        if (args.Length >= 1 && float.TryParse(args[0], out var r))
            radius = r;

        ClearMarkers();

        var pos = character.Transform.Local.Position;
        var zoneId = character.Transform.ZoneId;
        var world = WorldManager.Instance.GetWorldTemplateByZoneKey(zoneId);
        var worldInstance = character.ParentWorld;

        if (world == null)
        {
            CommandManager.SendNormalText(this, messageOutput,
                "|cFFFF0000World not found.|r");
            return;
        }

        var customCount = 0;
        var navModCount = 0;
        var type4Count = 0;

        // 1. Custom floor zones — stone posts at corners
        if (world.BuildingFloors != null)
        {
            var zones = world.BuildingFloors.GetNearbyZones(pos.X, pos.Y, radius);
            foreach (var zone in zones)
            {
                foreach (var p in zone.Points)
                {
                    AddMarker(worldInstance, new Vector3(p.X, p.Y, zone.FloorZ), StonePostDoodad);
                    customCount++;
                }

                // Also place markers along the edges for better visibility
                for (var i = 0; i < zone.Points.Count; i++)
                {
                    var p1 = zone.Points[i];
                    var p2 = zone.Points[(i + 1) % zone.Points.Count];
                    var midX = (p1.X + p2.X) / 2f;
                    var midY = (p1.Y + p2.Y) / 2f;
                    AddMarker(worldInstance, new Vector3(midX, midY, zone.FloorZ), StonePostDoodad);
                    customCount++;
                }
            }
        }

        // 2. BAI NavigationModifiers with BuildingId — crescent flags at polygon corners
        var bai = world.GetBaiByPos(pos);
        if (bai != null)
        {
            var radiusSq = radius * radius;

            foreach (var areaMission in bai.AreasMissionReaders)
            {
                foreach (var area in areaMission.NavigationModifiers)
                {
                    if (area.BuildingId <= 0)
                        continue;

                    // Check if any point is within radius
                    var near = false;
                    foreach (var p in area.Points)
                    {
                        var dx = p.X - pos.X;
                        var dy = p.Y - pos.Y;
                        if (dx * dx + dy * dy <= radiusSq)
                        {
                            near = true;
                            break;
                        }
                    }

                    if (!near)
                        continue;

                    foreach (var p in area.Points)
                    {
                        AddMarker(worldInstance, new Vector3(p.X, p.Y, (float)area.Height), CrescentFlagDoodad);
                        navModCount++;
                    }
                }
            }

            // 3. Type-4 netmission nodes — combat flags
            foreach (var netMission in bai.NetMissionReaders)
            {
                foreach (var (_, node) in netMission.NodeDescriptorList)
                {
                    if (node.Type != 4)
                        continue;

                    var dx = node.Pos.X - pos.X;
                    var dy = node.Pos.Y - pos.Y;
                    if (dx * dx + dy * dy > radiusSq)
                        continue;

                    AddMarker(worldInstance, node.Pos, CombatFlagDoodad);
                    type4Count++;
                }
            }
        }

        var total = customCount + navModCount + type4Count;
        CommandManager.SendNormalText(this, messageOutput,
            $"|cFF00FFFF=== ShowFloor: {total} markers placed (radius {radius:F0}u) ===|r");
        CommandManager.SendNormalText(this, messageOutput,
            $"  |cFFFFFF00Stone posts|r: {customCount} (custom floor zone corners)");
        CommandManager.SendNormalText(this, messageOutput,
            $"  |cFFFFFF00Crescent flags|r: {navModCount} (BAI NavModifier corners)");
        CommandManager.SendNormalText(this, messageOutput,
            $"  |cFFFFFF00Combat flags|r: {type4Count} (BAI type-4 interior nodes)");

        if (total == 0)
        {
            CommandManager.SendNormalText(this, messageOutput,
                "|cFFFFFF00No floor data found nearby. Use /addfloor to map a zone.|r");
        }

        CommandManager.SendNormalText(this, messageOutput,
            "Use |cFFFFFF00/showfloor clear|r to remove markers.");
    }

    private static void AddMarker(WorldInstance world, Vector3 pos, uint doodadTemplateId)
    {
        var markerDoodad = DoodadManager.Instance.Create(world, 0, doodadTemplateId);
        markerDoodad.Transform.Local.SetPosition(pos);
        markerDoodad.Show();
        Markers.Add(markerDoodad);
    }

    private static void ClearMarkers()
    {
        foreach (var marker in Markers)
        {
            ObjectIdManager.Instance.ReleaseId(marker.ObjId);
            marker.Delete();
        }

        Markers.Clear();
    }
}
