using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActCheckCompleteComponent : QuestActTemplate
    {
        public uint CompleteComponent { get; set; }
        
        public override bool Use(Unit unit, int objective)
        {
            return false;
        }
    }
}