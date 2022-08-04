using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActSupplyHonorPoint : QuestActTemplate
    {
        public int Point { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActSupplyHonorPoint");
            character.ChangeGamePoints(GamePointKind.Honor, Point);
            return true;
        }
    }
}
