using AAEmu.Game.Core.Managers;
using GameTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class TaskManagerTests
{
    // Test task implementation for testing
    private sealed class TestTask : GameTask
    {
        public bool WasExecuted { get; private set; }
        public int ExecuteCallCount { get; private set; }

        public override void Execute()
        {
            WasExecuted = true;
            ExecuteCallCount++;
        }
    }

    #region Basic Tests

    [Test]
    public void Start_SubscribesToTickManager()
    {
        var mockTick = Mock.Of<ITickManager>();
        var handler = new TickManager.TickEventHandler();
        mockTick.OnTick.Returns(handler);

        var manager = new TaskManager(mockTick.Object);
        manager.Start();

        mockTick.OnTick.WasCalled(Times.Once);
    }

    [Test]
    public async Task Schedule_ReturnsTrue_WhenTaskIsQueued()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();

        var result = manager.Schedule(task, TimeSpan.FromSeconds(60));

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Cancel_ReturnsFalse_WhenTaskNotInQueue()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();

        var result = manager.Cancel(task);

        await Assert.That(result).IsFalse();
    }

    #endregion

    #region CRON Schedule Tests

    [Test]
    public async Task CronSchedule_ReturnsTrue_WhenValidCronExpression()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();

        var result = manager.CronSchedule(task, "* * * * * *"); // Every second

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CronSchedule_SetsCronSchedule_WhenValidExpression()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        var result = manager.CronSchedule(testTask, "*/5 * * * * *");

        await Assert.That(result).IsTrue();
        await Assert.That(testTask.CronSchedule).IsNotNull();
    }

    [Test]
    public async Task CronSchedule_ReturnsTrue_WithStartDelay()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();

        var result = manager.CronSchedule(task, "* * * * * *", TimeSpan.FromMinutes(5));

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CronSchedule_ReturnsTrue_WithCount()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();

        var result = manager.CronSchedule(task, "* * * * * *", null, 5);

        await Assert.That(result).IsTrue();
    }

    #endregion

    #region Cancel Tests

    [Test]
    public async Task Cancel_ReturnsTrue_WhenTaskIsInQueue()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();
        task.Id = 1; // Set ID manually since Schedule will assign it

        // Manually add task to queue for testing
        var queueField = typeof(TaskManager).GetField("_queue",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var queue = (System.Collections.Concurrent.ConcurrentDictionary<uint, GameTask>)queueField!.GetValue(manager)!;
        queue.TryAdd(1, task);

        var result = manager.Cancel(task);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Cancel_SetsCancelledFlag_WhenTaskIsCancelled()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();
        task.Id = 1;

        // Add task to queue
        var queueField = typeof(TaskManager).GetField("_queue",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var queue = (System.Collections.Concurrent.ConcurrentDictionary<uint, GameTask>)queueField!.GetValue(manager)!;
        queue.TryAdd(1, task);

        manager.Cancel(task);

        await Assert.That(task.Cancelled).IsTrue();
    }

    [Test]
    public async Task Cancel_ReturnsFalse_ForAlreadyCancelledTask()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();
        task.Id = 1;
        task.Cancelled = true; // Already cancelled

        var result = manager.Cancel(task);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Cancel_RemovesTaskFromQueue()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();
        task.Id = 1;

        // Add task to queue
        var queueField = typeof(TaskManager).GetField("_queue",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var queue = (System.Collections.Concurrent.ConcurrentDictionary<uint, GameTask>)queueField!.GetValue(manager)!;
        queue.TryAdd(1, task);

        await Assert.That(queue).HasSingleItem();

        manager.Cancel(task);

        await Assert.That(queue).IsEmpty();
    }

    #endregion

    #region Repeat Task Tests

    [Test]
    public async Task Schedule_SetsRepeatCount_WhenRepeatIntervalProvided()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        manager.Schedule(testTask, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), 3);

        await Assert.That(testTask.RepeatInterval).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(testTask.RepeatCount).IsEqualTo(3);
    }

    [Test]
    public async Task Schedule_DefaultRepeatCount_WhenNoInterval()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        manager.Schedule(testTask, TimeSpan.FromSeconds(10));

        await Assert.That(testTask.RepeatCount).IsEqualTo(1);
    }

    [Test]
    public async Task Schedule_ReturnsTrue_WithInfiniteRepeatCount()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();

        var result = manager.Schedule(task, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), -1);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Schedule_ReturnsTrue_WithZeroRepeatCount()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();

        var result = manager.Schedule(task, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), 0);

        await Assert.That(result).IsTrue();
    }

    #endregion

    #region Execute Tests

    [Test]
    public async Task Schedule_ExecutesImmediately_WhenZeroDelayAndCountOne()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        var result = manager.Schedule(testTask, TimeSpan.Zero, null, 1);

        await Assert.That(result).IsTrue();
        await Assert.That(testTask.WasExecuted).IsTrue();
    }

    [Test]
    public async Task Schedule_ExecutesImmediately_WhenZeroDelayAndZeroCount()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        var result = manager.Schedule(testTask, TimeSpan.Zero, null, 0);

        await Assert.That(result).IsTrue();
        await Assert.That(testTask.WasExecuted).IsTrue();
    }

    [Test]
    public async Task CronSchedule_ExecutesImmediately_WhenZeroDelay()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        var result = manager.CronSchedule(testTask, "* * * * * *", TimeSpan.Zero);

        await Assert.That(result).IsTrue();
        await Assert.That(testTask.WasExecuted).IsTrue();
    }

    #endregion

    #region Edge Case Tests

    [Test]
    public void Schedule_ThrowsException_WhenTaskIsNull()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);

        // Currently it throws NullReferenceException when task is null
        Assert.Throws<NullReferenceException>(() => manager.Schedule(null!, TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void CronSchedule_ThrowsException_WhenCronExpressionIsNull()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();

        Assert.Throws<ArgumentNullException>(() => manager.CronSchedule(task, null!));
    }

    [Test]
    public void CronSchedule_ThrowsException_WhenInvalidCronExpression()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();

        Assert.Throws<NCrontab.CrontabException>(() => manager.CronSchedule(task, "invalid-cron"));
    }

    [Test]
    public async Task GetQueueCount_ReturnsZero_WhenNoTasks()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);

        var count = manager.GetQueueCount();

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task GetQueueCount_ReturnsCorrectCount_AfterSchedulingTasks()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task1 = new TestTask();
        var task2 = new TestTask();

        manager.Schedule(task1, TimeSpan.FromSeconds(60));
        manager.Schedule(task2, TimeSpan.FromSeconds(60));

        var count = manager.GetQueueCount();

        await Assert.That(count).IsEqualTo(2);
    }

    #endregion

    #region Periodic Task Tests

    [Test]
    public async Task Schedule_WithPeriodicInterval_QueuesTask()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = new TestTask();

        var result = manager.Schedule(task, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), 5);

        await Assert.That(result).IsTrue();
        await Assert.That(manager.GetQueueCount()).IsEqualTo(1);
    }

    [Test]
    public async Task Schedule_WithVeryLongDelay_SetsCorrectTriggerTime()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        var startDelay = TimeSpan.FromHours(24);
        manager.Schedule(testTask, startDelay, TimeSpan.FromHours(1), 10);

        // Trigger time should be approximately now + 24 hours
        var expectedMin = DateTime.UtcNow + startDelay - TimeSpan.FromSeconds(1);
        var expectedMax = DateTime.UtcNow + startDelay + TimeSpan.FromSeconds(1);
        await Assert.That(testTask.TriggerTime >= expectedMin && testTask.TriggerTime <= expectedMax).IsTrue();
    }

    #endregion

    #region Restart/Initialize Tests

    [Test]
    public async Task Initialize_PreservesQueue()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);

        var task = new TestTask();
        manager.Schedule(task, TimeSpan.FromSeconds(60));
        await Assert.That(manager.GetQueueCount()).IsEqualTo(1);

        // Initialize() is intentionally a no-op for the queue: in the orchestrated
        // boot, ILoadable.Load() runs in Stage 2 and may schedule tasks (e.g.
        // ZoneManager queues the initial Conflict→War→Peace timer chain) before
        // Initialize() runs in Stage 4. Clearing the queue here used to wipe those
        // boot-time tasks and break the zone-conflict cycle.
        manager.Initialize();

        await Assert.That(manager.GetQueueCount()).IsEqualTo(1);
    }

    [Test]
    public void Start_CanBeCalledMultipleTimes()
    {
        var mockTick = Mock.Of<ITickManager>();
        var handler = new TickManager.TickEventHandler();
        mockTick.OnTick.Returns(handler);

        var manager = new TaskManager(mockTick.Object);

        // Call Start multiple times
        manager.Start();
        manager.Start();
        manager.Start();

        // Each call to Start subscribes to OnTick
        // So we expect the property to be accessed 3 times
        mockTick.OnTick.WasCalled(Times.Exactly(3));
    }

    #endregion

    #region Task ID Tests

    [Test]
    public async Task Schedule_AssignsUniqueIds_ToMultipleTasks()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task1 = new TestTask();
        var task2 = new TestTask();
        var task3 = new TestTask();

        manager.Schedule(task1, TimeSpan.FromSeconds(60));
        manager.Schedule(task2, TimeSpan.FromSeconds(60));
        manager.Schedule(task3, TimeSpan.FromSeconds(60));

        // All tasks should have unique IDs
        await Assert.That(task2.Id).IsNotEqualTo(task1.Id);
        await Assert.That(task3.Id).IsNotEqualTo(task2.Id);
        await Assert.That(task3.Id).IsNotEqualTo(task1.Id);
    }

    #endregion

    #region RemoveTasks Tests

    [Test]
    public async Task RemoveTasks_RemovesMatchingTasks()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task1 = new TestTask();
        var task2 = new TestTask();
        var task3 = new TestTask();

        manager.Schedule(task1, TimeSpan.FromSeconds(60));
        manager.Schedule(task2, TimeSpan.FromSeconds(60));
        manager.Schedule(task3, TimeSpan.FromSeconds(60));

        await Assert.That(manager.GetQueueCount()).IsEqualTo(3);

        // Remove tasks with ID > 1
        manager.RemoveTasks(t => t.Id > 1);

        await Assert.That(manager.GetQueueCount()).IsEqualTo(1);
    }

    [Test]
    public async Task RemoveTasks_RemovesAll_WhenPredicateMatchesAll()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task1 = new TestTask();
        var task2 = new TestTask();

        manager.Schedule(task1, TimeSpan.FromSeconds(60));
        manager.Schedule(task2, TimeSpan.FromSeconds(60));

        await Assert.That(manager.GetQueueCount()).IsEqualTo(2);

        manager.RemoveTasks(t => true);

        await Assert.That(manager.GetQueueCount()).IsEqualTo(0);
    }

    #endregion
}