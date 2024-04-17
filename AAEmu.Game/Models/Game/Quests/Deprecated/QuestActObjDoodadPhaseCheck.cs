using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// No longer used? Assumed to check if nearby Doodad has a specific Phase?
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActObjDoodadPhaseCheck(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint DoodadId { get; set; }
    public uint Phase1 { get; set; }
    public uint Phase2 { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        return base.RunAct(quest, questAct, currentObjectiveCount);
    }
}
