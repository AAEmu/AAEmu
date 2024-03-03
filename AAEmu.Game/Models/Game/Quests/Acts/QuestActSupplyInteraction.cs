using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplyInteraction : QuestActTemplate
{
    public WorldInteractionType WorldInteractionId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActSupplyInteraction: WorldInteractionId {WorldInteractionId}");
        return true;
    }
}
