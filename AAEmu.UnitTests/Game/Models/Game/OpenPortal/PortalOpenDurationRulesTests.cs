using AAEmu.Game.Models.Game.OpenPortal;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.OpenPortal;

public class PortalOpenDurationRulesTests
{
    [Test]
    public async Task DurationFor_BelowDeath20_Is30Seconds()
    {
        await Assert.That(PortalOpenDurationRules.DurationFor(0)).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(PortalOpenDurationRules.DurationFor(19)).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task DurationFor_Death20OrAbove_Is60Seconds()
    {
        await Assert.That(PortalOpenDurationRules.DurationFor(20)).IsEqualTo(TimeSpan.FromSeconds(60));
        await Assert.That(PortalOpenDurationRules.DurationFor(55)).IsEqualTo(TimeSpan.FromSeconds(60));
    }

    [Test]
    public async Task ExtendedAbility_IsDeathOccultism()
    {
        await Assert.That(PortalOpenDurationRules.ExtendedAbility).IsEqualTo(AbilityType.Death);
        await Assert.That(PortalOpenDurationRules.ExtendedAbilityLevel).IsEqualTo((byte)20);
    }
}
