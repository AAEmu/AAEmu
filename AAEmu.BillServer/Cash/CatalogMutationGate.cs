namespace AAEmu.BillServer.Cash;

/// <summary>Serializes catalog admin mutations (upsert / fill-names / publish).</summary>
public sealed class CatalogMutationGate
{
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private int _activeMutations;
    private volatile bool _shuttingDown;

    public bool IsShuttingDown => _shuttingDown;
    public bool IsBusy => Volatile.Read(ref _activeMutations) > 0;

    public void BeginShutdown() => _shuttingDown = true;

    public async Task<CatalogMutationLease?> TryEnterAsync(CancellationToken cancellationToken = default)
    {
        if (_shuttingDown)
            return null;

        if (!await _mutex.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken))
            return null;

        if (_shuttingDown)
        {
            _mutex.Release();
            return null;
        }

        Interlocked.Increment(ref _activeMutations);
        return new CatalogMutationLease(this);
    }

    internal void ReleaseLease()
    {
        Interlocked.Decrement(ref _activeMutations);
        _mutex.Release();
    }

    public async Task WaitForIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (IsBusy && DateTime.UtcNow < deadline)
            await Task.Delay(50, cancellationToken);
    }

    public sealed class CatalogMutationLease(CatalogMutationGate gate) : IDisposable
    {
        private CatalogMutationGate? _gate = gate;

        public void Dispose()
        {
            var g = Interlocked.Exchange(ref _gate, null);
            g?.ReleaseLease();
        }
    }
}
