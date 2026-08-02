using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;

using PeriodStatus = AAEmu.Game.Core.Managers.GameScheduleManager.PeriodStatus;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

/// <summary>
/// Pins the spawn window decision the zone-authority gate applies to every ZWSpawnNpc.
/// A spawner passing this test as Closed is never acknowledged, so Zone never creates it.
/// </summary>
public class NpcSpawnerWindowTests
{
    private const float Noon = 12f;

    [Test]
    [Arguments(PeriodStatus.Ended)]
    [Arguments(PeriodStatus.NotStarted)]
    public async Task Evaluate_ScheduleOutsideItsPeriod_Closes(PeriodStatus status)
    {
        // 765 of the 922 game_schedules rows are dated live events that ended in 2013.
        var state = NpcSpawnerWindow.Evaluate(status, 0f, 0f, Noon);

        await Assert.That(state).IsEqualTo(NpcSpawnerWindowState.Closed);
    }

    [Test]
    public async Task Evaluate_ScheduleRunning_Opens()
    {
        var state = NpcSpawnerWindow.Evaluate(PeriodStatus.InProgress, 0f, 0f, Noon);

        await Assert.That(state).IsEqualTo(NpcSpawnerWindowState.Open);
    }

    [Test]
    public async Task Evaluate_NoScheduleAndNoWindow_IsUnscheduled()
    {
        // The ordinary population of a zone: 21882 of 22982 spawner rows reach this branch.
        var state = NpcSpawnerWindow.Evaluate(PeriodStatus.NotFound, 0f, 0f, Noon);

        await Assert.That(state).IsEqualTo(NpcSpawnerWindowState.Unscheduled);
    }

    [Test]
    public async Task Evaluate_RunningScheduleStillObeysTheDayNightWindow()
    {
        // A nocturnal spawner inside its calendar period still waits for its hour.
        var state = NpcSpawnerWindow.Evaluate(PeriodStatus.InProgress, 22f, 4f, Noon);

        await Assert.That(state).IsEqualTo(NpcSpawnerWindowState.Closed);
    }

    [Test]
    public async Task Evaluate_EndedScheduleIgnoresAnOpenDayNightWindow()
    {
        var state = NpcSpawnerWindow.Evaluate(PeriodStatus.Ended, 0f, 23f, Noon);

        await Assert.That(state).IsEqualTo(NpcSpawnerWindowState.Closed);
    }

    [Test]
    [Arguments(8f, 18f, 12f, NpcSpawnerWindowState.Open)]
    [Arguments(8f, 18f, 6f, NpcSpawnerWindowState.Closed)]
    [Arguments(8f, 18f, 20f, NpcSpawnerWindowState.Closed)]
    // Windows that wrap past midnight are the common shape for night spawns.
    [Arguments(22f, 4f, 23f, NpcSpawnerWindowState.Open)]
    [Arguments(22f, 4f, 2f, NpcSpawnerWindowState.Open)]
    [Arguments(22f, 4f, 12f, NpcSpawnerWindowState.Closed)]
    // Either bound alone is enough to make the window real; only both unset means unconstrained.
    [Arguments(0f, 6f, 3f, NpcSpawnerWindowState.Open)]
    [Arguments(0f, 6f, 9f, NpcSpawnerWindowState.Closed)]
    [Arguments(0f, 0f, 9f, NpcSpawnerWindowState.Unscheduled)]
    public async Task EvaluateDayNightWindow_MatchesTheSpawnerRow(
        float startTime, float endTime, float gameTimeHours, NpcSpawnerWindowState expected)
    {
        var state = NpcSpawnerWindow.EvaluateDayNightWindow(startTime, endTime, gameTimeHours);

        await Assert.That(state).IsEqualTo(expected);
    }

    [Test]
    public async Task EvaluateDayNightWindow_UnscheduledSpawnerIsNeverClosed()
    {
        // Guards the branch that decides whether a spawner is gated at all: treating an unset
        // window as Closed would suppress the entire ordinary NPC population.
        for (var hour = 0f; hour < 24f; hour += 1f)
        {
            var state = NpcSpawnerWindow.EvaluateDayNightWindow(0f, 0f, hour);
            await Assert.That(state).IsEqualTo(NpcSpawnerWindowState.Unscheduled);
        }
    }
}
