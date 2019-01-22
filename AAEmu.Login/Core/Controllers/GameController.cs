using System.Collections.Generic;
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

        protected GameController()
        {
            _gameServers = new Dictionary<byte, GameServer>();
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
                            var gameServer = new GameServer(reader.GetByte("id"), reader.GetString("name"));
                            _gameServers.Add(gameServer.Id, gameServer);
                        }
                    }
                }
            }

            _log.Info("Loaded {0} gs", _gameServers.Count);
        }

        public void Add(byte gsId, string ip, ushort port, InternalConnection connection)
        {
            if (!_gameServers.ContainsKey(gsId))
            {
                connection.SendPacket(new LGRegisterGameServerPacket(GSRegisterResult.Error));
                return;
            }

            var gameServer = _gameServers[gsId];
            gameServer.Ip = ip;
            gameServer.Port = port;
            gameServer.Connection = connection;
            connection.GameServer = gameServer;
            connection.AddAttribute("gsId", gameServer.Id);
            gameServer.SendPacket(new LGRegisterGameServerPacket(GSRegisterResult.Success));
            _log.Info("Registered GameServer {0}", gameServer.Id);
        }

        public void Remove(byte gsId)
        {
            if (!_gameServers.ContainsKey(gsId))
                return;

            var gameServer = _gameServers[gsId];
            gameServer.Ip = "";
            gameServer.Port = 0;
            gameServer.Connection = null;
        }

        public void RequestWorldList(LoginConnection connection)
        {
            var gsList = new List<GameServer>(_gameServers.Values);
            connection.SendPacket(new ACWorldListPacket(gsList, connection.GetCharacters()));
        }

        public void SetLoad(byte gsId, byte load)
        {
            lock (_gameServers)
            {
                _gameServers[gsId].Load = (GSLoad) load;
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
                    connection.SendPacket(new ACWorldCookiePacket((int) connection.Id, _gameServers[gsId]));
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