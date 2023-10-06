using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptItemGain : QuestActTemplate
{
    public uint ItemId { get; set; }
    public int Count { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Warn("QuestActConAcceptItemGain: ItemId {0}, Count {1}", ItemId, Count);
        return objective >= Count;
    }
}
