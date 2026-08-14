using AAEmu.Game.Models.Game.TodayAssignment;

namespace AAEmu.UnitTests.Game.Models.Game.TodayAssignment;

public class TodayAssignmentUnlockPolicyTests
{
    [Test]
    public async Task NewDay_LifetimeUnlocked_IsReadyNotLocked()
    {
        await Assert.That(TodayAssignmentUnlockPolicy.StatusForNewDay(lifetimeUnlocked: true))
            .IsEqualTo(TodayAssignmentStatus.Ready);
        await Assert.That(TodayAssignmentUnlockPolicy.ShouldSeedReady(true, hasTodayRow: false))
            .IsTrue();
    }

    [Test]
    public async Task NewDay_NeverUnlocked_StaysLocked()
    {
        await Assert.That(TodayAssignmentUnlockPolicy.StatusForNewDay(lifetimeUnlocked: false))
            .IsEqualTo(TodayAssignmentStatus.Locked);
        await Assert.That(TodayAssignmentUnlockPolicy.ShouldSeedReady(false, hasTodayRow: false))
            .IsFalse();
    }

    [Test]
    public async Task NewDay_DoesNotOverwriteTodaysRow()
    {
        await Assert.That(TodayAssignmentUnlockPolicy.ShouldSeedReady(true, hasTodayRow: true))
            .IsFalse();
    }

    [Test]
    public async Task ItemCost_OnlyOnceOnPaidSteps()
    {
        await Assert.That(TodayAssignmentUnlockPolicy.MustConsumeItemCost(true, alreadyLifetimeUnlocked: false))
            .IsTrue();
        await Assert.That(TodayAssignmentUnlockPolicy.MustConsumeItemCost(true, alreadyLifetimeUnlocked: true))
            .IsFalse();
        await Assert.That(TodayAssignmentUnlockPolicy.MustConsumeItemCost(false, alreadyLifetimeUnlocked: false))
            .IsFalse();
    }
}
