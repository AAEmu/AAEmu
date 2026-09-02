using AAEmu.Game.Utils.DB;

namespace AAEmu.UnitTests.Game.Utils.DB;

public class CompactSqlScriptTests
{
    [Test]
    public async Task Parse_SplitsOnSemicolon_KeepsQuotedNewlineAndSemicolon()
    {
        const string sql = """
            -- compact_table: instances
            UPDATE "instances" SET "desc" = 'line1
            still; inside' WHERE "id" = 14;
            INSERT INTO "tower_defs" ("id") VALUES (65);
            """;

        var statements = CompactSqlScript.Parse(sql);

        await Assert.That(statements.Count).IsEqualTo(2);
        await Assert.That(statements[0].Table).IsEqualTo("instances");
        await Assert.That(statements[0].Sql.Contains("still; inside")).IsTrue();
        await Assert.That(statements[1].Table).IsEqualTo("instances");
        await Assert.That(statements[1].Sql.StartsWith("INSERT", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Parse_TableMarker_ScopesFollowingStatements()
    {
        const string sql = """
            -- compact_table: items
            UPDATE "items" SET "max_enchantable_grade" = 12 WHERE "id" = 1;
            -- compact_table: zones
            UPDATE "zones" SET "closed" = 'f' WHERE "id" = 148;
            """;

        var statements = CompactSqlScript.Parse(sql);

        await Assert.That(statements.Count).IsEqualTo(2);
        await Assert.That(statements[0].Table).IsEqualTo("items");
        await Assert.That(statements[1].Table).IsEqualTo("zones");
    }

    [Test]
    public async Task Parse_DoubledQuote_StaysInsideString()
    {
        const string sql = "UPDATE t SET n = 'it''s' WHERE id = 1;";
        var statements = CompactSqlScript.Parse(sql);
        await Assert.That(statements.Count).IsEqualTo(1);
        await Assert.That(statements[0].Sql).IsEqualTo("UPDATE t SET n = 'it''s' WHERE id = 1");
    }

    [Test]
    public async Task ShippedR584ToR589_ParsesExpectedKeys()
    {
        var dir = CompactSqliteUpdater.FindScriptsDirectory(AppContext.BaseDirectory);
        await Assert.That(dir).IsNotNull();
        var path = Path.Combine(dir!, "2026-09-03_compact_r584_to_r589.sql");
        await Assert.That(File.Exists(path)).IsTrue();

        var statements = CompactSqlScript.Parse(File.ReadAllText(path));
        await Assert.That(statements.Count > 10000).IsTrue();
        await Assert.That(statements.Any(s => s.Table == "tower_defs" && s.Sql.Contains("65"))).IsTrue();
        await Assert.That(statements.Any(s => s.Table == "game_schedules" && s.Sql.Contains("1010"))).IsTrue();
        await Assert.That(statements.Any(s => s.Table == "instances" && s.Sql.Contains("(14,"))).IsTrue();
    }
}
