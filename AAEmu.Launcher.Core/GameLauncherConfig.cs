namespace AAEmu.Launcher.Core;

/// <summary>
/// Shared configuration for launching game clients. Passed once to <see cref="GameLauncher"/>;
/// per-launch credentials are supplied on each <c>Launch*</c> call.
/// </summary>
public record GameLauncherConfig
{
    /// <summary>Path to the game executable (e.g. archeage.exe).</summary>
    public required string GameExePath { get; init; }

    /// <summary>Login server hostname or IP.</summary>
    public required string LoginServerHost { get; init; }

    /// <summary>Login server port.</summary>
    public required int LoginServerPort { get; init; }

    /// <summary>Client locale (e.g. "en_us").</summary>
    public string? Locale { get; init; }

    /// <summary>Extra command-line arguments to pass to the game.</summary>
    public string? ExtraArguments { get; init; }

    /// <summary>
    /// How long to wait for the game client to read the authentication ticket before
    /// giving up in <see cref="GameLaunchHandle.DisposeAsync()"/>. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
