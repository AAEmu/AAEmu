namespace AAEmu.World.Core.Relay;

/// <summary>
/// Picks the CS/SC Game content root (Config.json + Configurations) for World-hosted Game.Main.
/// Prefers an output that includes <c>Configurations/TowerDefs.json</c> so Event Center Game-Time
/// membership is not silently empty when an older sibling Game bin is still on disk.
/// </summary>
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
            if (directoryExists(configured))
            {
                // Explicit roots must be bootable: Config + Configurations/ + compact DB.
                // Game.Program enumerates Configurations/*.json unconditionally.
                // TowerDefs overlay is preferred but warned separately when missing.
                if (IsBootableGameContent(configured, fileExists, directoryExists))
                    return configured;

                if (HasGameConfigs(configured, fileExists))
                {
                    throw new DirectoryNotFoundException(
                        $"GameContentRoot '{configured}' is incomplete (need Config + Configurations/ + Data/compact.sqlite3). " +
                        "Point GameContentRoot at a complete AAEmu.Game output.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            var bas = Path.GetFullPath(baseDirectory);
            if (directoryExists(bas) && IsPreferredGameContent(bas, fileExists, directoryExists))
                return bas;
        }

        var candidates = new List<string>();
        var dir = new DirectoryInfo(string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            foreach (var rel in new[]
                     {
                         Path.Combine("AAEmu.Game", "bin", "Debug", "net10.0"),
                         Path.Combine("AAEmu.Game", "bin", "Release", "net10.0"),
                         Path.Combine("..", "AAEmu.Game", "bin", "Debug", "net10.0"),
                     })
            {
                candidates.Add(Path.GetFullPath(Path.Combine(dir.FullName, rel)));
            }
        }

        string? withOverlay = null;
        string? withDbOnly = null;
        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!directoryExists(c) || !IsBootableGameContent(c, fileExists, directoryExists))
                continue;
            if (IsPreferredGameContent(c, fileExists, directoryExists))
                return c;
            if (withOverlay == null && HasTowerDefsOverlay(c, fileExists))
                withOverlay = c;
            if (withDbOnly == null && HasCompactDatabase(c, fileExists))
                withDbOnly = c;
        }

        if (withOverlay != null)
            return withOverlay;
        if (withDbOnly != null)
            return withDbOnly;

        throw new DirectoryNotFoundException(
            "Cannot find AAEmu.Game content root with Config + Configurations/ + Data/compact.sqlite3. " +
            "Prefer World output that copies Configurations/TowerDefs.json and place compact.sqlite3 under Data/. Tried: "
            + string.Join(", ", candidates.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>Minimum tree Game.Program can load without DirectoryNotFoundException.</summary>
    public static bool IsBootableGameContent(
        string root,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null) =>
        HasGameConfigs(root, fileExists)
        && HasConfigurationsDirectory(root, directoryExists)
        && HasCompactDatabase(root, fileExists);

    /// <summary>World/Game output that can run Event Center Game-Time and load sqlite.</summary>
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
