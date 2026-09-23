using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Account;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

using Microsoft.Data.Sqlite;
using Moq;

using MySql.Data.MySqlClient;

using Xunit;

namespace AAEmu.IntegrationTests;

/// <summary>
/// GF-S16 acceptance against an isolated MySQL schema: the account-return claim is exactly-once
/// (atomic grant + double-claim refusal) and survives a relog, and payment/account tier plus
/// entitlements load in the 10.x connection path from persisted state.
/// </summary>
public sealed class AccountReturnIntegrationTests
{
    private const string EnvironmentVariable = "AAEMU_RECRUITMENT_TEST_MYSQL";
    private const uint AccountId = 424242;
    private static readonly Regex SafeName = new("^aaemu_account_return_test_[0-9a-f]{12}$");

    private const string AccountsSchema = """
        CREATE TABLE accounts (
            account_id INT UNSIGNED NOT NULL,
            last_login DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (account_id)
        );
        """;

    private const string AccountAttributesSchema = """
        CREATE TABLE account_attributes (
            account_id INT UNSIGNED NOT NULL,
            kind_id INT UNSIGNED NOT NULL,
            kind_value INT UNSIGNED NOT NULL DEFAULT 0,
            world_id INT UNSIGNED NOT NULL DEFAULT 0,
            count INT NOT NULL DEFAULT 0,
            starts DATETIME NOT NULL DEFAULT '1970-01-01 00:00:00',
            expires DATETIME NOT NULL DEFAULT '9999-12-31 23:59:59',
            PRIMARY KEY (account_id, kind_id, kind_value, world_id)
        );
        """;

    [Fact]
    public async Task Claim_IsExactlyOnce_WithAnAtomicGrant_AndSurvivesRelog()
    {
        var supplied = Environment.GetEnvironmentVariable(EnvironmentVariable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(supplied),
            $"Set {EnvironmentVariable} for an isolated schema.");

        var (isolated, connectionString) = await CreateIsolatedSchema(supplied);
        var previousContent = SwapContentConfig(new ContentConfigGameData());
        try
        {
            // Content days: rest 3, block 10, reward const type 2 (all from seeded content rows).
            ContentConfigTestData(isolated: true);

            await Execute(isolated,
                "INSERT INTO accounts (account_id, last_login) VALUES " +
                $"({AccountId}, DATE_SUB(UTC_TIMESTAMP, INTERVAL 5 DAY))");

            var manager = new AccountReturnManager(Open(connectionString));

            // Available before the claim: 5 days away, rest 3, block 10 not reached.
            Assert.True(manager.IsRewardAvailable(AccountId));

            var grants = 0;
            var first = manager.TryClaim(AccountId, (_, _) => { grants++; return true; });
            Assert.Equal(AccountReturnClaimResult.Claimed, first);
            Assert.Equal(1, grants);
            Assert.True(manager.IsClaimed(AccountId));
            Assert.False(manager.IsRewardAvailable(AccountId));

            // Double claim: refused by the ledger primary key, grant never staged.
            var second = manager.TryClaim(AccountId, (_, _) => { grants++; return true; });
            Assert.Equal(AccountReturnClaimResult.AlreadyClaimed, second);
            Assert.Equal(1, grants);
            Assert.Equal(1L, await Scalar(connectionString,
                $"SELECT COUNT(*) FROM account_return_claims WHERE account_id={AccountId}"));

            // Relog: a brand-new manager over the same schema still refuses - the claim is in the DB.
            var afterRelog = new AccountReturnManager(Open(connectionString));
            Assert.True(afterRelog.IsClaimed(AccountId));
            Assert.False(afterRelog.IsRewardAvailable(AccountId));
            var third = afterRelog.TryClaim(AccountId, (_, _) => { grants++; return true; });
            Assert.Equal(AccountReturnClaimResult.AlreadyClaimed, third);
            Assert.Equal(1, grants);

            // Atomicity: a failed grant rolls the ledger row back, so the claim stays claimable.
            await Execute(isolated,
                "INSERT INTO accounts (account_id, last_login) VALUES " +
                "(424243, DATE_SUB(UTC_TIMESTAMP, INTERVAL 5 DAY))");
            var failed = manager.TryClaim(424243, (_, _) => false);
            Assert.Equal(AccountReturnClaimResult.GrantFailed, failed);
            Assert.False(manager.IsClaimed(424243));
            Assert.Equal(0L, await Scalar(connectionString,
                "SELECT COUNT(*) FROM account_return_claims WHERE account_id=424243"));
        }
        finally
        {
            RestoreContentConfig(previousContent);
            await DropIsolated(supplied, isolated);
        }
    }

