#nullable enable

using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.UnitTests.Login.Core.PacketHandlers.C2L;

public class CAOtpNumberPacketHandlerTests
{
    private readonly Mock<ILoginSession> _session = Mock.Of<ILoginSession>();
    private readonly Mock<ILoginClient> _client = Mock.Of<ILoginClient>();
    private readonly CAOtpNumberPacketHandler _handler = new();

    public CAOtpNumberPacketHandlerTests()
    {
        _session.Client.Returns(_client.Object);
    }

    [Test]
    public async Task Execute_CallsContinueAuthAsync()
    {
        // Arrange
        var packet = CreatePacket("123456");

        // Act
        await _handler.Execute(packet, _session.Object, CancellationToken.None);

        // Assert
        _session.ContinueAuthAsync(
            Any<Func<IOtpAuthFlow, Task<AuthFlowResult>>>(),
            Any<CancellationToken>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task Execute_NullOtpNumber_CallsContinueAuthAsync()
    {
        // Arrange
        var packet = CreatePacket(null);

        // Act
        await _handler.Execute(packet, _session.Object, CancellationToken.None);

        // Assert
        _session.ContinueAuthAsync(
            Any<Func<IOtpAuthFlow, Task<AuthFlowResult>>>(),
            Any<CancellationToken>()).WasCalled(Times.Once);
    }

    private static CAOtpNumberPacket CreatePacket(string? otpNumber)
    {
        var packet = new CAOtpNumberPacket();
        var otpProperty = typeof(CAOtpNumberPacket).GetProperty(nameof(CAOtpNumberPacket.OtpNumber));
        otpProperty!.SetValue(packet, otpNumber);
        return packet;
    }
}
