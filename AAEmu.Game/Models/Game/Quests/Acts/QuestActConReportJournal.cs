using AAEmu.Game.Models.Game.Quests.Templates;

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
    /// <returns>True</returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id})");
        return true;
    }
}
