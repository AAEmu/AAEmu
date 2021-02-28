using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class AccountManager : Singleton<AccountManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private ConcurrentDictionary<ulong, GameConnection> _accounts;

        public AccountManager()
        {
            _accounts = new ConcurrentDictionary<ulong, GameConnection>();
        }

        public void Add(GameConnection connection)
        {
            if (_accounts.ContainsKey(connection.AccountId))
                return;
            _accounts.TryAdd(connection.AccountId, connection);
        }

        public void Remove(ulong id)
        {
            _accounts.TryRemove(id, out _);
        }

        public bool Contains(ulong id)
        {
            return _accounts.ContainsKey(id);
        }
    }
}
