using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjMateLevel : QuestActTemplate
    {
        public uint ItemId { get; set; }
        public byte Level { get; set; }
        public bool Cleanup { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }
        
        public override bool Use(Unit unit, int objective)
        {
            return false;
        }
    }
}