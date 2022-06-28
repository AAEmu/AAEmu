using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Spheres
{
    public class SphereQuests
    {
        public uint Id { get; set; }
        public uint QuestId { get; set; }
        public QuestTrigger QuestTriggerId { get; set; }
    }
}
