using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActSupplyItem : QuestActTemplate
    {
        public uint ItemId { get; set; }
        public int Count { get; set; }
        public byte GradeId { get; set; }
        public bool ShowActionBar { get; set; }
        public bool Cleanup { get; set; }
        public bool DropWhenDestroy { get; set; }
        public bool DestroyWhenDrop { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActSupplyItem"); // TODO add item
            return false;
        }
    }
}
