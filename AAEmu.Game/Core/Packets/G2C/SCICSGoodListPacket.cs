using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSGoodListPacket : GamePacket
{
    private readonly bool _pageEnd;
    private readonly byte _totalPage;
    private readonly IcsItem _item;
    private readonly IcsSku _firstSku;
    private readonly byte _mainTab;
    private readonly byte _subTab;
    private readonly byte _reqPage;

    public SCICSGoodListPacket(bool pageEnd, byte reqPage, byte totalPage, byte mainTab, byte subTab, IcsItem item) : base(SCOffsets.SCICSGoodListPacket, 5)
    {
        _pageEnd = pageEnd;
        _reqPage = reqPage;
        _totalPage = totalPage;
        _mainTab = mainTab;
        _subTab = subTab;
        _item = item;
        _firstSku = item.FirstSku;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_pageEnd);
        stream.Write(_reqPage); // reqPage add in 3+
        stream.Write(_totalPage);

        // CashShopItem
        stream.Write(_item.ShopId);                // cashShopId
        stream.Write(_item.Name);                  // casnName
        stream.Write(_mainTab);                    // mainTab
        stream.Write(_subTab);                     // subTab
        stream.Write(_item.LevelMin);              // levelMin
        stream.Write(_item.LevelMax);              // levelMax
        stream.Write(_item.DisplayItemId);         // ItemTemplateId
        stream.Write(_item.IsSale);                // isSell
        stream.Write(_item.IsHidden);              // isHidden
        stream.Write((byte)_item.LimitedType);     // limitType
        stream.Write(_item.LimitedStockMax);       // buyCount
        stream.Write((byte)_item.BuyRestrictType); // buyType
        stream.Write(_item.BuyRestrictId);         // buyId
        stream.Write(_item.SaleStart);             // sdate
        stream.Write(_item.SaleEnd);               // edate

        // stream.Write(_item); // Replaced with new code
        if (_firstSku != null)
        {
            stream.Write((byte)_firstSku.Currency); // type
            stream.Write(_firstSku.Price);          // price
            stream.Write(_item.Remaining);          // remain
            stream.Write(_firstSku.BonusItemId);    // bonusType
            stream.Write(_firstSku.BonusItemCount); // bonusConut
            stream.Write((byte)_item.ShopButtons);  // cmdUi
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
        stream.Write(0); // payItemType  add in 3+
        stream.Write(0); // disPrice - In captures this is discount price add in 3+
        return stream;
    }
}
