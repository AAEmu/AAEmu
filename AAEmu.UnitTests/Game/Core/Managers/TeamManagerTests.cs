using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class TeamManagerTests
{
    [Fact]
    public void Constructor_DoesNotCallDeps()
    {
        var mockWorld = new Mock<IWorldManager>();
        var mockChat = new Mock<IChatManager>();
        var mockTeamId = new Mock<ITeamIdManager>();
        var manager = new TeamManager(mockWorld.Object, mockChat.Object, mockTeamId.Object);

        Assert.NotNull(manager);
        mockWorld.VerifyNoOtherCalls();
        mockChat.VerifyNoOtherCalls();
        mockTeamId.VerifyNoOtherCalls();
    }
}
