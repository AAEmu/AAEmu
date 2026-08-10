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

    /// <summary>
    /// Resolves the offer a buy request refers to. The client does not echo
    /// <c>merchant_goods.grade_id</c> back for stock whose grade is not player-visible, so an exact
    /// grade match cannot be required: no pack in the 10.0 content lists the same template twice, and
    /// when only one offer exists the requested grade carries no information. The strict path is kept
    /// first so a pack that does list several grades stays unambiguous.
    /// </summary>
    public MerchantGoodsItem GetItem(uint itemTemplateId, byte grade)
    {
        var exact = Items.FirstOrDefault(item => item.ItemTemplateId == itemTemplateId && item.Grade == grade);
        if (exact != null)
            return exact;

        MerchantGoodsItem sole = null;
        foreach (var item in Items)
        {
            if (item.ItemTemplateId != itemTemplateId)
                continue;
            if (sole != null)
                return null; // Several grades offered, the request has to name one of them exactly.
            sole = item;
        }

        return sole;
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

    /// <summary>Raw <c>merchant_goods.grade_id</c>, used only to tell several offers apart.</summary>
    public byte Grade { get; init; }

    /// <summary>
    /// Grade the buyer actually ends up holding. Item containers force the template grade on
    /// non-gradable items, so stacking and free-slot math have to use this rather than <see cref="Grade"/>.
    /// </summary>
    public byte GrantedGrade { get; init; }

    public int Cost { get; init; }
    public ShopCurrencyType Currency { get; init; }
    public MerchantPurchaseType PurchaseType { get; init; }
    public int PurchaseLimit { get; init; }
}

/// <summary>
/// One entry of <c>Data/ui_merchant_shops.json</c>: which merchant pack the client is showing when it
/// opens a shop from its own UI, keyed by the open type it stamps on <c>CSBuyItems</c>. Open type 0 is
/// a world shop and never appears here.
/// </summary>
public class UiMerchantShop
{
    public byte OpenType { get; set; }
    public uint MerchantPackId { get; set; }

    /// <summary>Free text, so the file is readable. Ignored by the server.</summary>
    public string Name { get; set; }
}

/// <summary>
/// Open type to merchant pack, for shops the client opens from its own UI. A request carrying an open
/// type that is not mapped here is refused: the open type is client-supplied, so the only thing that
/// establishes a UI shop as reachable is an entry the server was configured with.
/// </summary>
public class UiMerchantShopMap
{
    private readonly Dictionary<byte, uint> _packIdByOpenType = [];

    public int Count => _packIdByOpenType.Count;

    public void Clear()
    {
        _packIdByOpenType.Clear();
    }

    /// <summary>
    /// Records one mapping. Open type 0 means a world shop and a pack id of 0 is not a pack, so neither
    /// is accepted; both would otherwise turn a malformed request into a reachable shop.
    /// </summary>
    public bool TryAdd(byte openType, uint merchantPackId)
    {
        if (openType == 0 || merchantPackId == 0)
            return false;

        _packIdByOpenType[openType] = merchantPackId;
        return true;
    }

    /// <summary>Mapped pack id, or 0 when this open type has no entry.</summary>
    public uint GetMerchantPackId(byte openType)
    {
        return _packIdByOpenType.GetValueOrDefault(openType);
    }
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
