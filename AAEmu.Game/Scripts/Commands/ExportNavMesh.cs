using System.Globalization;
using System.Numerics;
using System.Text;

using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.Scripts;

using NLog;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Exports navmesh input geometry as OBJ (for Recast demo) and built navmesh as binary.
/// Usage: /exportnavmesh [radius=500]
///
/// Produces in Data/Exports/navmesh_{timestamp}/:
///   - input_layer0.obj — Heightmap terrain geometry (layer 0 input)
///   - input_layer1.obj — Brush/voxel geometry (layer 1 input)
///   - input_combined.obj — All geometry combined (single OBJ for Recast demo)
///   - navmesh.bin — Built DotRecast navmesh binary (import back with /importnavmesh)
/// </summary>
public class ExportNavMeshCommand : ICommand
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static float _cx, _cy, _cz;

    public string[] CommandNames { get; set; } = ["exportnavmesh", "enm"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "[radius=500 | all]";

    public string GetCommandHelpText() =>
        "Exports navmesh input geometry as OBJ files (for Recast demo visualization) " +
        "and built navmesh as binary (for import back with /importnavmesh). " +
        "Use 'all' to export the entire world (large files).";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var world = character.ParentWorld;
        if (world == null)
        {
            Log(messageOutput, "No world instance found.");
            return;
        }

        var pos = character.Transform.Local.Position;
        var exportAll = args.Length > 0 && args[0].Equals("all", StringComparison.OrdinalIgnoreCase);
        var radius = 500f;
        if (!exportAll && args.Length > 0 && float.TryParse(args[0], out var r))
            radius = Math.Clamp(r, 50f, 50000f);

        // Center on player so Blender viewport shows mesh at origin
        _cx = pos.X;
        _cy = pos.Z; // game height → OBJ Y
        _cz = pos.Y; // game Y → OBJ Z

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var exportDir = Path.Combine(FileManager.AppPath, "Data", "Exports", $"navmesh_{timestamp}");
        Directory.CreateDirectory(exportDir);

        if (exportAll)
            Log(messageOutput, "Exporting ENTIRE world geometry (this may take a while)...");
        else
            Log(messageOutput, $"Exporting navmesh data within {radius}m of ({pos.X:F0}, {pos.Y:F0}, {pos.Z:F0})...");
        Log(messageOutput, "  Player is at origin (0,0,0) in Blender");

        // 1. Export input geometry as OBJ files (separated by layer)
        var (terrainTris, brushTris) = exportAll
            ? ExportInputGeometry(world.Template, Vector3.Zero, float.MaxValue, exportDir)
            : ExportInputGeometry(world.Template, pos, radius, exportDir);
        Log(messageOutput, $"  input_layer0.obj: {terrainTris} heightmap terrain triangles");
        Log(messageOutput, $"  input_layer1.obj: {brushTris} brush/voxel triangles");
        Log(messageOutput, $"  input_combined.obj: {terrainTris + brushTris} total triangles (for Recast demo)");

        // 2. Export built navmesh as binary
        if (world.NavMesh != null && world.NavMesh.HasData)
        {
            var binPath = Path.Combine(exportDir, "navmesh.bin");
            if (world.NavMesh.ExportNavMesh(binPath))
            {
                var tileCount = world.NavMesh.TileCount;
                Log(messageOutput, $"  navmesh.bin: {tileCount} tiles exported");
            }
            else
            {
                Log(messageOutput, "  navmesh.bin: FAILED to export");
            }
        }
        else
        {
            Log(messageOutput, "  navmesh.bin: No navmesh data to export");
        }

        Log(messageOutput, $"Export complete → {exportDir}");
        Log(messageOutput, "Open input_combined.obj in Recast demo to visualize/tune parameters.");
        Log(messageOutput, "Use /importnavmesh <path> to load a modified navmesh.bin back.");
    }

    private (int terrainTris, int brushTris) ExportInputGeometry(WorldTemplate world, Vector3 center, float radius, string exportDir)
    {
        var exportAll = radius >= float.MaxValue / 2f;
        var radiusSq = exportAll ? float.MaxValue : radius * radius;

        var minCellX = exportAll ? 0 : Math.Max(0, (int)((center.X - radius) / WorldManager.CELL_SIZE));
        var maxCellX = exportAll ? world.CellX - 1 : Math.Min(world.CellX - 1, (int)((center.X + radius) / WorldManager.CELL_SIZE));
        var minCellY = exportAll ? 0 : Math.Max(0, (int)((center.Y - radius) / WorldManager.CELL_SIZE));
        var maxCellY = exportAll ? world.CellY - 1 : Math.Min(world.CellY - 1, (int)((center.Y + radius) / WorldManager.CELL_SIZE));

        // Stream directly to disk to avoid OutOfMemoryException on large worlds
        using var terrainW = new StreamWriter(Path.Combine(exportDir, "input_layer0.obj"), false, Encoding.UTF8, 65536);
        using var brushW = new StreamWriter(Path.Combine(exportDir, "input_layer1.obj"), false, Encoding.UTF8, 65536);
        using var combinedW = new StreamWriter(Path.Combine(exportDir, "input_combined.obj"), false, Encoding.UTF8, 65536);

        terrainW.WriteLine("# Heightmap Terrain Geometry (Layer 0)");
        terrainW.WriteLine($"# Center: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        terrainW.WriteLine("# Y-up (OBJ standard)");
        terrainW.WriteLine();

        brushW.WriteLine("# Brush/Voxel Structural Geometry (Layer 1)");
        brushW.WriteLine($"# Center: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        brushW.WriteLine("# Y-up (OBJ standard)");
        brushW.WriteLine();

        combinedW.WriteLine("# Combined Input Geometry (for Recast demo)");
        combinedW.WriteLine($"# Center: ({center.X:F1}, {center.Y:F1}, {center.Z:F1}) Radius: {radius}");
        combinedW.WriteLine("# Y-up (OBJ standard)");
        combinedW.WriteLine();

        var terrainVertIdx = 0;
        var brushVertIdx = 0;
        var combinedVertIdx = 0;
        var terrainTriCount = 0;
        var brushTriCount = 0;

        const int resolution = WorldManager.CELL_HMAP_RESOLUTION; // 512
        const int stride = 2; // 2 pixels = 4m per quad (matches NavMeshManager)

        // Use the same geometry collection logic as NavMeshManager
        for (var cellY = minCellY; cellY <= maxCellY; cellY++)
        {
            for (var cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                var cell = world.GetCell(cellX, cellY);
                if (cell == null) continue;
                cell.VerifyCellLoaded();

                // Heightmap terrain triangles
                if (cell.HeightMap != null)
                {
                    var cellOffsetX = (float)(cellX * WorldManager.CELL_SIZE);
                    var cellOffsetY = (float)(cellY * WorldManager.CELL_SIZE);
                    var coeff = world.HeightMaxCoefficient;
                    var gridW = resolution / stride + 1;

                    // Write all grid vertices for this cell
                    var cellBaseTerrainIdx = terrainVertIdx;
                    var cellBaseCombinedIdx = combinedVertIdx;

                    for (var gy = 0; gy < gridW; gy++)
                    {
                        var sy = Math.Min(gy * stride, resolution - 1);
                        var worldY = cellOffsetY + sy * 2f;

                        for (var gx = 0; gx < gridW; gx++)
                        {
                            var sx = Math.Min(gx * stride, resolution - 1);
                            var worldX = cellOffsetX + sx * 2f;
                            var height = (float)(cell.HeightMap[sx, sy] / coeff);

                            // OBJ Y-up: game X→X, height→Y, game Y→Z
                            var line = FmtV(worldX, height, worldY);
                            terrainW.WriteLine(line);
                            combinedW.WriteLine(line);
                            terrainVertIdx++;
                            combinedVertIdx++;
                        }
                    }

                    // Write quad faces (2 triangles per quad)
                    for (var gy = 0; gy < gridW - 1; gy++)
                    {
                        for (var gx = 0; gx < gridW - 1; gx++)
                        {
                            // Check quad center within radius
                            if (!exportAll)
                            {
                                var qcx = cellOffsetX + (gx * stride + stride / 2) * 2f - center.X;
                                var qcy = cellOffsetY + (gy * stride + stride / 2) * 2f - center.Y;
                                if (qcx * qcx + qcy * qcy > radiusSq)
                                    continue;
                            }

                            var i00t = cellBaseTerrainIdx + gy * gridW + gx + 1; // OBJ is 1-indexed
                            var i10t = i00t + 1;
                            var i01t = i00t + gridW;
                            var i11t = i01t + 1;

                            terrainW.WriteLine($"f {i00t} {i11t} {i10t}");
                            terrainW.WriteLine($"f {i00t} {i01t} {i11t}");

                            var i00c = cellBaseCombinedIdx + gy * gridW + gx + 1;
                            var i10c = i00c + 1;
                            var i01c = i00c + gridW;
                            var i11c = i01c + 1;

                            combinedW.WriteLine($"f {i00c} {i11c} {i10c}");
                            combinedW.WriteLine($"f {i00c} {i01c} {i11c}");

                            terrainTriCount += 2;
                        }
                    }
                }

                // Brush triangles (same as ExportMesh brush export logic, simplified)
                if (cell.LoadedObjectDat != null && cell.StatObjsFiles != null && cell.MaterialListFiles != null)
                {
                    var cellOffsetX = (float)(cellX * WorldManager.CELL_SIZE);
                    var cellOffsetY = (float)(cellY * WorldManager.CELL_SIZE);
                    var sources = new List<IEnumerable<AAEmu.Game.Models.CryEngine.Objects.ObjectDataBase>>
                        { cell.LoadedObjectDat.PrefabsList };
                    if (cell.LoadedVisAreasDat != null)
                        sources.Add(cell.LoadedVisAreasDat.PrefabsList);

                    foreach (var prefabsList in sources)
                    foreach (var objectData in prefabsList)
                    {
                        if (objectData is not AAEmu.Game.Models.CryEngine.Objects.ObjectDataType1Brush brush)
                            continue;

                        if (Math.Abs(brush.Matrix3X4.M33) < 0.1f) continue;
                        if (brush.PathId < 0 || brush.PathId >= cell.StatObjsFiles.MaterialList.Count) continue;
                        if (brush.MaterialId < 0 || brush.MaterialId >= cell.MaterialListFiles.MaterialsList.Count) continue;

                        var modelPath = cell.StatObjsFiles.MaterialList[brush.PathId];
                        var materialPath = cell.MaterialListFiles.MaterialsList[brush.MaterialId];
                        if (modelPath == "game/objects/nodraw" || materialPath == "game/objects/nodraw") continue;

                        var brushWorldX = cellOffsetX + brush.Matrix3X4.M14;
                        var brushWorldY = cellOffsetY + brush.Matrix3X4.M24;
                        var dx = brushWorldX - center.X;
                        var dy = brushWorldY - center.Y;
                        if (dx * dx + dy * dy > radiusSq) continue;

                        var triangles = AAEmu.Game.Models.CryEngine.CryEngineModelHelper.MakeModel(modelPath, materialPath, out var usedPhysProxy);
                        if (triangles == null || triangles.Count == 0) continue;
                        var maxTris = AAEmu.Game.Models.AppConfiguration.Instance.World.LoadBrushMaxTriangles;
                        if (!usedPhysProxy && maxTris > 0 && triangles.Count > maxTris) continue;

                        // OBJ group per brush — visible as separate objects in Blender
                        var safeName = modelPath.Replace('/', '_').Replace('\\', '_').Replace('.', '_');
                        brushW.WriteLine($"g {safeName}_{brushTriCount}");
                        brushW.WriteLine($"# model: {modelPath}");
                        brushW.WriteLine($"# world: ({brushWorldX:F1}, {brushWorldY:F1}, {brush.Matrix3X4.M34:F1})");
                        combinedW.WriteLine($"g {safeName}_{brushTriCount}");

                        var m = brush.Matrix3X4;
                        var r00 = m.M11; var r01 = m.M13; var r02 = m.M12;
                        var r10 = m.M31; var r11 = m.M33; var r12 = m.M32;
                        var r20 = m.M21; var r21 = m.M23; var r22 = m.M22;
                        var tx = m.M14 + cellOffsetX;
                        var ty = m.M34;
                        var tz = m.M24 + cellOffsetY;

                        foreach (var tri in triangles)
                        {
                            // Transform to world DotRecast Y-up, then write as OBJ Y-up
                            var wx0 = r00 * tri.V0.X + r01 * tri.V0.Y + r02 * tri.V0.Z + tx;
                            var wy0 = r10 * tri.V0.X + r11 * tri.V0.Y + r12 * tri.V0.Z + ty;
                            var wz0 = r20 * tri.V0.X + r21 * tri.V0.Y + r22 * tri.V0.Z + tz;
                            var wx1 = r00 * tri.V1.X + r01 * tri.V1.Y + r02 * tri.V1.Z + tx;
                            var wy1 = r10 * tri.V1.X + r11 * tri.V1.Y + r12 * tri.V1.Z + ty;
                            var wz1 = r20 * tri.V1.X + r21 * tri.V1.Y + r22 * tri.V1.Z + tz;
                            var wx2 = r00 * tri.V2.X + r01 * tri.V2.Y + r02 * tri.V2.Z + tx;
                            var wy2 = r10 * tri.V2.X + r11 * tri.V2.Y + r12 * tri.V2.Z + ty;
                            var wz2 = r20 * tri.V2.X + r21 * tri.V2.Y + r22 * tri.V2.Z + tz;

                            // DotRecast Y-up is already OBJ Y-up convention
                            var baseB = brushVertIdx + 1;
                            brushW.WriteLine(FmtV(wx0, wy0, wz0));
                            brushW.WriteLine(FmtV(wx1, wy1, wz1));
                            brushW.WriteLine(FmtV(wx2, wy2, wz2));
                            brushW.WriteLine($"f {baseB} {baseB + 1} {baseB + 2}");
                            brushVertIdx += 3;

                            var baseC = combinedVertIdx + 1;
                            combinedW.WriteLine(FmtV(wx0, wy0, wz0));
                            combinedW.WriteLine(FmtV(wx1, wy1, wz1));
                            combinedW.WriteLine(FmtV(wx2, wy2, wz2));
                            combinedW.WriteLine($"f {baseC} {baseC + 1} {baseC + 2}");
                            combinedVertIdx += 3;

                            brushTriCount++;
                        }
                    }
                }
            }
        }

        // StreamWriters flush and close via using statements above

        return (terrainTriCount, brushTriCount);
    }

    private static void WriteTriangle(StreamWriter w, ref int vertIdx, Vector3 v0, Vector3 v1, Vector3 v2)
    {
        var baseIdx = vertIdx + 1;
        // Game (X, Y, Z) → OBJ Y-up (X, Z_height, Y)
        w.WriteLine(FmtV(v0.X, v0.Z, v0.Y));
        w.WriteLine(FmtV(v1.X, v1.Z, v1.Y));
        w.WriteLine(FmtV(v2.X, v2.Z, v2.Y));
        w.WriteLine($"f {baseIdx} {baseIdx + 1} {baseIdx + 2}");
        vertIdx += 3;
    }

    private static string FmtV(float x, float y, float z)
        => $"v {(x - _cx).ToString("F2", CultureInfo.InvariantCulture)} {(y - _cy).ToString("F2", CultureInfo.InvariantCulture)} {(z - _cz).ToString("F2", CultureInfo.InvariantCulture)}";

    private void Log(IMessageOutput messageOutput, string text)
    {
        CommandManager.SendNormalText(this, messageOutput, text);
        Logger.Info("[ExportNavMesh] " + text);
    }
}
