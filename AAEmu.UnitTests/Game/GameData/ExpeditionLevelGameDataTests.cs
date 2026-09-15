using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

public class ExpeditionLevelGameDataTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute("""
            CREATE TABLE expedition_levels (
                id INTEGER PRIMARY KEY, total_exp INTEGER, daily_exp INTEGER, member_limit INTEGER,
                summon_limit INTEGER, require_item_id INTEGER, require_item_amount INTEGER,
                daily_contribution_point INTEGER, portal_point_limit INTEGER);
            CREATE TABLE enum_content_configs (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE content_configs (id INTEGER PRIMARY KEY, kind_id INTEGER, value INTEGER);
            """);
    }

    [Test]
    public async Task Load_UsesConfiguredAccessibleLevelCap()
    {
        Execute("""
            INSERT INTO expedition_levels VALUES
                (1,0,0,10,0,0,0,0,0),
                (2,100,100,20,0,0,0,0,0),
                (3,200,100,30,0,0,0,0,0),
                (4,300,100,40,0,0,0,0,0);
            INSERT INTO enum_content_configs VALUES (77,'expedition_level_max');
            INSERT INTO content_configs VALUES (77,25,3);
            """);
        var data = new ExpeditionLevelGameData();

        data.Load(Connection);
        data.PostLoad();

        await Assert.That(data.MaxLevel).IsEqualTo(3u);
        await Assert.That(data.GetLevel(3)).IsNotNull();
        await Assert.That(data.GetLevel(4)).IsNull();
        await Assert.That(data.GetAutoLevelForExp(1, long.MaxValue)).IsEqualTo(3u);
    }

    [Test]
    public async Task Load_RejectsMissingRequiredLevelCap()
    {
        Execute("INSERT INTO expedition_levels VALUES (1,0,0,10,0,0,0,0,0);");
        var data = new ExpeditionLevelGameData();

        await Assert.That(() => data.Load(Connection)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Load_RejectsZeroLevelCap()
    {
        Execute("""
            INSERT INTO expedition_levels VALUES (1,0,0,10,0,0,0,0,0);
            INSERT INTO enum_content_configs VALUES (77,'expedition_level_max');
            INSERT INTO content_configs VALUES (77,25,0);
            """);
        var data = new ExpeditionLevelGameData();

        await Assert.That(() => data.Load(Connection)).Throws<InvalidDataException>();
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
