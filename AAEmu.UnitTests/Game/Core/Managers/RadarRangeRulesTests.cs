using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class RadarRangeRulesTests
{
    [Test]
    public async Task BuffRange_KeepsNearbySchool()
    {
        await Assert.That(RadarRangeRules.IsInRange(800f, 1000f)).IsTrue();
    }

    [Test]
    public async Task BuffRange_HidesSchoolPastTheFinder()
    {
        await Assert.That(RadarRangeRules.IsInRange(12000f, 1000f)).IsFalse();
    }

    [Test]
    public async Task DisabledFinder_ShowsNothing()
    {
        await Assert.That(RadarRangeRules.IsInRange(10f, 0f)).IsFalse();
    }

    [Test]
    public async Task AccessLevelMustNotWidenTheCircle()
    {
        const float buffRange = 850f;
        const float acrossTheMap = 40000f;
        await Assert.That(RadarRangeRules.IsInRange(acrossTheMap, buffRange)).IsFalse();
    }
}
