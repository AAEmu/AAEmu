using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.L2C;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Models;
using AAEmu.Login.Utils;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Login.Core.Controllers
{
    public class LoginController : Singleton<LoginController>
    {
        private Dictionary<byte, Dictionary<uint, uint>> _tokens; // gsId, [token, accountId]
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private static bool _autoAccount = AppConfiguration.Instance.AutoAccount;
        protected LoginController()
        {
            _tokens = new Dictionary<byte, Dictionary<uint, uint>>();
        }

        /// <summary>
        /// Kr Method Auth
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="username"></param>
        public static void Login(LoginConnection connection, string username)
        {
            using (var connect = MySQL.Create())
            {
                using (var command = connect.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM users where username=@username";
                    command.Parameters.AddWithValue("@username", username);
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            connection.SendPacket(new ACLoginDeniedPacket(2));
                            return;
                        }

                        // TODO ... validation password

                        connection.AccountId = reader.GetUInt32("id");
                        connection.AccountName = username;
                        connection.LastLogin = DateTime.Now;
                        connection.LastIp = connection.Ip;

                        connection.SendPacket(new ACJoinResponsePacket(0, 6));
                        connection.SendPacket(new ACAuthResponsePacket(connection.AccountId, 6));
                    }
                }
            }
        }

        /// <summary>
        /// Eu Method Auth
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="username"></param>
        /// <param name="password"></param>
        public static void Login(LoginConnection connection, string username, IEnumerable<byte> password)
        {
            using (var connect = MySQL.Create())
            {
                using (var command = connect.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM users where username=@username";
                    command.Parameters.AddWithValue("@username", username);
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            if (_autoAccount)
                            {
                                reader.Close();
                                CreateAndLoginInvalid(connection, username, password, connect);
                            }
                            else
                            {
                                connection.SendPacket(new ACLoginDeniedPacket(2));
                            }
                            
                            return;
                        }

                        var pass = Convert.FromBase64String(reader.GetString("password"));
                        if (!pass.SequenceEqual(password))
                        {
                            connection.SendPacket(new ACLoginDeniedPacket(2));
                            return;
                        }

                        connection.AccountId = reader.GetUInt32("id");
                        connection.AccountName = username;
                        connection.LastLogin = DateTime.Now;
                        connection.LastIp = connection.Ip;

                        connection.SendPacket(new ACJoinResponsePacket(0, 6));
                        connection.SendPacket(new ACAuthResponsePacket(connection.AccountId, 6));
                    }
                }
            }
        }

        public static void CreateAndLoginInvalid(LoginConnection connection, string username, IEnumerable<byte> password, MySqlConnection connect)
        {
            var pass = Convert.ToBase64String(password.ToArray());

            using (var command = connect.CreateCommand())
            {
                command.CommandText = "INSERT into users (username, password, email, last_ip) VALUES (@username, @password, \"\", \"\")";
                command.Parameters.AddWithValue("@username", username);
                command.Parameters.AddWithValue("@password", pass);
                command.Prepare();

                if (command.ExecuteNonQuery() != 1)
                {
                    connection.SendPacket(new ACLoginDeniedPacket(2));
                    return;
                }

                _log.Debug("Created account from invalid username login with value:" + username);
                Login(connection, username, password);
            }
        }

        public void AddReconnectionToken(InternalConnection connection, byte gsId, uint accountId, uint token)
        {
            if (!_tokens.ContainsKey(gsId))
                _tokens.Add(gsId, new Dictionary<uint, uint>());

            _tokens[gsId].Add(token, accountId);
            connection.SendPacket(new LGPlayerReconnectPacket(token));
        }

        public void Reconnect(LoginConnection connection, byte gsId, uint accountId, uint token)
        {
            if (!_tokens.ContainsKey(gsId))
            {
                var parentId = GameController.Instance.GetParentId(gsId);
                if (parentId != null)
                    gsId = (byte)parentId;
                else
                {
                    // TODO ...
                    return;
                }
            }

            if (!_tokens[gsId].ContainsKey(token))
            {
                // TODO ...
                return;
            }

            if (_tokens[gsId][token] == accountId)
            {
                connection.AccountId = accountId;
                connection.SendPacket(new ACJoinResponsePacket(0, 6));
                connection.SendPacket(new ACAuthResponsePacket(connection.AccountId, 6));
            }
            else
            {
                // TODO ...
            }
        }
    }
}
