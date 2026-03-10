using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class FamilyManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockWorld = new Mock<IWorldManager>();
        var mockChat = new Mock<IChatManager>();
        var mockFamilyId = new Mock<IFamilyIdManager>();
        var manager = new FamilyManager(mockWorld.Object, mockChat.Object, mockFamilyId.Object);

        await Assert.That(manager).IsNotNull();
        mockWorld.VerifyNoOtherCalls();
        mockChat.VerifyNoOtherCalls();
        mockFamilyId.VerifyNoOtherCalls();
    }
}