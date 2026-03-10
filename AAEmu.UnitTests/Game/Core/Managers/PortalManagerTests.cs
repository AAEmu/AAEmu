using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class PortalManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockLocale = new Mock<ILocalizationManager>();
        var mockWorld = new Mock<IWorldManager>();
        var mockZone = new Mock<IZoneManager>();
        var mockNpc = new Mock<INpcManager>();
        var mockObjId = new Mock<IObjectIdManager>();
        var mockTask = new Mock<ITaskManager>();
        var manager = new PortalManager(mockLocale.Object, mockWorld.Object, mockZone.Object, mockNpc.Object, mockObjId.Object, mockTask.Object);

        await Assert.That(manager).IsNotNull();
        mockLocale.VerifyNoOtherCalls();
        mockWorld.VerifyNoOtherCalls();
        mockZone.VerifyNoOtherCalls();
        mockNpc.VerifyNoOtherCalls();
        mockObjId.VerifyNoOtherCalls();
        mockTask.VerifyNoOtherCalls();
    }
}