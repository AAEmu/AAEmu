using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// Wire shapes of the resident packets the settlement answers with: the client's serializer order
/// (i16 type, u64 type2, ...) is what these assert, byte for byte against an independently built
/// stream plus explicit lengths so a width drift cannot hide inside a matching order.
/// </summary>
public class ResidentPacketShapeTests
{
    [Test]
    public async Task ResidentInfo_WritesTypeThenType2ThenPoint()
    {
        var body = new SCResidentInfoPacket(33, 0, 75).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write((short)33);
        expected.Write(0ul);
        expected.Write(75u);

        // i16 + u64 + u32: 14 bytes; a fifteenth would desync the batch.
        await Assert.That(body.Length).IsEqualTo(14);
        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task ResidentBalanceInfo_WritesTheSevenFieldsInClientOrder()
    {
        var body = new SCResidentBalanceInfoPacket(33, 0, 4, 75, 150, 250, 500)
            .Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write((short)33); // type (zone group)
        expected.Write(0ul);       // type2
        expected.Write(4u);        // memberCount
        expected.Write(75u);       // point (personal service points)
        expected.Write(150u);      // zonePoint (zone aggregate)
        expected.Write(250ul);     // moneyAmount (personal charge)
        expected.Write(500ul);     // moneyAmount2 (zone hunting charge)

        // i16 + u64 + u32 x3 + u64 x2 = 38 bytes.
        await Assert.That(body.Length).IsEqualTo(38);
        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task ResidentMemberList_WritesHeaderThenTheRowFieldsInClientOrder()
    {
        var updated = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
        var rows = new List<ResidentMemberRow> { new(90, updated, 42, "Resi", 55, 3, 7, true, false) };
        var body = new SCResidentMemberListPacket(33, 1, rows).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write((short)33); // zone group
        expected.Write(1u);        // total
        expected.Write(1u);        // count
        expected.Write(true);      // final
        expected.Write(90u);       // servicePoint
        expected.Write(updated);   // updated (unix seconds)
        expected.Write(42ul);      // charId
        expected.Write("Resi");    // name
        expected.Write((byte)55);  // level
        expected.Write((byte)3);   // heirLevel
        expected.Write(7u);        // family
        expected.Write(true);      // online
        expected.Write(false);     // party

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());

        // Header is i16 + u32 + u32 + bool = 11 bytes; the final flag sits at offset 10,
        // so the first row can only start where the client reads it.
        await Assert.That(body[0..2]).IsEquivalentTo(new PacketStream().Write((short)33).GetBytes());
        await Assert.That(body[2..6]).IsEquivalentTo(new PacketStream().Write(1u).GetBytes());
        await Assert.That(body[6..10]).IsEquivalentTo(new PacketStream().Write(1u).GetBytes());
        await Assert.That(body[10]).IsEqualTo((byte)1);
    }
}
