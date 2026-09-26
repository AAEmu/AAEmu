namespace AAEmu.Game.Models.Game.PlotAuctions;

/// <summary>
/// One row of <c>SCPlotAuctionInfoPacket</c>'s <c>bidInfoMap</c>: the per-viewer state the
/// ingameshop's detail panel reads back through <c>X2Player:GetPlotAuctionBidData</c> /
/// <c>GetPlotAuctionInfoList</c> ({ plotId, myBidAmount, myRanking, totalBidders, basePrice } in
/// limited_auction_tab.lua lines 396-406). <c>currentWinningBid</c> is the field the Lua getters
/// do not surface but the client's value serializer writes (six s32 fields).
/// </summary>
public sealed class PlotAuctionBidInfoRow
{
    /// <summary>The map key and the row's own plotId — both the config id (see
    /// <see cref="PlotAuctionConfig.Id"/> for the evidence).</summary>
    public uint PlotId { get; init; }

    /// <summary>The viewing character's standing bid, 0 when they hold none.</summary>
    public long MyBidAmount { get; init; }

    /// <summary>The viewer's 1-based rank among standing bids, 0 when they hold none.</summary>
    public uint MyRanking { get; init; }

    /// <summary>The leading standing bid, 0 before the first bid.</summary>
    public long CurrentWinningBid { get; init; }

    /// <summary>How many characters currently hold a standing bid.</summary>
    public uint TotalBidders { get; init; }

    /// <summary>The auction's price base (leading bid, 0 before the first bid).</summary>
    public long BasePrice { get; init; }
}
