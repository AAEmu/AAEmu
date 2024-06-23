using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConReportDoodad(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint DoodadId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    /// <summary>
    /// Checks if quest was turned in at the specified Doodad
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns>False</returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), DoodadId {DoodadId}");
        return questAct.OverrideObjectiveCompleted;
    }

    public override void InitializeQuest(Quest quest, IQuestAct questAct)
    {
        base.InitializeQuest(quest, questAct);
        quest.Owner.Events.OnReportDoodad += questAct.OnReportDoodad;
    }

    public override void FinalizeQuest(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnReportDoodad += questAct.OnReportDoodad;
        base.FinalizeQuest(quest, questAct);
    }

    public override void OnReportDoodad(IQuestAct questAct, object sender, OnReportDoodadArgs args)
    {
        if ((questAct.Id != ActId) || (args.DoodadId != DoodadId))
            return;

        // This check is needed so that turning in a quest at a Doodad doesn't complete all active quests that
        // need to be turned in at the same Doodad
        var minimumProgress = questAct.Template.ParentComponent.ParentQuestTemplate.LetItDone
            ? QuestObjectiveStatus.CanEarlyComplete
            : QuestObjectiveStatus.QuestComplete; 
        var isReady = questAct.QuestComponent.Parent.Parent.GetQuestObjectiveStatus() >= minimumProgress;
        // TODO: Check doodad range?
        
        if (!isReady)
            return;

        questAct.OverrideObjectiveCompleted = true;
        if (questAct.QuestComponent.Parent.Parent.Step == QuestComponentKind.Progress)
            questAct.QuestComponent.Parent.Parent.Step = QuestComponentKind.Ready;
        questAct.RequestEvaluation(); // Manual request since this does not use objective counters to trigger
    }
}
