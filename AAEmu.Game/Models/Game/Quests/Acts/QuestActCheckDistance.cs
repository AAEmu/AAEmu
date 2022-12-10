using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActCheckDistance : QuestActTemplate
    {
        public bool WithIn { get; set; }
        public uint NpcId { get; set; }
        public int Distance { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActCheckDistance: WithIn {0}, NpcId {1}, Distance {2}", WithIn, NpcId, Distance);
            return false;
        }
    }
}
