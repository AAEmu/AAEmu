using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.CommonFarm.Static;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// The farm capacity lookup, against the shipped column layout.
/// </summary>
/// <remarks>
/// Shipped content does not give every farm type a <c>farm_groups</c> row, so a lookup that answers
/// a missing row with a zero reports a full farm rather than an absent one. These pin the two apart
/// at the loader, which is the only place that knows whether the row was there.
/// </remarks>
[NotInParallel]
public class CommonFarmGameDataTests
{
    [Test]
    public async Task Load_ReportsTheCapacityOfAFarmGroupThatHasARow()
    {
        var data = Loaded("INSERT INTO farm_groups VALUES (1, 'group one', 5);");

        var found = data.TryGetFarmGroupMaxCount(FarmType.Farm, out var capacity);

        await Assert.That(found).IsTrue();
        await Assert.That(capacity).IsEqualTo(5u);
    }

    [Test]
    public async Task Load_ReportsAFarmGroupWithNoRowAsUnconfiguredRatherThanEmpty()
    {
        // The regression. Only one group is seeded, so asking about another must come back
        // "unconfigured" and not "a farm that holds zero crops".
        var data = Loaded("INSERT INTO farm_groups VALUES (1, 'group one', 5);");

        var found = data.TryGetFarmGroupMaxCount(FarmType.Nursery, out var capacity);

        await Assert.That(found).IsFalse();
        await Assert.That(capacity).IsEqualTo(0u);
    }

    [Test]
    public async Task Load_ReportsEveryUnseededFarmTypeAsUnconfigured()
    {
        // Swept rather than spot-checked: a lookup that hardcoded an answer for one farm type would
        // still pass a single assertion. The seeded group must be the only one that reports a size.
        var data = Loaded("INSERT INTO farm_groups VALUES (1, 'group one', 5);");

        foreach (var farmType in Enum.GetValues<FarmType>())
        {
            var found = data.TryGetFarmGroupMaxCount(farmType, out _);
            var expected = farmType == FarmType.Farm;

            await Assert.That(found).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task Load_DoesNotResolveAFarmGroupIdToTheFarmTypeEnumByPosition()
    {
        // farm_groups.id is the farm group id, and farm_group_doodads.farm_group_id points at it.
        // Group 4 is a real row, so it must be found even though only group 1 was named above:
        // the lookup is by id, not by ordinal position in the seeded set.
        var data = Loaded(
            "INSERT INTO farm_groups VALUES (1, 'group one', 5);" +
            "INSERT INTO farm_groups VALUES (4, 'group four', 2);");

        var found = data.TryGetFarmGroupMaxCount(FarmType.Stable, out var capacity);

        await Assert.That(found).IsTrue();
        await Assert.That(capacity).IsEqualTo(2u);
    }

    private static CommonFarmGameData Loaded(string extraRows)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        // The shipped column layout of the three tables the loader reads. The doodad rows are this
        // fixture's own and assert nothing about content; they only have to parse.
        command.CommandText = $$"""
                               CREATE TABLE farm_groups (id INTEGER, name TEXT, count INTEGER);
                               CREATE TABLE farm_group_doodads (
                                   id INTEGER, farm_group_id INTEGER, doodad_id INTEGER, item_id INTEGER);
                               CREATE TABLE doodad_groups (
                                   id INTEGER, guard_on_field_time INTEGER, is_export INTEGER, removed_by_house INTEGER);
                               {{extraRows}}
                               """;
        command.ExecuteNonQuery();

        var data = new CommonFarmGameData();
        data.Load(connection);
        return data;
    }
}
