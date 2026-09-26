using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items.Loots;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

[NotInParallel]
public sealed class DropRuleGameDataTests
{
    [Test]
    public async Task LoadsTypedJoinAndReportsMetadataWithoutSelectingLoot()
    {
        using var connection = CreateDatabase(out var sharedUri);
        Seed(connection, packId: 500);
        var gameData = new DropRuleGameData(SharedConnection(sharedUri));
        gameData.Load(connection);
        gameData.PostLoad();

        var rule = gameData.Rules.Single();
        // The count is read before anything asks for a subject, so it is the cache size and must be zero:
        // the boot load does not touch the npcs table.
        await Assert.That(gameData.Diagnostics.NpcSubjectCount).IsEqualTo(0);
        var hasSubject = gameData.TryGetSubject(77, out var subject);
        var diagnostics = gameData.Diagnostics;

        await Assert.That(hasSubject).IsTrue();
        await Assert.That(subject.Level).IsEqualTo(12);
        await Assert.That(rule.MatcherId).IsEqualTo(10u);
        await Assert.That(rule.ForBatch).IsTrue();
        await Assert.That(rule.Memberships.Count).IsEqualTo(1);
        await Assert.That(rule.Matcher.Matches(subject)).IsTrue();
        await Assert.That(rule.IsMetadataValid).IsTrue();
        await Assert.That(diagnostics.RuleCount).IsEqualTo(1);
        await Assert.That(diagnostics.MembershipCount).IsEqualTo(1);
        await Assert.That(diagnostics.MissingLootPackMembershipCount).IsEqualTo(0);
        await Assert.That(diagnostics.ForBatchRuleCount).IsEqualTo(1);
        // Exactly the one that was asked for - the fixture has two npc rows and neither is loaded eagerly.
        await Assert.That(diagnostics.NpcSubjectCount).IsEqualTo(1);
    }

    [Test]
    public async Task BootLoadNeverQueriesTheNpcsTable()
    {
        // The defect: Load ran "SELECT ... FROM npcs" over all 19,522 shipped rows and kept every one, for a
        // map nothing read at startup. The fixture drops the table so any query against it fails loudly
        // rather than quietly returning nothing.
        using var connection = CreateDatabase(out _);
        Seed(connection, packId: 500);
        Execute(connection, "DROP TABLE npcs");
        var gameData = new DropRuleGameData(SharedConnection("Data Source=file:none"));

        gameData.Load(connection);
        gameData.PostLoad();

        await Assert.That(gameData.Rules.Count).IsEqualTo(1);
        await Assert.That(gameData.Diagnostics.NpcSubjectCount).IsEqualTo(0);
    }

    [Test]
    public async Task EachSubjectIsReadOnceAndThenServedFromTheCache()
    {
        using var connection = CreateDatabase(out var sharedUri);
        Seed(connection, packId: 500);
        var opened = 0;
        var gameData = new DropRuleGameData(() =>
        {
            opened++;
            var second = new SqliteConnection(sharedUri);
            second.Open();
            return second;
        });
        gameData.Load(connection);

        await Assert.That(gameData.TryGetSubject(77, out _)).IsTrue();
        var afterFirst = opened;
        await Assert.That(gameData.TryGetSubject(77, out _)).IsTrue();

        await Assert.That(afterFirst).IsEqualTo(1);
        await Assert.That(opened).IsEqualTo(1);
    }

    [Test]
    public async Task AMissingNpcIsNotCachedAndIsFoundAfterContentAddsIt()
    {
        using var connection = CreateDatabase(out var sharedUri);
        Seed(connection, packId: 500);
        var gameData = new DropRuleGameData(SharedConnection(sharedUri));
        gameData.Load(connection);

        await Assert.That(gameData.TryGetSubject(4242, out _)).IsFalse();

        Execute(connection, "INSERT INTO npcs VALUES (4242, 3, NULL, NULL, NULL, NULL, NULL, 'late', NULL, NULL, NULL, NULL)");

        await Assert.That(gameData.TryGetSubject(4242, out var subject)).IsTrue();
        await Assert.That(subject.Name).IsEqualTo("late");
    }

    [Test]
    public async Task MissingLootPackMembershipIsReportedAsInvalidMetadata()
    {
        using var connection = CreateDatabase(out var sharedUri);
        Seed(connection, packId: 999);
        Execute(connection, "DELETE FROM loot_packs WHERE id = 999");
        var gameData = new DropRuleGameData(SharedConnection(sharedUri));
        gameData.Load(connection);
        gameData.PostLoad();

        var rule = gameData.Rules.Single();
        var diagnostics = gameData.Diagnostics;

        await Assert.That(rule.IsMetadataValid).IsFalse();
        await Assert.That(rule.InvalidReason).Contains("absent from loot_packs");
        await Assert.That(diagnostics.MissingLootPackMembershipCount).IsEqualTo(1);
        await Assert.That(diagnostics.MissingLootPackIds).IsEquivalentTo(new uint[] { 999 });
        await Assert.That(diagnostics.InvalidRuleCount).IsEqualTo(1);
    }

