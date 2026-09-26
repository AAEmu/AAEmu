using System.Collections.ObjectModel;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.CashShop;

/// <summary>One content-resolved cart line. All product and price values are snapshots of ics_skus.</summary>
public sealed record CashShopPurchaseLine(
    uint SkuId,
    uint ShopId,
    byte DetailIndex,
    uint ItemId,
    uint ItemCount,
    uint BonusItemId,
    uint BonusItemCount,
    CashShopCurrencyType Currency,
    long Cost,
    string DeliveryTitle);

/// <summary>Immutable whole-cart quote used by validation, persistence, and the success reply.</summary>
public sealed class CashShopPurchasePlan
{
    public const int ReplySlotCapacity = 10;
    public const int MaxAaPointCharge = int.MaxValue;

    public CashShopPurchasePlan(
        IReadOnlyList<CashShopPurchaseLine> lines,
        IReadOnlyDictionary<CashShopCurrencyType, long> costs,
        IReadOnlyDictionary<uint, long> quantitiesByShop)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(costs);
        ArgumentNullException.ThrowIfNull(quantitiesByShop);
        Lines = new ReadOnlyCollection<CashShopPurchaseLine>(lines.ToList());
        Costs = new ReadOnlyDictionary<CashShopCurrencyType, long>(
            new Dictionary<CashShopCurrencyType, long>(costs));
        QuantitiesByShop = new ReadOnlyDictionary<uint, long>(
            new Dictionary<uint, long>(quantitiesByShop));
    }

    public IReadOnlyList<CashShopPurchaseLine> Lines { get; }
    public IReadOnlyDictionary<CashShopCurrencyType, long> Costs { get; }
    public IReadOnlyDictionary<uint, long> QuantitiesByShop { get; }

    public long CostOf(CashShopCurrencyType currency) => Costs.TryGetValue(currency, out var value) ? value : 0;
}

public enum CashShopPurchaseFailureReason
{
    InvalidContent,
    CartEmpty,
    Expired,
    LevelRequirement,
    QuestRequirement,
    SoldOut,
    PurchaseLimit,
    InsufficientCredits,
    InsufficientAaPoints,
    InsufficientLoyalty,
    InsufficientCoins
}

public sealed record CashShopPurchaseFailure(
    CashShopPurchaseFailureReason Reason,
    uint ShopId = 0,
    ErrorMessageType WireError = ErrorMessageType.IngameShopBuyFail,
    ErrorMessageType Toast = ErrorMessageType.IngameShopBuyFail);
