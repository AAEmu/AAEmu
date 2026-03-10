using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ManaRegenManagerTests
{
    [Test]
    public void Initialize_SubscribesToTickManager()
    {
        var mockTick = Mock.Of<ITickManager>();
        var handler = new TickManager.TickEventHandler();
        mockTick.OnTick.Returns(handler);

        var manager = new ManaRegenManager(mockTick.Object);
        manager.Initialize();

        mockTick.OnTick.WasCalled(Times.Once);
    }
}