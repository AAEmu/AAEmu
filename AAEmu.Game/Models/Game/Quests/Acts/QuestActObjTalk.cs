using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjTalk : QuestActTemplate
    {
        public uint NpcId { get; set; }
        public bool TeamShare { get; set; }
        public uint ItemId { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjTalk");
            if(!(character.CurrentTarget is Npc npc))
                return false;

            return npc.TemplateId == NpcId;
        }
    }
}
