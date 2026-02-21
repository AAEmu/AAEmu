using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class FamilyManagerTests
{
    [Fact]
    public void Constructor_DoesNotCallDeps()
    {
        var mockWorld = new Mock<IWorldManager>();
        var mockChat = new Mock<IChatManager>();
        var mockFamilyId = new Mock<IFamilyIdManager>();
        var manager = new FamilyManager(mockWorld.Object, mockChat.Object, mockFamilyId.Object);

        Assert.NotNull(manager);
        mockWorld.VerifyNoOtherCalls();
        mockChat.VerifyNoOtherCalls();
        mockFamilyId.VerifyNoOtherCalls();
    }
}
