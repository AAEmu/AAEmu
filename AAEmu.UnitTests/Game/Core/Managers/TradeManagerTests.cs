using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class TradeManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockTradeId = new Mock<ITradeIdManager>();
        var mockWorld = new Mock<IWorldManager>();
        var manager = new TradeManager(mockTradeId.Object, mockWorld.Object);

        await Assert.That(manager).IsNotNull();
        mockTradeId.VerifyNoOtherCalls();
        mockWorld.VerifyNoOtherCalls();
    }
}