using System.Buffers;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.UnitTests.Login.Core.Network.Login;

public class LoginProtocolHandlerTests
{
    private readonly LoginProtocolHandler _cut = new();

    [Test]
    public async Task TryParsePacket_ValidData_ReturnsTrue()
    {
        // Arrange
        const ushort PacketType = 42;
        var packetData = new byte[]
        {
            0x06, 0x00, // Packet length (6 bytes)
            0x2A, 0x00, // Packet type (42)
            0x01, 0x02, 0x03, 0x04 // Packet payload
        };

        var buffer = new ReadOnlySequence<byte>(packetData);

        // Act
        var result = _cut.TryParsePacket(ref buffer, out var parsedType, out var packet);

        // Assert
        await Assert.That(result).IsTrue();
        await Assert.That(parsedType).IsEqualTo(PacketType);
        await Assert.That(packet).IsNotNull();
        await Assert.That(packet.Count).IsEqualTo(4);
        await Assert.That(buffer.Start.GetInteger()).IsEqualTo(8);
        await Assert.That(buffer.End.GetInteger()).IsEqualTo(8);
    }

    [Test]
    public async Task TryParsePacket_ValidDataFollowedByPartialData_MutatesBuffer()
    {
        // Arrange
        const ushort PacketType = 42;
        var packetData = new byte[]
        {
            0x06, 0x00, // Packet length (6 bytes)
            0x2A, 0x00, // Packet type (42)
            0x01, 0x02, 0x03, 0x04, // Packet payload
            0x08, 0x00 // Partial data for the next packet (only length, no type or payload)
        };

        var buffer = new ReadOnlySequence<byte>(packetData);

        // Act
        var result = _cut.TryParsePacket(ref buffer, out var parsedType, out var packet);

        // Assert
        await Assert.That(result).IsTrue();
        await Assert.That(parsedType).IsEqualTo(PacketType);
        await Assert.That(packet).IsNotNull();
        await Assert.That(packet.Count).IsEqualTo(4);
        await Assert.That(buffer.Start.GetInteger()).IsEqualTo(8);
        await Assert.That(buffer.End.GetInteger()).IsEqualTo(10);
    }

    [Test]
    public async Task TryParsePacket_InsufficientData_ReturnsFalse()
    {
        // Arrange (buffer has only 2 bytes which isn't enough to read the length)
        var buffer = new ReadOnlySequence<byte>([0x02, 0x00]);

        // Act
        var result = _cut.TryParsePacket(ref buffer, out _, out _);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task TryParsePacket_InsufficientData_DoesNotMutateBuffer()
    {
        // Arrange (buffer has only 2 bytes which isn't enough to read the length)
        var originalBuffer = new ReadOnlySequence<byte>([0x02, 0x00]);
        var buffer = originalBuffer;

        // Act
        _ = _cut.TryParsePacket(ref buffer, out _, out _);

        // Assert
        await Assert.That(buffer).IsEqualTo(originalBuffer);
    }

    [Test]
    public async Task TryParsePacket_EmptyBuffer_ReturnsFalse()
    {
        // Arrange (empty buffer)
        var buffer = new ReadOnlySequence<byte>([]);

        // Act
        var result = _cut.TryParsePacket(ref buffer, out _, out _);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task TryParsePacket_EmptyBuffer_DoesNotMutateBuffer()
    {
        // Arrange (empty buffer)
        var originalBuffer = new ReadOnlySequence<byte>([]);
        var buffer = originalBuffer;

        // Act
        _ = _cut.TryParsePacket(ref buffer, out _, out _);

        // Assert
        await Assert.That(buffer).IsEqualTo(originalBuffer);
    }

    [Test]
    public async Task TryParsePacket_LargePacketSize_ReturnsTrue()
    {
        // Arrange (a large packet with length 65535 bytes, but valid type and data)
        const ushort PacketType = 42;
        var packetData = new byte[65537]; // 2 bytes for length + 2 for type + 65533 for payload
        packetData[0] = 0xFF; // High byte for length
        packetData[1] = 0xFF; // Low byte for length
        packetData[2] = 0x2A; // Packet type low byte
        packetData[3] = 0x00; // Packet type high byte

        var buffer = new ReadOnlySequence<byte>(packetData);

        // Act
        var result = _cut.TryParsePacket(ref buffer, out var parsedType, out var packet);

        // Assert
        await Assert.That(result).IsTrue();
        await Assert.That(parsedType).IsEqualTo(PacketType);
        await Assert.That(packet.Count).IsEqualTo(65533);
    }
}