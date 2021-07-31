﻿using System.Collections.Generic;
using System.Threading;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L
{
    public class GLRegisterGameServerPacket : InternalPacket
    {
        public GLRegisterGameServerPacket() : base(GLOffsets.GLRegisterGameServerPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var secretKey = stream.ReadString();
            if (secretKey == AppConfiguration.Instance.SecretKey)
            {
                var gsId = stream.ReadByte();
                var additionalesCount = stream.ReadInt32();
                var mirrors = new List<byte>();
                for (var i = 0; i < additionalesCount; i++)
                    mirrors.Add(stream.ReadByte());

                GameController.Instance.Add(gsId, mirrors, Connection);
            }
            else
            {
                _log.Error("Connection {0}, bad secret key", Connection.Ip);
                Thread.Sleep(5000);
                Connection.SendPacket(new LGRegisterGameServerPacket(GSRegisterResult.Error));
            }
        }
    }
}
