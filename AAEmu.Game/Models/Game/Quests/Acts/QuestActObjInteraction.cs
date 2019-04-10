using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjInteraction : QuestActTemplate
    {
        public WorldInteractionType WorldInteractionId { get; set; }
        public int Count { get; set; }
        public uint DoodadId { get; set; }
        public bool UseAlias { get; set; }
        public bool TeamShare { get; set; }
        public uint HighlightDoodadId { get; set; }
        public int HighlightDoodadPhase { get; set; }
        public uint QuestActObjAliasId { get; set; }
        public uint Phase { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjInteraction");
            return false;
        }
    }
}
