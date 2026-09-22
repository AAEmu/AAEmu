namespace AAEmu.Game.Models.Game.PlotAuctions;

/// <summary>
/// The plot auction's lifecycle window, mirroring the client's own
/// <c>PlotAuctionPhase</c> constants (x2ui/ingameshop/limited_auction_tab.lua lines 376-380).
/// </summary>
public enum PlotAuctionPhase : byte
{
    /// <summary>Not started yet, or already ended.</summary>
    None = 0,

    /// <summary>Preview window: the auction can be viewed but not bid on.</summary>
    Preview = 1,

    /// <summary>Bid window: bids and exits are accepted.</summary>
    Bid = 2,
}
