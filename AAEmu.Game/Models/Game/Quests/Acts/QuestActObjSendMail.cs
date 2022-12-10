using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjSendMail : QuestActTemplate
    {
        public uint ItemId1 { get; set; }
        public int Count1 { get; set; }
        public uint ItemId2 { get; set; }
        public int Count2 { get; set; }
        public uint ItemId3 { get; set; }
        public int Count3 { get; set; }

        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjSendMail");
            return false;
        }
    }
}
