using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjAggro : QuestActTemplate
    {
        public int Range { get; set; }
        public int Rank1 { get; set; }
        public int Rank2 { get; set; }
        public int Rank3 { get; set; }
        public int Rank1Ratio { get; set; }
        public int Rank2Ratio { get; set; }
        public int Rank3Ratio { get; set; }
        public bool Rank1Item { get; set; }
        public bool Rank2Item { get; set; }
        public bool Rank3Item { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjAggro");
            return false;
        }
    }
}
