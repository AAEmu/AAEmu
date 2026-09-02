using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Utils.DB;

public class CompactSqliteUpdaterTests
{
    [Test]
    public async Task Apply_UpdatesMatchingKeys_InsertsNew_KeepsExtraRows()
    {
        var root = NewTempRoot();
        WriteDb(root.Db, """
            CREATE TABLE items (id INTEGER PRIMARY KEY, max_enchantable_grade INTEGER);
            CREATE TABLE tower_defs (id INTEGER PRIMARY KEY, name TEXT);
            INSERT INTO items (id, max_enchantable_grade) VALUES (1, 7), (2, 7);
            INSERT INTO tower_defs (id, name) VALUES (3, 'local-only');
            """);
        WriteScript(root.Scripts, "2026-09-03_compact_r584_to_r589.sql", """
            -- compact_table: items
            UPDATE "items" SET "max_enchantable_grade" = 12 WHERE "id" = 1;
            -- compact_table: tower_defs
            INSERT INTO "tower_defs" ("id", "name") VALUES (65, 'kraken')
            ON CONFLICT("id") DO UPDATE SET "name" = excluded."name";
            """);

        var first = CompactSqliteUpdater.Apply(root.Db, root.Scripts);
        await Assert.That(first.Success).IsTrue();
        await Assert.That(first.ScriptsApplied).IsEqualTo(1);
        await Assert.That(first.StatementsExecuted).IsEqualTo(2);

        await using (var connection = Open(root.Db))
        {
            await Assert.That(Scalar<long>(connection, "SELECT max_enchantable_grade FROM items WHERE id = 1")).IsEqualTo(12);
            await Assert.That(Scalar<long>(connection, "SELECT max_enchantable_grade FROM items WHERE id = 2")).IsEqualTo(7);
            await Assert.That(Scalar<string>(connection, "SELECT name FROM tower_defs WHERE id = 65")).IsEqualTo("kraken");
            await Assert.That(Scalar<string>(connection, "SELECT name FROM tower_defs WHERE id = 3")).IsEqualTo("local-only");
        }

        var second = CompactSqliteUpdater.Apply(root.Db, root.Scripts);
        await Assert.That(second.Success).IsTrue();
        await Assert.That(second.ScriptsApplied).IsEqualTo(0);
        await Assert.That(second.ScriptsAlreadyInstalled).IsEqualTo(1);
        await Assert.That(second.StatementsExecuted).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_MissingTableSection_IsSkipped()
    {
        var root = NewTempRoot();
        WriteDb(root.Db, """
            CREATE TABLE items (id INTEGER PRIMARY KEY, max_enchantable_grade INTEGER);
            INSERT INTO items (id, max_enchantable_grade) VALUES (1, 7);
            """);
        WriteScript(root.Scripts, "2026-01-01_compact_demo.sql", """
            -- compact_table: not_in_this_db
            UPDATE "not_in_this_db" SET "x" = 1 WHERE "id" = 1;
            -- compact_table: items
            UPDATE "items" SET "max_enchantable_grade" = 12 WHERE "id" = 1;
            """);

        var result = CompactSqliteUpdater.Apply(root.Db, root.Scripts);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.TablesSkippedMissing).IsEqualTo(1);
        await Assert.That(result.StatementsExecuted).IsEqualTo(1);
        await using var connection = Open(root.Db);
        await Assert.That(Scalar<long>(connection, "SELECT max_enchantable_grade FROM items WHERE id = 1")).IsEqualTo(12);
    }

    [Test]
    public async Task Apply_InsertConflict_UpdatesExistingRow()
    {
        var root = NewTempRoot();
        WriteDb(root.Db, """
            CREATE TABLE instances (id INTEGER PRIMARY KEY, show_ui TEXT, extra TEXT);
            INSERT INTO instances (id, show_ui, extra) VALUES (14, 'f', 'keep-me');
            """);
        WriteScript(root.Scripts, "2026-01-02_compact_instances.sql", """
            -- compact_table: instances
            INSERT INTO "instances" ("id", "show_ui") VALUES (14, 't')
            ON CONFLICT("id") DO UPDATE SET "show_ui" = excluded."show_ui";
            """);

        var result = CompactSqliteUpdater.Apply(root.Db, root.Scripts);
        await Assert.That(result.Success).IsTrue();
        await using var connection = Open(root.Db);
        await Assert.That(Scalar<string>(connection, "SELECT show_ui FROM instances WHERE id = 14")).IsEqualTo("t");
        await Assert.That(Scalar<string>(connection, "SELECT extra FROM instances WHERE id = 14")).IsEqualTo("keep-me");
    }

