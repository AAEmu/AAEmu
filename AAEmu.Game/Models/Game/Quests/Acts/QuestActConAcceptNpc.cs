using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAcceptNpc : QuestActTemplate
    {
        public uint NpcId { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Debug("QuestActConAcceptNpc");
            
            if (!(character.CurrentTarget is Npc))
                return false;

            quest.QuestAcceptorType = QuestAcceptorType.Npc;
            quest.AcceptorType = NpcId;

            return ((Npc) character.CurrentTarget).TemplateId == NpcId;
        }
    }
}
