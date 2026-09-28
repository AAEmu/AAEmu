namespace AAEmu.Game.Models.Game.CashShop;

/// <summary>One validated AA-point checkout: the cash to charge and the points it buys.</summary>
public sealed record CashShopAaPointPurchasePlan(long CashSpent, long AaPoints, uint ExchangeRatio);

/// <summary>Validates an AA-point checkout request before any write begins.</summary>
public static class CashShopAaPointPurchaseRules
{
    /// <summary>
    /// Largest grant a single checkout may produce. The client clamps its own preview to this
    /// value before it sends the request, so anything above it is a request the client would
    /// never have made and is refused rather than silently truncated.
    /// </summary>
    public const long MaxGrantAmount = 2_100_000_000L;

    /// <summary>Largest balance the wallet column is required to represent after a checkout.</summary>
    public const long MaxWalletAmount = long.MaxValue;

    /// <summary>
    /// Turns a requested cash amount into a charge and a grant.
    /// </summary>
    /// <param name="requestedCash">Cash the request asks to spend, as the client sent it.</param>
    /// <param name="liveMoney">Cash the character actually holds right now.</param>
    /// <param name="liveAaPoints">AA points the character already holds.</param>
    /// <param name="exchangeRatio">
    /// AA points granted per unit of cash, the value published to the client on the exchange
    /// ratio packet. A request is only quotable while this is a usable positive value.
    /// </param>
    public static bool TryCreatePlan(
        long requestedCash,
        long liveMoney,
        long liveAaPoints,
        uint exchangeRatio,
        out CashShopAaPointPurchasePlan plan,
        out CashShopAaPointFailureReason failure)
    {
        plan = null;
        failure = CashShopAaPointFailureReason.None;

        if (requestedCash <= 0 || liveMoney < 0 || liveAaPoints < 0)
        {
            failure = CashShopAaPointFailureReason.InvalidAmount;
            return false;
        }

        if (exchangeRatio == 0)
        {
            failure = CashShopAaPointFailureReason.ExchangeRatioUnavailable;
            return false;
        }

        if (requestedCash > liveMoney)
        {
            failure = CashShopAaPointFailureReason.InsufficientCash;
            return false;
        }

        // The grant is computed in a wider type than the wallet so an overflowing product is
        // detected instead of wrapping into a small, creditable number.
        var grant = (long)requestedCash * exchangeRatio;
        if (grant <= 0 || grant > MaxGrantAmount)
        {
            failure = CashShopAaPointFailureReason.Overflow;
            return false;
        }

        if (liveAaPoints > MaxWalletAmount - grant)
        {
            failure = CashShopAaPointFailureReason.Overflow;
            return false;
        }

        plan = new CashShopAaPointPurchasePlan(requestedCash, grant, exchangeRatio);
        return true;
    }
}
