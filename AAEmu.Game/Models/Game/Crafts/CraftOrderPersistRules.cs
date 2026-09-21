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

    /// <summary>
    /// When a lapsed listing cannot leave the board (store delete or refund mail failed), wait
    /// this long before trying again. Flooring that delay to zero re-enters the task ticker
    /// every tick with the board lock held.
    /// </summary>
    public static readonly TimeSpan ExpireRetry = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long until the next sweep. A past-due row still on the board uses
    /// <see cref="ExpireRetry"/>; otherwise the delay is until the nearest future expiry.
    /// <see cref="TimeSpan.Zero"/> means the board is empty and nothing should be armed.
    /// </summary>
    public static TimeSpan NextSweepDelay(DateTimeOffset now, IEnumerable<long> expiresUnix)
    {
        if (expiresUnix == null)
            return TimeSpan.Zero;

        var nowUnix = now.ToUnixTimeSeconds();
        var hasPastDue = false;
        long nextFuture = 0;
        foreach (var expires in expiresUnix)
        {
            if (IsExpired(expires, nowUnix))
                hasPastDue = true;
            else if (nextFuture == 0 || expires < nextFuture)
                nextFuture = expires;
        }

        if (hasPastDue)
            return ExpireRetry;

        if (nextFuture <= 0)
            return TimeSpan.Zero;

        var delay = DateTimeOffset.FromUnixTimeSeconds(nextFuture) - now;
        return delay < TimeSpan.Zero ? ExpireRetry : delay;
    }
}
