using AAEmu.Game.Core.Managers;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.Models.Game.PlotAuctions;

/// <summary>
/// Reads the auction's content out of the shipped compact.sqlite3, read-only:
/// <c>plot_auction_config</c> (one row per auction) gated by its <c>game_activities</c> row
/// (<c>status == 1</c> is the activity switch — activity id 1001 拍卖活动 is this auction's row,
/// see re/research/world-systems-gap-2026-09-19.md §2). Every unparsable or orphaned row is
/// skipped with an error on the World log: no shipped value ever falls back to a source default.
/// </summary>
public static class PlotAuctionContent
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static Dictionary<uint, PlotAuctionConfig> Load(SqliteConnection connection)
    {
        var configs = new Dictionary<uint, PlotAuctionConfig>();
        var active = LoadActivitySwitches(connection);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, activity_id, name, start_price, bid_increase_pct, winner_count, rewards,
                   preview_start_time, bid_start_time, bid_end_time
            FROM plot_auction_config
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = Convert.ToUInt32(reader["id"]);
            if (!TryReadRow(reader, active, out var config, out var reason))
            {
                Logger.Error("Plot auction: skipping config {0}: {1}", id, reason);
                continue;
            }

            configs[config.Id] = config;
            if (!config.ActivityActive)
                Logger.Error(
                    "Plot auction: config {0} loaded but its activity {1} is missing or switched off in game_activities; bids and exits are refused",
                    config.Id, config.ActivityId);
        }

        if (configs.Count == 0)
            Logger.Error("Plot auction: plot_auction_config produced no usable rows; the auction stays inert");

        return configs;
    }

    /// <summary>Activity id → on (<c>status == 1</c>). A missing table yields an empty map, so
    /// every config lands in the "activity missing" branch and refuses bids loudly.</summary>
    private static Dictionary<uint, bool> LoadActivitySwitches(SqliteConnection connection)
    {
        var active = new Dictionary<uint, bool>();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, status FROM game_activities";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                active[Convert.ToUInt32(reader["id"])] = Convert.ToInt32(reader["status"]) == 1;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Plot auction: could not read game_activities; every auction will refuse bids");
        }

        return active;
    }

    private static bool TryReadRow(
        SqliteDataReader reader,
        Dictionary<uint, bool> active,
        out PlotAuctionConfig config,
        out string reason)
    {
        config = null;
        reason = string.Empty;

        var activityId = Convert.ToUInt32(reader["activity_id"]);
        if (!PlotAuctionRules.TryParseTriple(reader["start_price"] as string, out var priceType, out var priceId, out var priceAmount))
        {
            reason = $"start_price '{reader["start_price"]}' is not type|id|amount";
            return false;
        }

        if (!PlotAuctionRules.TryParseRewards(reader["rewards"] as string, out var rewards))
        {
            reason = $"rewards '{reader["rewards"]}' is not type|id|count;...";
            return false;
        }

        if (!PlotAuctionRules.TryParseSchedule(reader["preview_start_time"] as string, out var previewStart))
        {
            reason = $"preview_start_time '{reader["preview_start_time"]}' is not year|month|day|hour|minute";
            return false;
        }

        if (!PlotAuctionRules.TryParseSchedule(reader["bid_start_time"] as string, out var bidStart))
        {
            reason = $"bid_start_time '{reader["bid_start_time"]}' is not year|month|day|hour|minute";
            return false;
        }

        if (!PlotAuctionRules.TryParseSchedule(reader["bid_end_time"] as string, out var bidEnd))
        {
            reason = $"bid_end_time '{reader["bid_end_time"]}' is not year|month|day|hour|minute";
            return false;
        }

        var winnerCount = Convert.ToUInt32(reader["winner_count"]);
        if (winnerCount == 0)
        {
            reason = "winner_count is 0; no bidder could ever win";
            return false;
        }

        if (bidEnd <= bidStart)
        {
            reason = "bid_end_time is not after bid_start_time";
            return false;
        }

        config = new PlotAuctionConfig
        {
            Id = Convert.ToUInt32(reader["id"]),
            ActivityId = activityId,
            ActivityActive = active.GetValueOrDefault(activityId),
            Name = reader["name"] as string ?? string.Empty,
            StartPrice = (priceType, priceId, priceAmount),
            Rewards = rewards,
            BidIncreasePct = Convert.ToInt32(reader["bid_increase_pct"]),
            WinnerCount = winnerCount,
            PreviewStartUtc = previewStart,
            BidStartUtc = bidStart,
            BidEndUtc = bidEnd,
        };
        return true;
    }
}
