using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Bag/trade splits must conserve count and keep the new stack on the same
/// owner and flags as the source. A fresh <c>ItemManager.Create</c> starts at
/// <c>OwnerId = 0</c>; listing and save both treat that as "not in the bag".
/// </summary>
public static class ItemSplitRules
{
    public static bool IsSplitAmount(int sourceCount, int amount) =>
        amount > 0 && sourceCount > amount;

    /// <summary>
    /// True when the source lost exactly <paramref name="amount"/> and the new
    /// stack holds that same amount — never 101 → 100 + 101.
    /// </summary>
    public static bool ConservesCount(int sourceBefore, int amount, int sourceAfter, int newCount) =>
        IsSplitAmount(sourceBefore, amount)
        && sourceAfter == sourceBefore - amount
        && newCount == amount
        && sourceAfter + newCount == sourceBefore;

    public static void CopyStackFields(Item source, Item dest)
    {
        if (source == null || dest == null)
            return;

        dest.ItemFlags = source.ItemFlags;
        dest.LifespanMins = source.LifespanMins;
        dest.MadeUnitId = source.MadeUnitId;
        dest.CreateTime = source.CreateTime;
        dest.UnsecureTime = source.UnsecureTime;
        dest.UnpackTime = source.UnpackTime;
        dest.ImageItemTemplateId = source.ImageItemTemplateId;
        dest.UccId = source.UccId;
        dest.ExpirationTime = source.ExpirationTime;
        dest.ExpirationOnlineMinutesLeft = source.ExpirationOnlineMinutesLeft;
        dest.ChargeStartTime = source.ChargeStartTime;
        dest.ChargeCount = source.ChargeCount;
        dest.ChargeUseSkillTime = source.ChargeUseSkillTime;
        dest.DetailType = source.DetailType;
        dest.Detail = source.Detail == null ? null : (byte[])source.Detail.Clone();
    }

    /// <summary>
    /// The open bag watches the task reason. A split that reports <c>SwapItems</c>
    /// leaves the new stack as a ghost until the bag is closed and the full
    /// contents dump arrives.
    /// </summary>
    public static ItemTaskType ReportTaskType(bool wasSplit, ItemTaskType requested)
    {
        if (wasSplit)
            return ItemTaskType.Split;
        return requested == ItemTaskType.Split ? ItemTaskType.SwapItems : requested;
    }

    public static void PlaceNewStack(Item dest, ulong ownerId, SlotType slotType, byte slot)
    {
        if (dest == null)
            return;
        dest.OwnerId = ownerId;
        dest.SlotType = slotType;
        dest.Slot = slot;
    }
}
