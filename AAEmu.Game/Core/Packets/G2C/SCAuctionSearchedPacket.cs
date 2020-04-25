using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Auction.Templates;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.G2C
{
    class SCAuctionSearchedPacket : GamePacket
    {
        private List<AuctionItem> _auctionItems;
        private uint Page;
        private uint Count;
        private ushort ErrorMessage;
        private ulong  ServerTime;

        public SCAuctionSearchedPacket(List<AuctionItem> auctionItems) : base(SCOffsets.SCAuctionSearchedPacket, 1)
        {
            _auctionItems = auctionItems;
            Count = (uint)_auctionItems.Count();
            _log.Warn(Count);
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((uint)0); //TODO Page#
            stream.Write(Count); //TODO Count

            if(Count > 0)
            {
                foreach (var item in _auctionItems)
                {
                    stream.Write(item.ID);
                    stream.Write(item.Duration);
                    stream.Write(item.ItemID);
                    stream.Write(item.ObjectID);
                    stream.Write(item.Grade);
                    stream.Write(item.Flags);
                    stream.Write(item.StackSize);
                    stream.Write(item.DetailType);
                    stream.Write(item.CreationTime);
                    stream.Write(item.LifespanMins);
                    stream.Write(item.Type1);
                    stream.Write(item.WorldId);
                    stream.Write(item.UnsecureDateTime);
                    stream.Write(item.UnpackDateTIme);
                    stream.Write(item.WorldId2);
                    stream.Write(item.Type2);
                    stream.Write(item.ClientName);
                    stream.Write(item.StartMoney);
                    stream.Write(item.DirectMoney);
                    stream.Write(item.Asked);
                    stream.Write(item.BidWorldID);
                    stream.Write(item.Type3);
                    stream.Write(item.BidderName);
                    stream.Write(item.BidMoney);
                    stream.Write(item.Extra);
                }
            }
            stream.Write((ushort)0); //TODO ERRORMESSAGE
            stream.Write((ulong)TimeManager.Instance.GetTime()); //TODO SERVERTIME
            return stream;
        }
    }
}
