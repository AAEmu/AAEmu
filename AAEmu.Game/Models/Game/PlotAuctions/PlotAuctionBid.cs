namespace AAEmu.Game.Models.Game.PlotAuctions;

/// <summary>
/// One held (escrowed) bid: the money left the bidder's wallet when this row was written, and
/// exactly one future event — outbid-out-of-the-money refund, exit refund, or settlement —
/// consumes it. The row's lifetime IS the exactly-once ledger: it is deleted immediately before
/// a refund/prize is handed over and restored if the hand-over fails.
/// </summary>
public sealed class PlotAuctionBid
{
    /// <summary>Config id of the auction this bid belongs to.</summary>
    public uint AuctionId { get; init; }

    /// <summary>Bidding character.</summary>
    public uint CharacterId { get; init; }

    /// <summary>Held amount, in wallet copper (the wire field is s32, so this is bounded by it).</summary>
    public long Amount { get; init; }

    /// <summary>When the standing bid was placed — the tie-break when two amounts are equal.</summary>
    public DateTime BidTimeUtc { get; init; }
}
