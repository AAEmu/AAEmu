﻿using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Core.Packets.G2L
{
    public class GLPlayerReconnectPacket : InternalPacket
    {
        public GLPlayerReconnectPacket() : base(0x02)
        {
        }

        public override void Read(PacketStream stream)
        {
            var gsId = stream.ReadByte();
            var accountId = stream.ReadUInt64();
            var token = stream.ReadUInt32();

            LoginController.Instance.AddReconnectionToken(Connection, gsId, accountId, token);
        }
    }
}
