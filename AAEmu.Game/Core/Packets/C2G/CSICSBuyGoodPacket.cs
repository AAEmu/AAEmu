using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSICSBuyGoodPacket : GamePacket
    {
        public CSICSBuyGoodPacket() : base(CSOffsets.CSICSBuyGoodPacket, 1)
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
                thisChar.SendErrorMessage(ErrorMessageType.IngameShopFindCharacterNameFail);
                thisChar.SendPacket(new SCICSBuyResultPacket(false, buyMode, receiverName, 0));
                return;
            }

            if (buyList.Count <= 0)
            {
                thisChar.SendErrorMessage(ErrorMessageType.BuyCartEmpty);
                Connection.ActiveChar.SendPacket(new SCICSBuyResultPacket(false, buyMode, receiverName, 0));
                return;
            }

            // TODO: aaPoints, Loyalty and other currencies

            // Check Credits
            var thisCharCredits = CashShopManager.Instance.GetAccountCredits(Connection.AccountId);
            if (totalCost > thisCharCredits)
            {
                thisChar.SendErrorMessage(ErrorMessageType.IngameShopNotEnoughAaCash); // Not sure if this is the correct error
                Connection.ActiveChar.SendPacket(new SCICSBuyResultPacket(false, buyMode, receiverName, 0));
                return;
            }

            foreach(var ci in buyList)
            {
                if (CashShopManager.Instance.RemoveCredits(Connection.AccountId, (int)ci.Price))
                {
                    var items = new List<Item>();
                    // TODO: Add grade option to the cash shop items to be able to overwrite the grades ?
                    items.Add(ItemManager.Instance.Create(ci.ItemTemplateId, (int)(ci.BuyCount + ci.BonusCount), 0, true));
                    var mail = new CommercialMail(targetChar.Id, targetChar.Name, thisChar.Name, items, targetChar != thisChar, false, ci.CashName);
                    mail.FinalizeMail();
                    if (!mail.Send())
                    {
                        // Something went wrong here
                        if (!CashShopManager.Instance.AddCredits(Connection.AccountId, (int)ci.Price))
                        {
                            //Need to make sure this never happens somehow..
                            _log.Error($"Failed to restore credits for failed delivery to AccountId: {Connection.AccountId} for Credits: {ci.Price}");
                        }
                        targetChar.SendErrorMessage(ErrorMessageType.IngameShopFindCharacterNameFail); // This is the wrong error, but likely the most fitting for now
                    }
                    // TODO: Add purchase logs
                }
            }
            Connection.SendPacket(new SCICSCashPointPacket(CashShopManager.Instance.GetAccountCredits(Connection.AccountId)));

            _log.Info($"ICSBuyGood {Connection.ActiveChar.Name} -> {targetChar.Name}");

            Connection.ActiveChar.SendPacket(new SCICSBuyResultPacket(true, buyMode, receiverName, totalCost));
        }
    }
}
