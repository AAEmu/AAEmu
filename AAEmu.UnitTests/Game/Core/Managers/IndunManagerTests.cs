using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class IndunManagerTests
{
    [Fact]
    public void Initialize_SubscribesToTickManager()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());
        var manager = new IndunManager(mockTick.Object, new Mock<IWorldManager>().Object, new Mock<IZoneManager>().Object, new Mock<ITeamManager>().Object);
        manager.Initialize();

        mockTick.VerifyGet(t => t.OnTick, Times.Once);
    }
}
