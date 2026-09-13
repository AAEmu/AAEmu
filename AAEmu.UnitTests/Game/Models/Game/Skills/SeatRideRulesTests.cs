using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SeatRideRulesTests
{
    [Test]
    public async Task TimeoutSpecialEffect_IsALiftRide()
    {
        var triggers = new[]
        {
            new BuffTriggerTemplate
            {
                Kind = BuffEventTriggerKind.Timeout,
                Effect = new SpecialEffect { SpecialEffectTypeId = SpecialType.SkillUse }
            }
        };

        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(triggers)).IsTrue();
    }

    [Test]
    public async Task TimeoutBuffEffect_IsBedSleep_NotALiftRide()
    {
        var triggers = new[]
        {
            new BuffTriggerTemplate
            {
                Kind = BuffEventTriggerKind.Timeout,
                Effect = new BuffEffect { Buff = new BuffTemplate { Id = 5208 } }
            }
        };

        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(triggers)).IsFalse();
    }

    [Test]
    public async Task OtherKindsAndEmpty_DoNotTimeout()
    {
        var startedSpecial = new[]
        {
            new BuffTriggerTemplate
            {
                Kind = BuffEventTriggerKind.Started,
                Effect = new SpecialEffect()
            }
        };

        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(startedSpecial)).IsFalse();
        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(Array.Empty<BuffTriggerTemplate>())).IsFalse();
        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond((IEnumerable<BuffTriggerTemplate>)null)).IsFalse();
        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(0)).IsFalse();
    }

    [Test]
    public async Task SkillEffects_TimeoutOnlyWhenABuffTriggersASpecialEffect()
    {
        var bed = new SkillTemplate
        {
            Effects =
            [
                new SkillEffect
                {
                    Template = new BuffEffect { Buff = new BuffTemplate { Id = 4895 } }
                }
            ]
        };
        var lift = new SkillTemplate
        {
            Effects =
            [
                new SkillEffect
                {
                    Template = new BuffEffect { Buff = new BuffTemplate { Id = 23741 } }
                }
            ]
        };

        IReadOnlyList<BuffTriggerTemplate> BedTriggers(uint buffId) =>
        [
            new BuffTriggerTemplate
            {
                Kind = BuffEventTriggerKind.Timeout,
                Effect = new BuffEffect { Buff = new BuffTemplate { Id = 5208 } }
            }
        ];

        IReadOnlyList<BuffTriggerTemplate> LiftTriggers(uint buffId) =>
        [
            new BuffTriggerTemplate
            {
                Kind = BuffEventTriggerKind.Timeout,
                Effect = new SpecialEffect { SpecialEffectTypeId = SpecialType.SkillUse }
            }
        ];

        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(bed, BedTriggers)).IsFalse();
        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(lift, LiftTriggers)).IsTrue();
        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond(lift, _ => [])).IsFalse();
        await Assert.That(SeatRideRules.ShouldTimeoutOnUnbond((SkillTemplate)null, LiftTriggers)).IsFalse();
    }
}
