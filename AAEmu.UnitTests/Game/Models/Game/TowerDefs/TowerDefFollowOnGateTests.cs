using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.UnitTests.Game.Models.Game.TowerDefs;

public class TowerDefFollowOnGateTests
{
    [Test]
    public async Task ShouldFire_MatchingGeneration()
    {
        await Assert.That(TowerDefFollowOnGate.ShouldFire(37, 37, 5, 5)).IsTrue();
    }

    [Test]
    public async Task ShouldFire_StaleGeneration_AfterRestart_IsFalse()
    {
        // Ended run scheduled gen=3; restarted live run is gen=4 with same pending follow-on id.
        await Assert.That(TowerDefFollowOnGate.ShouldFire(37, 37, 4, 3)).IsFalse();
    }

    [Test]
    public async Task ShouldFire_ClearedPending_IsFalse()
    {
        await Assert.That(TowerDefFollowOnGate.ShouldFire(0, 37, 5, 5)).IsFalse();
    }
}
