﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDeleteMailPacket : GamePacket
    {
        public CSDeleteMailPacket() : base(CSOffsets.CSDeleteMailPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mailId = stream.ReadInt64();
            var isSent = stream.ReadBoolean();

            Connection.ActiveChar.Mails.DeleteMail(mailId, isSent);
        }
    }
}
