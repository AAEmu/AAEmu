using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class CooldownReductionRulesTests
{
    [Test]
    public async Task PercentBranch_UsesOriginalDuration()
    {
        await Assert.That(CooldownReductionRules.CalculateRemaining(
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(8), flatMilliseconds: 250, percent: 50))
            .IsEqualTo(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task FlatBranch_ReducesCurrentRemaining()
    {
        await Assert.That(CooldownReductionRules.CalculateRemaining(
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(4), flatMilliseconds: 250, percent: 0))
            .IsEqualTo(TimeSpan.FromMilliseconds(3750));
    }

    [Test]
    public async Task PercentBranch_WinsWhenBothValuesArePresent()
    {
        await Assert.That(CooldownReductionRules.CalculateRemaining(
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(8), flatMilliseconds: 250, percent: 50))
            .IsEqualTo(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task PositiveReduction_IsClampedToZero()
    {
        await Assert.That(CooldownReductionRules.CalculateRemaining(
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(4), flatMilliseconds: 0, percent: 150))
            .IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task RemainingAboveOriginalDuration_IsClampedToOriginal()
    {
        await Assert.That(CooldownReductionRules.CalculateRemaining(
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12), flatMilliseconds: 0, percent: 0))
            .IsEqualTo(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task NegativePercentBranch_DoesNotExtendTheCooldown()
    {
        await Assert.That(CooldownReductionRules.CalculateRemaining(
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(4), flatMilliseconds: 0, percent: -50))
            .IsEqualTo(TimeSpan.FromSeconds(4));
    }

    [Test]
    public async Task NegativeFlatBranch_DoesNotExtendTheCooldown()
    {
        await Assert.That(CooldownReductionRules.CalculateRemaining(
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(4), flatMilliseconds: -250, percent: 0))
            .IsEqualTo(TimeSpan.FromSeconds(4));
    }

    [Test]
    public async Task ExpiredCooldown_StaysAtZero()
    {
        await Assert.That(CooldownReductionRules.CalculateRemaining(
                TimeSpan.FromSeconds(10), TimeSpan.Zero, flatMilliseconds: 250, percent: 0))
            .IsEqualTo(TimeSpan.Zero);
    }
}
