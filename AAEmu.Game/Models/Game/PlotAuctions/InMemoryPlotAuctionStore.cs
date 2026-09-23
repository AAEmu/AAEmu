namespace AAEmu.Game.Models.Game.PlotAuctions;

/// <summary>Process-lifetime store used by tests, and by a World that has not loaded MySQL yet.</summary>
public sealed class InMemoryPlotAuctionStore : IPlotAuctionStore
{
    private readonly Dictionary<uint, PlotAuction> _auctions = [];
    private readonly Dictionary<(uint AuctionId, uint CharacterId), PlotAuctionBid> _bids = [];

    public IReadOnlyList<PlotAuction> LoadAuctions() => _auctions.Values.ToList();

    public IReadOnlyList<PlotAuctionBid> LoadBids() => _bids.Values.ToList();

    public bool UpsertAuction(PlotAuction auction, MySql.Data.MySqlClient.MySqlConnection connection = null,
        MySql.Data.MySqlClient.MySqlTransaction transaction = null)
    {
        if (auction == null || auction.Id == 0)
            return false;
        _auctions[auction.Id] = auction;
        return true;
    }

    public bool UpsertBid(PlotAuctionBid bid, MySql.Data.MySqlClient.MySqlConnection connection = null,
        MySql.Data.MySqlClient.MySqlTransaction transaction = null)
    {
        if (bid == null || bid.AuctionId == 0 || bid.CharacterId == 0)
            return false;
        _bids[(bid.AuctionId, bid.CharacterId)] = bid;
        return true;
    }

    public bool DeleteBid(uint auctionId, uint characterId, MySql.Data.MySqlClient.MySqlConnection connection = null,
        MySql.Data.MySqlClient.MySqlTransaction transaction = null) =>
        _bids.Remove((auctionId, characterId));
}
