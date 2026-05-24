using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class EnterWorldManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockAccount = Mock.Of<IAccountManager>();
        var mockStream = Mock.Of<IStreamManager>();
        var mockQuest = Mock.Of<IQuestManager>();
        var mockChat = Mock.Of<IChatManager>();
        var mockFamily = Mock.Of<IFamilyManager>();
        var mockWorld = Mock.Of<IWorldManager>();

        var manager = new EnterWorldManager(
            mockAccount.Object,
            mockStream.Object,
            mockQuest.Object,
            mockChat.Object,
            mockFamily.Object,
            mockWorld.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockAccount);
        Mock.VerifyNoOtherCalls(mockStream);
        Mock.VerifyNoOtherCalls(mockQuest);
        Mock.VerifyNoOtherCalls(mockChat);
        Mock.VerifyNoOtherCalls(mockFamily);
        Mock.VerifyNoOtherCalls(mockWorld);
    }
}