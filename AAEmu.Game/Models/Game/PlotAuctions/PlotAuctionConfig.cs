namespace AAEmu.Game.Models.Game.PlotAuctions;

/// <summary>
/// One parsed row of <c>plot_auction_config</c> (shipped compact.sqlite3). Everything on this
/// type is content: the price triple, the increment, the winner count, the reward list and the
/// preview/bid windows all come from the row, never from source.
/// </summary>
public sealed class PlotAuctionConfig
{
    /// <summary><c>plot_auction_config.id</c> — the <c>auctionConfigId</c> the client sends in
    /// every CS packet, and the <c>plotId</c> it sends with the query (the client's call sites
    /// pass the config id for both, see limited_auction_tab.lua lines 273, 1542, 1708).</summary>
    public uint Id { get; init; }

    /// <summary><c>plot_auction_config.activity_id</c> — joins <c>game_activities</c>.</summary>
    public uint ActivityId { get; init; }

    /// <summary>Whether <c>game_activities</c> still has this activity switched on
    /// (<c>status == 1</c>). Missing or off rows refuse bids loudly with
    /// <see cref="PlotAuctionErrorCodes.NotInAuction"/>.</summary>
    public bool ActivityActive { get; init; }

    /// <summary><c>name</c> — shown to players and used as the mail subject for refunds and prizes.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Parsed <c>start_price</c> "type|id|amount": the third field is the opening
    /// price; type/id are kept so an unsupported currency can be refused rather than guessed.</summary>
    public (uint RewardType, uint ItemId, long Amount) StartPrice { get; init; }

    /// <summary>Parsed <c>rewards</c> "type|id|count;type|id|count" — the prize list.</summary>
    public List<(uint RewardType, uint ItemId, uint Count)> Rewards { get; init; } = [];

    /// <summary><c>bid_increase_pct</c> in basis points (10000 = 100%), from the row.</summary>
    public int BidIncreasePct { get; init; }

    /// <summary><c>winner_count</c> — the top N bidders are winners, the rest are refunded.</summary>
    public uint WinnerCount { get; init; }

    /// <summary>Parsed <c>preview_start_time</c> (UTC; see <see cref="PlotAuctionRules.TryParseSchedule"/>).</summary>
    public DateTime PreviewStartUtc { get; init; }

    /// <summary>Parsed <c>bid_start_time</c> (UTC).</summary>
    public DateTime BidStartUtc { get; init; }

    /// <summary>Parsed <c>bid_end_time</c> (UTC) — settlement is due at this instant.</summary>
    public DateTime BidEndUtc { get; init; }
}
