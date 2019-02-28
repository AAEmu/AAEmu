namespace AAEmu.Game.Models.Game.Shipyard
{
    public class ShipyardRewards
    {
        public uint Id { get; set; }
        public uint ShipyardId { get; set; }
        public uint DoodadId { get; set; }
        public bool OnWater { get; set; }
        public int Radius { get; set; }
        public int Count { get; set; }
    }
}
