using System.Collections.Concurrent;
using AAEmu.Commons.Utils;

namespace AAEmu.Login.Core.Network.Connections;

public class InternalConnectionTable : Singleton<InternalConnectionTable>
{
    private readonly ConcurrentDictionary<uint, InternalConnection> _connections = [];

    public void AddConnection(InternalConnection con) => _connections.TryAdd(con.Id, con);

    public InternalConnection? GetConnection(uint id) => _connections.GetValueOrDefault(id);

    public InternalConnection? RemoveConnection(uint id)
    {
        _connections.TryRemove(id, out var con);
        return con;
    }
}
