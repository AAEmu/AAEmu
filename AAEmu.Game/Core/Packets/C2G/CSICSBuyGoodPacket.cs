using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.Tasks.CashShop;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSICSBuyGoodPacket : GamePacket
{
    public CSICSBuyGoodPacket() : base(CSOffsets.CSICSBuyGoodRequestPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        //var buyer = Connection.ActiveChar;
        var buyList = new List<IcsSku>();
        var thisChar = Connection.ActiveChar;
        byte buyMode = 1; // No idea what this means

        var numBuys = stream.ReadByte();
        for (var i = 0; i < numBuys; i++)
        {
            var cashShopId = stream.ReadUInt32();
            var mainTab = stream.ReadByte();
            var subTab = stream.ReadByte();
            var detailIndex = stream.ReadByte();

            if (!CashShopManager.Instance.ShopItems.TryGetValue(cashShopId, out var shopItem))
            {
                Logger.Warn($"{Connection.ActiveChar.Name} is trying to shop for invalid ShopItem: {cashShopId}");
                continue;
            }

            var idx = 0;
            IcsSku sku = null;
            foreach (var (key, detail) in shopItem.Skus)
            {
                if (idx == detailIndex)
                {
                    sku = detail;
                    break;
                }
                idx++;
            }

            if (sku == null)
            {
                Logger.Warn($"{Connection.ActiveChar.Name} is trying to shop from ShopItem: {shopItem.ShopId}, but with invalid index: {detailIndex}");
                continue;
            }

            buyList.Add(sku);
        }

        var receiverName = stream.ReadString();

        var targetChar = thisChar;
        if (receiverName != string.Empty)
            targetChar = WorldManager.Instance.GetCharacter(receiverName);

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

        // Create task for the transaction, this allows handling of credits in a async manner
        TaskManager.Instance.Schedule(new CashShopBuyTask(buyMode, Connection.ActiveChar, targetChar, buyList), TimeSpan.FromSeconds(1));
    }
}
