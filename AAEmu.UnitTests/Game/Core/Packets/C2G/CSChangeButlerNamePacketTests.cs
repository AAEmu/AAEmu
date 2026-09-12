using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

public sealed class CSChangeButlerNamePacketTests
{
    [Test]
    public async Task Read_ParsesExactUtf8BodyAndConsumesIt()
    {
        const string name = "Harvest42";
        var stream = new PacketStream(new PacketStream().Write(name).GetBytes());
        var packet = new CSChangeButlerNamePacket();

        packet.Read(stream);

        await Assert.That(packet.Name).IsEqualTo(name);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Read_RejectsTruncatedAndTrailingBodies()
    {
        var exact = new PacketStream().Write("Harvest42").GetBytes();
        var truncated = exact[..^1];
        var trailing = exact.Concat(new byte[] { 0x00 }).ToArray();

        await Assert.That(() => new CSChangeButlerNamePacket().Read(new PacketStream(truncated)))
            .Throws<InvalidDataException>();
        await Assert.That(() => new CSChangeButlerNamePacket().Read(new PacketStream(trailing)))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Read_RejectsNativeStringOverflow()
    {
        var body = new PacketStream()
            .Write(new string('a', ButlerRenameService.MaximumUtf8ByteCount + 1))
            .GetBytes();

        await Assert.That(() => new CSChangeButlerNamePacket().Read(new PacketStream(body)))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Read_RejectsMalformedUtf8()
    {
        var body = new PacketStream()
            .Write((short)2)
            .Write(new byte[] { 0xC3, 0x28 })
            .GetBytes();

        await Assert.That(() => new CSChangeButlerNamePacket().Read(new PacketStream(body)))
            .Throws<InvalidDataException>();
    }
}
