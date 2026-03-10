using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class MailManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockMailId = Mock.Of<IMailIdManager>();
        var mockName = Mock.Of<INameManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockTask = Mock.Of<ITaskManager>();
        var mockWorld = Mock.Of<IWorldManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockLocale = Mock.Of<ILocalizationManager>();
        var manager = new MailManager(mockMailId.Object, mockName.Object, mockItem.Object, mockTask.Object, mockWorld.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockLocale.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockMailId);
        Mock.VerifyNoOtherCalls(mockName);
        Mock.VerifyNoOtherCalls(mockItem);
        Mock.VerifyNoOtherCalls(mockTask);
        Mock.VerifyNoOtherCalls(mockWorld);
        Mock.VerifyNoOtherCalls(mockHousing);
        Mock.VerifyNoOtherCalls(mockLocale);
    }
}