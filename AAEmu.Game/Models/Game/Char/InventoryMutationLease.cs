namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Holds a character inventory's mutation monitor. The monitor is re-entrant so existing
/// container helpers can participate without requiring callers to know their nesting.
/// </summary>
public sealed class InventoryMutationLease : IDisposable
{
    private readonly object _syncRoot;
    private readonly Action _onDispose;
    private int _disposed;

    internal InventoryMutationLease(object syncRoot, Action onDispose = null)
    {
        _syncRoot = syncRoot;
        _onDispose = onDispose;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Monitor.Exit(_syncRoot);
        _onDispose?.Invoke();
    }
}

/// <summary>
/// Marks one synchronous skill-effect application without holding the inventory mutation monitor.
/// </summary>
internal sealed class InventorySkillEffectLease : IDisposable
{
    private readonly Action _onDispose;
    private int _disposed;

    internal InventorySkillEffectLease(Action onDispose)
    {
        _onDispose = onDispose;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _onDispose();
    }
}

internal sealed class InventoryMutationGroupLease : IDisposable
{
    private readonly object[] _syncRoots;
    private readonly IDisposable _persistenceScope;
    private int _disposed;

    internal InventoryMutationGroupLease(object[] syncRoots, IDisposable persistenceScope)
    {
        _syncRoots = syncRoots;
        _persistenceScope = persistenceScope;
        var acquired = 0;
        try
        {
            foreach (var syncRoot in _syncRoots)
            {
                Monitor.Enter(syncRoot);
                acquired++;
            }
        }
        catch
        {
            for (var index = acquired - 1; index >= 0; index--)
                Monitor.Exit(_syncRoots[index]);
            _persistenceScope?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        for (var index = _syncRoots.Length - 1; index >= 0; index--)
            Monitor.Exit(_syncRoots[index]);
        _persistenceScope?.Dispose();
    }
}
