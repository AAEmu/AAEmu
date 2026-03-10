using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class AccountManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockTick = Mock.Of<ITickManager>();
        var mockTimedRewards = Mock.Of<ITimedRewardsManager>();

        var manager = new AccountManager(mockTick.Object, mockTimedRewards.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockTick);
        Mock.VerifyNoOtherCalls(mockTimedRewards);
    }

    [Test]
    public void Initialize_AccessesOnTickProperty()
    {
        var mockTick = Mock.Of<ITickManager>();
        mockTick.OnTick.Returns(new TickManager.TickEventHandler());

        var manager = new AccountManager(mockTick.Object, Mock.Of<ITimedRewardsManager>().Object);
        manager.Initialize();

        mockTick.OnTick.WasCalled(Times.Once);
    }
}
