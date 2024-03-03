using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// There is exactly one entry for this Act, but the component it is attached to does not have a valid quest
/// </summary>
public class QuestActObjZoneQuestComplete : QuestActTemplate
{
    public uint ZoneId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug("QuestActObjZoneQuestComplete");
        return ParentQuestTemplate.Score > 0 ? objective * Count >= ParentQuestTemplate.Score : objective >= Count;
    }
}
