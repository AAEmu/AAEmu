using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.Game.Error;
using AAEmu.Game.Models.Game.Items.Actions;
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
            var buyList = new List<CashShopItem>();
            var totalCost = 0;
            var thisChar = Connection.ActiveChar;
            byte buyMode = 1; // No idea what this means
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
                    buyList.Add(cashItem);
                    totalCost += (int)cashItem.Price;
                }

            }
            var receiverName = stream.ReadString();

            var targetChar = thisChar;
            if (receiverName != string.Empty)
                targetChar =  WorldManager.Instance.GetCharacter(receiverName);
            if (targetChar == null)
            {
                // TODO: Add support for gifting (to offline players)
                thisChar.SendMessage("Target player must be online to gift!");
                Connection.ActiveChar.SendPacket(new SCICSBuyResultPacket(false, buyMode, receiverName, 0));
                return;
            }

            if (buyList.Count <= 0)
            {
                thisChar.SendErrorMessage(ErrorMessageType.BuyCartEmpty);
                Connection.ActiveChar.SendPacket(new SCICSBuyResultPacket(false, buyMode, receiverName, 0));
                return;
            }

            var thisCharAaPoints = 10000; // TODO: thisChar.aaPoints;
            if (totalCost > thisCharAaPoints)
            {
                thisChar.SendErrorMessage(ErrorMessageType.IngameShopNotEnoughAaPoint); // Not sure if this is the correct error
                Connection.ActiveChar.SendPacket(new SCICSBuyResultPacket(false, buyMode, receiverName, 0));
                return;
            }

            // TODO: Current hack to send item directly to inventory, this needs to be changed to MarketPlace Mail
            if (targetChar.Inventory.Bag.FreeSlotCount < buyList.Count)
            {
                thisChar.SendErrorMessage(ErrorMessageType.BagFull);
                Connection.ActiveChar.SendPacket(new SCICSBuyResultPacket(false, buyMode, receiverName, 0));
                return;
            }

            foreach(var ci in buyList)
            {
                if (!targetChar.Inventory.Bag.AcquireDefaultItem(ItemTaskType.StoreBuy, ci.ItemTemplateId, (int)(ci.BuyCount + ci.BonusCount)))
                {
                    // Something went wrong here
                    targetChar.SendErrorMessage(ErrorMessageType.BagFull);
                }
            }

            _log.Warn("ICSBuyGood");

            Connection.ActiveChar.SendPacket(new SCICSBuyResultPacket(true, buyMode, receiverName, totalCost));
        }
    }
}
