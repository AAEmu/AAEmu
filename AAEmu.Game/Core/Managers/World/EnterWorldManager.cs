using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Login;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.G2L;
using AAEmu.Game.Core.Packets.Proxy;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Tasks;
using NLog;

namespace AAEmu.Game.Core.Managers.World
{
    public class EnterWorldManager : Singleton<EnterWorldManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, uint> _accounts;

        protected EnterWorldManager()
        {
            _accounts = new Dictionary<uint, uint>();
        }

        public void AddAccount(uint accountId, uint connectionId)
        {
            var connection = LoginNetwork.Instance.GetConnection();
            var gsId = AppConfiguration.Instance.Id;

            if (AccountManager.Instance.Contains(accountId))
                connection.SendPacket(new GLPlayerEnterPacket(connectionId, gsId, 1));
            else
            {
                _accounts.Add(connectionId, accountId);
                connection.SendPacket(new GLPlayerEnterPacket(connectionId, gsId, 0));
            }
        }

        public void Login(GameConnection connection, uint accountId, uint token)
        {
            if (_accounts.ContainsKey(token))
            {
                if (_accounts[token] == accountId)
                {
                    _accounts.Remove(token);

                    connection.AccountId = accountId;
                    connection.State = GameState.Lobby;

                    AccountManager.Instance.Add(connection);
                    StreamManager.Instance.AddToken(connection.AccountId, connection.Id);

                    var port = AppConfiguration.Instance.StreamNetwork.Port;
                    var gm = connection.GetAttribute("gmFlag") != null;
                    connection.SendPacket(new X2EnterWorldResponsePacket(0, gm, connection.Id, port));
                    connection.SendPacket(new ChangeStatePacket(0));
                }
                else
                {
                    // TODO ...
                }
            }
            else
            {
                // TODO ...
            }
        }

        public void Leave(GameConnection connection, byte type)
        {
            switch (type)
            {
                case 0: // выход из игры
                case 1: // выход к списку персонажей
                    if (connection.State == GameState.World)
                    {
                        connection.SendPacket(new SCPrepareLeaveWorldPacket(10000, type, false));

                        connection.LeaveTask = new LeaveWorldTask(connection, type);
                        TaskManager.Instance.Schedule(connection.LeaveTask, TimeSpan.FromSeconds(10));
                    }

                    break;
                case 2: // выбор сервера
                    if (connection.State == GameState.Lobby)
                    {
                        var gsId = AppConfiguration.Instance.Id;
                        LoginNetwork
                            .Instance
                            .GetConnection()
                            .SendPacket(new GLPlayerReconnectPacket(gsId, connection.AccountId, connection.Id));
                    }

                    break;
                default:
                    _log.Info("[Leave] Unknown type: {0}", type);
                    break;
            }
        }
    }
}
