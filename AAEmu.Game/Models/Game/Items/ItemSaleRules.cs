using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>Why a vendor refused to take an item, or that it did not.</summary>
public enum ItemSaleRefusal
{
    /// <summary>The sale is allowed.</summary>
    None = 0,

    /// <summary><c>items.sellable</c> is off. The item cannot be handed to a vendor at all.</summary>
    NotSellable,

    /// <summary>
    /// <c>items.auction_only</c> is set: the item's only market is the auction house, so a vendor
    /// may never take it.
    /// </summary>
    AuctionOnly,

    /// <summary>
    /// <c>items.one_time_sale</c> is set and the day's one sale of this item is already spent.
    /// </summary>
    OneTimeSaleExhausted,

    /// <summary>
    /// <c>items.limited_sale_count</c> is set and the day's sales of this item have reached it.
    /// </summary>
    LimitedSaleExhausted
}

/// <summary>Whether a vendor may take an item today, and how many more of it they may take.</summary>
/// <param name="Allowed">True when the sale may go ahead.</param>
/// <param name="Refusal">Why it may not; <see cref="ItemSaleRefusal.None"/> when it may.</param>
/// <param name="Remaining">How many more sales the day still allows, or -1 when the day is unlimited.</param>
public readonly record struct ItemSaleDecision(bool Allowed, ItemSaleRefusal Refusal, int Remaining);

/// <summary>
/// The sale limits an item template carries.
/// <para>
/// Four columns of <c>items</c> decide whether a vendor may take a given item on a given day:
/// <c>sellable</c>, <c>auction_only</c>, <c>one_time_sale</c> and <c>limited_sale_count</c>. The
/// last two are not additive - they are two ways of writing the same daily allowance, and the shipped
/// rows show which wins: of the twelve rows that set <c>one_time_sale</c>, eleven also set
/// <c>limited_sale_count</c> to 1 and the twelfth sets it to 0, and a limit of 0 on the
/// <c>limited_sale_count</c> column means "no limit" everywhere else. So <c>one_time_sale</c> is
/// the narrower statement and it has to win, or the one row that sets it to 0 would be sellable
/// forever. A hundred and nineteen further rows set only <c>limited_sale_count</c>.
/// </para>
/// <para>
/// The count is per day, not per lifetime: the allowance is spent again after the daily sale reset.
/// </para>
/// </summary>
public static class ItemSaleRules
{
    /// <summary>Returned as <see cref="ItemSaleDecision.Remaining"/> when the day puts no limit on the item.</summary>
    public const int Unlimited = -1;

    /// <summary>
    /// Decides whether a vendor may take one stack of an item, given how many of that item the day
    /// has already taken.
    /// </summary>
    /// <param name="template">The item template being sold.</param>
    /// <param name="soldToday">How many sales of this item template the day has already recorded.</param>
    public static ItemSaleDecision Evaluate(ItemTemplate template, int soldToday)
    {
        if (template is null)
            return new ItemSaleDecision(false, ItemSaleRefusal.NotSellable, 0);

        if (!template.Sellable)
            return new ItemSaleDecision(false, ItemSaleRefusal.NotSellable, 0);

        // An auction-only item has no vendor market at all, whatever its sale columns say: the
        // auction house is its only route out of the character's bag.
        if (template.AuctionOnly)
            return new ItemSaleDecision(false, ItemSaleRefusal.AuctionOnly, 0);

        var limit = DailySaleLimit(template);
        if (limit == Unlimited)
            return new ItemSaleDecision(true, ItemSaleRefusal.None, Unlimited);

        var remaining = limit - Math.Max(0, soldToday);
        if (remaining <= 0)
        {
            return new ItemSaleDecision(false,
                template.OneTimeSale ? ItemSaleRefusal.OneTimeSaleExhausted : ItemSaleRefusal.LimitedSaleExhausted,
                0);
        }

        return new ItemSaleDecision(true, ItemSaleRefusal.None, remaining);
    }

    /// <summary>
    /// How many times the day allows this item to be sold to a vendor, or <see cref="Unlimited"/>.
    /// A negative <c>limited_sale_count</c> is content this build cannot read as a limit and is
    /// treated as no limit, the same way the column's own 0 is.
    /// </summary>
    public static int DailySaleLimit(ItemTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        // one_time_sale is the narrower of the two and wins outright, including over the
        // limited_sale_count of 0 that means "no limit" on its own column.
        if (template.OneTimeSale)
            return 1;

        return template.LimitedSaleCount > 0 ? template.LimitedSaleCount : Unlimited;
    }

    /// <summary>
    /// Whether the item template carries any per-day sale limit at all. An item that does not is
    /// not counted, so the sale counter never grows an entry for the overwhelming majority of the
    /// catalogue.
    /// </summary>
    public static bool HasDailySaleLimit(ItemTemplate template) => DailySaleLimit(template) != Unlimited;
}
