using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.PlotAuctions;

/// <summary>
/// MySQL store for the two state tables (<c>plot_auctions</c>, <c>character_plot_auction_bids</c>).
/// One statement per call on its own connection — the same shape as <c>MySqlCraftOrderStore</c>,
/// so a kill between two writes can never leave held money without its row's partner.
/// </summary>
public sealed class MySqlPlotAuctionStore : IPlotAuctionStore
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const string SelectAuctions = "SELECT id, activity_id, settled, base_price FROM plot_auctions";
    private const string SelectBids =
        "SELECT auction_id, character_id, bid_amount, bid_time_unix FROM character_plot_auction_bids";

    private const string UpsertAuctionSql = """
        INSERT INTO plot_auctions (id, activity_id, settled, base_price, updated_unix)
        VALUES (@id, @activity_id, @settled, @base_price, @updated_unix)
        ON DUPLICATE KEY UPDATE settled = @settled, base_price = @base_price, updated_unix = @updated_unix
        """;

    private const string UpsertBidSql = """
        INSERT INTO character_plot_auction_bids (auction_id, character_id, bid_amount, bid_time_unix)
        VALUES (@auction_id, @character_id, @bid_amount, @bid_time_unix)
        ON DUPLICATE KEY UPDATE bid_amount = @bid_amount, bid_time_unix = @bid_time_unix
        """;

    public IReadOnlyList<PlotAuction> LoadAuctions()
    {
        var rows = new List<PlotAuction>();
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = SelectAuctions;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new PlotAuction
                {
                    Id = reader.GetUInt32("id"),
                    ActivityId = reader.GetUInt32("activity_id"),
                    Settled = reader.GetByte("settled") != 0,
                    BasePrice = reader.GetInt64("base_price"),
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Plot auction: failed to load plot_auctions");
            throw;
        }

        return rows;
    }

    public IReadOnlyList<PlotAuctionBid> LoadBids()
    {
        var rows = new List<PlotAuctionBid>();
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = SelectBids;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new PlotAuctionBid
                {
                    AuctionId = reader.GetUInt32("auction_id"),
                    CharacterId = reader.GetUInt32("character_id"),
                    Amount = reader.GetInt64("bid_amount"),
                    BidTimeUtc = ServerCalendarFromUnix(reader.GetInt64("bid_time_unix")),
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Plot auction: failed to load character_plot_auction_bids");
            throw;
        }

        return rows;
    }

    public bool UpsertAuction(PlotAuction auction, MySqlConnection connection = null, MySqlTransaction transaction = null)
    {
        if (auction == null || auction.Id == 0)
            return false;
        var ownConnection = connection == null;
        connection ??= MySQL.CreateConnection();
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = UpsertAuctionSql;
            command.Parameters.AddWithValue("@id", auction.Id);
            command.Parameters.AddWithValue("@activity_id", auction.ActivityId);
            command.Parameters.AddWithValue("@settled", auction.Settled ? (byte)1 : (byte)0);
            command.Parameters.AddWithValue("@base_price", auction.BasePrice);
            command.Parameters.AddWithValue("@updated_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return command.ExecuteNonQuery() > 0;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Plot auction: failed to upsert auction {0}", auction.Id);
            return false;
        }
        finally
        {
            if (ownConnection)
                connection.Dispose();
        }
    }

    public bool UpsertBid(PlotAuctionBid bid, MySqlConnection connection = null, MySqlTransaction transaction = null)
    {
        if (bid == null || bid.AuctionId == 0 || bid.CharacterId == 0)
            return false;
        var ownConnection = connection == null;
        connection ??= MySQL.CreateConnection();
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = UpsertBidSql;
            command.Parameters.AddWithValue("@auction_id", bid.AuctionId);
            command.Parameters.AddWithValue("@character_id", bid.CharacterId);
            command.Parameters.AddWithValue("@bid_amount", bid.Amount);
            command.Parameters.AddWithValue("@bid_time_unix",
                new DateTimeOffset(ServerCalendarAsUtc(bid.BidTimeUtc)).ToUnixTimeSeconds());
            return command.ExecuteNonQuery() > 0;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Plot auction: failed to upsert bid {0}/{1}", bid.AuctionId, bid.CharacterId);
            return false;
        }
        finally
        {
            if (ownConnection)
                connection.Dispose();
        }
    }

    public bool DeleteBid(uint auctionId, uint characterId, MySqlConnection connection = null,
        MySqlTransaction transaction = null)
    {
        var ownConnection = connection == null;
        connection ??= MySQL.CreateConnection();
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "DELETE FROM character_plot_auction_bids WHERE auction_id = @auction_id AND character_id = @character_id";
            command.Parameters.AddWithValue("@auction_id", auctionId);
            command.Parameters.AddWithValue("@character_id", characterId);
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Plot auction: failed to delete bid {0}/{1}", auctionId, characterId);
            return false;
        }
        finally
        {
            if (ownConnection)
                connection.Dispose();
        }
    }

    private static DateTime ServerCalendarFromUnix(long unix) =>
        ServerCalendar.AsUtc(DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime);

    private static DateTime ServerCalendarAsUtc(DateTime value) => ServerCalendar.AsUtc(value);
}
