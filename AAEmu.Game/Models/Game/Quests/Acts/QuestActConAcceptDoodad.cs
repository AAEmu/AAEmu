using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAcceptDoodad : QuestActTemplate
    {
        public uint DoodadId { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActConAcceptDoodad: DoodadId {0}", DoodadId);
            return true;
        }
    }
}
