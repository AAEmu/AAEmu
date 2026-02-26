using System.Numerics;
using System.Text.RegularExpressions;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.Scripts;

using NLog;

namespace AAEmu.Game.Scripts.Commands;

public class DebugZ : ICommand
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly Regex ColorTagRegex = new(@"\|c[0-9A-Fa-f]{8}|\|r", RegexOptions.Compiled);

    public string[] CommandNames { get; set; } = ["debugz", "dz"];

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
        return "Shows all height sources at your current position (or target's position). " +
               "Useful for debugging NPC floor clipping in buildings.";
    }

    /// <summary>
    /// Sends text to both in-game chat and server console (NLog), stripping color tags for the console.
    /// </summary>
    private void Log(IMessageOutput messageOutput, string text)
    {
        CommandManager.SendNormalText(this, messageOutput, text);
        Logger.Info("[DebugZ] " + ColorTagRegex.Replace(text, ""));
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var pos = character.Transform.Local.Position;
        var zoneId = character.Transform.ZoneId;
        var label = character.Name;

        // If targeting an NPC, use their position instead
        if (character.CurrentTarget is Npc npc)
        {
            var playerZ = character.Transform.Local.Position.Z;
            pos = npc.Transform.Local.Position;
            zoneId = npc.Transform.ZoneId;
            label = $"NPC {npc.TemplateId} (ObjId:{npc.ObjId})";

            Log(messageOutput, $"|cFF00FFFF=== DebugZ for {label} ===|r");
            Log(messageOutput, $"NPC pos: X={pos.X:F2} Y={pos.Y:F2} Z=|cFFFF0000{pos.Z:F2}|r");
            Log(messageOutput, $"Player pos Z: |cFF00FF00{playerZ:F2}|r (reference/correct)");
            var delta = pos.Z - playerZ;
            Log(messageOutput, $"|cFFFFFF00NPC Z delta from player: {delta:F2}|r" +
                (MathF.Abs(delta) > 1f ? $" |cFFFF0000(NPC is {(delta < 0 ? "BELOW" : "ABOVE")} player by {MathF.Abs(delta):F1})|r" : " (OK)"));

            if (npc.Spawner != null)
            {
                Log(messageOutput, $"Spawner Z: |cFFFFFF00{npc.Spawner.Position.Z:F2}|r");
            }
        }
        else
        {
            Log(messageOutput, $"|cFF00FFFF=== DebugZ for {label} (self) ===|r");
            Log(messageOutput, $"Current pos: X={pos.X:F2} Y={pos.Y:F2} Z=|cFFFF0000{pos.Z:F2}|r");
        }

        Log(messageOutput,
$"ZoneId: {zoneId}");

        // 1. WorldManager.GetHeight (the master resolver)
        var masterZ = WorldManager.Instance.GetHeight(zoneId, pos.X, pos.Y, pos.Z);
        Log(messageOutput,
$"WorldManager.GetHeight: |cFFFFFF00{masterZ:F2}|r (this is what NPCs use)");

        // 1b. Custom building floor zone
        var world = WorldManager.Instance.GetWorldTemplateByZoneKey(zoneId);
        if (world?.BuildingFloors != null)
        {
            var customZ = world.BuildingFloors.GetFloorHeight(pos.X, pos.Y, pos.Z);
            Log(messageOutput,
$"CustomFloorZone: |cFF00FF00{customZ:F2}|r" +
                (customZ > 0f ? " (HIT)" : " (miss)"));
        }

        // 1c. NavMesh height query
        var worldInstance = character.ParentWorld;
        if (worldInstance?.NavMesh != null)
        {
            var navMeshZ = worldInstance.NavMesh.GetHeight(pos.X, pos.Y, pos.Z);
            Log(messageOutput,
$"NavMesh: |cFF00FF00{navMeshZ:F2}|r" +
                (navMeshZ > 0f ? " (HIT)" : " (miss)") +
                $" (tiles={worldInstance.NavMesh.TileCount})");
        }

        // 2. Building floor from NavigationModifiers
        if (world?.GeoData != null)
        {
            var buildingZ = world.GeoData.GetBuildingFloorHeight(pos);
            Log(messageOutput,
$"BuildingFloor (NavModifiers): |cFF00FF00{buildingZ:F2}|r" +
                (buildingZ > 0f ? " (HIT)" : " (miss)"));

            // 3. Interior floor from type-4 nodes
            var interiorZ = world.GeoData.GetInteriorFloorHeight(pos);
            Log(messageOutput,
$"InteriorFloor (Type4 nodes): |cFF00FF00{interiorZ:F2}|r" +
                (interiorZ > 0f ? " (HIT)" : " (miss)"));

            // 4. Navmesh height (GeoData.GetHeight)
            var navmeshZ = world.GeoData.GetHeight(pos);
            Log(messageOutput,
$"Navmesh (GeoData): |cFF00FF00{navmeshZ:F2}|r");

            // 5. Count type-4 nodes nearby (wider search for diagnostics)
            CountNearbyType4Nodes(world, pos, messageOutput);

            // 6. Count NavigationModifiers nearby
            CountNearbyNavModifiers(world, pos, messageOutput);
        }
        else
        {
            Log(messageOutput,
"|cFFFF0000GeoData not loaded for this world|r");
        }

        // 7. HeightMap
        if (world != null)
        {
            try
            {
                var heightMapZ = world.GetHeight(pos.X, pos.Y);
                Log(messageOutput,
    $"HeightMap: |cFF00FF00{heightMapZ:F2}|r");
            }
            catch
            {
                Log(messageOutput,
    "HeightMap: |cFFFF0000error|r");
            }
        }

        // 8. BAI tile info
        if (world != null)
        {
            var pathX = (int)MathF.Floor(pos.X / 256f);
            var pathY = (int)MathF.Floor(pos.Y / 256f);
            var bai = world.GetBaiByPos(pos);
            Log(messageOutput,
$"BAI tile: {pathX:000}_{pathY:000} loaded={bai != null}");

            if (bai != null)
            {
                Log(messageOutput,
    $"  AreasMissions: {bai.AreasMissionReaders.Count}, NetMissions: {bai.NetMissionReaders.Count}");

                var totalNodes = 0;
                var type4Nodes = 0;
                foreach (var nm in bai.NetMissionReaders)
                {
                    totalNodes += nm.NodeDescriptorList.Count;
                    foreach (var (_, n) in nm.NodeDescriptorList)
                    {
                        if (n.Type == 4) type4Nodes++;
                    }
                }
                Log(messageOutput,
    $"  NetMission nodes: {totalNodes} total, {type4Nodes} type-4");

                var navModCount = 0;
                var navModWithBuilding = 0;
                foreach (var am in bai.AreasMissionReaders)
                {
                    navModCount += am.NavigationModifiers.Count;
                    foreach (var nm in am.NavigationModifiers)
                    {
                        if (nm.BuildingId > 0) navModWithBuilding++;
                    }
                }
                Log(messageOutput,
    $"  NavigationModifiers: {navModCount} total, {navModWithBuilding} with BuildingId");
            }
        }
    }

    private void CountNearbyType4Nodes(WorldTemplate world, Vector3 pos, IMessageOutput messageOutput)
    {
        var bai = world.GetBaiByPos(pos);
        if (bai == null) return;

        var count10 = 0;
        var count25 = 0;
        var closestDist = float.MaxValue;
        var closestZ = 0f;
        var heightMapZ = world.GetRawHeightMapHeight((int)MathF.Round(pos.X), (int)MathF.Round(pos.Y));

        foreach (var netMission in bai.NetMissionReaders)
        {
            foreach (var (_, node) in netMission.NodeDescriptorList)
            {
                if (node.Type != 4) continue;

                var dx = node.Pos.X - pos.X;
                var dy = node.Pos.Y - pos.Y;
                var dist2D = MathF.Sqrt(dx * dx + dy * dy);

                if (dist2D <= 10f) count10++;
                if (dist2D <= 25f) count25++;

                if (dist2D < closestDist)
                {
                    closestDist = dist2D;
                    closestZ = node.Pos.Z;
                }
            }
        }

        Log(messageOutput,
$"  Type4 within 10u: {count10}, within 25u: {count25}");

        if (closestDist < float.MaxValue)
        {
            Log(messageOutput,
$"  Closest type4: Z={closestZ:F2}, dist2D={closestDist:F1}, aboveHMap={closestZ - heightMapZ:F1}");
        }
        else
        {
            Log(messageOutput,
"  No type-4 nodes in this BAI tile");
        }

        Log(messageOutput,
$"  RawHeightMap at pos: {heightMapZ:F2}");
    }

    private void CountNearbyNavModifiers(WorldTemplate world, Vector3 pos, IMessageOutput messageOutput)
    {
        var bai = world.GetBaiByPos(pos);
        if (bai == null) return;

        var containingCount = 0;

        foreach (var areaMission in bai.AreasMissionReaders)
        {
            foreach (var area in areaMission.NavigationModifiers)
            {
                if (area.BuildingId <= 0) continue;

                // Simple bounding box pre-check
                var minX = float.MaxValue;
                var maxX = float.MinValue;
                var minY = float.MaxValue;
                var maxY = float.MinValue;
                foreach (var p in area.Points)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }

                var dist = MathF.Max(0, MathF.Max(minX - pos.X, pos.X - maxX)) +
                           MathF.Max(0, MathF.Max(minY - pos.Y, pos.Y - maxY));

                if (dist < 30f)
                {
                    Log(messageOutput,
        $"  NavMod BuildId={area.BuildingId} MinZ={area.MinZ:F1} MaxZ={area.MaxZ:F1} H={area.Height:F1} dist~{dist:F0}");
                    containingCount++;
                    if (containingCount >= 5) return; // limit output
                }
            }
        }

        if (containingCount == 0)
        {
            Log(messageOutput,
"  No NavModifiers with BuildingId within 30u");
        }
    }
}
