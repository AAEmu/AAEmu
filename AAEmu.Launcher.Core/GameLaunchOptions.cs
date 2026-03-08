namespace AAEmu.Launcher.Core;

/// <summary>
/// Options for launching the game client.
/// </summary>
public record GameLaunchOptions
{
    /// <summary>Path to the game executable (e.g. archeage.exe).</summary>
    public required string GameExePath { get; init; }

    /// <summary>Login server hostname or IP.</summary>
    public required string LoginServerHost { get; init; }

    /// <summary>Login server port.</summary>
    public required int LoginServerPort { get; init; }

    /// <summary>Account username.</summary>
    public required string Username { get; init; }

    /// <summary>Account password (for password-based launch).</summary>
    public string? Password { get; init; }

    /// <summary>Game token (from HTTP API token generation).</summary>
    public string? Token { get; init; }

    /// <summary>Client locale (e.g. "en_us").</summary>
    public string? Locale { get; init; }

    /// <summary>Extra command-line arguments to pass to the game.</summary>
    public string? ExtraArguments { get; init; }
}
