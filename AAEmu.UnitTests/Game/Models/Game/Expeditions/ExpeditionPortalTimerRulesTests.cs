using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.UnitTests.Game.Models.Game.Expeditions;

public class ExpeditionPortalTimerRulesTests
{
    [Test]
    public async Task RemainingMilliseconds_PastEnd_IsZero()
    {
        var now = DateTime.UtcNow;
        await Assert.That(ExpeditionPortalTimerRules.RemainingMilliseconds(now.AddSeconds(-1), now))
            .IsEqualTo(0);
        await Assert.That(ExpeditionPortalTimerRules.HasRemaining(now.AddSeconds(-1), now)).IsFalse();
    }

    [Test]
    public async Task RemainingMilliseconds_FutureEnd_ReturnsDelta()
    {
        var now = DateTime.UtcNow;
        var ends = now.AddMilliseconds(5_000);
        await Assert.That(ExpeditionPortalTimerRules.RemainingMilliseconds(ends, now))
            .IsEqualTo(5_000);
        await Assert.That(ExpeditionPortalTimerRules.HasRemaining(ends, now)).IsTrue();
    }

    [Test]
    public async Task EndTimePhys_AddsRemainingOntoPhysClock()
    {
        await Assert.That(ExpeditionPortalTimerRules.EndTimePhys(1_000u, 500))
            .IsEqualTo(1_500);
        await Assert.That(ExpeditionPortalTimerRules.EndTimePhys(1_000u, 0))
            .IsEqualTo(0);
    }
}
