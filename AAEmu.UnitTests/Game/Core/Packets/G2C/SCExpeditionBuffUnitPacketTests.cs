using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public sealed class SCExpeditionBuffUnitPacketTests
{
    [Test]
    public async Task WireIsBcThenSizeThenTwelveByteGradeRows()
    {
        var grades = new Dictionary<uint, byte>
        {
            [1] = 1,
            [7] = 1
        };
        var stream = new SCExpeditionBuffUnitPacket(0x420, grades).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadBc()).IsEqualTo(0x420u);
        await Assert.That(stream.ReadInt32()).IsEqualTo(2);

        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);

        await Assert.That(stream.ReadUInt32()).IsEqualTo(7u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);
    }

    [Test]
    public async Task EmptyPurchaseListStillWritesAZeroCount()
    {
        var stream = new SCExpeditionBuffUnitPacket(0x420, new Dictionary<uint, byte>())
            .Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadBc()).IsEqualTo(0x420u);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
    }
}
