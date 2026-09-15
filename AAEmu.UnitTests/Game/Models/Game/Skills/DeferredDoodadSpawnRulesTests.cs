using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Whether SpawnDoodad's spawned doodad is made visible now or by a scheduled task, and whether a
/// deferred spawn is still allowed to happen once it fires.
/// </summary>
public class DeferredDoodadSpawnRulesTests
{
    [Test]
    [Arguments(1)]
    [Arguments(1000)]
    [Arguments(10000)]
    public async Task PositiveDelay_DefersTheSpawn(int delayMilliseconds)
    {
        await Assert.That(DeferredDoodadSpawnRules.ShouldDeferSpawn(delayMilliseconds)).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(-5000)] // spawn_doodad carries negative delays; Thread.Sleep skipped those too
    public async Task ZeroOrNegativeDelay_SpawnsWithoutATask(int delayMilliseconds)
    {
        await Assert.That(DeferredDoodadSpawnRules.ShouldDeferSpawn(delayMilliseconds)).IsFalse();
    }

    [Test]
    public async Task DeferredSpawn_IsOnlyAllowedWhileTheDoodadAndItsWorldAreAlive()
    {
        await Assert.That(DeferredDoodadSpawnRules.CanSpawn(doodadDeleted: false, hasParentWorld: true)).IsTrue();
        await Assert.That(DeferredDoodadSpawnRules.CanSpawn(doodadDeleted: true, hasParentWorld: true)).IsFalse();
        await Assert.That(DeferredDoodadSpawnRules.CanSpawn(doodadDeleted: false, hasParentWorld: false)).IsFalse();
        await Assert.That(DeferredDoodadSpawnRules.CanSpawn(doodadDeleted: true, hasParentWorld: false)).IsFalse();
    }
}
