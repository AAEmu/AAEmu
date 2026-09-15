namespace AAEmu.Game.Models.Game.Trading;

public sealed class TradeGoodMaterial
{
    public uint Id { get; set; }
    public uint TradeGoodId { get; set; }
    public uint TagId { get; set; }
    public uint RequiredCount { get; set; }
}
