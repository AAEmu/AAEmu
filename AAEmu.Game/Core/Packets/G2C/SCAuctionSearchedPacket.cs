using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C
{
    class SCAuctionSearchedPacket : GamePacket
    {
        private List<AuctionItem> _auctionItems;
        private uint _page;
        private uint _count;
        //private ushort _errorMessage;
        //private ulong  _serverTIme;

        public SCAuctionSearchedPacket(List<AuctionItem> auctionItems, uint page) : base(SCOffsets.SCAuctionSearchedPacket, 1)
        {
            _auctionItems = auctionItems;
            _count = (uint)_auctionItems.Count();
            _page = page; 
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_page);
            stream.Write(_count);
            Random random = new Random();

            if (_count > 0)
            {
                foreach (var item in _auctionItems)
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
                    var offset = (ulong)random.Next(0, 10); //Adds offset to timeleft to prevent client from guessing it. 
                    stream.Write(item.TimeLeft + offset);
                    stream.Write(item.GameServerID);
                    stream.Write(item.BidderId);
                    stream.Write(item.BidderName);
                    stream.Write(item.BidMoney);
                    stream.Write(item.Extra);
                }
            }
            stream.Write((ushort)0);
            stream.Write((ulong)TimeManager.Instance.GetTime());
            return stream;
        }
    }
}
