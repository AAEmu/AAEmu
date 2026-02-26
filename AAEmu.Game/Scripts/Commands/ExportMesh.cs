using System.Globalization;
using System.Numerics;
using System.Text;

using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.IO;
using AAEmu.Game.Models.CryEngine;
using AAEmu.Game.Models.CryEngine.Loaders;
using AAEmu.Game.Models.CryEngine.Objects;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.Scripts;
using Jitter2.LinearMath;

using NLog;

namespace AAEmu.Game.Scripts.Commands;

public class ExportMesh : ICommand
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    // Center offset: subtracted from all OBJ coordinates so player is at origin in Blender.
    // Set in Execute() before calling export functions. OBJ Y-up convention.
    private static float _cx, _cy, _cz;

    public string[] CommandNames { get; set; } = ["exportmesh", "em"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "[radius=500]";

    public string GetCommandHelpText() =>
        "Exports all server-side mesh data as OBJ files for visualization in Blender. " +
        "All coordinates are absolute world (Y-up OBJ convention). " +
        "Includes terrain, BAI, brushes, navmesh, roads, NPCs.";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var radius = 500f;
        if (args.Length > 0 && float.TryParse(args[0], out var r))
            radius = Math.Clamp(r, 50f, 2000f);

        var pos = character.Transform.Local.Position;
        var zoneId = character.Transform.ZoneId;
        var world = WorldManager.Instance.GetWorldTemplateByZoneKey(zoneId);
        if (world == null)
        {
            CommandManager.SendErrorText(this, messageOutput, "No world template found for current zone.");
            return;
        }

        // Center all exports on the player so Blender viewport shows the mesh at origin
        _cx = pos.X;          // game X → OBJ X
        _cy = pos.Z;          // game Z (height) → OBJ Y
        _cz = pos.Y;          // game Y → OBJ Z

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var exportDir = Path.Combine(FileManager.AppPath, "Data", "Exports", $"exportmesh_{timestamp}");
        Directory.CreateDirectory(exportDir);

        Log(messageOutput, $"Exporting mesh data within {radius}m of ({pos.X:F0}, {pos.Y:F0}, {pos.Z:F0})...");
        Log(messageOutput, $"  Player is at origin (0,0,0) in Blender");

        var terrainVerts = ExportTerrain(world, pos, radius, exportDir);
        var navModCount = ExportNavModifiers(world, pos, radius, exportDir);
        var (perType, skipped) = ExportBaiTrianglesByType(world, pos, radius, exportDir);
        var (roadCount, roadNodeCount) = ExportRoads(world, pos, radius, exportDir);
        var flightSpanCount = ExportFlightSpans(world, pos, radius, exportDir);
        var (brushMeshCount, brushTriCount) = ExportBrushMeshes(world, pos, radius, exportDir);
        var npcCount = ExportNpcs(character, pos, radius, exportDir);
        var navMeshTriCount = ExportNavMeshPolygons(character, pos, radius, exportDir);

        Log(messageOutput, $"Export complete → {exportDir}");
        Log(messageOutput, $"  terrain.obj: {terrainVerts} vertices");
        Log(messageOutput, $"  navmodifiers.obj: {navModCount} polygons");
        foreach (var (type, count) in perType.OrderBy(kv => kv.Key))
            Log(messageOutput, $"  bai_type{type}.obj: {count} triangles");
        Log(messageOutput, $"  ({skipped} triangles skipped - bad obstacle indices)");
        Log(messageOutput, $"  roads.obj: {roadCount} roads ({roadNodeCount} nodes)");
        Log(messageOutput, $"  flight_spans.obj: {flightSpanCount} spans");
        Log(messageOutput, $"  brush_meshes.obj: {brushMeshCount} brushes ({brushTriCount} triangles)");
        Log(messageOutput, $"  npcs.obj: {npcCount} NPCs + player marker");
        Log(messageOutput, $"  navmesh.obj: {navMeshTriCount} triangles (built navmesh surface)");
    }

    private void Log(IMessageOutput messageOutput, string text)
    {
        CommandManager.SendNormalText(this, messageOutput, text);
        Logger.Info("[ExportMesh] " + text);
    }

    /// <summary>
    /// Exports heightmap terrain as triangulated OBJ mesh.
    /// Uses stride=2 (4m resolution). Absolute world coordinates, Y-up.
    /// </summary>
    private static int ExportTerrain(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        const int stride = 2;
        const int resolution = WorldManager.CELL_HMAP_RESOLUTION;
        var gridW = resolution / stride + 1;

        var sb = new StringBuilder(4 * 1024 * 1024);
        sb.AppendLine("# AAEmu Terrain Heightmap Export");
        sb.AppendLine($"# Near: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        sb.AppendLine("# Absolute world coordinates, Y-up (OBJ standard)");
        sb.AppendLine();

        var totalVerts = 0;
        var radiusSq = radius * radius;
        var coeff = world.HeightMaxCoefficient;

        var minCellX = Math.Max(0, (int)((center.X - radius) / WorldManager.CELL_SIZE));
        var maxCellX = Math.Min(world.CellX - 1, (int)((center.X + radius) / WorldManager.CELL_SIZE));
        var minCellY = Math.Max(0, (int)((center.Y - radius) / WorldManager.CELL_SIZE));
        var maxCellY = Math.Min(world.CellY - 1, (int)((center.Y + radius) / WorldManager.CELL_SIZE));

        for (var cellY = minCellY; cellY <= maxCellY; cellY++)
        {
            for (var cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                var cell = world.GetCell(cellX, cellY);
                if (cell == null) continue;
                cell.VerifyCellLoaded();
                if (cell.HeightMap == null) continue;

                var cellWorldX = (float)(cellX * WorldManager.CELL_SIZE);
                var cellWorldY = (float)(cellY * WorldManager.CELL_SIZE);
                var baseVertIndex = totalVerts;

                sb.AppendLine($"# Cell ({cellX}, {cellY})");

                for (var gy = 0; gy < gridW; gy++)
                {
                    var sy = Math.Min(gy * stride, resolution - 1);
                    var worldY = cellWorldY + sy * 2f;

                    for (var gx = 0; gx < gridW; gx++)
                    {
                        var sx = Math.Min(gx * stride, resolution - 1);
                        var worldX = cellWorldX + sx * 2f;
                        var height = (float)(cell.HeightMap[sx, sy] / coeff);

                        // Game X→OBJ X, game Z(height)→OBJ Y, game Y→OBJ Z
                        sb.AppendLine(FmtV(worldX, height, worldY));
                        totalVerts++;
                    }
                }

                for (var gy = 0; gy < gridW - 1; gy++)
                {
                    for (var gx = 0; gx < gridW - 1; gx++)
                    {
                        var qcx = cellWorldX + (gx * stride + stride) * 2f;
                        var qcy = cellWorldY + (gy * stride + stride) * 2f;
                        var qdx = qcx - center.X;
                        var qdy = qcy - center.Y;
                        if (qdx * qdx + qdy * qdy > radiusSq)
                            continue;

                        var i00 = baseVertIndex + gy * gridW + gx + 1;
                        var i10 = i00 + 1;
                        var i01 = i00 + gridW;
                        var i11 = i01 + 1;

                        sb.AppendLine($"f {i00} {i11} {i10}");
                        sb.AppendLine($"f {i00} {i01} {i11}");
                    }
                }
            }
        }

        File.WriteAllText(Path.Combine(exportDir, "terrain.obj"), sb.ToString());
        return totalVerts;
    }

    private static readonly string[] NodeTypeNames =
    [
        "Walkable0", "Walkable1", "Walkable2", "Walkable3",
        "Forbidden", "ForbiddenDesign", "Removable",
    ];

    /// <summary>
    /// Exports NavigationModifier building floor polygons as OBJ faces.
    /// Absolute world coordinates.
    /// </summary>
    private static int ExportNavModifiers(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        var sb = new StringBuilder(256 * 1024);
        sb.AppendLine("# AAEmu NavigationModifier Building Floor Zones");
        sb.AppendLine($"# Near: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        sb.AppendLine("# Absolute world coordinates, Y-up");
        sb.AppendLine();

        var polyCount = 0;
        var vertIndex = 0;
        var radiusSq = radius * radius;
        var visited = new HashSet<(int, int)>();

        var minPathX = (int)((center.X - radius) / 256);
        var maxPathX = (int)((center.X + radius) / 256);
        var minPathY = (int)((center.Y - radius) / 256);
        var maxPathY = (int)((center.Y + radius) / 256);

        for (var py = minPathY; py <= maxPathY; py++)
        {
            for (var px = minPathX; px <= maxPathX; px++)
            {
                if (!visited.Add((px, py)))
                    continue;

                var tileCenter = new Vector3(px * 256 + 128, py * 256 + 128, center.Z);
                var bai = world.GetBaiByPos(tileCenter);
                if (bai == null) continue;

                foreach (var areaMission in bai.AreasMissionReaders)
                {
                    foreach (var area in areaMission.NavigationModifiers)
                    {
                        if (area.BuildingId <= 0 || area.Points.Count < 3)
                            continue;

                        var inRange = false;
                        foreach (var p in area.Points)
                        {
                            var dx = p.X - center.X;
                            var dy = p.Y - center.Y;
                            if (dx * dx + dy * dy <= radiusSq)
                            {
                                inRange = true;
                                break;
                            }
                        }
                        if (!inRange) continue;

                        var floorZ = (float)area.MinZ;
                        var baseIdx = vertIndex + 1;

                        sb.AppendLine($"# BuildingId={area.BuildingId} MinZ={area.MinZ:F1} MaxZ={area.MaxZ:F1}");

                        foreach (var p in area.Points)
                        {
                            sb.AppendLine(FmtV(p.X, floorZ, p.Y));
                            vertIndex++;
                        }

                        var ceilZ = (float)area.MaxZ;
                        foreach (var p in area.Points)
                        {
                            sb.AppendLine(FmtV(p.X, ceilZ, p.Y));
                            vertIndex++;
                        }

                        var n = area.Points.Count;
                        for (var i = 1; i < n - 1; i++)
                            sb.AppendLine($"f {baseIdx} {baseIdx + i} {baseIdx + i + 1}");

                        var ceilBase = baseIdx + n;
                        for (var i = 1; i < n - 1; i++)
                            sb.AppendLine($"f {ceilBase} {ceilBase + i + 1} {ceilBase + i}");

                        polyCount++;
                    }
                }
            }
        }

        File.WriteAllText(Path.Combine(exportDir, "navmodifiers.obj"), sb.ToString());
        return polyCount;
    }

    /// <summary>
    /// Exports BAI triangulation mesh split by node type into separate OBJ files.
    /// Absolute world coordinates.
    /// </summary>
    private static (Dictionary<int, int> perType, int skipped) ExportBaiTrianglesByType(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        var builders = new Dictionary<int, (StringBuilder sb, int vertIndex)>();
        var perTypeCount = new Dictionary<int, int>();
        var skippedCount = 0;
        var radiusSq = radius * radius;
        var visitedLoaders = new HashSet<BaseBaiLoader>();

        StringBuilder GetOrCreateBuilder(int type)
        {
            if (!builders.ContainsKey(type))
            {
                var typeName = type < NodeTypeNames.Length ? NodeTypeNames[type] : $"Unknown{type}";
                var sb = new StringBuilder(1024 * 1024);
                sb.AppendLine($"# AAEmu BAI Triangulation - Type {type} ({typeName})");
                sb.AppendLine($"# Near: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
                sb.AppendLine("# Absolute world coordinates, Y-up");
                sb.AppendLine();
                builders[type] = (sb, 0);
                perTypeCount[type] = 0;
            }
            return builders[type].sb;
        }

        var minPathX = (int)((center.X - radius) / 256);
        var maxPathX = (int)((center.X + radius) / 256);
        var minPathY = (int)((center.Y - radius) / 256);
        var maxPathY = (int)((center.Y + radius) / 256);

        for (var py = minPathY; py <= maxPathY; py++)
        {
            for (var px = minPathX; px <= maxPathX; px++)
            {
                var tileCenter = new Vector3(px * 256 + 128, py * 256 + 128, center.Z);
                var bai = world.GetBaiByPos(tileCenter);
                if (bai == null || !visitedLoaders.Add(bai))
                    continue;

                foreach (var netMission in bai.NetMissionReaders)
                {
                    var vertexMission = bai.VertexMissionReaders
                        .FirstOrDefault(v => v.ZoneId == netMission.ZoneId);

                    if (vertexMission == null || vertexMission.ObstacleDataDescriptorList.Count == 0)
                        continue;

                    var obstacles = vertexMission.ObstacleDataDescriptorList;

                    foreach (var (_, node) in netMission.NodeDescriptorList)
                    {
                        var dx = node.Pos.X - center.X;
                        var dy = node.Pos.Y - center.Y;
                        if (dx * dx + dy * dy > radiusSq)
                            continue;

                        if (node.Obstacle.Length < 3 ||
                            node.Obstacle[0] < 0 || node.Obstacle[0] >= obstacles.Count ||
                            node.Obstacle[1] < 0 || node.Obstacle[1] >= obstacles.Count ||
                            node.Obstacle[2] < 0 || node.Obstacle[2] >= obstacles.Count)
                        {
                            skippedCount++;
                            continue;
                        }

                        var type = (int)node.Type;
                        var sb = GetOrCreateBuilder(type);
                        var (_, vi) = builders[type];

                        var v0 = obstacles[node.Obstacle[0]].Pos;
                        var v1 = obstacles[node.Obstacle[1]].Pos;
                        var v2 = obstacles[node.Obstacle[2]].Pos;

                        var baseIdx = vi + 1;
                        // Game X→OBJ X, game Z(height)→OBJ Y, game Y→OBJ Z
                        sb.AppendLine(FmtV(v0.X, v0.Z, v0.Y));
                        sb.AppendLine(FmtV(v1.X, v1.Z, v1.Y));
                        sb.AppendLine(FmtV(v2.X, v2.Z, v2.Y));
                        sb.AppendLine($"f {baseIdx} {baseIdx + 1} {baseIdx + 2}");

                        builders[type] = (sb, vi + 3);
                        perTypeCount[type]++;
                    }
                }
            }
        }

        foreach (var (type, (sb, _)) in builders)
        {
            File.WriteAllText(Path.Combine(exportDir, $"bai_type{type}.obj"), sb.ToString());
        }

        return (perTypeCount, skippedCount);
    }

    /// <summary>
    /// Exports BAI roads as ribbon meshes. Absolute world coordinates.
    /// </summary>
    private static (int roads, int nodes) ExportRoads(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        var sb = new StringBuilder(512 * 1024);
        sb.AppendLine("# AAEmu BAI Roads");
        sb.AppendLine($"# Near: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        sb.AppendLine("# Absolute world coordinates, Y-up");
        sb.AppendLine();

        var roadCount = 0;
        var totalNodes = 0;
        var vertIndex = 0;
        var radiusSq = radius * radius;
        var visitedLoaders = new HashSet<BaseBaiLoader>();

        var minPathX = (int)((center.X - radius) / 256);
        var maxPathX = (int)((center.X + radius) / 256);
        var minPathY = (int)((center.Y - radius) / 256);
        var maxPathY = (int)((center.Y + radius) / 256);

        for (var py = minPathY; py <= maxPathY; py++)
        {
            for (var px = minPathX; px <= maxPathX; px++)
            {
                var tileCenter = new Vector3(px * 256 + 128, py * 256 + 128, center.Z);
                var bai = world.GetBaiByPos(tileCenter);
                if (bai == null || !visitedLoaders.Add(bai))
                    continue;

                foreach (var roadReader in bai.RoadMissionReaders)
                {
                    foreach (var road in roadReader.RoadList)
                    {
                        if (road.RoadNodeList.Count < 2)
                            continue;

                        var inRange = false;
                        foreach (var node in road.RoadNodeList)
                        {
                            var dx = node.Pos.X - center.X;
                            var dy = node.Pos.Y - center.Y;
                            if (dx * dx + dy * dy <= radiusSq)
                            {
                                inRange = true;
                                break;
                            }
                        }
                        if (!inRange) continue;

                        sb.AppendLine($"# Road: {road.Name} ({road.RoadNodeList.Count} nodes)");
                        var baseIdx = vertIndex + 1;

                        for (var i = 0; i < road.RoadNodeList.Count; i++)
                        {
                            var node = road.RoadNodeList[i];
                            var halfW = (float)(node.Width * 0.5);
                            if (halfW < 0.1f) halfW = 1f;

                            Vector3 perp;
                            if (i < road.RoadNodeList.Count - 1)
                            {
                                var next = road.RoadNodeList[i + 1];
                                var dir = next.Pos - node.Pos;
                                perp = Vector3.Normalize(new Vector3(-dir.Y, dir.X, 0)) * halfW;
                            }
                            else
                            {
                                var prev = road.RoadNodeList[i - 1];
                                var dir = node.Pos - prev.Pos;
                                perp = Vector3.Normalize(new Vector3(-dir.Y, dir.X, 0)) * halfW;
                            }

                            var left = node.Pos + perp;
                            var right = node.Pos - perp;

                            sb.AppendLine(FmtV(left.X, left.Z, left.Y));
                            sb.AppendLine(FmtV(right.X, right.Z, right.Y));
                            vertIndex += 2;
                        }

                        for (var i = 0; i < road.RoadNodeList.Count - 1; i++)
                        {
                            var i0 = baseIdx + i * 2;
                            var i1 = i0 + 1;
                            var i2 = i0 + 2;
                            var i3 = i0 + 3;
                            sb.AppendLine($"f {i0} {i2} {i3} {i1}");
                        }

                        roadCount++;
                        totalNodes += road.RoadNodeList.Count;
                    }
                }
            }
        }

        File.WriteAllText(Path.Combine(exportDir, "roads.obj"), sb.ToString());
        return (roadCount, totalNodes);
    }

    /// <summary>
    /// Exports BAI flight navigation spans as vertical columns.
    /// Absolute world coordinates.
    /// </summary>
    private static int ExportFlightSpans(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        var sb = new StringBuilder(512 * 1024);
        sb.AppendLine("# AAEmu BAI Flight Navigation Spans");
        sb.AppendLine($"# Near: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        sb.AppendLine("# Absolute world coordinates, Y-up");
        sb.AppendLine();

        var spanCount = 0;
        var vertIndex = 0;
        var radiusSq = radius * radius;
        var visitedLoaders = new HashSet<BaseBaiLoader>();

        var minPathX = (int)((center.X - radius) / 256);
        var maxPathX = (int)((center.X + radius) / 256);
        var minPathY = (int)((center.Y - radius) / 256);
        var maxPathY = (int)((center.Y + radius) / 256);

        for (var py = minPathY; py <= maxPathY; py++)
        {
            for (var px = minPathX; px <= maxPathX; px++)
            {
                var tileCenter = new Vector3(px * 256 + 128, py * 256 + 128, center.Z);
                var bai = world.GetBaiByPos(tileCenter);
                if (bai == null || !visitedLoaders.Add(bai))
                    continue;

                foreach (var flightReader in bai.FlightMissionReaders)
                {
                    var region = flightReader.FlightNavRegion;
                    if (region == null) continue;

                    foreach (var span in region.SpanList)
                    {
                        var dx = span.X - center.X;
                        var dy = span.Y - center.Y;
                        if (dx * dx + dy * dy > radiusSq)
                            continue;

                        var r = (float)span.MaxRadius;
                        if (r < 0.5f) r = 2f;
                        // Game coords → OBJ Y-up absolute
                        var x = (float)span.X;
                        var z = (float)span.Y;      // game Y → OBJ Z
                        var yMin = (float)span.MinZ; // game Z → OBJ Y
                        var yMax = (float)span.MaxZ;

                        var baseIdx = vertIndex + 1;
                        sb.AppendLine(FmtV(x - r, yMin, z - r));
                        sb.AppendLine(FmtV(x + r, yMin, z - r));
                        sb.AppendLine(FmtV(x + r, yMin, z + r));
                        sb.AppendLine(FmtV(x - r, yMin, z + r));
                        sb.AppendLine(FmtV(x - r, yMax, z - r));
                        sb.AppendLine(FmtV(x + r, yMax, z - r));
                        sb.AppendLine(FmtV(x + r, yMax, z + r));
                        sb.AppendLine(FmtV(x - r, yMax, z + r));

                        sb.AppendLine($"f {baseIdx} {baseIdx + 3} {baseIdx + 2} {baseIdx + 1}");
                        sb.AppendLine($"f {baseIdx + 4} {baseIdx + 5} {baseIdx + 6} {baseIdx + 7}");
                        sb.AppendLine($"f {baseIdx} {baseIdx + 1} {baseIdx + 5} {baseIdx + 4}");
                        sb.AppendLine($"f {baseIdx + 1} {baseIdx + 2} {baseIdx + 6} {baseIdx + 5}");
                        sb.AppendLine($"f {baseIdx + 2} {baseIdx + 3} {baseIdx + 7} {baseIdx + 6}");
                        sb.AppendLine($"f {baseIdx + 3} {baseIdx} {baseIdx + 4} {baseIdx + 7}");

                        vertIndex += 8;
                        spanCount++;
                    }
                }
            }
        }

        File.WriteAllText(Path.Combine(exportDir, "flight_spans.obj"), sb.ToString());
        return spanCount;
    }

    /// <summary>
    /// Exports brush collision meshes (.cgf physics proxies) as OBJ triangles.
    /// Uses the same transform as NavMeshManager (which produces correct navmesh).
    /// Absolute world coordinates in DotRecast Y-up (= OBJ Y-up).
    /// </summary>
    private static (int brushes, int triangles) ExportBrushMeshes(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        var sb = new StringBuilder(8 * 1024 * 1024);
        sb.AppendLine("# AAEmu Brush Collision Meshes (.cgf physics proxies)");
        sb.AppendLine($"# Near: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        sb.AppendLine("# Absolute world coordinates, Y-up");
        sb.AppendLine();

        var brushCount = 0;
        var totalTriangles = 0;
        var vertIndex = 0;
        var radiusSq = radius * radius;
        var processedBrushes = new HashSet<(int pathId, int matrixHash)>();

        var minCellX = Math.Max(0, (int)((center.X - radius) / WorldManager.CELL_SIZE));
        var maxCellX = Math.Min(world.CellX - 1, (int)((center.X + radius) / WorldManager.CELL_SIZE));
        var minCellY = Math.Max(0, (int)((center.Y - radius) / WorldManager.CELL_SIZE));
        var maxCellY = Math.Min(world.CellY - 1, (int)((center.Y + radius) / WorldManager.CELL_SIZE));

        for (var cellY = minCellY; cellY <= maxCellY; cellY++)
        {
            for (var cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                var cell = world.GetCell(cellX, cellY);
                if (cell == null) continue;
                cell.VerifyCellLoaded();

                if (cell.LoadedObjectDat == null || cell.StatObjsFiles == null || cell.MaterialListFiles == null)
                    continue;

                var cellOffsetX = (float)(cellX * WorldManager.CELL_SIZE);
                var cellOffsetY = (float)(cellY * WorldManager.CELL_SIZE);

                // Process both object.dat and visareas.dat brushes
                var sources = new List<(IEnumerable<ObjectDataBase> prefabs, string tag)>
                {
                    (cell.LoadedObjectDat.PrefabsList, "brush")
                };
                if (cell.LoadedVisAreasDat != null)
                    sources.Add((cell.LoadedVisAreasDat.PrefabsList, "visarea"));

                foreach (var (prefabs, tag) in sources)
                foreach (var objectData in prefabs)
                {
                    if (objectData is not ObjectDataType1Brush brush)
                        continue;

                    if (Math.Abs(brush.Matrix3X4.M33) < 0.1f)
                        continue;

                    if (brush.PathId < 0 || brush.PathId >= cell.StatObjsFiles.MaterialList.Count)
                        continue;
                    if (brush.MaterialId < 0 || brush.MaterialId >= cell.MaterialListFiles.MaterialsList.Count)
                        continue;

                    var modelPath = cell.StatObjsFiles.MaterialList[brush.PathId];
                    var materialPath = cell.MaterialListFiles.MaterialsList[brush.MaterialId];

                    if (modelPath == "game/objects/nodraw" || materialPath == "game/objects/nodraw")
                        continue;

                    // Deduplicate
                    {
                        var bm = brush.Matrix3X4;
                        var h1 = HashCode.Combine(
                            BitConverter.SingleToInt32Bits(bm.M11), BitConverter.SingleToInt32Bits(bm.M12),
                            BitConverter.SingleToInt32Bits(bm.M13), BitConverter.SingleToInt32Bits(bm.M14));
                        var h2 = HashCode.Combine(h1,
                            BitConverter.SingleToInt32Bits(bm.M21), BitConverter.SingleToInt32Bits(bm.M22),
                            BitConverter.SingleToInt32Bits(bm.M23), BitConverter.SingleToInt32Bits(bm.M24));
                        var h3 = HashCode.Combine(h2,
                            BitConverter.SingleToInt32Bits(bm.M31), BitConverter.SingleToInt32Bits(bm.M32),
                            BitConverter.SingleToInt32Bits(bm.M33), BitConverter.SingleToInt32Bits(bm.M34));
                        if (!processedBrushes.Add((brush.PathId, h3)))
                            continue;
                    }

                    var brushWorldX = cellOffsetX + brush.Matrix3X4.M14;
                    var brushWorldY = cellOffsetY + brush.Matrix3X4.M24;
                    var dx = brushWorldX - center.X;
                    var dy = brushWorldY - center.Y;
                    if (dx * dx + dy * dy > radiusSq)
                        continue;

                    var triangles = CryEngineModelHelper.MakeModel(modelPath, materialPath, out var usedPhysicsProxy);
                    if (triangles == null || triangles.Count == 0)
                        continue;
                    var maxTris = AppConfiguration.Instance.World.LoadBrushMaxTriangles;
                    if (!usedPhysicsProxy && maxTris > 0 && triangles.Count > maxTris)
                        continue;

                    // Same rotation matrix as NavMeshManager.BuildTileFromCell
                    // CryEngine Z-up column-vector → DotRecast/OBJ Y-up
                    // Model verts from MakeModel are Y↔Z swapped: v.X=lx, v.Y=lz, v.Z=ly
                    var m = brush.Matrix3X4;
                    var r00 = m.M11; var r01 = m.M13; var r02 = m.M12;
                    var r10 = m.M31; var r11 = m.M33; var r12 = m.M32;
                    var r20 = m.M21; var r21 = m.M23; var r22 = m.M22;

                    // Translation: DotRecast X=M14+cellX, Y(height)=M34, Z(depth)=M24+cellY
                    var tx = m.M14 + cellOffsetX;
                    var ty = m.M34;
                    var tz = m.M24 + cellOffsetY;

                    sb.AppendLine($"g {tag}_{brushCount}");
                    sb.AppendLine($"# model: {modelPath}");
                    sb.AppendLine($"# world pos: ({brushWorldX:F1}, {brushWorldY:F1}, {brush.Matrix3X4.M34:F1})");

                    var baseIdx = vertIndex + 1;

                    foreach (var tri in triangles)
                    {
                        // Transform: world = rotation * local + translation
                        // Output directly in DotRecast Y-up = OBJ Y-up absolute coords
                        var wx0 = r00 * tri.V0.X + r01 * tri.V0.Y + r02 * tri.V0.Z + tx;
                        var wy0 = r10 * tri.V0.X + r11 * tri.V0.Y + r12 * tri.V0.Z + ty;
                        var wz0 = r20 * tri.V0.X + r21 * tri.V0.Y + r22 * tri.V0.Z + tz;
                        sb.AppendLine(FmtV(wx0, wy0, wz0));

                        var wx1 = r00 * tri.V1.X + r01 * tri.V1.Y + r02 * tri.V1.Z + tx;
                        var wy1 = r10 * tri.V1.X + r11 * tri.V1.Y + r12 * tri.V1.Z + ty;
                        var wz1 = r20 * tri.V1.X + r21 * tri.V1.Y + r22 * tri.V1.Z + tz;
                        sb.AppendLine(FmtV(wx1, wy1, wz1));

                        var wx2 = r00 * tri.V2.X + r01 * tri.V2.Y + r02 * tri.V2.Z + tx;
                        var wy2 = r10 * tri.V2.X + r11 * tri.V2.Y + r12 * tri.V2.Z + ty;
                        var wz2 = r20 * tri.V2.X + r21 * tri.V2.Y + r22 * tri.V2.Z + tz;
                        sb.AppendLine(FmtV(wx2, wy2, wz2));
                    }

                    for (var i = 0; i < triangles.Count; i++)
                    {
                        var fi = baseIdx + i * 3;
                        sb.AppendLine($"f {fi} {fi + 1} {fi + 2}");
                    }

                    vertIndex += triangles.Count * 3;
                    totalTriangles += triangles.Count;
                    brushCount++;
                }
            }
        }

        File.WriteAllText(Path.Combine(exportDir, "brush_meshes.obj"), sb.ToString());
        return (brushCount, totalTriangles);
    }

    /// <summary>
    /// Exports the built navmesh surface (what the server actually uses for height/pathfinding).
    /// Uses NavMeshManager.GetAllDetailTriangles() which returns game-coordinate triangles.
    /// </summary>
    private static int ExportNavMeshPolygons(Character character, Vector3 center, float radius, string exportDir)
    {
        var worldInstance = character.ParentWorld;
        if (worldInstance?.NavMesh == null || !worldInstance.NavMesh.HasData)
        {
            File.WriteAllText(Path.Combine(exportDir, "navmesh.obj"), "# No navmesh data available\n");
            return 0;
        }

        var sb = new StringBuilder(8 * 1024 * 1024);
        sb.AppendLine("# AAEmu Built NavMesh Surface (server collision)");
        sb.AppendLine($"# Near: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        sb.AppendLine("# Absolute world coordinates, Y-up");
        sb.AppendLine();

        var triCount = 0;
        var vertIndex = 0;
        var radiusSq = radius * radius;

        var allTris = worldInstance.NavMesh.GetAllDetailTriangles();

        foreach (var (v0, v1, v2, tileX, tileZ, tileLayer) in allTris)
        {
            // v0/v1/v2 are in game coords (X, Y, Z where Z=height)
            // Check if triangle center is within radius
            var cx = (v0.X + v1.X + v2.X) / 3f;
            var cy = (v0.Y + v1.Y + v2.Y) / 3f;
            var dx = cx - center.X;
            var dy = cy - center.Y;
            if (dx * dx + dy * dy > radiusSq)
                continue;

            var baseIdx = vertIndex + 1;
            // Game X→OBJ X, game Z(height)→OBJ Y, game Y→OBJ Z
            sb.AppendLine(FmtV(v0.X, v0.Z, v0.Y));
            sb.AppendLine(FmtV(v1.X, v1.Z, v1.Y));
            sb.AppendLine(FmtV(v2.X, v2.Z, v2.Y));
            sb.AppendLine($"f {baseIdx} {baseIdx + 1} {baseIdx + 2}");

            vertIndex += 3;
            triCount++;
        }

        File.WriteAllText(Path.Combine(exportDir, "navmesh.obj"), sb.ToString());
        return triCount;
    }

    /// <summary>
    /// Exports NPCs as diamond markers. Absolute world coordinates.
    /// </summary>
    private static int ExportNpcs(Character character, Vector3 center, float radius, string exportDir)
    {
        var sb = new StringBuilder(256 * 1024);
        sb.AppendLine("# AAEmu NPC Positions");
        sb.AppendLine($"# Near: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        sb.AppendLine("# Absolute world coordinates, Y-up");
        sb.AppendLine();

        var npcCount = 0;
        var vertIndex = 0;
        var radiusSq = radius * radius;

        var world = character.ParentWorld;
        if (world == null)
            goto writeFile;

        // Player marker
        {
            var pp = character.Transform.World.Position;
            sb.AppendLine($"g player_{character.Name}");
            sb.AppendLine($"# Player: {character.Name} at ({pp.X:F1}, {pp.Y:F1}, {pp.Z:F1})");
            WriteDiamondMarker(sb, ref vertIndex, pp.X, pp.Z, pp.Y, 1.5f);
        }

        foreach (var npc in world.GetAllNpcs())
        {
            var npcPos = npc.Transform.World.Position;
            var dx = npcPos.X - center.X;
            var dy = npcPos.Y - center.Y;
            if (dx * dx + dy * dy > radiusSq)
                continue;

            var aiName = npc.Ai?.GetType().Name ?? "NoAi";
            sb.AppendLine($"g npc_{npc.ObjId}");
            sb.AppendLine($"# NPC ObjId={npc.ObjId} TemplateId={npc.TemplateId} AI={aiName}");
            sb.AppendLine($"# Pos: ({npcPos.X:F1}, {npcPos.Y:F1}, {npcPos.Z:F1})");

            WriteDiamondMarker(sb, ref vertIndex, npcPos.X, npcPos.Z, npcPos.Y, 1.0f);
            npcCount++;
        }

        writeFile:
        File.WriteAllText(Path.Combine(exportDir, "npcs.obj"), sb.ToString());
        return npcCount;
    }

    private static void WriteDiamondMarker(StringBuilder sb, ref int vertIndex, float x, float y, float z, float size)
    {
        var half = size * 0.5f;
        var baseIdx = vertIndex + 1;

        sb.AppendLine(FmtV(x, y + size, z));
        sb.AppendLine(FmtV(x, y, z));
        sb.AppendLine(FmtV(x + half, y + half, z));
        sb.AppendLine(FmtV(x - half, y + half, z));
        sb.AppendLine(FmtV(x, y + half, z + half));
        sb.AppendLine(FmtV(x, y + half, z - half));

        sb.AppendLine($"f {baseIdx} {baseIdx + 2} {baseIdx + 4}");
        sb.AppendLine($"f {baseIdx} {baseIdx + 4} {baseIdx + 3}");
        sb.AppendLine($"f {baseIdx} {baseIdx + 3} {baseIdx + 5}");
        sb.AppendLine($"f {baseIdx} {baseIdx + 5} {baseIdx + 2}");
        sb.AppendLine($"f {baseIdx + 1} {baseIdx + 4} {baseIdx + 2}");
        sb.AppendLine($"f {baseIdx + 1} {baseIdx + 3} {baseIdx + 4}");
        sb.AppendLine($"f {baseIdx + 1} {baseIdx + 5} {baseIdx + 3}");
        sb.AppendLine($"f {baseIdx + 1} {baseIdx + 2} {baseIdx + 5}");

        vertIndex += 6;
    }

    private static string FmtV(float x, float y, float z)
        => $"v {(x - _cx).ToString("F2", CultureInfo.InvariantCulture)} {(y - _cy).ToString("F2", CultureInfo.InvariantCulture)} {(z - _cz).ToString("F2", CultureInfo.InvariantCulture)}";
}
