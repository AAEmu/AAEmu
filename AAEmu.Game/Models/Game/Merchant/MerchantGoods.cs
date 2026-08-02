using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Merchant;

public class MerchantGoods(uint id, MerchantPackKind kind, uint itemPointId)
{
    public uint Id { get; set; } = id;
    public MerchantPackKind Kind { get; } = kind;
    public uint ItemPointId { get; } = itemPointId;
    public List<MerchantGoodsItem> Items { get; set; } = [];

    public bool SellsItem(uint itemTemplateId)
    {
        return Items.Any(item => item.ItemTemplateId == itemTemplateId);
    }

    public MerchantGoodsItem GetItem(uint itemTemplateId, byte grade)
    {
        return Items.FirstOrDefault(item => item.ItemTemplateId == itemTemplateId && item.Grade == grade);
    }

    public void AddItemToStock(MerchantGoodsItem item)
    {
        // The 10.0 content has one exact duplicate enabled row. Preserve one authoritative offer;
        // distinct grades for the same template are retained if future content adds them.
        if (Items.Any(existing => existing.ItemTemplateId == item.ItemTemplateId && existing.Grade == item.Grade))
            return;

        Items.Add(item);
    }
}

public class MerchantGoodsItem
{
    public uint Id { get; init; }
    public uint ItemTemplateId { get; init; }
    public byte Grade { get; init; }
    public int Cost { get; init; }
    public ShopCurrencyType Currency { get; init; }
    public MerchantPurchaseType PurchaseType { get; init; }
    public int PurchaseLimit { get; init; }
}

/// <summary>
/// Raw <c>merchant_packs.kind_id</c> values. The gaps are real in the 10.0.2.13 content.
/// </summary>
public enum MerchantPackKind : byte
{
    Money = 0,
    Honor = 1,
    Empty = 2,
    Vocation = 3,
    ItemPoint = 6,
    CustomItemPoint = 7,
}

/// <summary>Raw <c>enum_purchase_types.id</c> values.</summary>
public enum MerchantPurchaseType : byte
{
    Always = 1,
    Daily = 2,
    Weekly = 3,
    Monthly = 4,
}

public class MerchantPurchaseState
{
    public uint CharacterId { get; init; }
    public uint ItemTemplateId { get; init; }
    public int BuyCount { get; init; }
    public MerchantPurchaseType PurchaseType { get; init; }
    public DateTime PeriodStart { get; init; }
}
