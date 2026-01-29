#nullable enable
using AAEmu.Commons.Models;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Login.Core.Controllers;

public class GameControllerTests
{
    private readonly Mock<IRequestController> _requestController = new();
    private readonly Mock<ILoginConnectionTable> _connectionTable = new();

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
    private static InternalConnection CreateConnection() => new(Mock.Of<ISession>());

    /// <summary>Creates an InternalConnection with a verifiable mock session.</summary>
    private static (InternalConnection connection, Mock<ISession> sessionMock) CreateTrackedConnection()
    {
        var mock = new Mock<ISession>();
        mock.Setup(s => s.Ip).Returns(System.Net.IPAddress.Loopback);
        return (new InternalConnection(mock.Object), mock);
    }

    [Fact]
    public void Load_VisibleServer_IsAvailableViaGetGameServer()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));

        sut.Load();

        var server = sut.GetGameServer(s_gsId1);
        Assert.NotNull(server);
        Assert.Equal("World1", server.Name);
        Assert.Equal("127.0.0.1", server.Host);
        Assert.Equal(1234, server.Port);
    }

    [Fact]
    public void Load_HiddenServer_IsNotAvailableViaGetGameServer()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "HiddenWorld", "127.0.0.1", 1234, true)));

        sut.Load();

        Assert.Null(sut.GetGameServer(s_gsId1));
    }

    [Fact]
    public void Load_MixedServers_OnlyLoadsVisibleOnes()
    {
        var sut = CreateSut(MakeConfig(
            (s_gsId1.Value, "Visible", "127.0.0.1", 1234, false),
            (s_gsId2.Value, "Hidden", "127.0.0.2", 5678, true)));

        sut.Load();

        Assert.NotNull(sut.GetGameServer(s_gsId1));
        Assert.Null(sut.GetGameServer(s_gsId2));
    }

    [Fact]
    public void GetGameServer_UnknownId_ReturnsNull()
    {
        var sut = CreateSut(MakeConfig());

        Assert.Null(sut.GetGameServer(s_gsId1));
    }

    [Fact]
    public void TryGetParentId_NoMirrorRegistered_ReturnsFalse()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();

        var found = sut.TryGetParentId(s_gsId1, out _);

        Assert.False(found);
    }

    [Fact]
    public void TryGetParentId_AfterAddWithMirror_ReturnsTrueWithParentId()
    {
        var sut = CreateSut(MakeConfig(
            (s_gsId1.Value, "World1", "127.0.0.1", 1234, false),
            (s_gsId2.Value, "Mirror", "127.0.0.2", 1234, false)));
        sut.Load();
        sut.Add(s_gsId1, [s_gsId2], CreateConnection());

        var found = sut.TryGetParentId(s_gsId2, out var parentId);

        Assert.True(found);
        Assert.Equal(s_gsId1, parentId);
    }

    [Fact]
    public void Add_KnownServerId_MakesServerActive()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();

        sut.Add(s_gsId1, [], CreateConnection());

        Assert.True(sut.GetGameServer(s_gsId1)!.Active);
    }

    [Fact]
    public void Add_WithMirrors_MakesMirrorServersActive()
    {
        var sut = CreateSut(MakeConfig(
            (s_gsId1.Value, "World1", "127.0.0.1", 1234, false),
            (s_gsId2.Value, "Mirror1", "127.0.0.2", 1234, false),
            (s_gsId3.Value, "Mirror2", "127.0.0.3", 1234, false)));
        sut.Load();

        sut.Add(s_gsId1, [s_gsId2, s_gsId3], CreateConnection());

        Assert.True(sut.GetGameServer(s_gsId2)!.Active);
        Assert.True(sut.GetGameServer(s_gsId3)!.Active);
    }

    [Fact]
    public void Remove_AfterAdd_ClearsServerConnection()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();
        sut.Add(s_gsId1, [], CreateConnection());

        sut.Remove(s_gsId1);

        Assert.False(sut.GetGameServer(s_gsId1)!.Active);
    }

    [Fact]
    public void Remove_WithMirrors_ClearsMirrorConnections()
    {
        var sut = CreateSut(MakeConfig(
            (s_gsId1.Value, "World1", "127.0.0.1", 1234, false),
            (s_gsId2.Value, "Mirror", "127.0.0.2", 1234, false)));
        sut.Load();
        sut.Add(s_gsId1, [s_gsId2], CreateConnection());

        sut.Remove(s_gsId1);

        Assert.False(sut.GetGameServer(s_gsId2)!.Active);
        Assert.False(sut.TryGetParentId(s_gsId2, out _));
    }

    [Fact]
    public void Remove_UnknownServerId_DoesNotThrow()
    {
        var sut = CreateSut(MakeConfig());

        sut.Remove(s_gsId1);
    }

    [Fact]
    public void SetLoad_SetsLoadOnServer()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();

        sut.SetLoad(s_gsId1, (byte)GSLoad.High);

        Assert.Equal(GSLoad.High, sut.GetGameServer(s_gsId1)!.Load);
    }

    [Fact]
    public void RequestEnterWorld_UnknownGsId_DoesNotThrow()
    {
        var sut = CreateSut(MakeConfig());

        sut.RequestEnterWorld(s_accountId, s_connectionId, s_gsId1);
    }

    [Fact]
    public void RequestEnterWorld_KnownButInactiveServer_DoesNotThrow()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load(); // loaded but never Add()ed, so inactive

        sut.RequestEnterWorld(s_accountId, s_connectionId, s_gsId1);
    }

    [Fact]
    public void RequestEnterWorld_ActiveServer_SendsPacketToSession()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();
        var (connection, sessionMock) = CreateTrackedConnection();
        sut.Add(s_gsId1, [], connection);
        sessionMock.Invocations.Clear(); // discard the packet sent during Add()

        sut.RequestEnterWorld(s_accountId, s_connectionId, s_gsId1);

        sessionMock.Verify(s => s.SendPacket(It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void RouteEnterWorldResponse_ConnectionNotFound_DoesNotThrow()
    {
        var sut = CreateSut(MakeConfig());
        _connectionTable.Setup(t => t.GetConnection(s_connectionId)).Returns(default(ILoginConnection?));

        sut.RouteEnterWorldResponse(s_connectionId, s_gsId1, 0);
    }

    [Fact]
    public void RouteEnterWorldResponse_ConnectionFound_DelegatesToSession()
    {
        var sut = CreateSut(MakeConfig());
        var sessionMock = new Mock<ILoginSession>();
        var connectionMock = new Mock<ILoginConnection>();
        connectionMock.Setup(c => c.Session).Returns(sessionMock.Object);
        _connectionTable.Setup(t => t.GetConnection(s_connectionId)).Returns(connectionMock.Object);

        sut.RouteEnterWorldResponse(s_connectionId, s_gsId1, 42);

        sessionMock.Verify(s => s.CompleteEnterWorldRequest(s_gsId1, 42), Times.Once);
    }

    [Fact]
    public async Task GetWorldListAsync_NoServersLoaded_ReturnsEmptyResult()
    {
        var sut = CreateSut(MakeConfig());
        var connectionMock = new Mock<ILoginConnection>();
        connectionMock.Setup(c => c.GetCharacters()).Returns([]);

        var result = await sut.GetWorldListAsync(connectionMock.Object);

        Assert.Empty(result.GameServers);
        Assert.Empty(result.Characters);
    }

    [Fact]
    public async Task GetWorldListAsync_LoadedButInactiveServers_ReturnsServersWithoutRequesting()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();
        var connectionMock = new Mock<ILoginConnection>();
        connectionMock.Setup(c => c.GetCharacters()).Returns([]);

        var result = await sut.GetWorldListAsync(connectionMock.Object);

        Assert.Single(result.GameServers);
        Assert.Empty(result.Characters);
        _requestController.Verify(r => r.Create(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetWorldListAsync_ActiveServer_RequestsCharacterInfoPerServer()
    {
        var sut = CreateSut(MakeConfig((s_gsId1.Value, "World1", "127.0.0.1", 1234, false)));
        sut.Load();
        sut.Add(s_gsId1, [], CreateConnection());

        var connectionMock = new Mock<ILoginConnection>();
        connectionMock.Setup(c => c.Id).Returns(s_connectionId);
        connectionMock.Setup(c => c.AccountId).Returns(s_accountId);
        connectionMock.Setup(c => c.Characters)
            .Returns(new Dictionary<GameServerId, List<LoginCharacterInfo>>());
        connectionMock.Setup(c => c.GetCharacters()).Returns([]);
        _requestController.Setup(r => r.Create(1, It.IsAny<int>()))
            .Returns(([1u], Task.CompletedTask));

        var result = await sut.GetWorldListAsync(connectionMock.Object);

        Assert.Single(result.GameServers);
        _requestController.Verify(r => r.Create(1, It.IsAny<int>()), Times.Once);
    }
}
