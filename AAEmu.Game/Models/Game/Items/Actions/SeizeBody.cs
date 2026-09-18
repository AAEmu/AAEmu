using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Wire body shared by every <see cref="ItemAction.Seize"/> (14) task.
/// </summary>
/// <remarks>
/// <code>
/// u8   actionOwnerType
/// u8   slotType
/// u8   slot
/// u64  itemId
/// </code>
/// <para>
/// The 10.0.2.13 client serializer writes exactly these 11 bytes. Extra slot,
/// item, or Butler fields shift the next item action and the packet trailer out of alignment.
/// </para>
/// </remarks>
internal static class SeizeBody
{
    public static void Write(PacketStream stream, byte actionOwnerType, SlotType slotType, byte slot,
        ulong itemId)
    {
        stream.Write(actionOwnerType);
        stream.Write((byte)slotType);
        stream.Write(slot);
        stream.Write(itemId);
    }
}
