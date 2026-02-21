using AAEmu.Game.Core.Managers;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class TaskManagerTests
{
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
        var task = Mock.Of<AAEmu.Game.Models.Tasks.Task>();

        var result = manager.Schedule(task, TimeSpan.FromSeconds(60));

        Assert.True(result);
    }

    [Fact]
    public void Cancel_ReturnsFalse_WhenTaskNotInQueue()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new TaskManager(mockTick.Object);
        var task = Mock.Of<AAEmu.Game.Models.Tasks.Task>();

        var result = manager.Cancel(task);

        Assert.False(result);
    }
}
