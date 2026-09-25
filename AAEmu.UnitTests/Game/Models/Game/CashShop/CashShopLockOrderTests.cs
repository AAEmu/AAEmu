using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.UnitTests.Game.Models.Game.CashShop;

/// <summary>
/// Pins the one global lock order the ICS purchase path uses. These assertions are about the order itself,
/// not about timing: the tracker throws the moment a path would invert it, so a regression fails the suite
/// instead of deadlocking a live World.
/// </summary>
[NotInParallel]
public sealed class CashShopLockOrderTests
{
    [Before(Test)]
    public void ResetRecordedOrder() => CashShopLockOrder.Reset();

    [Test]
    public async Task ExecuteInOrder_TakesGateThenStateThenAccountThenPurchase()
    {
        var acquired = new List<CashShopLockLevel>();
        var released = new List<CashShopLockLevel>();
        CashShopLockLevel? innermostInBody = null;

        CashShopPurchaseLocking.ExecuteInOrder(
            () => Scope(CashShopLockLevel.PersistenceGate, acquired, released),
            () => Scope(CashShopLockLevel.CharacterState, acquired, released),
            () => Scope(CashShopLockLevel.Account, acquired, released),
            () => Scope(CashShopLockLevel.Purchase, acquired, released),
            () => innermostInBody = CashShopLockOrder.Innermost);

        await Assert.That(acquired).HasCount(4);
        await Assert.That(acquired[0]).IsEqualTo(CashShopLockLevel.PersistenceGate);
        await Assert.That(acquired[1]).IsEqualTo(CashShopLockLevel.CharacterState);
        await Assert.That(acquired[2]).IsEqualTo(CashShopLockLevel.Account);
        await Assert.That(acquired[3]).IsEqualTo(CashShopLockLevel.Purchase);
        await Assert.That(released).HasCount(4);
        await Assert.That(released[0]).IsEqualTo(CashShopLockLevel.Purchase);
        await Assert.That(released[1]).IsEqualTo(CashShopLockLevel.Account);
        await Assert.That(released[2]).IsEqualTo(CashShopLockLevel.CharacterState);
        await Assert.That(released[3]).IsEqualTo(CashShopLockLevel.PersistenceGate);
        await Assert.That(innermostInBody).IsEqualTo(CashShopLockLevel.Purchase);
        await Assert.That(CashShopLockOrder.Depth).IsEqualTo(0);
        await Assert.That(CashShopLockOrder.Innermost).IsEqualTo(CashShopLockLevel.None);
    }

    [Test]
    public async Task ExecuteInOrder_KeepsTheOrderWhenTheBodyThrows()
    {
        var released = new List<CashShopLockLevel>();

        try
        {
            CashShopPurchaseLocking.ExecuteInOrder(
                () => Scope(CashShopLockLevel.PersistenceGate, [], released),
                () => Scope(CashShopLockLevel.CharacterState, [], released),
                () => Scope(CashShopLockLevel.Account, [], released),
                () => Scope(CashShopLockLevel.Purchase, [], released),
                () => throw new InvalidOperationException("boom"));
            await Assert.That(false).IsTrue();
        }
        catch (InvalidOperationException ex)
        {
            await Assert.That(ex.Message).IsEqualTo("boom");
        }

        await Assert.That(released).HasCount(4);
        await Assert.That(released[0]).IsEqualTo(CashShopLockLevel.Purchase);
        await Assert.That(released[1]).IsEqualTo(CashShopLockLevel.Account);
        await Assert.That(released[2]).IsEqualTo(CashShopLockLevel.CharacterState);
        await Assert.That(released[3]).IsEqualTo(CashShopLockLevel.PersistenceGate);
        await Assert.That(CashShopLockOrder.Depth).IsEqualTo(0);
    }

    [Test]
    public async Task EnteringTheGateUnderTheStateLock_IsRefused()
    {
        // The exact inversion that deadlocked the World: the purchase held the character state lock and then
        // asked for the gate while a craft (gate, then state lock) was already inside.
        CashShopLockOrder.Enter(CashShopLockLevel.PersistenceGate);
        try
        {
            CashShopLockOrder.Enter(CashShopLockLevel.CharacterState);
            var refused = false;
            try
            {
                CashShopLockOrder.Enter(CashShopLockLevel.PersistenceGate);
            }
            catch (InvalidOperationException ex)
            {
                refused = ex.Message.Contains(CashShopPurchaseLocking.DocumentedOrder);
            }

            await Assert.That(refused).IsTrue();
            await Assert.That(CashShopLockOrder.Innermost).IsEqualTo(CashShopLockLevel.CharacterState);

            CashShopLockOrder.Exit(CashShopLockLevel.CharacterState);
        }
        finally
        {
            CashShopLockOrder.Exit(CashShopLockLevel.PersistenceGate);
        }

        await Assert.That(CashShopLockOrder.Depth).IsEqualTo(0);
    }

