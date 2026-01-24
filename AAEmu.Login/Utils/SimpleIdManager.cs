using System.Collections.Concurrent;

namespace AAEmu.Login.Utils;

public sealed class SimpleIdManager<T>(Func<uint, T> factory, Func<T, uint> accessor) : IIdManager<T>
{
    private uint _nextId;

    // This may have almost-unbounded growth when under heavy load or DDoS; ideally rate limiting would be applied at a higher level
    private readonly ConcurrentStack<uint> _freeIds = new();

    public T Rent()
    {
        // Try to reuse a freed ID first
        if (_freeIds.TryPop(out var id))
            return factory(id);

        // Otherwise, generate a new ID atomically
        var newId = Interlocked.Increment(ref _nextId);

        // Guard against overflow
        return newId <= 0 ? throw new InvalidOperationException("ID space exhausted.") : factory(newId);
    }

    public void Return(T id)
    {
        var value = accessor(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

        _freeIds.Push(value);
    }
}
