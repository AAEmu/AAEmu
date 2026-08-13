using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.UnitTests.Game.Models.Game.TowerDefs;

public class TowerDefFollowOnScheduleTests
{
    [Test]
    public async Task StaleDelay_AfterEndAndRestart_DoesNotFire_CurrentFiresOnce()
    {
        var schedule = new TowerDefFollowOnSchedule();

        schedule.BeginRun(generation: 1);
        schedule.Schedule(37);
        var staleGeneration = schedule.FollowOnGeneration;

        schedule.EndRun();
        schedule.BeginRun(generation: 2);
        schedule.Schedule(37);
        var liveGeneration = schedule.FollowOnGeneration;

        await Assert.That(schedule.TryConsumeDue(37, staleGeneration)).IsFalse();
        await Assert.That(schedule.PendingFollowOnId).IsEqualTo(37u);

        await Assert.That(schedule.TryConsumeDue(37, liveGeneration)).IsTrue();
        await Assert.That(schedule.PendingFollowOnId).IsEqualTo(0u);

        await Assert.That(schedule.TryConsumeDue(37, liveGeneration)).IsFalse();
    }
}
