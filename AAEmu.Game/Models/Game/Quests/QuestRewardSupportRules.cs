using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// A quest whose Reward step holds an act the server cannot grant (IUnsupportedRewardAct) is
/// refused at accept. Holding it at Reward instead is not safe: QuestStep.RunComponents re-runs
/// every sibling Supply act and DistributeRewards on each re-evaluation of a step that returned
/// false, and a second talk to the report NPC requests one, so exp, copper and items would be
/// granted again each time. Completing it would hand out the quest without its grant. GM adds
/// (forcibly) skip the refusal.
/// </summary>
public static class QuestRewardSupportRules
{
    public static IUnsupportedRewardAct FirstUnsupported(IQuestTemplate template)
    {
        if (template == null)
            return null;

        foreach (var component in template.GetComponents(QuestComponentKind.Reward))
        {
            if (component?.ActTemplates == null)
                continue;

            foreach (var act in component.ActTemplates)
            {
                if (act is IUnsupportedRewardAct unsupported)
                    return unsupported;
            }
        }

        return null;
    }

    public static bool RefusesAccept(IQuestTemplate template, bool forcibly)
        => !forcibly && FirstUnsupported(template) != null;
}
