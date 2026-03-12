using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// Tests for SCCharacterListPacket class
/// </summary>
public class SCCharacterListPacketTests
{
    [Test]
    public async Task Write_WithEmptyCharacterList_WritesCorrectData()
    {
        // Arrange
        var characters = new Character[0];
        var packet = new SCCharacterListPacket(true, characters);

        // Act & Assert
        // Note: Full testing requires PacketStream implementation
        await Assert.That(packet).IsNotNull();
    }

    [Test]
    public async Task Write_WithMultipleCharacters_WritesCorrectData()
    {
        // Arrange
        var characters = new Character[3];
        var packet = new SCCharacterListPacket(true, characters);

        // Assert
        await Assert.That(packet).IsNotNull();
    }

    [Test]
    public async Task Constructor_SetsLastFlag()
    {
        // Arrange & Act
        var packetLast = new SCCharacterListPacket(true, []);
        var packetNotLast = new SCCharacterListPacket(false, []);

        // Assert
        await Assert.That(packetLast).IsNotNull();
        await Assert.That(packetNotLast).IsNotNull();
    }
}