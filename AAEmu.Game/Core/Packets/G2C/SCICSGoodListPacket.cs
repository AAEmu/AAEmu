using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSGoodListPacket(bool pageEnd, ushort totalPage, byte mainTab, byte subTab, IcsItem item)
    : GamePacket(SCOffsets.SCICSGoodListPacket, 5)
{
    private readonly IcsSku _firstSku = item.FirstSku;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(pageEnd);
        stream.Write(totalPage);

        stream.Write(item.ShopId);
        stream.Write(item.Name);
        stream.Write(mainTab);
        stream.Write(subTab);
        stream.Write(item.LevelMin);
        stream.Write(item.LevelMax);
        stream.Write(item.DisplayItemId);
        stream.Write(item.IsSale);
        stream.Write(item.IsHidden);
        stream.Write((byte)item.LimitedType);
        stream.Write(item.LimitedStockMax);
        stream.Write((byte)item.BuyRestrictType);
        stream.Write(item.BuyRestrictId);
        stream.Write(item.SaleStart);
        stream.Write(item.SaleEnd);

        // stream.Write(_item); // Replaced with new code
        if (_firstSku != null)
        {
            stream.Write((byte)_firstSku.Currency);
            stream.Write(_firstSku.Price);
            stream.Write(item.Remaining);
            stream.Write(_firstSku.BonusItemId);
            stream.Write(_firstSku.BonusItemCount);
            stream.Write((byte)item.ShopButtons);
        }
        else
        {
            // No item for some reason?
            stream.Write((byte)CashShopCurrencyType.Credits);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write(0);
            stream.Write((byte)CashShopCmdUiType.AllowAll);
        }
        // stream.Write(0);
        // stream.Write(0); // In captures this is discount price
        return stream;
    }
}
