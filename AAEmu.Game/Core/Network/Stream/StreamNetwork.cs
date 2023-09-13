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

            RegisterPacket(CTOffsets.CTJoinPacket, typeof(CTJoinPacket));
            RegisterPacket(CTOffsets.CTRequestCellPacket, typeof(CTRequestCellPacket));
            RegisterPacket(CTOffsets.CTRequestEmblemPacket, typeof(CTRequestEmblemPacket));
            RegisterPacket(CTOffsets.CTCancelCellPacket, typeof(CTCancelCellPacket));
            RegisterPacket(CTOffsets.CTContinuePacket, typeof(CTContinuePacket));
            RegisterPacket(CTOffsets.CTUccComplexPacket, typeof(CTUccComplexPacket));
            RegisterPacket(CTOffsets.CTUccStringPacket, typeof(CTUccStringPacket));
            RegisterPacket(CTOffsets.CTUccPositionPacket, typeof(CTUccPositionPacket));
            RegisterPacket(CTOffsets.CTUccCharacterNamePacket, typeof(CTUccCharacterNamePacket));
            RegisterPacket(CTOffsets.CTQueryCharNamePacket, typeof(CTQueryCharNamePacket));
            RegisterPacket(CTOffsets.CTUploadEmblemStreamPacket, typeof(CTUploadEmblemStreamPacket));
            RegisterPacket(CTOffsets.CTEmblemStreamUploadStatusPacket, typeof(CTEmblemStreamUploadStatusPacket));
            RegisterPacket(CTOffsets.CTStartUploadEmblemStreamPacket, typeof(CTStartUploadEmblemStreamPacket));
            RegisterPacket(CTOffsets.CTEmblemStreamDownloadStatusPacket, typeof(CTEmblemStreamDownloadStatusPacket));
            RegisterPacket(CTOffsets.CTItemUccPacket, typeof(CTItemUccPacket));
            RegisterPacket(CTOffsets.CTEmblemPartDownloadedPacket, typeof(CTEmblemPartDownloadedPacket));
            RegisterPacket(CTOffsets.CTUccComplexCheckValidPacket, typeof(CTUccComplexCheckValidPacket));
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
            if (_server?.IsStarted ?? false)
                _server.Stop();
            _log.Info("StreamNetwork stoped");
        }

        private void RegisterPacket(uint type, Type classType)
        {
            _handler.RegisterPacket(type, classType);
        }
    }
}
