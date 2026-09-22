using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestRewardSupportRulesTests
{
    // Quest 9334, Reward component 40675: quest_act_supply_resident_points 1 (zone group 33, 10),
    // quest_act_supply_resident_charges 1 (33, 10000), quest_act_supply_coppers 3721 and
    // quest_act_supply_appellations 390.
    [Test]
    public async Task Quest9334_ResidentPointIsTheFirstUnsupportedReward()
    {
        var template = Quest(9334, 40675, reward =>
        {
            reward.ActTemplates.Add(new QuestActSupplyResidentPoint(reward) { DetailId = 1, ZoneGroupId = 33, Point = 10 });
            reward.ActTemplates.Add(new QuestActSupplyResidentCharge(reward) { DetailId = 1, ZoneGroupId = 33, Charge = 10000 });
            reward.ActTemplates.Add(new QuestActSupplyCopper(reward) { DetailId = 3721, Amount = 99999999 });
            reward.ActTemplates.Add(new QuestActSupplyAppellation(reward) { DetailId = 390 });
        });

        var unsupported = QuestRewardSupportRules.FirstUnsupported(template);
        await Assert.That(unsupported is QuestActSupplyResidentPoint).IsTrue();
        await Assert.That(unsupported.MissingSubsystem).IsEqualTo("resident points");
        await Assert.That(QuestRewardSupportRules.RefusesAccept(template, false)).IsTrue();
    }

    [Test]
    public async Task Quest9334_GmAddIsNotRefused()
    {
        var template = Quest(9334, 40675, reward =>
            reward.ActTemplates.Add(new QuestActSupplyResidentPoint(reward) { DetailId = 1, ZoneGroupId = 33, Point = 10 }));

        await Assert.That(QuestRewardSupportRules.RefusesAccept(template, true)).IsFalse();
    }

    // Quest 7823, Reward component with quest_act_supply_items 6612 only.
    [Test]
    public async Task Quest7823_SupportedRewardsAreNotRefused()
    {
        var template = Quest(7823, 33493, reward =>
            reward.ActTemplates.Add(new QuestActSupplyItem(reward) { DetailId = 6612, ItemId = 1, Count = 1 }));

        await Assert.That(QuestRewardSupportRules.FirstUnsupported(template)).IsNull();
        await Assert.That(QuestRewardSupportRules.RefusesAccept(template, false)).IsFalse();
    }

    // Quest 9902 (exile, category 189): quest_act_supply_faction_changes 13 (161, ignore_limit t);
    // quest 6572: quest_act_supply_ranked_items 1 (rank 1, 43779 x8); quest 11132:
    // quest_act_supply_result_ranked_items 9 (win, rank 1, 51578 x15).
    [Test]
    public async Task FactionChangeAndRankedItems_AreUnsupported()
    {
        var exile = Quest(9902, 43021, reward =>
        {
            reward.ActTemplates.Add(new QuestActConAutoComplete(reward) { DetailId = 3711 });
            reward.ActTemplates.Add(new QuestActSupplyFactionChange(reward) { DetailId = 13, SystemFactionId = 161, IgnoreLimit = true });
        });
        var war = Quest(6572, 28062, reward =>
            reward.ActTemplates.Add(new QuestActSupplyRankedItem(reward) { DetailId = 1, Rank = 1, ItemId = 43779, Count = 8 }));
        var competition = Quest(11132, 48507, reward =>
            reward.ActTemplates.Add(new QuestActSupplyResultRankedItem(reward) { DetailId = 9, Result = true, Rank = 1, ItemId = 51578, Count = 15 }));

        await Assert.That(QuestRewardSupportRules.FirstUnsupported(exile) is QuestActSupplyFactionChange).IsTrue();
        await Assert.That(QuestRewardSupportRules.FirstUnsupported(war) is QuestActSupplyRankedItem).IsTrue();
        await Assert.That(QuestRewardSupportRules.FirstUnsupported(competition) is QuestActSupplyResultRankedItem).IsTrue();
    }

    [Test]
    public async Task NoTemplate_IsNotRefused()
    {
        await Assert.That(QuestRewardSupportRules.FirstUnsupported(null)).IsNull();
        await Assert.That(QuestRewardSupportRules.RefusesAccept(null, false)).IsFalse();
    }

    private static QuestTemplate Quest(uint questId, uint rewardComponentId, Action<QuestComponentTemplate> fill)
    {
        var template = new QuestTemplate { Id = questId };
        var reward = new QuestComponentTemplate(template) { Id = rewardComponentId, KindId = QuestComponentKind.Reward };
        fill(reward);
        template.Components[reward.Id] = reward;
        return template;
    }
}
