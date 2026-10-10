using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Models.Game.World.Zones;

/// <summary>
/// World-authored Create names the dungeon copy. An unplaced transform is not a copy id.
/// </summary>
public class ZoneCopySpawnRouteRulesTests
{
    [Test]
    public async Task ParentWorld_Wins()
    {
        await Assert.That(ZoneCopySpawnRouteRules.InstanceIdForSpawn(102, 0)).IsEqualTo(102u);
    }

    [Test]
    public async Task ParentWorld_WinsOverTransform()
    {
        await Assert.That(ZoneCopySpawnRouteRules.InstanceIdForSpawn(103, 7)).IsEqualTo(103u);
    }

    [Test]
    public async Task PlacedTransform_UsedWhenWorldUnset()
    {
        await Assert.That(ZoneCopySpawnRouteRules.InstanceIdForSpawn(0, 8)).IsEqualTo(8u);
    }

    [Test]
    public async Task UnplacedTransform_IsNotACopy()
    {
        await Assert.That(ZoneCopySpawnRouteRules.InstanceIdForSpawn(0, AAEmu.Game.Models.Game.World.Transform.Transform.NoInstanceId))
            .IsEqualTo(0u);
    }

    [Test]
    public async Task Overworld_UsesUniqueHost()
    {
        await Assert.That(ZoneCopySpawnRouteRules.InstanceIdForSpawn(0, 0)).IsEqualTo(0u);
    }
}
