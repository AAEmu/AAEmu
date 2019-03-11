using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjItemGroupGather : QuestActTemplate
    {
        public uint ItemGroupId { get; set; }
        public int Count { get; set; }
        public bool Cleanup { get; set; }
        public uint HighlightDoodadId { get; set; }
        public int HighlightDoodadPhase { get; set;  }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }
        public bool DropWhenDestroy { get; set; }
        public bool DestroyWhenDrop { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjItemGroupGather");
            return false;
        }
    }
}
