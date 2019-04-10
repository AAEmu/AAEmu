using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActSupplySelectiveItem : QuestActTemplate
    {
        public uint ItemId { get; set; }
        public int Count { get; set; }
        public byte GradeId { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActSupplySelectiveItem");
            return false;
        }
    }
}
