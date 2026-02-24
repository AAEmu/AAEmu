using System.Globalization;
using System.Numerics;
using System.Text;

using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.CryEngine;
using AAEmu.Game.Models.CryEngine.Loaders;
using AAEmu.Game.Models.CryEngine.Objects;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;
using Jitter2.LinearMath;

using NLog;

namespace AAEmu.Game.Scripts.Commands;

public class ExportMesh : ICommand
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public string[] CommandNames { get; set; } = ["exportmesh", "em"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "[radius=500]";

    public string GetCommandHelpText() =>
        "Exports terrain heightmap, BAI Type4 nodes, and NavigationModifier floors " +
        "as OBJ files for visualization in Blender/MeshLab.";

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

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var exportDir = Path.Combine(FileManager.AppPath, "Data", "Exports", $"exportmesh_{timestamp}");
        Directory.CreateDirectory(exportDir);

        Log(messageOutput, $"Exporting mesh data within {radius}m of ({pos.X:F0}, {pos.Y:F0}, {pos.Z:F0})...");

        var terrainVerts = ExportTerrain(world, pos, radius, exportDir);
        var navModCount = ExportNavModifiers(world, pos, radius, exportDir);
        var (perType, skipped) = ExportBaiTrianglesByType(world, pos, radius, exportDir);
        var (roadCount, roadNodeCount) = ExportRoads(world, pos, radius, exportDir);
        var flightSpanCount = ExportFlightSpans(world, pos, radius, exportDir);
        var brushCount = ExportBrushBoxes(world, pos, radius, exportDir);
        var (forbiddenCount, designerCount) = ExportForbiddenAreas(world, pos, radius, exportDir);
        var (brushMeshCount, brushTriCount) = ExportBrushMeshes(world, pos, radius, exportDir);

        Log(messageOutput, $"Export complete → {exportDir}");
        Log(messageOutput, $"  terrain.obj: {terrainVerts} vertices");
        Log(messageOutput, $"  navmodifiers.obj: {navModCount} polygons");
        foreach (var (type, count) in perType.OrderBy(kv => kv.Key))
            Log(messageOutput, $"  bai_type{type}.obj: {count} triangles");
        Log(messageOutput, $"  ({skipped} triangles skipped - bad obstacle indices)");
        Log(messageOutput, $"  roads.obj: {roadCount} roads ({roadNodeCount} nodes)");
        Log(messageOutput, $"  flight_spans.obj: {flightSpanCount} spans");
        Log(messageOutput, $"  brush_boxes.obj: {brushCount} brush AABB boxes");
        Log(messageOutput, $"  brush_meshes.obj: {brushMeshCount} brushes ({brushTriCount} triangles)");
        Log(messageOutput, $"  forbidden_areas.obj: {forbiddenCount} forbidden + {designerCount} designer areas");
    }

    private void Log(IMessageOutput messageOutput, string text)
    {
        CommandManager.SendNormalText(this, messageOutput, text);
        Logger.Info("[ExportMesh] " + text);
    }

    /// <summary>
    /// Exports heightmap terrain as triangulated OBJ mesh.
    /// Uses stride=2 (4m resolution) for a good balance of detail and file size.
    /// Coordinates are centered on player position and use OBJ Y-up convention.
    /// </summary>
    private static int ExportTerrain(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        const int stride = 2; // 2 samples = 4m resolution
        const int resolution = WorldManager.CELL_HMAP_RESOLUTION; // 512
        var gridW = resolution / stride + 1; // vertices per cell edge

        var sb = new StringBuilder(4 * 1024 * 1024);
        sb.AppendLine("# AAEmu Terrain Heightmap Export");
        sb.AppendLine($"# Game Center: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        sb.AppendLine("# Centered at origin, Y-up (OBJ standard)");
        sb.AppendLine();

        var totalVerts = 0;
        var radiusSq = radius * radius;
        var coeff = world.HeightMaxCoefficient;

        // Find cells that overlap the radius
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

                // Generate vertices
                for (var gy = 0; gy < gridW; gy++)
                {
                    var sy = Math.Min(gy * stride, resolution - 1);
                    var worldY = cellWorldY + sy * 2f;

                    for (var gx = 0; gx < gridW; gx++)
                    {
                        var sx = Math.Min(gx * stride, resolution - 1);
                        var worldX = cellWorldX + sx * 2f;

                        var height = (float)(cell.HeightMap[sx, sy] / coeff);

                        // OBJ Y-up: game X→OBJ X, game Z(height)→OBJ Y, game Y→OBJ Z
                        // Centered on player position
                        sb.AppendLine(FormatVertex(worldX - center.X, height - center.Z, worldY - center.Y));
                        totalVerts++;
                    }
                }

                // Generate faces (two triangles per quad, 1-indexed)
                for (var gy = 0; gy < gridW - 1; gy++)
                {
                    for (var gx = 0; gx < gridW - 1; gx++)
                    {
                        // Check if quad center is within radius
                        var qcx = cellWorldX + (gx * stride + stride) * 2f;
                        var qcy = cellWorldY + (gy * stride + stride) * 2f;
                        var qdx = qcx - center.X;
                        var qdy = qcy - center.Y;
                        if (qdx * qdx + qdy * qdy > radiusSq)
                            continue;

                        var i00 = baseVertIndex + gy * gridW + gx + 1; // OBJ is 1-indexed
                        var i10 = i00 + 1;
                        var i01 = i00 + gridW;
                        var i11 = i01 + 1;

                        sb.AppendLine($"f {i00} {i10} {i11}");
                        sb.AppendLine($"f {i00} {i11} {i01}");
                    }
                }
            }
        }

        File.WriteAllText(Path.Combine(exportDir, "terrain.obj"), sb.ToString());
        return totalVerts;
    }

    // Node type names from CryEngine BAI format
    private static readonly string[] NodeTypeNames =
    [
        "Walkable0",       // 0 - EWayPointNodeType0
        "Walkable1",       // 1 - EWayPointNodeType1
        "Walkable2",       // 2 - EWayPointNodeType2
        "Walkable3",       // 3 - EWayPointNodeType3
        "Forbidden",       // 4 - Forbidden area
        "ForbiddenDesign", // 5 - Designer-marked forbidden
        "Removable",       // 6 - Removable
    ];

    /// <summary>
    /// Exports NavigationModifier building floor polygons as OBJ faces.
    /// Each polygon is triangulated using fan from the first vertex.
    /// </summary>
    private static int ExportNavModifiers(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        var sb = new StringBuilder(256 * 1024);
        sb.AppendLine("# AAEmu NavigationModifier Building Floor Zones");
        sb.AppendLine($"# Center: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
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

                        // Check if any polygon point is within radius
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
                        var baseIdx = vertIndex + 1; // OBJ 1-indexed

                        sb.AppendLine($"# BuildingId={area.BuildingId} MinZ={area.MinZ:F1} MaxZ={area.MaxZ:F1}");

                        // Write vertices at floor height (centered + Y-up)
                        foreach (var p in area.Points)
                        {
                            sb.AppendLine(FormatVertex(p.X - center.X, floorZ - center.Z, p.Y - center.Y));
                            vertIndex++;
                        }

                        // Also write vertices at ceiling height for volume visualization
                        var ceilZ = (float)area.MaxZ;
                        foreach (var p in area.Points)
                        {
                            sb.AppendLine(FormatVertex(p.X - center.X, ceilZ - center.Z, p.Y - center.Y));
                            vertIndex++;
                        }

                        var n = area.Points.Count;

                        // Floor face (fan triangulation)
                        for (var i = 1; i < n - 1; i++)
                            sb.AppendLine($"f {baseIdx} {baseIdx + i} {baseIdx + i + 1}");

                        // Ceiling face
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
    /// Each NetMission node has a Type (0-6) and 3 obstacle indices forming a triangle.
    /// Produces: bai_type0.obj, bai_type1.obj, ... for each type that has triangles.
    /// </summary>
    private static (Dictionary<int, int> perType, int skipped) ExportBaiTrianglesByType(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        // Per-type builders: StringBuilder + vertex index counter
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
                sb.AppendLine($"# Game Center: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
                sb.AppendLine("# Centered at origin, Y-up (OBJ standard)");
                sb.AppendLine();
                builders[type] = (sb, 0);
                perTypeCount[type] = 0;
            }
            return builders[type].sb;
        }

        // Scan path tiles (256m each) within radius
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

                        var baseIdx = vi + 1; // OBJ 1-indexed
                        sb.AppendLine(FormatVertex(v0.X - center.X, v0.Z - center.Z, v0.Y - center.Y));
                        sb.AppendLine(FormatVertex(v1.X - center.X, v1.Z - center.Z, v1.Y - center.Y));
                        sb.AppendLine(FormatVertex(v2.X - center.X, v2.Z - center.Z, v2.Y - center.Y));
                        sb.AppendLine($"f {baseIdx} {baseIdx + 1} {baseIdx + 2}");

                        builders[type] = (sb, vi + 3);
                        perTypeCount[type]++;
                    }
                }
            }
        }

        // Write each type to its own file
        foreach (var (type, (sb, _)) in builders)
        {
            File.WriteAllText(Path.Combine(exportDir, $"bai_type{type}.obj"), sb.ToString());
        }

        return (perTypeCount, skippedCount);
    }

    /// <summary>
    /// Exports BAI roads as ribbon meshes. Each road is a strip of quads connecting
    /// consecutive nodes, using the node width to create left/right edges perpendicular
    /// to the road direction.
    /// </summary>
    private static (int roads, int nodes) ExportRoads(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        var sb = new StringBuilder(512 * 1024);
        sb.AppendLine("# AAEmu BAI Roads");
        sb.AppendLine($"# Game Center: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        sb.AppendLine("# Centered at origin, Y-up (OBJ standard)");
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

                        // Check if any node is within radius
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

                        // Generate left/right vertices for each node
                        for (var i = 0; i < road.RoadNodeList.Count; i++)
                        {
                            var node = road.RoadNodeList[i];
                            var halfW = (float)(node.Width * 0.5);
                            if (halfW < 0.1f) halfW = 1f;

                            // Direction perpendicular to road
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

                            // Y-up: game X→X, game Z→Y (height), game Y→Z
                            sb.AppendLine(FormatVertex(left.X - center.X, left.Z - center.Z, left.Y - center.Y));
                            sb.AppendLine(FormatVertex(right.X - center.X, right.Z - center.Z, right.Y - center.Y));
                            vertIndex += 2;
                        }

                        // Generate quad faces between consecutive node pairs
                        for (var i = 0; i < road.RoadNodeList.Count - 1; i++)
                        {
                            var i0 = baseIdx + i * 2;     // left current
                            var i1 = i0 + 1;              // right current
                            var i2 = i0 + 2;              // left next
                            var i3 = i0 + 3;              // right next
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
    /// Exports BAI flight navigation spans as vertical columns (box per span).
    /// Each span has X, Y, MinZ, MaxZ defining a flight volume in the air.
    /// </summary>
    private static int ExportFlightSpans(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        var sb = new StringBuilder(512 * 1024);
        sb.AppendLine("# AAEmu BAI Flight Navigation Spans");
        sb.AppendLine($"# Game Center: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        sb.AppendLine("# Centered at origin, Y-up (OBJ standard)");
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
                        var x = (float)(span.X - center.X);
                        var z = (float)(span.Y - center.Y); // game Y → OBJ Z
                        var yMin = (float)(span.MinZ - center.Z); // height → OBJ Y
                        var yMax = (float)(span.MaxZ - center.Z);

                        // 8-vertex box (bottom 4 + top 4)
                        var baseIdx = vertIndex + 1;
                        sb.AppendLine(FormatVertex(x - r, yMin, z - r));
                        sb.AppendLine(FormatVertex(x + r, yMin, z - r));
                        sb.AppendLine(FormatVertex(x + r, yMin, z + r));
                        sb.AppendLine(FormatVertex(x - r, yMin, z + r));
                        sb.AppendLine(FormatVertex(x - r, yMax, z - r));
                        sb.AppendLine(FormatVertex(x + r, yMax, z - r));
                        sb.AppendLine(FormatVertex(x + r, yMax, z + r));
                        sb.AppendLine(FormatVertex(x - r, yMax, z + r));

                        // Bottom face
                        sb.AppendLine($"f {baseIdx} {baseIdx + 3} {baseIdx + 2} {baseIdx + 1}");
                        // Top face
                        sb.AppendLine($"f {baseIdx + 4} {baseIdx + 5} {baseIdx + 6} {baseIdx + 7}");
                        // Side faces
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
    /// Exports brush bounding boxes (AABB) as wireframe-style boxes.
    /// These represent the collision volumes of static objects (buildings, rocks, walls).
    /// </summary>
    private static int ExportBrushBoxes(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        var sb = new StringBuilder(1024 * 1024);
        sb.AppendLine("# AAEmu Brush Bounding Boxes (AABB)");
        sb.AppendLine($"# Game Center: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        sb.AppendLine("# Centered at origin, Y-up (OBJ standard)");
        sb.AppendLine();

        var brushes = world.GetNearbyBrushBounds(center.X, center.Y, radius);
        var vertIndex = 0;

        foreach (var b in brushes)
        {
            // Convert to centered + Y-up coordinates
            var x0 = b.MinX - center.X;
            var x1 = b.MaxX - center.X;
            var z0 = b.MinY - center.Y; // game Y → OBJ Z
            var z1 = b.MaxY - center.Y;
            var y0 = b.MinZ - center.Z; // game Z (height) → OBJ Y
            var y1 = b.MaxZ - center.Z;

            // 8-vertex box
            var baseIdx = vertIndex + 1;
            sb.AppendLine(FormatVertex(x0, y0, z0)); // 0: min corner bottom
            sb.AppendLine(FormatVertex(x1, y0, z0)); // 1
            sb.AppendLine(FormatVertex(x1, y0, z1)); // 2
            sb.AppendLine(FormatVertex(x0, y0, z1)); // 3
            sb.AppendLine(FormatVertex(x0, y1, z0)); // 4: min corner top
            sb.AppendLine(FormatVertex(x1, y1, z0)); // 5
            sb.AppendLine(FormatVertex(x1, y1, z1)); // 6
            sb.AppendLine(FormatVertex(x0, y1, z1)); // 7

            // 6 faces
            sb.AppendLine($"f {baseIdx} {baseIdx + 3} {baseIdx + 2} {baseIdx + 1}");     // bottom
            sb.AppendLine($"f {baseIdx + 4} {baseIdx + 5} {baseIdx + 6} {baseIdx + 7}"); // top
            sb.AppendLine($"f {baseIdx} {baseIdx + 1} {baseIdx + 5} {baseIdx + 4}");     // front
            sb.AppendLine($"f {baseIdx + 1} {baseIdx + 2} {baseIdx + 6} {baseIdx + 5}"); // right
            sb.AppendLine($"f {baseIdx + 2} {baseIdx + 3} {baseIdx + 7} {baseIdx + 6}"); // back
            sb.AppendLine($"f {baseIdx + 3} {baseIdx} {baseIdx + 4} {baseIdx + 7}");     // left

            vertIndex += 8;
        }

        File.WriteAllText(Path.Combine(exportDir, "brush_boxes.obj"), sb.ToString());
        return brushes.Count;
    }

    /// <summary>
    /// Exports BAI forbidden areas as extruded wall polygons.
    /// Each forbidden area is a 2D polygon — we extrude it vertically (3m walls)
    /// to visualize where NPCs cannot walk.
    /// </summary>
    private static (int forbidden, int designer) ExportForbiddenAreas(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        var sb = new StringBuilder(512 * 1024);
        sb.AppendLine("# AAEmu BAI Forbidden Areas (extruded as walls)");
        sb.AppendLine($"# Game Center: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        sb.AppendLine("# Centered at origin, Y-up (OBJ standard)");
        sb.AppendLine();

        var forbiddenCount = 0;
        var designerCount = 0;
        var vertIndex = 0;
        var radiusSq = radius * radius;
        var visitedLoaders = new HashSet<BaseBaiLoader>();

        const float wallHeight = 5f; // extrude walls 5m high for visualization

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

                foreach (var areaMission in bai.AreasMissionReaders)
                {
                    // Process both forbidden and designer forbidden areas
                    foreach (var (areaList, isDesigner) in new[]
                    {
                        (areaMission.ForbiddenAreasList, false),
                        (areaMission.DesignerForbiddenAreasList, true)
                    })
                    {
                        foreach (var area in areaList)
                        {
                            if (area.Points.Count < 3)
                                continue;

                            // Check if any point is within radius
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

                            var label = isDesigner ? "DesignerForbidden" : "Forbidden";
                            sb.AppendLine($"# {label}: {area.Name} ({area.Points.Count} pts)");

                            // Use Z from the polygon points (floor level), or estimate from terrain
                            var baseZ = area.Points[0].Z;
                            var topZ = baseZ + wallHeight;

                            var baseIdx = vertIndex + 1;
                            var n = area.Points.Count;

                            // Bottom ring vertices
                            foreach (var p in area.Points)
                            {
                                sb.AppendLine(FormatVertex(p.X - center.X, baseZ - center.Z, p.Y - center.Y));
                                vertIndex++;
                            }

                            // Top ring vertices
                            foreach (var p in area.Points)
                            {
                                sb.AppendLine(FormatVertex(p.X - center.X, topZ - center.Z, p.Y - center.Y));
                                vertIndex++;
                            }

                            // Bottom face (fan triangulation)
                            for (var i = 1; i < n - 1; i++)
                                sb.AppendLine($"f {baseIdx} {baseIdx + i} {baseIdx + i + 1}");

                            // Top face (reversed winding)
                            var topBase = baseIdx + n;
                            for (var i = 1; i < n - 1; i++)
                                sb.AppendLine($"f {topBase} {topBase + i + 1} {topBase + i}");

                            // Side wall quads (connect bottom edge to top edge)
                            for (var i = 0; i < n; i++)
                            {
                                var next = (i + 1) % n;
                                var b0 = baseIdx + i;
                                var b1 = baseIdx + next;
                                var t0 = topBase + i;
                                var t1 = topBase + next;
                                sb.AppendLine($"f {b0} {b1} {t1} {t0}");
                            }

                            if (isDesigner) designerCount++;
                            else forbiddenCount++;
                        }
                    }
                }
            }
        }

        File.WriteAllText(Path.Combine(exportDir, "forbidden_areas.obj"), sb.ToString());
        return (forbiddenCount, designerCount);
    }

    /// <summary>
    /// Exports actual brush collision meshes (.cgf) as OBJ triangles.
    /// Each brush is labeled with its .cgf model path so you can identify
    /// stairs, ramps, platforms, etc. in Blender.
    /// Uses LoadBrushMinimumSize=0 to include ALL brushes regardless of size.
    /// </summary>
    private static (int brushes, int triangles) ExportBrushMeshes(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        var sb = new StringBuilder(8 * 1024 * 1024);
        sb.AppendLine("# AAEmu Brush Collision Meshes (.cgf physics proxies)");
        sb.AppendLine($"# Game Center: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        sb.AppendLine("# Each 'g' group is a brush object with its .cgf model path");
        sb.AppendLine("# Centered at origin, Y-up (OBJ standard)");
        sb.AppendLine();

        var brushCount = 0;
        var totalTriangles = 0;
        var vertIndex = 0;
        var radiusSq = radius * radius;

        // Find cells that overlap the radius
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

                foreach (var objectData in cell.LoadedObjectDat.PrefabsList)
                {
                    if (objectData is not ObjectDataType1Brush brush)
                        continue;

                    // Resolve paths
                    if (brush.PathId < 0 || brush.PathId >= cell.StatObjsFiles.MaterialList.Count)
                        continue;
                    if (brush.MaterialId < 0 || brush.MaterialId >= cell.MaterialListFiles.MaterialsList.Count)
                        continue;

                    var modelPath = cell.StatObjsFiles.MaterialList[brush.PathId];
                    var materialPath = cell.MaterialListFiles.MaterialsList[brush.MaterialId];

                    if (modelPath == "game/objects/nodraw" || materialPath == "game/objects/nodraw")
                        continue;

                    // Check if brush center is within radius (using world-space position)
                    var brushWorldX = cellOffsetX + brush.Matrix3X4.M14;
                    var brushWorldY = cellOffsetY + brush.Matrix3X4.M24;
                    var dx = brushWorldX - center.X;
                    var dy = brushWorldY - center.Y;
                    if (dx * dx + dy * dy > radiusSq)
                        continue;

                    // Load collision triangles (cached, in Jitter Y-up local model space)
                    var triangles = CryEngineModelHelper.MakeModel(modelPath, materialPath);
                    if (triangles == null || triangles.Count == 0)
                        continue;

                    // Build rotation matrix (same Y<->Z swap as NavMeshManager.AddBrushTriangles)
                    var m = brush.Matrix3X4;
                    var r00 = m.M11; var r01 = m.M31; var r02 = m.M21;
                    var r10 = m.M13; var r11 = m.M33; var r12 = m.M23;
                    var r20 = m.M12; var r21 = m.M32; var r22 = m.M22;

                    // Translation (Y-up): brushX=M14, brushY(height)=M34, brushZ(depth)=M24
                    var tx = m.M14 + cellOffsetX;
                    var ty = m.M34;
                    var tz = m.M24 + cellOffsetY;

                    var roughSize = Vector3.Distance(brush.StartPos, brush.EndPos);
                    sb.AppendLine($"g brush_{brushCount}");
                    sb.AppendLine($"# model: {modelPath}");
                    sb.AppendLine($"# material: {materialPath}");
                    sb.AppendLine($"# world pos: ({brushWorldX:F1}, {brushWorldY:F1}, {brush.Matrix3X4.M34:F1})");
                    sb.AppendLine($"# rough size: {roughSize:F1}");

                    var baseIdx = vertIndex + 1; // OBJ 1-indexed

                    foreach (var tri in triangles)
                    {
                        // Transform each vertex: world = rotation * local + translation
                        // Then center on player position for OBJ
                        WriteTransformedVertex(sb, tri.V0, r00, r01, r02, r10, r11, r12, r20, r21, r22, tx, ty, tz, center);
                        WriteTransformedVertex(sb, tri.V1, r00, r01, r02, r10, r11, r12, r20, r21, r22, tx, ty, tz, center);
                        WriteTransformedVertex(sb, tri.V2, r00, r01, r02, r10, r11, r12, r20, r21, r22, tx, ty, tz, center);
                    }

                    // Write faces
                    for (var i = 0; i < triangles.Count; i++)
                    {
                        var fi = baseIdx + i * 3;
                        sb.AppendLine($"f {fi} {fi + 1} {fi + 2}");
                    }

                    vertIndex += triangles.Count * 3;
                    totalTriangles += triangles.Count;
                    brushCount++;
                }

                // Also export visareas brushes (indoor objects)
                if (cell.LoadedVisAreasDat != null)
                {
                    foreach (var objectData in cell.LoadedVisAreasDat.PrefabsList)
                    {
                        if (objectData is not ObjectDataType1Brush brush)
                            continue;

                        if (brush.PathId < 0 || brush.PathId >= cell.StatObjsFiles.MaterialList.Count)
                            continue;
                        if (brush.MaterialId < 0 || brush.MaterialId >= cell.MaterialListFiles.MaterialsList.Count)
                            continue;

                        var modelPath = cell.StatObjsFiles.MaterialList[brush.PathId];
                        var materialPath = cell.MaterialListFiles.MaterialsList[brush.MaterialId];

                        if (modelPath == "game/objects/nodraw" || materialPath == "game/objects/nodraw")
                            continue;

                        var brushWorldX = cellOffsetX + brush.Matrix3X4.M14;
                        var brushWorldY = cellOffsetY + brush.Matrix3X4.M24;
                        var ddx = brushWorldX - center.X;
                        var ddy = brushWorldY - center.Y;
                        if (ddx * ddx + ddy * ddy > radiusSq)
                            continue;

                        var triangles = CryEngineModelHelper.MakeModel(modelPath, materialPath);
                        if (triangles == null || triangles.Count == 0)
                            continue;

                        var m = brush.Matrix3X4;
                        var r00 = m.M11; var r01 = m.M31; var r02 = m.M21;
                        var r10 = m.M13; var r11 = m.M33; var r12 = m.M23;
                        var r20 = m.M12; var r21 = m.M32; var r22 = m.M22;

                        var tx = m.M14 + cellOffsetX;
                        var ty = m.M34;
                        var tz = m.M24 + cellOffsetY;

                        var roughSize = Vector3.Distance(brush.StartPos, brush.EndPos);
                        sb.AppendLine($"g visarea_brush_{brushCount}");
                        sb.AppendLine($"# model: {modelPath} (visarea)");
                        sb.AppendLine($"# material: {materialPath}");
                        sb.AppendLine($"# world pos: ({brushWorldX:F1}, {brushWorldY:F1}, {brush.Matrix3X4.M34:F1})");
                        sb.AppendLine($"# rough size: {roughSize:F1}");

                        var baseIdx = vertIndex + 1;

                        foreach (var tri in triangles)
                        {
                            WriteTransformedVertex(sb, tri.V0, r00, r01, r02, r10, r11, r12, r20, r21, r22, tx, ty, tz, center);
                            WriteTransformedVertex(sb, tri.V1, r00, r01, r02, r10, r11, r12, r20, r21, r22, tx, ty, tz, center);
                            WriteTransformedVertex(sb, tri.V2, r00, r01, r02, r10, r11, r12, r20, r21, r22, tx, ty, tz, center);
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
        }

        File.WriteAllText(Path.Combine(exportDir, "brush_meshes.obj"), sb.ToString());
        return (brushCount, totalTriangles);
    }

    /// <summary>
    /// Transforms a JVector vertex by rotation matrix + translation, centers on player,
    /// and writes to StringBuilder in OBJ format.
    /// Output is in Y-up OBJ convention (already matching DotRecast/Jitter space).
    /// </summary>
    private static void WriteTransformedVertex(StringBuilder sb, JVector v,
        float r00, float r01, float r02,
        float r10, float r11, float r12,
        float r20, float r21, float r22,
        float tx, float ty, float tz,
        Vector3 center)
    {
        // rotation * v + translation (all in Y-up space: X, Y=height, Z=depth)
        var wx = r00 * v.X + r01 * v.Y + r02 * v.Z + tx;
        var wy = r10 * v.X + r11 * v.Y + r12 * v.Z + ty;
        var wz = r20 * v.X + r21 * v.Y + r22 * v.Z + tz;

        // Center on player: DotRecast X=gameX, Y=gameZ(height), Z=gameY
        // OBJ format uses the same Y-up convention
        sb.AppendLine(FormatVertex(wx - center.X, wy - center.Z, wz - center.Y));
    }

    private static string FormatVertex(float x, float y, float z)
    {
        return $"v {x.ToString("F2", CultureInfo.InvariantCulture)} {y.ToString("F2", CultureInfo.InvariantCulture)} {z.ToString("F2", CultureInfo.InvariantCulture)}";
    }
}
