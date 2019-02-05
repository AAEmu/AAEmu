namespace AAEmu.Game.Models.Game.Char
{
    public class ExpertLimit
    {
        public uint Id { get; set; }
        public int UpLimit { get; set; }
        public byte ExpertLimitCount { get; set; }
        public int Advantage { get; set; }
        public int CastAdvantage { get; set; }
        public uint UpCurrencyId { get; set; }
        public int UpPrice { get; set; }
        public uint DownCurrencyId { get; set; }
        public int DownPrice { get; set; }
    }
}
