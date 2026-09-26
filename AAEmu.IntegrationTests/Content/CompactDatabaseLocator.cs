using Microsoft.Data.Sqlite;
using Xunit;

namespace AAEmu.IntegrationTests.Content;

/// <summary>
/// Opens the shipped compact database for tests that assert against real content instead of a
/// hand-built fixture.
/// </summary>
/// <remarks>
/// These tests are gated on <see cref="EnvVar"/> (or the deployment default beside the app, which is
/// where <c>SQLite.CreateConnection</c> looks). Without a database they skip with an actionable
/// message instead of failing a runner that simply has no game content, and with one they assert
/// against the shipped rows. <see cref="Open"/> is only called after the gate passes, so a missing
/// file is never mistaken for an empty result set.
/// </remarks>
public static class CompactDatabaseLocator
{
    public const string EnvVar = "AAEMU_TEST_COMPACT_DB";

    public const string SkipMessage =
        "Set " + EnvVar + " to the shipped compact.sqlite3 to run the compact content tests.";

    /// <summary>A database this machine can actually open, or null when none is configured.</summary>
    public static string? ResolvePath()
    {
        var configured = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var deploymentDefault = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Data", "compact.sqlite3");
        return File.Exists(deploymentDefault) ? deploymentDefault : null;
    }

    public static SqliteConnection Open()
    {
        var path = ResolvePath() ??
            throw new FileNotFoundException("Compact database not found. " + SkipMessage);
        var connection = new SqliteConnection($"Data Source=file:{path}; Mode=ReadOnly");
        connection.Open();
        return connection;
    }

    /// <summary>Runs a query and returns every row as a value array.</summary>
    public static List<object[]> Query(SqliteConnection connection, string sql, params (string, object)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        using var reader = command.ExecuteReader();
        var rows = new List<object[]>();
        while (reader.Read())
        {
            var row = new object[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }
}
