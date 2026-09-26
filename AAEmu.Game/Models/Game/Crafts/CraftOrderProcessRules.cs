using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>
/// Filling a posted order: the board cast names the row, the crafter's actability and labor are
/// checked, and the product and the escrowed fee leave by mail.
/// </summary>
public static class CraftOrderProcessRules
{
    /// <summary>Locale-helper key the requester's completed-order letter is filed under.</summary>
    public const string CompletedMailSender = ".craftOrderHandle";

    /// <summary>Locale-helper key the crafter's fee letter is filed under.</summary>
    public const string FeeMailSender = ".craftOrderFee";

    /// <summary>Locale-helper key the requester's expired-listing refund is filed under.</summary>
    public const string ExpiredMailSender = ".craftOrderExpired";

    /// <summary>
    /// ActionResult kind that closes the process window. The client treats kind 0 as a normal fill
    /// and kind 5 as an instant fill; both raise PROCESS_CRAFT_ORDER(result, processType). Kind 1
    /// is a different board action and does not close this window. The SCCraftOrderActionResult
    /// handler raises UI event 0x238, PROCESS_CRAFT_ORDER, for kinds 0 and 5 with different
    /// process types, and event 0x23a (POST_CRAFT_ORDER) for kind 1.
    /// </summary>
    public const byte ProcessActionKind = 0;

    /// <summary>ActionResult kind for the coupon / instant fill. Same event, processType instant.</summary>
    public const byte InstantActionKind = 5;

    /// <summary>The board's own character cannot fill the row they posted.</summary>
    public static bool IsOwnOrder(CraftOrder order, uint crafterId) =>
        order != null && order.OwnerId == crafterId;

    /// <summary>
    /// Whether this character may fill the order: not their own row, and their actability clears
    /// the point the order (and the craft) asked for.
    /// </summary>
    public static bool CanProcess(CraftOrder order, uint crafterId, int actabilityPoint) =>
        order != null && !IsOwnOrder(order, crafterId) && CraftOrderRules.CanFill(order, actabilityPoint);

    /// <summary>Instant complete is the owner's Complete on their own row, never a third party.</summary>
    public static bool CanInstant(CraftOrder order, uint characterId) =>
        IsOwnOrder(order, characterId);

    /// <summary>
    /// The owner field Complete compares to the local character id. A world-tagged composite is a
    /// different number, and the client then refuses the cast as an invalid crafting type.
    /// </summary>
    public static ulong InstantOwnerId(uint characterId) => characterId;

    /// <summary>
    /// Items one fill mails: the craft's first product amount, scaled by how many runs the order
    /// asked for. Zero when the bill is empty or will not fit a stack.
    /// </summary>
    public static int ProductCount(int amountPerCraft, uint count)
    {
        var total = (long)Math.Max(0, amountPerCraft) * count;
        return total is > 0 and <= int.MaxValue ? (int)total : 0;
    }

    /// <summary>
    /// Labor one fill costs before the actability multiplier: the craft skill's <c>consume_lp</c>
    /// times how many runs the order asked for.
    /// </summary>
    public static int LaborCost(int consumeLp, uint count)
    {
        var total = (long)Math.Max(0, consumeLp) * Math.Max(1u, count);
        if (total > int.MaxValue)
            return int.MaxValue;
        return (int)total;
    }

    /// <summary>
    /// The order id the process cast carries: two extras, low then high, which is how a 64-bit id
    /// fits the extra-values block. A single extra is the low half.
    /// </summary>
    public static bool TryReadOrderId(SkillObjectExtraValues extras, out ulong orderId)
    {
        orderId = 0;
        if (extras == null || extras.ReadCount < 1)
            return false;

        return TryReadOrderId(extras.Values, extras.ReadCount, out orderId);
    }

    /// <summary>Same read as <see cref="TryReadOrderId(SkillObjectExtraValues, out ulong)"/>, for tests.</summary>
    public static bool TryReadOrderId(IReadOnlyList<int> values, int count, out ulong orderId)
    {
        orderId = 0;
        if (values == null || count < 1 || values.Count < 1)
            return false;

        var lo = (uint)values[0];
        var hi = count > 1 && values.Count > 1 ? (uint)values[1] : 0u;
        orderId = lo | ((ulong)hi << 32);
        return orderId != 0;
    }

    /// <summary>
    /// Title/body the 10.x mail window feeds to the locale-helper functions. Those functions take
    /// the craft id as a number and look the product name up themselves — a quoted string is shown
    /// raw because it is not a craft id.
    /// </summary>
    public static string MailArgument(uint craftId) => $"title({craftId})";

    public static string MailBodyArgument(uint craftId) => $"body({craftId})";

    /// <summary>
    /// How the resident charge is scaled: <c>content_configs.craft_order_charge_for_resident</c>
    /// 480 is 4.8 %, the same number the client prints as <c>GetCraftOrderCharge() / 100</c>.
    /// Auction listing rates use this same 1/10000 scale.
    /// </summary>
    public const ulong ChargeRateDivisor = 10_000;

    /// <summary>
    /// The board's resident cut of a listed fee: <c>fee × rate / 10000</c>. Split so a huge
    /// fee cannot overflow the multiply. A missing or negative rate is no cut.
    /// </summary>
    public static ulong ChargeCut(ulong fee, int rate)
    {
        if (rate <= 0 || fee == 0)
            return 0;

        var cut = (ulong)rate;
        return fee / ChargeRateDivisor * cut + fee % ChargeRateDivisor * cut / ChargeRateDivisor;
    }

    /// <summary>
    /// What the crafter is mailed: the listed fee minus <see cref="ChargeCut"/>. Zero when the
    /// cut eats the whole listing — the leftover is not mailed.
    /// </summary>
    public static ulong CrafterPayout(ulong fee, int permille)
    {
        var cut = ChargeCut(fee, permille);
        return fee <= cut ? 0 : fee - cut;
    }
}
