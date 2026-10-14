using System.Globalization;

using NLog;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Reads a zone's <c>spawn_point.g</c> from the configured zone game-data roots. No fallback disk path.
/// </summary>
/// <remarks>
/// The file is the level pack's own record of where players arrive in a zone, so it is the authority for
/// an instance's entry point; the hand-kept <c>world_spawns.json</c> table is the older source and has no
/// entry for most instances. Missing configuration or a missing file simply yields no spawn, and the
/// caller reports it.
/// </remarks>
public static class ZoneSpawnPointGCatalog
{
    public const string FileName = "spawn_point.g";

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>The arrival point for <paramref name="worldName"/>/<paramref name="zoneKey"/> from configured roots.</summary>
    public static bool TryGetZoneSpawn(string worldName, uint zoneKey, out ZoneSpawnPointFileRules.ZoneSpawn spawn) =>
        TryGetZoneSpawn(Core.Managers.World.ZoneGameDataRoots.Enumerate(), worldName, zoneKey, out spawn);

    public static bool TryGetZoneSpawn(
        IEnumerable<string> roots,
        string worldName,
        uint zoneKey,
        out ZoneSpawnPointFileRules.ZoneSpawn spawn)
    {
        spawn = default;
        if (roots == null || string.IsNullOrWhiteSpace(worldName) || zoneKey == 0)
            return false;

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var path = Path.Combine(
                root, "worlds", worldName, "level_design", "zone",
                zoneKey.ToString(CultureInfo.InvariantCulture), "world_server", FileName);
            if (!File.Exists(path))
                continue;

            try
            {
                if (ZoneSpawnPointFileRules.TryParseFirst(File.ReadAllText(path), out spawn))
                    return true;

                Logger.Warn("spawn_point.g {0} has no positioned object", path);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to read spawn_point.g {0}", path);
            }
        }

        return false;
    }
}
