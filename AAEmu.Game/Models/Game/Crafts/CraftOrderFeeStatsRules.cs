namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>One craft's recent listing-fee range, the pair the post dialog shows as lowest / highest.</summary>
public readonly record struct CraftOrderFeeStat(uint CraftId, ulong Lowest, ulong Highest);

/// <summary>
/// Recent listing fees for a craft. The post dialog asks for this on each sheet pick; a filled
/// row has already left the live board, so the range has to survive that delete.
/// </summary>
public static class CraftOrderFeeStatsRules
{
    /// <summary>Lowest and highest of the posted unit fees, or zeroes when the list is empty.</summary>
    public static (ulong Lowest, ulong Highest, bool Any) FromFees(IEnumerable<ulong> fees)
    {
        ulong? lowest = null;
        ulong? highest = null;
        if (fees != null)
        {
            foreach (var fee in fees)
            {
                lowest = lowest == null ? fee : Math.Min(lowest.Value, fee);
                highest = highest == null ? fee : Math.Max(highest.Value, fee);
            }
        }

        return lowest == null ? (0, 0, false) : (lowest.Value, highest.Value, true);
    }

    /// <summary>Fold one more posted unit fee into a stored range.</summary>
    public static (ulong Lowest, ulong Highest) Include(ulong lowest, ulong highest, bool have, ulong fee)
    {
        if (!have)
            return (fee, fee);

        return (Math.Min(lowest, fee), Math.Max(highest, fee));
    }
}
