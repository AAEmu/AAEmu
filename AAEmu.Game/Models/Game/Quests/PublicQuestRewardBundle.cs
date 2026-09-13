using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests;

public sealed record PublicQuestItemReward(uint TemplateId, int Count, byte GradeId);

public sealed record PublicQuestRewardBundle(
    uint QuestTemplateId,
    string QuestName,
    IReadOnlyList<PublicQuestItemReward> Items,
    uint ExpeditionExperience,
    uint ContributionPoints);

/// <summary>Builds the shared reward bundle from the loaded quest content.</summary>
public static class PublicQuestRewardBundleResolver
{
    public static bool TryResolve(IQuestManager questManager, uint questTemplateId,
        out PublicQuestRewardBundle bundle)
    {
        bundle = null;
        var template = questManager?.GetTemplate(questTemplateId);
        if (template?.DetailId != QuestDetail.Expedition)
            return false;

        var items = new List<PublicQuestItemReward>();
        uint? expeditionExperience = null;
        uint? contributionPoints = null;
        foreach (var component in template.GetComponents(QuestComponentKind.Reward))
        foreach (var act in component.ActTemplates)
        {
            switch (act)
            {
                case QuestActConAutoComplete:
                    break;
                case QuestActSupplyItem item when item.ItemId > 0 && item.Count > 0:
                    items.Add(new PublicQuestItemReward(item.ItemId, item.Count, item.GradeId));
                    break;
                case QuestActSupplyExpeditionExp expedition when expeditionExperience == null:
                    expeditionExperience = expedition.Point;
                    break;
                case QuestActSupplyContributionPoint contribution when contributionPoints == null:
                    contributionPoints = contribution.Point;
                    break;
                case QuestActSupplyExp { Exp: 0 }:
                case QuestActSupplyCopper { Amount: 0 }:
                    break;
                default:
                    return false;
            }
        }

        if (items.Count == 0 || expeditionExperience is not > 0 || contributionPoints is not > 0)
            return false;

        bundle = new PublicQuestRewardBundle(
            template.Id,
            template.Name,
            items.AsReadOnly(),
            expeditionExperience.Value,
            contributionPoints.Value);
        return true;
    }
}
