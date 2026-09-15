using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Reflection;
using MySql.Data.MySqlClient;
using Xunit;

namespace AAEmu.IntegrationTests;

public sealed class FamilyMySqlFixture : IAsyncLifetime
{
    private const string EnvironmentVariable = "AAEMU_FAMILY_TEST_MYSQL";
    private static readonly Regex DatabaseName = new("^aaemu_family_test_[0-9a-f]{12}$", RegexOptions.CultureInvariant);
    private string _serverConnectionString;
    public string ConnectionString { get; private set; }
    public string Database { get; private set; }
    public bool Enabled => !string.IsNullOrWhiteSpace(_serverConnectionString);

    public async ValueTask InitializeAsync()
    {
        _serverConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!Enabled) return;
        Database = "aaemu_family_test_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        if (!DatabaseName.IsMatch(Database)) throw new InvalidOperationException("Generated invalid family test database name.");
        var server = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = string.Empty };
        await using var connection = new MySqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await Execute(connection, $"CREATE DATABASE `{Database}` CHARACTER SET utf8mb4");
        var database = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = Database };
        ConnectionString = database.ConnectionString;
        await using var isolated = new MySqlConnection(ConnectionString);
        await isolated.OpenAsync();
        await Execute(isolated, LegacySchema);
        await Execute(isolated, LoadProductMigration());
    }

    public async ValueTask DisposeAsync()
    {
        if (!Enabled || !DatabaseName.IsMatch(Database ?? string.Empty)) return;
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

    private static async Task Execute(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string LoadProductMigration()
    {
        const string resourceName = "AAEmu.IntegrationTests.Migrations.2026-09-13_aaemu_game_families.sql";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded product migration {resourceName}.");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        if (Regex.IsMatch(sql, @"\bUSE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Family migration must not select a database.");
        return sql;
    }

    private const string LegacySchema = """
CREATE TABLE characters (id INT UNSIGNED NOT NULL, family INT UNSIGNED NOT NULL DEFAULT 0, level TINYINT UNSIGNED NOT NULL DEFAULT 1, heir_exp BIGINT NOT NULL DEFAULT 0, PRIMARY KEY(id)) ENGINE=InnoDB;
CREATE TABLE family_members (character_id INT UNSIGNED NOT NULL, family_id INT UNSIGNED NOT NULL, name VARCHAR(45) NOT NULL, role TINYINT NOT NULL DEFAULT 0, title VARCHAR(45), PRIMARY KEY(family_id,character_id)) ENGINE=InnoDB;
CREATE TABLE item_containers (container_id BIGINT UNSIGNED NOT NULL, container_type VARCHAR(64) NOT NULL, slot_type INT NOT NULL, container_size INT NOT NULL, owner_id INT UNSIGNED NOT NULL, mate_id INT UNSIGNED NOT NULL DEFAULT 0, parent_item_id BIGINT UNSIGNED NOT NULL DEFAULT 0, PRIMARY KEY(container_id)) ENGINE=InnoDB;
CREATE TABLE items (id BIGINT UNSIGNED NOT NULL, type VARCHAR(100) NOT NULL, template_id INT UNSIGNED NOT NULL, container_id BIGINT UNSIGNED NOT NULL DEFAULT 0, slot_type INT NOT NULL, slot INT NOT NULL, count INT NOT NULL, detail_type TINYINT UNSIGNED NOT NULL DEFAULT 0, details BLOB, lifespan_mins INT NOT NULL, made_unit_id INT UNSIGNED NOT NULL DEFAULT 0, unsecure_time DATETIME NOT NULL, unpack_time DATETIME NOT NULL, owner INT UNSIGNED NOT NULL, created_at DATETIME NOT NULL, grade TINYINT NOT NULL, flags TINYINT UNSIGNED NOT NULL, ucc BIGINT UNSIGNED NOT NULL DEFAULT 0, expire_time DATETIME NULL, expire_online_minutes DOUBLE NOT NULL DEFAULT 0, charge_time DATETIME NULL, charge_count INT NOT NULL DEFAULT 0, PRIMARY KEY(id)) ENGINE=InnoDB;
""";

}
