using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class IndunManagerTests
{
    [Test]
    public void Initialize_SubscribesToTickManager()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());
        var manager = new IndunManager(mockTick.Object, Mock.Of<IWorldManager>().Object, Mock.Of<IZoneManager>().Object, Mock.Of<ITeamManager>().Object);
        manager.Initialize();

        mockTick.OnTick.WasCalled(Times.Once);
    }
}