    [Test]
    public async Task EnteringTheAccountLockUnderThePurchaseLock_IsRefused()
    {
        CashShopLockOrder.Enter(CashShopLockLevel.PersistenceGate);
        CashShopLockOrder.Enter(CashShopLockLevel.CharacterState);
        CashShopLockOrder.Enter(CashShopLockLevel.Account);
        try
        {
            CashShopLockOrder.Enter(CashShopLockLevel.Purchase);
            var refused = false;
            try
            {
                CashShopLockOrder.Enter(CashShopLockLevel.Account);
            }
            catch (InvalidOperationException)
            {
                refused = true;
            }

            await Assert.That(refused).IsTrue();
            await Assert.That(CashShopLockOrder.Innermost).IsEqualTo(CashShopLockLevel.Purchase);
            CashShopLockOrder.Exit(CashShopLockLevel.Purchase);
        }
        finally
        {
            CashShopLockOrder.Exit(CashShopLockLevel.Account);
            CashShopLockOrder.Exit(CashShopLockLevel.CharacterState);
            CashShopLockOrder.Exit(CashShopLockLevel.PersistenceGate);
        }

        await Assert.That(CashShopLockOrder.Depth).IsEqualTo(0);
    }

    [Test]
    public async Task TheInnermostLevel_AllowsTheReentrantPurchaseLock()
    {
        // ApplyCommittedStock re-enters the purchase lock on the same thread after the commit.
        CashShopLockOrder.Enter(CashShopLockLevel.PersistenceGate);
        CashShopLockOrder.Enter(CashShopLockLevel.CharacterState);
        CashShopLockOrder.Enter(CashShopLockLevel.Account);
        CashShopLockOrder.Enter(CashShopLockLevel.Purchase);
        CashShopLockOrder.Enter(CashShopLockLevel.Purchase);
        await Assert.That(CashShopLockOrder.Depth).IsEqualTo(5);
        CashShopLockOrder.Exit(CashShopLockLevel.Purchase);
        await Assert.That(CashShopLockOrder.Innermost).IsEqualTo(CashShopLockLevel.Purchase);
        CashShopLockOrder.Exit(CashShopLockLevel.Purchase);
        CashShopLockOrder.Exit(CashShopLockLevel.Account);
        CashShopLockOrder.Exit(CashShopLockLevel.CharacterState);
        CashShopLockOrder.Exit(CashShopLockLevel.PersistenceGate);
        await Assert.That(CashShopLockOrder.Depth).IsEqualTo(0);
    }

    [Test]
    public async Task TheTransactionLevel_IsTheOnlyLevelTheStoreMayRunUnder()
    {
        // Staging without the gate and the state lock would be the old, deadlocking order.
        var refused = false;
        try
        {
            CashShopPurchaseLocking.EnterTransaction();
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }

        await Assert.That(refused).IsTrue();
        await Assert.That(CashShopLockOrder.Depth).IsEqualTo(0);
    }

    [Test]
    public async Task ReleasingOutOfOrder_IsRefused()
    {
        CashShopLockOrder.Enter(CashShopLockLevel.PersistenceGate);
        CashShopLockOrder.Enter(CashShopLockLevel.CharacterState);
        var refused = false;
        try
        {
            CashShopLockOrder.Exit(CashShopLockLevel.PersistenceGate);
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }

        await Assert.That(refused).IsTrue();
        CashShopLockOrder.Exit(CashShopLockLevel.CharacterState);
        CashShopLockOrder.Exit(CashShopLockLevel.PersistenceGate);
        await Assert.That(CashShopLockOrder.Depth).IsEqualTo(0);
    }

    [Test]
    public async Task MonitorLockScope_HoldsTheMonitorForTheWholeBody()
    {
        var syncRoot = new object();
        using (MonitorLockScope.Enter(syncRoot))
        {
            await Assert.That(Monitor.IsEntered(syncRoot)).IsTrue();
        }

        var freeAfterDispose = Monitor.TryEnter(syncRoot);
        if (freeAfterDispose)
            Monitor.Exit(syncRoot);
        await Assert.That(freeAfterDispose).IsTrue();
    }

    private static IDisposable Scope(CashShopLockLevel level, List<CashShopLockLevel> acquired,
        List<CashShopLockLevel> released)
    {
        // The tracker itself is driven by ExecuteInOrder; this scope only records and releases the stand-in lock.
        acquired.Add(level);
        return new DelegateScope(() => released.Add(level));
    }

    private sealed class DelegateScope(Action onDispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            onDispose();
        }
    }
}
