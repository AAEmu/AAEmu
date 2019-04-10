using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActSupplyRemoveItem : QuestActTemplate
    {
        public uint ItemId { get; set; }
        public int Count { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActSupplyRemoveItem");
            return false;
        }
    }
}
