using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.CashShop;

public sealed class CashShopPurchaseRulesTests
{
    [Test]
    public async Task AaPointCart_UsesContentDiscountAndQuantities()
    {
        var (shop, sku) = Content(CashShopCurrencyType.AaPoints, price: 100, discount: 75, count: 2);
        var context = Context(aaPoints: 100);

        var ok = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(sku, DetailIndex: 3)], ContextItems(shop), context, out var plan, out var failure);

        await Assert.That(ok).IsTrue();
        await Assert.That(failure).IsNull();
        await Assert.That(plan.CostOf(CashShopCurrencyType.AaPoints)).IsEqualTo(75L);
        await Assert.That(plan.QuantitiesByShop[shop.ShopId]).IsEqualTo(2L);
        await Assert.That(plan.Lines.Single().SkuId).IsEqualTo(sku.Sku);
    }

    [Test]
    public async Task InsufficientAaPoints_IsRefusedBeforeAnyPurchaseWork()
    {
        var (shop, sku) = Content(CashShopCurrencyType.AaPoints, price: 100, count: 1);
        var context = Context(aaPoints: 99);

        var ok = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(sku, 0)], ContextItems(shop), context, out var plan, out var failure);

        await Assert.That(ok).IsFalse();
        await Assert.That(plan).IsNull();
        await Assert.That(failure.Reason).IsEqualTo(CashShopPurchaseFailureReason.InsufficientAaPoints);
        await Assert.That(failure.WireError).IsEqualTo(ErrorMessageType.IngameShopNotEnoughAaPoint);
    }

    [Test]
    public async Task SharedStock_IsCountedByItemQuantityAcrossTheWholeCart()
    {
        var (shop, sku) = Content(CashShopCurrencyType.AaPoints, price: 1, count: 2, remaining: 3);
        var context = Context(aaPoints: 10);

        var ok = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(sku, 0), new IcsPurchase(sku, 0)], ContextItems(shop), context,
            out _, out var failure);

        await Assert.That(ok).IsFalse();
        await Assert.That(failure.Reason).IsEqualTo(CashShopPurchaseFailureReason.SoldOut);
    }

    [Test]
    public async Task PurchaseLimit_UsesTheContentLimitAndExistingAuditCount()
    {
        var (shop, sku) = Content(CashShopCurrencyType.AaPoints, price: 1, count: 3,
            limitedType: CashShopLimitType.Account, limitedMax: 5, remaining: -1);
        var context = Context(aaPoints: 10, purchased: 3);

        var ok = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(sku, 0)], ContextItems(shop), context, out _, out var failure);

        await Assert.That(ok).IsFalse();
        await Assert.That(failure.Reason).IsEqualTo(CashShopPurchaseFailureReason.PurchaseLimit);
        await Assert.That(failure.WireError).IsEqualTo(ErrorMessageType.IngameShopBuyNoDuplicateItem);
    }

    [Test]
    public async Task MissingProductContent_FailsClosed()
    {
        var (shop, sku) = Content(CashShopCurrencyType.AaPoints, price: 1, count: 1);
        sku.ItemId = 0;

        var ok = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(sku, 0)], ContextItems(shop), Context(aaPoints: 10), out _, out var failure);

        await Assert.That(ok).IsFalse();
        await Assert.That(failure.Reason).IsEqualTo(CashShopPurchaseFailureReason.InvalidContent);
    }

    [Test]
    public async Task ReplySlotsAboveTheTenSlotWireCapacity_AreRefused()
    {
        var (shop, sku) = Content(CashShopCurrencyType.AaPoints, price: 1, count: 1);
        var cart = Enumerable.Range(0, CashShopPurchasePlan.ReplySlotCapacity + 1)
            .Select(_ => new IcsPurchase(sku, 0))
            .ToArray();

        var ok = CashShopPurchaseRules.TryCreatePlan(
            cart, ContextItems(shop), Context(aaPoints: 100), out _, out var failure);

        await Assert.That(ok).IsFalse();
        await Assert.That(failure.Reason).IsEqualTo(CashShopPurchaseFailureReason.InvalidContent);
    }

    [Test]
    public async Task AggregateAaPointChargeAboveInt32WireCapacity_IsRefused()
    {
        var (shop, first) = Content(CashShopCurrencyType.AaPoints,
            price: CashShopPurchasePlan.MaxAaPointCharge, count: 1, skuId: 101);
        var (_, second) = Content(CashShopCurrencyType.AaPoints,
            price: 1, count: 1, skuId: 102);
        shop.Skus[second.Sku] = second;

        var ok = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(first, 0), new IcsPurchase(second, 1)],
            ContextItems(shop), Context(aaPoints: long.MaxValue), out _, out var failure);

        await Assert.That(ok).IsFalse();
        await Assert.That(failure.Reason).IsEqualTo(CashShopPurchaseFailureReason.InvalidContent);
    }

    [Test]
    public async Task ShopButtons_RejectDisallowedCartAndGiftModes()
    {
        var (noCart, noCartSku) = Content(CashShopCurrencyType.AaPoints, price: 1, count: 1,
            shopButtons: CashShopCmdUiType.NoCartAllowed);
        var noCartPlan = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(noCartSku, 0), new IcsPurchase(noCartSku, 0)],
            ContextItems(noCart), Context(aaPoints: 10), out _, out var noCartFailure);

        var (noGift, noGiftSku) = Content(CashShopCurrencyType.AaPoints, price: 1, count: 1,
            shopButtons: CashShopCmdUiType.NoGiftAllowed, skuId: 102);
        var noGiftPlan = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(noGiftSku, 0)], ContextItems(noGift),
            Context(aaPoints: 10, isGift: true), out _, out var noGiftFailure);

        var (onlyBuy, onlyBuySku) = Content(CashShopCurrencyType.AaPoints, price: 1, count: 1,
            shopButtons: CashShopCmdUiType.OnlyBuyAllowed, skuId: 103);
        var onlyBuyGiftPlan = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(onlyBuySku, 0)], ContextItems(onlyBuy),
            Context(aaPoints: 10, isGift: true), out _, out var onlyBuyGiftFailure);
        var onlyBuyCartPlan = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(onlyBuySku, 0), new IcsPurchase(onlyBuySku, 0)], ContextItems(onlyBuy),
            Context(aaPoints: 10), out _, out var onlyBuyCartFailure);

        await Assert.That(noCartPlan).IsFalse();
        await Assert.That(noCartFailure.Reason).IsEqualTo(CashShopPurchaseFailureReason.InvalidContent);
        await Assert.That(noGiftPlan).IsFalse();
        await Assert.That(noGiftFailure.Reason).IsEqualTo(CashShopPurchaseFailureReason.InvalidContent);
        await Assert.That(onlyBuyGiftPlan).IsFalse();
        await Assert.That(onlyBuyGiftFailure.Reason).IsEqualTo(CashShopPurchaseFailureReason.InvalidContent);
        await Assert.That(onlyBuyCartPlan).IsFalse();
        await Assert.That(onlyBuyCartFailure.Reason).IsEqualTo(CashShopPurchaseFailureReason.InvalidContent);
    }

    [Test]
    public async Task UnknownAndSentinelCurrencyValues_FailClosed()
    {
        var (shop, sku) = Content(CashShopCurrencyType.AaPoints, price: 1, count: 1);
        sku.Currency = (CashShopCurrencyType)byte.MaxValue;
        var unknown = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(sku, 0)], ContextItems(shop), Context(aaPoints: 10), out _, out var unknownFailure);

        sku.Currency = CashShopCurrencyType.Max;
        var sentinel = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(sku, 0)], ContextItems(shop), Context(aaPoints: 10), out _, out var sentinelFailure);

        await Assert.That(unknown).IsFalse();
        await Assert.That(unknownFailure.Reason).IsEqualTo(CashShopPurchaseFailureReason.InvalidContent);
        await Assert.That(sentinel).IsFalse();
        await Assert.That(sentinelFailure.Reason).IsEqualTo(CashShopPurchaseFailureReason.InvalidContent);
    }

    [Test]
    public async Task UnknownShopContentEnums_FailClosed()
    {
        var (shop, sku) = Content(CashShopCurrencyType.AaPoints, price: 1, count: 1);
        shop.LimitedType = (CashShopLimitType)byte.MaxValue;
        var limited = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(sku, 0)], ContextItems(shop), Context(aaPoints: 10), out _, out var limitedFailure);

        shop.LimitedType = CashShopLimitType.None;
        shop.BuyRestrictType = (CashShopRestrictSaleType)byte.MaxValue;
        var restricted = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(sku, 0)], ContextItems(shop), Context(aaPoints: 10), out _, out var restrictedFailure);

        shop.BuyRestrictType = CashShopRestrictSaleType.None;
        shop.ShopButtons = (CashShopCmdUiType)byte.MaxValue;
        var buttons = CashShopPurchaseRules.TryCreatePlan(
            [new IcsPurchase(sku, 0)], ContextItems(shop), Context(aaPoints: 10), out _, out var buttonsFailure);

        await Assert.That(limited).IsFalse();
        await Assert.That(limitedFailure.Reason).IsEqualTo(CashShopPurchaseFailureReason.InvalidContent);
        await Assert.That(restricted).IsFalse();
        await Assert.That(restrictedFailure.Reason).IsEqualTo(CashShopPurchaseFailureReason.InvalidContent);
        await Assert.That(buttons).IsFalse();
        await Assert.That(buttonsFailure.Reason).IsEqualTo(CashShopPurchaseFailureReason.InvalidContent);
    }

    private static (IcsItem Shop, IcsSku Sku) Content(
        CashShopCurrencyType currency,
        uint price,
        uint count,
        uint discount = 0,
        int remaining = -1,
        CashShopLimitType limitedType = CashShopLimitType.None,
        ushort limitedMax = 0,
        CashShopCmdUiType shopButtons = CashShopCmdUiType.AllowAll,
        CashShopRestrictSaleType buyRestrictType = CashShopRestrictSaleType.None,
        uint shopId = 201,
        uint skuId = 101)
    {
        var sku = new IcsSku
        {
            Sku = skuId,
            ShopId = shopId,
            ItemId = 301,
            ItemCount = count,
            Currency = currency,
            Price = price,
            DiscountPrice = discount
        };
        var shop = new IcsItem
        {
            ShopId = sku.ShopId,
            Name = "Content shop",
            Remaining = remaining,
            LimitedType = limitedType,
            LimitedStockMax = limitedMax,
            ShopButtons = shopButtons,
            BuyRestrictType = buyRestrictType
        };
        shop.Skus[sku.Sku] = sku;
        return (shop, sku);
    }

    private static Dictionary<uint, IcsItem> ContextItems(IcsItem shop) => new() { [shop.ShopId] = shop };

    private static CashShopPurchaseContext Context(
        long aaPoints,
        uint purchased = 0,
        bool isGift = false) => new(
        Credits: 0,
        Loyalty: 0,
        Money: 0,
        AaPoints: aaPoints,
        Level: 50,
        IsGift: isGift,
        UtcNow: new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc),
        HasCompletedQuest: _ => true,
        GetPurchasedItemCount: _ => purchased);
}
