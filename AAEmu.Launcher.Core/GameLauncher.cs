using System.Diagnostics;
using System.Text;

namespace AAEmu.Launcher.Core;

/// <summary>
/// Launches game client processes. Multiple clients can be launched simultaneously;
/// each returns an independent <see cref="GameLaunchHandle"/>.
/// </summary>
public sealed class GameLauncher(GameLauncherConfig config)
{
    /// <summary>
    /// Launch with pre-hashed password (version 1 ticket - SHA256), embedded in a Trion auth ticket.
    /// </summary>
    /// <remarks>This method is insecure unless the protocol or transport is encrypted.</remarks>
    /// <param name="username">The username of the account.</param>
    /// <param name="hashedPassword">The plaintext password of the account.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A handle representing the game client instance that was launched.
    /// Dispose to wait for the client to read the authentication ticket, then release shared memory resources.
    /// </returns>
    public GameLaunchHandle LaunchWithHash(string username, string hashedPassword, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentException.ThrowIfNullOrEmpty(hashedPassword);
        return LaunchWithTicket(username, TicketBuilder.BuildPreHashed(username, hashedPassword), cancellationToken);
    }
    
    /// <summary>
    /// Launch with plaintext password (version 2 ticket), embedded in a Trion auth ticket.
    /// </summary>
    /// <remarks>This method is insecure unless the protocol or transport is encrypted.</remarks>
    /// <param name="username">The username of the account.</param>
    /// <param name="password">The plaintext password of the account.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A handle representing the game client instance that was launched.
    /// Dispose to wait for the client to read the authentication ticket, then release shared memory resources.
    /// </returns>
    public GameLaunchHandle LaunchWithPlaintext(string username, string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentException.ThrowIfNullOrEmpty(password);
        return LaunchWithTicket(username, TicketBuilder.BuildPlainText(username, password), cancellationToken);
    }

    /// <summary>
    /// Launch with token from HTTP API flow (version 3 ticket), embedded in a Trion auth ticket.
    /// </summary>
    /// <remarks>
    /// This method requires an HTTP API to obtain an authentication token, and for the login server to accept and
    /// decode the token.
    /// </remarks>
    /// <param name="username">The username of the account.</param>
    /// <param name="token">The authentication token.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A handle representing the game client instance that was launched.
    /// Dispose to wait for the client to read the authentication ticket, then release shared memory resources.
    /// </returns>
    public GameLaunchHandle LaunchWithToken(string username, string token, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentException.ThrowIfNullOrEmpty(token);
        return LaunchWithTicket(username, TicketBuilder.BuildToken(username, token), cancellationToken);
    }

    /// <summary>
    /// Launch with a Trion authentication ticket.
    /// </summary>
    /// <param name="username">The username of the account.</param>
    /// <param name="ticket">The full Trion authentication ticket in XML format.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A handle representing the game client instance that was launched.
    /// Dispose to wait for the client to read the authentication ticket, then release shared memory resources.
    /// </returns>
    public GameLaunchHandle LaunchWithTicket(string username, string ticket, CancellationToken cancellationToken)
    {
        var session = new SharedMemoryTicketSession(ticket);
        try
        {
            var args = BuildArguments(username, session.FileMapHandle, session.EventHandle);
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = config.GameExePath,
                    Arguments = args,
                    UseShellExecute = true,
                }
            };
            process.Start();
            return new GameLaunchHandle(process, session, config.ReadTimeout, cancellationToken);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private string BuildArguments(string username, nint fileMapHandle, nint eventHandle)
    {
        var sb = new StringBuilder();
        sb.Append($"-t +auth_ip {config.LoginServerHost} -auth_port {config.LoginServerPort}");
        sb.Append($" -handle {fileMapHandle:X}:{eventHandle:X}");
        sb.Append($" -uid {username}");

        if (!string.IsNullOrEmpty(config.Locale))
            sb.Append($" -lang {config.Locale}");

        if (!string.IsNullOrEmpty(config.ExtraArguments))
            sb.Append($" {config.ExtraArguments}");

        return sb.ToString();
    }
}
