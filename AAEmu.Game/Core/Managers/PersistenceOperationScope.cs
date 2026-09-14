namespace AAEmu.Game.Core.Managers;

internal sealed class PersistenceOperationScope : IDisposable
{
    private readonly bool _ownsGate;
    private bool _disposed;

    private PersistenceOperationScope(bool ownsGate) => _ownsGate = ownsGate;

    public bool OwnsGate => _ownsGate;

    public static PersistenceOperationScope Enter()
    {
        var ownsGate = !PersistenceGate.IsOperationHeld && !PersistenceGate.IsSaveHeld;
        if (ownsGate)
            PersistenceGate.EnterOperation();
        return new PersistenceOperationScope(ownsGate);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsGate)
            PersistenceGate.ExitOperation();
    }
}
