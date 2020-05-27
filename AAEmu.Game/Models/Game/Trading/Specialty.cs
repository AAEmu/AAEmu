namespace AAEmu.Game.Models.Game.Trading
{
    public class Specialty
    {
        public uint Id { get; set; }
        public uint RowZoneGroupId { get; set; }
        public uint ColZoneGroupId { get; set; }
        public uint Ratio { get; set; }
        public uint Profit { get; set; }
        public bool VendorExist { get; set; }
    }
}
