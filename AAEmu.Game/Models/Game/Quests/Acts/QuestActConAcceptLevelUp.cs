using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptLevelUp(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public byte Level { get; set; }

    /// <summary>
    /// Checks if the player level is at least Level
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Level {Level}");
        return quest.Owner.Level >= Level;
    }
}
