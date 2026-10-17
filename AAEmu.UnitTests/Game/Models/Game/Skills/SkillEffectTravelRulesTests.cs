using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillEffectTravelRulesTests
{
    [Test]
    public async Task TravelDelay_BelowOneMetrePerSecond_IsZero()
    {
        // Quest capture rows ship ~0.05 m/s; a literal read would hold the cast for minutes.
        await Assert.That(SkillEffectTravelRules.TravelDelayMs(0.05f, 10f)).IsEqualTo(0);
        await Assert.That(SkillEffectTravelRules.TravelDelayMs(0.99f, 100f)).IsEqualTo(0);
        await Assert.That(SkillEffectTravelRules.TravelDelayMs(0f, 10f)).IsEqualTo(0);
    }

    [Test]
    public async Task TravelDelay_AtOrAboveOneMetrePerSecond_IsDistanceOverSpeed()
    {
        await Assert.That(SkillEffectTravelRules.TravelDelayMs(1f, 10f)).IsEqualTo(10000);
        await Assert.That(SkillEffectTravelRules.TravelDelayMs(10f, 10f)).IsEqualTo(1000);
        await Assert.That(SkillEffectTravelRules.TravelDelayMs(100f, 50f)).IsEqualTo(500);
    }

    [Test]
    public async Task TravelDelay_NonPositiveDistance_IsZero()
    {
        await Assert.That(SkillEffectTravelRules.TravelDelayMs(10f, 0f)).IsEqualTo(0);
        await Assert.That(SkillEffectTravelRules.TravelDelayMs(10f, -1f)).IsEqualTo(0);
    }
}
