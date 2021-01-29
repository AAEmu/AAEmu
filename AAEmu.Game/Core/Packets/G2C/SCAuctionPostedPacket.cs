using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C
{
    class SCAuctionPostedPacket : GamePacket
    {
        private readonly AuctionItem item;
        public SCAuctionPostedPacket(AuctionItem auctionItem) : base(SCOffsets.SCAuctionPostedPacket, 1)
        {
            item = auctionItem;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(item.ID);
            stream.Write(item.Duration);
            stream.Write(item.ItemID);
            stream.Write(item.ObjectID);
            stream.Write(item.Grade);
            stream.Write((byte)item.Flags);
            stream.Write(item.StackSize);
            stream.Write(item.DetailType);
            stream.Write(DateTime.Now);
            stream.Write(item.LifespanMins);
            stream.Write(item.Type1);
            stream.Write(item.WorldId);
            stream.Write(DateTime.Now);
            stream.Write(DateTime.Now);
            stream.Write(item.WorldId2);
            stream.Write(item.ClientId);
            stream.Write(item.ClientName);
            stream.Write(item.StartMoney);
            stream.Write(item.DirectMoney);
            var Random = new Random();
            var offsett = item.TimeLeft + (ulong)Random.Next(0, 10);
            stream.Write(offsett);
            stream.Write(item.GameServerID);
            stream.Write(item.BidderId);
            stream.Write(item.BidderName);
            stream.Write(item.BidMoney);
            stream.Write(item.Extra);
            return stream;
        }
    }
}
