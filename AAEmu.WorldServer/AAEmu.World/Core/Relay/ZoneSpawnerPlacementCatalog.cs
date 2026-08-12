using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using AAEmu.Game.Core.Managers.World;
using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Zone-local npc_spawners.g placements keyed by <c>spawnerType</c> (npc_spawners template id).
/// Used when World must place event NPCs that Zone fails to emit via ZWSpawnNpc.
/// </summary>
public static partial class ZoneSpawnerPlacementCatalog
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly ConcurrentDictionary<uint, IReadOnlyList<SpawnerPlacement>> ByZone = new();

    public readonly record struct SpawnerPlacement(
        uint PlacementId,
        uint SpawnerType,
        float X,
        float Y,
        float Z,
        float ZRot);

    public static IReadOnlyList<SpawnerPlacement> GetByType(uint zoneId, uint spawnerType)
    {
        if (zoneId == 0 || spawnerType == 0)
            return [];

        var all = GetAll(zoneId);
        if (all.Count == 0)
            return [];

        List<SpawnerPlacement> match = null;
        foreach (var p in all)
        {
            if (p.SpawnerType != spawnerType)
                continue;
            match ??= [];
            match.Add(p);
        }

        return match ?? [];
    }

    /// <summary>All placements for a zone (empty when ZoneGameDataRoot / file is unavailable).</summary>
    public static IReadOnlyList<SpawnerPlacement> GetAll(uint zoneId)
    {
        if (zoneId == 0)
            return [];
        return ByZone.GetOrAdd(zoneId, LoadZone);
    }

    public static void Invalidate(uint zoneId = 0)
    {
        if (zoneId == 0)
            ByZone.Clear();
        else
            ByZone.TryRemove(zoneId, out _);
    }

    private static IReadOnlyList<SpawnerPlacement> LoadZone(uint zoneId)
    {
        var world = WorldManager.Instance.GetWorldTemplateByZoneKey(zoneId);
        if (world == null)
        {
            Logger.Warn("ZoneSpawnerPlacementCatalog: no world template for zoneId={0}", zoneId);
            return [];
        }

        var path = ZoneGameDataRootResolver.TryResolveNpcSpawnersFile(zoneId, world.Name);
        if (path is null)
            return [];

        var list = ParsePlacements(path);
        Logger.Info(
            "ZoneSpawnerPlacementCatalog zoneId={0} world={1} file={2} placements={3}",
            zoneId, world.Name, path, list.Count);
        return list;
    }

    /// <summary>Parse placements from an absolute <c>npc_spawners.g</c> path (tests / tooling).</summary>
    public static IReadOnlyList<SpawnerPlacement> ParseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("npc_spawners.g not found", path);
        return ParsePlacements(path);
    }

    private static List<SpawnerPlacement> ParsePlacements(string path)
    {
        var text = File.ReadAllText(path);
        var blocks = SplitSpawnerBlocks().Split(text);
        var list = new List<SpawnerPlacement>(Math.Max(64, blocks.Length / 4));
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block))
                continue;

            var idMatch = SpawnerIdRegex().Match(block);
            var typeMatch = SpawnerTypeRegex().Match(block);
            var posMatch = PosRegex().Match(block);
            if (!idMatch.Success || !typeMatch.Success || !posMatch.Success)
                continue;
            if (!uint.TryParse(idMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                continue;
            if (!uint.TryParse(typeMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var type))
                continue;
            if (!TryF(posMatch.Groups[1].Value, out var x) ||
                !TryF(posMatch.Groups[2].Value, out var y) ||
                !TryF(posMatch.Groups[3].Value, out var z))
                continue;

            var zRot = 0f;
            var rotMatch = ZRotRegex().Match(block);
            if (rotMatch.Success)
                TryF(rotMatch.Groups[1].Value, out zRot);

            list.Add(new SpawnerPlacement(id, type, x, y, z, zRot));
        }

        return list;
    }

    private static bool TryF(string s, out float v) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    [GeneratedRegex(@"spawnerId\s+(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex SpawnerIdRegex();

    [GeneratedRegex(@"spawnerType\s+(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex SpawnerTypeRegex();

    [GeneratedRegex(@"pos\s*\(\s*x\s+([-\d.]+)\s*,\s*y\s+([-\d.]+)\s*,\s*z\s+([-\d.]+)\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex PosRegex();

    [GeneratedRegex(@"zRot\s+([-\d.]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ZRotRegex();

    [GeneratedRegex(@"(?m)^spawner\r?\n", RegexOptions.CultureInvariant)]
    private static partial Regex SplitSpawnerBlocks();
}
