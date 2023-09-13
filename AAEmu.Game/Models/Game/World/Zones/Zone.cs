namespace AAEmu.Game.Models.Game.World.Zones
{
    public class Zone
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public uint ZoneKey { get; set; }
        public uint GroupId { get; set; }
        public bool Closed { get; set; }
        public uint FactionId { get; set; }
        public uint ZoneClimateId { get; set; }
    }
}