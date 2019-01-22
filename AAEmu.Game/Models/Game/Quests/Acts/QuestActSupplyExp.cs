using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActSupplyExp : QuestActTemplate
    {
        public int Exp { get; set; }

        public override bool Use(Unit unit, int objective)
        {
            ((Character) unit).AddExp(Exp, true);
            return true;
        }
    }
}