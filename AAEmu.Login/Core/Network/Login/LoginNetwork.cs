using System;
using System.Net;
using AAEmu.Commons.Network.Type;
using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Packets.C2L;
using AAEmu.Login.Core.Packets.L2C;
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

            RegisterPacket(0x01, typeof(CARequestAuthPacket)); // TODO +---
            RegisterPacket(0x02, typeof(CARequestAuthTencentPacket));
            RegisterPacket(0x03, typeof(CARequestAuthGameOnPacket));
            RegisterPacket(0x04, typeof(CARequestAuthMailRuPacket)); // TODO +
            RegisterPacket(0x05, typeof(CAChallengeResponsePacket));
            RegisterPacket(0x06, typeof(CAChallengeResponse2Packet));
            RegisterPacket(0x07, typeof(CAOtpNumberPacket));
            RegisterPacket(0x09, typeof(CAPcCertNumberPacket));
            RegisterPacket(0x0a, typeof(CAListWorldPacket)); // TODO +
            RegisterPacket(0x0b, typeof(CAEnterWorldPacket)); // TODO +
            RegisterPacket(0x0c, typeof(CACancelEnterWorldPacket));
            RegisterPacket(0x0d, typeof(CARequestReconnectPacket)); // TODO +
        }

        public void Start()
        {
            var config = AppConfiguration.Instance.Network;
            _server = new Server(
                new IPEndPoint(config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port),
                10);
            _server.SetHandler(_handler);
            _server.Start();

            _log.Info("Network started");
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