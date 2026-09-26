using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The leadership-period reset is driven once a minute by <c>HeroTickTask</c> for the whole
/// LeadershipRanking phase. Taking the world save gate exclusively on that path would pause every in-flight
/// mail/auction/butler operation once a minute even though, after the first roll, the cycle's idempotency
/// marker is always present and there is nothing to do.
/// <para>
/// The gate is thread-bound and these tests assert inside no await while it is held, so no assertion can
/// resume on a pool thread that never took the lock. The database read is replaced through
/// <see cref="HeroManager.PeriodResetProbe"/>; production keeps the real query.
/// </para>
/// </summary>
[NotInParallel]
public class HeroPeriodResetGateTests
{
    private static HeroCycle AnyCycle(uint cycleId) => new() { Id = cycleId };

    /// <summary>
    /// A cycle that is already marked reset must not take the exclusive save gate at all. Before the fix the
    /// save scope was entered first, so the gate was held for the duration of every tick's marker SELECT.
    /// </summary>
    [Test]
    public async Task AlreadyResetCycle_NeverTakesTheExclusiveSaveGate()
    {
        var original = HeroManager.PeriodResetProbe;
        var saveGateTakenDuringCall = false;
        try
        {
            HeroManager.PeriodResetProbe = _ =>
            {
                // Sampled inside the probe: if the gate were taken before this read it would already be held.
                saveGateTakenDuringCall = PersistenceGate.IsSaveHeld;
                return true;
            };

            HeroManager.EnsureLeadershipPeriodReset(AnyCycle(7));
        }
        finally
        {
            HeroManager.PeriodResetProbe = original;
        }

        await Assert.That(saveGateTakenDuringCall).IsFalse();
        // Nothing may be left held on this thread once the tick returns.
        await Assert.That(PersistenceGate.IsSaveHeld).IsFalse();
    }

    /// <summary>
    /// A tick that fires while a money operation owns the gate must skip the roll and return normally rather than
    /// throwing. Throwing here would abort HeroTickTask and stop every later phase entry with it, and the skip
    /// is safe because the next tick retries.
    /// </summary>
    [Test]
    public async Task RollSkippedWhileAnOperationOwnsTheGate_DoesNotThrow()
    {
        var original = HeroManager.PeriodResetProbe;
        var probeSawOperationHeld = false;
        var threw = false;
        try
        {
            HeroManager.PeriodResetProbe = _ =>
            {
                probeSawOperationHeld = PersistenceGate.IsOperationHeld;
                return false;
            };

            PersistenceGate.EnterOperation();
            try
            {
                HeroManager.EnsureLeadershipPeriodReset(AnyCycle(11));
            }
            catch
            {
                threw = true;
            }
            finally
            {
                PersistenceGate.ExitOperation();
            }
        }
        finally
        {
            HeroManager.PeriodResetProbe = original;
        }

        await Assert.That(probeSawOperationHeld).IsTrue();
        await Assert.That(threw).IsFalse();
        // The refusal released nothing it did not take.
        await Assert.That(PersistenceGate.IsOperationHeld).IsFalse();
        await Assert.That(PersistenceGate.IsSaveHeld).IsFalse();
    }
}
