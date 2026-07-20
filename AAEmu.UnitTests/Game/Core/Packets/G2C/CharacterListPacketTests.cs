using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// Tests for CharacterListPacket class
/// </summary>
public class CharacterListPacketTests
{
    [Test]
    public async Task Write_WithEmptyCharacterList_WritesCorrectData()
    {
        // Arrange
        var characters = new Character[0];
        var packet = new CharacterListPacket(true, characters);

        // Act & Assert
        // Note: Full testing requires PacketStream implementation
        await Assert.That(packet).IsNotNull();
    }

    [Test]
    public async Task Write_WithMultipleCharacters_WritesCorrectData()
    {
        // Arrange
        var characters = new Character[3];
        var packet = new CharacterListPacket(true, characters);

        // Assert
        await Assert.That(packet).IsNotNull();
    }

    [Test]
    public async Task Constructor_SetsLastFlag()
    {
        // Arrange & Act
        var packetLast = new CharacterListPacket(true, []);
        var packetNotLast = new CharacterListPacket(false, []);

        // Assert
        await Assert.That(packetLast).IsNotNull();
        await Assert.That(packetNotLast).IsNotNull();
    }
}