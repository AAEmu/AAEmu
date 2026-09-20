using AAEmu.Commons.Utils.Updater;
using AAEmu.Commons.Models;

namespace AAEmu.UnitTests.Commons.Utils.Updater;

public class MySqlDatabaseBootstrapTests
{
    [Test]
    public async Task RewriteBaseSchemaSql_ReplacesSchemaDirectivesWithConfiguredDatabase()
    {
        const string sql = """
            CREATE DATABASE IF NOT EXISTS `aaemu_game`;
            USE `aaemu_game`;
            CREATE TABLE `characters` (`id` int NOT NULL);
            """;

        var rewritten = MySqlDatabaseBootstrap.RewriteBaseSchemaSql(sql, "aaemu_game_custom");

        await Assert.That(rewritten).Contains("CREATE DATABASE IF NOT EXISTS `aaemu_game_custom`");
        await Assert.That(rewritten).Contains("USE `aaemu_game_custom`");
        await Assert.That(rewritten).DoesNotContain("`aaemu_game`;");
        await Assert.That(rewritten).Contains("CREATE TABLE `characters`");
    }

    [Test]
    public async Task PrepareImportSql_RemovesCreateDatabaseButRetainsConfiguredUseAndSchemaBody()
    {
        const string sql = """
            CREATE DATABASE IF NOT EXISTS `aaemu_game`;
            USE `aaemu_game`;
            CREATE TABLE `characters` (`id` int NOT NULL);
            """;

        var prepared = MySqlDatabaseBootstrap.PrepareImportSql(sql, "aaemu_game_custom");

        await Assert.That(prepared).DoesNotContain("CREATE DATABASE");
        await Assert.That(prepared).Contains("USE `aaemu_game_custom`");
        await Assert.That(prepared).Contains("CREATE TABLE `characters`");
    }

    [Test]
    public async Task RewriteBaseSchemaSql_DoesNotTreatUseInsideTableNamesAsUseDirective()
    {
        const string sql = """
            CREATE DATABASE IF NOT EXISTS `aaemu_game`;
            USE aaemu_game;
            -- Table structure for auction_house
            -- ------------------------------------------------
            CREATE TABLE `auction_house` (`id` int NOT NULL);
            """;

        var rewritten = MySqlDatabaseBootstrap.RewriteBaseSchemaSql(sql, "aaemu_game_custom");

        await Assert.That(rewritten).Contains("-- Table structure for auction_house");
        await Assert.That(rewritten).Contains("CREATE TABLE `auction_house`");
        await Assert.That(rewritten).DoesNotContain("auction_hoUSE");
    }

    [Test]
    public async Task RewriteBaseSchemaSql_AllowsQuotedMySqlDatabaseNamesWithHyphens()
    {
        const string sql = """
            CREATE DATABASE IF NOT EXISTS `aaemu_game`;
            USE `aaemu_game`;
            """;

        var rewritten = MySqlDatabaseBootstrap.RewriteBaseSchemaSql(sql, "aaemu-game");

        await Assert.That(rewritten).Contains("CREATE DATABASE IF NOT EXISTS `aaemu-game`");
        await Assert.That(rewritten).Contains("USE `aaemu-game`");
    }

    [Test]
    public async Task EnsureDatabase_ReportsInvalidQuotedIdentifierAsFailure()
    {
        var settings = new MySqlConnectionSettings { Database = "aaemu`game" };

        var result = MySqlDatabaseBootstrap.EnsureDatabase(settings, "aaemu_game.sql");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task SortUpdateFiles_UsesStableOrdinalIgnoreCaseOrder()
    {
        var files = new List<string>
        {
            @"D:\temp\2026-05-13_aaemu_game_character_cooldowns_expires_at_index.sql",
            @"D:\temp\2026-05-13_aaemu_game_character_cooldowns.sql"
        };

        MySqlDatabaseUpdater.SortUpdateFiles(files);

        await Assert.That(files[0]).IsEqualTo(@"D:\temp\2026-05-13_aaemu_game_character_cooldowns.sql");
        await Assert.That(files[1]).IsEqualTo(@"D:\temp\2026-05-13_aaemu_game_character_cooldowns_expires_at_index.sql");
    }

    [Test]
    public async Task SplitBaseSchemaStatements_KeepsSemicolonsInsideLiteralsAndSkipsComments()
    {
        const string sql = """
            -- This comment must not be sent as a command;
            CREATE TABLE `character_butler_permanent_data` (`id` int NOT NULL)
            COMMENT='Farmhand permanent-data map; serialized to the client';
            CREATE TABLE `characters` (`id` int NOT NULL);
            """;

        var statements = MySqlDatabaseBootstrap.SplitBaseSchemaStatements(sql);

        await Assert.That(statements.Count).IsEqualTo(2);
        await Assert.That(statements[0]).Contains("COMMENT='Farmhand permanent-data map; serialized to the client'");
        await Assert.That(statements[1]).IsEqualTo("CREATE TABLE `characters` (`id` int NOT NULL)");
    }
}
