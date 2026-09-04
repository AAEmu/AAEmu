namespace AAEmu.Game.Models.Game.Trading;

public sealed class SpecialtyContentSettings
{
    public int MaxPriceRatio { get; init; }
    public int MinPriceRatio { get; init; }
    public int AdjustRatioPerTrade { get; init; }
    public int SellerShareRatio { get; init; }
    public int SellBackpackLevelLimit { get; init; }
    public int PriceTradeGoodsCount { get; init; }
    public int PriceRecoverRate { get; init; }
    public int MailInterest { get; init; }
    public int GoodsRatioCount { get; init; }
}
