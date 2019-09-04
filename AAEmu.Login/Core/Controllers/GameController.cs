using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.L2C;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Models;
using AAEmu.Login.Utils;
using NLog;

namespace AAEmu.Login.Core.Controllers
{
    public class GameController : Singleton<GameController>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<byte, GameServer> _gameServers;
        private Dictionary<byte, byte> _mirrorsId;

        public byte? GetParentId(byte gsId)
        {
            if (_mirrorsId.ContainsKey(gsId))
                return _mirrorsId[gsId];
            return null;
        }


        protected GameController()
        {
            _gameServers = new Dictionary<byte, GameServer>();
            _mirrorsId = new Dictionary<byte, byte>();
        }

        public void Load()
        {
            using (var connection = MySQL.Create())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM game_servers WHERE hidden = 0";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var gameServer = new GameServer(
                                reader.GetByte("id"),
                                reader.GetString("name"),
                                reader.GetString("host"),
                                reader.GetUInt16("port"));
                            _gameServers.Add(gameServer.Id, gameServer);
                        }
                    }
                }
            }

            _log.Info("Loaded {0} gs", _gameServers.Count);
        }

        public void Add(byte gsId, List<byte> mirrorsId, InternalConnection connection)
        {
            if (!_gameServers.ContainsKey(gsId))
            {
                connection.SendPacket(new LGRegisterGameServerPacket(GSRegisterResult.Error));
                return;
            }

            var gameServer = _gameServers[gsId];
            gameServer.Connection = connection;
            gameServer.MirrorsId.AddRange(mirrorsId);
            connection.GameServer = gameServer;
            connection.AddAttribute("gsId", gameServer.Id);
            gameServer.SendPacket(new LGRegisterGameServerPacket(GSRegisterResult.Success));

            foreach (var mirrorId in mirrorsId)
            {
                _gameServers[mirrorId].Connection = connection;
                _mirrorsId.Add(mirrorId, gsId);
            }

            _log.Info("Registered GameServer {0}", gameServer.Id);
        }

        public void Remove(byte gsId)
        {
            if (!_gameServers.ContainsKey(gsId))
                return;

            var gameServer = _gameServers[gsId];
            gameServer.Connection = null;

            foreach (var mirrorId in gameServer.MirrorsId)
            {
                if (_gameServers.ContainsKey(mirrorId))
                    _gameServers[mirrorId].Connection = null;

                _mirrorsId.Remove(mirrorId);
            }

            gameServer.MirrorsId.Clear();
        }

        public async void RequestWorldList(LoginConnection connection)
        {
            if (_gameServers.Values.Any(x => x.Active))
            {
                var gameServers = _gameServers.Values.ToList();
                var (requestIds, task) = RequestController.Instance.Create(gameServers.Count, 20000); // TODO Request 20s
                for (var i = 0; i < gameServers.Count; i++)
                {
                    var value = gameServers[i];
                    if (!value.Active)
                        continue;
                    var chars = !connection.Characters.ContainsKey(value.Id);
                    value.SendPacket(
                        new LGRequestInfoPacket(connection.Id, requestIds[i], chars ? connection.AccountId : 0));
                }

                await task;
                connection.SendPacket(new ACWorldListPacket(gameServers, connection.GetCharacters()));
            }
            else
            {
                var gsList = new List<GameServer>(_gameServers.Values);
                connection.SendPacket(new ACWorldListPacket(gsList, connection.GetCharacters()));
            }
        }

        public void SetLoad(byte gsId, byte load)
        {
            lock (_gameServers)
            {
                _gameServers[gsId].Load = (GSLoad)load;
            }
        }

        public void RequestEnterWorld(LoginConnection connection, byte gsId)
        {
            if (!_gameServers.ContainsKey(gsId))
                return;
            var gs = _gameServers[gsId];
            if (!gs.Active)
                return;
            gs.SendPacket(new LGPlayerEnterPacket(connection.AccountId, connection.Id));
        }

        public void EnterWorld(LoginConnection connection, byte gsId, byte result)
        {
            if (result == 0)
            {
                if (_gameServers.ContainsKey(gsId))
                {
                    connection.SendPacket(new ACWorldCookiePacket((int)connection.Id, _gameServers[gsId]));
                }
                else
                {
                    // TODO ...
                }
            }
            else if (result == 1)
            {
                connection.SendPacket(new ACEnterWorldDeniedPacket(0)); // TODO change reason
            }
            else
            {
                // TODO ...
            }
        }
    }
}
