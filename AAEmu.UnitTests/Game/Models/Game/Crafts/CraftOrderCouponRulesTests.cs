using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.UnitTests.Game.Models.Game.Crafts;

/// <summary>
/// Instant ticket cost and listing lifetime come from the coupon bands, not from a C# hour or item id.
/// </summary>
public class CraftOrderCouponRulesTests
{
    private static CraftOrderCoupon Band(uint itemId, int amount, int min, int max) =>
        new(itemId, amount, min, max);

    [Test]
    public async Task RemainingHours_IsTheWholeHoursLeft()
    {
        await Assert.That(CraftOrderCouponRules.RemainingHours(7_200, 0)).IsEqualTo(2);
        await Assert.That(CraftOrderCouponRules.RemainingHours(3_599, 0)).IsEqualTo(0);
        await Assert.That(CraftOrderCouponRules.RemainingHours(100, 200)).IsEqualTo(0);
    }

    [Test]
    public async Task TryCost_TakesTheFirstCoveringBandAndRefusesWhenNoneMatch()
    {
        var bands = new[]
        {
            Band(9, 5, 36, 48),
            Band(9, 3, 24, 35),
            Band(9, 1, 0, 11)
        };

        await Assert.That(CraftOrderCouponRules.TryCost(bands, 40, out var item, out var amount)).IsTrue();
        await Assert.That(item).IsEqualTo(9u);
        await Assert.That(amount).IsEqualTo(5);

        await Assert.That(CraftOrderCouponRules.TryCost(bands, 11, out item, out amount)).IsTrue();
        await Assert.That(amount).IsEqualTo(1);

        await Assert.That(CraftOrderCouponRules.TryCost(bands, 20, out _, out _)).IsFalse();
        await Assert.That(CraftOrderCouponRules.TryCost([], 10, out _, out _)).IsFalse();
    }

    [Test]
    public async Task ListingLifetime_IsTheHighestCouponMaxHour()
    {
        var bands = new[]
        {
            Band(9, 5, 36, 48),
            Band(9, 1, 0, 11)
        };

        await Assert.That(CraftOrderCouponRules.ListingLifetime(bands)).IsEqualTo(TimeSpan.FromHours(bands[0].MaxHours));
        await Assert.That(CraftOrderCouponRules.ListingLifetime([])).IsEqualTo(TimeSpan.Zero);
        await Assert.That(CraftOrderCouponRules.ListingLifetime(null)).IsEqualTo(TimeSpan.Zero);
    }
}
