using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjTalk : QuestActTemplate
{
    public uint NpcId { get; set; }
    public bool TeamShare { get; set; }
    /// <summary>
    /// Not sure how ItemId is supposed to work here
    /// </summary>
    public uint ItemId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        // TODO: Implement ItemId? There seems to be only one valid Act that uses this. Quest: Feigned Formalities ( 3526 )
        Logger.Debug("QuestActObjTalk");
        if (character.CurrentInteractionObject is not Npc npc)
            return false;

        return npc.TemplateId == NpcId;
    }
}
