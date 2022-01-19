using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjItemGroupUse : QuestActTemplate
    {
        public uint ItemGroupId { get; set; }
        public int Count { get; set; }
        public uint HighlightDoodadId { get; set; }
        public int HighlightDoodadPhase { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }
        public bool DropWhenDestroy { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjItemGroupUse");
            return quest.Template.Score > 0 ? objective * Count >= quest.Template.Score : objective >= Count;
        }
    }
}
