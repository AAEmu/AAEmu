#nullable enable
using AAEmu.Commons.Models;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
namespace AAEmu.UnitTests.Login.Core.Controllers;

public class GameControllerTests
{
    private readonly Mock<IRequestController> _requestController = Mock.Of<IRequestController>();
    private readonly Mock<ILoginConnectionTable> _connectionTable = Mock.Of<ILoginConnectionTable>();

    private static readonly GameServerId s_gsId1 = new(1);
    private static readonly GameServerId s_gsId2 = new(2);
    private static readonly GameServerId s_gsId3 = new(3);
    private static readonly AccountId s_accountId = new(100);
    private static readonly ConnectionId s_connectionId = new(50);

    private GameController CreateSut(AppConfiguration config) =>
        new(_requestController.Object, _connectionTable.Object,
            Options.Create(config), NullLogger<GameController>.Instance);

    private static AppConfiguration MakeConfig(
        params (byte id, string name, string host, ushort port, bool hidden)[] servers) =>
        new()
        {
            SecretKey = "test",
            SkipHostResolve = true,
            GameServers = servers.Select(s => new AppConfiguration.GameServerConfig
            {
                Id = s.id,
                Name = s.name,
                Host = s.host,
                Port = s.port,
                Hidden = s.hidden
            }).ToList()
        };

    /// <summary>Creates an InternalConnection backed by a loose mock session (SendPacket is a no-op).</summary>
    private static InternalConnection CreateConnection() => new(Mock.Of<ISession>().Object);

    /// <summary>Creates an InternalConnection with a verifiable mock session.</summary>
    private static (InternalConnection connection, Mock<ISession> sessionMock) CreateTrackedConnection()
    {
        var mock = Mock.Of<ISession>();
        mock.Ip.Returns(System.Net.IPAddress.Loopback);
        return (new InternalConnection(mock.Object), mock);
    }

