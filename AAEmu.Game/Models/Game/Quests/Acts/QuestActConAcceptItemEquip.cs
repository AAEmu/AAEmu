using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAcceptItemEquip : QuestActTemplate
    {
        public uint ItemId { get; set; }

        public override bool Use(Unit unit, int objective)
        {
            return false;
        }
    }
}