using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using NLog;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConReportJournal(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective) // take reward
    {
        Logger.Debug("QuestActConReportJournal");

        return true;
    }

    /// <summary>
    /// Checks if the quest can be auto-completed, always returns true
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"QuestActConReportJournal({DetailId}).RunAct: Quest: {quest.TemplateId}");
        return true;
    }
}
