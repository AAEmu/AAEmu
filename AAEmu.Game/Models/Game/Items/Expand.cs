namespace AAEmu.Game.Models.Game.Items
{
    public class Expand
    {
        public bool IsBank { get; set; }
        public int Step { get; set; }
        public int Price { get; set; }
        public uint ItemId { get; set; }
        public int ItemCount { get; set; }
        public int CurrencyId { get; set; }
    }
}