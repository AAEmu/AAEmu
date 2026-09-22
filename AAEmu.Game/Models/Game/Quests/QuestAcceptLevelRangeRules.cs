using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// quest_act_con_accept_level_ranges (10 rows, all Start): the character level must sit inside
/// level_min..level_max, both inclusive. The client reader LoadQuestActConAcceptLevelRangeDescs
/// (x2game-dev.dll FUN_39d40f70) reads id, level_max, level_min. Row 2 (quest 10930) is 10..19;
/// rows 16 to 24 (quests 10534 to 10542) run 10..125 up to 54..125 while their quest_contexts
/// min_level and max_level are 0, so this act is the only level gate those quests have.
/// </summary>
public static class QuestAcceptLevelRangeRules
{
    public static bool InRange(int level, int levelMin, int levelMax)
        => level >= levelMin && level <= levelMax;

    /// <summary>
    /// A Start component answers on the OR of its acts, so its level range decides the accept only
    /// where nothing else in that component can: the nine anniversary quests 10534 to 10542 pair
    /// theirs with a QuestActConAcceptComponent that starts the quest anyway. Quest 10930 (10..19)
    /// is the only range that stands alone in its component, and an out-of-range accept there used
    /// to be added to the journal and then dropped with the ignored RunCurrentStep result, leaving
    /// the quest stuck at Start.
    /// </summary>
    public static bool RefusesAccept(IQuestTemplate template, int level)
    {
        if (template == null)
            return false;

        foreach (var component in template.GetComponents(QuestComponentKind.Start))
        {
            if (component?.ActTemplates == null || component.ActTemplates.Count == 0)
                continue;

            var onlyRanges = true;
            var inRange = false;
            foreach (var act in component.ActTemplates)
            {
                if (act is not QuestActConAcceptLevelRange range)
                {
                    onlyRanges = false;
                    break;
                }

                inRange |= InRange(level, range.LevelMin, range.LevelMax);
            }

            if (onlyRanges && !inRange)
                return true;
        }

        return false;
    }
}
