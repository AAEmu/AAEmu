#nullable enable

using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.UnitTests.Login.Core.PacketHandlers.C2L;

public class CAPcCertNumberPacketHandlerTests
{
    private readonly Mock<ILoginSession> _session = Mock.Of<ILoginSession>();
    private readonly Mock<ILoginClient> _client = Mock.Of<ILoginClient>();
    private readonly CAPcCertNumberPacketHandler _handler = new();

    public CAPcCertNumberPacketHandlerTests()
    {
        _session.Client.Returns(_client.Object);
    }

    [Test]
    public async Task Execute_CallsContinueAuthAsync()
    {
        // Arrange
        var packet = CreatePacket("12345678");

        // Act
        await _handler.Execute(packet, _session.Object, CancellationToken.None);

        // Assert
        _session.ContinueAuthAsync(
            Any<Func<ICertAuthFlow, Task<AuthFlowResult>>>(),
            Any<CancellationToken>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task Execute_NullCertNumber_CallsContinueAuthAsync()
    {
        // Arrange
        var packet = CreatePacket(null);

        // Act
        await _handler.Execute(packet, _session.Object, CancellationToken.None);

        // Assert
        _session.ContinueAuthAsync(
            Any<Func<ICertAuthFlow, Task<AuthFlowResult>>>(),
            Any<CancellationToken>()).WasCalled(Times.Once);
    }

    private static CAPcCertNumberPacket CreatePacket(string? certNumber)
    {
        var packet = new CAPcCertNumberPacket();
        var certProperty = typeof(CAPcCertNumberPacket).GetProperty(nameof(CAPcCertNumberPacket.CertNumber));
        certProperty!.SetValue(packet, certNumber);
        return packet;
    }
}
