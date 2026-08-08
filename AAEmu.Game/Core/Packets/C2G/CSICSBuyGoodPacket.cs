using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.Tasks.CashShop;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSICSBuyGoodPacket() : GamePacket(CSOffsets.CSICSBuyGoodPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var thisChar = Connection.ActiveChar;
        var buyList = new List<IcsPurchase>();
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
                Logger.Warn($"{thisChar.Name} is trying to shop for invalid ShopItem: {cashShopId}");
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
                Logger.Warn(
                    $"{thisChar.Name} is trying to shop from ShopItem: {shopItem.ShopId}, but with invalid index: {detailIndex}");
                continue;
            }

            // Keep the client's detail index so the buy-result can echo it back (buyItem/remainBuyCount).
            buyList.Add(new IcsPurchase(sku, detailIndex));
        }

        var receiverName = stream.ReadString();

        var targetChar = thisChar;
        if (receiverName != string.Empty)
            targetChar = WorldManager.Instance.GetCharacter(receiverName);

        if (targetChar == null)
        {
            thisChar.SendErrorMessage(ErrorMessageType.IngameShopFindCharacterNameFail);
            thisChar.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
            return;
        }

        if (buyList.Count <= 0)
        {
            thisChar.SendErrorMessage(ErrorMessageType.BuyCartEmpty);
            thisChar.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
            return;
        }

        // Create task for the transaction, this allows handling of credits in a async manner
        TaskManager.Instance.Schedule(new CashShopBuyTask(buyMode, thisChar, targetChar, buyList), TimeSpan.FromSeconds(1));
    }
}
