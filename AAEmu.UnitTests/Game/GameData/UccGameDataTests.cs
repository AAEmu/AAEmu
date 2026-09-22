using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>Which item templates can carry a crest, read from the applicable list by its kind name.</summary>
public sealed class UccGameDataTests : SqliteTestBase
{
    private const uint Cloak = 16249;
    private const uint OtherCloak = 16359;
    private const uint DanglingItem = 99999;
    private const uint DoodadTarget = 70001;

    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute("CREATE TABLE enum_ucc_applicable_kinds (id INTEGER PRIMARY KEY, name TEXT)");
        Execute("CREATE TABLE ucc_applicables (id INTEGER PRIMARY KEY, name TEXT, kind_id INTEGER, actual_id INTEGER, tooltip_msg TEXT)");
        Execute("CREATE TABLE items (id INTEGER PRIMARY KEY)");
    }

    private void SeedContent(bool withItemKind = true)
    {
        Execute("INSERT INTO enum_ucc_applicable_kinds (id, name) VALUES (1, 'slave')");
        if (withItemKind)
            Execute("INSERT INTO enum_ucc_applicable_kinds (id, name) VALUES (2, 'item')");
        Execute("INSERT INTO enum_ucc_applicable_kinds (id, name) VALUES (3, 'doodad')");

        foreach (var id in new[] { Cloak, OtherCloak, DoodadTarget })
            Execute($"INSERT INTO items (id) VALUES ({id})");

        Execute($"INSERT INTO ucc_applicables (id, name, kind_id, actual_id, tooltip_msg) VALUES (5, 'item', 2, {Cloak}, '')");
        Execute($"INSERT INTO ucc_applicables (id, name, kind_id, actual_id, tooltip_msg) VALUES (6, 'item', 2, {OtherCloak}, '')");
        // Rows naming no item: skipped.
        Execute($"INSERT INTO ucc_applicables (id, name, kind_id, actual_id, tooltip_msg) VALUES (7, 'item', 2, {DanglingItem}, '')");
        Execute("INSERT INTO ucc_applicables (id, name, kind_id, actual_id, tooltip_msg) VALUES (8, 'item', 2, 0, '')");
        // Another kind: its id names something that is not an item template.
        Execute($"INSERT INTO ucc_applicables (id, name, kind_id, actual_id, tooltip_msg) VALUES (9, 'doodad', 3, {DoodadTarget}, '')");
    }

    [Test]
    public async Task Load_KeepsTheItemKindsRowsThatNameAnItem()
    {
        SeedContent();
        var data = new UccGameData();
        data.Load(Connection);

        await Assert.That(data.TakesCrest(Cloak)).IsTrue();
        await Assert.That(data.TakesCrest(OtherCloak)).IsTrue();
        await Assert.That(data.TakesCrest(DanglingItem)).IsFalse();
        await Assert.That(data.TakesCrest(0)).IsFalse();
        await Assert.That(data.TakesCrest(DoodadTarget)).IsFalse();
        await Assert.That(data.CrestItemCount).IsEqualTo(2);
    }

    [Test]
    public async Task Load_WithoutTheItemKindRow_NoItemTakesACrest()
    {
        SeedContent(withItemKind: false);
        var data = new UccGameData();
        data.Load(Connection);

        await Assert.That(data.TakesCrest(Cloak)).IsFalse();
        await Assert.That(data.CrestItemCount).IsEqualTo(0);
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
