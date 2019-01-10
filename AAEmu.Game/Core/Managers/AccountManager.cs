using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class AccountManager : Singleton<AccountManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private ConcurrentDictionary<uint, GameConnection> _accounts;

        public AccountManager()
        {
            _accounts = new ConcurrentDictionary<uint, GameConnection>();
        }

        public void Add(GameConnection connection)
        {
            if (_accounts.ContainsKey(connection.AccountId))
                return;
            _accounts.TryAdd(connection.AccountId, connection);
        }

        public void Remove(uint id)
        {
            _accounts.TryRemove(id, out _);
        }

        public bool Contains(uint id)
        {
            return _accounts.ContainsKey(id);
        }
    }
}