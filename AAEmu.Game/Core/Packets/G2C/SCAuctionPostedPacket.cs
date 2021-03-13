﻿using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C
{
    class SCAuctionPostedPacket : GamePacket
    {
        private readonly AuctionItem item;
        public SCAuctionPostedPacket(AuctionItem auctionItem) : base(SCOffsets.SCAuctionPostedPacket, 5)
        {
            item = auctionItem;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(item.ID);
            stream.Write(item.Duration);

            #region Item
            stream.Write(item.ItemID); // TemplateId
            stream.Write(item.ObjectID);
            stream.Write(item.Grade);
            stream.Write((byte)item.Flags);
            stream.Write(item.StackSize);

            #region ItemDetail
            stream.Write(item.DetailType);
            #endregion ItemDetail

            stream.Write(DateTime.Now); // creationTime
            stream.Write(item.LifespanMins);
            stream.Write(item.Type1);
            stream.Write(item.WorldId);
            stream.Write(DateTime.Now); // unsecureDateTime
            stream.Write(DateTime.Now); // unpackDateTime
            stream.Write(DateTime.Now); // chargeUseSkillTime
            #endregion Item
            
            stream.Write(item.WorldId2);
            stream.Write(item.ClientId);
            stream.Write(item.ClientName);
            stream.Write(item.StartMoney);
            stream.Write(item.DirectMoney);
            var Random = new Random();
            var offsett = item.TimeLeft + (ulong)Random.Next(0, 10);
            stream.Write(offsett); // asked
            stream.Write(0); // chargePercent
            stream.Write(item.BidWorldID);
            stream.Write(item.BidderId);
            stream.Write(item.BidderName);
            stream.Write(item.BidMoney);
            stream.Write(item.Extra);

            return stream;
        }
    }
}
