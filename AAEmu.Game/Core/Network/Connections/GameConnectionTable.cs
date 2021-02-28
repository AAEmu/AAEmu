using System.Collections.Concurrent;
using System.Collections.Generic;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Core.Network.Connections
{
    public class GameConnectionTable : Singleton<GameConnectionTable>
    {
        private ConcurrentDictionary<ulong, GameConnection> _connections;

        private GameConnectionTable()
        {
            _connections = new ConcurrentDictionary<ulong, GameConnection>();
        }

        public void AddConnection(GameConnection con)
        {
            _connections.TryAdd(con.Id, con);
        }

        public GameConnection GetConnection(ulong id)
        {
            _connections.TryGetValue(id, out var con);
            return con;
        }

        public GameConnection RemoveConnection(uint id)
        {
            _connections.TryRemove(id, out var con);
            return con;
        }

        public List<GameConnection> GetConnections()
        {
            return new List<GameConnection>(_connections.Values);
        }
    }
}
