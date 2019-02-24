using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjDistance : QuestActTemplate
    {
        public bool WithIn { get; set; }
        public uint NpcId { get; set; }
        public int Distance { get; set; }
        public uint HighlightDoodadId { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }

        public override bool Use(Unit unit, int objective)
        {
            return false;
        }
    }
}