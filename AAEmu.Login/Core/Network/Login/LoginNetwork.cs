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

            RegisterPacket(CLOffsets.CARequestAuthPacket, typeof(CARequestAuthPacket));
            RegisterPacket(CLOffsets.CARequestAuthTencentPacket, typeof(CARequestAuthTencentPacket));
            RegisterPacket(CLOffsets.CARequestAuthGameOnPacket, typeof(CARequestAuthGameOnPacket));
            RegisterPacket(CLOffsets.CARequestAuthTrionPacket, typeof(CARequestAuthTrionPacket));
            RegisterPacket(CLOffsets.CARequestAuthMailRuPacket, typeof(CARequestAuthMailRuPacket));
            RegisterPacket(CLOffsets.CAChallengeResponsePacket, typeof(CAChallengeResponsePacket));
            RegisterPacket(CLOffsets.CAChallengeResponse2Packet, typeof(CAChallengeResponse2Packet));
            RegisterPacket(CLOffsets.CAOtpNumberPacket, typeof(CAOtpNumberPacket));
            RegisterPacket(CLOffsets.CAPcCertNumberPacket, typeof(CAPcCertNumberPacket));
            RegisterPacket(CLOffsets.CAListWorldPacket, typeof(CAListWorldPacket));
            RegisterPacket(CLOffsets.CAEnterWorldPacket, typeof(CAEnterWorldPacket));
            RegisterPacket(CLOffsets.CACancelEnterWorldPacket, typeof(CACancelEnterWorldPacket));
            RegisterPacket(CLOffsets.CARequestReconnectPacket, typeof(CARequestReconnectPacket));
        }

        public void Start()
        {
            var config = AppConfiguration.Instance.Network;
            _server = new Server(
                config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port, _handler);
            _server.Start();

            _log.Info("Network started with Number of Connections: " + config.NumConnections);
        }

        public void Stop()
        {
            if ((_server != null) && (_server.IsStarted))
                _server.Stop();

            _log.Info("Network stopped");
        }

        private void RegisterPacket(uint type, Type classType)
        {
            _handler.RegisterPacket(type, classType);
        }
    }
}
