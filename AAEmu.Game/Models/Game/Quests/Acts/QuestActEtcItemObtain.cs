using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActEtcItemObtain : QuestActTemplate
    {
        public uint ItemId { get; set; }
        public int Count { get; set; }
        public uint HighlightDooadId { get; set; }
        public bool Cleanup { get; set; }

        public override bool Use(Unit unit, int objective)
        {
            return false;
        }
    }
}