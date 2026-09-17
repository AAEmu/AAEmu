using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class ResidentAndSensitiveOperationPacketTests
{
    [Test]
    public async Task ResidentMap_WritesTheZoneGroupThenTheAddOption()
    {
        var body = new SCResidentMapPacket(7).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write((short)7);
        expected.Write(SCResidentMapPacket.Add);

        // The client's serializer (0x39C60A10 in x2game-dev.dll) writes an i16 and then a byte, and
        // the retail handler at 0x393553D0 in x2game.dll only adds the group to the resident map
        // when that byte is 1 — anything else leaves residency unset.
        await Assert.That(body.Length).IsEqualTo(3);
        await Assert.That(body[2]).IsEqualTo((byte)1);
        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task ResidentInfoList_WritesTotalCountFinalThenTheRows()
    {
        var rows = new List<ResidentInfoRow> { new(33, 12, 0, 0), new(34, 0, 0, 0) };
        var body = new SCResidentInfoListPacket(2, rows).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(2u);
        expected.Write(2u);
        expected.Write(true);
        foreach (var row in rows)
        {
            expected.Write(row.ZoneGroup);
            expected.Write(row.Point);
            expected.Write(row.MoneyAmount);
            expected.Write(row.ZoneMoneyAmount);
            expected.Write(0); // i32 x3, the client's three trailing "type" fields
            expected.Write(0);
            expected.Write(0);
        }

        // 4 + 4 + 1 header, then 34 bytes a row. The client's serializer (0x39C74B00 in
        // x2game-dev.dll) writes u32 total, u32 count, bool final and then the rows, and its row
        // serializer (0x39C70070) writes i16, u32, u64, u64, i32, i32, i32.
        await Assert.That(body.Length).IsEqualTo(9 + (2 * 34));
        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task ResidentInfoList_StopsAtTheHundredRowsTheClientCanHold()
    {
        var rows = Enumerable.Range(0, 101).Select(i => new ResidentInfoRow((ushort)i, 0, 0, 0)).ToList();
        var body = new SCResidentInfoListPacket(101, rows).Write(new PacketStream()).GetBytes();

        // The client's reader caps count at 0x64 (0x39C74B7D in x2game-dev.dll) and its constructor
        // sizes the row array at 100 rows, so a 101st row would be left in the stream for whatever
        // packet follows it in the same batch to read.
        await Assert.That(body.Length).IsEqualTo(9 + (100 * 34));
        await Assert.That(body[4..8]).IsEquivalentTo(new PacketStream().Write(100u).GetBytes());
    }

    [Test]
    public async Task ResidentInfoList_WritesAnEmptyPageWithoutRows()
    {
        var body = new SCResidentInfoListPacket(0, []).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(0u);
        expected.Write(0u);
        expected.Write(true);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task ProtectSensitiveOperationResult_WritesOneFlagThenTheRemainingTime()
    {
        var body = new SCProtectSensitiveOperationResultPacket(1, 3600).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write((byte)1);
        expected.Write(3600u);

        // Five bytes: the client reads a u8 and then a u32, so a sixth byte would desync anything
        // that follows in the same batch.
        await Assert.That(body.Length).IsEqualTo(5);
        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task SensitiveOperationVerifyUrl_WritesTheSequenceNumberThenTheUrl()
    {
        var body = new SCSensitiveOperationVerifyUrlPacket(4, "https://example.invalid/verify")
            .Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(4u);
        expected.Write("https://example.invalid/verify");

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task SensitiveOperationVerifySuccess_HasNoBody()
    {
        var body = new SCSensitiveOperationVerifySuccessPacket().Write(new PacketStream()).GetBytes();

        await Assert.That(body.Length).IsEqualTo(0);
    }
}
