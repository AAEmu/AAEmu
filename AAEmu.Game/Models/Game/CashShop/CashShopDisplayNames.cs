namespace AAEmu.Game.Models.Game.CashShop;

public static class CashShopDisplayNames
{
    public static bool NeedsResolvedName(string? name) =>
        string.IsNullOrWhiteSpace(name)
        || name.StartsWith("Premium #", StringComparison.OrdinalIgnoreCase);

    public static uint ResolveItemTemplateId(uint displayItemId, uint skuItemId) =>
        displayItemId != 0 ? displayItemId : skuItemId;
}