    [Test]
    public async Task OrphanMembershipIsCountedWithoutInventingARule()
    {
        using var connection = CreateDatabase(out var sharedUri);
        Seed(connection, packId: 500);
        Execute(connection, "INSERT INTO drop_rule_loot_packs VALUES (2, 999, 500)");
        var gameData = new DropRuleGameData(SharedConnection(sharedUri));
        gameData.Load(connection);

        var diagnostics = gameData.Diagnostics;

        await Assert.That(gameData.Rules.Count).IsEqualTo(1);
        await Assert.That(diagnostics.MembershipCount).IsEqualTo(1);
        await Assert.That(diagnostics.OrphanMembershipCount).IsEqualTo(1);
    }

    [Test]
    public async Task RuleWithoutMembershipIsReportedButDoesNotEnterRuntimeLootFlow()
    {
        using var connection = CreateDatabase(out var sharedUri);
        Seed(connection, packId: 500);
        Execute(connection, "DELETE FROM drop_rule_loot_packs");
        var gameData = new DropRuleGameData(SharedConnection(sharedUri));
        gameData.Load(connection);
        gameData.PostLoad();

        var rule = gameData.Rules.Single();
        var diagnostics = gameData.Diagnostics;

        await Assert.That(rule.Matcher.IsValid).IsTrue();
        await Assert.That(rule.IsMetadataValid).IsFalse();
        await Assert.That(rule.InvalidReason).Contains("no loot-pack");
        await Assert.That(diagnostics.InvalidRuleCount).IsEqualTo(1);
    }

    [Test]
    public async Task NullableNpcFieldsRemainTypedAsNull()
    {
        using var connection = CreateDatabase(out var sharedUri);
        Seed(connection, packId: 500);
        Execute(connection,
            "INSERT INTO npcs VALUES (79, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL)");
        var gameData = new DropRuleGameData(SharedConnection(sharedUri));
        gameData.Load(connection);

        var hasSubject = gameData.TryGetSubject(79, out var subject);

        await Assert.That(hasSubject).IsTrue();
        await Assert.That(subject.Level).IsNull();
        await Assert.That(subject.Name).IsNull();
        await Assert.That(subject.Aggression).IsNull();
    }

    /// <summary>Opens a second handle on the fixture database, the way the on-demand subject read does.</summary>
    private static Func<SqliteConnection> SharedConnection(string sharedUri) => () =>
    {
        var connection = new SqliteConnection(sharedUri);
        connection.Open();
        return connection;
    };

    /// <summary>
    /// Opens the fixture database on a shared-cache URI, so the connection the loader is given and the
    /// connection TryGetSubject opens for itself are two handles on the *same* in-memory database. A plain
    /// "Data Source=:memory:" would give the second connection its own empty database.
    /// </summary>
    private static SqliteConnection CreateDatabase(out string sharedUri)
    {
        sharedUri = $"Data Source=file:drop-rule-{Guid.NewGuid():N}?mode=memory&cache=shared";
        var connection = new SqliteConnection(sharedUri);
        connection.Open();
        Execute(connection,
            """
            CREATE TABLE matchers (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                impl_type TEXT NOT NULL,
                impl_id INTEGER NOT NULL,
                parent_id INTEGER NULL
            );
            CREATE TABLE matcher_impl_sql_wheres (
                id INTEGER PRIMARY KEY,
                sql_where TEXT NOT NULL
            );
            CREATE TABLE drop_rules (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                matcher_id INTEGER NOT NULL,
                for_batch TEXT NOT NULL
            );
            CREATE TABLE drop_rule_loot_packs (
                id INTEGER PRIMARY KEY,
                drop_rule_id INTEGER NOT NULL,
                loot_pack_id INTEGER NOT NULL
            );
            CREATE TABLE loot_packs (
                id INTEGER PRIMARY KEY,
                name TEXT NULL,
                war_drop TEXT NULL,
                extra_gain_act_group_id INTEGER NOT NULL
            );
            CREATE TABLE npcs (
                id INTEGER PRIMARY KEY,
                level INTEGER NULL,
                npc_tendency_id INTEGER NULL,
                npc_grade_id INTEGER NULL,
                npc_kind_id INTEGER NULL,
                npc_nickname_id INTEGER NULL,
                heir_level INTEGER NULL,
                name TEXT NULL,
                comment1 TEXT NULL,
                comment2 TEXT NULL,
                comment3 TEXT NULL,
                aggression TEXT NULL
            );
            """);
        return connection;
    }

    private static void Seed(SqliteConnection connection, uint packId)
    {
        Execute(connection, "INSERT INTO matchers VALUES (10, 'test', 'MatcherImplSqlWhere', 20, NULL)");
        Execute(connection, "INSERT INTO matcher_impl_sql_wheres VALUES (20, 'level >= 10 AND npc_tendency_id = 2')");
        Execute(connection, "INSERT INTO drop_rules VALUES (100, 'test-rule', 10, 't')");
        Execute(connection, $"INSERT INTO drop_rule_loot_packs VALUES (1, 100, {packId})");
        Execute(connection, $"INSERT INTO loot_packs VALUES ({packId}, 'test-pack', 'f', 0)");
        Execute(connection,
            "INSERT INTO npcs VALUES (77, 12, 2, 1, 1, 0, 0, 'Test', '', 'field', '', 'f')");
        Execute(connection,
            "INSERT INTO npcs VALUES (78, 1, 2, 1, 1, 0, 0, 'Other', '', 'field', '', 'f')");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
