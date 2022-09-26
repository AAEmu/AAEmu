﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSReturnMailPacket : GamePacket
    {
        public CSReturnMailPacket() : base(CSOffsets.CSReturnMailPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mailId = stream.ReadInt64();

            _log.Debug("ReturnMail, mailId: {0}", mailId);
            Connection.ActiveChar.Mails.ReturnMail(mailId);
        }
    }
}
