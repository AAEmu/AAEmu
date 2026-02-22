using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class SaveManagerTests
{
    [Fact]
    public void Constructor_DoesNotCallDeps()
    {
        var mockTask = new Mock<ITaskManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockMail = new Mock<IMailManager>();
        var mockItem = new Mock<IItemManager>();
        var mockAuction = new Mock<IAuctionManager>();
        var mockWorld = new Mock<IWorldManager>();

        var manager = new SaveManager(
            mockTask.Object,
            mockHousing.Object,
            mockMail.Object,
            mockItem.Object,
            mockAuction.Object,
            mockWorld.Object);

        Assert.NotNull(manager);
        mockTask.VerifyNoOtherCalls();
        mockHousing.VerifyNoOtherCalls();
        mockMail.VerifyNoOtherCalls();
        mockItem.VerifyNoOtherCalls();
        mockAuction.VerifyNoOtherCalls();
        mockWorld.VerifyNoOtherCalls();
    }
}
