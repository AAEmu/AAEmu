using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// No longer used?
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActObjCondition(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ConditionId { get; set; }
    public uint QuestContextId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        return base.RunAct(quest, questAct, currentObjectiveCount);
    }
}
