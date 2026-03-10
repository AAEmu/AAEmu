#nullable enable

using System.Net;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Packets.L2C;
using AAEmu.Login.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using ISession = AAEmu.Commons.Network.Core.ISession;

namespace AAEmu.UnitTests.Login.Core.Network.Connections;

public class LoginSessionTests
{
    private static readonly AccountId s_testAccountId = new(100);
    private static readonly ConnectionId s_testConnectionId = new(42);
    private static readonly GameServerId s_testGsId = new(1);
    private static readonly IPAddress s_testIp = IPAddress.Parse("10.0.0.1");

    private readonly Mock<ILoginConnection> _mockConnection;
    private readonly Mock<IGameController> _mockGameController;
    private readonly List<LoginPacket> _sentPackets = [];
    private readonly CancellationTokenSource _connectionClosedCts = new();
    private readonly LoginSession _session;

    public LoginSessionTests()
    {
        _mockConnection = new Mock<ILoginConnection>();
        _mockConnection.SetupGet(c => c.Id).Returns(s_testConnectionId);
        _mockConnection.SetupGet(c => c.Ip).Returns(s_testIp);
        _mockConnection.SetupGet(c => c.ConnectionClosed).Returns(_connectionClosedCts.Token);
        _mockConnection.SetupProperty(c => c.AccountId);
        _mockConnection.SetupProperty(c => c.AccountName);
        _mockConnection.SetupProperty(c => c.LastLogin);
        _mockConnection.SetupProperty(c => c.LastIp);
        _mockConnection
            .Setup(c => c.SendPacketAsync(It.IsAny<LoginPacket>(), It.IsAny<CancellationToken>()))
            .Callback<LoginPacket, CancellationToken>((p, _) => _sentPackets.Add(p))
            .Returns(ValueTask.CompletedTask);

        _mockGameController = new Mock<IGameController>();

        var appConfig = Options.Create(new AppConfiguration
        {
            SecretKey = "test", EnterWorldTimeout = TimeSpan.FromMilliseconds(200), GameServers = []
        });

        _session = new LoginSession(
            _mockConnection.Object,
            _mockGameController.Object,
            TimeProvider.System,
            appConfig,
            Mock.Of<ILogger<LoginSession>>());
    }

    private LoginSession CreateSessionWithTimeProvider(TimeProvider timeProvider)
    {
        var appConfig = Options.Create(new AppConfiguration
        {
            SecretKey = "test", EnterWorldTimeout = TimeSpan.FromMilliseconds(200), GameServers = []
        });

        return new LoginSession(
            _mockConnection.Object,
            _mockGameController.Object,
            timeProvider,
            appConfig,
            Mock.Of<ILogger<LoginSession>>());
    }

