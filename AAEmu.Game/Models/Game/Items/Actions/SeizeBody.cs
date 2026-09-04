using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Wire body shared by every <see cref="ItemAction.Seize"/> (14) task.
/// </summary>
/// <remarks>
/// <para>Schema:</para>
/// <code>
/// u8   actionOwnerType
/// u8   slotType, u8 slot   // slot descriptor #1
/// u8   slotType, u8 slot   // slot descriptor #2
/// item                     // full item body; templateId 0 means "no item" and ends the body there
/// u64  removeItemId
/// u64  dbButlerId
/// </code>
/// <para>
/// The body is fixed length only when the item is empty; a real item extends it. Getting the length
/// wrong does not stay contained to this task: the fields that follow it in SCItemTaskSuccess - the
/// force-remove count and the ids after it - are then read from the wrong offset, and the client
/// drops item ids that were never named. Consuming a stack to zero emits this action, so every
/// destroy path in the server rides on this body being right.
/// </para>
/// <para>
/// The item is written empty, which is what clears the slot; writing the real item re-states it into
/// the slot instead. Deletion is expressed by <c>removeItemId</c> together with the packet's
/// force-remove list, not by this body.
/// </para>
/// </remarks>
internal static class SeizeBody
{
    /// <param name="item">
    /// Written into the item slot of the body. Null writes templateId 0, meaning "no item", which
    /// clears the slot. Passing the real item re-sets the slot from it, which is what a shrinking
    /// stack wants rather than a disappearing one.
    /// </param>
    /// <param name="removeItemId">Item id the client should drop, or 0 for none.</param>
    public static void Write(PacketStream stream, byte actionOwnerType, SlotType slotType, byte slot,
        ulong removeItemId, Item item = null)
    {
        stream.Write(actionOwnerType);
        stream.Write((byte)slotType);
        stream.Write(slot);
        stream.Write((byte)slotType);
        stream.Write(slot);
        if (item is null)
            stream.Write(0u); // item.templateId - 0 means "no item", i.e. leave the slot empty
        else
            item.Write(stream);
        stream.Write(removeItemId);
        stream.Write(0UL); // dbButlerId
    }
}
