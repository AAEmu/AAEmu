using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;

using System.Reflection;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class IndunManagerTests
{
    private object _originalIndunZones;

    [Before(Test)]
    public void SaveIndunZones()
    {
        _originalIndunZones = GetIndunZonesField().GetValue(IndunGameData.Instance)!;
    }

    [After(Test)]
    public void RestoreIndunZones()
    {
        GetIndunZonesField().SetValue(IndunGameData.Instance, _originalIndunZones);
    }

    [Test]
    public async Task RequestLeaveInstance_RejectsCharacterOutsideCurrentDungeon()
    {
        var manager = new IndunManager(
            Mock.Of<ITickManager>().Object,
            Mock.Of<IWorldManager>().Object,
            Mock.Of<IZoneManager>().Object,
            Mock.Of<ITeamManager>().Object);

        var result = manager.RequestLeaveInstance(new AAEmu.Game.Models.Game.Char.Character(new UnitCustomModelParams()));

        await Assert.That(result).IsFalse();
    }

    [Test]
    public void Initialize_SubscribesToTickManager()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());
        var manager = new IndunManager(mockTick.Object, Mock.Of<IWorldManager>().Object, Mock.Of<IZoneManager>().Object, Mock.Of<ITeamManager>().Object);
        manager.Initialize();

        mockTick.OnTick.WasCalled(Times.Once);
    }

    [Test]
    public async Task IsDungeonFull_AtMaxCapacity_ReturnsTrue()
    {
        var method = typeof(IndunManager).GetMethod("IsDungeonFull",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        var result = method!.Invoke(null, [1, 1u]);

        await Assert.That(result).IsEqualTo(true);
    }

    [Test]
    public async Task IsDungeonFull_BelowMaxCapacity_ReturnsFalse()
    {
        var method = typeof(IndunManager).GetMethod("IsDungeonFull",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        var result = method!.Invoke(null, [0, 1u]);

        await Assert.That(result).IsEqualTo(false);
    }

    [Test]
    public async Task RequestSystemInstance_ClosedSchedule_StopsBeforeWorldLookup()
    {
        var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc); // Monday
        var zone = new Zone { Id = 900, ZoneKey = 901, GroupId = 902 };
        var indun = new IndunZone { ZoneGroupId = zone.GroupId, InstanceCatalogId = 903, UseUtcEntranceTimes = true };
        indun.EntranceTimes.Add(new InstanceEntranceTime(2, 0, 0, 0, 0)); // Tuesday only

        var manager = CreateAdmissionManager(zone, indun);
        manager.AdmissionUtcNow = () => now;

        var accepted = manager.RequestSystemInstance(
            new Character(new UnitCustomModelParams()) { Id = 1, ObjId = 1, Name = "Closed" },
            zone.Id,
            0,
            out var dungeon);

        await Assert.That(accepted).IsFalse();
        await Assert.That(dungeon).IsNull();
    }

    [Test]
    public async Task RequestSystemInstance_ProhibitedBuffTag_StopsBeforeWorldLookup()
    {
        var zone = new Zone { Id = 910, ZoneKey = 911, GroupId = 912 };
        var indun = new IndunZone { ZoneGroupId = zone.GroupId, InstanceCatalogId = 913, UseUtcEntranceTimes = true };
        indun.PermissionTags.Add(new InstancePermissionTag(InstancePermissionTagKind.Buff, 5947));

        var manager = CreateAdmissionManager(zone, indun);
        manager.AdmissionTagMatcher = (_, kind, tagId) =>
            kind == InstancePermissionTagKind.Buff && tagId == 5947;

        var accepted = manager.RequestSystemInstance(
            new Character(new UnitCustomModelParams()) { Id = 2, ObjId = 2, Name = "Transformed" },
            zone.Id,
            0,
            out var dungeon);

        await Assert.That(accepted).IsFalse();
        await Assert.That(dungeon).IsNull();
    }

    [Test]
    public async Task RequestDungeonInstance_ClosedSchedule_StopsBeforeInstanceLookup()
    {
        var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc); // Monday
        var zone = new Zone { Id = 920, ZoneKey = 921, GroupId = 922 };
        var indun = new IndunZone { ZoneGroupId = zone.GroupId, InstanceCatalogId = 923, UseUtcEntranceTimes = true };
        indun.EntranceTimes.Add(new InstanceEntranceTime(2, 0, 0, 0, 0)); // Tuesday only

        var manager = CreateAdmissionManager(zone, indun);
        manager.AdmissionUtcNow = () => now;

        var accepted = manager.RequestDungeonInstance(
            new Character(new UnitCustomModelParams()) { Id = 3, ObjId = 3, Name = "ClosedDungeon" },
            zone.Id,
            0);

        await Assert.That(accepted).IsFalse();
    }

    [Test]
    public async Task PreparedMatch_ChangedAdmissionStopsBeforeQueuePlayer()
    {
        var manager = new IndunMatchmakingManager();
        var character = new Character(new UnitCustomModelParams()) { Id = 4, ObjId = 4, Name = "BuffExpired" };
        var dungeonZone = new IndunZone { ZoneGroupId = 930, InstanceCatalogId = 931 };
        var queueCalls = 0;
        var order = new List<string>();
        manager.PreparedCanQueue = (_, _) => true;
        manager.FinalAdmissionCheck = (_, _) => { order.Add("admission"); return false; };
        manager.PreparedQueuePlayer = (_, _) => { order.Add("queue"); queueCalls++; return true; };
        var method = typeof(IndunMatchmakingManager).GetMethod("TryEnterPreparedPlayer",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var admitted = (bool)method.Invoke(manager, [null, dungeonZone, character])!;

        await Assert.That(admitted).IsFalse();
        await Assert.That(queueCalls).IsEqualTo(0);
        await Assert.That(order.Count).IsEqualTo(1);
        await Assert.That(order[0]).IsEqualTo("admission");
    }

    private static IndunManager CreateAdmissionManager(
        Zone zone,
        IndunZone indun)
    {
        var tick = Mock.Of<ITickManager>();
        var world = Mock.Of<IWorldManager>();
        world.GetWorldTemplateByZoneKey(zone.ZoneKey).Returns(new WorldTemplate { Id = 1, Name = "admission-test" });
        world.GetWorlds().Throws(new InvalidOperationException("Admission rejection must happen before world lookup."));
        var zones = Mock.Of<IZoneManager>();
        zones.GetZoneById(zone.Id).Returns(zone);

        GetIndunZonesField().SetValue(IndunGameData.Instance, new Dictionary<uint, IndunZone> { [indun.ZoneGroupId] = indun });

        return new IndunManager(tick.Object, world.Object, zones.Object, Mock.Of<ITeamManager>().Object);
    }

    private static FieldInfo GetIndunZonesField() =>
        typeof(IndunGameData).GetField("_indunZones", BindingFlags.Instance | BindingFlags.NonPublic)!;
}
