using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Not used?
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActConAcceptComponent(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint QuestContextId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActConAcceptComponent: QuestContextId {QuestContextId}");
        return false;
    }

    /// <summary>
    /// Not used?
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Warn($"QuestActConAcceptComponent({DetailId}).RunAct: Quest: {quest.TemplateId}, QuestContextId {QuestContextId}");
        return base.RunAct(quest, questAct, currentObjectiveCount);
    }
}
