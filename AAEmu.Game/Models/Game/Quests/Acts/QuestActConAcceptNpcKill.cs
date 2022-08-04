using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAcceptNpcKill : QuestActTemplate
    {
        public uint NpcId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActConAcceptNpcKill: NpcId {0}", NpcId);

            if (!(character.CurrentTarget is Npc))
                return false;

            quest.QuestAcceptorType = QuestAcceptorType.Npc;
            quest.AcceptorType = NpcId;

            return ((Npc)character.CurrentTarget).TemplateId == NpcId;
        }
    }
}
