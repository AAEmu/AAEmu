using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class AccountManagerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IdleSweep_KeepsANewHandshakeButClosesAnExpiredConnection(bool expired)
    {
        var session = Mock.Of<AAEmu.Commons.Network.Core.ISession>();
        var closed = false;
        session.Close().Callback(() => closed = true);
        var connection = new AAEmu.Game.Core.Network.Connections.GameConnection(session.Object);
        if (expired)
            connection.LastPing = DateTime.UtcNow.AddMinutes(-1);
        var manager = new AccountManager(Mock.Of<ITickManager>().Object, Mock.Of<ITimedRewardsManager>().Object);
        var accounts = (System.Collections.Concurrent.ConcurrentDictionary<uint,
            AAEmu.Game.Core.Network.Connections.GameConnection>)typeof(AccountManager)
            .GetField("_accounts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(manager)!;
        accounts[1] = connection;
        typeof(AccountManager).GetMethod("RemoveDeadConnections",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(manager, [TimeSpan.FromSeconds(30)]);
        await Assert.That(closed).IsEqualTo(expired);
    }

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

    [Test]
    public async Task WithAccountLock_AllowsTheSameAccountOperationToReenter()
    {
        var manager = new AccountManager(Mock.Of<ITickManager>().Object, Mock.Of<ITimedRewardsManager>().Object);

        var result = manager.WithAccountLock(1, () => manager.WithAccountLock(1, () => 42));

        await Assert.That(result).IsEqualTo(42);
    }
}
