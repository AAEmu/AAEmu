using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Resolves the configured zone game-data root used for <c>npc_spawners.g</c> and related level files.
/// Never falls back to machine-specific absolute paths — missing config fails loudly.
/// </summary>
public static class ZoneGameDataRootResolver
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static bool _missingRootWarned;

    /// <summary>
    /// Configured root directory, or null when unset / missing on disk.
    /// Prefer <see cref="WorldRuntime.Config.ZoneGameDataRoot"/>; env
    /// <c>AAEMU_ZONE_GAME_DATA_ROOT</c> is an explicit override for ops.
    /// </summary>
    public static string? TryGetRoot()
    {
        var candidates = new[]
        {
            WorldRuntime.Config?.ZoneGameDataRoot,
            Environment.GetEnvironmentVariable("AAEMU_ZONE_GAME_DATA_ROOT")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var full = Path.GetFullPath(candidate.Trim());
            if (Directory.Exists(full))
                return full;

            Logger.Error(
                "ZoneGameDataRoot configured but directory missing: {0}",
                full);
            return null;
        }

        if (!_missingRootWarned)
        {
            _missingRootWarned = true;
            Logger.Error(
                "ZoneGameDataRoot is not configured. Set World Config.ZoneGameDataRoot (or AAEMU_ZONE_GAME_DATA_ROOT) " +
                "to the extracted game data root that contains worlds/.../zone_server/npc_spawners.g");
        }

        return null;
    }

    /// <summary>
    /// Absolute path to <c>npc_spawners.g</c> for a zone, or null when root/file is unavailable.
    /// </summary>
    public static string? TryResolveNpcSpawnersFile(uint zoneId, string worldName)
    {
        if (zoneId == 0 || string.IsNullOrWhiteSpace(worldName))
            return null;

        var root = TryGetRoot();
        if (root is null)
            return null;

        var path = Path.Combine(
            root,
            "worlds",
            worldName,
            "level_design",
            "zone",
            zoneId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "zone_server",
            "npc_spawners.g");

        if (File.Exists(path))
            return Path.GetFullPath(path);

        Logger.Warn(
            "npc_spawners.g missing under ZoneGameDataRoot zoneId={0} world={1} path={2}",
            zoneId, worldName, path);
        return null;
    }
}
