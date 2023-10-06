using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptItemEquip : QuestActTemplate
{
    public uint ItemId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Warn("QuestActConAcceptItemEquip: ItemId {0}", ItemId);
        return false;
    }
}
