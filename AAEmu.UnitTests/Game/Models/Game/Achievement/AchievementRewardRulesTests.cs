using AAEmu.Game.Models.Game.Achievement;

namespace AAEmu.UnitTests.Game.Models.Game.Achievement;

/// <summary>
/// What an achievement is worth and where its item goes — the part of the reward that is a decision rather
/// than a delivery.
/// </summary>
public class AchievementRewardRulesTests
{
    [Test]
    public async Task Reward_CarriesTheContentsItemAndTitle()
    {
        var reward = AchievementRewardRules.RewardOf(new Achievements
        {
            Id = 9,
            ItemId = 23633,
            ItemNum = 5,
            AppellationId = 120
        });

        await Assert.That(reward.ItemId).IsEqualTo(23633u);
        await Assert.That(reward.ItemCount).IsEqualTo(5);
        await Assert.That(reward.AppellationId).IsEqualTo(120u);
        await Assert.That(reward.HasItem).IsTrue();
        await Assert.That(reward.HasAppellation).IsTrue();
    }

    [Test]
    public async Task Reward_WithoutAnItemOrTitle_PaysNothing()
    {
        var reward = AchievementRewardRules.RewardOf(new Achievements { Id = 1 });

        await Assert.That(reward.HasItem).IsFalse();
        await Assert.That(reward.HasAppellation).IsFalse();
        await Assert.That(AchievementRewardRules.RewardOf(null).HasItem).IsFalse();
    }

    [Test]
    public async Task Reward_WithAnItemIdButNoCount_IsNotAnItemReward()
    {
        // The two columns are read separately, and half of a reward is not one.
        var reward = AchievementRewardRules.RewardOf(new Achievements { ItemId = 23633, ItemNum = 0 });

        await Assert.That(reward.HasItem).IsFalse();
    }

    [Test]
    public async Task Item_GoesToTheBagWhileTheWholeStackFits()
    {
        await Assert.That(AchievementRewardRules.GoesToMail(freeSpaceForItem: 5, itemCount: 5)).IsFalse();
        await Assert.That(AchievementRewardRules.GoesToMail(freeSpaceForItem: 50, itemCount: 5)).IsFalse();
    }

    [Test]
    public async Task Item_IsMailedWhenTheBagCannotHoldAllOfIt()
    {
        await Assert.That(AchievementRewardRules.GoesToMail(freeSpaceForItem: 4, itemCount: 5)).IsTrue();
        await Assert.That(AchievementRewardRules.GoesToMail(freeSpaceForItem: 0, itemCount: 1)).IsTrue();
    }

    [Test]
    public async Task OverflowMail_UsesTheClientLocaleCalls()
    {
        await Assert.That(AchievementRewardRules.OverflowMailSender).IsEqualTo(".achievementNew");
        await Assert.That(AchievementRewardRules.OverflowMailTitle(3950)).IsEqualTo("title(3950)");
        await Assert.That(AchievementRewardRules.OverflowMailBody(3950)).IsEqualTo("body(3950)");
    }
}
