using System;
using System.Net;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.C2S;
using AAEmu.Game.Models;
using NLog;

namespace AAEmu.Game.Core.Network.Stream
{
    public class StreamNetwork : Singleton<StreamNetwork>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Server _server;
        private StreamProtocolHandler _handler;

        private StreamNetwork()
        {
            _handler = new StreamProtocolHandler();

            RegisterPacket(0x01, typeof(CTJoinPacket));
            RegisterPacket(0x02, typeof(CTRequestCellPacket));
//            RegisterPacket(0x03, typeof(CTRequestEmblemPacket));
            RegisterPacket(0x04, typeof(CTCancelCellPacket));
            RegisterPacket(0x05, typeof(CTContinuePacket));
//            RegisterPacket(0x06, typeof(CTUccComplexPacket));
//            RegisterPacket(0x07, typeof(CTUccStringPacket));
//            RegisterPacket(0x08, typeof(CTUccPositionPacket));
            RegisterPacket(0x09, typeof(CTUccCharacterNamePacket));
            RegisterPacket(0x0a, typeof(CTQueryCharNamePacket));
//            RegisterPacket(0x0c, typeof(CTUploadEmblemStreamPacket));
//            RegisterPacket(0x0d, typeof(CTEmblemStreamUploadStatusPacket));
//            RegisterPacket(0x0e, typeof(CTStartUploadEmblemStreamPacket));
//            RegisterPacket(0x0f, typeof(CTEmblemStreamDownloadStatusPacket));
//            RegisterPacket(0x10, typeof(CTItemUccPacket));
//            RegisterPacket(0x11, typeof(CTEmblemPartDownloadedPacket));
//            RegisterPacket(0x12, typeof(CTUccComplexCheckValidPacket));
        }

        public void Start()
        {
            var config = AppConfiguration.Instance.StreamNetwork;
            _server = new Server(config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port, _handler);
            _server.Start();
            _log.Info("StreamNetwork started");
        }

        public void Stop()
        {
            if (_server.IsStarted)
                _server.Stop();
            _log.Info("StreamNetwork stoped");
        }

        private void RegisterPacket(uint type, Type classType)
        {
            _handler.RegisterPacket(type, classType);
        }
    }
}
