﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSHeroAbstainPacket : GamePacket
    {
        public CSHeroAbstainPacket() : base(CSOffsets.CSHeroAbstainPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSHeroAbstainPacket");
        }
    }
}
