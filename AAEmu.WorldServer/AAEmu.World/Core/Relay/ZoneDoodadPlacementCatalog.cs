using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// World-space doodad placements from <c>worlds/{name}/level_design/cells/{CX}_{CY}/doodad.g</c>
/// under <see cref="ZoneGameDataRootResolver"/>. Cell-local <c>pos</c> → world via
/// <c>cell*1024 + local</c>. Used when World must author event doodads Zone ChangeStep does not arm.
/// </summary>
public static partial class ZoneDoodadPlacementCatalog
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<uint, IReadOnlyList<DoodadPlacement>>> ByWorld =
        new(StringComparer.OrdinalIgnoreCase);

    public readonly record struct DoodadPlacement(
        uint TemplateId,
        float X,
        float Y,
        float Z,
        float YawDegrees);

    /// <summary>All placements of <paramref name="templateId"/> in a world (empty when root/files missing).</summary>
    public static IReadOnlyList<DoodadPlacement> GetByTemplate(string worldName, uint templateId)
    {
        if (string.IsNullOrWhiteSpace(worldName) || templateId == 0)
            return [];

        if (!GetIndex(worldName).TryGetValue(templateId, out var list) || list == null)
            return [];
        return list;
    }

    /// <summary>Placements whose template is in <paramref name="templateIds"/> (stable order by template then XYZ).</summary>
    public static IReadOnlyList<DoodadPlacement> GetByTemplates(string worldName, IReadOnlyCollection<uint> templateIds)
    {
        if (string.IsNullOrWhiteSpace(worldName) || templateIds == null || templateIds.Count == 0)
            return [];

        var index = GetIndex(worldName);
        if (index.Count == 0)
            return [];

        List<DoodadPlacement>? matched = null;
        foreach (var id in templateIds)
        {
            if (id == 0 || !index.TryGetValue(id, out var list) || list == null || list.Count == 0)
                continue;
            matched ??= [];
            matched.AddRange(list);
        }

        return matched ?? [];
    }

    public static void Invalidate(string? worldName = null)
    {
        if (string.IsNullOrWhiteSpace(worldName))
            ByWorld.Clear();
        else
            ByWorld.TryRemove(worldName.Trim(), out _);
    }

    /// <summary>Replace the cached index for a world (unit tests only).</summary>
    public static void SeedIndexForTests(string worldName, IEnumerable<DoodadPlacement> placements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldName);
        var byTemplate = new Dictionary<uint, List<DoodadPlacement>>();
        foreach (var p in placements ?? [])
        {
            if (p.TemplateId == 0)
                continue;
            if (!byTemplate.TryGetValue(p.TemplateId, out var list))
            {
                list = [];
                byTemplate[p.TemplateId] = list;
            }

            list.Add(p);
        }

        var frozen = new Dictionary<uint, IReadOnlyList<DoodadPlacement>>(byTemplate.Count);
        foreach (var (id, list) in byTemplate)
            frozen[id] = list;
        ByWorld[worldName.Trim()] = frozen;
    }

    /// <summary>Parse one cell <c>doodad.g</c> with explicit cell indices (tests / tooling).</summary>
    public static IReadOnlyList<DoodadPlacement> ParseFile(string path, int cellX, int cellY)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("doodad.g not found", path);
        return ParsePlacements(path, cellX, cellY);
    }

    /// <summary><c>019_020</c> → (19, 20).</summary>
    public static bool TryParseCellFolderName(string folderName, out int cellX, out int cellY)
    {
        cellX = 0;
        cellY = 0;
        if (string.IsNullOrWhiteSpace(folderName))
            return false;
        var m = CellFolderRegex().Match(folderName.Trim());
        if (!m.Success)
            return false;
        if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out cellX))
            return false;
        if (!int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out cellY))
            return false;
        return true;
    }

    /// <summary>Yaw degrees from level <c>ori (x,y,z,w)</c> (Z-up pure yaw when x≈y≈0).</summary>
    public static float YawDegreesFromOri(float oriX, float oriY, float oriZ, float oriW)
    {
        _ = oriX;
        _ = oriY;
        return (float)(Math.Atan2(oriZ, oriW) * (360.0 / Math.PI));
    }

    private static IReadOnlyDictionary<uint, IReadOnlyList<DoodadPlacement>> GetIndex(string worldName)
    {
        var key = worldName.Trim();
        return ByWorld.GetOrAdd(key, LoadWorld);
    }

    private static IReadOnlyDictionary<uint, IReadOnlyList<DoodadPlacement>> LoadWorld(string worldName)
    {
        var root = ZoneGameDataRootResolver.TryGetRoot();
        if (root is null)
            return EmptyIndex;

        var cellsDir = Path.Combine(root, "worlds", worldName, "level_design", "cells");
        if (!Directory.Exists(cellsDir))
        {
            Logger.Warn(
                "ZoneDoodadPlacementCatalog: cells dir missing world={0} path={1}",
                worldName, cellsDir);
            return EmptyIndex;
        }

        var byTemplate = new Dictionary<uint, List<DoodadPlacement>>();
        var cellDirs = 0;
        var files = 0;
        var placements = 0;
        foreach (var cellDir in Directory.EnumerateDirectories(cellsDir))
        {
            cellDirs++;
            if (!TryParseCellFolderName(Path.GetFileName(cellDir), out var cellX, out var cellY))
                continue;
            var path = Path.Combine(cellDir, "doodad.g");
            if (!File.Exists(path))
                continue;

            files++;
            foreach (var place in ParsePlacements(path, cellX, cellY))
            {
                if (!byTemplate.TryGetValue(place.TemplateId, out var list))
                {
                    list = [];
                    byTemplate[place.TemplateId] = list;
                }

                list.Add(place);
                placements++;
            }
        }

        Logger.Info(
            "ZoneDoodadPlacementCatalog world={0} cells={1} doodadFiles={2} placements={3} templates={4}",
            worldName, cellDirs, files, placements, byTemplate.Count);

        if (byTemplate.Count == 0)
            return EmptyIndex;

        var frozen = new Dictionary<uint, IReadOnlyList<DoodadPlacement>>(byTemplate.Count);
        foreach (var (id, list) in byTemplate)
            frozen[id] = list;
        return frozen;
    }

    private static readonly IReadOnlyDictionary<uint, IReadOnlyList<DoodadPlacement>> EmptyIndex =
        new Dictionary<uint, IReadOnlyList<DoodadPlacement>>();

    private static List<DoodadPlacement> ParsePlacements(string path, int cellX, int cellY)
    {
        var text = File.ReadAllText(path);
        var blocks = SplitDoodadBlocks().Split(text);
        var list = new List<DoodadPlacement>(Math.Max(32, blocks.Length / 4));
        var originX = cellX * 1024f;
        var originY = cellY * 1024f;

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block))
                continue;

            var typeMatch = TypeRegex().Match(block);
            var posMatch = PosRegex().Match(block);
            if (!typeMatch.Success || !posMatch.Success)
                continue;
            if (!uint.TryParse(typeMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var type) ||
                type == 0)
                continue;
            if (!TryF(posMatch.Groups[1].Value, out var lx) ||
                !TryF(posMatch.Groups[2].Value, out var ly) ||
                !TryF(posMatch.Groups[3].Value, out var lz))
                continue;

            var yawDeg = 0f;
            var oriMatch = OriRegex().Match(block);
            if (oriMatch.Success &&
                TryF(oriMatch.Groups[1].Value, out var ox) &&
                TryF(oriMatch.Groups[2].Value, out var oy) &&
                TryF(oriMatch.Groups[3].Value, out var oz) &&
                TryF(oriMatch.Groups[4].Value, out var ow))
            {
                yawDeg = YawDegreesFromOri(ox, oy, oz, ow);
            }

            list.Add(new DoodadPlacement(type, originX + lx, originY + ly, lz, yawDeg));
        }

        return list;
    }

    private static bool TryF(string s, out float v) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    [GeneratedRegex(@"^(\d+)_(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex CellFolderRegex();

    [GeneratedRegex(@"type\s+(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex TypeRegex();

    [GeneratedRegex(
        @"pos\s*\(\s*x\s+([-\d.eE+]+)\s*,\s*y\s+([-\d.eE+]+)\s*,\s*z\s+([-\d.eE+]+)\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex PosRegex();

    [GeneratedRegex(
        @"ori\s*\(\s*x\s+([-\d.eE+]+)\s*,\s*y\s+([-\d.eE+]+)\s*,\s*z\s+([-\d.eE+]+)\s*,\s*w\s+([-\d.eE+]+)\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex OriRegex();

    [GeneratedRegex(@"(?m)^doodad\r?\n", RegexOptions.CultureInvariant)]
    private static partial Regex SplitDoodadBlocks();
}
