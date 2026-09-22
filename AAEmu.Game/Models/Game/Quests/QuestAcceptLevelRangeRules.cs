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
}
