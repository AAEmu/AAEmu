using System.Collections.Concurrent;
using System.Collections.Generic;
using AAEmu.Commons.Utils;

namespace AAEmu.Login.Core.Network.Connections
{
    public class LoginConnectionTable : Singleton<LoginConnectionTable>
    {
        private ConcurrentDictionary<uint, LoginConnection> _connections;

        private LoginConnectionTable()
        {
            _connections = new ConcurrentDictionary<uint, LoginConnection>();
        }

        public void AddConnection(LoginConnection con)
        {
            _connections.TryAdd(con.Id, con);
        }

        public LoginConnection GetConnection(uint id)
        {
            _connections.TryGetValue(id, out var con);
            return con;
        }

        public LoginConnection RemoveConnection(uint id)
        {
            _connections.TryRemove(id, out var con);
            return con;
        }

        public List<LoginConnection> GetConnections()
        {
            return new List<LoginConnection>(_connections.Values);
        }
    }
}
