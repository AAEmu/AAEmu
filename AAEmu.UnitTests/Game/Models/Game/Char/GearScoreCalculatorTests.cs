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

    [Test]
    public async Task Combine_KeepsThePieceAsBareAndAddsGemsToTheTotal()
    {
        var none = GearScoreCalculator.Combine(9412, 0);
        await Assert.That(none.RoundedTotal).IsEqualTo(9412);
        await Assert.That(none.RoundedBare).IsEqualTo(9412);

        var stones = GearScoreCalculator.Combine(9774, 2);
        await Assert.That(stones.RoundedTotal).IsEqualTo(9776);
        await Assert.That(stones.RoundedBare).IsEqualTo(9774);
        await Assert.That(stones.RoundedGems).IsEqualTo(2);

        // A level-1 stone is 2.5 before rounding, and the window shows that as +2.
        var levelOne = GearScoreCalculator.Combine(9412, 2.5);
        await Assert.That(levelOne.RoundedBare).IsEqualTo(9412);
        await Assert.That(levelOne.RoundedGems).IsEqualTo(2);
        await Assert.That(levelOne.RoundedTotal).IsEqualTo(9414);
    }

    [Test]
    public async Task ItemLevelForScore_AddsTheReinforceFormulaResultRatherThanTheLadderGain()
    {
        // A level-68 piece whose slot ladder says 2.5 does not become 70.5. The reinforce formula
        // turns that 2.5 into the bonus that is actually added (1.222 for this piece).
        var level = GearScoreCalculator.ItemLevelForScore(68, 2.5, (_, _) => 1.222);

        await Assert.That(level).IsEqualTo(69.222);
        await Assert.That(GearScoreCalculator.ItemLevelForScore(68, 0, (_, _) => 5)).IsEqualTo(68);
    }

    [Test]
    public async Task TemperMultiplier_LeavesAnUntemperedPieceAloneAndScalesATemperedOne()
    {
        await Assert.That(GearScoreCalculator.TemperMultiplier(0)).IsEqualTo(1);
        await Assert.That(GearScoreCalculator.TemperMultiplier(200)).IsEqualTo(1.2);
        await Assert.That(GearScoreCalculator.TemperMultiplier(10)).IsEqualTo(1.01);
    }

    [Test]
    public async Task TruncateTenth_DropsAnythingPastOneDecimal()
    {
        await Assert.That(GearScoreCalculator.TruncateTenth(650.43)).IsEqualTo(650.4);
        await Assert.That(GearScoreCalculator.TruncateTenth(1496.4319)).IsEqualTo(1496.4);
        await Assert.That(GearScoreCalculator.TruncateTenth(0)).IsEqualTo(0);
    }

    [Test]
    public async Task CountsTowardGearScore_KeepsWornGearAndLeavesTheBodySlotsOut()
    {
        await Assert.That(GearScoreCalculator.CountsTowardGearScore(0)).IsTrue();
        await Assert.That(GearScoreCalculator.CountsTowardGearScore(15)).IsTrue();
        await Assert.That(GearScoreCalculator.CountsTowardGearScore(26)).IsTrue();
        await Assert.That(GearScoreCalculator.CountsTowardGearScore(27)).IsTrue();
        await Assert.That(GearScoreCalculator.CountsTowardGearScore(19)).IsFalse();
        await Assert.That(GearScoreCalculator.CountsTowardGearScore(24)).IsFalse();
        await Assert.That(GearScoreCalculator.CountsTowardGearScore(31)).IsFalse();
    }

    [Test]
    public async Task GemItemLevels_UseTheStoneAndSkipEmptySockets()
    {
        var levels = GearScoreCalculator.GemItemLevels([0, 9, 0], id => id == 9 ? 1 : null);

        await Assert.That(levels).IsEquivalentTo(new[] { 1 });
    }

    [Test]
    public async Task GemItemLevels_SkipAStoneWhoseTemplateIsMissing()
    {
        var levels = GearScoreCalculator.GemItemLevels([4], _ => null);

        await Assert.That(levels).IsEmpty();
    }

    [Test]
    public async Task ScoreGems_UsesEachStonesLevelInTheSocketAndGemFormulas()
    {
        // Socket formula is item_level * 2, gem formula is item_level * 0.5.
        static double Socket(double level) => level * 2;
        static double Gem(double level) => level * 0.5;

        await Assert.That(GearScoreCalculator.ScoreGems([1], Socket, Gem)).IsEqualTo(2.5);
        // Feeding the piece's level (68) instead of the stone's is what painted +170.
        await Assert.That(GearScoreCalculator.ScoreGems([68], Socket, Gem)).IsEqualTo(170);
    }
}
