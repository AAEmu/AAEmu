using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// There is exactly one entry for this Act, but the component it is attached to does not have a valid quest
/// </summary>
public class QuestActObjZoneQuestComplete(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ZoneId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        return base.RunAct(quest, questAct, currentObjectiveCount);
    }
}
