using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class AdditionalSkillPointBudgetRulesTests
{
    [Test]
    public async Task TotalBudget_AddsBonusToLevelTable()
    {
        await Assert.That(AdditionalSkillPointBudgetRules.TotalBudget(20, 1)).IsEqualTo(21);
        await Assert.That(AdditionalSkillPointBudgetRules.TotalBudget(20, 0)).IsEqualTo(20);
    }

    [Test]
    public async Task Remaining_SubtractsUsed()
    {
        await Assert.That(AdditionalSkillPointBudgetRules.Remaining(20, 1, 19)).IsEqualTo(2);
        await Assert.That(AdditionalSkillPointBudgetRules.Remaining(20, 0, 20)).IsEqualTo(0);
    }

    [Test]
    public async Task TryAdd_RejectsNonPositive()
    {
        await Assert.That(AdditionalSkillPointBudgetRules.TryAdd(0, 0, out _)).IsFalse();
        await Assert.That(AdditionalSkillPointBudgetRules.TryAdd(1, -1, out _)).IsFalse();
    }

    [Test]
    public async Task TryAdd_Increments()
    {
        await Assert.That(AdditionalSkillPointBudgetRules.TryAdd(0, 1, out var next)).IsTrue();
        await Assert.That(next).IsEqualTo(1u);
        await Assert.That(AdditionalSkillPointBudgetRules.TryAdd(1, 1, out next)).IsTrue();
        await Assert.That(next).IsEqualTo(2u);
    }
}
