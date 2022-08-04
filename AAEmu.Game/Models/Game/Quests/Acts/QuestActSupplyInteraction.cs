using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActSupplyInteraction : QuestActTemplate
    {
        public WorldInteractionType WorldInteractionId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActSupplyInteraction: WorldInteractionId {0}", WorldInteractionId);
            return false;
        }
    }
}
