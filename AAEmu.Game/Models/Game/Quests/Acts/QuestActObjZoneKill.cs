using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjZoneKill : QuestActTemplate
    {
        public int CountPlayerKill { get; set; }
        public int CountNpc { get; set; }
        public uint ZoneId { get; set; }
        public bool TeamShare { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }
        public int LvlMin { get; set; }
        public int LvlMax { get; set; }
        public bool IsParty { get; set; }

        // TODO 1.2 // public int LvlMinNpc { get; set; }
        // TODO 1.2 // public int LvlMaxNpc { get; set; }
        // TODO 1.2 // public uint PcFactionId { get; set; }
        // TODO 1.2 // public bool PcFactionExclusive { get; set; }
        // TODO 1.2 // public uint NpcFactionId { get; set; }
        // TODO 1.2 // public bool NpcFactionExclusive { get; set; }

        public override bool Use(Unit unit, int objective)
        {
            return false;
        }
    }
}