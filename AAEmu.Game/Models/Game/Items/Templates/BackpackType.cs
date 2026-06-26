namespace AAEmu.Game.Models.Game.Items.Templates;

public enum BackpackType
{
    CastleClaim = 1,
    Glider = 2,
    TradePack = 3,
    SiegeDeclare = 4,
    NationFlag = 5,
    Fish = 6,
    ToyFlag = 7,
    // 10.0.2.13: enum_backpack_types adds 8=tradegoods, 9=instance (id 5 no longer present in data)
    TradeGoods = 8,
    Instance = 9
}
