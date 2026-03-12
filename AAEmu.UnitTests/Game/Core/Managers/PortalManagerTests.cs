using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class PortalManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockLocale = Mock.Of<ILocalizationManager>();
        var mockWorld = Mock.Of<IWorldManager>();
        var mockZone = Mock.Of<IZoneManager>();
        var mockNpc = Mock.Of<INpcManager>();
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockTask = Mock.Of<ITaskManager>();
        var manager = new PortalManager(mockLocale.Object, mockWorld.Object, mockZone.Object, mockNpc.Object, mockObjId.Object, mockTask.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockLocale);
        Mock.VerifyNoOtherCalls(mockWorld);
        Mock.VerifyNoOtherCalls(mockZone);
        Mock.VerifyNoOtherCalls(mockNpc);
        Mock.VerifyNoOtherCalls(mockObjId);
        Mock.VerifyNoOtherCalls(mockTask);
    }
}