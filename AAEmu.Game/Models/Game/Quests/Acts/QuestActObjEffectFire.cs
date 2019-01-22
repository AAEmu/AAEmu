using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjEffectFire : QuestActTemplate
    {
        public uint EffectId { get; set; }
        public int Count { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }
        
        public override bool Use(Unit unit, int objective)
        {
            return false;
        }
    }
}