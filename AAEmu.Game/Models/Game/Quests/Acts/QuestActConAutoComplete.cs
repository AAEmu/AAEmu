using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAutoComplete(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActConAutoComplete");

        return character.Quests.IsQuestComplete(ParentQuestTemplate.Id);
    }

    /// <summary>
    /// Used for auto-complete conditions, always returns true
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActConAutoComplete({DetailId}).RunAct: Quest: {quest.TemplateId}");
        return true;
    }
}
