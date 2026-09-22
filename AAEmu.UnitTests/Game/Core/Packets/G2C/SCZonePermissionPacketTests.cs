using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The two zone-permission answers, pinned to the shapes the 10.0.2.13 client's own serializers write:
/// 0x086 carries nothing, 0x087 carries a single option byte.
/// </summary>
public sealed class SCZonePermissionPacketTests
{
    [Test]
    public async Task ChangedPacket_IsOpcode086_WithAnEmptyBody()
    {
        var packet = new SCZonePermissionChangedPacket();

        await Assert.That(packet.TypeId).IsEqualTo(SCOffsets.SCZonePermissionChangedPacket);
        await Assert.That(SCOffsets.SCZonePermissionChangedPacket).IsEqualTo((ushort)0x086);

        var stream = packet.Write(new PacketStream());
        await Assert.That(stream.Count).IsEqualTo(0);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ExpelledPacket_IsOpcode087_AndWritesTheOptionByteAlone()
    {
        var packet = new SCZonePermissionExpelledPacket(3);

        await Assert.That(packet.TypeId).IsEqualTo(SCOffsets.SCZonePermissionExpelledPacket);
        await Assert.That(SCOffsets.SCZonePermissionExpelledPacket).IsEqualTo((ushort)0x087);
        await Assert.That(packet.Option).IsEqualTo((byte)3);

        var stream = packet.Write(new PacketStream());
        var bytes = stream.GetBytes();
        await Assert.That(bytes.Length).IsEqualTo(1);
        await Assert.That(new PacketStream(bytes).ReadByte()).IsEqualTo((byte)3);
    }
}
