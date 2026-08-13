namespace AAEmu.World.Core.Relay;

/// <summary>
/// Resolves the CS/SC Game content root for World-hosted Game.Main from deployment config.
/// </summary>
/// <remarks>
/// Authoritative sources only: non-empty <c>GameContentRoot</c>, else the World process
/// <c>baseDirectory</c> when it is already a complete Game output. No repository layout or
/// target-framework path discovery.
/// </remarks>
public static class GameContentRootResolver
{
    public static string Resolve(
        string? configuredRoot,
        string baseDirectory,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null)
    {
        directoryExists ??= Directory.Exists;
        fileExists ??= File.Exists;

        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var configured = Path.GetFullPath(configuredRoot.Trim());
            if (!directoryExists(configured))
            {
                throw new DirectoryNotFoundException(
                    $"GameContentRoot '{configured}' does not exist. " +
                    "Set GameContentRoot to a complete AAEmu.Game output.");
            }

            if (!IsBootableGameContent(configured, fileExists, directoryExists))
            {
                throw new DirectoryNotFoundException(
                    $"GameContentRoot '{configured}' is incomplete " +
                    "(need Config + Configurations/ + Data/compact.sqlite3).");
            }

            return configured;
        }

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            var bas = Path.GetFullPath(baseDirectory);
            if (directoryExists(bas) && IsBootableGameContent(bas, fileExists, directoryExists))
                return bas;
        }

        throw new DirectoryNotFoundException(
            "Cannot find a bootable AAEmu.Game content root. " +
            "Set World GameContentRoot (or run from a Game output that includes " +
            "Config + Configurations/ + Data/compact.sqlite3).");
    }

    /// <summary>Minimum tree Game.Program can load without DirectoryNotFoundException.</summary>
    public static bool IsBootableGameContent(
        string root,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null) =>
        HasGameConfigs(root, fileExists)
        && HasConfigurationsDirectory(root, directoryExists)
        && HasCompactDatabase(root, fileExists);

    /// <summary>Bootable root that also ships the TowerDefs overlay.</summary>
    public static bool IsPreferredGameContent(
        string root,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null) =>
        IsBootableGameContent(root, fileExists, directoryExists)
        && HasTowerDefsOverlay(root, fileExists);

    public static bool HasConfigurationsDirectory(string root, Func<string, bool>? directoryExists = null)
    {
        directoryExists ??= Directory.Exists;
        return directoryExists(Path.Combine(root, "Configurations"));
    }

    public static bool HasTowerDefsOverlay(string root, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        return fileExists(Path.Combine(root, "Configurations", "TowerDefs.json"));
    }

    public static bool HasCompactDatabase(string root, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        return fileExists(Path.Combine(root, "Data", "compact.sqlite3"));
    }

    public static bool HasGameConfigs(string root, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        return fileExists(Path.Combine(root, "Config.json"))
               || fileExists(Path.Combine(root, "Config.Local.json"))
               || fileExists(Path.Combine(root, "Game.Config.json"));
    }
}
