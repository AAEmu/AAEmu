using AAEmu.Game.Core.Managers;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class AccountManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockTick = new Mock<ITickManager>();
        var mockTimedRewards = new Mock<ITimedRewardsManager>();

        var manager = new AccountManager(mockTick.Object, mockTimedRewards.Object);

        await Assert.That(manager).IsNotNull();
        mockTick.VerifyNoOtherCalls();
        mockTimedRewards.VerifyNoOtherCalls();
    }

    [Test]
    public void Initialize_AccessesOnTickProperty()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.Setup(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new AccountManager(mockTick.Object, new Mock<ITimedRewardsManager>().Object);
        manager.Initialize();

        mockTick.Verify(t => t.OnTick, Times.Once);
    }
}
