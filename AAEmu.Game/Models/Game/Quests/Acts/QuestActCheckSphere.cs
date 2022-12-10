using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActCheckSphere : QuestActTemplate
    {
        public uint SphereId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActCheckSphere: SphereId {0}", SphereId);
            return false;
        }
    }
}
