using AAEmu.Game.Models;

namespace AAEmu.Game.Models.Game.PlotAuctions;

/// <summary>
/// Pure decisions for the plot auction: content parsing, phase, ranking and the bid floor.
/// No effects, no clock reads — tests drive these directly.
/// </summary>
public static class PlotAuctionRules
{
    /// <summary>
    /// Parses a shipped "a|b|c" triple (start_price "28|0|100", one rewards entry "1|23490|1").
    /// </summary>
    public static bool TryParseTriple(string raw, out uint first, out uint second, out uint third)
    {
        first = second = third = 0;
        var parts = (raw ?? string.Empty).Split('|');
        if (parts.Length != 3)
            return false;
        if (!uint.TryParse(parts[0], out first) ||
            !uint.TryParse(parts[1], out second) ||
            !uint.TryParse(parts[2], out third))
            return false;
        return true;
    }

    /// <summary>
    /// Parses the shipped schedule cell "year|month|day|hour|minute". The rows carry no zone
    /// marker, so the instant is normalised through <see cref="ServerCalendar.AsUtc"/>: an
    /// Unspecified stamp becomes UTC instead of being silently read as server-local time.
    /// </summary>
    public static bool TryParseSchedule(string raw, out DateTime utc)
    {
        utc = default;
        var parts = (raw ?? string.Empty).Split('|');
        if (parts.Length != 5)
            return false;
        if (!int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var month) ||
            !int.TryParse(parts[2], out var day) ||
            !int.TryParse(parts[3], out var hour) ||
            !int.TryParse(parts[4], out var minute))
            return false;
        try
        {
            utc = ServerCalendar.AsUtc(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Splits the shipped rewards cell "type|id|count;type|id|count" into entries.
    /// </summary>
    public static bool TryParseRewards(string raw, out List<(uint RewardType, uint ItemId, uint Count)> rewards)
    {
        rewards = [];
        if (string.IsNullOrWhiteSpace(raw))
            return true; // no rewards is legal content: the auction pays but prizes nothing (settlement logs it)
        foreach (var group in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParseTriple(group, out var type, out var itemId, out var count))
            {
                rewards = [];
                return false;
            }

            rewards.Add((type, itemId, count));
        }

        return true;
    }

    /// <summary>
    /// The lifecycle window, from the config's own timestamps (the client derives its
    /// PlotAuctionPhase from the same three cells — limited_auction_tab.lua lines 121-141):
    /// none → preview at preview_start → bid at bid_start → none at bid_end.
    /// </summary>
    public static PlotAuctionPhase Phase(DateTime nowUtc, PlotAuctionConfig config)
    {
        var now = ServerCalendar.AsUtc(nowUtc);
        if (now < ServerCalendar.AsUtc(config.PreviewStartUtc))
            return PlotAuctionPhase.None;
        if (now < ServerCalendar.AsUtc(config.BidStartUtc))
            return PlotAuctionPhase.Preview;
        if (now < ServerCalendar.AsUtc(config.BidEndUtc))
            return PlotAuctionPhase.Bid;
        return PlotAuctionPhase.None;
    }

    /// <summary>
    /// The lowest acceptable next bid: <c>floor(base * (10000 + bid_increase_pct) / 10000)</c>,
    /// basis points exactly as the client computes it
    /// (CalcDisplayPrice, limited_auction_tab.lua lines 199-206).
    /// </summary>
    public static long NextBidFloor(long basePrice, int bidIncreasePct)
    {
        if (basePrice < 0 || bidIncreasePct < 0)
            return 0;
        var scaled = basePrice * (10000L + bidIncreasePct);
        return scaled / 10000L;
    }

    /// <summary>
    /// Standing bids in ranking order: amount desc, then the older bid wins a tie, then the
    /// lower character id so the order is total (the wire gives no other tie-break).
    /// </summary>
    public static List<PlotAuctionBid> Ranked(IEnumerable<PlotAuctionBid> bids) =>
        bids
            .OrderByDescending(b => b.Amount)
            .ThenBy(b => b.BidTimeUtc)
            .ThenBy(b => b.CharacterId)
            .ToList();
}
