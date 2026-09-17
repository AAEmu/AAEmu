using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// heal_pet (type 56): value1 and value2 are one percentage band applied to the pet's maximum health — the
/// 10 %, 20 % and 50 % 소환수 부상 치료 물약 tiers, whose rows all set both slots to the same number.
/// </summary>
public class PetHealRulesTests
{
    [Test]
    public async Task AnEqualPair_IsAOnePointBand()
    {
        var (min, max) = PetHealRules.PercentBand(20, 20);

        await Assert.That(min).IsEqualTo(20);
        await Assert.That(max).IsEqualTo(20);
    }

    [Test]
    public async Task ASwappedPair_IsReadLowEndFirst()
    {
        var (min, max) = PetHealRules.PercentBand(50, 10);

        await Assert.That(min).IsEqualTo(10);
        await Assert.That(max).IsEqualTo(50);
    }

    [Test]
    public async Task NegativeSlots_AreNotPercentages()
    {
        var (min, max) = PetHealRules.PercentBand(-50, -1);

        await Assert.That(min).IsEqualTo(0);
        await Assert.That(max).IsEqualTo(0);
    }

    [Test]
    public async Task HealthRestored_IsThePercentOfMaximum()
    {
        await Assert.That(PetHealRules.AmountFor(20, 10000)).IsEqualTo(2000);
        await Assert.That(PetHealRules.AmountFor(50, 12345)).IsEqualTo(6172);
        await Assert.That(PetHealRules.AmountFor(10, 5)).IsEqualTo(0);
    }

    [Test]
    public async Task NoPercentageOrNoHealth_HealsNothing()
    {
        await Assert.That(PetHealRules.AmountFor(0, 10000)).IsEqualTo(0);
        await Assert.That(PetHealRules.AmountFor(-20, 10000)).IsEqualTo(0);
        await Assert.That(PetHealRules.AmountFor(50, 0)).IsEqualTo(0);
    }

    [Test]
    public async Task ALargePet_DoesNotOverflow()
    {
        // 2,147,483,647 * 50 still fits in the long the rule works in.
        await Assert.That(PetHealRules.AmountFor(50, int.MaxValue)).IsEqualTo(1073741823);
    }
}
