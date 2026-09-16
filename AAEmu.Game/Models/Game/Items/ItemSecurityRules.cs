namespace AAEmu.Game.Models.Game.Items;

/// <summary>What a lock/unlock request did to one item.</summary>
public enum ItemSecurityChange
{
    /// <summary>The item was already in the requested state, so nothing was sent to the client.</summary>
    Unchanged,

    /// <summary>The item carries <see cref="ItemFlag.Secure"/> now.</summary>
    Locked,

    /// <summary>The lock is on its way out: <see cref="Item.UnsecureTime"/> says when it ends.</summary>
    Unlocking,

    /// <summary>The client refuses to lock this template (<c>item_secure_exceptions</c>).</summary>
    Refused
}

/// <summary>
/// The state machine behind the item security lock (CS 0x07B/0x07C and the equipment-wide
/// 0x07D/0x07E), kept apart from the manager so the decisions can be tested without a connection.
/// </summary>
/// <remarks>
/// The client renders three states — <c>ITEM_SECURITY_LOCKED</c>, <c>ITEM_SECURITY_UNLOCKING</c> and
/// <c>ITEM_SECURITY_UNLOCKED</c> — from the item's own flag byte and unsecure timestamp, so the
/// server only has to move those two values:
/// <list type="bullet">
/// <item>locking sets <see cref="ItemFlag.Secure"/> and clears the timestamp;</item>
/// <item>unlocking leaves the flag alone and sets the timestamp to the end of the delay, which is
/// what makes the item read as "unlocking" rather than "unlocked";</item>
/// <item>a lock whose timestamp has passed is dropped when the item is next looked at, which is what
/// finally turns it into "unlocked".</item>
/// </list>
/// </remarks>
public static class ItemSecurityRules
{
    /// <summary>
    /// How long an item stays locked after an unlock request. The client's own
    /// <c>X2Item:GetSecurityUnlockDelayTime()</c> answers 4320, and both of its lock dialogs print
    /// that value divided by 60 — "72 hours" — so the server counts down the same window.
    /// </summary>
    public const int UnlockDelayMinutes = 4320;

    /// <summary>
    /// Drops a lock whose delay has run out. Returns true when the item's state changed, which the
    /// load path uses to keep a stale <see cref="ItemFlag.Secure"/> bit from surviving a relog.
    /// </summary>
    public static bool ExpireUnlock(Item item, DateTime? now = null)
    {
        if (item == null || !item.HasFlag(ItemFlag.Secure))
            return false;
        if (item.UnsecureTime == DateTime.MinValue || item.UnsecureTime > (now ?? DateTime.UtcNow))
            return false;

        item.RemoveFlag(ItemFlag.Secure);
        item.UnsecureTime = DateTime.MinValue;
        return true;
    }

    /// <summary>
    /// Applies one lock or unlock request. <paramref name="isSecureException"/> is the template's
    /// verdict from <c>item_secure_exceptions</c>, which only ever blocks locking.
    /// </summary>
    public static ItemSecurityChange Apply(Item item, bool lockItem, DateTime now, bool isSecureException)
    {
        if (item == null)
            return ItemSecurityChange.Unchanged;

        ExpireUnlock(item, now);

        if (lockItem)
        {
            if (item.HasFlag(ItemFlag.Secure))
                return ItemSecurityChange.Unchanged;
            if (isSecureException)
                return ItemSecurityChange.Refused;

            item.SetFlag(ItemFlag.Secure);
            item.UnsecureTime = DateTime.MinValue;
            return ItemSecurityChange.Locked;
        }

        if (!item.HasFlag(ItemFlag.Secure))
            return ItemSecurityChange.Unchanged;

        item.UnsecureTime = now.AddMinutes(UnlockDelayMinutes);
        return ItemSecurityChange.Unlocking;
    }
}
