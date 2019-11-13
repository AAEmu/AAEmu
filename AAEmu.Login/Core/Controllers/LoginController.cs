using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.DB.Login;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.L2C;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Utils;

namespace AAEmu.Login.Core.Controllers
{
    public class LoginController : Singleton<LoginController>
    {
        private Dictionary<byte, Dictionary<uint, uint>> _tokens; // gsId, [token, accountId]

        protected LoginController()
        {
            _tokens = new Dictionary<byte, Dictionary<uint, uint>>();
        }

        /// <summary>
        /// Eu Method Auth
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="username"></param>
        /// <param name="password"></param>
        public static void Login(LoginConnection connection, string username, IEnumerable<byte> password = null)
        {
            using (var ctx = new LoginDBContext())
            {
                Users user = ctx.Users.Where(u => u.Username == username).FirstOrDefault();

                if (user == null)
                {
                    connection.SendPacket(new ACLoginDeniedPacket(2));
                }
                else
                {
                    // Check pw if supplied.
                    if (password != null)
                    {
                        byte[] pass = Convert.FromBase64String(user.Password);
                        if (!pass.SequenceEqual(password))
                        {
                            connection.SendPacket(new ACLoginDeniedPacket(2));
                            return;
                        }
                    }

                    connection.AccountId = (uint)user.Id;
                    connection.AccountName = username;
                    connection.LastLogin = DateTime.Now;
                    connection.LastIp = connection.Ip;

                    connection.SendPacket(new ACJoinResponsePacket(0, 6));
                    connection.SendPacket(new ACAuthResponsePacket(connection.AccountId, 6));
                }

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
