using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// This Act does not seem to be used anymore
/// </summary>
public class QuestActObjEffectFire(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint EffectId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActObjEffectFire");
        return ParentQuestTemplate.Score > 0 ? objective * Count >= ParentQuestTemplate.Score : objective >= Count;
    }
}
