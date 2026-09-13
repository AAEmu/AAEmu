using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.UnitTests.Game.Models.Game.Butlers;

public sealed class ButlerHarvestCompletionRulesTests
{
    [Test]
    public async Task DueCycle_AdvancesPersistedAnchorAndDecrementsOneRepeat()
    {
        var job = new ButlerHarvestJob(7, 11, 3, 5, 30, 1_000);

        var planned = ButlerHarvestCompletionRules.TryPlanNextCycle(
            job, 5, 60, 1_060, out var plan);

        await Assert.That(planned).IsTrue();
        await Assert.That(plan.CycleNumber).IsEqualTo((ushort)1);
        await Assert.That(plan.AdvancedJob.RemainingRepeatCount).IsEqualTo((ushort)4);
        await Assert.That(plan.AdvancedJob.UpdateTime).IsEqualTo(1_060L);
        await Assert.That(plan.IsFinal).IsFalse();
    }

    [Test]
    public async Task BeforeDueTime_DoesNotAdvance()
    {
        var planned = ButlerHarvestCompletionRules.TryPlanNextCycle(
            new ButlerHarvestJob(7, 11, 3, 5, 30, 1_000), 5, 60, 1_059, out _);

        await Assert.That(planned).IsFalse();
    }

    [Test]
    public async Task FinalRepeat_IsMarkedFinal()
    {
        var planned = ButlerHarvestCompletionRules.TryPlanNextCycle(
            new ButlerHarvestJob(7, 11, 3, 1, 30, 1_240), 5, 60, 1_300, out var plan);

        await Assert.That(planned).IsTrue();
        await Assert.That(plan.CycleNumber).IsEqualTo((ushort)5);
        await Assert.That(plan.AdvancedJob.RemainingRepeatCount).IsEqualTo((ushort)0);
        await Assert.That(plan.IsFinal).IsTrue();
    }

    [Test]
    public async Task StaleRepeatCountBeyondStaticContent_FailsClosed()
    {
        var planned = ButlerHarvestCompletionRules.TryPlanNextCycle(
            new ButlerHarvestJob(7, 11, 3, 6, 30, 1_000), 5, 60, 2_000, out _);

        await Assert.That(planned).IsFalse();
    }

    [Test]
    public async Task CorruptZeroAmountJob_FailsClosed()
    {
        var planned = ButlerHarvestCompletionRules.TryPlanNextCycle(
            new ButlerHarvestJob(7, 11, 0, 5, 30, 1_000), 5, 60, 2_000, out _);

        await Assert.That(planned).IsFalse();
    }
}
