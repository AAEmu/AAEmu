using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Network.Connections;

public class LoginConnectionTable : Singleton<LoginConnectionTable>
{
    private readonly ConcurrentDictionary<ConnectionId, LoginConnection> _connections = [];

    public void AddConnection(LoginConnection con)
    {
        _connections.TryAdd(con.Id, con);
    }

    public LoginConnection? GetConnection(ConnectionId id)
    {
        _connections.TryGetValue(id, out var con);
        return con;
    }

    public LoginConnection? RemoveConnection(ConnectionId id)
    {
        _connections.TryRemove(id, out var con);
        return con;
    }

    public List<LoginConnection> GetConnections()
    {
        return [.. _connections.Values];
    }
}
