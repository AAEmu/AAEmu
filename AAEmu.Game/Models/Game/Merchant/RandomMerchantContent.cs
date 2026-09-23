using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Merchant;

/// <summary>One pack of <c>merchant_random_packs</c>: refresh policy plus its groups and goods.</summary>
public class RandomMerchantPack
{
    public uint Id { get; init; }

    /// <summary><c>merchant_random_packs.kind_id</c>; drives the shop currency like <see cref="MerchantPackKind"/>.</summary>
    public MerchantPackKind Kind { get; init; }

    /// <summary><c>merchant_random_packs.item_point_id</c> - the item CustomItemPoint prices are paid in.</summary>
    public uint ItemPointId { get; init; }

    /// <summary><c>merchant_random_packs.sale_cnt</c> - offers drawn into one window.</summary>
    public int SaleCnt { get; init; }

    /// <summary><c>merchant_random_packs.refresh_use</c> - 'f' packs never allow a manual refresh (pack 3).</summary>
    public bool RefreshUse { get; init; }

    /// <summary><c>merchant_random_packs.refresh_multiply_use</c>. Semantics are unknown and every shipped row is 'f'.</summary>
    public bool RefreshMultiplyUse { get; init; }

    /// <summary><c>merchant_random_packs.refresh_free_cnt</c> - free re-rolls per window.</summary>
    public int RefreshFreeCnt { get; init; }

    /// <summary><c>merchant_random_packs.refresh_charge_cnt</c> - paid re-rolls per window.</summary>
    public int RefreshChargeCnt { get; init; }

    /// <summary><c>merchant_random_packs.refresh_currency_id</c> as an <see cref="ContentCurrencyType"/>.</summary>
    public uint RefreshCurrencyId { get; init; }

    /// <summary><c>merchant_random_packs.refresh_point</c> - price of one paid re-roll in that currency (or count of the refresh item).</summary>
    public int RefreshPoint { get; init; }

    /// <summary><c>merchant_random_packs.refresh_item_id</c> - the item a currency-6 refresh consumes.</summary>
    public uint RefreshItemId { get; init; }

    /// <summary>The window's currency, resolved from <see cref="Kind"/> at load; null marks the pack unloadable.</summary>
    public ShopCurrencyType? Currency { get; set; }

    /// <summary>Currency the refresh charge is taken in, resolved from <see cref="RefreshCurrencyId"/> at load.</summary>
    public ContentCurrencyType? RefreshCurrency { get; set; }

    /// <summary>Groups that can actually yield an offer (non-empty, positive weight), ordered by <c>group_no</c>.</summary>
    public List<RandomMerchantGroup> EligibleGroups { get; } = [];

    /// <summary>False when content validation rejected the pack; windows for it are refused loudly.</summary>
    public bool Usable { get; set; } = true;
}

/// <summary>One row of <c>merchant_random_groups</c> with its goods.</summary>
public class RandomMerchantGroup
{
    public uint Id { get; init; }

    /// <summary><c>merchant_random_groups.group_no</c> - unique per pack; display order of the offer.</summary>
    public int GroupNo { get; init; }

    /// <summary><c>merchant_random_groups.weight</c> - die weight when the window draws this group.</summary>
    public long Weight { get; init; }

    /// <summary>Goods of this group; one is drawn per window this group is selected in.</summary>
    public List<RandomMerchantGood> Goods { get; } = [];
}

/// <summary>One row of <c>merchant_random_goods</c>: the offer actually sold, price included.</summary>
public class RandomMerchantGood
{
    public uint Id { get; init; }

    /// <summary><c>merchant_random_goods.item_id</c>.</summary>
    public uint ItemId { get; init; }

    /// <summary>
    /// Resolved from <c>merchant_random_goods.grade_id</c> the same way vendor stock resolves it
    /// (NpcManager's merchant load: items.fixed_grade when set, else gradable row grade, else 0),
    /// because the client buys at the grade it resolves locally.
    /// </summary>
    public byte Grade { get; set; }

    /// <summary><c>merchant_random_goods.cost</c> - the price quoted for this offer.</summary>
    public int Cost { get; set; }

    /// <summary><c>merchant_random_goods.weight</c> - die weight inside the group.</summary>
    public long Weight { get; init; }
}
