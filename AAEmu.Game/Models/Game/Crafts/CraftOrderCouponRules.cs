namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>
/// Instant complete's ticket cost: remaining whole hours until the listing lapses, then the
/// first coupon band that covers that hour.
/// </summary>
public static class CraftOrderCouponRules
{
    /// <summary>
    /// Whole hours left on the listing. The client takes the time difference, divides by 3600,
    /// and keeps the integer part; a lapsed or already-due row is 0.
    /// </summary>
    public static int RemainingHours(long expiresUnix, long nowUnix)
    {
        var seconds = expiresUnix - nowUnix;
        if (seconds <= 0)
            return 0;

        return (int)TimeSpan.FromSeconds(seconds).TotalHours;
    }

    /// <summary>
    /// How long a new listing stays on the board: the highest coupon <c>max</c> hour. No bands
    /// means Instant has no cost table and a listing has no lifetime — the caller must refuse.
    /// </summary>
    public static TimeSpan ListingLifetime(IReadOnlyList<CraftOrderCoupon> coupons)
    {
        if (coupons == null || coupons.Count == 0)
            return TimeSpan.Zero;

        var hours = 0;
        foreach (var coupon in coupons)
        {
            if (coupon.MaxHours > hours)
                hours = coupon.MaxHours;
        }

        return hours > 0 ? TimeSpan.FromHours(hours) : TimeSpan.Zero;
    }

    /// <summary>
    /// First band whose <c>[min, max]</c> contains <paramref name="hours"/>. Load order is the
    /// content id order. No band means Instant cannot run — it is not a free complete.
    /// </summary>
    public static bool TryCost(IReadOnlyList<CraftOrderCoupon> coupons, int hours, out uint itemId, out int amount)
    {
        itemId = 0;
        amount = 0;
        if (coupons == null)
            return false;

        foreach (var coupon in coupons)
        {
            if (hours < coupon.MinHours || hours > coupon.MaxHours)
                continue;
            if (coupon.ItemId == 0 || coupon.Amount <= 0)
                return false;

            itemId = coupon.ItemId;
            amount = coupon.Amount;
            return true;
        }

        return false;
    }
}
