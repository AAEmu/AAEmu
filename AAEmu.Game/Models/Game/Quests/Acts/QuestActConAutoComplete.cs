using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAutoComplete : QuestActTemplate
    {
        public override bool Use(Unit unit, int objective)
        {
            return false;
        }
    }
}