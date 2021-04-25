using System;
using System.Net;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Packets.G2L;
using AAEmu.Login.Models;
using NLog;

namespace AAEmu.Login.Core.Network.Internal
{
    public class InternalNetwork : Singleton<InternalNetwork>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Server _server;
        private InternalProtocolHandler _handler;

        public InternalNetwork()
        {
            _handler = new InternalProtocolHandler();

            RegisterPacket(GLOffsets.GLRegisterGameServerPacket, typeof(GLRegisterGameServerPacket));
            RegisterPacket(GLOffsets.GLPlayerEnterPacket, typeof(GLPlayerEnterPacket));
            RegisterPacket(GLOffsets.GLPlayerReconnectPacket, typeof(GLPlayerReconnectPacket));
            RegisterPacket(GLOffsets.LGRequestInfoPacket, typeof(LGRequestInfoPacket));
        }

        public void Start()
        {
            var config = AppConfiguration.Instance.InternalNetwork;
            var host =
                new IPEndPoint(config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port);

            _server = new Server(host.Address, host.Port, _handler);
            _server.Start();

            _log.Info("InternalNetwork started");
        }

        public void Stop()
        {
            if (_server.IsStarted)
                _server.Stop();

            _log.Info("InternalNetwork stoped");
        }

        public void RegisterPacket(uint type, Type classType)
        {
            _handler.RegisterPacket(type, classType);
        }
    }
}
