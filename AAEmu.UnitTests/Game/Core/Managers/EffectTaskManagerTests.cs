using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class EffectTaskManagerTests
{
    [Test]
    public void AddDispelTask_CallsTaskManagerSchedule()
    {
        var mockTaskManager = Mock.Of<ITaskManager>();
        mockTaskManager
            .Schedule(Any<AAEmu.Game.Models.Tasks.Task>(), Any<TimeSpan?>(), Any<TimeSpan?>(), Any<int>())
            .Returns(true);

        var manager = new EffectTaskManager(mockTaskManager.Object);
        manager.AddDispelTask(null, 250.0);

        mockTaskManager
            .Schedule(Any<AAEmu.Game.Models.Tasks.Task>(), Any<TimeSpan?>(), Any<TimeSpan?>(), Any<int>())
            .WasCalled(Times.Once);
    }

    [Test]
    public void AddDispelTask_WithDifferentInterval_PassesCorrectTimeSpan()
    {
        var mockTaskManager = Mock.Of<ITaskManager>();
        mockTaskManager
            .Schedule(Any<AAEmu.Game.Models.Tasks.Task>(), Any<TimeSpan?>(), Any<TimeSpan?>(), Any<int>())
            .Returns(true);

        var manager = new EffectTaskManager(mockTaskManager.Object);
        manager.AddDispelTask(null, 1000.0);

        mockTaskManager
            .Schedule(Any<AAEmu.Game.Models.Tasks.Task>(), Any<TimeSpan?>(), Any<TimeSpan?>(), Any<int>())
            .WasCalled(Times.Once);
    }
}