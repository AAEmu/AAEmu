namespace AAEmu.Game.Models.Game.Items;

using AAEmu.Game.Models.Game.Items.Templates;

/// <summary>Why a vendor may not take an item.</summary>
public enum ItemSaleRefusal
{
    /// <summary>The vendor may take it.</summary>
    None = 0,

    /// <summary>There is no template to judge, so the sale cannot be allowed.</summary>
    NoTemplate,

    /// <summary><c>items.sellable</c> is not set, so the item may not be handed to a vendor.</summary>
    NotSellable,

    /// <summary><c>items.auction_only</c> is set, so the item may only go through the auction house.</summary>
    AuctionOnly
}

/// <summary>The vendor sale decision for one item.</summary>
public readonly record struct ItemSaleDecision(bool Allowed, ItemSaleRefusal Refusal);

/// <summary>
/// Which items a vendor may take, from the two <c>items</c> columns that describe a vendor sale.
/// </summary>
/// <remarks>
/// <para>
/// <c>sellable</c> and <c>auction_only</c> are the columns that decide this question, and the
/// shipped rows agree: the two are complementary flags on whether a vendor may take the item at all.
/// </para>
/// <para>
/// <c>one_time_sale</c> and <c>limited_sale_count</c> are deliberately <b>not</b> read here. They are
/// purchase limits, not vendor-sale allowances, and treating them as the latter was wrong twice over.
/// Every one of the twelve rows that set <c>one_time_sale</c> has <c>sellable</c> unset, so a
/// vendor-sale limit on them governs an item no vendor could take; and of the 130 rows with a
/// positive <c>limited_sale_count</c>, 118 likewise cannot be sold. Their values - 1, 3, 10, 14, 21,
/// 28, 70, 100, on blueprints, boxes and exchange tickets - read as how many may be bought, and the
/// limit belongs on the purchase path. A per-day counter shared across the server was worse than
/// merely misplaced: one player would have spent the allowance for everybody.
/// </para>
/// </remarks>
public static class ItemSaleRules
{
    /// <summary>
    /// Decides whether a vendor may take this item.
    /// </summary>
    /// <param name="template">The item template, or <c>null</c> when the item has none.</param>
    public static ItemSaleDecision Evaluate(ItemTemplate template)
    {
        // No template means nothing to judge. Allowing it would let an unidentifiable item through.
        if (template == null)
            return new ItemSaleDecision(false, ItemSaleRefusal.NoTemplate);

        if (!template.Sellable)
            return new ItemSaleDecision(false, ItemSaleRefusal.NotSellable);

        // Auction-only wins outright: the item may only reach a buyer through the auction house, so
        // no vendor may take it however sellable it otherwise is.
        if (template.AuctionOnly)
            return new ItemSaleDecision(false, ItemSaleRefusal.AuctionOnly);

        return new ItemSaleDecision(true, ItemSaleRefusal.None);
    }
}
