using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjDoodadPhaseCheck : QuestActTemplate
    {
        public uint DoodadId { get; set; }
        public uint Phase1 { get; set; }
        public uint Phase2 { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }

        public override bool Use(Unit unit, int objective)
        {
            return false;
        }
    }
}