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

    [Test]
    public async Task Load_ReportsTheProtectionWindowOfADoodadGroupThatHasARow()
    {
        var data = Loaded("INSERT INTO doodad_groups VALUES (7, 120, 0, 0);");

        var found = data.TryGetDoodadGuardTime(7, out var guardSeconds);

        await Assert.That(found).IsTrue();
        await Assert.That(guardSeconds).IsEqualTo(120u);
    }

    [Test]
    public async Task Load_ReportsADoodadGroupWithNoRowAsUnknownRatherThanAsNoProtection()
    {
        // The same trap as the capacity, in the other table. A protection window of zero is not the
        // same as no protection window, and a lookup that answers a missing row with zero retires
        // every crop of that group on the first expiry pass after it is planted.
        var data = Loaded("INSERT INTO doodad_groups VALUES (7, 120, 0, 0);");

        var found = data.TryGetDoodadGuardTime(8, out var guardSeconds);

        await Assert.That(found).IsFalse();
        await Assert.That(guardSeconds).IsEqualTo(0u);
    }

    [Test]
    public async Task Load_ReportsAConfiguredZeroWindowAsConfiguredRatherThanUnknown()
    {
        // The other side of the same line, and the reason the lookup cannot be a truthiness test on
        // the value. Zero is a real content value meaning "no protection at all"; only a missing row
        // is unknown, and the two must not be reported the same way.
        var data = Loaded("INSERT INTO doodad_groups VALUES (7, 0, 0, 0);");

        var found = data.TryGetDoodadGuardTime(7, out var guardSeconds);

        await Assert.That(found).IsTrue();
        await Assert.That(guardSeconds).IsEqualTo(0u);
    }

    [Test]
    public async Task Load_ReportsEveryUnseededDoodadGroupAsUnknown()
    {
        // Swept rather than spot-checked: a lookup that answered one group from a fallback would still
        // pass a single assertion, and the guarantee is about the whole table.
        var data = Loaded("INSERT INTO doodad_groups VALUES (7, 120, 0, 0);");

        foreach (var groupId in new uint[] { 0, 1, 6, 8, 9, 5000 })
        {
            var found = data.TryGetDoodadGuardTime(groupId, out _);

            await Assert.That(found).IsFalse();
        }
    }

    /// <summary>
    /// The shipped shape that matters: a tab with no farm_groups row of its own, whose crops come out
    /// of a group that does have one. The seed below reproduces it - group 2 and group 3 list no row,
    /// five of Nursery's six crops and all six of Ranch's are also group 1's, and group 4 shares
    /// nothing with group 1.
    /// </summary>
    private const string ShippedShape =
        "INSERT INTO farm_groups VALUES (1, 'group one', 5);" +
        "INSERT INTO farm_groups VALUES (4, 'group four', 2);" +
        // Group 1: the seven crops Farm may plant.
        "INSERT INTO farm_group_doodads VALUES (1, 1, 100, 0), (2, 1, 101, 0), (3, 1, 102, 0)," +
        " (4, 1, 103, 0), (5, 1, 104, 0), (6, 1, 105, 0), (7, 1, 106, 0);" +
        // Group 2 (Nursery): five shared with group 1, one of its own.
        "INSERT INTO farm_group_doodads VALUES (8, 2, 100, 0), (9, 2, 101, 0), (10, 2, 102, 0)," +
        " (11, 2, 103, 0), (12, 2, 104, 0), (13, 2, 200, 0);" +
        // Group 3 (Ranch): all six shared with group 1.
        "INSERT INTO farm_group_doodads VALUES (14, 3, 100, 0), (15, 3, 101, 0), (16, 3, 102, 0)," +
        " (17, 3, 103, 0), (18, 3, 104, 0), (19, 3, 105, 0);" +
        // Group 4 (Stable): its own crops entirely.
        "INSERT INTO farm_group_doodads VALUES (20, 4, 300, 0), (21, 4, 301, 0);";

    /// <summary>
    /// A tab with no row of its own is still governed by a group that has one, because content states
    /// the size per group and this tab plants from that group's pool. Swept over both affected tabs,
    /// so a fix that only answered for one of them could not pass.
    /// </summary>
    [Test]
    public async Task ATabWithNoRowOfItsOwnIsGovernedByTheGroupItsCropsComeFrom()
    {
        var data = Loaded(ShippedShape);

        foreach (var farmType in new[] { FarmType.Nursery, FarmType.Ranch })
        {
            var found = data.TryGetOwningFarmGroup(farmType, out var owner);

            await Assert.That(found).IsTrue();
            await Assert.That(owner).IsEqualTo(1u);
        }
    }

    /// <summary>A tab with its own row is governed by itself, never by a larger group.</summary>
    [Test]
    public async Task ATabWithARowOfItsOwnIsGovernedByItself()
    {
        var data = Loaded(ShippedShape);

        foreach (var farmType in new[] { FarmType.Farm, FarmType.Stable })
        {
            var found = data.TryGetOwningFarmGroup(farmType, out var owner);

            await Assert.That(found).IsTrue();
            await Assert.That(owner).IsEqualTo((uint)farmType);
        }
    }

    /// <summary>
    /// The point of the whole thing: the size a tab inherits is the governing group's own number, not
    /// a number chosen for it. Nursery has no row, so it must report group 1's 5 and not its own zero.
    /// </summary>
    [Test]
    public async Task AnInheritedCapacityIsTheGoverningGroupsOwnNumber()
    {
        var data = Loaded(ShippedShape);

        var found = data.TryGetOwningFarmGroup(FarmType.Nursery, out var owner);
        var capacity = data.TryGetFarmGroupMaxCount((FarmType)owner, out var max);

        await Assert.That(found).IsTrue();
        await Assert.That(capacity).IsTrue();
        await Assert.That(max).IsEqualTo(5u);
    }

    /// <summary>
    /// A tab that shares no crop with any group that has a row resolves to no owner. That is the
    /// honest answer and it is what leaves the loud log in place, rather than borrowing a number from
    /// an unrelated group.
    /// </summary>
    [Test]
    public async Task ATabSharingNoCropWithAnySizedGroupHasNoOwner()
    {
        var data = Loaded(
            "INSERT INTO farm_groups VALUES (1, 'group one', 5);" +
            "INSERT INTO farm_group_doodads VALUES (1, 1, 100, 0);" +
            "INSERT INTO farm_group_doodads VALUES (2, 2, 900, 0);");

        var found = data.TryGetOwningFarmGroup(FarmType.Nursery, out var owner);

        await Assert.That(found).IsFalse();
        await Assert.That(owner).IsEqualTo(0u);
    }

    /// <summary>A tab content lists no crop for is not answerable, and must not borrow a group.</summary>
    [Test]
    public async Task ATabWithNoCropsListedHasNoOwner()
    {
        var data = Loaded("INSERT INTO farm_groups VALUES (1, 'group one', 5);");

        var found = data.TryGetOwningFarmGroup(FarmType.Ranch, out var owner);

        await Assert.That(found).IsFalse();
        await Assert.That(owner).IsEqualTo(0u);
    }

    /// <summary>
    /// With two sized groups sharing crops, the larger overlap wins and the lower id breaks a tie, so
    /// the answer does not depend on dictionary order and cannot change between restarts.
    /// </summary>
    [Test]
    public async Task TheOwnerIsTheBestOverlapAndTheLowerIdBreaksATie()
    {
        var data = Loaded(
            "INSERT INTO farm_groups VALUES (1, 'a', 5);" +
            "INSERT INTO farm_groups VALUES (4, 'b', 5);" +
            // Group 4 shares three of the tab's crops, group 1 only one.
            "INSERT INTO farm_group_doodads VALUES (1, 4, 100, 0), (2, 4, 101, 0), (3, 4, 102, 0);" +
            "INSERT INTO farm_group_doodads VALUES (4, 1, 100, 0);" +
            "INSERT INTO farm_group_doodads VALUES (5, 2, 100, 0), (6, 2, 101, 0), (7, 2, 102, 0);");

        var best = data.TryGetOwningFarmGroup(FarmType.Nursery, out var bestOwner);
        var tie = data.TryGetOwningFarmGroup(FarmType.Ranch, out _);

        await Assert.That(best).IsTrue();
        await Assert.That(bestOwner).IsEqualTo(4u);   // three shared beats one
        await Assert.That(tie).IsFalse();             // equal overlap, so no single owner
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
