using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillLaborRulesTests
{
    [Test]
    public async Task NoCost_IsAlwaysAffordable()
    {
        // 3,338 of the 38,043 skills declare a consume_lp, and none of them is an ability skill.
        await Assert.That(SkillLaborRules.CanAfford(0, 0, 0)).IsTrue();
        await Assert.That(SkillLaborRules.CanAfford(-5, 0, 0)).IsTrue();
    }

    [Test]
    public async Task CostWithinBothPools_IsAffordable()
    {
        // Labor and the server-local pool are spent together by ChangeLabor.
        await Assert.That(SkillLaborRules.CanAfford(50, 30, 20)).IsTrue();
        await Assert.That(SkillLaborRules.CanAfford(50, 50, 0)).IsTrue();
        await Assert.That(SkillLaborRules.CanAfford(50, 0, 50)).IsTrue();
    }

    [Test]
    public async Task CostPastBothPools_IsNotAffordable()
    {
        // Skill 22520 무기 강화: consume_lp 50.
        await Assert.That(SkillLaborRules.CanAfford(50, 49, 0)).IsFalse();
        await Assert.That(SkillLaborRules.CanAfford(50, 0, 0)).IsFalse();
        await Assert.That(SkillLaborRules.CanAfford(1, 0, 0)).IsFalse();
    }

    [Test]
    public async Task CostPastTheShortTheDebitUses_IsNotAffordable()
    {
        // GetLaborCost clamps to short.MaxValue, and ChangeLabor takes a short.
        await Assert.That(SkillLaborRules.CanAfford(short.MaxValue, short.MaxValue, 0)).IsTrue();
        await Assert.That(SkillLaborRules.CanAfford(short.MaxValue + 1, 100000, 100000)).IsFalse();
    }

    [Test]
    public async Task PoolsCannotSatisfyACostByOverflowing()
    {
        // Both pools are ints; their sum must not wrap into a false pass.
        await Assert.That(SkillLaborRules.CanAfford(int.MaxValue, int.MaxValue, int.MaxValue)).IsFalse();
    }
}
