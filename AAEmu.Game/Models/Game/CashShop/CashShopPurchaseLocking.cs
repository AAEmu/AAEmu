using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.CashShop;

/// <summary>
/// Runs an ICS purchase body under the one documented lock order, and refuses to run it any other way.
/// </summary>
internal static class CashShopPurchaseLocking
{
    /// <summary>The single global order every money path takes its locks in.</summary>
    public const string DocumentedOrder =
        "persistence gate -> character state lock -> account lock -> purchase lock -> database transaction";

    /// <summary>
    /// The production entry point: opens the World persistence gate first, then the buyer's character state
    /// lock, then the account lock, then the process purchase lock, and runs <paramref name="body"/> inside all of
    /// them. The database transaction the caller opens is the innermost level and must be taken through
    /// <see cref="EnterTransaction"/>, so the order stays observable instead of being a comment.
    /// </summary>
    public static void Execute(Character buyer, Action body)
    {
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(body);

        ExecuteInOrder(
            enterGate: () => MailManager.Instance.DeferPersist(),
            enterCharacterState: () => MonitorLockScope.Enter(buyer.WalletSyncRoot),
            enterAccount: () => AccountManager.Instance.EnterAccountLock(buyer.AccountId),
            enterPurchase: () => MonitorLockScope.Enter(CashShopManager.Instance.PurchaseSyncRoot),
            body);
    }

    /// <summary>
    /// The order-enforcing core, shared by the production path and its tests. Each source returns a scope that
    /// releases its own lock when disposed; the scopes unwind in the mirror of the documented order.
    /// </summary>
    internal static void ExecuteInOrder(
        Func<IDisposable> enterGate,
        Func<IDisposable> enterCharacterState,
        Func<IDisposable> enterAccount,
        Func<IDisposable> enterPurchase,
        Action body)
    {
        ArgumentNullException.ThrowIfNull(enterGate);
        ArgumentNullException.ThrowIfNull(enterCharacterState);
        ArgumentNullException.ThrowIfNull(enterAccount);
        ArgumentNullException.ThrowIfNull(enterPurchase);
        ArgumentNullException.ThrowIfNull(body);

        using (Enter(enterGate, CashShopLockLevel.PersistenceGate))
        using (Enter(enterCharacterState, CashShopLockLevel.CharacterState))
        using (Enter(enterAccount, CashShopLockLevel.Account))
        using (Enter(enterPurchase, CashShopLockLevel.Purchase))
        {
            body();
        }
    }

    /// <summary>
    /// Marks the innermost level: the purchase's own database transaction. Staging a purchase without it is a
    /// lock-order violation, so a future caller cannot quietly lose the gate or a state lock first.
    /// </summary>
    public static IDisposable EnterTransaction()
    {
        // A transaction may only be opened inside the four locks; taking it first is the old, deadlocking shape.
        CashShopLockOrder.RequireHeld(CashShopLockLevel.Purchase);
        return Enter(static () => null, CashShopLockLevel.Database);
    }

    private static IDisposable Enter(Func<IDisposable> source, CashShopLockLevel level)
    {
        CashShopLockOrder.Enter(level);
        try
        {
            // A null scope is the transaction level: it owns a DbTransaction, not a monitor.
            return new TrackedScope(level, source());
        }
        catch
        {
            CashShopLockOrder.Exit(level);
            throw;
        }
    }

    private sealed class TrackedScope(CashShopLockLevel level, IDisposable? inner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                inner?.Dispose();
            }
            finally
            {
                CashShopLockOrder.Exit(level);
            }
        }
    }
}

/// <summary>A monitor lock held for the life of the returned scope, released in the documented order.</summary>
internal sealed class MonitorLockScope : IDisposable
{
    private readonly object _syncRoot;
    private bool _disposed;

    private MonitorLockScope(object syncRoot)
    {
        _syncRoot = syncRoot;
        Monitor.Enter(syncRoot);
    }

    public static MonitorLockScope Enter(object syncRoot)
    {
        ArgumentNullException.ThrowIfNull(syncRoot);
        return new MonitorLockScope(syncRoot);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Monitor.Exit(_syncRoot);
    }
}
