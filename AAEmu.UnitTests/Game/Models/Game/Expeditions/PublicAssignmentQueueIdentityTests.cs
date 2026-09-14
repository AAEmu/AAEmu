using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Expeditions.PublicAssignments;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Expeditions;

public sealed class PublicAssignmentQueueIdentityTests
{
    [Test]
    public async Task ProgressVersionDoesNotInvalidateQueuedEventsForSameSelection()
    {
        var period = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);
        var state = State(period, group: 17, quest: 10887, version: 4);
        var queued = Queue(state);

        state.Version = 6; // two earlier progress events committed while this event waited

        await Assert.That(queued.Matches(state)).IsTrue();
    }

    [Test]
    public async Task RerollOrWeeklyReplacementInvalidatesQueuedEvent()
    {
        var period = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);
        var original = State(period, group: 17, quest: 10887, version: 4);
        var queued = Queue(original);

        var rerolled = State(period, group: 18, quest: 10889, version: 5);
        rerolled.SelectionGeneration = 2;
        var nextWeek = State(period.AddDays(7), group: 17, quest: 10887, version: 1);

        await Assert.That(queued.Matches(rerolled)).IsFalse();
        await Assert.That(queued.Matches(nextWeek)).IsFalse();

        // A later A selection must not accept work queued for the earlier A selection.
        rerolled.GroupId = original.GroupId;
        rerolled.QuestContextId = original.QuestContextId;
        await Assert.That(queued.Matches(rerolled)).IsFalse();
    }

    [Test]
    public async Task WeeklyPeriodUsesConfiguredSundayOrMondayBoundary()
    {
        var wednesday = new DateTime(2026, 9, 9, 17, 30, 0, DateTimeKind.Utc);

        await Assert.That(ExpeditionPublicAssignmentService.GetPeriodStart(wednesday, 0))
            .IsEqualTo(new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(ExpeditionPublicAssignmentService.GetPeriodStart(wednesday, 1))
            .IsEqualTo(new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc));
    }

    private static ExpeditionPublicAssignmentState State(
        DateTime period, uint group, uint quest, uint version) => new()
    {
        ExpeditionId = 41,
        RealStep = 13,
        PeriodStart = period,
        GroupId = group,
        QuestContextId = quest,
        Version = version
    };

    private static QueuedPublicAssignmentProgress Queue(ExpeditionPublicAssignmentState state) => new(
        new PublicQuestProgressEvent(7, state.ExpeditionId, PublicQuestProgressEventKind.Honor,
            10, 0, 0, 0, 0, default),
        "Contributor", state.RealStep, state.PeriodStart, state.SelectionGeneration);
}
