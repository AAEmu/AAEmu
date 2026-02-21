using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using Moq;
using Xunit;
using AaEmuTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ShipyardManagerTests
{
    [Fact]
    public void Initialize_SchedulesTick()
    {
        var mockTask = new Mock<ITaskManager>();
        mockTask.Setup(t => t.Schedule(It.IsAny<AaEmuTask>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>())).Returns(true);
        var manager = new ShipyardManager(
            mockTask.Object,
            new Mock<IObjectIdManager>().Object,
            new Mock<IShipyardIdManager>().Object,
            new Mock<IWorldManager>().Object,
            new Mock<ITaxationsManager>().Object,
            new Mock<ISkillManager>().Object);
        manager.Initialize();

        mockTask.Verify(t => t.Schedule(It.IsAny<AaEmuTask>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>()), Times.Once);
    }
}
