namespace AAEmu.Game.Models.Json
{
    public class JsonNpcSpawns
    {
        public uint Id { get; set; }
        public uint UnitId { get; set; }
        public string FollowPath { get; set; }
        public bool CanFly { get; set; }
        public bool CanSwim { get; set; }
        public JsonPosition Position { get; set; }
    }
}
