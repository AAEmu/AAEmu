using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

/// <summary>
/// Which <c>indun_event_zone_score_level_changeds</c> row a zone-score level move fires. The row watches one
/// level and one direction: 0 any change, 1 rising, 2 falling. Zone group 130's rows are way 2 to level 1,
/// way 0 to level 2 and way 1 to level 3.
/// </summary>
public class IndunZoneScoreLevelRulesTests
{
    [Test]
    public async Task AnyDirection_FiresOnARiseAndOnAFall()
    {
        await Assert.That(IndunRoundRules.ZoneScoreLevelChangeMatches(
            IndunRoundRules.ZoneScoreChangeWayAny, rowLevel: 2, previousLevel: 1, level: 2)).IsTrue();
        await Assert.That(IndunRoundRules.ZoneScoreLevelChangeMatches(
            IndunRoundRules.ZoneScoreChangeWayAny, rowLevel: 2, previousLevel: 3, level: 2)).IsTrue();
    }

    [Test]
    public async Task Rising_FiresOnlyOnARise()
    {
        await Assert.That(IndunRoundRules.ZoneScoreLevelChangeMatches(
            IndunRoundRules.ZoneScoreChangeWayRising, rowLevel: 3, previousLevel: 2, level: 3)).IsTrue();
        await Assert.That(IndunRoundRules.ZoneScoreLevelChangeMatches(
            IndunRoundRules.ZoneScoreChangeWayRising, rowLevel: 3, previousLevel: 4, level: 3)).IsFalse();
    }

    [Test]
    public async Task Falling_FiresOnlyOnAFall()
    {
        await Assert.That(IndunRoundRules.ZoneScoreLevelChangeMatches(
            IndunRoundRules.ZoneScoreChangeWayFalling, rowLevel: 1, previousLevel: 2, level: 1)).IsTrue();
        await Assert.That(IndunRoundRules.ZoneScoreLevelChangeMatches(
            IndunRoundRules.ZoneScoreChangeWayFalling, rowLevel: 1, previousLevel: 0, level: 1)).IsFalse();
    }

    [Test]
    public async Task LandingOnAnotherLevel_FiresNothing()
    {
        await Assert.That(IndunRoundRules.ZoneScoreLevelChangeMatches(
            IndunRoundRules.ZoneScoreChangeWayAny, rowLevel: 2, previousLevel: 1, level: 3)).IsFalse();
    }

    [Test]
    public async Task MoveThatChangesNoLevel_FiresNothing()
    {
        await Assert.That(IndunRoundRules.ZoneScoreLevelChangeMatches(
            IndunRoundRules.ZoneScoreChangeWayAny, rowLevel: 2, previousLevel: 2, level: 2)).IsFalse();
    }

    [Test]
    public async Task UnknownWay_FiresNothing()
    {
        await Assert.That(IndunRoundRules.ZoneScoreLevelChangeMatches(
            99, rowLevel: 2, previousLevel: 1, level: 2)).IsFalse();
    }
}
