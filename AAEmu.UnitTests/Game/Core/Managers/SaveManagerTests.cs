using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class SaveManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockTask = Mock.Of<ITaskManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockMail = Mock.Of<IMailManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockAuction = Mock.Of<IAuctionManager>();
        var mockCrime = Mock.Of<ICrimeManager>();
        var mockAccountAttribute = Mock.Of<IAccountAttributeManager>();
        var mockWorld = Mock.Of<IWorldManager>();

        var manager = new SaveManager(
            mockTask.Object,
            mockHousing.Object,
            mockMail.Object,
            mockItem.Object,
            mockAuction.Object,
            mockCrime.Object,
            mockAccountAttribute.Object,
            mockWorld.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockTask);
        Mock.VerifyNoOtherCalls(mockHousing);
        Mock.VerifyNoOtherCalls(mockMail);
        Mock.VerifyNoOtherCalls(mockItem);
        Mock.VerifyNoOtherCalls(mockAuction);
        Mock.VerifyNoOtherCalls(mockCrime);
        Mock.VerifyNoOtherCalls(mockAccountAttribute);
        Mock.VerifyNoOtherCalls(mockWorld);
    }
}
