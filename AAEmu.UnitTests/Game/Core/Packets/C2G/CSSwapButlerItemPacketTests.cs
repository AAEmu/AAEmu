using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

public class CSSwapButlerItemPacketTests
{
    private static readonly byte[] NativeBody =
    [
        0x12, 0x34, 0x56, 0x78,
        0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11
    ];

    [Test]
    public async Task Read_ParsesExactNativeBody()
    {
        var stream = new PacketStream(NativeBody);
        var packet = new CSSwapButlerItemPacket();

        packet.Read(stream);

        await Assert.That(packet.BagType).IsEqualTo((byte)0x12);
        await Assert.That(packet.BagIndex).IsEqualTo((byte)0x34);
        await Assert.That(packet.ButlerType).IsEqualTo((byte)0x56);
        await Assert.That(packet.ButlerIndex).IsEqualTo((byte)0x78);
        await Assert.That(packet.FromType).IsEqualTo((byte)0x12);
        await Assert.That(packet.FromIndex).IsEqualTo((byte)0x34);
        await Assert.That(packet.ToType).IsEqualTo((byte)0x56);
        await Assert.That(packet.ToIndex).IsEqualTo((byte)0x78);
        await Assert.That(packet.ButlerItemId).IsEqualTo(0x1122334455667788UL);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Read_RejectsTruncatedBody()
    {
        await Assert.That(() => new CSSwapButlerItemPacket().Read(new PacketStream(NativeBody[..^1])))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Read_RejectsTrailingByte()
    {
        var body = NativeBody.Concat([(byte)0x99]).ToArray();

        await Assert.That(() => new CSSwapButlerItemPacket().Read(new PacketStream(body)))
            .Throws<InvalidDataException>();
    }
}
