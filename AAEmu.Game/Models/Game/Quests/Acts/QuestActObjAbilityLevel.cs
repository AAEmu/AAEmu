using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjAbilityLevel : QuestActTemplate
    {
        public byte AbilityId { get; set; }
        public byte Level { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjAbilityLevel");
            return false;
        }
    }
}
