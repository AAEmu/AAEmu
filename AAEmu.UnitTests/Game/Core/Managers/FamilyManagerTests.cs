using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class FamilyManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockWorld = Mock.Of<IWorldManager>();
        var mockChat = Mock.Of<IChatManager>();
        var mockFamilyId = Mock.Of<IFamilyIdManager>();
        var manager = new FamilyManager(mockWorld.Object, mockChat.Object, mockFamilyId.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockWorld);
        Mock.VerifyNoOtherCalls(mockChat);
        Mock.VerifyNoOtherCalls(mockFamilyId);
    }
}