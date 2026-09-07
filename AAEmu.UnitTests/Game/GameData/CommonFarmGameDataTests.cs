using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.GameData;

public class CommonFarmGameDataTests
{
    [Test]
    public async Task GetFarmGuardTime_MatchingZoneAndKind_UsesMatchingRow()
    {
        using var data = new CommonFarmTestData();
        data.InsertFarm(1, FarmGroupKind.Farm, 3_600_000, "zone 100");
        data.InsertFarm(2, FarmGroupKind.Ranch, 7_200_000, "zone 200");
        data.InsertFarm(3, FarmGroupKind.Farm, 10_800_000, "zone 200");
        var gameData = new CommonFarmGameData();
        gameData.Load(data.Connection);

        await Assert.That(gameData.GetFarmGuardTime(FarmGroupKind.Farm, 200)).IsEqualTo(10_800_000u);
        await Assert.That(gameData.GetFarmGuardTime(FarmGroupKind.Ranch, 200)).IsEqualTo(7_200_000u);
    }

    [Test]
    public async Task GetFarmGuardTime_ZoneNotFound_FallsBackToSameKind()
    {
        using var data = new CommonFarmTestData();
        data.InsertFarm(1, FarmGroupKind.Ranch, 7_200_000, "zone 100");
        data.InsertFarm(2, FarmGroupKind.Farm, 3_600_000, "zone 200");
        var gameData = new CommonFarmGameData();
        gameData.Load(data.Connection);

        await Assert.That(gameData.GetFarmGuardTime(FarmGroupKind.Farm, 300)).IsEqualTo(3_600_000u);
    }

    [Test]
    [Arguments(FarmGroupKind.Invalid)]
    [Arguments(FarmGroupKind.Stable)]
    public async Task GetFarmGuardTime_KindNotFound_ReturnsZero(FarmGroupKind kind)
    {
        using var data = new CommonFarmTestData();
        data.InsertFarm(1, FarmGroupKind.Farm, 3_600_000, "zone 100");
        var gameData = new CommonFarmGameData();
        gameData.Load(data.Connection);

        await Assert.That(gameData.GetFarmGuardTime(kind, 100)).IsEqualTo(0u);
    }

    [Test]
    public async Task Load_ReloadedData_DoesNotRetainOldGuardTimes()
    {
        using var data = new CommonFarmTestData();
        data.InsertFarm(1, FarmGroupKind.Farm, 3_600_000, "zone 100");
        var gameData = new CommonFarmGameData();
        gameData.Load(data.Connection);

        data.Execute("DELETE FROM common_farms");
        data.InsertFarm(1, FarmGroupKind.Ranch, 7_200_000, "zone 100");
        gameData.Load(data.Connection);

        await Assert.That(gameData.GetFarmGuardTime(FarmGroupKind.Farm, 100)).IsEqualTo(0u);
        await Assert.That(gameData.GetFarmGuardTime(FarmGroupKind.Ranch, 100)).IsEqualTo(7_200_000u);
    }

    [Test]
    public async Task Load_FarmGroupData_PreservesPlacementLimitsAndAllowedDoodads()
    {
        using var data = new CommonFarmTestData();
        data.Execute("""
            INSERT INTO farm_groups (id, count) VALUES (1, 5), (3, 10);
            INSERT INTO farm_group_doodads (id, farm_group_id, doodad_id, item_id)
            VALUES (1, 1, 1001, 2001), (2, 1, 1002, 2002), (3, 3, 1003, 2003);
            """);
        var gameData = new CommonFarmGameData();
        gameData.Load(data.Connection);

        await Assert.That(gameData.GetFarmGroupMaxCount(FarmGroupKind.Farm)).IsEqualTo(5u);
        await Assert.That(gameData.GetAllowedDoodads(FarmGroupKind.Farm)).IsEquivalentTo(new uint[] { 1001, 1002 });
        await Assert.That(gameData.GetFarmGroupMaxCount(FarmGroupKind.Ranch)).IsEqualTo(10u);
        await Assert.That(gameData.GetAllowedDoodads(FarmGroupKind.Ranch)).IsEquivalentTo(new uint[] { 1003 });
    }
}
