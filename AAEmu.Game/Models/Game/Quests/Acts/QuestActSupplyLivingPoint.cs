using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActSupplyLivingPoint : QuestActTemplate
    {
        public int Point { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActSupplyLivingPoint");
            character.ChangeGamePoints(GamePointKind.Vocation, Point);
            return true;
        }
    }
}
