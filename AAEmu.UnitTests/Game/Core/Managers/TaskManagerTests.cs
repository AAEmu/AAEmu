using AAEmu.Game.Core.Managers;
using Moq;
using Xunit;
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

    [Fact]
    public void Start_SubscribesToTickManager()
    {
        var mockTick = new Mock<ITickManager>();
        var handler = new TickManager.TickEventHandler();
        mockTick.SetupGet(t => t.OnTick).Returns(handler);

        var manager = new TaskManager(mockTick.Object);
        manager.Start();

        mockTick.VerifyGet(t => t.OnTick, Times.Once);
    }

    [Fact]
    public void Schedule_ReturnsTrue_WhenTaskIsQueued()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();

        var result = manager.Schedule(task, TimeSpan.FromSeconds(60));

        Assert.True(result);
    }

    [Fact]
    public void Cancel_ReturnsFalse_WhenTaskNotInQueue()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();

        var result = manager.Cancel(task);

        Assert.False(result);
    }

    #endregion

    #region CRON Schedule Tests

    [Fact]
    public void CronSchedule_ReturnsTrue_WhenValidCronExpression()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();

        var result = manager.CronSchedule(task, "* * * * * *"); // Every second

        Assert.True(result);
    }

    [Fact]
    public void CronSchedule_SetsCronSchedule_WhenValidExpression()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        var result = manager.CronSchedule(testTask, "*/5 * * * * *");

        Assert.True(result);
        Assert.NotNull(testTask.CronSchedule);
    }

    [Fact]
    public void CronSchedule_ReturnsTrue_WithStartDelay()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();

        var result = manager.CronSchedule(task, "* * * * * *", TimeSpan.FromMinutes(5));

        Assert.True(result);
    }

    [Fact]
    public void CronSchedule_ReturnsTrue_WithCount()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();

        var result = manager.CronSchedule(task, "* * * * * *", null, 5);

        Assert.True(result);
    }

    #endregion

    #region Cancel Tests

    [Fact]
    public void Cancel_ReturnsTrue_WhenTaskIsInQueue()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();
        task.Id = 1; // Set ID manually since Schedule will assign it

        // Manually add task to queue for testing
        var queueField = typeof(TaskManager).GetField("_queue",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var queue = (System.Collections.Concurrent.ConcurrentDictionary<uint, GameTask>)queueField!.GetValue(manager)!;
        queue.TryAdd(1, task);

        var result = manager.Cancel(task);

        Assert.True(result);
    }

    [Fact]
    public void Cancel_SetsCancelledFlag_WhenTaskIsCancelled()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();
        task.Id = 1;

        // Add task to queue
        var queueField = typeof(TaskManager).GetField("_queue",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var queue = (System.Collections.Concurrent.ConcurrentDictionary<uint, GameTask>)queueField!.GetValue(manager)!;
        queue.TryAdd(1, task);

        manager.Cancel(task);

        Assert.True(task.Cancelled);
    }

    [Fact]
    public void Cancel_ReturnsFalse_ForAlreadyCancelledTask()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();
        task.Id = 1;
        task.Cancelled = true; // Already cancelled

        var result = manager.Cancel(task);

        Assert.False(result);
    }

    [Fact]
    public void Cancel_RemovesTaskFromQueue()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();
        task.Id = 1;

        // Add task to queue
        var queueField = typeof(TaskManager).GetField("_queue",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var queue = (System.Collections.Concurrent.ConcurrentDictionary<uint, GameTask>)queueField!.GetValue(manager)!;
        queue.TryAdd(1, task);

        Assert.Single(queue);

        manager.Cancel(task);

        Assert.Empty(queue);
    }

    #endregion

    #region Repeat Task Tests

    [Fact]
    public void Schedule_SetsRepeatCount_WhenRepeatIntervalProvided()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        manager.Schedule(testTask, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), 3);

        Assert.Equal(TimeSpan.FromSeconds(5), testTask.RepeatInterval);
        Assert.Equal(3, testTask.RepeatCount);
    }

    [Fact]
    public void Schedule_DefaultRepeatCount_WhenNoInterval()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        manager.Schedule(testTask, TimeSpan.FromSeconds(10));

        Assert.Equal(1, testTask.RepeatCount);
    }

    [Fact]
    public void Schedule_ReturnsTrue_WithInfiniteRepeatCount()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();

        var result = manager.Schedule(task, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), -1);

        Assert.True(result);
    }

    [Fact]
    public void Schedule_ReturnsTrue_WithZeroRepeatCount()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();

        var result = manager.Schedule(task, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), 0);

        Assert.True(result);
    }

    #endregion

    #region Execute Tests

    [Fact]
    public void Schedule_ExecutesImmediately_WhenZeroDelayAndCountOne()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        var result = manager.Schedule(testTask, TimeSpan.Zero, null, 1);

        Assert.True(result);
        Assert.True(testTask.WasExecuted);
    }

    [Fact]
    public void Schedule_ExecutesImmediately_WhenZeroDelayAndZeroCount()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        var result = manager.Schedule(testTask, TimeSpan.Zero, null, 0);

        Assert.True(result);
        Assert.True(testTask.WasExecuted);
    }

    [Fact]
    public void CronSchedule_ExecutesImmediately_WhenZeroDelay()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        var result = manager.CronSchedule(testTask, "* * * * * *", TimeSpan.Zero);

        Assert.True(result);
        Assert.True(testTask.WasExecuted);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void Schedule_ThrowsException_WhenTaskIsNull()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);

        // Currently it throws NullReferenceException when task is null
        Assert.Throws<NullReferenceException>(() => manager.Schedule(null!, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void CronSchedule_ThrowsException_WhenCronExpressionIsNull()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();

        Assert.Throws<ArgumentNullException>(() => manager.CronSchedule(task, null!));
    }

    [Fact]
    public void CronSchedule_ThrowsException_WhenInvalidCronExpression()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();

        Assert.Throws<NCrontab.CrontabException>(() => manager.CronSchedule(task, "invalid-cron"));
    }

    [Fact]
    public void GetQueueCount_ReturnsZero_WhenNoTasks()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);

        var count = manager.GetQueueCount();

        Assert.Equal(0, count);
    }

    [Fact]
    public void GetQueueCount_ReturnsCorrectCount_AfterSchedulingTasks()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task1 = Mock.Of<GameTask>();
        var task2 = Mock.Of<GameTask>();

        manager.Schedule(task1, TimeSpan.FromSeconds(60));
        manager.Schedule(task2, TimeSpan.FromSeconds(60));

        var count = manager.GetQueueCount();

        Assert.Equal(2, count);
    }

    #endregion

    #region Periodic Task Tests

    [Fact]
    public void Schedule_WithPeriodicInterval_QueuesTask()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<GameTask>();

        var result = manager.Schedule(task, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), 5);

        Assert.True(result);
        Assert.Equal(1, manager.GetQueueCount());
    }

    [Fact]
    public void Schedule_WithVeryLongDelay_SetsCorrectTriggerTime()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var testTask = new TestTask();

        var startDelay = TimeSpan.FromHours(24);
        manager.Schedule(testTask, startDelay, TimeSpan.FromHours(1), 10);

        // Trigger time should be approximately now + 24 hours
        var expectedMin = DateTime.UtcNow + startDelay - TimeSpan.FromSeconds(1);
        var expectedMax = DateTime.UtcNow + startDelay + TimeSpan.FromSeconds(1);
        Assert.True(testTask.TriggerTime >= expectedMin && testTask.TriggerTime <= expectedMax);
    }

    #endregion

    #region Restart/Initialize Tests

    [Fact]
    public void Initialize_ClearsQueue()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);

        // Add some tasks first
        var task = Mock.Of<GameTask>();
        manager.Schedule(task, TimeSpan.FromSeconds(60));
        Assert.Equal(1, manager.GetQueueCount());

        // Initialize should clear the queue
        manager.Initialize();

        Assert.Equal(0, manager.GetQueueCount());
    }

    [Fact]
    public void Start_CanBeCalledMultipleTimes()
    {
        var mockTick = new Mock<ITickManager>();
        var handler = new TickManager.TickEventHandler();
        mockTick.SetupGet(t => t.OnTick).Returns(handler);

        var manager = new TaskManager(mockTick.Object);

        // Call Start multiple times
        manager.Start();
        manager.Start();
        manager.Start();

        // Each call to Start subscribes to OnTick
        // So we expect the property to be accessed 3 times
        mockTick.VerifyGet(t => t.OnTick, Times.Exactly(3));
    }

    #endregion

    #region Task ID Tests

    [Fact]
    public void Schedule_AssignsUniqueIds_ToMultipleTasks()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task1 = Mock.Of<GameTask>();
        var task2 = Mock.Of<GameTask>();
        var task3 = Mock.Of<GameTask>();

        manager.Schedule(task1, TimeSpan.FromSeconds(60));
        manager.Schedule(task2, TimeSpan.FromSeconds(60));
        manager.Schedule(task3, TimeSpan.FromSeconds(60));

        // All tasks should have unique IDs
        Assert.NotEqual(task1.Id, task2.Id);
        Assert.NotEqual(task2.Id, task3.Id);
        Assert.NotEqual(task1.Id, task3.Id);
    }

    #endregion

    #region RemoveTasks Tests

    [Fact]
    public void RemoveTasks_RemovesMatchingTasks()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task1 = Mock.Of<GameTask>();
        var task2 = Mock.Of<GameTask>();
        var task3 = Mock.Of<GameTask>();

        manager.Schedule(task1, TimeSpan.FromSeconds(60));
        manager.Schedule(task2, TimeSpan.FromSeconds(60));
        manager.Schedule(task3, TimeSpan.FromSeconds(60));

        Assert.Equal(3, manager.GetQueueCount());

        // Remove tasks with ID > 1
        manager.RemoveTasks(t => t.Id > 1);

        Assert.Equal(1, manager.GetQueueCount());
    }

    [Fact]
    public void RemoveTasks_RemovesAll_WhenPredicateMatchesAll()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task1 = Mock.Of<GameTask>();
        var task2 = Mock.Of<GameTask>();

        manager.Schedule(task1, TimeSpan.FromSeconds(60));
        manager.Schedule(task2, TimeSpan.FromSeconds(60));

        Assert.Equal(2, manager.GetQueueCount());

        manager.RemoveTasks(t => true);

        Assert.Equal(0, manager.GetQueueCount());
    }

    #endregion
}
