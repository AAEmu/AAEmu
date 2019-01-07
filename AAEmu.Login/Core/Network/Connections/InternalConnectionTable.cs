using System.Collections.Concurrent;
using AAEmu.Commons.Utils;

namespace AAEmu.Login.Core.Network.Connections
{
    public class InternalConnectionTable : Singleton<InternalConnectionTable>
    {
        private ConcurrentDictionary<uint, InternalConnection> _connections;

        private InternalConnectionTable()
        {
            _connections = new ConcurrentDictionary<uint, InternalConnection>();
        }

        public void AddConnection(InternalConnection con)
        {
            _connections.TryAdd(con.Id, con);
        }

        public InternalConnection GetConnection(uint id)
        {
            _connections.TryGetValue(id, out var con);
            return con;
        }

        public InternalConnection RemoveConnection(uint id)
        {
            _connections.TryRemove(id, out var con);
            return con;
        }
    }
}
