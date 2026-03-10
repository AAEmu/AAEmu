using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class MailManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockMailId = new Mock<IMailIdManager>();
        var mockName = new Mock<INameManager>();
        var mockItem = new Mock<IItemManager>();
        var mockTask = new Mock<ITaskManager>();
        var mockWorld = new Mock<IWorldManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockLocale = new Mock<ILocalizationManager>();
        var manager = new MailManager(mockMailId.Object, mockName.Object, mockItem.Object, mockTask.Object, mockWorld.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockLocale.Object);

        await Assert.That(manager).IsNotNull();
        mockMailId.VerifyNoOtherCalls();
        mockName.VerifyNoOtherCalls();
        mockItem.VerifyNoOtherCalls();
        mockTask.VerifyNoOtherCalls();
        mockWorld.VerifyNoOtherCalls();
        mockHousing.VerifyNoOtherCalls();
        mockLocale.VerifyNoOtherCalls();
    }
}