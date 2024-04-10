using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using NLog;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptLevelUp(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public byte Level { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActConAcceptLevelUp");

        return character.Level >= Level;
    }

    /// <summary>
    /// Checks if the player level is at least Level
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"QuestActConAcceptLevelUp({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Level {Level}");
        return quest.Owner.Level >= Level;
    }
}
