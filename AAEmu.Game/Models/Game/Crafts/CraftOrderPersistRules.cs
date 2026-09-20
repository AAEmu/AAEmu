namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>
/// Board persistence: which rows survive a load, what the next id is, and when a listing
/// has lapsed. Unix seconds so a MySQL DateTime Kind cannot shift the expiry day.
/// </summary>
public static class CraftOrderPersistRules
{
    /// <summary>A listing whose expiry is at or before now is no longer on the board.</summary>
    public static bool IsExpired(long expiresUnix, long nowUnix) => expiresUnix <= nowUnix;

    public static bool IsExpired(CraftOrder order, long nowUnix) =>
        order != null && IsExpired(order.ExpiresUnix, nowUnix);

    /// <summary>The next unused id after a load. An empty board starts at 1.</summary>
    public static ulong NextId(IEnumerable<ulong> ids)
    {
        ulong max = 0;
        if (ids != null)
        {
            foreach (var id in ids)
            {
                if (id > max)
                    max = id;
            }
        }

        return max + 1;
    }

    /// <summary>Live rows a load should put back on the board.</summary>
    public static IReadOnlyList<CraftOrder> KeepOnLoad(IEnumerable<CraftOrder> rows, long nowUnix)
    {
        if (rows == null)
            return [];

        return rows.Where(order => !IsExpired(order, nowUnix)).ToList();
    }
}
