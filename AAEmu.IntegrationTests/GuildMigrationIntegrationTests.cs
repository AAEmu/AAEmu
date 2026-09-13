using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MySql.Data.MySqlClient;
using Xunit;

namespace AAEmu.IntegrationTests;

public sealed class GuildMigrationIntegrationTests
{
    private const string EnvironmentVariable = "AAEMU_RECRUITMENT_TEST_MYSQL";
    private static readonly Regex SafeName = new("^aaemu_guild_migration_test_[0-9a-f]{12}$");
    private static readonly string[] Migrations =
    [
        "2026-09-13_aaemu_game_families.sql",
        "2026-09-13_aaemu_game_expedition_activities.sql",
        "2026-09-13_aaemu_game_expedition_instance_histories.sql",
        "2026-09-13_aaemu_game_expedition_instance_history_type.sql",
        "2026-09-13_aaemu_game_expedition_daily_exp.sql",
        "2026-09-13_aaemu_game_expedition_recruitment.sql",
        "2026-09-13_aaemu_game_expedition_renames.sql",
        "2026-09-13_aaemu_game_expedition_weekly_contribution.sql",
        "2026-09-13_aaemu_game_expedition_descriptor_state.sql",
        "2026-09-13_aaemu_game_expedition_public_assignments.sql"
    ];

    [Fact]
    public async Task ProductGuildMigrations_ExecuteAgainstIsolatedLegacySchema()
    {
        var supplied = Environment.GetEnvironmentVariable(EnvironmentVariable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(supplied), $"Set {EnvironmentVariable} for an isolated schema.");
        var database = "aaemu_guild_migration_test_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        Assert.Matches(SafeName, database);
        var serverBuilder = new MySqlConnectionStringBuilder(supplied) { Database = string.Empty };
        await using var server = new MySqlConnection(serverBuilder.ConnectionString);
        await server.OpenAsync(TestContext.Current.CancellationToken);
        try
        {
            await Execute(server, $"CREATE DATABASE `{database}` CHARACTER SET utf8mb4");
            var isolatedBuilder = new MySqlConnectionStringBuilder(supplied) { Database = database };
            await using var isolated = new MySqlConnection(isolatedBuilder.ConnectionString);
            await isolated.OpenAsync(TestContext.Current.CancellationToken);
            await Execute(isolated, "CREATE TABLE expeditions(id INT NOT NULL PRIMARY KEY, exp INT UNSIGNED NOT NULL DEFAULT 0, interest INT UNSIGNED NOT NULL DEFAULT 0); CREATE TABLE expedition_members(character_id INT UNSIGNED NOT NULL PRIMARY KEY, weekly_contribution_point INT UNSIGNED NOT NULL DEFAULT 0); CREATE TABLE characters(id INT UNSIGNED NOT NULL PRIMARY KEY, expedition_id INT NOT NULL DEFAULT 0, family INT UNSIGNED NOT NULL DEFAULT 0); CREATE TABLE family_members(character_id INT UNSIGNED NOT NULL PRIMARY KEY, role TINYINT NOT NULL DEFAULT 0, title VARCHAR(45) NULL DEFAULT NULL); INSERT INTO characters VALUES(1,0,7); INSERT INTO family_members VALUES(1,0,NULL)");
            foreach (var name in Migrations)
            {
                if (name == "2026-09-13_aaemu_game_expedition_instance_history_type.sql")
                    await Execute(isolated, "INSERT INTO expedition_instance_histories(expedition_id,battlefield_type,instance_id,score,play_result,recorded_at) VALUES(1,41,69,120,1,NOW(6))");
                var sql = ReadMigration(name);
                Assert.DoesNotMatch(new Regex(@"(?im)^\s*USE\b|\baaemu_game\s*\."), sql);
                await Execute(isolated, sql);
            }
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='expedition_portals'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='expedition_instance_histories'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='expedition_instance_history_members'"));
            Assert.Equal(0L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='expedition_instance_histories' AND column_name='battlefield_type'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='expedition_instance_histories' AND column_name='instance_rank_detail_id' AND column_type='int unsigned'"));
            Assert.Equal(41L, await Scalar(isolated, "SELECT instance_rank_detail_id FROM expedition_instance_histories WHERE instance_id=69"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='expedition_recruitments'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='expedition_public_assignments'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='expedition_public_assignment_contributors'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='expedition_public_assignment_claims'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='character_today_board_reset_counts'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='family_act_sanctions'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='expeditions' AND column_name='daily_exp'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='characters' AND column_name='expedition_rejoin_until'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='characters' AND column_name='family_rejoin_until'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='expedition_members' AND column_name='weekly_contribution_period_start'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='expeditions' AND column_name='war_deposit'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='expeditions' AND column_name='last_contribution_point_added'"));
            Assert.Equal(1L, await Scalar(isolated, "SELECT COUNT(*) FROM families WHERE id=7"));
            Assert.Equal(0L, await Scalar(isolated, "SELECT COUNT(*) FROM family_members WHERE title IS NULL"));
        }
        finally
        {
            if (SafeName.IsMatch(database)) await ExecuteCleanup(server, $"DROP DATABASE IF EXISTS `{database}`");
        }
    }

    private static string ReadMigration(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"AAEmu.IntegrationTests.Migrations.{name}")
                           ?? throw new InvalidOperationException($"Missing embedded migration {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<long> Scalar(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
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
}
