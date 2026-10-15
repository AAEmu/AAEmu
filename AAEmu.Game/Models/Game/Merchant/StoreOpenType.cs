namespace AAEmu.Game.Models.Game.Merchant;

/// <summary>
/// Client <c>SHOP_OPEN_*</c> wire values on <c>CSBuyItems.openType</c>. Direct-open shops
/// (no NPC/doodad) pick their merchant pack from <c>content_configs</c> by these values.
/// </summary>
public static class StoreOpenType
{
    public const byte Normal = 0;
    public const byte LivingPoint = 1;
    public const byte HonorPoint = 2;
    public const byte Contribution = 3;
    public const byte Battlefield = 4;

    public const string LivingPointShopConfig = "living_point_shop";
    public const string HonorPointShopConfig = "honor_point_shop";
    public const string ContributionShopConfig = "expedition_contribution_point_shop";
    public const string BattlefieldShopConfig = "battle_field_shop";

    public static string ConfigNameFor(byte openType) => openType switch
    {
        LivingPoint => LivingPointShopConfig,
        HonorPoint => HonorPointShopConfig,
        Contribution => ContributionShopConfig,
        Battlefield => BattlefieldShopConfig,
        _ => null
    };
}
