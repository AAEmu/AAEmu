using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.UnitTests.Game.GameData;

public class HousingGameDataTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        using var command = Connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE housing_sizes (
                id INTEGER PRIMARY KEY,
                garden_radius REAL NOT NULL,
                housing_view_size_id INTEGER NOT NULL,
                butler_garden_size INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task LoadHousingSizes_MapsPlotAndFarmhandMetadataBySizeId()
    {
        using (var command = Connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO housing_sizes
                    (id, garden_radius, housing_view_size_id, butler_garden_size)
                VALUES
                    (2, 16.5, 7, 40),
                    (3, 24.0, 8, 180);
                """;
            command.ExecuteNonQuery();
        }

        var sizes = HousingGameData.LoadHousingSizes(Connection);

        await Assert.That(sizes.Count).IsEqualTo(2);
        await Assert.That(sizes[2].GardenRadius).IsEqualTo(16.5f);
        await Assert.That(sizes[2].HousingViewSizeId).IsEqualTo(7u);
        await Assert.That(sizes[2].ButlerGardenSize).IsEqualTo((ushort)40);
        await Assert.That(sizes[3].ButlerGardenSize).IsEqualTo((ushort)180);
    }

    [Test]
    public async Task BuildButlerGardenTemplates_UsesUniqueDesignGradeAndNamedUnderwaterCategory()
    {
        var land = new HousingTemplate
        {
            Id = 10,
            CategoryId = 16,
            ButlerHarvestGradeId = 1,
            HousingSize = new HousingSize { ButlerGardenSize = 0 }
        };
        var water = new HousingTemplate
        {
            Id = 20,
            CategoryId = 7,
            ButlerHarvestGradeId = 2,
            HousingSize = new HousingSize { ButlerGardenSize = 180 }
        };
        var noGrade = new HousingTemplate
        {
            Id = 30,
            CategoryId = 16,
            ButlerHarvestGradeId = 0,
            HousingSize = new HousingSize { ButlerGardenSize = 40 }
        };

        var templates = HousingGameData.BuildButlerGardenTemplates(
            [
                new HousingItemHousings { Item_Id = 100, Design_Id = 10 },
                new HousingItemHousings { Item_Id = 200, Design_Id = 20 },
                new HousingItemHousings { Item_Id = 300, Design_Id = 30 },
                new HousingItemHousings { Item_Id = 400, Design_Id = 10 },
                new HousingItemHousings { Item_Id = 400, Design_Id = 20 }
            ],
            new Dictionary<uint, HousingTemplate> { [10] = land, [20] = water, [30] = noGrade },
            new HashSet<uint> { 7 });

        await Assert.That(templates.ContainsKey(100)).IsTrue();
        await Assert.That(templates[100].GardenSize).IsEqualTo((ushort)0);
        await Assert.That(templates[100].IsUnderWater).IsFalse();
        await Assert.That(templates[200].ButlerHarvestGradeId).IsEqualTo(2u);
        await Assert.That(templates[200].IsUnderWater).IsTrue();
        await Assert.That(templates.ContainsKey(300)).IsFalse();
        await Assert.That(templates.ContainsKey(400)).IsFalse();
    }
}
