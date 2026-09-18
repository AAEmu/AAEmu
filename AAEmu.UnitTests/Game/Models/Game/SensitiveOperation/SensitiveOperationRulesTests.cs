using AAEmu.Game.Models.Game.SensitiveOperation;

namespace AAEmu.UnitTests.Game.Models.Game.SensitiveOperation;

/// <summary>
/// The account-protection window's decisions. The one that matters most is the first: with feature bit 56 off
/// nothing is ever held back, which is how the guard ships.
/// </summary>
/// <remarks>
/// The guard that drives these decisions — the per-account windows and the two packet paths that reach them —
/// is exercised in <c>AAEmu.UnitTests.Game.Core.Managers.SensitiveOperationGuardTests</c>.
/// </remarks>
public class SensitiveOperationRulesTests
{
    [Test]
    public async Task MayPerform_IsInertWhileTheFeatureBitIsOff()
    {
        // Protected or not, nothing is refused while the guard is switched off — the state cannot even be set.
        await Assert.That(SensitiveOperationRules.MayPerform(featureEnabled: false, protectedNow: true,
            verifiedNow: false)).IsTrue();
        await Assert.That(SensitiveOperationRules.MayPerform(featureEnabled: false, protectedNow: false,
            verifiedNow: false)).IsTrue();
    }

    [Test]
    public async Task MayPerform_HoldsBackOnlyALiveUnverifiedWindow()
    {
        await Assert.That(SensitiveOperationRules.MayPerform(true, protectedNow: true, verifiedNow: false))
            .IsFalse();
        await Assert.That(SensitiveOperationRules.MayPerform(true, protectedNow: false, verifiedNow: false))
            .IsTrue();
        await Assert.That(SensitiveOperationRules.MayPerform(true, protectedNow: true, verifiedNow: true))
            .IsTrue();
    }

    [Test]
    public async Task RemainingSeconds_CountsUpFromTheExpiry()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

        await Assert.That(SensitiveOperationRules.RemainingSeconds(now.AddSeconds(90), now)).IsEqualTo(90u);
        await Assert.That(SensitiveOperationRules.RemainingSeconds(now.AddMilliseconds(1500), now))
            .IsEqualTo(2u); // rounded up, so a window with time left never reads as zero
    }

    [Test]
    public async Task RemainingSeconds_IsZeroOnceTheWindowHasPassed()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

        await Assert.That(SensitiveOperationRules.RemainingSeconds(now, now)).IsEqualTo(0u);
        await Assert.That(SensitiveOperationRules.RemainingSeconds(now.AddSeconds(-5), now)).IsEqualTo(0u);
    }

    [Test]
    public async Task IsExpired_FlipsAtTheExpiry()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

        await Assert.That(SensitiveOperationRules.IsExpired(now.AddSeconds(1), now)).IsFalse();
        await Assert.That(SensitiveOperationRules.IsExpired(now, now)).IsTrue();
    }

    [Test]
    public async Task DefaultProtectionWindow_IsAWholeNumberOfSeconds()
    {
        await Assert.That(SensitiveOperationRules.DefaultProtectionWindow.TotalSeconds)
            .IsEqualTo(Math.Floor(SensitiveOperationRules.DefaultProtectionWindow.TotalSeconds));
        await Assert.That(SensitiveOperationRules.DefaultProtectionWindow).IsGreaterThan(TimeSpan.Zero);
    }
}
