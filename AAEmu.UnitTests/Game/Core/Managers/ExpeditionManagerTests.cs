using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ExpeditionManagerTests
{
    [Fact]
    public void Constructor_DoesNotCallDeps()
    {
        var mockExpId = new Mock<IExpeditionIdManager>();
        var mockTeam = new Mock<ITeamManager>();
        var mockWorld = new Mock<IWorldManager>();
        var mockChat = new Mock<IChatManager>();
        var manager = new ExpeditionManager(mockExpId.Object, mockTeam.Object, mockWorld.Object, mockChat.Object);

        Assert.NotNull(manager);
        mockExpId.VerifyNoOtherCalls();
        mockTeam.VerifyNoOtherCalls();
        mockWorld.VerifyNoOtherCalls();
        mockChat.VerifyNoOtherCalls();
    }
}
