using System.Numerics;
using System.Text;

using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.IO;
using AAEmu.Game.Models.CryEngine;
using AAEmu.Game.Models.CryEngine.Objects;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;

using NLog;

namespace AAEmu.Game.Scripts.Commands;

public class SearchPak : ICommand
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public string[] CommandNames { get; set; } = ["searchpak", "sp"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "<keyword> | brushes [radius]";

    public string GetCommandHelpText() =>
        "Search game_pak for files matching a keyword, or list brush .cgf paths near player.\n" +
        "  /searchpak stair         - Find files with 'stair' in their path\n" +
        "  /searchpak ramp          - Find files with 'ramp' in their path\n" +
        "  /searchpak brushes 200   - List all brush .cgf models within 200m of player";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 1)
        {
            CommandManager.SendErrorText(this, messageOutput, "Usage: /searchpak <keyword> | brushes [radius]");
            return;
        }

        if (args[0].Equals("brushes", StringComparison.OrdinalIgnoreCase))
        {
            var radius = 200f;
            if (args.Length > 1 && float.TryParse(args[1], out var r))
                radius = Math.Clamp(r, 10f, 2000f);
            ListNearbyBrushes(character, radius, messageOutput);
        }
        else
        {
            var keyword = string.Join(" ", args);
            SearchPakFiles(keyword, messageOutput);
        }
    }

    private void SearchPakFiles(string keyword, IMessageOutput messageOutput)
    {
        Log(messageOutput, $"Searching game_pak for '{keyword}'...");

        var results = new List<string>();
        var keywordLower = keyword.ToLower();

        foreach (var source in ClientFileManager.Sources)
        {
            if (source.SourceType != ClientSourceType.GamePak || source.GamePak == null)
                continue;

            foreach (var fileName in source.GamePak.pakFiles.Keys)
            {
                if (fileName.ToLower().Contains(keywordLower))
                    results.Add(fileName);
            }
        }

        if (results.Count == 0)
        {
            Log(messageOutput, $"No files found matching '{keyword}'");
            return;
        }

        // Sort and limit output
        results.Sort();
        Log(messageOutput, $"Found {results.Count} files matching '{keyword}':");

        // Show first 50 in chat
        var shown = 0;
        foreach (var r in results)
        {
            if (shown < 50)
                Log(messageOutput, $"  {r}");
            shown++;
        }

        if (results.Count > 50)
            Log(messageOutput, $"  ... and {results.Count - 50} more");

        // Also dump full list to file
        var exportPath = Path.Combine(Commons.IO.FileManager.AppPath, "Data", "Exports", $"searchpak_{keyword.Replace(' ', '_')}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
        File.WriteAllLines(exportPath, results);
        Log(messageOutput, $"Full list saved to: {exportPath}");
    }

    private void ListNearbyBrushes(Character character, float radius, IMessageOutput messageOutput)
    {
        var pos = character.Transform.Local.Position;
        var zoneId = character.Transform.ZoneId;
        var world = WorldManager.Instance.GetWorldTemplateByZoneKey(zoneId);
        if (world == null)
        {
            CommandManager.SendErrorText(this, messageOutput, "No world template found.");
            return;
        }

        Log(messageOutput, $"Listing brush .cgf models within {radius}m of ({pos.X:F0}, {pos.Y:F0}, {pos.Z:F0})...");

        var brushInfos = new List<BrushInfo>();
        var radiusSq = radius * radius;

        var minCellX = Math.Max(0, (int)((pos.X - radius) / WorldManager.CELL_SIZE));
        var maxCellX = Math.Min(world.CellX - 1, (int)((pos.X + radius) / WorldManager.CELL_SIZE));
        var minCellY = Math.Max(0, (int)((pos.Y - radius) / WorldManager.CELL_SIZE));
        var maxCellY = Math.Min(world.CellY - 1, (int)((pos.Y + radius) / WorldManager.CELL_SIZE));

        for (var cellY = minCellY; cellY <= maxCellY; cellY++)
        for (var cellX = minCellX; cellX <= maxCellX; cellX++)
        {
            var cell = world.GetCell(cellX, cellY);
            if (cell == null) continue;
            cell.VerifyCellLoaded();

            if (cell.StatObjsFiles == null || cell.MaterialListFiles == null)
                continue;

            var cellOffsetX = (float)(cellX * WorldManager.CELL_SIZE);
            var cellOffsetY = (float)(cellY * WorldManager.CELL_SIZE);

            // Check object.dat brushes
            if (cell.LoadedObjectDat != null)
                CollectBrushes(cell.LoadedObjectDat.PrefabsList, cell, cellOffsetX, cellOffsetY, pos, radiusSq, "object.dat", brushInfos);

            // Check visareas.dat brushes
            if (cell.LoadedVisAreasDat != null)
                CollectBrushes(cell.LoadedVisAreasDat.PrefabsList, cell, cellOffsetX, cellOffsetY, pos, radiusSq, "visareas.dat", brushInfos);
        }

        // Sort by distance
        brushInfos.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        // Count stats
        var loaded = brushInfos.Count(b => b.Status == "OK");
        var failed = brushInfos.Count(b => b.Status == "FAILED");
        var noMesh = brushInfos.Count(b => b.Status == "NO_MESH");

        Log(messageOutput, $"Found {brushInfos.Count} brushes: {loaded} loaded, {failed} failed, {noMesh} no mesh");

        // Show in chat (first 30)
        var shown = 0;
        foreach (var b in brushInfos)
        {
            if (shown < 30)
            {
                var statusTag = b.Status switch
                {
                    "OK" => $"OK ({b.TriCount} tris)",
                    "FAILED" => "FAILED",
                    "NO_MESH" => "NO_MESH",
                    _ => b.Status
                };
                Log(messageOutput, $"  [{statusTag}] {b.ModelPath} (size={b.RoughSize:F1}, dist={b.Distance:F0}, src={b.Source})");
            }
            shown++;
        }

        if (brushInfos.Count > 30)
            Log(messageOutput, $"  ... and {brushInfos.Count - 30} more");

        // Dump full list to file
        var sb = new StringBuilder();
        sb.AppendLine("Status\tTriangles\tRoughSize\tDistance\tSource\tModelPath\tMaterialPath\tWorldPos");
        foreach (var b in brushInfos)
            sb.AppendLine($"{b.Status}\t{b.TriCount}\t{b.RoughSize:F1}\t{b.Distance:F0}\t{b.Source}\t{b.ModelPath}\t{b.MaterialPath}\t({b.WorldX:F1},{b.WorldY:F1},{b.WorldZ:F1})");

        var exportPath = Path.Combine(Commons.IO.FileManager.AppPath, "Data", "Exports", "nearby_brushes.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
        File.WriteAllText(exportPath, sb.ToString());
        Log(messageOutput, $"Full list saved to: {exportPath}");

        // Also dump unique failed paths
        var failedPaths = brushInfos.Where(b => b.Status != "OK").Select(b => b.ModelPath).Distinct().OrderBy(p => p).ToList();
        if (failedPaths.Count > 0)
        {
            var failedExport = Path.Combine(Commons.IO.FileManager.AppPath, "Data", "Exports", "failed_brush_models.txt");
            File.WriteAllLines(failedExport, failedPaths);
            Log(messageOutput, $"Failed/no-mesh model paths ({failedPaths.Count}) saved to: {failedExport}");
        }
    }

    private static void CollectBrushes(IEnumerable<ObjectDataBase> prefabs, WorldCell cell,
        float cellOffsetX, float cellOffsetY, Vector3 playerPos, float radiusSq,
        string source, List<BrushInfo> results)
    {
        foreach (var objectData in prefabs)
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

            var wx = cellOffsetX + brush.Matrix3X4.M14;
            var wy = cellOffsetY + brush.Matrix3X4.M24;
            var wz = brush.Matrix3X4.M34;
            var dx = wx - playerPos.X;
            var dy = wy - playerPos.Y;
            var distSq = dx * dx + dy * dy;
            if (distSq > radiusSq)
                continue;

            var roughSize = Vector3.Distance(brush.StartPos, brush.EndPos);

            // Try loading the model to check status
            string status;
            var triCount = 0;
            var key = modelPath.Replace('\\', '/').ToLower();

            if (CryEngineModelHelper.CryEngineModelsFailedPaths.Contains(key))
            {
                status = "FAILED";
            }
            else
            {
                var triangles = CryEngineModelHelper.MakeModel(modelPath, materialPath);
                if (triangles == null || triangles.Count == 0)
                {
                    status = "NO_MESH";
                }
                else
                {
                    status = "OK";
                    triCount = triangles.Count;
                }
            }

            results.Add(new BrushInfo
            {
                ModelPath = modelPath,
                MaterialPath = materialPath,
                WorldX = wx,
                WorldY = wy,
                WorldZ = wz,
                RoughSize = roughSize,
                Distance = MathF.Sqrt(distSq),
                Status = status,
                TriCount = triCount,
                Source = source
            });
        }
    }

    private void Log(IMessageOutput messageOutput, string text)
    {
        CommandManager.SendNormalText(this, messageOutput, text);
        Logger.Info("[SearchPak] " + text);
    }

    private class BrushInfo
    {
        public string ModelPath;
        public string MaterialPath;
        public float WorldX, WorldY, WorldZ;
        public float RoughSize;
        public float Distance;
        public string Status;
        public int TriCount;
        public string Source;
    }
}
