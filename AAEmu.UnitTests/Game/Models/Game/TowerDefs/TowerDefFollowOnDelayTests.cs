using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.UnitTests.Game.Models.Game.TowerDefs;

public class TowerDefFollowOnDelayTests
{
    [Test]
    public async Task FromFinalProg_UsesCondToNextTime()
    {
        var delay = TowerDefFollowOnDelay.FromFinalProg(new TowerDefProg { CondToNextTime = 10f });
        await Assert.That(delay).IsEqualTo(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task FromFinalProg_ZeroOrNull_IsImmediate()
    {
        await Assert.That(TowerDefFollowOnDelay.FromFinalProg(null)).IsEqualTo(TimeSpan.Zero);
        await Assert.That(TowerDefFollowOnDelay.FromFinalProg(new TowerDefProg { CondToNextTime = 0f }))
            .IsEqualTo(TimeSpan.Zero);
        await Assert.That(TowerDefFollowOnDelay.FromFinalProg(new TowerDefProg { CondToNextTime = -1f }))
            .IsEqualTo(TimeSpan.Zero);
    }
}
