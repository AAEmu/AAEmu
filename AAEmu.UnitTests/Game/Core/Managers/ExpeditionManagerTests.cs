using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ExpeditionManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockExpId = Mock.Of<IExpeditionIdManager>();
        var mockTeam = Mock.Of<ITeamManager>();
        var mockWorld = Mock.Of<IWorldManager>();
        var mockChat = Mock.Of<IChatManager>();
        var manager = new ExpeditionManager(mockExpId.Object, mockTeam.Object, mockWorld.Object, mockChat.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockExpId);
        Mock.VerifyNoOtherCalls(mockTeam);
        Mock.VerifyNoOtherCalls(mockWorld);
        Mock.VerifyNoOtherCalls(mockChat);
    }
}