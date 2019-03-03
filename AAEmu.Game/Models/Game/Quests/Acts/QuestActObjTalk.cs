using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjTalk : QuestActTemplate
    {
        public uint NpcId { get; set; }
        public bool TeamShare { get; set; }
        public uint ItemId { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjTalk");
            return false;
        }
    }
}
