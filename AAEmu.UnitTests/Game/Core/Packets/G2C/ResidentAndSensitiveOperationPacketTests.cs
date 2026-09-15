using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class ResidentAndSensitiveOperationPacketTests
{
    [Test]
    public async Task ResidentMap_WritesTheZoneGroupThenTheOptionByte()
    {
        var body = new SCResidentMapPacket(7, 1).Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write((short)7);
        expected.Write((byte)1);

        // The client's reader takes a u16 and then an option byte; a short body leaves it one byte
        // into the following packet.
        await Assert.That(body.Length).IsEqualTo(3);
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
            expected.Write(0u);
            expected.Write(0u);
            expected.Write(0u);
        }

        // 4 + 4 + 1 header, then 34 bytes a row.
        await Assert.That(body.Length).IsEqualTo(9 + (2 * 34));
        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
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
