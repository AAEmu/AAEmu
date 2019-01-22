using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Templates
{
    public abstract class QuestActTemplate
    {
        public uint Id { get; set; }

        public abstract bool Use(Unit unit, int objective);
    }
}