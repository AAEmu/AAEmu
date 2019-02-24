using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjItemGather : QuestActTemplate // Сбор предметов
    {
        public uint ItemId { get; set; }
        public int Count { get; set; }
        public uint HighlightDoodadId { get; set; }
        // TODO 1.2 // public int HighlightDoodadPhase { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }
        public bool Cleanup { get; set; }
        public bool DropWhenDestroy { get; set; }
        public bool DestroyWhenDrop { get; set; }

        public override bool Use(Unit unit, int objective)
        {
            return objective >= Count;
        }
    }
}