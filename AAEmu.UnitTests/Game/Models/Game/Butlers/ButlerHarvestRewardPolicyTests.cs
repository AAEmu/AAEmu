using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Formulas;

namespace AAEmu.UnitTests.Game.Models.Game.Butlers;

public sealed class ButlerHarvestRewardPolicyTests
{
    [Test]
    public async Task Experience_UsesContentFormula67WithFarmhandParameters()
    {
        var formula = CreateFormula(parameters =>
            (parameters["butler_level"] * 2 + 30) * (parameters["labor_power"] / 2d));
        var formulaManager = Mock.Of<IFormulaManager>();
        formulaManager.GetFormula((uint)FormulaKind.ButlerExpByLaborPower).Returns(formula);
        var policy = new ButlerHarvestRewardPolicy(formulaManager.Object);

        var valid = policy.TryCalculateExperienceAward(10, 1, 1, 0, out var award, out var total);

        await Assert.That(valid).IsTrue();
        await Assert.That(award).IsEqualTo((ulong)160);
        await Assert.That(total).IsEqualTo((ulong)160);
        formulaManager.GetFormula((uint)FormulaKind.ButlerExpByLaborPower).WasCalled(Times.Once);
    }

    [Test]
    public async Task ZeroLaborPower_UsesContentFormulaZeroBranch()
    {
        var formula = CreateFormula(parameters =>
            parameters["labor_power"] == 0 ? (parameters["butler_level"] * 2 + 30) * 0.5 : 0);
        var formulaManager = Mock.Of<IFormulaManager>();
        formulaManager.GetFormula((uint)FormulaKind.ButlerExpByLaborPower).Returns(formula);
        var policy = new ButlerHarvestRewardPolicy(formulaManager.Object);

        var valid = policy.TryCalculateExperienceAward(0, 1, 1, 0, out var award, out var total);

        await Assert.That(valid).IsTrue();
        await Assert.That(award).IsEqualTo((ulong)16);
        await Assert.That(total).IsEqualTo((ulong)16);
    }

    [Test]
    public async Task BasisPointBoundary_UsesConfiguredScale()
    {
        var roll = 99L;
        var policy = new ButlerHarvestRewardPolicy((_, _) => 0, _ => roll);

        await Assert.That(policy.GrantsBonus(100, 0, 10_000)).IsTrue();
        roll = 100;
        await Assert.That(policy.GrantsBonus(100, 0, 10_000)).IsFalse();
    }

    [Test]
    public async Task ZeroScale_DisablesBonusWithoutRolling()
    {
        var rolls = 0;
        var policy = new ButlerHarvestRewardPolicy((_, _) => 0, _ => { rolls++; return 0; });

        await Assert.That(policy.GrantsBonus(uint.MaxValue, uint.MaxValue, 0)).IsFalse();
        await Assert.That(rolls).IsEqualTo(0);
    }

    [Test]
    public async Task Experience_UsesPositiveIntegerTruncation()
    {
        var policy = new ButlerHarvestRewardPolicy((labor, level) => labor * 4.5 + level, _ => 0);

        var valid = policy.TryCalculateExperienceAward(10, 3, 0.5, 100,
            out var award, out var total);

        await Assert.That(valid).IsTrue();
        await Assert.That(award).IsEqualTo((ulong)24);
        await Assert.That(total).IsEqualTo((ulong)124);
    }

    [Test]
    public async Task ZeroExperienceRate_DisablesAward()
    {
        var policy = new ButlerHarvestRewardPolicy((_, _) => double.PositiveInfinity, _ => 0);

        var valid = policy.TryCalculateExperienceAward(10, 3, 0, 100,
            out var award, out var total);

        await Assert.That(valid).IsTrue();
        await Assert.That(award).IsEqualTo((ulong)0);
        await Assert.That(total).IsEqualTo((ulong)100);
    }

    [Test]
    public async Task ExperienceAddition_RejectsStorageOverflow()
    {
        var policy = new ButlerHarvestRewardPolicy((_, _) => 10, _ => 0);

        var valid = policy.TryCalculateExperienceAward(1, 40, 1, ulong.MaxValue - 3,
            out var award, out var total);

        await Assert.That(valid).IsFalse();
        await Assert.That(award).IsEqualTo((ulong)0);
        await Assert.That(total).IsEqualTo(ulong.MaxValue - 3);
    }

    [Test]
    public async Task NonFiniteFormulaResult_FailsClosed()
    {
        var policy = new ButlerHarvestRewardPolicy((_, _) => double.NaN, _ => 0);

        var valid = policy.TryCalculateExperienceAward(1, 1, 1, 0, out _, out _);

        await Assert.That(valid).IsFalse();
    }

    private static Formula CreateFormula(Func<Dictionary<string, double>, double> expression)
    {
        var formula = new Formula { Id = (uint)FormulaKind.ButlerExpByLaborPower };
        typeof(Formula).GetProperty("Expression", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(formula, expression);
        return formula;
    }
}