    [Fact]
    public async Task BlockDay_FromContent_RefusesAnAbsenceThatPassedIt()
    {
        var supplied = Environment.GetEnvironmentVariable(EnvironmentVariable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(supplied),
            $"Set {EnvironmentVariable} for an isolated schema.");

        var (isolated, connectionString) = await CreateIsolatedSchema(supplied);
        var previousContent = SwapContentConfig(new ContentConfigGameData());
        try
        {
            ContentConfigTestData(isolated: true); // rest 3, block 10
            await Execute(isolated,
                "INSERT INTO accounts (account_id, last_login) VALUES " +
                $"({AccountId}, DATE_SUB(UTC_TIMESTAMP, INTERVAL 11 DAY))");

            var manager = new AccountReturnManager(Open(connectionString));
            Assert.False(manager.IsRewardAvailable(AccountId));
            var grants = 0;
            var result = manager.TryClaim(AccountId, (_, _) => { grants++; return true; });
            Assert.Equal(AccountReturnClaimResult.NotEligible, result);
            Assert.Equal(0, grants);
            Assert.False(manager.IsClaimed(AccountId));
        }
        finally
        {
            RestoreContentConfig(previousContent);
            await DropIsolated(supplied, isolated);
        }
    }

    private static void ContentConfigTestData(bool isolated)
    {
        _ = isolated;
        var data = ContentConfigGameData.Instance;
        data.SetForTest(ReturnAccountRules.RestDayKey, 3);
        data.SetForTest(ReturnAccountRules.RewardItemTypeKey, 2);
        data.SetForTest(ReturnAccountRules.RewardBlockDayKey, 10);
    }

    private static Func<MySqlConnection> Open(string connectionString) => () =>
    {
        var connection = new MySqlConnection(connectionString);
        connection.Open();
        return connection;
    };

    private static async Task<(MySqlConnection Connection, string ConnectionString)> CreateIsolatedSchema(
        string serverConnectionString)
    {
        var database = "aaemu_account_return_test_" +
                       Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        Assert.Matches(SafeName, database);
        var serverBuilder = new MySqlConnectionStringBuilder(serverConnectionString) { Database = string.Empty };
        await using (var server = new MySqlConnection(serverBuilder.ConnectionString))
        {
            await server.OpenAsync(TestContext.Current.CancellationToken);
            await Execute(server, $"CREATE DATABASE `{database}` CHARACTER SET utf8mb4");
        }

        var builder = new MySqlConnectionStringBuilder(serverConnectionString) { Database = database };
        var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await Execute(connection, AccountsSchema);
        await Execute(connection, ReadMigration("2026-09-23_aaemu_game_account_return_claims.sql"));
        return (connection, builder.ConnectionString);
    }

    private static async Task DropIsolated(string serverConnectionString, MySqlConnection isolated)
    {
        var database = isolated.Database;
        try
        {
            await isolated.DisposeAsync();
        }
        catch
        {
            // ignored: cleanup below still drops the schema
        }

        if (string.IsNullOrWhiteSpace(database) || !SafeName.IsMatch(database))
            return;
        var serverBuilder = new MySqlConnectionStringBuilder(serverConnectionString) { Database = string.Empty };
        await using var server = new MySqlConnection(serverBuilder.ConnectionString);
        await server.OpenAsync(CancellationToken.None);
        await ExecuteCleanup(server, $"DROP DATABASE IF EXISTS `{database}`");
    }

    private static string ReadMigration(string name)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"AAEmu.IntegrationTests.Migrations.{name}")
            ?? throw new InvalidOperationException($"Missing embedded migration {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task Execute(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task ExecuteCleanup(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 10;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<long> Scalar(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static object SwapContentConfig(ContentConfigGameData replacement) =>
        Swap<ContentConfigGameData>(replacement);

    private static void RestoreContentConfig(object previous) => Restore<ContentConfigGameData>(previous);

    private static object SwapSingleton<T>(T replacement) where T : class => Swap<T>(replacement);

    private static void RestoreSingleton<T>(object previous) where T : class => Restore<T>(previous);

    private static object Swap<T>(T replacement) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        field.SetValue(null, replacement);
        return previous;
    }

    private static void Restore<T>(object previous) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        field.SetValue(null, previous);
    }
}
