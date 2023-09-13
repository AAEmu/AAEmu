using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAcceptDoodad : QuestActTemplate
    {
        public uint DoodadId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActConAcceptDoodad: DoodadId {0}", DoodadId);

            quest.QuestAcceptorType = QuestAcceptorType.Doodad;
            quest.AcceptorType = DoodadId;

            return true;
        }
    }
}
