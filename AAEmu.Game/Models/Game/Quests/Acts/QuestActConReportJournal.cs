using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConReportJournal(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    /// <summary>
    /// Checks if the quest can be auto-completed, differs in some way from QuestActConAutoComplete
    /// Seems to be used mostly on TaskBoard style group kill quests
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns>False</returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id})");
        return questAct.OverrideObjectiveCompleted || true;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeQuest(quest, questAct);
        quest.Owner.Events.OnReportJournal += questAct.OnReportJournal;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnReportJournal -= questAct.OnReportJournal;
        base.FinalizeQuest(quest, questAct);
    }

    public override void OnReportJournal(QuestAct questAct, object sender, OnReportJournalArgs args)
    {
        if (questAct.Id != ActId)
            return;

        questAct.OverrideObjectiveCompleted = true;
        if (questAct.QuestComponent.Parent.Parent.Step == QuestComponentKind.Progress)
            questAct.QuestComponent.Parent.Parent.Step = QuestComponentKind.Ready;
        questAct.RequestEvaluation(); // Manual request since this does not use objective counters to trigger
    }
}
