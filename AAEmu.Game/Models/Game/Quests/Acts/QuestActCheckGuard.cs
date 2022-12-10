using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActCheckGuard : QuestActTemplate
    {
        public uint NpcId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActCheckGuard: NpcId {0}", NpcId);
            return false;
        }
    }
}
