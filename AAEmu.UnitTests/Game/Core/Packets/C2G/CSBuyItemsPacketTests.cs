using AAEmu.Game.Core.Packets.C2G;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// Tests for CSBuyItemsPacket class
/// </summary>
public class CSBuyItemsPacketTests
{
    [Fact]
    public void Constructor_InitializesPacket()
    {
        // Arrange & Act
        var packet = new CSBuyItemsPacket();

        // Assert
        Assert.NotNull(packet);
    }
}
