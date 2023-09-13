using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjDoodadPhaseCheck : QuestActTemplate
    {
        public uint DoodadId { get; set; }
        public uint Phase1 { get; set; }
        public uint Phase2 { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjDoodadPhaseCheck");
            return false;
        }
    }
}
