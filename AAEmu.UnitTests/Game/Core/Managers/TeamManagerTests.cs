using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class TeamManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockWorld = Mock.Of<IWorldManager>();
        var mockChat = Mock.Of<IChatManager>();
        var mockTeamId = Mock.Of<ITeamIdManager>();
        var mockTick = Mock.Of<ITickManager>();
        var manager = new TeamManager(mockWorld.Object, mockChat.Object, mockTeamId.Object, mockTick.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockWorld);
        Mock.VerifyNoOtherCalls(mockChat);
        Mock.VerifyNoOtherCalls(mockTeamId);
        Mock.VerifyNoOtherCalls(mockTick);
    }
}
