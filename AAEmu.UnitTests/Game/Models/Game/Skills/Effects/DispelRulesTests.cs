using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <see cref="DispelRules"/>: the count a dispel takes off, and which side of the Good/Bad split a tagged
/// removal belongs to.
/// </summary>
public class DispelRulesTests
{
    [Test]
    public async Task StackCount_ReadsTheStackColumnWhenAuthored()
    {
        // 1,012 of the 3,168 rows author a non-zero stack (10 on 272, 1 on 540, 100 on 5, 250 on 1).
        await Assert.That(DispelRules.StackCount(stack: 10, dispelCount: 1, cureCount: 0)).IsEqualTo(10);
        await Assert.That(DispelRules.StackCount(stack: 250, dispelCount: 0, cureCount: 1)).IsEqualTo(250);
    }

    [Test]
    public async Task StackCount_WithoutAStack_FallsBackToTheCounts()
    {
        // 0 is "no stack authored", the majority of the table.
        await Assert.That(DispelRules.StackCount(stack: 0, dispelCount: 3, cureCount: 1)).IsEqualTo(3);
        await Assert.That(DispelRules.StackCount(stack: 0, dispelCount: 1, cureCount: 5)).IsEqualTo(5);
    }

    [Test]
    public async Task StackCount_WithNothingAuthored_TakesOne()
    {
        await Assert.That(DispelRules.StackCount(stack: 0, dispelCount: 0, cureCount: 0)).IsEqualTo(1);
    }

    [Test]
    public async Task TargetsGoodBuffs_FollowsTheDispelCureSplit()
    {
        // A hostile cast dispels Good, a friendly one cures Bad - the rule the untagged path already used.
        await Assert.That(DispelRules.TargetsGoodBuffs(casterCanAttack: true, dispelCount: 1, cureCount: 0)).IsTrue();
        await Assert.That(DispelRules.TargetsGoodBuffs(casterCanAttack: false, dispelCount: 0, cureCount: 1)).IsFalse();
    }

    [Test]
    public async Task TargetsGoodBuffs_ARowThatDoesBoth_FollowsTheRelation()
    {
        // Sail fold state is a "debuff" on a friendly hull and those rows author cure_count, so they cure.
        await Assert.That(DispelRules.TargetsGoodBuffs(casterCanAttack: false, dispelCount: 1, cureCount: 1)).IsFalse();
        await Assert.That(DispelRules.TargetsGoodBuffs(casterCanAttack: true, dispelCount: 1, cureCount: 1)).IsTrue();
    }
}
