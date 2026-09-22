namespace AAEmu.Game.Models.Game.PlotAuctions;

/// <summary>
/// The <c>errorCode</c> values <c>SCPlotAuctionBidResponsePacket</c> may carry. The numbers come
/// from 10.0.2.13's own <c>g_activity_error_codes.h</c> as the client reads them —
/// x2ui/ingameshop/limited_auction_tab.lua lines 447-451 name AEC_UNKNOWN_ERROR 1,
/// AEC_NOT_IN_AUCTION 500, AEC_AUCTION_BID_TOO_LOW 503, AEC_AUCTION_ENDED 504 and
/// AEC_AUCTION_BID_UNCHANGED 508, and its PLOT_AUCTION_BID_RESPONSE handler branches on exactly
/// these values (exit treats 0 and 500 both as "already out", 504 as ended).
/// </summary>
public static class PlotAuctionErrorCodes
{
    public const uint Success = 0;

    /// <summary>No specific code ships for a rejected cash balance or a store failure, so the
    /// generic client code is the only pinned answer (it shows the plain "bid failed" text).</summary>
    public const uint UnknownError = 1;

    /// <summary>No auction matches the request: unknown config, wrong/mismatched activity, an
    /// activity row that is switched off, or a bid before the bid window opened.</summary>
    public const uint NotInAuction = 500;

    /// <summary>The bid is below the current floor (<c>basePrice * (10000 + bid_increase_pct) / 10000</c>).</summary>
    public const uint BidTooLow = 503;

    /// <summary>The bid window has closed (also used for an exit the client itself disables in
    /// the auction's last 20 minutes — see <see cref="PlotAuctionManager.ExitLockout"/>).</summary>
    public const uint AuctionEnded = 504;

    /// <summary>The bid equals the character's own standing bid.</summary>
    public const uint BidUnchanged = 508;
}
