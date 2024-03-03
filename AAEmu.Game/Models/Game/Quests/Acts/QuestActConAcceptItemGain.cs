using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptItemGain : QuestActTemplate
{
    public uint ItemId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug($"QuestActConAcceptItemGain: ItemId {ItemId}, Count {Count}");
        return objective >= Count;
    }
}
