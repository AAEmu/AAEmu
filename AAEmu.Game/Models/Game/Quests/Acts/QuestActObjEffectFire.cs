using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// This Act does not seem to be used
/// </summary>
public class QuestActObjEffectFire : QuestActTemplate
{
    public uint EffectId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug("QuestActObjEffectFire");
        return ParentQuestTemplate.Score > 0 ? objective * Count >= ParentQuestTemplate.Score : objective >= Count;
    }
}