    [Test]
    public async Task FindScriptsDirectory_WalksParents()
    {
        var root = NewTempRoot();
        WriteScript(root.Scripts, "2026-01-03_compact_find.sql", "-- compact_table: items\nSELECT 1;");
        var nested = Path.Combine(root.Root, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(nested);

        var found = CompactSqliteUpdater.FindScriptsDirectory(nested);
        await Assert.That(Path.GetFullPath(found!)).IsEqualTo(Path.GetFullPath(root.Scripts));
    }

    [Test]
    public async Task FindScriptsDirectory_UsesLaterRootWhenContentRootHasNone()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "aaemu-compact-tests", Guid.NewGuid().ToString("N"), "game-content");
        var worldRoot = Path.Combine(Path.GetTempPath(), "aaemu-compact-tests", Guid.NewGuid().ToString("N"), "world");
        Directory.CreateDirectory(contentRoot);
        var worldScripts = Path.Combine(worldRoot, "SQL", "compact");
        Directory.CreateDirectory(worldScripts);
        WriteScript(worldScripts, "2026-01-04_compact_world.sql", "-- compact_table: items\nSELECT 1;");

        var found = CompactSqliteUpdater.FindScriptsDirectory(contentRoot, worldRoot);
        await Assert.That(Path.GetFullPath(found!)).IsEqualTo(Path.GetFullPath(worldScripts));
    }

    [Test]
    public async Task ApplyAt_CompactPresentScriptsMissing_Fails()
    {
        var isolated = Path.Combine(Path.GetTempPath(), "aaemu-compact-tests", Guid.NewGuid().ToString("N"), "isolated");
        Directory.CreateDirectory(isolated);
        var db = Path.Combine(isolated, "compact.sqlite3");
        WriteDb(db, "CREATE TABLE items (id INTEGER PRIMARY KEY);");

        var result = CompactSqliteUpdater.ApplyAt(db, isolated);
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Errors.Count > 0).IsTrue();
        await Assert.That(result.ScriptsApplied).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_FailedStatement_RollsBackContentAndDoesNotMarkInstalled()
    {
        var root = NewTempRoot();
        WriteDb(root.Db, """
            CREATE TABLE items (id INTEGER PRIMARY KEY, max_enchantable_grade INTEGER);
            INSERT INTO items (id, max_enchantable_grade) VALUES (1, 7);
            """);
        WriteScript(root.Scripts, "2026-01-05_compact_bad.sql", """
            -- compact_table: items
            UPDATE "items" SET "max_enchantable_grade" = 12 WHERE "id" = 1;
            UPDATE "items" SET "no_such_column" = 1 WHERE "id" = 1;
            """);

        var result = CompactSqliteUpdater.Apply(root.Db, root.Scripts);
        await Assert.That(result.Success).IsFalse();
        await using var connection = Open(root.Db);
        await Assert.That(Scalar<long>(connection, "SELECT max_enchantable_grade FROM items WHERE id = 1")).IsEqualTo(7);
        await Assert.That(Scalar<long>(connection, $"SELECT COUNT(*) FROM {CompactSqliteUpdater.TrackingTable} WHERE installed = 1")).IsEqualTo(0);
    }

    private static (string Root, string Db, string Scripts) NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "aaemu-compact-tests", Guid.NewGuid().ToString("N"));
        var scripts = Path.Combine(root, "SQL", "compact");
        Directory.CreateDirectory(scripts);
        return (root, Path.Combine(root, "compact.sqlite3"), scripts);
    }

    private static void WriteDb(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        foreach (var statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
    }

    private static void WriteScript(string directory, string name, string sql)
    {
        File.WriteAllText(Path.Combine(directory, name), sql);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}; Mode=ReadOnly");
        connection.Open();
        return connection;
    }

    private static T Scalar<T>(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T));
    }
}
