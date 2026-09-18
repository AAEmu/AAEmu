using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The operation side of <see cref="PersistenceGate"/> when it is reached in layers — a house build opens an
/// operation scope and then consumes the design item, whose inventory mutation opens a mail-persistence
/// deferral of its own. The lock forbids recursive reads, so the inner layer must take nothing and release
/// nothing; before <see cref="PersistenceGate.TryEnterOperation"/> the second entry threw and the whole packet
/// was abandoned (a house placement that silently did nothing).
/// The gate is thread-bound, so nothing here awaits while it is held: an awaited assertion may resume on a
/// pool thread, and the release would then throw on a thread that never took it. Every fact is read inside
/// the lock and asserted after it is released.
/// </summary>
public class PersistenceGateTests
{
    [Test]
    public async Task TryEnterOperation_OnAFreeThread_TakesTheGate()
    {
        bool heldWhileTaken;
        bool heldAfterRelease;

        var took = PersistenceGate.TryEnterOperation();
        try
        {
            heldWhileTaken = PersistenceGate.IsOperationHeld;
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        heldAfterRelease = PersistenceGate.IsOperationHeld;

        await Assert.That(took).IsTrue();
        await Assert.That(heldWhileTaken).IsTrue();
        await Assert.That(heldAfterRelease).IsFalse();
    }

    [Test]
    public async Task TryEnterOperation_InsideAHeldGate_TakesNothingAndDoesNotThrow()
    {
        bool took;
        bool heldByTheOuterScope;
        bool heldAfterRelease;

        PersistenceGate.EnterOperation();
        try
        {
            took = PersistenceGate.TryEnterOperation();
            // The outer holder still owns it: the inner call neither took a second read nor released the first.
            heldByTheOuterScope = PersistenceGate.IsOperationHeld;
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        heldAfterRelease = PersistenceGate.IsOperationHeld;

        await Assert.That(took).IsFalse();
        await Assert.That(heldByTheOuterScope).IsTrue();
        await Assert.That(heldAfterRelease).IsFalse();
    }

    [Test]
    public async Task TryEnterOperation_InNestedOperationScope_LeavesTheReleaseToTheOuterOne()
    {
        bool outerOwnsGate;
        bool innerOwnsGate;
        bool heldWhileTheInnerScopeIsOpen;
        bool heldAfterTheInnerScopeClosed;
        bool heldAfterTheOuterScopeClosed;

        using (var outer = PersistenceOperationScope.Enter())
        {
            outerOwnsGate = outer.OwnsGate;

            using (var inner = PersistenceOperationScope.Enter())
            {
                innerOwnsGate = inner.OwnsGate;
                heldWhileTheInnerScopeIsOpen = PersistenceGate.IsOperationHeld;
            }

            // The inner scope returned without releasing what the outer one took.
            heldAfterTheInnerScopeClosed = PersistenceGate.IsOperationHeld;
        }

        heldAfterTheOuterScopeClosed = PersistenceGate.IsOperationHeld;

        await Assert.That(outerOwnsGate).IsTrue();
        await Assert.That(innerOwnsGate).IsFalse();
        await Assert.That(heldWhileTheInnerScopeIsOpen).IsTrue();
        await Assert.That(heldAfterTheInnerScopeClosed).IsTrue();
        await Assert.That(heldAfterTheOuterScopeClosed).IsFalse();
    }
}
