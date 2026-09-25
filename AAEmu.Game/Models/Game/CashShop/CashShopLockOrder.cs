namespace AAEmu.Game.Models.Game.CashShop;

/// <summary>
/// The one order in which the ICS purchase path may take locks, from outermost to innermost:
/// <c>persistence gate -> character state lock -> account lock -> purchase lock -> database transaction</c>.
///
/// <para>Every holder that a World save can meet takes the same prefix, so the purchase path can never
/// close a cycle with one of them:</para>
/// <list type="bullet">
/// <item><description><c>MailManager.DeferPersist</c> / <c>CharacterCraft.Craft</c> / <c>ItemManager</c> /
/// <c>SpecialtyManager</c> / <c>ButlerChargeService</c>: gate, then the character state or account lock.</description></item>
/// <item><description><c>Character.ChangeLabor</c>: character state lock, then the account lock.</description></item>
/// <item><description>A save: the gate exclusively, and no other lock.</description></item>
/// </list>
///
/// <para>The previous purchase order (purchase, state, account, gate-last) inverted the first of those and
/// deadlocked: the purchase held the character state lock while waiting for the gate, a craft held the gate
/// while waiting for that same state lock, and the periodic World save then blocked on the gate for good.</para>
/// </summary>
internal enum CashShopLockLevel
{
    /// <summary>Nothing is held on this thread.</summary>
    None = 0,
    /// <summary>The World persistence gate, shared with every other money operation and exclusive with a save.</summary>
    PersistenceGate = 1,
    /// <summary>The buyer's character state lock (<c>Character.WalletSyncRoot</c>).</summary>
    CharacterState = 2,
    /// <summary>The buyer's account lock (<c>AccountManager.WithAccountLock</c>).</summary>
    Account = 3,
    /// <summary>The process cash-shop purchase lock.</summary>
    Purchase = 4,
    /// <summary>The purchase's own database transaction.</summary>
    Database = 5
}

/// <summary>
/// Thread-local enforcement of <see cref="CashShopLockLevel"/> order. It records every acquisition the
/// cash-shop purchase path makes and refuses an inversion instead of documenting one, so a later change that
/// reorders the path fails loudly (and is caught by a deterministic test) rather than deadlocking a live World.
/// </summary>
internal static class CashShopLockOrder
{
    [ThreadStatic] private static List<CashShopLockLevel>? _held;

    /// <summary>The innermost level currently held on this thread, or <see cref="CashShopLockLevel.None"/>.</summary>
    public static CashShopLockLevel Innermost => _held is { Count: > 0 } ? _held[^1] : CashShopLockLevel.None;

    /// <summary>How many levels (re-entrant entries included) this thread holds.</summary>
    public static int Depth => _held?.Count ?? 0;

    /// <summary>A snapshot of the held levels, outermost first. Used by tests and diagnostics.</summary>
    public static IReadOnlyList<CashShopLockLevel> Held =>
        _held is { Count: > 0 } ? _held.ToArray() : Array.Empty<CashShopLockLevel>();

    /// <summary>Records an acquisition, throwing when it would invert the documented order.</summary>
    public static void Enter(CashShopLockLevel level)
    {
        if (level == CashShopLockLevel.None)
            throw new ArgumentOutOfRangeException(nameof(level), level, "A purchase lock level must be a real lock.");

        _held ??= [];
        if (_held.Count == 0 && level != CashShopLockLevel.PersistenceGate)
            throw new InvalidOperationException(
                $"ICS purchase lock order violation: {level} cannot be the first lock this thread takes. " +
                $"The documented order is {CashShopPurchaseLocking.DocumentedOrder}.");
        if (_held.Count > 0 && _held[^1] > level)
            throw new InvalidOperationException(
                $"ICS purchase lock order violation: {level} cannot be taken while {_held[^1]} is held. " +
                $"The documented order is {CashShopPurchaseLocking.DocumentedOrder}.");

        _held.Add(level);
    }

    /// <summary>Records a release, throwing when the path unwinds out of order.</summary>
    public static void Exit(CashShopLockLevel level)
    {
        if (_held is not { Count: > 0 } || _held[^1] != level)
            throw new InvalidOperationException(
                $"ICS purchase lock order violation: {level} was released while " +
                $"{(_held is { Count: > 0 } ? _held[^1] : CashShopLockLevel.None)} is held.");

        _held.RemoveAt(_held.Count - 1);
    }

    /// <summary>Throws unless this thread holds <paramref name="level"/> and every level before it.</summary>
    public static void RequireHeld(CashShopLockLevel level)
    {
        if (_held is not { Count: > 0 } || _held[^1] != level)
            throw new InvalidOperationException(
                $"ICS purchase work must run under {level}; the documented order is {CashShopPurchaseLocking.DocumentedOrder}.");
    }

    /// <summary>
    /// Drops anything this thread recorded. A test that fails mid-body leaves its levels behind, and the
    /// tracker is thread-local; only tests call this.
    /// </summary>
    internal static void Reset() => _held?.Clear();
}
