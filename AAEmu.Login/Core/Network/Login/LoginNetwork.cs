using System;
using System.Net;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Packets.C2L;
using AAEmu.Login.Models;
using NLog;

namespace AAEmu.Login.Core.Network.Login
{
    public class LoginNetwork : Singleton<LoginNetwork>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Server _server;
        private LoginProtocolHandler _handler;

        private LoginNetwork()
        {
            _handler = new LoginProtocolHandler();

            RegisterPacket(0x01, typeof(CARequestAuthPacket)); // требует клиент 3.5.1.4 tw
            //RegisterPacket(0x02, typeof(CARequestAuthTencentPacket));
            //RegisterPacket(0x03, typeof(CARequestAuthGameOnPacket));
            //RegisterPacket(0x05, typeof(CARequestAuthMailRuPacket));
            //RegisterPacket(0x05, typeof(CAChallengeResponsePacket));
            //RegisterPacket(0x08, typeof(CAOtpNumberPacket));
            //RegisterPacket(0x0a, typeof(CAPcCertNumberPacket));
            //RegisterPacket(0x0d, typeof(CACancelEnterWorldPacket));
            RegisterPacket(0x06, typeof(CARequestAuthMailRuPacket)); // если включен русский язык в лаунчере
            RegisterPacket(0x0C, typeof(CAListWorldPacket));
            RegisterPacket(0x0D, typeof(CAEnterWorldPacket));
            RegisterPacket(0x0F, typeof(CARequestReconnectPacket));
            RegisterPacket(0x11, typeof(CARequestAuthTWPacket));
            RegisterPacket(0x05, typeof(CARequestAuthTrionPacket)); // если включен английский язык в лаунчере
        }

        public void Start()
        {
            var config = AppConfiguration.Instance.Network;
            _server = new Server(
                config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port, _handler);
            _server.Start();

            _log.Info("Network started with Number Connections of: " + config.NumConnections);
        }

        public void Stop()
        {
            if (_server.IsStarted)
                _server.Stop();

            _log.Info("Network stoped");
        }

        private void RegisterPacket(uint type, Type classType)
        {
            _handler.RegisterPacket(type, classType);
        }
    }
}
