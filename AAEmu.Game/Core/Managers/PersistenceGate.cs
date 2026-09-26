namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Keeps a World save from snapshotting a money operation halfway through.
///
/// An operation (player mail with coin, a bid, a buyout settle, an expiry sweep) holds the
/// gate shared for its whole duration; a save holds it exclusively. A save therefore begins
/// only after every operation that was in flight has finished, and no operation starts while
/// the snapshot is being read. Deferring the operation's own save request
/// (<see cref="MailManager.DeferPersist"/>) is not enough on its own: the save tick, or another
/// player's letter, can still ask for a snapshot from a different thread in the middle.
///
/// Because of this ordering, a save that is already running when an operation asks to flush
/// necessarily started after that operation completed and carries its state, so the busy
/// answer from <see cref="ISaveManager.DoSave"/> is a safe skip rather than a lost write.
/// </summary>
public static class PersistenceGate
{
    private static readonly ReaderWriterLockSlim Gate = new(LockRecursionPolicy.NoRecursion);

    /// <summary>Marks the start of a money operation on this thread. Blocks while a save is reading.</summary>
    public static void EnterOperation() => Gate.EnterReadLock();

    /// <summary>
    /// Enters the operation side only when this thread does not already hold it, and reports whether this call
    /// was the one that took it.
    /// </summary>
    /// <remarks>
    /// The gate forbids recursive reads, and several paths reach it in layers: a house build opens an operation
    /// scope and then consumes the design item from the bag, and an inventory mutation opens a mail-persistence
    /// deferral of its own. A caller that may be nested inside another operation uses this instead of
    /// <see cref="EnterOperation"/>, and releases only when it returned true - the same ownership rule
    /// <see cref="PersistenceOperationScope"/> follows.
    /// </remarks>
    public static bool TryEnterOperation()
    {
        if (Gate.IsReadLockHeld || Gate.IsWriteLockHeld)
            return false;

        Gate.EnterReadLock();
        return true;
    }

    public static void ExitOperation() => Gate.ExitReadLock();

    /// <summary>Marks the start of a snapshot. Blocks until every in-flight operation has finished.</summary>
    public static void EnterSave() => Gate.EnterWriteLock();

    public static void ExitSave() => Gate.ExitWriteLock();

    /// <summary>True while this thread is inside an operation.</summary>
    public static bool IsOperationHeld => Gate.IsReadLockHeld;

    /// <summary>True while this thread is taking a snapshot.</summary>
    public static bool IsSaveHeld => Gate.IsWriteLockHeld;
}

internal sealed class PersistenceSaveScope : IDisposable
{
    private readonly bool _ownsGate;
    private bool _disposed;

    private PersistenceSaveScope(bool ownsGate) => _ownsGate = ownsGate;

    public static PersistenceSaveScope Enter()
    {
        if (!TryEnter(out var scope))
            throw new InvalidOperationException("A persistence save cannot start inside a live operation.");
        return scope!;
    }

    /// <summary>
    /// Takes the save side of the gate, or reports that it cannot. <paramref name="scope"/> is never null when
    /// this returns true, and is null when the caller is already inside a live operation on this thread and so
    /// must not start a snapshot.
    /// </summary>
    /// <remarks>
    /// A caller that has to treat that refusal as an ordinary, recoverable outcome (a character save, say, which
    /// already reports failure by returning false) uses this rather than <see cref="Enter"/>: the throwing form
    /// would abandon the caller's own error handling and let the refusal escape as an unrelated-looking
    /// exception.
    /// </remarks>
    public static bool TryEnter(out PersistenceSaveScope? scope)
    {
        if (PersistenceGate.IsSaveHeld)
        {
            scope = new PersistenceSaveScope(false);
            return true;
        }
        if (PersistenceGate.IsOperationHeld)
        {
            scope = null;
            return false;
        }

        PersistenceGate.EnterSave();
        scope = new PersistenceSaveScope(true);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsGate)
            PersistenceGate.ExitSave();
    }
}
