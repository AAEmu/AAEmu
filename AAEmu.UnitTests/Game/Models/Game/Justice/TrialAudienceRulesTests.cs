using AAEmu.Game.Models.Game.Justice;

namespace AAEmu.UnitTests.Game.Models.Game.Justice;

public class TrialAudienceRulesTests
{
    [Test]
    public async Task CanWatch_IsTrueWhileTheCaseIsBeingHeard()
    {
        // From the summons until the ruling closes the case.
        await Assert.That(TrialAudienceRules.CanWatch(TrialState.WaitingCrimeRecord)).IsTrue();
        await Assert.That(TrialAudienceRules.CanWatch(TrialState.WaitingJury)).IsTrue();
        await Assert.That(TrialAudienceRules.CanWatch(TrialState.Testimony)).IsTrue();
        await Assert.That(TrialAudienceRules.CanWatch(TrialState.FinalStatement)).IsTrue();
        await Assert.That(TrialAudienceRules.CanWatch(TrialState.Sentence)).IsTrue();
        await Assert.That(TrialAudienceRules.CanWatch(TrialState.GuiltyBySystem)).IsTrue();
        await Assert.That(TrialAudienceRules.CanWatch(TrialState.GuiltyByUser)).IsTrue();
    }

    [Test]
    public async Task CanWatch_IsFalseOnceTheCaseIsOver()
    {
        await Assert.That(TrialAudienceRules.CanWatch(TrialState.PostSentence)).IsFalse();
        await Assert.That(TrialAudienceRules.CanWatch(TrialState.Free)).IsFalse();
    }

    [Test]
    public async Task CanJoinGallery_RefusesTheParties()
    {
        await Assert.That(TrialAudienceRules.CanJoinGallery(isDefendant: true, isSeatedJuror: false)).IsFalse();
        await Assert.That(TrialAudienceRules.CanJoinGallery(isDefendant: false, isSeatedJuror: true)).IsFalse();
        await Assert.That(TrialAudienceRules.CanJoinGallery(isDefendant: true, isSeatedJuror: true)).IsFalse();
        await Assert.That(TrialAudienceRules.CanJoinGallery(isDefendant: false, isSeatedJuror: false)).IsTrue();
    }
}
