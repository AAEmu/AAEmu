using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillCombatResourceRulesTests
{
    [Test]
    public async Task NoBand_AllowsEverything()
    {
        await Assert.That(SkillCombatResourceRules.IsInRange(0, 0, 0)).IsTrue();
        await Assert.That(SkillCombatResourceRules.IsInRange(7, 0, 0)).IsTrue();
    }

    [Test]
    public async Task CastGate_NeedsThePoolInsideTheBand()
    {
        // 34276 저승과 공간 and 43711 카마하 쾌속정 대포 발포: exactly 1.
        await Assert.That(SkillCombatResourceRules.AllowsCast(1, 1, 1)).IsTrue();
        await Assert.That(SkillCombatResourceRules.AllowsCast(1, 1, 0)).IsFalse();
        await Assert.That(SkillCombatResourceRules.AllowsCast(1, 1, 2)).IsFalse();

        // 34088 증오 분노 테스트: 2..4.
        await Assert.That(SkillCombatResourceRules.AllowsCast(2, 4, 1)).IsFalse();
        await Assert.That(SkillCombatResourceRules.AllowsCast(2, 4, 2)).IsTrue();
        await Assert.That(SkillCombatResourceRules.AllowsCast(2, 4, 4)).IsTrue();
        await Assert.That(SkillCombatResourceRules.AllowsCast(2, 4, 5)).IsFalse();

        // 도발의 외침 names resource 3 with its own ceiling, so it asks for nothing.
        await Assert.That(SkillCombatResourceRules.AllowsCast(0, 5000, 0)).IsTrue();
        await Assert.That(SkillCombatResourceRules.AllowsCast(0, 5000, 5000)).IsTrue();
    }

    [Test]
    public async Task ReversedBand_IsReadAsAuthored()
    {
        await Assert.That(SkillCombatResourceRules.IsInRange(3, 5, 1)).IsTrue();
        await Assert.That(SkillCombatResourceRules.IsInRange(6, 5, 1)).IsFalse();
    }

    [Test]
    public async Task EffectGate_OnlyAppliesWhenTheEffectNamesAPool()
    {
        // 10159 정신 파괴: target resource 2 must sit in 1..5.
        await Assert.That(SkillCombatResourceRules.AllowsEffect(1, 5, 2, 3)).IsTrue();
        await Assert.That(SkillCombatResourceRules.AllowsEffect(1, 5, 2, 0)).IsFalse();
        await Assert.That(SkillCombatResourceRules.AllowsEffect(1, 5, 2, 6)).IsFalse();

        // No pool named: 39 of the 41 banded rows are like this and none of them is gated.
        await Assert.That(SkillCombatResourceRules.AllowsEffect(1, 5, 0, 0)).IsTrue();
    }

    [Test]
    public async Task StackBand_IsInclusive()
    {
        // 49770 carries siblings at 1..4, 5..15 and 10..15 on tag 5769.
        await Assert.That(SkillCombatResourceRules.AllowsStackBand(4, 1, 4)).IsTrue();
        await Assert.That(SkillCombatResourceRules.AllowsStackBand(5, 1, 4)).IsFalse();
        await Assert.That(SkillCombatResourceRules.AllowsStackBand(5, 5, 15)).IsTrue();
        await Assert.That(SkillCombatResourceRules.AllowsStackBand(15, 5, 15)).IsTrue();
        await Assert.That(SkillCombatResourceRules.AllowsStackBand(16, 5, 15)).IsFalse();

        // 42459 wants exactly 5 stacks.
        await Assert.That(SkillCombatResourceRules.AllowsStackBand(5, 5, 5)).IsTrue();
        await Assert.That(SkillCombatResourceRules.AllowsStackBand(4, 5, 5)).IsFalse();
    }

    [Test]
    public async Task StackBand_ZeroMeansNoGate()
    {
        // 48,726 of the 48,744 effect rows carry no band at all.
        await Assert.That(SkillCombatResourceRules.AllowsStackBand(0, 0, 0)).IsTrue();
        await Assert.That(SkillCombatResourceRules.AllowsStackBand(99, 0, 0)).IsTrue();
    }

    [Test]
    public async Task CastingUseChance_IsTheEndValue_AndTheDefaultIsNoGate()
    {
        // The shipped default: 1..100.
        await Assert.That(SkillCombatResourceRules.AllowsCastingUseChance(3000, 100, 99.9)).IsTrue();
        // An instant skill has no cast to interpolate across.
        await Assert.That(SkillCombatResourceRules.AllowsCastingUseChance(0, 0, 99.9)).IsTrue();
        // The four rows that deviate, read at their end value.
        await Assert.That(SkillCombatResourceRules.AllowsCastingUseChance(3000, 39, 30)).IsTrue();
        await Assert.That(SkillCombatResourceRules.AllowsCastingUseChance(3000, 39, 45)).IsFalse();
        await Assert.That(SkillCombatResourceRules.AllowsCastingUseChance(3000, 99, 98.5)).IsTrue();
    }
}
