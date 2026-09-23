namespace AAEmu.Game.Models.Game.PlotAuctions;

/// <summary>
/// Persistence for the auction state machine. Auction rows and escrow rows are written at the
/// moment they change (first bid / every bid / refund / settlement), never on the save tick —
/// the same contract <c>ICraftOrderStore</c> states, so a World kill cannot leave held money
/// without its row.
/// </summary>
public interface IPlotAuctionStore
{
    IReadOnlyList<PlotAuction> LoadAuctions();

    IReadOnlyList<PlotAuctionBid> LoadBids();

    /// <summary>Inserts or updates one auction row. False means "not durably written".</summary>
    bool UpsertAuction(PlotAuction auction, MySql.Data.MySqlClient.MySqlConnection connection = null,
        MySql.Data.MySqlClient.MySqlTransaction transaction = null);

    /// <summary>Inserts or updates one escrow row. False means "not durably written".</summary>
    bool UpsertBid(PlotAuctionBid bid, MySql.Data.MySqlClient.MySqlConnection connection = null,
        MySql.Data.MySqlClient.MySqlTransaction transaction = null);

    /// <summary>Removes one escrow row. False means the row was not there / not written.</summary>
    bool DeleteBid(uint auctionId, uint characterId, MySql.Data.MySqlClient.MySqlConnection connection = null,
        MySql.Data.MySqlClient.MySqlTransaction transaction = null);
}
