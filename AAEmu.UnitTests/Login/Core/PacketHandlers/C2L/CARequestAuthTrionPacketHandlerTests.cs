#nullable enable

using System.Net;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.UnitTests.Login.Core.PacketHandlers.C2L;

public class CARequestAuthTrionPacketHandlerTests
{
    private readonly Mock<ILoginController> _loginController = Mock.Of<ILoginController>();
    private readonly Mock<ILoginSession> _session = Mock.Of<ILoginSession>();
    private readonly Mock<ILoginConnection> _connection = Mock.Of<ILoginConnection>();
    private readonly CARequestAuthTrionPacketHandler _handler;

    public CARequestAuthTrionPacketHandlerTests()
    {
        _handler = new CARequestAuthTrionPacketHandler(_loginController.Object);

        _connection.Ip.Returns(IPAddress.Loopback);
        _session.Connection.Returns(_connection.Object);
    }

    [Test]
    public async Task Execute_CallsAuthenticateAsync()
    {
        // Arrange
        var packet = CreatePacket("testuser", "testpass");

        // Act
        await _handler.Execute(packet, _session.Object, CancellationToken.None);

        // Assert
        _session.AuthenticateAsync(Any<IAuthenticationFlow>(), Any<CancellationToken>()).WasCalled(Times.Once);
    }

    private static CARequestAuthTrionPacket CreatePacket(string username, string password)
    {
        var packet = new CARequestAuthTrionPacket();
        var usernameProperty = typeof(CARequestAuthTrionPacket).GetProperty(nameof(CARequestAuthTrionPacket.Username));
        var passwordProperty = typeof(CARequestAuthTrionPacket).GetProperty(nameof(CARequestAuthTrionPacket.Password));
        usernameProperty!.SetValue(packet, username);
        passwordProperty!.SetValue(packet, password);
        return packet;
    }
}
