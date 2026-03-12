using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AaEmuTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ShipyardManagerTests
{
    [Test]
    public void Initialize_SchedulesTick()
    {
        var mockTask = Mock.Of<ITaskManager>();
        mockTask.Schedule(Any<AaEmuTask>(), Any<TimeSpan?>(), Any<TimeSpan?>(), Any<int>()).Returns(true);
        var manager = new ShipyardManager(
            mockTask.Object,
            Mock.Of<IObjectIdManager>().Object,
            Mock.Of<IShipyardIdManager>().Object,
            Mock.Of<IWorldManager>().Object,
            Mock.Of<ITaxationsManager>().Object,
            Mock.Of<ISkillManager>().Object);
        manager.Initialize();

        mockTask.Schedule(Any<AaEmuTask>(), Any<TimeSpan?>(), Any<TimeSpan?>(), Any<int>()).WasCalled(Times.Once);
    }
}