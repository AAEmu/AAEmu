using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class TradeManagerTests
{
    [Fact]
    public void Constructor_DoesNotCallDeps()
    {
        var mockTradeId = new Mock<ITradeIdManager>();
        var mockWorld = new Mock<IWorldManager>();
        var manager = new TradeManager(mockTradeId.Object, mockWorld.Object);

        Assert.NotNull(manager);
        mockTradeId.VerifyNoOtherCalls();
        mockWorld.VerifyNoOtherCalls();
    }
}
