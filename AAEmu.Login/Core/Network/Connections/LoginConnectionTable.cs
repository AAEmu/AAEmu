using System.Collections.Concurrent;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Network.Connections;

public class LoginConnectionTable : ILoginConnectionTable
{
    private readonly ConcurrentDictionary<ConnectionId, ILoginConnection> _connections = [];

    public void AddConnection(ILoginConnection con)
    {
        if (!_connections.TryAdd(con.Id, con))
        {
            throw new ArgumentException("Connection with the same ID already exists.");
        }
    }

    public ILoginConnection? GetConnection(ConnectionId id) => _connections.GetValueOrDefault(id);

    public ILoginConnection? RemoveConnection(ConnectionId id)
    {
        _connections.TryRemove(id, out var con);
        return con;
    }

    public List<ILoginConnection> GetConnections() => [.. _connections.Values];
}
