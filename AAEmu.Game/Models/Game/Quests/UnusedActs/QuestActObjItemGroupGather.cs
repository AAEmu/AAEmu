using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Not used
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActObjItemGroupGather(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ItemGroupId { get; set; }
    public bool Cleanup { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public bool DropWhenDestroy { get; set; }
    public bool DestroyWhenDrop { get; set; }

    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        return base.RunAct(quest, questAct, currentObjectiveCount);
    }
}
