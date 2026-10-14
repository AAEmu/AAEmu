using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class GradeEnchantBreakRewardRulesTests
{
    private static GradeEnchantFailBreakRewardCategory WeaponBand() => new()
    {
        ItemCategoryId = 69,
        RewardId = 23,
        WearableSlotId = 0,
        MinLv = 41,
        MaxLv = 43,
        ArtifactMultiple = 10,
        WonderMultiple = 20,
        EpicMultiple = 150,
        LegendaryMultiple = 300,
        MythicMultiple = 600
    };

    private static GradeEnchantFailBreakRewardCategory ArmorBand() => new()
    {
        ItemCategoryId = 83,
        RewardId = 24,
        WearableSlotId = 1,
        MinLv = 41,
        MaxLv = 43,
        ArtifactMultiple = 10,
        WonderMultiple = 20,
        EpicMultiple = 150,
        LegendaryMultiple = 300,
        MythicMultiple = 600
    };

    [Test]
    public async Task MatchesCategory_AcceptsWeaponsOnTheSlotZeroBand()
    {
        await Assert.That(GradeEnchantBreakRewardRules.MatchesCategory(WeaponBand(), 69, 42, 0)).IsTrue();
        await Assert.That(GradeEnchantBreakRewardRules.MatchesCategory(WeaponBand(), 69, 42, 7)).IsTrue();
    }

    [Test]
    public async Task MatchesCategory_RequiresAnExactWearableSlotWhenSet()
    {
        await Assert.That(GradeEnchantBreakRewardRules.MatchesCategory(ArmorBand(), 83, 42, 1)).IsTrue();
        await Assert.That(GradeEnchantBreakRewardRules.MatchesCategory(ArmorBand(), 83, 42, 3)).IsFalse();
    }

    [Test]
    public async Task MatchesCategory_RejectsWrongCategoryOrLevel()
    {
        await Assert.That(GradeEnchantBreakRewardRules.MatchesCategory(WeaponBand(), 70, 42, 0)).IsFalse();
        await Assert.That(GradeEnchantBreakRewardRules.MatchesCategory(WeaponBand(), 69, 40, 0)).IsFalse();
        await Assert.That(GradeEnchantBreakRewardRules.MatchesCategory(WeaponBand(), 69, 44, 0)).IsFalse();
    }

    [Test]
    public async Task MultipleForGrade_MapsArtifactThroughMythic()
    {
        var band = WeaponBand();
        await Assert.That(GradeEnchantBreakRewardRules.MultipleForGrade(band, 6)).IsEqualTo(0);
        await Assert.That(GradeEnchantBreakRewardRules.MultipleForGrade(band, 7)).IsEqualTo(10);
        await Assert.That(GradeEnchantBreakRewardRules.MultipleForGrade(band, 8)).IsEqualTo(20);
        await Assert.That(GradeEnchantBreakRewardRules.MultipleForGrade(band, 9)).IsEqualTo(150);
        await Assert.That(GradeEnchantBreakRewardRules.MultipleForGrade(band, 10)).IsEqualTo(300);
        await Assert.That(GradeEnchantBreakRewardRules.MultipleForGrade(band, 11)).IsEqualTo(600);
        await Assert.That(GradeEnchantBreakRewardRules.MultipleForGrade(band, 12)).IsEqualTo(600);
    }

    [Test]
    public async Task Roll_MissesWhenTheChanceDoesNotLand()
    {
        var reward = new GradeEnchantFailBreakReward { ItemId = 42096, Min = 8, Max = 12, Ratio = 1000 };
        var payout = GradeEnchantBreakRewardRules.Roll(reward, 150, _ => 1000, (_, _) => 10);

        await Assert.That(payout.HasValue).IsFalse();
    }

    [Test]
    public async Task Roll_PaysBaseTimesMultipleOverOneHundred()
    {
        var reward = new GradeEnchantFailBreakReward { ItemId = 42096, Min = 8, Max = 12, Ratio = 1000 };
        // chance hits (roll 0 < 1000), base 10, multiple 150 → 10 * 150 / 100 = 15
        var payout = GradeEnchantBreakRewardRules.Roll(reward, 150, _ => 0, (_, _) => 10);

        await Assert.That(payout!.Value.ItemId).IsEqualTo(42096u);
        await Assert.That(payout.Value.Count).IsEqualTo(15);
    }

    [Test]
    public async Task Roll_PaysNothingWhenTheScaledCountRoundsToZero()
    {
        var reward = new GradeEnchantFailBreakReward { ItemId = 38568, Min = 1, Max = 1, Ratio = 10000 };
        var payout = GradeEnchantBreakRewardRules.Roll(reward, 1, _ => 0, (_, _) => 1);

        await Assert.That(payout.HasValue).IsFalse();
    }

    [Test]
    public async Task Roll_SkipsGradesWithoutAMultiple()
    {
        var reward = new GradeEnchantFailBreakReward { ItemId = 42096, Min = 8, Max = 12, Ratio = 10000 };
        var payout = GradeEnchantBreakRewardRules.Roll(reward, 0, _ => 0, (_, _) => 10);

        await Assert.That(payout.HasValue).IsFalse();
    }

    [Test]
    public async Task GoesToMail_WhenTheBagCannotHoldTheStack()
    {
        await Assert.That(GradeEnchantBreakRewardRules.GoesToMail(4, 5)).IsTrue();
        await Assert.That(GradeEnchantBreakRewardRules.GoesToMail(5, 5)).IsFalse();
    }

    [Test]
    public async Task OverflowMail_UsesTheLocaleKeyShape()
    {
        await Assert.That(GradeEnchantBreakRewardRules.OverflowMailSender)
            .IsEqualTo(".itemGradeEnchantFailBreakReward");
        await Assert.That(GradeEnchantBreakRewardRules.OverflowMailTitle(1234))
            .IsEqualTo("title(1234)");
        await Assert.That(GradeEnchantBreakRewardRules.OverflowMailBody(1234, 42096, 9, 15))
            .IsEqualTo("body(1234, 42096, 9, 15)");
    }
}
