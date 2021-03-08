using System;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSICSGoodsListPacket : GamePacket
    {
        public CSICSGoodsListPacket() : base(CSOffsets.CSICSGoodsListPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mainTabId = stream.ReadSByte();
            var subTabId = stream.ReadSByte();
            var page = stream.ReadUInt16();

            var items = CashShopManager.Instance.GetCashShopItems(mainTabId, subTabId, page);
            bool featured = (mainTabId == 1) && (subTabId == 1);//Im sure there is another way to check this..
            int maxPerPage = featured ? 4 : 8;
            var numPages = (ushort)Math.Ceiling((float)items.Count / maxPerPage);
            var pageItems = items.Skip(maxPerPage * (page - 1)).Take(maxPerPage).ToList();

            int i = 0;
            foreach(var item in pageItems)
            {
                i++;
                var itemDetail = CashShopManager.Instance.GetCashShopItemDetail(item.CashShopId);
                var end = i >= pageItems.Count();
                Connection.SendPacket(new SCICSGoodListPacket(end, numPages, item));
                Connection.SendPacket(new SCICSGoodDetailPacket(end, itemDetail));
            }
        }
    }
}
