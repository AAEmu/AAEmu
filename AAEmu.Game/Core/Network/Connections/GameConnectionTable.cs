using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Core.Network.Connections
{
    public class GameConnectionTable : Singleton<GameConnectionTable>
    {
        private ConcurrentDictionary<uint, GameConnection> _connections;

        private GameConnectionTable()
        {
            _connections = new ConcurrentDictionary<uint, GameConnection>();
        }

        public void AddConnection(GameConnection con)
        {
            _connections.TryAdd(con.Id, con);
        }

        public GameConnection GetConnection(uint id)
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

        public GameConnection GetConnectionByAccount(uint accountId)
        {
            var connectionInfo = _connections.Where(c => c.Value.AccountId == accountId).ToList();
            if (connectionInfo.Count >= 1)
                return connectionInfo[0].Value;
            return null;
        }
    }
}
