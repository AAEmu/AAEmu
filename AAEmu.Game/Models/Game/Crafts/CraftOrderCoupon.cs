namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>
/// One Instant-complete ticket band from <c>craft_order_coupons</c>: remaining hours in
/// <c>[MinHours, MaxHours]</c> costs <c>Amount</c> of <c>ItemId</c>.
/// </summary>
public readonly record struct CraftOrderCoupon(uint ItemId, int Amount, int MinHours, int MaxHours);
