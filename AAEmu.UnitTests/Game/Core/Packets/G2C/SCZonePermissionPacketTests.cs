using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The zone-permission expulsion packet. The permission-state refresh is not sent until its body
/// is implemented, so only the expulsion packet is pinned here.
/// </summary>
public sealed class SCZonePermissionPacketTests
{
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
