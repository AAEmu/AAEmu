using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C
{
    class SCAuctionLowestPricePacket : GamePacket
    {
        private readonly AuctionItem _auctionItem;

        public SCAuctionLowestPricePacket(AuctionItem auctionItem) : base(SCOffsets.SCAuctionLowestPricePacket, 1)
        {
            _auctionItem = auctionItem;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(0); //Type1?
            stream.Write(0); //Type2?
            if (_auctionItem != null)
                stream.Write(_auctionItem.DirectMoney);
            else
                stream.Write(0);
            return stream;
        }
    }
}
