using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

[NotInParallel]
public class DoodadItemPersistenceTests
{
    [Test]
    public async Task PlacementFailure_AbandonsInitializedDoodadBeforeRestoringItem()
    {
        var taskManager = new TaskManager(Mock.Of<ITickManager>().Object);
        using var taskScope = new SingletonScope<TaskManager>(taskManager);
        var doodad = new Doodad { ObjId = 40, ItemId = 50, ItemTemplateId = 60 };
        var task = new TestDoodadFuncTask(doodad);
        var initializedWhilePending = false;
        var persistedWhilePending = false;
        var rollbackCalled = false;
        uint releasedObjectId = 0;

        var committed = DoodadItemPersistence.TryInitializeAndPersistPlacement(
            doodad,
            () =>
            {
                initializedWhilePending = doodad.IsPlacementPending && doodad.IsPersistent && Monitor.IsEntered(doodad);
                doodad.FuncTask = task;
                taskManager.Schedule(task, TimeSpan.FromHours(1));
            },
            () =>
            {
                persistedWhilePending = doodad.IsPlacementPending && doodad.IsPersistent && Monitor.IsEntered(doodad);
                return false;
            },
            () => rollbackCalled = true,
            objectId => releasedObjectId = objectId);

        await Assert.That(committed).IsFalse();
        await Assert.That(initializedWhilePending).IsTrue();
        await Assert.That(persistedWhilePending).IsTrue();
        await Assert.That(rollbackCalled).IsTrue();
        await Assert.That(doodad.IsPersistent).IsFalse();
        await Assert.That(doodad.ItemId).IsEqualTo(0ul);
        await Assert.That(doodad.ItemTemplateId).IsEqualTo(0u);
        await Assert.That(doodad.FuncTask).IsNull();
        await Assert.That(task.Cancelled).IsTrue();
        await Assert.That(releasedObjectId).IsEqualTo(doodad.ObjId);
    }

    [Test]
    public async Task PlacementSuccess_ClearsPendingOnlyAfterPersistence()
    {
        var doodad = new Doodad { ObjId = 40, ItemId = 50, ItemTemplateId = 60 };
        var initializedWhilePending = false;
        var persistedWhilePending = false;
        var rollbackCalled = false;
        var releasedObjectId = false;

        var committed = DoodadItemPersistence.TryInitializeAndPersistPlacement(
            doodad,
            () => initializedWhilePending = doodad.IsPlacementPending && doodad.IsPersistent && Monitor.IsEntered(doodad),
            () =>
            {
                persistedWhilePending = doodad.IsPlacementPending && doodad.IsPersistent && Monitor.IsEntered(doodad);
                return true;
            },
            () => rollbackCalled = true,
            _ => releasedObjectId = true);

        await Assert.That(committed).IsTrue();
        await Assert.That(initializedWhilePending).IsTrue();
        await Assert.That(persistedWhilePending).IsTrue();
        await Assert.That(doodad.IsPlacementPending).IsFalse();
        await Assert.That(doodad.IsPersistent).IsTrue();
        await Assert.That(doodad.ItemId).IsEqualTo(50ul);
        await Assert.That(doodad.ItemTemplateId).IsEqualTo(60u);
        await Assert.That(rollbackCalled).IsFalse();
        await Assert.That(releasedObjectId).IsFalse();
    }

    private sealed class TestDoodadFuncTask(Doodad owner) : DoodadFuncTask(null, owner, 0)
    {
        public override void Execute()
        {
        }
    }

    private sealed class SingletonScope<T> : IDisposable where T : class
    {
        private readonly FieldInfo _field = typeof(Singleton<T>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;

        public SingletonScope(T value)
        {
            _previous = _field.GetValue(null);
            _field.SetValue(null, value);
        }

        public void Dispose() => _field.SetValue(null, _previous);
    }
}
