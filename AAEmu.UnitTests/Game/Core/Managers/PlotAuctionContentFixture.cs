using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Test content fixture: builds the two shipped tables (plot_auction_config, game_activities)
/// in an in-memory SQLite so the real loader runs against the real column layout.
/// </summary>
internal static class PlotAuctionContentFixture
{
    public static SqliteConnection CreateInMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE game_activities (
                id INTEGER PRIMARY KEY,
                activity_name TEXT,
                activity_desc TEXT,
                status INTEGER DEFAULT 0,
                time_mode INTEGER DEFAULT 0,
                start_time TEXT,
                end_time TEXT,
                server_groups TEXT,
                task_group_id INTEGER DEFAULT 0
            );
            CREATE TABLE plot_auction_config (
                id INTEGER PRIMARY KEY,
                activity_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                describe TEXT,
                start_price TEXT NOT NULL,
                bid_increase_pct INTEGER DEFAULT 1000,
                winner_count INTEGER DEFAULT 10,
                rewards TEXT,
                preview_start_time TEXT,
                preview_end_time TEXT,
                bid_start_time TEXT,
                bid_end_time TEXT
            );
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    public static void AddActivity(SqliteConnection connection, long id, long status)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO game_activities (id, activity_name, activity_desc, status) VALUES ($id, 'auction', 'auction', $status)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", status);
        command.ExecuteNonQuery();
    }

    public static void AddConfig(
        SqliteConnection connection,
        long id,
        long activityId,
        string name,
        string startPrice,
        long bidIncreasePct,
        long winnerCount,
        string rewards,
        DateTime previewStartUtc,
        DateTime bidStartUtc,
        DateTime bidEndUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO plot_auction_config
                (id, activity_id, name, describe, start_price, bid_increase_pct, winner_count, rewards,
                 preview_start_time, preview_end_time, bid_start_time, bid_end_time)
            VALUES
                ($id, $activity_id, $name, 'fixture', $start_price, $pct, $winners, $rewards,
                 $preview, $preview, $bid_start, $bid_end)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$activity_id", activityId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$start_price", startPrice);
        command.Parameters.AddWithValue("$pct", bidIncreasePct);
        command.Parameters.AddWithValue("$winners", winnerCount);
        command.Parameters.AddWithValue("$rewards", rewards);
        command.Parameters.AddWithValue("$preview", Schedule(previewStartUtc));
        command.Parameters.AddWithValue("$bid_start", Schedule(bidStartUtc));
        command.Parameters.AddWithValue("$bid_end", Schedule(bidEndUtc));
        command.ExecuteNonQuery();
    }

    /// <summary>The shipped cell format: year|month|day|hour|minute.</summary>
    public static string Schedule(DateTime utc) =>
        $"{utc.Year}|{utc.Month}|{utc.Day}|{utc.Hour}|{utc.Minute}";
}
