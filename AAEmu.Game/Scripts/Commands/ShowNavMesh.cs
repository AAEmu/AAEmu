using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Visualizes navmesh polygon edges in-game using doodad markers.
/// Boundary edges (mesh border) use one doodad type, inner edges another.
/// Usage: /shownavmesh [radius=30] [step=2]
///        /shownavmesh clear
/// </summary>
public class ShowNavMeshCommand : ICommand
{
    public string[] CommandNames { get; set; } = ["shownavmesh", "snm2"];

    private static readonly List<BaseUnit> Markers = [];

    // Small doodad IDs for edge visualization
    private const uint BoundaryDoodad = 5014u;  // Combat flag — boundary edges (red-ish)
    private const uint InnerDoodad = 5622u;     // Stone post — inner edges
    private const uint VertexDoodad = 4763u;    // Crescent flag — polygon vertices

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "[radius=30] [step=2] | clear | verts [radius=30]";

    public string GetCommandHelpText() =>
        "Visualizes navmesh edges in-game with doodad markers. " +
        "Combat flags = boundary edges (mesh border), " +
        "Stone posts = inner edges (polygon-to-polygon). " +
        "Use 'verts' to show polygon vertices instead. " +
        "Use 'clear' to remove all markers.";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length >= 1 && args[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            ClearMarkers();
            Log(messageOutput, "|cFF00FF00All navmesh markers cleared.|r");
            return;
        }

        var navMesh = character.ParentWorld?.NavMesh;
        if (navMesh == null || !navMesh.HasData)
        {
            Log(messageOutput, "|cFFFF0000No navmesh data available.|r");
            return;
        }

        var pos = character.Transform.Local.Position;
        var showVerts = args.Length >= 1 && args[0].Equals("verts", StringComparison.OrdinalIgnoreCase);
        var argOffset = showVerts ? 1 : 0;

        var radius = 30f;
        if (args.Length > argOffset && float.TryParse(args[argOffset], out var r))
            radius = Math.Clamp(r, 5f, 200f);

        var step = 2f;
        if (args.Length > argOffset + 1 && float.TryParse(args[argOffset + 1], out var s))
            step = Math.Clamp(s, 0.5f, 10f);

        ClearMarkers();

        var world = character.ParentWorld;
        var (boundary, inner) = navMesh.GetPolygonEdgesNear(pos.X, pos.Y, pos.Z, radius);

        if (showVerts)
        {
            // Show unique vertices instead of edges
            var verts = new HashSet<(int, int, int)>(); // quantized positions to deduplicate
            var count = 0;
            foreach (var (a, _) in boundary)
            {
                var key = ((int)(a.X * 2), (int)(a.Y * 2), (int)(a.Z * 2));
                if (verts.Add(key))
                {
                    AddMarker(world, a, BoundaryDoodad);
                    count++;
                }
            }
            foreach (var (a, _) in inner)
            {
                var key = ((int)(a.X * 2), (int)(a.Y * 2), (int)(a.Z * 2));
                if (verts.Add(key))
                {
                    AddMarker(world, a, VertexDoodad);
                    count++;
                }
            }
            Log(messageOutput, $"|cFF00FFFF=== ShowNavMesh: {count} vertices (radius {radius:F0}) ===|r");
        }
        else
        {
            // Place doodads along edges at intervals
            var boundaryCount = PlaceEdgeMarkers(world, boundary, BoundaryDoodad, step);
            var innerCount = PlaceEdgeMarkers(world, inner, InnerDoodad, step);

            Log(messageOutput, $"|cFF00FFFF=== ShowNavMesh: {boundaryCount + innerCount} markers (radius {radius:F0}, step {step:F1}) ===|r");
            Log(messageOutput, $"  |cFFFF4444Combat flags|r: {boundaryCount} (boundary edges — mesh border)");
            Log(messageOutput, $"  |cFFFFFF00Stone posts|r: {innerCount} (inner edges — polygon seams)");
        }

        Log(messageOutput, $"  Total edges: {boundary.Count} boundary + {inner.Count} inner");
        Log(messageOutput, "Use |cFFFFFF00/shownavmesh clear|r to remove markers.");
    }

    private static int PlaceEdgeMarkers(WorldInstance world, List<(Vector3 a, Vector3 b)> edges, uint doodadId, float step)
    {
        var count = 0;
        foreach (var (a, b) in edges)
        {
            var edgeLen = Vector3.Distance(a, b);
            var steps = Math.Max(1, (int)(edgeLen / step));

            for (var i = 0; i <= steps; i++)
            {
                var t = (float)i / steps;
                var p = Vector3.Lerp(a, b, t);
                AddMarker(world, p, doodadId);
                count++;
            }
        }
        return count;
    }

    private static void AddMarker(WorldInstance world, Vector3 pos, uint doodadTemplateId)
    {
        var marker = DoodadManager.Instance.Create(world, 0, doodadTemplateId);
        marker.Transform.Local.SetPosition(pos);
        marker.Show();
        Markers.Add(marker);
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

    private void Log(IMessageOutput messageOutput, string text)
    {
        CommandManager.SendNormalText(this, messageOutput, text);
    }
}
