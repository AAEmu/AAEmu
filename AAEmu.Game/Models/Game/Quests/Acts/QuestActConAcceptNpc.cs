using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAcceptNpc : QuestActTemplate
    {
        public uint NpcId { get; set; }

        public override bool Use(Unit unit, int objective)
        {
            if (!(unit.CurrentTarget is Npc))
                return false;
            return ((Npc) unit.CurrentTarget).TemplateId == NpcId;
        }
    }
}