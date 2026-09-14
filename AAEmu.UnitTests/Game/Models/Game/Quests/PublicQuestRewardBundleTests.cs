using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class PublicQuestRewardBundleTests
{
    [Test]
    public async Task Resolver_UsesLoadedExpeditionQuestRewardActs()
    {
        var template = CreateTemplate(QuestDetail.Expedition);
        var reward = template.GetFirstComponent(QuestComponentKind.Reward);
        reward.ActTemplates.Add(new QuestActConAutoComplete(reward));
        reward.ActTemplates.Add(new QuestActSupplyItem(reward) { ItemId = 9001, Count = 2, GradeId = 3 });
        reward.ActTemplates.Add(new QuestActSupplyExpeditionExp(reward) { Point = 1600 });
        reward.ActTemplates.Add(new QuestActSupplyContributionPoint(reward) { Point = 1600 });
        reward.ActTemplates.Add(new QuestActSupplyExp(reward) { Exp = 0 });
        reward.ActTemplates.Add(new QuestActSupplyCopper(reward) { Amount = 0 });
        var quests = Mock.Of<IQuestManager>();
        quests.GetTemplate(template.Id).Returns(template);

        var resolved = PublicQuestRewardBundleResolver.TryResolve(quests.Object, template.Id, out var bundle);

        await Assert.That(resolved).IsTrue();
        await Assert.That(bundle.QuestTemplateId).IsEqualTo(template.Id);
        await Assert.That(bundle.Items).IsEquivalentTo([new PublicQuestItemReward(9001, 2, 3)]);
        await Assert.That(bundle.ExpeditionExperience).IsEqualTo(1600u);
        await Assert.That(bundle.ContributionPoints).IsEqualTo(1600u);
    }

    [Test]
    public async Task Resolver_RejectsUnsupportedNonzeroPersonalReward()
    {
        var template = CreateTemplate(QuestDetail.Expedition);
        var reward = template.GetFirstComponent(QuestComponentKind.Reward);
        reward.ActTemplates.Add(new QuestActSupplyItem(reward) { ItemId = 9001, Count = 1 });
        reward.ActTemplates.Add(new QuestActSupplyExpeditionExp(reward) { Point = 1600 });
        reward.ActTemplates.Add(new QuestActSupplyContributionPoint(reward) { Point = 1600 });
        reward.ActTemplates.Add(new QuestActSupplyExp(reward) { Exp = 1 });
        var quests = Mock.Of<IQuestManager>();
        quests.GetTemplate(template.Id).Returns(template);

        await Assert.That(PublicQuestRewardBundleResolver.TryResolve(quests.Object, template.Id, out _)).IsFalse();
    }

    [Test]
    public async Task ContributionAction_AwardsOrdinaryQuestButSkipsSharedExpeditionQuest()
    {
        var character = new Character(new UnitCustomModelParams());
        var awarded = new List<uint>();
        var ordinary = CreateTemplate(QuestDetail.Today);
        var ordinaryAction = new QuestActSupplyContributionPoint(
            ordinary.GetFirstComponent(QuestComponentKind.Reward))
        {
            Point = 10,
            AddContribution = (_, point) =>
            {
                awarded.Add(point);
                return true;
            }
        };
        var publicTemplate = CreateTemplate(QuestDetail.Expedition);
        var publicAction = new QuestActSupplyContributionPoint(
            publicTemplate.GetFirstComponent(QuestComponentKind.Reward))
        {
            Point = 1600,
            AddContribution = (_, point) =>
            {
                awarded.Add(point);
                return true;
            }
        };
        var quest = new Quest(null, character, Mock.Of<IQuestManager>().Object,
            Mock.Of<ITaskManager>().Object, Mock.Of<ISkillManager>().Object,
            Mock.Of<IExpressTextManager>().Object, Mock.Of<IWorldManager>().Object);

        ordinaryAction.RunAct(quest, null, 0);
        publicAction.RunAct(quest, null, 0);

        await Assert.That(awarded).IsEquivalentTo([10u]);
    }

    private static QuestTemplate CreateTemplate(QuestDetail detail)
    {
        var template = new QuestTemplate { Id = 7001, Name = "Assignment", DetailId = detail };
        template.Components[1] = new QuestComponentTemplate(template) { Id = 1, KindId = QuestComponentKind.Reward };
        return template;
    }
}
