using AAEmu.Game;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

/// <summary>
/// Host-drop markers clear a copy's started flag; deadline expiry must not.
/// </summary>
public class InstanceTowerDefHostDropRulesTests
{
    [Test]
    public async Task HostDropMarker_IsOneShot()
    {
        const uint instanceId = 900001;
        const uint towerDefId = 178;

        WorldIntegration.MarkInstanceTowerDefHostDropped(instanceId, towerDefId);
        await Assert.That(WorldIntegration.ConsumeInstanceTowerDefHostDropped(instanceId, towerDefId))
            .IsTrue();
        await Assert.That(WorldIntegration.ConsumeInstanceTowerDefHostDropped(instanceId, towerDefId))
            .IsFalse();
    }

    [Test]
    public async Task ZeroIds_AreIgnored()
    {
        WorldIntegration.MarkInstanceTowerDefHostDropped(0, 178);
        WorldIntegration.MarkInstanceTowerDefHostDropped(1, 0);
        await Assert.That(WorldIntegration.ConsumeInstanceTowerDefHostDropped(0, 178)).IsFalse();
        await Assert.That(WorldIntegration.ConsumeInstanceTowerDefHostDropped(1, 0)).IsFalse();
    }
}
