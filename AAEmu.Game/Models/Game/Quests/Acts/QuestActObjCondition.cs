using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Progress: wait for quest quest_context_id to reach condition_id (QuestConditionActRules).
/// quest_act_obj_conditions has no count column, so the objective is met once.
/// </summary>
public class QuestActObjCondition(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public override int Count => 1;
    public QuestConditionObj ConditionId { get; set; }
    public uint QuestContextId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug(
            "{0}({1}).RunAct: Quest {2}, Owner {3}, waits for quest {4} to be {5}, {6}/{7}",
            QuestActTemplateName, DetailId, quest.TemplateId, quest.Owner.Name, QuestContextId, ConditionId,
            currentObjectiveCount, Count);
        return currentObjectiveCount >= Count;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnQuestStepChanged += questAct.OnQuestStepChanged;
        quest.Owner.Events.OnQuestComplete += questAct.OnQuestComplete;
        // The referenced quest may already be there when this step opens.
        var quests = quest.Owner.Quests;
        if (quests == null)
            return;
        if (QuestConditionActRules.MetByCompletion(ConditionId, quests.IsQuestComplete(QuestContextId)) ||
            (quests.ActiveQuests.TryGetValue(QuestContextId, out var other) &&
             QuestConditionActRules.MetByStep(ConditionId, other.Step)))
            SetObjective(quest, 1);
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnQuestStepChanged -= questAct.OnQuestStepChanged;
        quest.Owner.Events.OnQuestComplete -= questAct.OnQuestComplete;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnQuestStepChanged(QuestAct questAct, object sender, OnQuestStepChangedArgs args)
    {
        if (questAct.Template.ActId != ActId || args.QuestId != QuestContextId)
            return;
        if (QuestConditionActRules.MetByStep(ConditionId, args.Step))
            SetObjective(questAct, 1);
    }

    public override void OnQuestComplete(QuestAct questAct, object sender, OnQuestCompleteArgs args)
    {
        if (questAct.Template.ActId != ActId || args.QuestId != QuestContextId)
            return;
        if (QuestConditionActRules.MetByCompletion(ConditionId, true))
            SetObjective(questAct, 1);
    }
}
