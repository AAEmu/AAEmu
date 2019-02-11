namespace AAEmu.Game.Models.Game.World.Zones
{
    public class SubZone
    {
        public uint Id { get; set; }
        public uint IdX { get; set; }
        public string Name { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float W { get; set; }
        public float H { get; set; }
        public int ImageMap { get; set; }
        public uint LinkedZoneGroupId { get; set; }
        public uint ParentSubZoneId { get; set; }
        public uint CategoryId { get; set; }
        // public uint SoundId { get; set; }
        // public uint SoundPackId { get; set; }
    }
}