    [Test]
    public async Task Load_VisibleServer_IsAvailableViaGetGameServer()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));

        sut.Load();

        var server = sut.GetGameServer(s_gsId1);
        await Assert.That(server).IsNotNull();
        await Assert.That(server!.Name).IsEqualTo("World1");
        await Assert.That(server.Host).IsEqualTo("127.0.0.1");
        await Assert.That(server.Port).IsEqualTo((ushort)1234);
    }

    [Test]
    public async Task Load_HiddenServer_IsNotAvailableViaGetGameServer()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "HiddenWorld", "127.0.0.1", 1234, true)));

        sut.Load();

        await Assert.That(sut.GetGameServer(s_gsId1)).IsNull();
    }

    [Test]
    public async Task Load_MixedServers_OnlyLoadsVisibleOnes()
    {
        var sut = CreateSut(MakeConfig(
            (s_gsId1.Value, "Visible", "127.0.0.1", 1234, false),
            (s_gsId2.Value, "Hidden", "127.0.0.2", 5678, true)));

        sut.Load();

        await Assert.That(sut.GetGameServer(s_gsId1)).IsNotNull();
        await Assert.That(sut.GetGameServer(s_gsId2)).IsNull();
    }

    [Test]
    public async Task GetGameServer_UnknownId_ReturnsNull()
    {
        var sut = CreateSut(MakeConfig());

        await Assert.That(sut.GetGameServer(s_gsId1)).IsNull();
    }

    [Test]
    public async Task TryGetParentId_NoMirrorRegistered_ReturnsFalse()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();

        var found = sut.TryGetParentId(s_gsId1, out _);

        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task TryGetParentId_AfterAddWithMirror_ReturnsTrueWithParentId()
    {
        var sut = CreateSut(MakeConfig(
            (s_gsId1.Value, "World1", "127.0.0.1", 1234, false),
            (s_gsId2.Value, "Mirror", "127.0.0.2", 1234, false)));
        sut.Load();
        sut.Add(s_gsId1, [s_gsId2], CreateConnection());

        var found = sut.TryGetParentId(s_gsId2, out var parentId);

        await Assert.That(found).IsTrue();
        await Assert.That(parentId).IsEqualTo(s_gsId1);
    }

    [Test]
    public async Task Add_KnownServerId_MakesServerActive()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();

        sut.Add(s_gsId1, [], CreateConnection());

        await Assert.That(sut.GetGameServer(s_gsId1)!.Active).IsTrue();
    }

    [Test]
    public async Task Add_WithMirrors_MakesMirrorServersActive()
    {
        var sut = CreateSut(MakeConfig(
            (s_gsId1.Value, "World1", "127.0.0.1", 1234, false),
            (s_gsId2.Value, "Mirror1", "127.0.0.2", 1234, false),
            (s_gsId3.Value, "Mirror2", "127.0.0.3", 1234, false)));
        sut.Load();

        sut.Add(s_gsId1, [s_gsId2, s_gsId3], CreateConnection());

        await Assert.That(sut.GetGameServer(s_gsId2)!.Active).IsTrue();
        await Assert.That(sut.GetGameServer(s_gsId3)!.Active).IsTrue();
    }

    [Test]
    public async Task Remove_AfterAdd_ClearsServerConnection()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();
        sut.Add(s_gsId1, [], CreateConnection());

        sut.Remove(s_gsId1);

        await Assert.That(sut.GetGameServer(s_gsId1)!.Active).IsFalse();
    }

    [Test]
    public async Task Remove_WithMirrors_ClearsMirrorConnections()
    {
        var sut = CreateSut(MakeConfig(
            (s_gsId1.Value, "World1", "127.0.0.1", 1234, false),
            (s_gsId2.Value, "Mirror", "127.0.0.2", 1234, false)));
        sut.Load();
        sut.Add(s_gsId1, [s_gsId2], CreateConnection());

        sut.Remove(s_gsId1);

        await Assert.That(sut.GetGameServer(s_gsId2)!.Active).IsFalse();
        await Assert.That(sut.TryGetParentId(s_gsId2, out _)).IsFalse();
    }

    [Test]
    public void Remove_UnknownServerId_DoesNotThrow()
    {
        var sut = CreateSut(MakeConfig());

        sut.Remove(s_gsId1);
    }

    [Test]
    public async Task SetLoad_SetsLoadOnServer()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();

        sut.SetLoad(s_gsId1, (byte)GSLoad.High);

        await Assert.That(sut.GetGameServer(s_gsId1)!.Load).IsEqualTo(GSLoad.High);
    }

    [Test]
    public void RequestEnterWorld_UnknownGsId_DoesNotThrow()
    {
        var sut = CreateSut(MakeConfig());

        sut.RequestEnterWorld(s_accountId, s_connectionId, s_gsId1);
    }

    [Test]
    public void RequestEnterWorld_KnownButInactiveServer_DoesNotThrow()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load(); // loaded but never Add()ed, so inactive

        sut.RequestEnterWorld(s_accountId, s_connectionId, s_gsId1);
    }

    [Test]
    public void RequestEnterWorld_ActiveServer_SendsPacketToSession()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();
        var (connection, sessionMock) = CreateTrackedConnection();
        sut.Add(s_gsId1, [], connection);
        // Note: Add() sends 1 packet; RequestEnterWorld() sends 1 more

        sut.RequestEnterWorld(s_accountId, s_connectionId, s_gsId1);

        sessionMock.SendPacket(Any<byte[]>()).WasCalled(Times.Exactly(2));
    }

    [Test]
    public void RouteEnterWorldResponse_ConnectionNotFound_DoesNotThrow()
    {
        var sut = CreateSut(MakeConfig());
        _connectionTable.GetConnection(s_connectionId).Returns(default(ILoginConnection?));

        sut.RouteEnterWorldResponse(s_connectionId, s_gsId1, 0);
    }

    [Test]
    public void RouteEnterWorldResponse_ConnectionFound_DelegatesToSession()
    {
        var sut = CreateSut(MakeConfig());
        var sessionMock = Mock.Of<ILoginSession>();
        var connectionMock = Mock.Of<ILoginConnection>();
        connectionMock.Session.Returns(sessionMock.Object);
        _connectionTable.GetConnection(s_connectionId).Returns(connectionMock.Object);

        sut.RouteEnterWorldResponse(s_connectionId, s_gsId1, 42);

        sessionMock.CompleteEnterWorldRequest(s_gsId1, 42).WasCalled(Times.Once);
    }

    [Test]
    public async Task GetWorldListAsync_NoServersLoaded_ReturnsEmptyResult()
    {
        var sut = CreateSut(MakeConfig());
        var connectionMock = Mock.Of<ILoginConnection>();
        connectionMock.GetCharacters().Returns([]);

        var result = await sut.GetWorldListAsync(connectionMock.Object);

        await Assert.That(result.GameServers).IsEmpty();
        await Assert.That(result.Characters).IsEmpty();
    }

    [Test]
    public async Task GetWorldListAsync_LoadedButInactiveServers_ReturnsServersWithoutRequesting()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();
        var connectionMock = Mock.Of<ILoginConnection>();
        connectionMock.GetCharacters().Returns([]);

        var result = await sut.GetWorldListAsync(connectionMock.Object);

        await Assert.That(result.GameServers.Count).IsEqualTo(1);
        await Assert.That(result.Characters).IsEmpty();
        _requestController.Create(Any<int>(), Any<TimeSpan>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task GetWorldListAsync_ActiveServer_RequestsCharacterInfoPerServer()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();
        sut.Add(s_gsId1, [], CreateConnection());

        var connectionMock = Mock.Of<ILoginConnection>();
        connectionMock.Id.Returns(s_connectionId);
        connectionMock.AccountId.Returns(s_accountId);
        connectionMock.Characters.Returns(new Dictionary<GameServerId, List<LoginCharacterInfo>>());
        connectionMock.GetCharacters().Returns([]);
        _requestController.Create(1, Any<TimeSpan>()).Returns(([1u], Task.CompletedTask));

        var result = await sut.GetWorldListAsync(connectionMock.Object);

        await Assert.That(result.GameServers.Count).IsEqualTo(1);
        _requestController.Create(1, Any<TimeSpan>()).WasCalled(Times.Once);
    }
}
