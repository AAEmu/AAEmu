using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class AuctionManagerTests
{
    [Fact]
    public void Constructor_DoesNotCallDeps()
    {
        var mockItem = new Mock<IItemManager>();
        var mockName = new Mock<INameManager>();
        var mockAuctionId = new Mock<IAuctionIdManager>();
        var mockLocale = new Mock<ILocalizationManager>();
        var mockTask = new Mock<ITaskManager>();
        var manager = new AuctionManager(mockItem.Object, mockName.Object, mockAuctionId.Object, mockLocale.Object, mockTask.Object);

        Assert.NotNull(manager);
        mockItem.VerifyNoOtherCalls();
        mockName.VerifyNoOtherCalls();
        mockAuctionId.VerifyNoOtherCalls();
        mockLocale.VerifyNoOtherCalls();
        mockTask.VerifyNoOtherCalls();
    }
}
