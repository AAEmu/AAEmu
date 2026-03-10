using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class EnterWorldManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockAccount = new Mock<IAccountManager>();
        var mockStream = new Mock<IStreamManager>();
        var mockQuest = new Mock<IQuestManager>();
        var mockTeam = new Mock<ITeamManager>();
        var mockChat = new Mock<IChatManager>();
        var mockFamily = new Mock<IFamilyManager>();
        var mockWorld = new Mock<IWorldManager>();

        var manager = new EnterWorldManager(
            mockAccount.Object,
            mockStream.Object,
            mockQuest.Object,
            mockTeam.Object,
            mockChat.Object,
            mockFamily.Object,
            mockWorld.Object);

        await Assert.That(manager).IsNotNull();
        mockAccount.VerifyNoOtherCalls();
        mockStream.VerifyNoOtherCalls();
        mockQuest.VerifyNoOtherCalls();
        mockTeam.VerifyNoOtherCalls();
        mockChat.VerifyNoOtherCalls();
        mockFamily.VerifyNoOtherCalls();
        mockWorld.VerifyNoOtherCalls();
    }
}