using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillComboRulesTests
{
    private const uint Root = 1;
    private const uint Second = 2;
    private const uint Third = 3;

    private static uint? NextOf(uint id) => id switch
    {
        Root => Second,
        Second => Third,
        _ => null
    };

    [Test]
    public async Task TryGetComboFollowUp_ReadsComboSpecialOnly()
    {
        var template = new SkillTemplate
        {
            Effects =
            [
                new SkillEffect
                {
                    Template = new SpecialEffect
                    {
                        SpecialEffectTypeId = SpecialType.Cooldown,
                        Value1 = 1000
                    }
                },
                new SkillEffect
                {
                    Template = new SpecialEffect
                    {
                        SpecialEffectTypeId = SpecialType.Combo,
                        Value1 = 22,
                        Value2 = 1000
                    }
                }
            ]
        };

        await Assert.That(SkillComboRules.TryGetComboFollowUp(template, out var next, out var delay)).IsTrue();
        await Assert.That(next).IsEqualTo(22u);
        await Assert.That(delay).IsEqualTo(1000);
        await Assert.That(SkillComboRules.TryGetComboFollowUp(new SkillTemplate(), out _, out _)).IsFalse();
    }

    [Test]
    public async Task WalkChain_FollowsComboLinks()
    {
        var chain = SkillComboRules.WalkChain(Second, NextOf);
        await Assert.That(chain).IsEquivalentTo(new uint[] { Second, Third });
    }

    [Test]
    public async Task IsComboFollowUpOf_FindsSecondAndThirdHit()
    {
        await Assert.That(SkillComboRules.IsComboFollowUpOf(Root, Second, NextOf)).IsTrue();
        await Assert.That(SkillComboRules.IsComboFollowUpOf(Root, Third, NextOf)).IsTrue();
        await Assert.That(SkillComboRules.IsComboFollowUpOf(Root, Root, NextOf)).IsFalse();
        await Assert.That(SkillComboRules.IsComboFollowUpOf(Second, Root, NextOf)).IsFalse();
    }

    [Test]
    public async Task IsHeldOrComboFollowUp_AllowsLaterHitsOfAHeldRoot()
    {
        // Triple Slash / Quake heir / Whirlwind Slash: only the first hit is held; the bar swaps to 2 then 3.
        uint[] held = [Root];

        await Assert.That(SkillComboRules.IsHeldOrComboFollowUp(Root, held, NextOf)).IsTrue();
        await Assert.That(SkillComboRules.IsHeldOrComboFollowUp(Second, held, NextOf)).IsTrue();
        await Assert.That(SkillComboRules.IsHeldOrComboFollowUp(Third, held, NextOf)).IsTrue();
        await Assert.That(SkillComboRules.IsHeldOrComboFollowUp(99, held, NextOf)).IsFalse();
        await Assert.That(SkillComboRules.IsHeldOrComboFollowUp(Second, [], NextOf)).IsFalse();
    }
}
