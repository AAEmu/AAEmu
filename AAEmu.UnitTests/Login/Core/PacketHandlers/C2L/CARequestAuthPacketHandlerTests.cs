#nullable enable

using System.Net;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Packets.C2L;
using Microsoft.Extensions.Options;

namespace AAEmu.UnitTests.Login.Core.PacketHandlers.C2L;

public class CARequestAuthPacketHandlerTests
{
    private readonly Mock<ILoginController> _loginController = Mock.Of<ILoginController>();
    private readonly Mock<ILoginSession> _session = Mock.Of<ILoginSession>();
    private readonly Mock<ILoginConnection> _connection = Mock.Of<ILoginConnection>();
    private readonly CARequestAuthPacketHandler _handler;

    public CARequestAuthPacketHandlerTests()
    {
        _handler = new CARequestAuthPacketHandler(
            _loginController.Object,
            Options.Create(new KoreaAuthOptions()),
            Options.Create(new KoreaChallengeAuthOptions()));

        _connection.Ip.Returns(IPAddress.Loopback);
        _session.Connection.Returns(_connection.Object);
    }

    [Test]
    public async Task Execute_CallsAuthenticateAsync()
    {
        // Arrange
        var packet = CreatePacket("testuser");

        // Act
        await _handler.Execute(packet, _session.Object, CancellationToken.None);

        // Assert
        _session.AuthenticateAsync(Any<IAuthenticationFlow>(), Any<CancellationToken>()).WasCalled(Times.Once);
    }

    private static CARequestAuthPacket CreatePacket(string username)
    {
        var packet = new CARequestAuthPacket();
        var accountProperty = typeof(CARequestAuthPacket).GetProperty(nameof(CARequestAuthPacket.Account));
        accountProperty!.SetValue(packet, username);
        return packet;
    }
}
