namespace AAEmu.Game.Models.Game.Items;

/// <summary>Whether one item in a vendor sale batch actually reached the buy-back list.</summary>
public enum ItemSaleTransfer
{
    /// <summary>The item was moved to the buy-back list, so the player no longer holds it.</summary>
    Moved = 0,

    /// <summary>The move failed and the item is still in the bag, so the player still holds it.</summary>
    NotMoved
}

/// <summary>
/// What a vendor sale batch pays out, kept as a pure function of what each line actually did.
/// </summary>
/// <remarks>
/// <para>
/// The invariant is that money is owed only for items the player no longer holds. The sell handler
/// moves each item to the buy-back list and then pays once for the whole batch, so an item whose move
/// failed but whose value was still added to the total would pay a character for something still in
/// their bag - and the mirror of that is a moved item that is never paid for, which is the state this
/// exists to make impossible.
/// </para>
/// <para>
/// Keeping it here rather than inline in the handler is what makes it checkable: the handler needs a
/// live inventory to run, and the property that matters here is arithmetic over what happened, not
/// inventory behaviour.
/// </para>
/// </remarks>
public static class VendorSalePayoutRules
{
    /// <summary>
    /// What one line of a vendor sale is worth.
    /// </summary>
    /// <param name="refund">The template's refund value.</param>
    /// <param name="refundMultiplier">The grade's refund percentage.</param>
    /// <param name="count">How many of the item were sold.</param>
    /// <param name="transfer">Whether the item actually moved to the buy-back list.</param>
    /// <returns>
    /// The line's value when it moved, and <c>0</c> when it did not - because an item still in the
    /// bag is not something a vendor has taken, and paying for it would hand out money for nothing.
    /// </returns>
    public static int LineValue(int refund, float refundMultiplier, int count, ItemSaleTransfer transfer)
    {
        if (transfer != ItemSaleTransfer.Moved)
            return 0;

        return (int)(refund * refundMultiplier / 100f) * count;
    }

    /// <summary>
    /// The batch total, which is what the handler pays once. Summing per line rather than accumulating
    /// in the loop keeps the "only what moved" rule in one place, so a line added without consulting
    /// it cannot be paid for by accident.
    /// </summary>
    public static int BatchValue(IEnumerable<(int Refund, float Multiplier, int Count, ItemSaleTransfer Transfer)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var total = 0;
        foreach (var (refund, multiplier, count, transfer) in lines)
            total += LineValue(refund, multiplier, count, transfer);
        return total;
    }
}
