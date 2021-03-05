﻿using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CARequestReconnectPacket : LoginPacket
    {
        public CARequestReconnectPacket() : base(0x0e)
        {}

        public override void Read(PacketStream stream)
        {
            var pFrom = stream.ReadUInt32();
            var pTo = stream.ReadUInt32();
            var accountId = stream.ReadUInt64();
            var gsId = stream.ReadByte(); // wid
            var cookie = stream.ReadUInt32();
            var macLength = stream.ReadUInt16();
            var mac = stream.ReadBytes(macLength);

            LoginController.Instance.Reconnect(Connection, gsId, accountId, cookie);
        }
    }
}
