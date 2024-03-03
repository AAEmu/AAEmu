using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjZoneMonsterHunt : QuestActTemplate
{
    public uint ZoneId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug("QuestActObjZoneMonsterHunt");

        if (character.Transform.ZoneId != ZoneId)
            return false;

        return ParentQuestTemplate.Score > 0 ? objective * Count >= ParentQuestTemplate.Score : objective >= Count;
    }
}
