using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class PublicFarmGuardTimeTests
{
    private static readonly DateTime s_plantTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly FieldInfo s_instanceField = typeof(Singleton<CommonFarmGameData>)
        .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(Singleton<CommonFarmGameData>).FullName, "s_instance");

    private CommonFarmTestData _data;
    private CommonFarmGameData _gameData;
    private CommonFarmGameData _previousInstance;

    [Before(Test)]
    public void Setup()
    {
        _data = new CommonFarmTestData();
        _gameData = new CommonFarmGameData();
        // The public entry point still resolves game data through its singleton.
        _previousInstance = (CommonFarmGameData)s_instanceField.GetValue(null);
        s_instanceField.SetValue(null, _gameData);
    }

    [After(Test)]
    public void Teardown()
    {
        s_instanceField.SetValue(null, _previousInstance);
        _data.Dispose();
    }

    [Test]
    [Arguments(-1, true)]
    [Arguments(0, false)]
    [Arguments(1, false)]
    public async Task IsProtectedByPublicFarm_AtExpiryBoundary_ExpiresAtDeadline(int ticksFromExpiry, bool expected)
    {
        LoadFarm(1500);
        var doodad = CreateDoodad(s_plantTime);
        var now = s_plantTime.AddMilliseconds(1500).AddTicks(ticksFromExpiry);

        await Assert.That(PublicFarmManager.IsProtectedByPublicFarm(doodad, now)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(3_600_000u, 1800, true)]
    [Arguments(3_600_000u, 7200, false)]
    [Arguments(172_800_000u, 129600, true)]
    [Arguments(uint.MaxValue, 2_147_484, true)]
    public async Task IsProtectedByPublicFarm_ConfiguredDuration_UsesMilliseconds(uint guardTime, int ageSeconds, bool expected)
    {
        LoadFarm(guardTime);
        var doodad = CreateDoodad(s_plantTime);

        await Assert.That(PublicFarmManager.IsProtectedByPublicFarm(doodad, s_plantTime.AddSeconds(ageSeconds))).IsEqualTo(expected);
    }

    [Test]
    [Arguments(-3600)]
    [Arguments(3600)]
    public async Task IsProtectedByPublicFarm_ZeroDuration_IsNeverProtected(int ageSeconds)
    {
        LoadFarm(0);
        var doodad = CreateDoodad(s_plantTime);

        await Assert.That(PublicFarmManager.IsProtectedByPublicFarm(doodad, s_plantTime.AddSeconds(ageSeconds))).IsFalse();
    }

    [Test]
    [Arguments(FarmGroupKind.Invalid)]
    [Arguments(FarmGroupKind.Stable)]
    public async Task IsProtectedByPublicFarm_MissingMapping_IsNotProtected(FarmGroupKind kind)
    {
        LoadFarm(3_600_000);
        var doodad = CreateDoodad(s_plantTime, kind);

        await Assert.That(PublicFarmManager.IsProtectedByPublicFarm(doodad, s_plantTime)).IsFalse();
    }

    [Test]
    [Arguments(0u)]
    [Arguments(86_400u)]
    public async Task IsProtectedByPublicFarm_ExpiredFarm_IgnoresDoodadGroupDuration(uint fieldGuardTime)
    {
        LoadFarm(3_600_000);
        var doodad = CreateDoodad(s_plantTime);
        doodad.Template.Group = new DoodadGroups { GuardOnFieldTime = fieldGuardTime };

        await Assert.That(PublicFarmManager.IsProtectedByPublicFarm(doodad, s_plantTime.AddHours(2))).IsFalse();
    }

    [Test]
    public async Task PublicFarmTick_ConfiguredDurations_ReleasesOnlyExpiredFarmDoodads()
    {
        _data.InsertFarm(1, FarmGroupKind.Farm, 3_600_000, "zone 100");
        _data.InsertFarm(2, FarmGroupKind.Ranch, 172_800_000, "zone 100");
        _gameData.Load(_data.Connection);
        var (manager, world) = CreateManager();
        var now = DateTime.UtcNow;
        var expired = CreateDoodad(now.AddHours(-2));
        var expiredRanch = CreateDoodad(now.AddDays(-3), FarmGroupKind.Ranch);
        var protectedFarm = CreateDoodad(now.AddMinutes(-30));
        var protectedRanch = CreateDoodad(now.AddHours(-36), FarmGroupKind.Ranch);
        var outsideFarm = CreateDoodad(now.AddDays(-3), FarmGroupKind.Invalid);
        foreach (var doodad in new[] { expired, protectedFarm, expiredRanch, protectedRanch, outsideFarm })
            world.SpawnManager.AddPlayerDoodad(doodad);

        manager.PublicFarmTick();

        foreach (var doodad in new[] { expired, expiredRanch })
        {
            await Assert.That(doodad.OwnerId).IsEqualTo(0u);
            await Assert.That(doodad.OwnerType).IsEqualTo(DoodadOwnerType.System);
            await Assert.That(doodad.FarmType).IsEqualTo(FarmGroupKind.Invalid);
        }
        await Assert.That(world.SpawnManager.GetAllPlayerDoodads()).IsEquivalentTo(new[] { protectedFarm, protectedRanch, outsideFarm });
        foreach (var doodad in new[] { protectedFarm, protectedRanch, outsideFarm })
        {
            await Assert.That(doodad.OwnerId).IsEqualTo(42u);
            await Assert.That(doodad.OwnerType).IsEqualTo(DoodadOwnerType.Character);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublicFarmTick_ZeroOrMissingDuration_ReleasesOwnership(bool missingMapping)
    {
        if (missingMapping)
            _gameData.Load(_data.Connection);
        else
            LoadFarm(0);
        var (manager, world) = CreateManager();
        var doodad = CreateDoodad(DateTime.UtcNow.AddHours(1));
        world.SpawnManager.AddPlayerDoodad(doodad);

        manager.PublicFarmTick();

        await Assert.That(doodad.OwnerId).IsEqualTo(0u);
        await Assert.That(doodad.OwnerType).IsEqualTo(DoodadOwnerType.System);
        await Assert.That(doodad.FarmType).IsEqualTo(FarmGroupKind.Invalid);
        await Assert.That(world.SpawnManager.GetAllPlayerDoodads()).IsEmpty();
    }

    [Test]
    [Arguments(100u, false)]
    [Arguments(200u, true)]
    public async Task IsProtectedByPublicFarm_DifferentZones_UsesDoodadZone(uint zoneId, bool expected)
    {
        _data.InsertFarm(1, FarmGroupKind.Farm, 3_600_000, "zone 100");
        _data.InsertFarm(2, FarmGroupKind.Farm, 172_800_000, "zone 200");
        _gameData.Load(_data.Connection);
        var doodad = CreateDoodad(s_plantTime);
        doodad.Transform.ZoneId = zoneId;

        await Assert.That(PublicFarmManager.IsProtectedByPublicFarm(doodad, s_plantTime.AddHours(2))).IsEqualTo(expected);
    }

    private void LoadFarm(uint guardTime)
    {
        _data.InsertFarm(1, FarmGroupKind.Farm, guardTime, "zone 100");
        _gameData.Load(_data.Connection);
    }

    private static (PublicFarmManager Manager, WorldInstance World) CreateManager()
    {
        var world = new WorldInstance(new WorldTemplate(), 1, true, 0);
        // This fixture has no runtime services for the world finalizer to shut down.
        GC.SuppressFinalize(world);
        world.SpawnManager = new SpawnManager(world);
        var worldManager = Mock.Of<IWorldManager>();
        worldManager.GetWorld(WorldManager.DefaultInstanceId).Returns(world);
        var manager = new PublicFarmManager(Mock.Of<ITaskManager>().Object, worldManager.Object, Mock.Of<ISubZoneManager>().Object);
        return (manager, world);
    }

    private static Doodad CreateDoodad(DateTime plantTime, FarmGroupKind kind = FarmGroupKind.Farm)
    {
        var doodad = new Doodad
        {
            PlantTime = plantTime,
            FarmType = kind,
            OwnerId = 42,
            OwnerType = DoodadOwnerType.Character,
            Template = new DoodadTemplate { Group = new DoodadGroups { GuardOnFieldTime = 86_400 } }
        };
        doodad.Transform.ZoneId = 100;
        return doodad;
    }
}
