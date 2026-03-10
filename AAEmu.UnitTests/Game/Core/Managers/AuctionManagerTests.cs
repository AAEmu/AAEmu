using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class AuctionManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockItem = new Mock<IItemManager>();
        var mockName = new Mock<INameManager>();
        var mockAuctionId = new Mock<IAuctionIdManager>();
        var mockLocale = new Mock<ILocalizationManager>();
        var mockTask = new Mock<ITaskManager>();
        var manager = new AuctionManager(mockItem.Object, mockName.Object, mockAuctionId.Object, mockLocale.Object, mockTask.Object);

        await Assert.That(manager).IsNotNull();
        mockItem.VerifyNoOtherCalls();
        mockName.VerifyNoOtherCalls();
        mockAuctionId.VerifyNoOtherCalls();
        mockLocale.VerifyNoOtherCalls();
        mockTask.VerifyNoOtherCalls();
    }
}
