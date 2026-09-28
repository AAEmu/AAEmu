using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// A client that asks to stop casting keeps showing that timeline until the server closes it. When the
/// server holds nothing for the timeline the request names, the request is the only signal left that the
/// cast is over, so it has to be answered with that timeline's end.
/// </summary>
public class CastReleaseRulesTests
{
    [Test]
    public async Task StopForATimelineThePlayerIsStillCasting_IsNotReleased()
    {
        await Assert.That(CastReleaseRules.ServerHoldsCast(208, 208)).IsTrue();
        await Assert.That(CastReleaseRules.ShouldReleaseOrphanedSkillTimeline(208, 208)).IsFalse();
    }

    [Test]
    public async Task StopAfterTheCastAlreadyEnded_IsReleased()
    {
        // The cast is over, nothing is running, and the client is still holding the bar it was shown.
        await Assert.That(CastReleaseRules.ServerHoldsCast(208, 0)).IsFalse();
        await Assert.That(CastReleaseRules.ShouldReleaseOrphanedSkillTimeline(208, 0)).IsTrue();
    }

    [Test]
    public async Task StopForAnOlderTimelineWhileANewerOneRuns_ReleasesOnlyTheOlderOne()
    {
        // The client's request names the timeline it is giving up; the newer cast is not what it asked about.
        await Assert.That(CastReleaseRules.ServerHoldsCast(208, 209)).IsFalse();
        await Assert.That(CastReleaseRules.ShouldReleaseOrphanedSkillTimeline(208, 209)).IsTrue();
        await Assert.That(CastReleaseRules.ShouldReleaseOrphanedSkillTimeline(209, 209)).IsFalse();
    }

    [Test]
    public async Task ARequestThatNamesNoTimeline_ReleasesNothing()
    {
        await Assert.That(CastReleaseRules.ServerHoldsCast(0, 0)).IsFalse();
        await Assert.That(CastReleaseRules.ShouldReleaseOrphanedSkillTimeline(0, 0)).IsFalse();
        await Assert.That(CastReleaseRules.ShouldReleaseOrphanedSkillTimeline(0, 208)).IsFalse();
    }
}
