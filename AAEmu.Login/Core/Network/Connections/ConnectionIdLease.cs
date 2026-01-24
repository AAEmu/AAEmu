using AAEmu.Login.Models;
using AAEmu.Login.Utils;

namespace AAEmu.Login.Core.Network.Connections;

/// <summary>
/// Represents a leased connection ID that will be returned to the pool when disposed.
/// </summary>
public readonly struct ConnectionIdLease : IDisposable
{
    private readonly IIdManager<ConnectionId> _pool;

    /// <summary>
    /// Gets the leased connection ID.
    /// </summary>
    public ConnectionId Id { get; }

    internal ConnectionIdLease(ConnectionId id, IIdManager<ConnectionId> pool)
    {
        Id = id;
        _pool = pool;
    }

    public void Dispose() => _pool.Return(Id);
}
