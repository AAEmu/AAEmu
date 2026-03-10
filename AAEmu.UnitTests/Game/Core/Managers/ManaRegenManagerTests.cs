using AAEmu.Game.Core.Managers;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ManaRegenManagerTests
{
    [Test]
    public void Initialize_SubscribesToTickManager()
    {
        var mockTick = new Mock<ITickManager>();
        var handler = new TickManager.TickEventHandler();
        mockTick.SetupGet(t => t.OnTick).Returns(handler);

        var manager = new ManaRegenManager(mockTick.Object);
        manager.Initialize();

        mockTick.VerifyGet(t => t.OnTick, Times.Once);
    }
}