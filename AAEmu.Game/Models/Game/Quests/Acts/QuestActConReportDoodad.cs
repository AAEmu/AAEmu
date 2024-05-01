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
        return false;
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

        // TODO: Check doodad range?

        questAct.OverrideObjectiveCompleted = true;
        questAct.RequestEvaluation(); // Manual request since this does not use objective counters to trigger
    }
}
