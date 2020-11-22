using System;
using System.Net;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.L2G;
using AAEmu.Game.Models;

namespace AAEmu.Game.Core.Network.Login
{
    public class LoginNetwork : Singleton<LoginNetwork>
    {
        private Client _client;
        private LoginProtocolHandler _handler;
        private LoginConnection _connection;

        private LoginNetwork()
        {
            _handler = new LoginProtocolHandler();

            RegisterPacket(0x00, typeof(LGRegisterGameServerPacket));
            RegisterPacket(0x01, typeof(LGPlayerEnterPacket));
            RegisterPacket(0x02, typeof(LGPlayerReconnectPacket));
            RegisterPacket(0x03, typeof(LGRequestInfoPacket));
        }

        public void Start()
        {
            var config = AppConfiguration.Instance.LoginNetwork;
            _client = new Client(IPAddress.Parse(config.Host), config.Port, _handler);
            _client.ConnectAsync();

        }

        public void Stop()
        {
            if (_client.IsConnected)
                _client.DisconnectAsync();
        }

        public void SetConnection(LoginConnection con)
        {
            _connection = con;
        }

        public LoginConnection GetConnection()
        {
            return _connection;
        }

        private void RegisterPacket(uint type, Type classType)
        {
            _handler.RegisterPacket(type, classType);
        }
    }
}
