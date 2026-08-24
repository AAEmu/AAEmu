using AAEmu.Game;
using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

[NotInParallel]
public class DungeonLoaderTaskTests
{
    [After(Test)]
    public void RestoreHooks()
    {
        WorldIntegration.IsZoneInstanceLoaded = null;
        WorldIntegration.IsZoneLoaded = null;
        WorldIntegration.ZoneHostSpawnEnabled = false;
    }

    [Test]
    public async Task IsHostReady_SpawnedCopy_IgnoresUniqueLeftover()
    {
        WorldIntegration.IsZoneInstanceLoaded = (_, _) => false;
        WorldIntegration.IsZoneLoaded = _ => true;

        await Assert.That(DungeonLoaderTask.IsHostReady(265, 7, requireExactCopy: true)).IsFalse();
    }

    [Test]
    public async Task IsHostReady_SpawnedCopy_AcceptsMatchingInstance()
    {
        WorldIntegration.IsZoneInstanceLoaded = (zoneId, instanceId) => zoneId == 265 && instanceId == 7;
        WorldIntegration.IsZoneLoaded = _ => false;

        await Assert.That(DungeonLoaderTask.IsHostReady(265, 7, requireExactCopy: true)).IsTrue();
        await Assert.That(DungeonLoaderTask.IsHostReady(265, 8, requireExactCopy: true)).IsFalse();
    }

    [Test]
    public async Task IsHostReady_ManualHost_AcceptsUniqueZone()
    {
        WorldIntegration.IsZoneInstanceLoaded = (_, _) => false;
        WorldIntegration.IsZoneLoaded = zoneId => zoneId == 265;

        await Assert.That(DungeonLoaderTask.IsHostReady(265, 7, requireExactCopy: false)).IsTrue();
        await Assert.That(DungeonLoaderTask.IsHostReady(184, 7, requireExactCopy: false)).IsFalse();
    }

    [Test]
    public async Task ShouldAbortMissingSpawnedHost_OnlyWhenWorldSpawnsAndProcessDies()
    {
        await Assert.That(DungeonLoaderTask.ShouldAbortMissingSpawnedHost(false, true, false)).IsTrue();
        await Assert.That(DungeonLoaderTask.ShouldAbortMissingSpawnedHost(false, true, true)).IsFalse();
        await Assert.That(DungeonLoaderTask.ShouldAbortMissingSpawnedHost(false, false, false)).IsFalse();
        await Assert.That(DungeonLoaderTask.ShouldAbortMissingSpawnedHost(true, true, false)).IsFalse();
    }

    [Test]
    public async Task EnsureDungeonContentSpawned_SkipsWhenAlreadyFlagged()
    {
        var world = new AAEmu.Game.Models.Game.World.WorldInstance(
            new AAEmu.Game.Models.Game.World.WorldTemplate { Name = "instance_warm_alpha" },
            0,
            true,
            99)
        {
            DungeonContentSpawned = true
        };
        DungeonLoaderTask.EnsureDungeonContentSpawned(world);
        await Assert.That(world.DungeonContentSpawned).IsTrue();
    }
}

public class DungeonLifecycleTests
{
    [Test]
    public async Task ShouldDestroyAfterLastPlayerLeft_NeverOnExitDoodad()
    {
        await Assert.That(Dungeon.ShouldDestroyAfterLastPlayerLeft(false, 0)).IsFalse();
        await Assert.That(Dungeon.ShouldDestroyAfterLastPlayerLeft(false, 1)).IsFalse();
        await Assert.That(Dungeon.ShouldDestroyAfterLastPlayerLeft(true, 0)).IsFalse();
    }

    [Test]
    public async Task ShouldChargeDailyEntry_OnlyFirstVisitToThisCopy()
    {
        await Assert.That(Dungeon.ShouldChargeDailyEntry(alreadyChargedThisCopy: false)).IsTrue();
        await Assert.That(Dungeon.ShouldChargeDailyEntry(alreadyChargedThisCopy: true)).IsFalse();
    }

    [Test]
    public async Task ShouldUnbindOnDisconnect_IsFalse()
    {
        await Assert.That(Dungeon.ShouldUnbindOnDisconnect()).IsFalse();
    }

    [Test]
    public async Task ShouldRefuseResetWhileInside_OnlyWhenStillInCopy()
    {
        await Assert.That(Dungeon.ShouldRefuseResetWhileInside(true)).IsTrue();
        await Assert.That(Dungeon.ShouldRefuseResetWhileInside(false)).IsFalse();
    }

    [Test]
    public async Task ShouldDestroyAfterLastAccessRemoved_WhenNobodyBound()
    {
        await Assert.That(Dungeon.ShouldDestroyAfterLastAccessRemoved(0)).IsTrue();
        await Assert.That(Dungeon.ShouldDestroyAfterLastAccessRemoved(1)).IsFalse();
    }
}
