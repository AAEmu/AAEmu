using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActCheckCompleteComponent(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint CompleteComponent { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActCheckCompleteComponent: Complete Component {CompleteComponent}");
        return true;
    }

    /// <summary>
    /// This looks like a Legacy Act that is no longer used. Also got no idea of how it would have worked.
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActCheckCompleteComponent({DetailId}).RunAct: Quest {quest.TemplateId}, Complete Component {CompleteComponent}");
        return true;
    }
}
