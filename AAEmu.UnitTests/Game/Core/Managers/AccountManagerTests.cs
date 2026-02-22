using AAEmu.Game.Core.Managers;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class AccountManagerTests
{
    [Fact]
    public void Constructor_DoesNotCallDeps()
    {
        var mockTick = new Mock<ITickManager>();
        var mockTimedRewards = new Mock<ITimedRewardsManager>();

        var manager = new AccountManager(mockTick.Object, mockTimedRewards.Object);

        Assert.NotNull(manager);
        mockTick.VerifyNoOtherCalls();
        mockTimedRewards.VerifyNoOtherCalls();
    }

    [Fact]
    public void Initialize_AccessesOnTickProperty()
    {
        var mockTick = new Mock<ITickManager>();
        mockTick.Setup(t => t.OnTick).Returns(new TickManager.TickEventHandler());

        var manager = new AccountManager(mockTick.Object, new Mock<ITimedRewardsManager>().Object);
        manager.Initialize();

        mockTick.Verify(t => t.OnTick, Times.Once);
    }
}
