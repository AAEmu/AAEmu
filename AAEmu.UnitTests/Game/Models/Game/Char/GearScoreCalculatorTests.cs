using AAEmu.Game.Models.Game.Char;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class GearScoreCalculatorTests
{
    [Test]
    public async Task StoredMultiplier_IsHundredthsOfTheFormulaMultiplier()
    {
        // A weapon the table weighs 2.2, an armor slot it weighs 0.78.
        await Assert.That(GearScoreCalculator.FromStoredMultiplier(220)).IsEqualTo(2.2);
        await Assert.That(GearScoreCalculator.FromStoredMultiplier(78)).IsEqualTo(0.78);
        await Assert.That(GearScoreCalculator.FromStoredMultiplier(330)).IsEqualTo(3.3);
    }

    [Test]
    public async Task StoredMultiplier_CosmeticTiersLandOnTheValuesTheArmorFormulaComparesAgainst()
    {
        // Formula 56 branches on gear_score_multiplier - 0.01 and - 0.02 for the two cosmetic tiers,
        // which the table stores as 1 and 2.
        await Assert.That(GearScoreCalculator.FromStoredMultiplier(1)).IsEqualTo(0.01);
        await Assert.That(GearScoreCalculator.FromStoredMultiplier(2)).IsEqualTo(0.02);
    }

    [Test]
    public async Task StoredMultiplier_MissingRowStaysZero()
    {
        await Assert.That(GearScoreCalculator.FromStoredMultiplier(0)).IsEqualTo(0.0);
    }
}
