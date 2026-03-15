#nullable enable

using System.Net;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Packets.C2L;
using AAEmu.Login.Core.Services.TwoFactor;
using Microsoft.Extensions.Options;

namespace AAEmu.UnitTests.Login.Core.PacketHandlers.C2L;

public class CARequestAuthPacketHandlerTests
{
    private readonly Mock<IKoreaAuthFlowFactory> _authFlowFactory;
    private readonly Mock<ILoginSession> _session;
    private readonly Mock<ILoginConnection> _connection;
    private readonly CARequestAuthPacketHandler _handler;

    public CARequestAuthPacketHandlerTests()
    {
        _authFlowFactory = Mock.Of<IKoreaAuthFlowFactory>();
        _session = Mock.Of<ILoginSession>();
        _connection = Mock.Of<ILoginConnection>();
        _handler = new CARequestAuthPacketHandler(_authFlowFactory.Object);

        _connection.Ip.Returns(IPAddress.Loopback);
        _session.Connection.Returns(_connection.Object);
    }

    [Test]
    public async Task Execute_CreatesFlowAndCallsAuthenticateAsync()
    {
        // Arrange
        const string Username = "testuser";
        var packet = CreatePacket(Username);
        var flow = CreateFlow(Username);
        _authFlowFactory.Create(Username, IPAddress.Loopback).Returns(flow);

        // Act
        await _handler.Execute(packet, _session.Object, CancellationToken.None);

        // Assert
        _session.AuthenticateAsync(Any<IAuthenticationFlow>(), Any<CancellationToken>()).WasCalled(Times.Once);
    }

    private static KoreaAuthFlow CreateFlow(string username)
    {
        return new KoreaAuthFlow(
            Mock.Of<ILoginController>().Object,
            Mock.Of<ITwoFactorService>().Object,
            Mock.Of<IOtpService>().Object,
            Mock.Of<IPcCertService>().Object,
            Mock.Of<IArsService>().Object,
            Options.Create(new KoreaAuthOptions()),
            username,
            IPAddress.Loopback);
    }

    private static CARequestAuthPacket CreatePacket(string username)
    {
        var packet = new CARequestAuthPacket();
        var accountProperty = typeof(CARequestAuthPacket).GetProperty(nameof(CARequestAuthPacket.Account));
        accountProperty!.SetValue(packet, username);
        return packet;
    }
}
