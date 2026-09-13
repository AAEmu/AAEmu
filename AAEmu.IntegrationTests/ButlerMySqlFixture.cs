using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MySql.Data.MySqlClient;
using Xunit;

namespace AAEmu.IntegrationTests;

/// <summary>Opt-in isolated MySQL database for persistence integration tests.</summary>
public sealed class ButlerMySqlFixture : IAsyncLifetime
{
    private const string EnvironmentVariable = "AAEMU_BUTLER_TEST_MYSQL";
    private static readonly Regex DatabaseName = new("^aaemu_butler_test_[0-9a-f]{12}$", RegexOptions.CultureInvariant);
    private string _serverConnectionString;
    public string ConnectionString { get; private set; }
    public string Database { get; private set; }
    public bool Enabled => !string.IsNullOrWhiteSpace(_serverConnectionString);

    public async ValueTask InitializeAsync()
    {
        _serverConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!Enabled)
            return;

        Database = "aaemu_butler_test_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        if (!DatabaseName.IsMatch(Database))
            throw new InvalidOperationException("Generated an invalid integration database name.");
        var builder = new MySqlConnectionStringBuilder(_serverConnectionString);
        if (string.Equals(builder.Database, "aaemu_game", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Butler integration fixture never accepts aaemu_game.");
        builder.Database = Database;
        ConnectionString = builder.ConnectionString;
        var server = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = string.Empty };
        await using var connection = new MySqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await Execute(connection, $"CREATE DATABASE `{Database}` CHARACTER SET utf8mb4");
        await using var databaseConnection = new MySqlConnection(ConnectionString);
        await databaseConnection.OpenAsync();
        await Execute(databaseConnection, Schema);
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
        if (!Enabled) throw new InvalidOperationException($"Set {EnvironmentVariable} to opt into MySQL integration tests.");
        var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task Execute(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }

    // Deliberately fixture-local: product scripts include USE aaemu_game and must never be replayed here.
    private const string Schema = """
CREATE TABLE character_butlers (character_id INT UNSIGNED NOT NULL, house_id INT UNSIGNED NULL, name VARCHAR(128) NOT NULL DEFAULT '', labor_power INT UNSIGNED NOT NULL DEFAULT 0, lp_charged_amount SMALLINT UNSIGNED NOT NULL DEFAULT 0, lp_charge_reset_time BIGINT NOT NULL DEFAULT 0, remain_production_cost SMALLINT UNSIGNED NOT NULL DEFAULT 0, PRIMARY KEY(character_id), UNIQUE KEY ux_house(house_id)) ENGINE=InnoDB;
CREATE TABLE character_butler_harvest_jobs (id BIGINT NOT NULL AUTO_INCREMENT, character_id INT UNSIGNED NOT NULL, static_harvest_id INT UNSIGNED NOT NULL, requested_amount SMALLINT UNSIGNED NOT NULL, remaining_repeat_count SMALLINT UNSIGNED NOT NULL, lp_for_calc_exp INT UNSIGNED NOT NULL, update_time BIGINT NOT NULL, PRIMARY KEY(id)) ENGINE=InnoDB;
CREATE TABLE character_butler_harvest_completions (job_id BIGINT NOT NULL, cycle_number SMALLINT UNSIGNED NOT NULL, completed_at BIGINT NOT NULL, PRIMARY KEY(job_id,cycle_number)) ENGINE=InnoDB;
CREATE TABLE character_butler_items (character_id INT UNSIGNED NOT NULL, item_type TINYINT UNSIGNED NOT NULL, item_id BIGINT UNSIGNED NOT NULL, PRIMARY KEY(character_id,item_id), UNIQUE KEY ux_item(item_id)) ENGINE=InnoDB;
CREATE TABLE item_containers (container_id BIGINT UNSIGNED NOT NULL, container_type VARCHAR(64) NOT NULL, slot_type INT NOT NULL, container_size INT NOT NULL, owner_id INT UNSIGNED NOT NULL, mate_id INT UNSIGNED NOT NULL DEFAULT 0, parent_item_id BIGINT UNSIGNED NOT NULL DEFAULT 0, PRIMARY KEY(container_id)) ENGINE=InnoDB;
CREATE TABLE items (id BIGINT UNSIGNED NOT NULL, type VARCHAR(100) NOT NULL, template_id INT UNSIGNED NOT NULL, container_id BIGINT UNSIGNED NOT NULL DEFAULT 0, slot_type INT NOT NULL, slot INT NOT NULL, count INT NOT NULL, details BLOB, lifespan_mins INT NOT NULL, made_unit_id INT UNSIGNED NOT NULL DEFAULT 0, unsecure_time DATETIME NOT NULL, unpack_time DATETIME NOT NULL, owner INT UNSIGNED NOT NULL, created_at DATETIME NOT NULL, grade TINYINT NOT NULL, flags TINYINT UNSIGNED NOT NULL, ucc BIGINT UNSIGNED NOT NULL DEFAULT 0, expire_time DATETIME NULL, expire_online_minutes DOUBLE NOT NULL DEFAULT 0, charge_time DATETIME NULL, charge_count INT NOT NULL DEFAULT 0, PRIMARY KEY(id)) ENGINE=InnoDB;
CREATE TABLE mails (id INT NOT NULL, type INT NOT NULL, status INT NOT NULL, title TEXT NOT NULL, text TEXT NOT NULL, sender_id INT NOT NULL, sender_name VARCHAR(45) NOT NULL, attachment_count INT NOT NULL, receiver_id INT NOT NULL, receiver_name VARCHAR(45) NOT NULL, open_date DATETIME NOT NULL, send_date DATETIME NOT NULL, received_date DATETIME NOT NULL, sender_deleted TINYINT NOT NULL, receiver_deleted TINYINT NOT NULL, returned INT NOT NULL, extra BIGINT NOT NULL, money_amount_1 INT NOT NULL, money_amount_2 INT NOT NULL, money_amount_3 INT NOT NULL, attachment0 BIGINT NOT NULL DEFAULT 0, attachment1 BIGINT NOT NULL DEFAULT 0, attachment2 BIGINT NOT NULL DEFAULT 0, attachment3 BIGINT NOT NULL DEFAULT 0, attachment4 BIGINT NOT NULL DEFAULT 0, attachment5 BIGINT NOT NULL DEFAULT 0, attachment6 BIGINT NOT NULL DEFAULT 0, attachment7 BIGINT NOT NULL DEFAULT 0, attachment8 BIGINT NOT NULL DEFAULT 0, attachment9 BIGINT NOT NULL DEFAULT 0, PRIMARY KEY(id)) ENGINE=InnoDB;
CREATE TABLE accounts (account_id INT UNSIGNED NOT NULL, labor SMALLINT NOT NULL, local_labor INT NOT NULL, PRIMARY KEY(account_id)) ENGINE=InnoDB;
""";
}
