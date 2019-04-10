using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActSupplyLp : QuestActTemplate
    {
        public int LaborPower { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Debug("QuestActSupplyLp");
            
            character.ChangeLabor((short)LaborPower, 0);
            return true;
        }
    }
}
