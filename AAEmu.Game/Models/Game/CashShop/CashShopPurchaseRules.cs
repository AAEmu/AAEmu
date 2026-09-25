using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.CashShop;

/// <summary>Balance/eligibility snapshot used to quote a complete ICS cart before any write begins.</summary>
public sealed record CashShopPurchaseContext(
    long Credits,
    long Loyalty,
    long Money,
    long AaPoints,
    byte Level,
    bool IsGift,
    DateTime UtcNow,
    Func<uint, bool> HasCompletedQuest,
    Func<uint, uint> GetPurchasedItemCount);

/// <summary>Validates and snapshots the whole cart from the loaded ICS content.</summary>
public static class CashShopPurchaseRules
{
    public static bool TryCreatePlan(
        IReadOnlyList<IcsPurchase> shoppingCart,
        IReadOnlyDictionary<uint, IcsItem> shopItems,
        CashShopPurchaseContext context,
        out CashShopPurchasePlan plan,
        out CashShopPurchaseFailure failure)
    {
        plan = null;
        failure = null;

        if (shoppingCart is not { Count: > 0 } || shopItems is null || context is null ||
            context.HasCompletedQuest is null || context.GetPurchasedItemCount is null)
            return Refuse(CashShopPurchaseFailureReason.CartEmpty, 0, out failure);

        var now = ServerCalendar.AsUtc(context.UtcNow);
        var costs = new Dictionary<CashShopCurrencyType, long>();
        var quantitiesByShop = new Dictionary<uint, long>();
        var lines = new List<CashShopPurchaseLine>(shoppingCart.Count);

        foreach (var purchase in shoppingCart)
        {
            var requestedSku = purchase?.Sku;
            if (requestedSku == null || !shopItems.TryGetValue(requestedSku.ShopId, out var shopItem) ||
                !shopItem.Skus.TryGetValue(requestedSku.Sku, out var sku) ||
                !ReferenceEquals(sku, requestedSku))
                return Refuse(CashShopPurchaseFailureReason.InvalidContent, requestedSku?.ShopId ?? 0, out failure);

            if (sku.ItemId == 0 || sku.ItemCount == 0 ||
                (sku.BonusItemId == 0) != (sku.BonusItemCount == 0) ||
                !Enum.IsDefined(typeof(CashShopCurrencyType), sku.Currency) ||
                sku.Currency == CashShopCurrencyType.Max ||
                !Enum.IsDefined(typeof(CashShopLimitType), shopItem.LimitedType) ||
                !Enum.IsDefined(typeof(CashShopRestrictSaleType), shopItem.BuyRestrictType) ||
                !Enum.IsDefined(typeof(CashShopCmdUiType), shopItem.ShopButtons))
                return Refuse(CashShopPurchaseFailureReason.InvalidContent, sku.ShopId, out failure);

            var isCart = shoppingCart.Count > 1;
            if ((shopItem.ShopButtons == CashShopCmdUiType.NoCartAllowed && isCart) ||
                (shopItem.ShopButtons is CashShopCmdUiType.NoGiftAllowed or CashShopCmdUiType.OnlyBuyAllowed &&
                 context.IsGift) ||
                (shopItem.ShopButtons == CashShopCmdUiType.OnlyBuyAllowed && isCart))
                return Refuse(CashShopPurchaseFailureReason.InvalidContent, sku.ShopId, out failure);

            var cost = sku.DiscountPrice > 0 ? (long)sku.DiscountPrice : sku.Price;
            if (cost < 0 || cost > int.MaxValue ||
                !TryAdd(costs, sku.Currency, cost) || !TryAdd(quantitiesByShop, sku.ShopId, sku.ItemCount))
                return Refuse(CashShopPurchaseFailureReason.InvalidContent, sku.ShopId, out failure);

            if (ServerCalendar.AsUtc(sku.EventEndDate) != DateTime.MinValue && now >= ServerCalendar.AsUtc(sku.EventEndDate) ||
                ServerCalendar.AsUtc(shopItem.SaleStart) != DateTime.MinValue && now <= ServerCalendar.AsUtc(shopItem.SaleStart) ||
                ServerCalendar.AsUtc(shopItem.SaleEnd) != DateTime.MinValue && now >= ServerCalendar.AsUtc(shopItem.SaleEnd))
                return Refuse(CashShopPurchaseFailureReason.Expired, sku.ShopId,
                    ErrorMessageType.IngameShopExpiredSellByDate, ErrorMessageType.IngameShopExpiredSellByDate, out failure);

            if (shopItem.LevelMin > 0 && context.Level < shopItem.LevelMin ||
                shopItem.LevelMax > 0 && context.Level > shopItem.LevelMax ||
                shopItem.BuyRestrictType == CashShopRestrictSaleType.Level && context.Level < shopItem.BuyRestrictId)
                return Refuse(CashShopPurchaseFailureReason.LevelRequirement, sku.ShopId,
                    ErrorMessageType.IngameShopBuyFail, ErrorMessageType.IngameShopBuyLowLevel, out failure);

            if (shopItem.BuyRestrictType == CashShopRestrictSaleType.Quest &&
                !context.HasCompletedQuest(shopItem.BuyRestrictId))
                return Refuse(CashShopPurchaseFailureReason.QuestRequirement, sku.ShopId,
                    ErrorMessageType.IngameShopBuyFail, ErrorMessageType.IngameShopBuyQuestIncomplete, out failure);

            lines.Add(new CashShopPurchaseLine(
                sku.Sku,
                sku.ShopId,
                purchase.DetailIndex,
                sku.ItemId,
                sku.ItemCount,
                sku.BonusItemId,
                sku.BonusItemCount,
                sku.Currency,
                cost,
                shopItem.Name));
        }

        if (lines.Count > CashShopPurchasePlan.ReplySlotCapacity)
            return Refuse(CashShopPurchaseFailureReason.InvalidContent, lines[0].ShopId, out failure);
        if (costs.GetValueOrDefault(CashShopCurrencyType.AaPoints) > CashShopPurchasePlan.MaxAaPointCharge)
            return Refuse(CashShopPurchaseFailureReason.InvalidContent, 0, out failure);

        foreach (var (shopId, quantity) in quantitiesByShop)
        {
            var shopItem = shopItems[shopId];
            if (shopItem.Remaining >= 0 && quantity > shopItem.Remaining)
                return Refuse(CashShopPurchaseFailureReason.SoldOut, shopId,
                    ErrorMessageType.IngameShopSoldOut, ErrorMessageType.IngameShopSoldOut, out failure);

            if (shopItem.LimitedType == CashShopLimitType.None)
                continue;

            var purchased = context.GetPurchasedItemCount(shopId);
            if (purchased > shopItem.LimitedStockMax || quantity > (long)shopItem.LimitedStockMax - purchased)
            {
                var wire = shopItem.LimitedType == CashShopLimitType.Account
                    ? ErrorMessageType.IngameShopBuyNoDuplicateItem
                    : ErrorMessageType.IngameShopSoldOut;
                return Refuse(CashShopPurchaseFailureReason.PurchaseLimit, shopId, wire,
                    ErrorMessageType.IngameShopSoldOut, out failure);
            }
        }

        if (costs.GetValueOrDefault(CashShopCurrencyType.Credits) > context.Credits)
            return Refuse(CashShopPurchaseFailureReason.InsufficientCredits, 0,
                ErrorMessageType.IngameShopNotEnoughAaCash, ErrorMessageType.IngameShopNotEnoughAaCash, out failure);
        if (costs.GetValueOrDefault(CashShopCurrencyType.AaPoints) > context.AaPoints)
            return Refuse(CashShopPurchaseFailureReason.InsufficientAaPoints, 0,
                ErrorMessageType.IngameShopNotEnoughAaPoint, ErrorMessageType.IngameShopNotEnoughAaPoint, out failure);
        if (costs.GetValueOrDefault(CashShopCurrencyType.Loyalty) > context.Loyalty)
            return Refuse(CashShopPurchaseFailureReason.InsufficientLoyalty, 0,
                ErrorMessageType.IngameShopNotEnoughBmMileage, ErrorMessageType.IngameShopNotEnoughBmMileage, out failure);
        if (costs.GetValueOrDefault(CashShopCurrencyType.Coins) > context.Money)
            return Refuse(CashShopPurchaseFailureReason.InsufficientCoins, 0,
                ErrorMessageType.IngameShopBuyFail, ErrorMessageType.NotEnoughCoin, out failure);

        plan = new CashShopPurchasePlan(lines, costs, quantitiesByShop);
        return true;
    }

    private static bool TryAdd<TKey>(Dictionary<TKey, long> values, TKey key, long amount)
        where TKey : notnull
    {
        var current = values.GetValueOrDefault(key);
        if (amount < 0 || current > long.MaxValue - amount)
            return false;
        values[key] = current + amount;
        return true;
    }

    private static bool Refuse(
        CashShopPurchaseFailureReason reason,
        uint shopId,
        out CashShopPurchaseFailure failure) =>
        Refuse(reason, shopId, ErrorMessageType.IngameShopBuyFail, ErrorMessageType.IngameShopBuyFail, out failure);

    private static bool Refuse(
        CashShopPurchaseFailureReason reason,
        uint shopId,
        ErrorMessageType wireError,
        ErrorMessageType toast,
        out CashShopPurchaseFailure failure)
    {
        failure = new CashShopPurchaseFailure(reason, shopId, wireError, toast);
        return false;
    }
}
