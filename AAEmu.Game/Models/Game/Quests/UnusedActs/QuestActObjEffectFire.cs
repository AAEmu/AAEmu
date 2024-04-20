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

    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        return base.RunAct(quest, questAct, currentObjectiveCount);
    }
}
