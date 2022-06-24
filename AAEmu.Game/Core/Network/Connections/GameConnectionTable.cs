using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Core.Network.Connections
{
    public class GameConnectionTable : Singleton<GameConnectionTable>, IGameConnectionTable
    {
        private ConcurrentDictionary<uint, IGameConnection> _connections;

        private GameConnectionTable()
        {
            _connections = new ConcurrentDictionary<uint, IGameConnection>();
        }

        public void AddConnection(IGameConnection con)
        {
            _connections.TryAdd(con.Id, con);
        }

        public IGameConnection GetConnection(uint id)
        {
            _connections.TryGetValue(id, out var con);
            return con;
        }

        public IGameConnection RemoveConnection(uint id)
        {
            _connections.TryRemove(id, out var con);
            return con;
        }

        public List<IGameConnection> GetConnections()
        {
            return new List<IGameConnection>(_connections.Values);
        }

        public IGameConnection GetConnectionByAccount(uint accountId)
        {
            var connectionInfo = _connections.Where(c => c.Value.AccountId == accountId).ToList();
            if (connectionInfo.Count >= 1)
                return connectionInfo[0].Value;
            return null;
        }
    }
}
