using System.Diagnostics;

namespace AAEmu.Launcher.Core;

/// <summary>
/// Represents a single running game client instance launched by <see cref="GameLauncher"/>.
/// Dispose to wait for the client to read the authentication ticket, then release shared memory resources.
/// </summary>
public sealed class GameLaunchHandle : IAsyncDisposable
{
    private readonly SharedMemoryTicketSession _session;
    private readonly TimeSpan _timeout;
    private readonly CancellationToken _cancellationToken;
    private bool _disposed;

    /// <summary>The started game client process.</summary>
    public Process Process { get; }

    internal GameLaunchHandle(Process process, SharedMemoryTicketSession session, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Process = process;
        _session = session;
        _timeout = timeout;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Waits for the game client to read the ticket (using the configured timeout and cancellation token), then
    /// releases resources.
    /// </summary>
    public ValueTask DisposeAsync() => DisposeAsync(_cancellationToken);

    /// <summary>
    /// Waits for the game client to read the ticket, then releases resources.
    /// Cancelling <paramref name="cancellationToken"/> stops the wait early; resources are always released.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async ValueTask DisposeAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await _session.WaitForGameReadAsync(_timeout, cancellationToken);
        }
        finally
        {
            _session.Dispose();
        }
    }
}
