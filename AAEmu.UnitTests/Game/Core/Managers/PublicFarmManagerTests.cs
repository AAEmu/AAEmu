using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AaEmuTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class PublicFarmManagerTests
{
    [Test]
    public void Initialize_SchedulesTick()
    {
        var mockTask = Mock.Of<ITaskManager>();
        mockTask.Schedule(Any<AaEmuTask>(), Any<TimeSpan?>(), Any<TimeSpan?>(), Any<int>()).Returns(true);
        var manager = new PublicFarmManager(mockTask.Object, Mock.Of<IWorldManager>().Object, Mock.Of<ISubZoneManager>().Object);
        manager.Load();
        manager.Initialize();

        mockTask.Schedule(Any<AaEmuTask>(), Any<TimeSpan?>(), Any<TimeSpan?>(), Any<int>()).WasCalled(Times.Once);
    }
}