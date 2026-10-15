using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// One line the client puts in a vendor-sell request.
/// </summary>
/// <remarks>
/// Wire order matches the client's sell-entry serializer: container type, slot index, instance id,
/// then a stack field, a reservation timestamp, and three trailing type/slave fields shared with the
/// remove-by-id item task. The in-memory slot struct pads type/index to four bytes; only the two
/// live bytes travel on the wire.
/// </remarks>
public readonly record struct VendorSellEntry(
    SlotType SlotType,
    byte Slot,
    ulong ItemId,
    uint Stack,
    ulong RemoveReservationTime,
    uint DetailType,
    uint DbSlaveId,
    uint OwnerType);

/// <summary>
/// Pure helpers for vendor-sell wire layout and the buy-back list the sell path feeds.
/// </summary>
public static class VendorSellWireRules
{
    /// <summary>
    /// How many sold items the buy-back tab keeps. Matches the sold-item list packet's client cap.
    /// </summary>
    public const int BuybackListCapacity = 12;

    /// <summary>
    /// Bytes of one sell-entry body after the shared npc/doodad/count header.
    /// </summary>
    public const int EntryWireSize =
        1 + 1 + 8 + 4 + 8 + 4 + 4 + 4; // type, index, id, stack, reservation, type, dbSlaveId, type

    /// <summary>
    /// Reads one sell-entry with the live wire widths (u8 type + u8 index, not a padded u32 slot).
    /// </summary>
    public static VendorSellEntry ReadEntry(PacketStream stream) =>
        new(
            (SlotType)stream.ReadByte(),
            stream.ReadByte(),
            stream.ReadUInt64(),
            stream.ReadUInt32(),
            stream.ReadUInt64(),
            stream.ReadUInt32(),
            stream.ReadUInt32(),
            stream.ReadUInt32());

    /// <summary>
    /// Whether this line names a bag or equipment slot the sell handler knows how to look up.
    /// </summary>
    public static bool IsSellableContainer(SlotType slotType) =>
        slotType is SlotType.Equipment or SlotType.Inventory;

    /// <summary>
    /// Whether the sell entry's <c>stack</c> field may be accepted for this bag item.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's sell builder only fills slot, instance id, and a trailing type on each entry.
    /// It never writes the serializer's <c>stack</c> field, so that dword stays zero on the wire
    /// (confirmed live for a single gold ticket and for a 50-stack). Lua <c>SellStoreItem</c>
    /// <c>count</c> is how many slots are in the cart, not how many of one stack to sell.
    /// </para>
    /// <para>
    /// Sold quantity is therefore always the live bag <paramref name="itemCount"/> — payout uses
    /// that count (1 silver × 50 = 50 silver). A zero wire stack is "unused", not "sell zero".
    /// A positive wire stack is still checked against the bag so a mismatched client cannot
    /// under-pay a larger stack.
    /// </para>
    /// </remarks>
    public static bool StackFieldAllowsSale(uint wireStack, int itemCount)
    {
        if (itemCount <= 0)
            return false;
        // Client leave the field zero; quantity comes from the bag item itself.
        if (wireStack == 0)
            return true;
        return wireStack <= int.MaxValue && (int)wireStack == itemCount;
    }

    /// <summary>
    /// How many oldest buy-back rows to drop so a new sale still fits under
    /// <see cref="BuybackListCapacity"/>. Newest sales sit at the front of the list.
    /// </summary>
    public static int ExcessBuybackCount(int currentCount, int capacity = BuybackListCapacity)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (currentCount < 0)
            throw new ArgumentOutOfRangeException(nameof(currentCount));

        return currentCount > capacity ? currentCount - capacity : 0;
    }
}
