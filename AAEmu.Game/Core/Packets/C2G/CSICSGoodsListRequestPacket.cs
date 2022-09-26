using System;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSICSGoodsListRequestPacket : GamePacket
    {
        public CSICSGoodsListRequestPacket() : base(CSOffsets.CSICSGoodsListRequestPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mainTabId = stream.ReadByte(); // mianType
            var subTabId = stream.ReadByte();  // subType
            var page = stream.ReadByte();      // page short in 1.2, byte in 3+

            var items = CashShopManager.Instance.GetCashShopItems(mainTabId, subTabId, page);
            var featured = mainTabId == 1 && subTabId == 1; // Im sure there is another way to check this..
            var maxPerPage = featured ? 4 : 8;
            var numPages = (byte)Math.Ceiling((float)items.Count / maxPerPage);
            var pageItems = items.Skip(maxPerPage * (page - 1)).Take(maxPerPage).ToList();

            var i = 0;
            foreach (var item in pageItems)
            {
                i++;
                var itemDetail = CashShopManager.Instance.GetCashShopItemDetail(item.CashShopId);
                var end = i >= pageItems.Count;
                Connection.SendPacket(new SCICSGoodsListPacket(end, (byte)i, numPages, item));
                Connection.SendPacket(new SCICSGoodsDetailPacket(end, itemDetail));
            }
        }
    }
}
