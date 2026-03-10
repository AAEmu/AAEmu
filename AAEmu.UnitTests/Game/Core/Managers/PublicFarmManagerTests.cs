using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using Moq;
using AaEmuTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class PublicFarmManagerTests
{
    [Test]
    public void Initialize_SchedulesTick()
    {
        var mockTask = new Mock<ITaskManager>();
        mockTask.Setup(t => t.Schedule(It.IsAny<AaEmuTask>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>())).Returns(true);
        var manager = new PublicFarmManager(mockTask.Object, new Mock<IWorldManager>().Object, new Mock<ISubZoneManager>().Object);
        manager.Load();
        manager.Initialize();

        mockTask.Verify(t => t.Schedule(It.IsAny<AaEmuTask>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>()), Times.Once);
    }
}