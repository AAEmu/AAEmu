using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// The item_housings loader and complete-kit resolution. The table is what separates a plain
/// construction design from its completed-kit twin on the same design (the shipped rows pair e.g.
/// item 40190 / completion 'f' with item 43929 / completion 't' on design 644), and the resolution
/// fails loud when no row pairs the held item with the requested design: no row, no build.
/// </summary>
public class HousingItemHousingsTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        using var command = Connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE item_housings (
                id INTEGER PRIMARY KEY,
                item_id INTEGER NOT NULL,
                design_id INTEGER NOT NULL,
                completion TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task LoadItemHousings_MapsTheCompletionFlagFromPostgresStyleText()
    {
        using (var command = Connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO item_housings (id, item_id, design_id, completion)
                VALUES (1, 40190, 644, 'f'), (2, 43929, 644, 't');
                """;
            command.ExecuteNonQuery();
        }

        var rows = HousingGameData.LoadItemHousings(Connection);

        await Assert.That(rows.Count).IsEqualTo(2);
        var plain = rows.Single(r => r.Item_Id == 40190);
        await Assert.That(plain.Design_Id).IsEqualTo(644u);
        await Assert.That(plain.Completion).IsFalse();
        var completeKit = rows.Single(r => r.Item_Id == 43929);
        await Assert.That(completeKit.Design_Id).IsEqualTo(644u);
        await Assert.That(completeKit.Completion).IsTrue();
    }

    [Test]
    public async Task TryResolveCompleteKit_DistinguishesTheKitTwinOnTheSameDesign()
    {
        List<HousingItemHousings> rows =
        [
            new() { Id = 1, Item_Id = 40190, Design_Id = 644, Completion = false },
            new() { Id = 2, Item_Id = 43929, Design_Id = 644, Completion = true },
        ];

        var plainFound = HousingGameData.TryResolveCompleteKit(rows, 40190, 644, out var plainKit);
        var kitFound = HousingGameData.TryResolveCompleteKit(rows, 43929, 644, out var completeKit);

        await Assert.That(plainFound).IsTrue();
        await Assert.That(plainKit).IsFalse();
        await Assert.That(kitFound).IsTrue();
        await Assert.That(completeKit).IsTrue();
    }

    [Test]
    public async Task TryResolveCompleteKit_MissingContent_FailsLoudInsteadOfDefaulting()
    {
        List<HousingItemHousings> rows =
        [
            new() { Id = 1, Item_Id = 40190, Design_Id = 644, Completion = false },
        ];

        // Unknown item template: no row at all.
        var unknownItem = HousingGameData.TryResolveCompleteKit(rows, 999999, 644, out _);
        // The item exists but does not name the requested design (a mismatched pair the client
        // should never send — refused rather than placed as a guessed kit variant).
        var designMismatch = HousingGameData.TryResolveCompleteKit(rows, 40190, 645, out _);
        // No content loaded at all.
        var noContent = HousingGameData.TryResolveCompleteKit(null, 40190, 644, out _);

        await Assert.That(unknownItem).IsFalse();
        await Assert.That(designMismatch).IsFalse();
        await Assert.That(noContent).IsFalse();
    }
}
