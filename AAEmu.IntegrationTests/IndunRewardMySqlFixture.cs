using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MySql.Data.MySqlClient;
using Xunit;

namespace AAEmu.IntegrationTests;

/// <summary>Opt-in isolated MySQL database for the W03A claim transaction tests.</summary>
public sealed class IndunRewardMySqlFixture : IAsyncLifetime
{
    private const string EnvironmentVariable = "AAEMU_INDUN_REWARD_TEST_MYSQL";
    private static readonly Regex DatabaseName = new("^aaemu_indun_reward_test_[0-9a-f]{12}$", RegexOptions.CultureInvariant);
    private string _serverConnectionString;

    public string ConnectionString { get; private set; }
    public string Database { get; private set; }
    public bool Enabled => !string.IsNullOrWhiteSpace(_serverConnectionString);

    public async ValueTask InitializeAsync()
    {
        _serverConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!Enabled)
            return;

        Database = "aaemu_indun_reward_test_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        if (!DatabaseName.IsMatch(Database))
            throw new InvalidOperationException("Generated an invalid integration database name.");

        var builder = new MySqlConnectionStringBuilder(_serverConnectionString);
        if (string.Equals(builder.Database, "aaemu_game", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The indun reward fixture never accepts aaemu_game.");
        // Exercise the production-default CLIENT_FOUND_ROWS behavior: duplicate statements
        // must not report a successful insert through MySqlAffectedRows.
        builder.UseAffectedRows = false;
        builder.Database = Database;
        ConnectionString = builder.ConnectionString;

        var server = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = string.Empty };
        await using (var connection = new MySqlConnection(server.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, $"CREATE DATABASE `{Database}` CHARACTER SET utf8mb4");
        }

        await using var database = new MySqlConnection(ConnectionString);
        await database.OpenAsync();
        await ExecuteAsync(database, Schema);
    }

    public async ValueTask DisposeAsync()
    {
        if (!Enabled || !DatabaseName.IsMatch(Database ?? string.Empty))
            return;

        var server = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = string.Empty };
        await using var connection = new MySqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS `{Database}`");
    }

    public MySqlConnection Open()
    {
        if (!Enabled)
            throw new InvalidOperationException($"Set {EnvironmentVariable} to opt into MySQL integration tests.");
        var connection = new MySqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private const string Schema = """
        CREATE TABLE indun_reward_claims (
            run_id VARCHAR(128) NOT NULL,
            instance_id INT UNSIGNED NOT NULL,
            instance_reward_kind_id INT UNSIGNED NOT NULL,
            character_id INT UNSIGNED NOT NULL,
            mail_id BIGINT UNSIGNED NULL,
            claimed_at DATETIME(6) NOT NULL,
            PRIMARY KEY (run_id, instance_id, character_id, instance_reward_kind_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        CREATE TABLE indun_reward_test_mail (
            mail_id BIGINT UNSIGNED NOT NULL,
            run_id VARCHAR(128) NOT NULL,
            receiver_id INT UNSIGNED NOT NULL,
            PRIMARY KEY (mail_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """;
}
