using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSICSGoodsListPacket : GamePacket
    {
        public CSICSGoodsListPacket() : base(0x11c, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mainTabId = stream.ReadSByte();
            var subTabId = stream.ReadSByte();
            var page = stream.ReadUInt16();

            var items = CashShopManager.Instance.GetCashShopItems(mainTabId, subTabId, page);
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var itemDetail = CashShopManager.Instance.GetCashShopItemDetail(items[i].CashShopId);
                var end = false;
                if (i <= 0) end = true;
                Connection.SendPacket(new SCICSGoodListPacket(end, 1, items[i]));
                Connection.SendPacket(new SCICSGoodDetailPacket(end, itemDetail));
            }
        }
    }
}
