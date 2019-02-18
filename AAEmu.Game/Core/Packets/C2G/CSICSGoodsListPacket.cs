using AAEmu.Commons.Network;
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
            var mainTabId = stream.ReadByte();
            var subTabId = stream.ReadByte();
            var page = stream.ReadUInt16();
            
            _log.Warn("ICSGoodsList, MainTabId: {0}, SubTabId: {1}, Page: {2}", mainTabId, subTabId, page);

            var item = new CashShopItem();
            item.CashShopId = 20100010;
            item.CashName = "Костюм копейщицы северного Мейра";
            item.MainTab = 1;
            item.SubTab = 2;
            item.LevelMin = 1;
            item.LevelMax = 50;
            item.ItemTemplateId = 29181;
            item.Price = 950;

            var itemDetail = new CashShopItemDetail();
            itemDetail.CashShopId = 20100010;
            itemDetail.CashUniqId = 201000101;
            itemDetail.ItemTemplateId = 29181;
            itemDetail.ItemCount = 1;
            itemDetail.DefaultFlag = 1;
            itemDetail.Price = 950;
            
            Connection.SendPacket(new SCICSGoodListPacket(true, 1, item));
            Connection.SendPacket(new SCICSGoodDetailPacket(true, itemDetail));
        }
    }
}
