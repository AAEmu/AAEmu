using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Progress act QuestActObjCondition: wait for another quest (quest_context_id) to reach condition_id,
/// an enum_quest_condition_objs value (1 complete, 2 fail, 3 ready, 4 progress). One enabled row:
/// test quest 6774 waits for quest 6621 to fail. Complete is the completed-quests flag; the other
/// three are the referenced quest's current step.
/// </summary>
public static class QuestConditionActRules
{
    public static bool MetByStep(QuestConditionObj condition, QuestComponentKind step)
        => condition switch
        {
            QuestConditionObj.Fail => step == QuestComponentKind.Fail,
            QuestConditionObj.Ready => step == QuestComponentKind.Ready,
            QuestConditionObj.Progress => step == QuestComponentKind.Progress,
            _ => false
        };

    public static bool MetByCompletion(QuestConditionObj condition, bool completed)
        => condition == QuestConditionObj.Complete && completed;
}
