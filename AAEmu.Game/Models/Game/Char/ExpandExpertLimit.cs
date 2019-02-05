namespace AAEmu.Game.Models.Game.Char
{
    public class ExpandExpertLimit
    {
        public uint Id { get; set; }
        public byte ExpandCount { get; set; }
        public int LifePoint { get; set; } // TODO Vocation
        public uint ItemId { get; set; }
        public int ItemCount { get; set; }
    }
}
