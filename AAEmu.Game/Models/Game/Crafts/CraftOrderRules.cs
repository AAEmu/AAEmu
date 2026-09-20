namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>A board search as the client asked for it.</summary>
/// <param name="ActabilityGroup">Actability group to list, or 0 for every group.</param>
/// <param name="SortKind">Column: 0 posting id, 1 actability group, 2 listed fee.</param>
/// <param name="SortOrder">0 ascending, 1 descending.</param>
/// <param name="Page">Zero-based page.</param>
/// <param name="Possible">Only list orders this character can actually fill.</param>
public readonly record struct CraftOrderQuery(uint ActabilityGroup, sbyte SortKind, sbyte SortOrder, uint Page, bool Possible);

/// <summary>
/// The board's decisions, kept apart from the manager so they can be tested without a world.
/// </summary>
public static class CraftOrderRules
{
    /// <summary>Orders one character may have live at once.</summary>
    public const int EntriesPerCharacter = CraftOrderWire.LoadEntryLimit;

    /// <summary>Search sort column: posting order (the row id).</summary>
    public const sbyte SortKindDefault = 0;

    /// <summary>Search sort column: the order's actability group.</summary>
    public const sbyte SortKindActabilityGroup = 1;

    /// <summary>Search sort column: the listed fee.</summary>
    public const sbyte SortKindFee = 2;

    /// <summary>Search sort direction: low to high.</summary>
    public const sbyte SortAscending = 0;

    /// <summary>Search sort direction: high to low. The board opens on this.</summary>
    public const sbyte SortDescending = 1;

    public static bool CanPost(int liveOrders) => liveOrders < EntriesPerCharacter;

    /// <summary>
    /// Money the post dialog treats as the floor: formula <c>MinCraftOrderFee</c> (the same
    /// bind as <c>X2Craft:GetMinCraftOrderFee</c>), not <c>crafts.cost</c>.
    /// </summary>
    public static ulong MinimumFee(int formulaCopper) => (ulong)Math.Max(0, formulaCopper);

    public static bool IsFeeAcceptable(ulong fee, ulong minimum) => fee >= minimum;

    /// <summary>A craft can be ordered when the content marks it so and it produces something.</summary>
    public static bool IsOrderable(Craft craft) => craft is { Orderable: true, CraftProducts.Count: > 0 };

    /// <summary>Whether a character's actability is enough to fill an order.</summary>
    public static bool CanFill(CraftOrder order, int actabilityPoint) => actabilityPoint >= order.ActabilityPoint;

    /// <summary>Whether an order belongs on the page the client asked for.</summary>
    /// <param name="fillerActability">
    /// The character's points in <paramref name="order"/>'s own actability group.
    /// </param>
    public static bool MatchesFilter(CraftOrder order, CraftOrderQuery query, int fillerActability)
    {
        if (query.ActabilityGroup != 0 && order.ActabilityGroupId != query.ActabilityGroup)
            return false;

        return !query.Possible || CanFill(order, fillerActability);
    }

    /// <summary>
    /// The slice of matching orders the given page covers. The client's page counter is 1-based, so
    /// page 1 is the first page; page 0 is read as the first page too rather than paging backwards.
    /// </summary>
    public static IReadOnlyList<CraftOrder> PageOf(IReadOnlyList<CraftOrder> matching, uint page)
    {
        var zeroBased = page > 0 ? page - 1 : 0;
        var skip = (long)zeroBased * CraftOrderWire.SearchEntryLimit;
        if (skip >= matching.Count)
            return [];

        return matching.Skip((int)skip).Take(CraftOrderWire.SearchEntryLimit).ToList();
    }

    /// <summary>
    /// Orders a filtered list the way the search packet asked: kind picks the column, order
    /// picks the direction. An unknown kind falls back to posting id. Equal keys keep the
    /// newer row after the older one when ascending, and the reverse when descending.
    /// </summary>
    public static IReadOnlyList<CraftOrder> Sorted(IReadOnlyList<CraftOrder> matching, CraftOrderQuery query)
    {
        if (matching == null || matching.Count == 0)
            return matching ?? [];

        var descending = query.SortOrder != SortAscending;
        IOrderedEnumerable<CraftOrder> ordered = query.SortKind switch
        {
            SortKindActabilityGroup => descending
                ? matching.OrderByDescending(order => order.ActabilityGroupId)
                : matching.OrderBy(order => order.ActabilityGroupId),
            SortKindFee => descending
                ? matching.OrderByDescending(order => order.Fee)
                : matching.OrderBy(order => order.Fee),
            _ => descending
                ? matching.OrderByDescending(order => order.Id)
                : matching.OrderBy(order => order.Id)
        };

        if (query.SortKind is SortKindActabilityGroup or SortKindFee)
        {
            ordered = descending
                ? ordered.ThenByDescending(order => order.Id)
                : ordered.ThenBy(order => order.Id);
        }

        return ordered.ToList();
    }
}
