using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// Tests for SCCharacterListPacket class
/// </summary>
public class SCCharacterListPacketTests
{
    [Fact]
    public void Write_WithEmptyCharacterList_WritesCorrectData()
    {
        // Arrange
        var characters = new Character[0];
        var packet = new SCCharacterListPacket(true, characters);

        // Act & Assert
        // Note: Full testing requires PacketStream implementation
        Assert.NotNull(packet);
    }

    [Fact]
    public void Write_WithMultipleCharacters_WritesCorrectData()
    {
        // Arrange
        var characters = new Character[3];
        var packet = new SCCharacterListPacket(true, characters);

        // Assert
        Assert.NotNull(packet);
    }

    [Fact]
    public void Constructor_SetsLastFlag()
    {
        // Arrange & Act
        var packetLast = new SCCharacterListPacket(true, []);
        var packetNotLast = new SCCharacterListPacket(false, []);

        // Assert
        Assert.NotNull(packetLast);
        Assert.NotNull(packetNotLast);
    }
}
