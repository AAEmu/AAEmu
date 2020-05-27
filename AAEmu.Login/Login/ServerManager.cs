using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using AAEmu.Commons.DI;
using AAEmu.Login.Models;
using AAEmu.Login.Utils;

namespace AAEmu.Login.Login
{
    public interface IServerManager
    {
        IEnumerable<GameServerInstance> GameServers { get; }

        void Initialize();
        GameServerInstance GetServer(int id);
        bool AddReconnectionToken(byte gsId, ulong accountId, uint token);
        bool ReconnectContainsKey(byte gsId, ulong accountId, uint token);
    }

    public sealed class ServerManager : ISingletonService, IServerManager
    {
        private readonly AuthContext _context;
        private readonly ConcurrentDictionary<byte, ConcurrentDictionary<uint, ulong>> _tokens; // gsId, [token, accountId]
        
        public IEnumerable<GameServerInstance> GameServers { get; private set; }

        public ServerManager(AuthContext context)
        {
            _context = context;
            _tokens = new ConcurrentDictionary<byte, ConcurrentDictionary<uint, ulong>>();
        }

        public void Initialize()
        {
            GameServers = _context
                .GetServers()
                .Select(s => new GameServerInstance(s))
                .ToImmutableList();
            
            Parallel.ForEach(GameServers, x => _tokens
                .TryAdd((byte) x.GameServer.Id, new ConcurrentDictionary<uint, ulong>())
            );
        }

        public GameServerInstance GetServer(int id)
        {
            return GameServers.First(gs => gs.GameServer.Id == id);
        }

        public bool AddReconnectionToken(byte gsId, ulong accountId, uint token)
        {
            if (_tokens.TryGetValue(gsId, out var pool))
                return pool.TryAdd(token, accountId);
            return false;
        }

        public bool ReconnectContainsKey(byte gsId, ulong accountId, uint token)
        {
            if (!_tokens.TryGetValue(gsId, out var pool))
                return false;

            if (pool.ContainsKey(token) && pool.TryGetValue(token, out var id))
            {
                var result = id == accountId;
                if (result)
                    pool.TryRemove(token, out _);

                return result;
            }

            return false;
        }
    }
}
