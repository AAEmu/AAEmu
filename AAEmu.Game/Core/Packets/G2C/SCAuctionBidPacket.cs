using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C
{
    class SCAuctionBidPacket : GamePacket
    {
        private readonly AuctionItem _auctionItem;
        public SCAuctionBidPacket(AuctionItem auctionItem) : base(SCOffsets.SCAuctionBidPacket, 1)
        {
            _auctionItem = auctionItem;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_auctionItem.ID);
            stream.Write(_auctionItem.GameServerID);
            stream.Write(_auctionItem.DetailType);
            stream.Write(_auctionItem.BidderName);
            stream.Write(_auctionItem.BidMoney);
            stream.Write(_auctionItem.Duration);
            stream.Write(_auctionItem.ItemID);
            return stream;
        }
    }
}
