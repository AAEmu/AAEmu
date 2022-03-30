﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBidAuctionPacket : GamePacket
    {
        public CSBidAuctionPacket() : base(CSOffsets.CSBidAuctionPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();
            // TODO struct

            _log.Warn("BidAuction, NpcObjId: {0}", npcObjId);
        }
    }
}