    private static Mock<IAuthenticationFlow> CreateMockFlow(AuthFlowResult result)
    {
        var mock = new Mock<IAuthenticationFlow>();
        mock.Setup(f => f.StartAsync(It.IsAny<ILoginClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    private async Task AuthenticateSuccessfullyAsync(LoginSession? session = null)
    {
        session ??= _session;
        var flow = CreateMockFlow(new AuthFlowResult.Success(s_testAccountId, "testuser"));
        await session.AuthenticateAsync(flow.Object, CancellationToken.None);
        _sentPackets.Clear();
    }

    private GameServer CreateActiveGameServer(GameServerId gsId)
    {
        var server = new GameServer(gsId, "TestServer", "127.0.0.1", 1234)
        {
            Connection = new InternalConnection(Mock.Of<ISession>())
        };
        _mockGameController.Setup(g => g.GetGameServer(gsId)).Returns(server);
        return server;
    }

    [Test]
    public async Task AuthenticateAsync_Success_SendsJoinThenAuthResponse()
    {
        var flow = CreateMockFlow(new AuthFlowResult.Success(s_testAccountId, "testuser"));

        await _session.AuthenticateAsync(flow.Object, CancellationToken.None);

        await Assert.That(_sentPackets.Count).IsEqualTo(2);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACJoinResponsePacket));
        await Assert.That(_sentPackets[1].GetType()).IsEqualTo(typeof(ACAuthResponsePacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task AuthenticateAsync_Denied_SendsLoginDeniedPacket()
    {
        var flow = CreateMockFlow(new AuthFlowResult.Denied(LoginDeniedReason.BadAccount));

        await _session.AuthenticateAsync(flow.Object, CancellationToken.None);

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACLoginDeniedPacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Connected);
    }

    [Test]
    public async Task AuthenticateAsync_FromWrongState_SendsDeniedBadResponse()
    {
        await AuthenticateSuccessfullyAsync();

        var flow = CreateMockFlow(new AuthFlowResult.Success(s_testAccountId));
        await _session.AuthenticateAsync(flow.Object, CancellationToken.None);

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACLoginDeniedPacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task AuthenticateAsync_Pending_NoPacketSent()
    {
        var flow = CreateMockFlow(new AuthFlowResult.Pending());

        await _session.AuthenticateAsync(flow.Object, CancellationToken.None);

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticating);
    }

    [Test]
    public async Task AuthenticateAsync_Success_SetsConnectionProperties()
    {
        var flow = CreateMockFlow(new AuthFlowResult.Success(s_testAccountId, "testuser"));

        await _session.AuthenticateAsync(flow.Object, CancellationToken.None);

        await Assert.That(_mockConnection.Object.AccountId).IsEqualTo(s_testAccountId);
        await Assert.That(_mockConnection.Object.AccountName).IsEqualTo("testuser");
        await Assert.That(_mockConnection.Object.LastIp).IsEqualTo(s_testIp);
        // LastLogin should be set to approximately now
        await Assert.That(DateTime.UtcNow - _mockConnection.Object.LastLogin < TimeSpan.FromSeconds(5)).IsTrue();
    }

    [Test]
    public async Task ContinueAuthAsync_MatchingFlow_Success_SendsAuthPackets()
    {
        // Start with pending flow
        var flow = new Mock<IAuthenticationFlow>();
        flow.Setup(f => f.StartAsync(It.IsAny<ILoginClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthFlowResult.Pending());
        await _session.AuthenticateAsync(flow.Object, CancellationToken.None);
        _sentPackets.Clear();

        // Continue with success
        await _session.ContinueAuthAsync<IAuthenticationFlow>(
            _ => Task.FromResult<AuthFlowResult>(new AuthFlowResult.Success(s_testAccountId, "user")),
            CancellationToken.None);

        await Assert.That(_sentPackets.Count).IsEqualTo(2);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACJoinResponsePacket));
        await Assert.That(_sentPackets[1].GetType()).IsEqualTo(typeof(ACAuthResponsePacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task ContinueAuthAsync_MatchingFlow_Denied_SendsDeniedPacket()
    {
        var flow = new Mock<IAuthenticationFlow>();
        flow.Setup(f => f.StartAsync(It.IsAny<ILoginClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthFlowResult.Pending());
        await _session.AuthenticateAsync(flow.Object, CancellationToken.None);
        _sentPackets.Clear();

        await _session.ContinueAuthAsync<IAuthenticationFlow>(
            _ => Task.FromResult<AuthFlowResult>(new AuthFlowResult.Denied(LoginDeniedReason.BadAccount)),
            CancellationToken.None);

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACLoginDeniedPacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Connected);
    }

    /// <summary>
    /// A distinct flow interface for type-mismatch testing.
    /// </summary>
    private interface IOtherFlow : IAuthenticationFlow;

    [Test]
    public async Task ContinueAuthAsync_WrongFlowType_SendsDeniedAndShutdown()
    {
        // Start with a mocked IAuthenticationFlow
        var flow = new Mock<IAuthenticationFlow>();
        flow.Setup(f => f.StartAsync(It.IsAny<ILoginClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthFlowResult.Pending());
        await _session.AuthenticateAsync(flow.Object, CancellationToken.None);
        _sentPackets.Clear();

        // Continue expecting IOtherFlow = type mismatch
        await _session.ContinueAuthAsync<IOtherFlow>(
            _ => Task.FromResult<AuthFlowResult>(new AuthFlowResult.Success(s_testAccountId)),
            CancellationToken.None);

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACLoginDeniedPacket));
        _mockConnection.Verify(c => c.Shutdown(), Times.Once);
        await Assert.That(_session.State).IsEqualTo(LoginState.Disconnected);
    }

    [Test]
    public async Task ContinueAuthAsync_FromWrongState_SendsDenied()
    {
        // Session is in Connected state (no pending auth)
        await _session.ContinueAuthAsync<IAuthenticationFlow>(
            _ => Task.FromResult<AuthFlowResult>(new AuthFlowResult.Success(s_testAccountId)),
            CancellationToken.None);

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACLoginDeniedPacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Connected);
    }

    [Test]
    public async Task InitiateEnterWorld_Success_SendsWorldCookiePacket()
    {
        await AuthenticateSuccessfullyAsync();
        CreateActiveGameServer(s_testGsId);

        await _session.InitiateEnterWorldAsync(s_testGsId, CancellationToken.None);
        _session.CompleteEnterWorldRequest(s_testGsId, 0);
        await _session.EnterWorldBackgroundTask!;

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACWorldCookiePacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task InitiateEnterWorld_Failure_SendsEnterWorldDeniedPacket()
    {
        await AuthenticateSuccessfullyAsync();
        CreateActiveGameServer(s_testGsId);

        await _session.InitiateEnterWorldAsync(s_testGsId, CancellationToken.None);
        _session.CompleteEnterWorldRequest(s_testGsId, 1);
        await _session.EnterWorldBackgroundTask!;

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACEnterWorldDeniedPacket));
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task InitiateEnterWorld_Timeout_SendsEnterWorldDenied()
    {
        var fakeTime = new FakeTimeProvider();
        var session = CreateSessionWithTimeProvider(fakeTime);
        await AuthenticateSuccessfullyAsync(session);
        CreateActiveGameServer(s_testGsId);

        await session.InitiateEnterWorldAsync(s_testGsId, CancellationToken.None);

        // Advance past the 200ms timeout
        fakeTime.Advance(TimeSpan.FromMilliseconds(300));
        await session.EnterWorldBackgroundTask!;

        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_sentPackets[0].GetType()).IsEqualTo(typeof(ACEnterWorldDeniedPacket));
        await Assert.That(session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task InitiateEnterWorld_FromWrongState_NoAction()
    {
        // Session is in Connected state
        await _session.InitiateEnterWorldAsync(s_testGsId, CancellationToken.None);

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Connected);
    }

    [Test]
    public async Task InitiateEnterWorld_ServerNotActive_NoAction()
    {
        await AuthenticateSuccessfullyAsync();

        // Server with no connection (not active)
        var server = new GameServer(s_testGsId, "TestServer", "127.0.0.1", 1234);
        _mockGameController.Setup(g => g.GetGameServer(s_testGsId)).Returns(server);

        await _session.InitiateEnterWorldAsync(s_testGsId, CancellationToken.None);

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task CompleteEnterWorld_GsIdMismatch_Ignored()
    {
        await AuthenticateSuccessfullyAsync();
        CreateActiveGameServer(s_testGsId);

        await _session.InitiateEnterWorldAsync(s_testGsId, CancellationToken.None);

        // Complete with a different gsId
        var wrongGsId = new GameServerId(99);
        _session.CompleteEnterWorldRequest(wrongGsId, 0);
        await Assert.That(_sentPackets).IsEmpty();

        // Clean up: cancel to avoid timeout firing later
        _session.CancelEnterWorld();
        await _session.EnterWorldBackgroundTask!;
    }

    [Test]
    public async Task CancelEnterWorld_WhileEntering_CancelsOperation()
    {
        await AuthenticateSuccessfullyAsync();
        CreateActiveGameServer(s_testGsId);

        await _session.InitiateEnterWorldAsync(s_testGsId, CancellationToken.None);
        _session.CancelEnterWorld();
        await _session.EnterWorldBackgroundTask!;

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task CancelEnterWorld_NotEntering_Ignored()
    {
        await AuthenticateSuccessfullyAsync();

        _session.CancelEnterWorld();

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Authenticated);
    }

    [Test]
    public async Task DisconnectAsync_CancelsPendingEnterWorld()
    {
        await AuthenticateSuccessfullyAsync();
        CreateActiveGameServer(s_testGsId);

        await _session.InitiateEnterWorldAsync(s_testGsId, CancellationToken.None);
        await _session.DisconnectAsync();

        // Should not send any packets since disconnect cancels before result
        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Disconnected);
    }

    [Test]
    public async Task DisconnectAsync_FromAuthenticated_SetsDisconnected()
    {
        await AuthenticateSuccessfullyAsync();

        await _session.DisconnectAsync();

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Disconnected);
    }

    [Test]
    public async Task DisconnectAsync_AlreadyDisconnected_Idempotent()
    {
        await _session.DisconnectAsync();
        await _session.DisconnectAsync();

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(_session.State).IsEqualTo(LoginState.Disconnected);
    }
}
