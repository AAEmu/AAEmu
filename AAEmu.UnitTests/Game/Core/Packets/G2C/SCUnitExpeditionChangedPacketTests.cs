using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public sealed class SCUnitExpeditionChangedPacketTests
{
    [Test]
    public async Task WireIsBcThenCharacterIdThenNamesThenOldNewAndExpel()
    {
        // Login/create self path uses unitId=0; observers get the real bc.
        var stream = new SCUnitExpeditionChangedPacket(
                0, 8, "", "Tester", 0, 1002, false)
            .Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadBc()).IsEqualTo(0u);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(8uL);
        await Assert.That(stream.ReadString()).IsEqualTo("");
        await Assert.That(stream.ReadString()).IsEqualTo("Tester");
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1002u);
        await Assert.That(stream.ReadBoolean()).IsFalse();
    }

    [Test]
    public async Task ObserverPathCarriesTheUnitBc()
    {
        var stream = new SCUnitExpeditionChangedPacket(
                0x40c, 8, "", "Tester", 0, 1002, false)
            .Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadBc()).IsEqualTo(0x40cu);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(8uL);
        stream.ReadString();
        stream.ReadString();
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1002u);
        await Assert.That(stream.ReadBoolean()).IsFalse();
    }
}
