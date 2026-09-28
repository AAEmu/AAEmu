using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.UnitTests.Game.Models.Game.CashShop;

/// <summary>
/// Covers the quote the server computes for an AA-point checkout. The client is told a ratio and
/// previews the same product, so the server has to arrive at the grant on its own and refuse
/// anything it cannot represent.
/// </summary>
public sealed class CashShopAaPointPurchaseRulesTests
{
    [Test]
    public async Task GrantsTheCashTimesTheRatio()
    {
        await Assert.That(TryQuote(3, 900, 50, 100, out var plan, out var reason)).IsTrue();
        await Assert.That(reason).IsEqualTo(CashShopAaPointFailureReason.None);
        await Assert.That(plan!.CashSpent).IsEqualTo(3L);
        await Assert.That(plan.AaPoints).IsEqualTo(300L);
        await Assert.That(plan.ExchangeRatio).IsEqualTo(100u);
    }

    [Test]
    public async Task ChargesExactlyTheCashTheRequestNamed()
    {
        // The request names the cash, never the points. A tampered request that named its own
        // price would show up here as a grant that is not the cash times the ratio.
        await Assert.That(TryQuote(7, 900, 50, 3, out var plan, out _)).IsTrue();
        await Assert.That(plan!.AaPoints).IsEqualTo(21L);
        await Assert.That(plan.CashSpent).IsEqualTo(7L);
    }

    [Test]
    public async Task RefusesAZeroCashRequest()
    {
        await Assert.That(TryQuote(0, 900, 50, 100, out _, out var reason)).IsFalse();
        await Assert.That(reason).IsEqualTo(CashShopAaPointFailureReason.InvalidAmount);
    }

    [Test]
    public async Task RefusesANegativeCashRequest()
    {
        await Assert.That(TryQuote(-100, 900, 50, 100, out _, out var reason)).IsFalse();
        await Assert.That(reason).IsEqualTo(CashShopAaPointFailureReason.InvalidAmount);
    }

    [Test]
    public async Task RefusesCashTheWalletDoesNotHold()
    {
        await Assert.That(TryQuote(901, 900, 50, 100, out _, out var reason)).IsFalse();
        await Assert.That(reason).IsEqualTo(CashShopAaPointFailureReason.InsufficientCash);
    }

    [Test]
    public async Task RefusesAZeroRatioRatherThanChargingForNothing()
    {
        // A zero ratio would make every checkout free while still reporting success.
        await Assert.That(TryQuote(1, 900, 50, 0, out _, out var reason)).IsFalse();
        await Assert.That(reason).IsEqualTo(CashShopAaPointFailureReason.ExchangeRatioUnavailable);
    }

    [Test]
    public async Task RefusesAGrantAboveTheClientCap()
    {
        // The client clamps its own preview to the cap, so a request beyond it never came from
        // this client and must not be quietly truncated into a smaller, chargeable grant.
        var cash = CashShopAaPointPurchaseRules.MaxGrantAmount;
        await Assert.That(TryQuote(cash, cash * 2, 0, 2, out _, out var reason)).IsFalse();
        await Assert.That(reason).IsEqualTo(CashShopAaPointFailureReason.Overflow);
    }

    [Test]
    public async Task GrantsExactlyTheCapWhenTheRequestLandsOnIt()
    {
        var cash = CashShopAaPointPurchaseRules.MaxGrantAmount;
        await Assert.That(TryQuote(cash, cash, 0, 1, out var plan, out var reason)).IsTrue();
        await Assert.That(reason).IsEqualTo(CashShopAaPointFailureReason.None);
        await Assert.That(plan!.AaPoints).IsEqualTo(CashShopAaPointPurchaseRules.MaxGrantAmount);
    }

    [Test]
    public async Task RefusesAGrantThatWouldOverflowTheWallet()
    {
        // The product itself fits the cap, but adding it to the balance the player already holds
        // would push the wallet past what it can represent.
        await Assert.That(TryQuote(long.MaxValue / 2, long.MaxValue, 50, 2, out _, out var reason))
            .IsFalse();
        await Assert.That(reason).IsEqualTo(CashShopAaPointFailureReason.Overflow);
    }

    [Test]
    public async Task RefusesANegativeWalletRatherThanChargingIntoIt()
    {
        await Assert.That(TryQuote(1, -1, 50, 100, out _, out var reason)).IsFalse();
        await Assert.That(reason).IsEqualTo(CashShopAaPointFailureReason.InvalidAmount);

        await Assert.That(TryQuote(1, 900, -1, 100, out _, out var pointsReason)).IsFalse();
        await Assert.That(pointsReason).IsEqualTo(CashShopAaPointFailureReason.InvalidAmount);
    }

    [Test]
    public async Task SpendsTheWholeBalanceWhenAskedForExactlyThat()
    {
        await Assert.That(TryQuote(900, 900, 50, 100, out var plan, out _)).IsTrue();
        await Assert.That(plan!.CashSpent).IsEqualTo(900L);
        await Assert.That(plan.AaPoints).IsEqualTo(90_000L);
    }

    private static bool TryQuote(long cash, long money, long aaPoints, uint ratio,
        out CashShopAaPointPurchasePlan plan, out CashShopAaPointFailureReason reason) =>
        CashShopAaPointPurchaseRules.TryCreatePlan(cash, money, aaPoints, ratio, out plan, out reason);
}
