namespace AAEmu.Game.Models.Json
{
    public class JsonQuestSphere
    {
        public uint Id { get; set; }
        public uint QuestId { get; set; }
        public uint SphereId { get; set; }
        public float Radius { get; set; }
        public JsonPosition Position { get; set; }
    }
}
