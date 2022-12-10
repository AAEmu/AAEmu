using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

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
        public int LvlMinNpc { get; set; }
        public int LvlMaxNpc { get; set; }
        public uint PcFactionId { get; set; }
        public bool PcFactionExclusive { get; set; }
        public uint NpcFactionId { get; set; }
        public bool NpcFactionExclusive { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjZoneKill");
            return objective >= CountNpc || objective >= CountPlayerKill;
        }
    }
}
