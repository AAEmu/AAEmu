using System.Collections.Concurrent;
using System.Collections.Generic;

using AAEmu.Commons.Utils;

namespace AAEmu.Game.Core.Network.Connections
{
    public class StreamConnectionTable : Singleton<StreamConnectionTable>
    {
        private ConcurrentDictionary<uint, StreamConnection> _connections;

        private StreamConnectionTable()
        {
            _connections = new ConcurrentDictionary<uint, StreamConnection>();
        }

        public void AddConnection(StreamConnection con)
        {
            _connections.TryAdd(con.Id, con);
        }

        public StreamConnection GetConnection(uint id)
        {
            _connections.TryGetValue(id, out var con);
            return con;
        }

        public StreamConnection RemoveConnection(uint id)
        {
            _connections.TryRemove(id, out var con);
            return con;
        }

        public List<StreamConnection> GetConnections()
        {
            return new List<StreamConnection>(_connections.Values);
        }
    }
}
