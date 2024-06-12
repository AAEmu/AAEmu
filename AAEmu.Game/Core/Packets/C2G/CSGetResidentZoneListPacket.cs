﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSGetResidentZoneListPacket : GamePacket
    {
        public CSGetResidentZoneListPacket() : base(CSOffsets.CSGetResidentZoneListPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            Logger.Debug("CSGetResidentZoneListPacket");
        }
    }
}