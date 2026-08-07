using System.Diagnostics;
using System.Text.RegularExpressions;
using AAEmu.Web.Models;
using Microsoft.Extensions.Options;

namespace AAEmu.Web.Services;

/// <summary>
/// The outcome of a launch attempt. <see cref="Success"/> means the process was started, not that
/// the client reached the login screen — that takes another minute or so.
/// </summary>
/// <param name="Success">Whether the client process was started.</param>
/// <param name="Message">A message suitable for showing to the user.</param>
public readonly record struct LaunchResult(bool Success, string Message);

public interface IClientLauncher
{
    /// <summary>Whether launching is configured and turned on.</summary>
    bool Enabled { get; }

    /// <summary>
    /// Starts the game client authenticated as <paramref name="username"/> via the launcher
    /// passport flow, so the login server auto-creates the account if it does not exist.
    /// </summary>
    LaunchResult Launch(string username);
}

public partial class ClientLauncher(IOptions<ClientLauncherOptions> options, ILogger<ClientLauncher> logger)
    : IClientLauncher
{
    private readonly ClientLauncherOptions _options = options.Value;

    // The same shape LoginController.UsernameRegex accepts. Account names come from the database
    // rather than from the request, so this is defence in depth rather than the primary guard.
    [GeneratedRegex(@"^[\p{L}\p{Nd}_.\-@]{1,32}$")]
    private static partial Regex UsernameRegex();

    public bool Enabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ExecutablePath);

    public LaunchResult Launch(string username)
    {
        if (!Enabled)
            return new LaunchResult(false, "Launching the client is disabled. See ClientLauncher in Config.Local.json.");

        if (!UsernameRegex().IsMatch(username))
            return new LaunchResult(false, "That account name cannot be passed to the client.");

        if (!File.Exists(_options.ExecutablePath))
        {
            logger.LogWarning("Client executable not found at {Path}.", _options.ExecutablePath);
            return new LaunchResult(false, $"The client was not found at {_options.ExecutablePath}.");
        }

        // The client resolves its data relative to its own directory, so launch_aaemu.bat cds into
        // Bin64 before starting it. Reproduce that here.
        var workingDirectory = Path.GetDirectoryName(_options.ExecutablePath);
        if (string.IsNullOrEmpty(workingDirectory))
            return new LaunchResult(false, "The configured client path has no parent directory.");

        // Unlike launch_aaemu.bat, no running client is terminated first — launching several
        // accounts side by side is the point of the button.
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            WorkingDirectory = workingDirectory,
            // No shell: arguments are passed as a list, so an account name can never be
            // interpreted as part of a command.
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-devmode");
        startInfo.ArgumentList.Add(_options.DevMode);
        startInfo.ArgumentList.Add($"-StrUserName={username}");
        startInfo.ArgumentList.Add($"-strUserToken={_options.UserToken}");
        startInfo.ArgumentList.Add($"-sIp={_options.AuthIp}");
        startInfo.ArgumentList.Add($"-sPort={_options.AuthPort}");
        startInfo.ArgumentList.Add($"-gameId={_options.GameId}");
        // Deliberately no -serverId / -selectedServerId: without them the client stops on world
        // select, which lets it outlive a world restart instead of needing a full restart.
        startInfo.ArgumentList.Add("+locale");
        startInfo.ArgumentList.Add(_options.Locale);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return new LaunchResult(false, "The client process could not be started.");

            logger.LogInformation("Started client process {ProcessId} as {Username} against {Ip}:{Port}.",
                process.Id, username.ReplaceLineEndings(" "), _options.AuthIp, _options.AuthPort);

            return new LaunchResult(true,
                $"Client launching as \"{username}\". It takes about a minute to reach the login screen.");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not start the client as {Username}.", username.ReplaceLineEndings(" "));
            return new LaunchResult(false, $"The client could not be started: {e.Message}");
        }
    }
}
