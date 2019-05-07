using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSICSBuyGoodPacket : GamePacket
    {
        public CSICSBuyGoodPacket() : base(0x11d, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var cashShopItems = CashShopManager.Instance.GetCashShopItems();
            
            var numBuys = stream.ReadByte();
            for (var i = 0; i < numBuys; i++)
            {
                var cashShopId = stream.ReadUInt32();
                var mainTab = stream.ReadByte();
                var subTab = stream.ReadByte();
                var detailIndex = stream.ReadByte();

                var cashItem = cashShopItems.Find(a => a.CashShopId == cashShopId);

                if (cashItem != null) { 
                    var item = ItemManager.Instance.Create(cashItem.ItemTemplateId, 1, 0);
                    InventoryHelper.AddItemAndUpdateClient(Connection.ActiveChar, item);
                }


            }

            var receiverName = stream.ReadString();


            //_log.Warn("ICSBuyGood");

            Connection.ActiveChar.SendPacket(new SCICSBuyResultPacket(true, 1, "Test", 5555));
        }
    }
}
