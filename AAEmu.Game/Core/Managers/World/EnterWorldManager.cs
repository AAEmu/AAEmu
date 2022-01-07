using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Login;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.G2L;
using AAEmu.Game.Core.Packets.Proxy;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Skills;
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
                case 0: // выход из игры, quit game
                case 1: // выход к списку персонажей, go to character select
                    if (connection.State == GameState.World)
                    {
                        // Say goodbye if player is quitting (but not going to character select)
                        if (type == 0)
                            connection.ActiveChar?.SendMessage(ChatType.System, AppConfiguration.Instance.World.LogoutMessage);

                        int logoutTime = 10000; // in ms

                        // Make it 5 minutes if you're still in combat
                        if (connection.ActiveChar?.IsInCombat ?? false)
                            logoutTime *= 30;
                        
                        connection.SendPacket(new SCPrepareLeaveWorldPacket(logoutTime, type, false));

                        connection.LeaveTask = new LeaveWorldTask(connection, type);
                        TaskManager.Instance.Schedule(connection.LeaveTask, TimeSpan.FromMilliseconds(logoutTime));
                    }

                    break;
                case 2: // выбор сервера, server select
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
                    _log.Warn("[Leave] Unknown type: {0}", type);
                    break;
            }
        }
    }
}
