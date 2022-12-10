using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAcceptBuff : QuestActTemplate
    {
        public uint BuffId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActConAcceptBuff: BuffId {0}", BuffId);
            return false;
        }
    }
}
