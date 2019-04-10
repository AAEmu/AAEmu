using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAcceptSphere : QuestActTemplate
    {
        public uint SphereId { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActConAcceptSphere: SphereId {0}", SphereId);
            return false;
        }
    }
}
