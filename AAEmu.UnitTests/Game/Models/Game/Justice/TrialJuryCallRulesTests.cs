using AAEmu.Game.Models.Game.Justice;

namespace AAEmu.UnitTests.Game.Models.Game.Justice;

public class TrialJuryCallRulesTests
{
    [Test]
    public async Task Call_IsLiveOnlyWhileTheBenchGathers()
    {
        await Assert.That(TrialJuryCallRules.IsCallLive(TrialState.WaitingJury)).IsTrue();

        foreach (var phase in new[]
                 {
                     TrialState.Free, TrialState.WaitingCrimeRecord, TrialState.Testimony,
                     TrialState.FinalStatement, TrialState.Sentence, TrialState.GuiltyBySystem
                 })
        {
            await Assert.That(TrialJuryCallRules.IsCallLive(phase)).IsFalse();
        }
    }

    [Test]
    public async Task PendingCalls_AreDroppedWhenTheGatheringWindowEnds()
    {
        await Assert.That(TrialJuryCallRules.ShouldDropPendingCalls(
            TrialState.WaitingJury, TrialState.Testimony)).IsTrue();
        await Assert.That(TrialJuryCallRules.ShouldDropPendingCalls(
            TrialState.WaitingJury, TrialState.WaitingCrimeRecord)).IsTrue();
    }

    [Test]
    public async Task PendingCalls_SurviveEveryOtherTransition()
    {
        // Opening the case file and re-announcing the phase must not throw the invites away, and a
        // phase that is not the gathering window has nothing to drop.
        await Assert.That(TrialJuryCallRules.ShouldDropPendingCalls(
            TrialState.WaitingCrimeRecord, TrialState.WaitingJury)).IsFalse();
        await Assert.That(TrialJuryCallRules.ShouldDropPendingCalls(
            TrialState.WaitingJury, TrialState.WaitingJury)).IsFalse();
        await Assert.That(TrialJuryCallRules.ShouldDropPendingCalls(
            TrialState.Testimony, TrialState.FinalStatement)).IsFalse();
        await Assert.That(TrialJuryCallRules.ShouldDropPendingCalls(
            TrialState.Sentence, TrialState.GuiltyBySystem)).IsFalse();
    }
}
