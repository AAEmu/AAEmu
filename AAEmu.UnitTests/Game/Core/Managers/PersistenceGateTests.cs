using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The operation side of <see cref="PersistenceGate"/> when it is reached in layers — a house build opens an
/// operation scope and then consumes the design item, whose inventory mutation opens a mail-persistence
/// deferral of its own. The lock forbids recursive reads, so the inner layer must take nothing and release
/// nothing; before <see cref="PersistenceGate.TryEnterOperation"/> the second entry threw and the whole packet
/// was abandoned (a house placement that silently did nothing).
/// </summary>
public class PersistenceGateTests
{
    [Test]
    public async Task TryEnterOperation_OnAFreeThread_TakesTheGate()
    {
        var took = PersistenceGate.TryEnterOperation();
        try
        {
            await Assert.That(took).IsTrue();
            await Assert.That(PersistenceGate.IsOperationHeld).IsTrue();
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        await Assert.That(PersistenceGate.IsOperationHeld).IsFalse();
    }

    [Test]
    public async Task TryEnterOperation_InsideAHeldGate_TakesNothingAndDoesNotThrow()
    {
        PersistenceGate.EnterOperation();
        try
        {
            var took = PersistenceGate.TryEnterOperation();

            await Assert.That(took).IsFalse();
            // The outer holder still owns it: the inner call neither took a second read nor released the first.
            await Assert.That(PersistenceGate.IsOperationHeld).IsTrue();
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        await Assert.That(PersistenceGate.IsOperationHeld).IsFalse();
    }

    [Test]
    public async Task TryEnterOperation_InNestedOperationScope_LeavesTheReleaseToTheOuterOne()
    {
        using var outer = PersistenceOperationScope.Enter();
        await Assert.That(outer.OwnsGate).IsTrue();

        using (var inner = PersistenceOperationScope.Enter())
        {
            await Assert.That(inner.OwnsGate).IsFalse();
            await Assert.That(PersistenceGate.IsOperationHeld).IsTrue();
        }

        // The inner scope returned without releasing what the outer one took.
        await Assert.That(PersistenceGate.IsOperationHeld).IsTrue();

        outer.Dispose();
        await Assert.That(PersistenceGate.IsOperationHeld).IsFalse();
    }
}
