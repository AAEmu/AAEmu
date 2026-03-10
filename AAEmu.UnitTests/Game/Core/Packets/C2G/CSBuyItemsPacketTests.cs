using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// Tests for CSBuyItemsPacket class
/// </summary>
public class CSBuyItemsPacketTests
{
    [Test]
    public async Task Constructor_InitializesPacket()
    {
        // Arrange & Act
        var packet = new CSBuyItemsPacket();

        // Assert
        await Assert.That(packet).IsNotNull();
    }
}
