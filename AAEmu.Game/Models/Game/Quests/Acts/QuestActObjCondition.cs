using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjCondition(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ConditionId { get; set; }
    public uint QuestContextId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActObjCondition");
        return false;
    }

    /// <summary>
    /// No longer used?
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Error($"QuestActObjCondition({DetailId}).RunAct: Quest: {quest.TemplateId}, ConditionId {ConditionId}");
        return false;
    }
}
