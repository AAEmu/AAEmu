using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCTimeOfDayPacketTests
{
    [Test]
    public async Task DetailedPacket_WritesAllFourZoneValues()
    {
        var stream = new PacketStream();
        new SCDetailedTimeOfDayPacket(7.5f, 0.25f, 3f, 21f).Write(stream);
        var body = stream.GetBytes();

        await Assert.That(body.Length).IsEqualTo(16);
        await Assert.That(BitConverter.ToSingle(body, 0)).IsEqualTo(7.5f);
        await Assert.That(BitConverter.ToSingle(body, 4)).IsEqualTo(0.25f);
        await Assert.That(BitConverter.ToSingle(body, 8)).IsEqualTo(3f);
        await Assert.That(BitConverter.ToSingle(body, 12)).IsEqualTo(21f);
    }

    [Test]
    public async Task TimePacket_WritesOneFloatAndNoTail()
    {
        var stream = new PacketStream();
        new SCTimeOfDayPacket(18.75f).Write(stream);
        var body = stream.GetBytes();

        await Assert.That(body.Length).IsEqualTo(4);
        await Assert.That(BitConverter.ToSingle(body, 0)).IsEqualTo(18.75f);
    }
}
