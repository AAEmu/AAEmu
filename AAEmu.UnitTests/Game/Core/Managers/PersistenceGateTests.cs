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

    /// <summary>
    /// A save asked for from inside a live operation cannot take the gate: the reader already held here
    /// forbids the recursive write, so the request has to be refused. <c>TryEnter</c> reports that refusal
    /// instead of throwing, which lets a caller with its own failure path (a character save) treat it as the
    /// ordinary unsuccessful outcome it is rather than losing the save to an escaping exception.
    /// </summary>
    [Test]
    public async Task TryEnterSave_InsideAHeldOperation_RefusesWithoutThrowingAndLeavesTheOperationIntact()
    {
        bool entered;
        bool scopeReturnedNull;
        bool operationStillHeld;
        bool saveTakenAfterwards;
        bool operationHeldAfterRelease;

        PersistenceGate.EnterOperation();
        try
        {
            entered = PersistenceSaveScope.TryEnter(out var scope);
            scopeReturnedNull = scope is null;
            operationStillHeld = PersistenceGate.IsOperationHeld;
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        // The refusal must not have poisoned the gate: a save taken afterwards still succeeds.
        saveTakenAfterwards = PersistenceSaveScope.TryEnter(out var taken);
        try
        {
            operationHeldAfterRelease = PersistenceGate.IsSaveHeld;
        }
        finally
        {
            taken?.Dispose();
        }

        await Assert.That(entered).IsFalse();
        await Assert.That(scopeReturnedNull).IsTrue();
        await Assert.That(operationStillHeld).IsTrue();
        await Assert.That(saveTakenAfterwards).IsTrue();
        await Assert.That(operationHeldAfterRelease).IsTrue();
    }

    /// <summary>
    /// The free-thread case still takes the gate, and a save already running on this thread reports a
    /// non-owning scope so the nested request does not try to release a lock it never took.
    /// </summary>
    [Test]
    public async Task TryEnterSave_OnAFreeThread_TakesTheGateAndReleasesIt()
    {
        bool entered;
        bool saveHeldWhileTaken;
        bool saveHeldAfterRelease;

        entered = PersistenceSaveScope.TryEnter(out var scope);
        try
        {
            saveHeldWhileTaken = PersistenceGate.IsSaveHeld;
        }
        finally
        {
            scope?.Dispose();
        }

        saveHeldAfterRelease = PersistenceGate.IsSaveHeld;

        await Assert.That(entered).IsTrue();
        await Assert.That(scope).IsNotNull();
        await Assert.That(saveHeldWhileTaken).IsTrue();
        await Assert.That(saveHeldAfterRelease).IsFalse();
    }

    [Test]
    public async Task TryEnterSave_InsideAnAlreadyHeldSave_ReportsANonOwningScope()
    {
        bool outerEntered;
        bool nestedEntered;
        bool nestedOwnsGate;
        bool saveHeldAfterNestedScopeClosed;

        outerEntered = PersistenceSaveScope.TryEnter(out var outer);
        try
        {
            nestedEntered = PersistenceSaveScope.TryEnter(out var nested);
            nestedOwnsGate = nested is not null;
            nested?.Dispose();
            saveHeldAfterNestedScopeClosed = PersistenceGate.IsSaveHeld;
        }
        finally
        {
            outer?.Dispose();
        }

        await Assert.That(outerEntered).IsTrue();
        await Assert.That(nestedEntered).IsTrue();
        await Assert.That(nestedOwnsGate).IsTrue();
        // Disposing the nested scope released nothing, so the outer save is still holding the gate.
        await Assert.That(saveHeldAfterNestedScopeClosed).IsTrue();
        await Assert.That(PersistenceGate.IsSaveHeld).IsFalse();
    }

    /// <summary>
    /// The throwing form still refuses an inside-operation save, so the two entry points cannot drift.
    /// </summary>
    [Test]
    public async Task EnterSave_InsideAHeldOperation_Throws()
    {
        var threw = false;

        PersistenceGate.EnterOperation();
        try
        {
            try
            {
                PersistenceSaveScope.Enter();
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        await Assert.That(threw).IsTrue();
    }

    /// <summary>
    /// A character save requested while a money operation owns the gate reports a failed save instead of
    /// throwing. The logout path reaches the save after the character has already left the world, so an
    /// escaping exception there would drop the save with nothing logged and nothing to retry against.
    /// </summary>
    [Test]
    public async Task CharacterSave_InsideAHeldOperation_ReturnsFalseInsteadOfThrowing()
    {
        var character = new AAEmu.UnitTests.Utils.Mocks.CharacterMock();
        var saved = false;
        var threw = false;

        PersistenceGate.EnterOperation();
        try
        {
            saved = character.SaveDirectlyToDatabase();
        }
        catch
        {
            threw = true;
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        await Assert.That(threw).IsFalse();
        await Assert.That(saved).IsFalse();
        // The refusal released nothing it did not take.
        await Assert.That(PersistenceGate.IsOperationHeld).IsFalse();
        await Assert.That(PersistenceGate.IsSaveHeld).IsFalse();
    }
}
