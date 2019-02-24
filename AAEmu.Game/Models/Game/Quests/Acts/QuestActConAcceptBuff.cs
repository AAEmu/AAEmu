using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAcceptBuff : QuestActTemplate
    {
        public uint BuffId { get; set; }

        public override bool Use(Unit unit, int objective)
        {
            return false;
        }
    }
}