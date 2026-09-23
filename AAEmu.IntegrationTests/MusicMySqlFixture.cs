using System.Security.Cryptography;
using System.Text.RegularExpressions;

using MySql.Data.MySqlClient;

using Xunit;

namespace AAEmu.IntegrationTests;

/// <summary>
/// An isolated database holding only the <c>music</c> table, for the save/relog round-trip. Opt-in
/// through <c>AAEMU_MUSIC_TEST_MYSQL</c> like the other MySQL fixtures, and dropped again after
/// every run.
/// </summary>
public sealed class MusicMySqlFixture : IAsyncLifetime
{
    private const string EnvironmentVariable = "AAEMU_MUSIC_TEST_MYSQL";
    private static readonly Regex DatabaseName = new("^aaemu_music_test_[0-9a-f]{12}$", RegexOptions.CultureInvariant);

    private string _serverConnectionString;

    public string ConnectionString { get; private set; }
    public string Database { get; private set; }
    public bool Enabled => !string.IsNullOrWhiteSpace(_serverConnectionString);

    public async ValueTask InitializeAsync()
    {
        _serverConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!Enabled)
            return;

        Database = "aaemu_music_test_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        if (!DatabaseName.IsMatch(Database))
            throw new InvalidOperationException("Generated invalid music test database name.");

        var server = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = string.Empty };
        await using (var connection = new MySqlConnection(server.ConnectionString))
        {
            await connection.OpenAsync();
            await Execute(connection, $"CREATE DATABASE `{Database}` CHARACTER SET utf8mb4");
        }

        var database = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = Database };
        ConnectionString = database.ConnectionString;
        await using var isolated = new MySqlConnection(ConnectionString);
        await isolated.OpenAsync();
        await Execute(isolated, LegacySchema);
    }

    public async ValueTask DisposeAsync()
    {
        if (!Enabled || !DatabaseName.IsMatch(Database ?? string.Empty))
            return;

        var server = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = string.Empty };
        await using var connection = new MySqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await Execute(connection, $"DROP DATABASE IF EXISTS `{Database}`");
    }

    public async Task<MySqlConnection> OpenAsync()
    {
        var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>The shape SQL/updates/2021-09-23_aaemu_game_music.sql gives the production table.</summary>
    private const string LegacySchema =
        """
        CREATE TABLE `music` (
          `id` int NOT NULL AUTO_INCREMENT,
          `author` int NOT NULL,
          `title` varchar(128) NOT NULL,
          `song` text NOT NULL,
          PRIMARY KEY (`id`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8;
        """;

    private static async Task Execute(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
