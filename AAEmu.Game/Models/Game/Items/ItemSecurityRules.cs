namespace AAEmu.Game.Models.Game.Items;

using AAEmu.Game.GameData;

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
    /// The <c>content_configs</c> row that carries this delay, <c>enum_content_configs</c> id 43.
    /// </summary>
    public const string UnlockDelayConfigName = "item_secure_unlock_delay_time";

    /// <summary>
    /// What the delay falls back to when that row is absent. The 10.0.2.13 content ships the row with
    /// this value (id 43, kind 14), and the client's own <c>X2Item:GetSecurityUnlockDelayTime()</c>
    /// answers the same 4320 — which both of its lock dialogs print divided by 60 as "72 hours" — so
    /// the server and the client count down the same window even before the table is read.
    /// </summary>
    public const int DefaultUnlockDelayMinutes = 4320;

    /// <summary>
    /// How long an item stays locked after an unlock request: the shipped content setting, so that the
    /// server's deadline and the client's countdown cannot drift apart if the table is ever changed.
    /// </summary>
    public static int UnlockDelayMinutes =>
        ContentConfigGameData.Instance.GetInt(UnlockDelayConfigName, DefaultUnlockDelayMinutes);

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
    /// <remarks>
    /// Locking again while the unlock delay is running cancels that delay — without this an item
    /// that had been unlocked once was stuck: it kept <see cref="ItemFlag.Secure"/> and a timestamp
    /// in the future, so locking it changed nothing and the client refused to unlock it a second
    /// time. A repeated unlock request does not extend the window.
    /// </remarks>
    public static ItemSecurityChange Apply(Item item, bool lockItem, DateTime now, bool isSecureException)
    {
        if (item == null)
            return ItemSecurityChange.Unchanged;

        ExpireUnlock(item, now);

        if (lockItem)
        {
            if (!item.HasFlag(ItemFlag.Secure))
            {
                if (isSecureException)
                    return ItemSecurityChange.Refused;

                item.SetFlag(ItemFlag.Secure);
                item.UnsecureTime = DateTime.MinValue;
                return ItemSecurityChange.Locked;
            }

            // Already locked. A pending unlock is what locking again is for; with no pending
            // unlock there is genuinely nothing to do.
            if (item.UnsecureTime == DateTime.MinValue)
                return ItemSecurityChange.Unchanged;

            item.UnsecureTime = DateTime.MinValue;
            return ItemSecurityChange.Locked;
        }

        // Not locked, or already counting down (a repeat must not push the deadline out).
        if (!item.HasFlag(ItemFlag.Secure) || item.UnsecureTime != DateTime.MinValue)
            return ItemSecurityChange.Unchanged;

        item.UnsecureTime = now.AddMinutes(UnlockDelayMinutes);
        return ItemSecurityChange.Unlocking;
    }
}
