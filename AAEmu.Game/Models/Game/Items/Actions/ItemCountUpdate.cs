using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemCountUpdate : ItemTask
{
    private readonly Item _item;

    /// <summary>
    /// Announces the new stack size of an existing item.
    /// </summary>
    /// <param name="item">Item whose count has already been changed</param>
    /// <param name="count">
    /// Signed delta that was applied. Kept for call-site clarity only — the wire carries the resulting
    /// stack size, not the delta.
    /// </param>
    /// <remarks>
    /// Always Take (action 6): slot followed by a full item body whose stackSize is the new count. It is
    /// the only action that re-states a stack without destroying it.
    /// <para>
    /// Create (action 5) is deliberately not used. Its body is only
    /// <c>id, amount, templateId</c> and it means "this many units were gained", which the client applies
    /// by creating the item — so it does nothing for an id the client already holds. Merging a stack sent
    /// Remove for the emptied source and Create for the target, and the client honoured the removal while
    /// dropping the increment, so a 9 stack plus 1 lost the item entirely until the next relog resynced
    /// the bag. There is also no negative form: a negative delta arrives as a huge u32 and is ignored,
    /// and the stack size gets announced as a bogus gain (a 37 stack consumed 10 at a time logged
    /// "Acquired x27, x17, x7" while never shrinking).
    /// </para>
    /// <para>
    /// Take still announces the change in chat as "Acquired xN" with the pickup sound, which is wrong for
    /// a decrease but is cosmetic. The alternatives are worse: Seize reads correctly in chat but takes the
    /// WHOLE stack out of the client's bag, and AddStack (action 4) is templateId + amount with no slot or
    /// item id, so it cannot address a particular stack.
    /// </para>
    /// </remarks>
    public ItemCountUpdate(Item item, int count)
    {
        _ = count;
        _item = item;
        _type = ItemAction.Take;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_item.SlotType);
        stream.Write((byte)_item.Slot);
        WriteDetails(stream, _item);

        return stream;
    }
}
