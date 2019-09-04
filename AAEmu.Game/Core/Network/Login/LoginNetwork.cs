using System;
using System.Net;
using AAEmu.Commons.Network.Type;
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
            _client = new Client(new IPEndPoint(IPAddress.Parse(config.Host), config.Port));
            _client.SetHandler(_handler);
            _client.Start();
        }

        public void Stop()
        {
            if (_client.IsStarted)
                _client.Stop();
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
