using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Tasks.Doodads;
using AAEmu.Game.Scripts.Commands;

namespace AAEmu.UnitTests.Game.Scripts.Commands;

public class TickDoodadTests
{
    [Test]
    public async Task WakeCurrentScheduledTask_LeavesExecutionToTheSchedulerAndRunsOnce()
    {
        var tickHandler = new TickManager.TickEventHandler();
        var tickManager = Mock.Of<ITickManager>();
        tickManager.OnTick.Returns(tickHandler);
        var taskManager = new TaskManager(tickManager.Object);
        taskManager.Start();

        var doodad = new Doodad();
        var executed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = new CountingDoodadFuncTask(doodad, executed);
        doodad.FuncTask = task;
        taskManager.Schedule(task, TimeSpan.FromHours(1));

        try
        {
            var wakeTime = DateTime.UtcNow.AddSeconds(-1);
            var woke = TickDoodad.TryWakeCurrentTask(doodad, wakeTime);

            await Assert.That(woke).IsTrue();
            await Assert.That(task.TriggerTime).IsEqualTo(wakeTime);
            await Assert.That(task.ExecutionCount).IsEqualTo(0);

            tickHandler.Invoke();
            await executed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var dequeued = await WaitUntilAsync(
                () => taskManager.GetQueueCount() == 0,
                TimeSpan.FromSeconds(2));

            await Assert.That(dequeued).IsTrue();
            await Assert.That(task.ExecutionCount).IsEqualTo(1);
            await Assert.That(task.ExecuteCount).IsEqualTo(1);

            await Task.Delay(60);
            tickHandler.Invoke();
            await Task.Delay(60);
            await Assert.That(task.ExecutionCount).IsEqualTo(1);
        }
        finally
        {
            await taskManager.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task WakeCurrentTask_DoesNotReviveCancelledOrTouchStaleTask()
    {
        var doodad = new Doodad();
        var cancelled = new CountingDoodadFuncTask(doodad, new TaskCompletionSource<bool>())
        {
            Cancelled = true,
            TriggerTime = DateTime.UtcNow.AddHours(1)
        };
        doodad.FuncTask = cancelled;
        var cancelledTrigger = cancelled.TriggerTime;

        var cancelledWoke = TickDoodad.TryWakeCurrentTask(doodad, DateTime.UtcNow);

        await Assert.That(cancelledWoke).IsFalse();
        await Assert.That(cancelled.TriggerTime).IsEqualTo(cancelledTrigger);

        var stale = new CountingDoodadFuncTask(doodad, new TaskCompletionSource<bool>())
        {
            TriggerTime = DateTime.UtcNow.AddHours(2)
        };
        var current = new CountingDoodadFuncTask(doodad, new TaskCompletionSource<bool>())
        {
            TriggerTime = DateTime.UtcNow.AddHours(3)
        };
        doodad.FuncTask = current;
        var staleTrigger = stale.TriggerTime;
        var wakeTime = DateTime.UtcNow;

        var currentWoke = TickDoodad.TryWakeCurrentTask(doodad, wakeTime);

        await Assert.That(currentWoke).IsTrue();
        await Assert.That(current.TriggerTime).IsEqualTo(wakeTime);
        await Assert.That(stale.TriggerTime).IsEqualTo(staleTrigger);
        await Assert.That(cancelled.Cancelled).IsTrue();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(10);
        }

        return condition();
    }

    private sealed class CountingDoodadFuncTask : DoodadFuncTask
    {
        private readonly Doodad _owner;
        private readonly TaskCompletionSource<bool> _executed;

        public CountingDoodadFuncTask(Doodad owner, TaskCompletionSource<bool> executed)
            : base(null, owner, 0)
        {
            _owner = owner;
            _executed = executed;
        }

        public int ExecutionCount { get; private set; }

        public override void Execute()
        {
            ExecuteIfCurrent(() =>
            {
                ExecutionCount++;
                _owner.FuncTask = null;
                _executed.TrySetResult(true);
            });
        }
    }
}
