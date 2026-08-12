using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Resolves the configured zone game-data root used for <c>npc_spawners.g</c> and related level files.
/// Never falls back to machine-specific absolute paths — missing config fails loudly.
/// </summary>
public static class ZoneGameDataRootResolver
{
    public const string EnvVarName = "AAEMU_ZONE_GAME_DATA_ROOT";

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static bool _missingRootWarned;

    /// <summary>
    /// Existing directory for zone level files, or null when unset / missing on disk.
    /// <see cref="EnvVarName"/> is an explicit override and is tried first; then
    /// <see cref="WorldRuntime.Config.ZoneGameDataRoot"/>. Invalid paths are skipped so the
    /// remaining candidate can still apply.
    /// </summary>
    public static string? TryGetRoot()
    {
        var root = Resolve(
            Environment.GetEnvironmentVariable(EnvVarName),
            WorldRuntime.Config?.ZoneGameDataRoot,
            Directory.Exists);

        if (root is null && !_missingRootWarned)
        {
            _missingRootWarned = true;
            Logger.Error(
                "ZoneGameDataRoot is not configured. Set World Config.ZoneGameDataRoot (or {0}) " +
                "to the extracted game data root that contains worlds/.../zone_server/npc_spawners.g",
                EnvVarName);
        }

        return root;
    }

    /// <summary>
    /// Resolve a root from an env override then a configured path. Invalid or missing candidates
    /// are skipped so a later valid candidate can still apply.
    /// </summary>
    public static string? Resolve(string? envOverride, string? configuredRoot, Func<string, bool> directoryExists)
    {
        foreach (var candidate in new[] { envOverride, configuredRoot })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string full;
            try
            {
                full = Path.GetFullPath(candidate.Trim());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ZoneGameDataRoot candidate is not a valid path: {0}", candidate);
                continue;
            }

            if (directoryExists(full))
                return full;

            Logger.Error("ZoneGameDataRoot candidate missing: {0}", full);
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
