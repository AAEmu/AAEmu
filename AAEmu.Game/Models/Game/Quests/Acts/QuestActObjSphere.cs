using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjSphere : QuestActTemplate
    {
        public uint SphereId { get; set; }
        public uint NpcId { get; set; }
        public uint HighlightDoodadId { get; set; }
        // TODO 1.2 // public int HighlightDoodadPhase { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }
        
        public override bool Use(Unit unit, int objective)
        {
            return false;
        }
    }
}