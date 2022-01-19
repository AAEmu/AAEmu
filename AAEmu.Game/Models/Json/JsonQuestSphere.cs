namespace AAEmu.Game.Models.Json
{

    public class SpherePos
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    public class JsonQuestSphere
    {
        public uint Id { get; set; }
        public uint QuestId { get; set; }
        public uint SphereId { get; set; }
        public float Radius { get; set; }
        public SpherePos Position { get; set; }
    }
}
