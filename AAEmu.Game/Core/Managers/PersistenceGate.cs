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

    public static void ExitOperation() => Gate.ExitReadLock();

    /// <summary>Marks the start of a snapshot. Blocks until every in-flight operation has finished.</summary>
    public static void EnterSave() => Gate.EnterWriteLock();

    public static void ExitSave() => Gate.ExitWriteLock();

    /// <summary>True while this thread is inside an operation.</summary>
    public static bool IsOperationHeld => Gate.IsReadLockHeld;

    /// <summary>True while this thread is taking a snapshot.</summary>
    public static bool IsSaveHeld => Gate.IsWriteLockHeld;
}
