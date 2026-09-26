using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.SailingActivity;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The sailing activity's packets, pinned byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// The activity id and the two result codes are written as <b>signed</b> 32-bit values. The raw
/// serializer schema marks all three <c>s32</c> signed, while the derived packet catalog summarises
/// the same fields as unsigned. The raw schema is the authority, and the difference is not
/// cosmetic: an <c>errorCode</c> that is negative in content would come back as a four-billion
/// value through an unsigned field. These tests assert the signed encoding so a future edit back
/// to <c>uint</c> fails here rather than in a live client.
/// </para>
/// </remarks>
public class SailingActivityPacketTests
{
    // CS 0x212, 0x213 and 0x215 all carry the same leading signed activity id and nothing else.
    [Test]
    public async Task Read_EnterTakesOnlyTheSignedActivityId()
    {
        var body = new PacketStream().Write(4242).GetBytes();
        await Assert.That(body).IsEquivalentTo(new byte[] { 0x92, 0x10, 0x00, 0x00 });

        var stream = new PacketStream(body);
        var packet = new CSSailingActivityEnterPacket();
        packet.Read(stream);

        await Assert.That(packet.ActivityId).IsEqualTo(4242);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Read_LeaveTakesOnlyTheSignedActivityId()
    {
        var stream = new PacketStream(new PacketStream().Write(4242).GetBytes());
        var packet = new CSSailingActivityLeavePacket();
        packet.Read(stream);

        await Assert.That(packet.ActivityId).IsEqualTo(4242);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Read_RequestDataTakesOnlyTheSignedActivityId()
    {
        var stream = new PacketStream(new PacketStream().Write(4242).GetBytes());
        var packet = new CSSailingActivityRequestDataPacket();
        packet.Read(stream);

        await Assert.That(packet.ActivityId).IsEqualTo(4242);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Read_PreservesANegativeActivityIdInsteadOfWrappingIt()
    {
        var body = new PacketStream().Write(-7).GetBytes();
        await Assert.That(body).IsEquivalentTo(new byte[] { 0xF9, 0xFF, 0xFF, 0xFF });

        var leave = new CSSailingActivityLeavePacket();
        leave.Read(new PacketStream(body));

        await Assert.That(leave.ActivityId).IsEqualTo(-7);
    }

    [Test]
    public async Task Read_KeepsTheClaimContainerVerbatimAndNeverDecodesIt()
    {
        // Five opaque bytes stand in for the unrecovered element vector.
        var body = new PacketStream()
            .Write(1)
            .Write((byte)0xDE).Write((byte)0xAD).Write((byte)0xBE).Write((byte)0xEF).Write((byte)0x01)
            .GetBytes();

        var packet = new CSSailingActivityClaimRewardPacket();
        packet.Read(new PacketStream(body));

        await Assert.That(packet.ActivityId).IsEqualTo(1);
        await Assert.That(packet.Container.Raw).IsEquivalentTo(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01 });
        await Assert.That(packet.Container.IsDecoded).IsFalse();
    }

    [Test]
    public async Task Read_LeavesTheContainerEmptyWhenTheBodyCarriesNothing()
    {
        var packet = new CSSailingActivityClaimRewardPacket();
        packet.Read(new PacketStream(new PacketStream().Write(1).GetBytes()));

        await Assert.That(packet.ActivityId).IsEqualTo(1);
        await Assert.That(packet.Container.IsEmpty).IsTrue();
    }

    // SC 0x38F and SC 0x390 are the same eight-byte shape under two opcodes.
    [Test]
    public async Task Error_WritesEightBytesWithASignedCode()
    {
        var bytes = new SCSailingActivityErrorPacket(1, -5).Write(new PacketStream()).GetBytes();

        await Assert.That(bytes).IsEquivalentTo(
            new PacketStream().Write(1).Write(-5).GetBytes());
        await Assert.That(bytes.Length).IsEqualTo(8);
    }

    [Test]
    public async Task Error_EncodesANegativeCodeAsOnesComplementNotAsAHugeUnsigned()
    {
        var bytes = new SCSailingActivityErrorPacket(1, -5).Write(new PacketStream()).GetBytes();

        await Assert.That(bytes[4]).IsEqualTo((byte)0xFB);
        await Assert.That(bytes[5]).IsEqualTo((byte)0xFF);
        await Assert.That(bytes[6]).IsEqualTo((byte)0xFF);
        await Assert.That(bytes[7]).IsEqualTo((byte)0xFF);
    }

    [Test]
    public async Task EnterResponse_WritesEightBytesWithASignedCode()
    {
        var bytes = new SCSailingActivityEnterResponsePacket(4242, 0).Write(new PacketStream()).GetBytes();

        await Assert.That(bytes).IsEquivalentTo(
            new PacketStream().Write(4242).Write(0).GetBytes());
        await Assert.That(bytes.Length).IsEqualTo(8);
    }

    [Test]
    public async Task PointsChanged_WritesEightBytes()
    {
        var bytes = new SCSailingActivityPointsChangedPacket(1, 30).Write(new PacketStream()).GetBytes();

        await Assert.That(bytes).IsEquivalentTo(new PacketStream().Write(1).Write(30).GetBytes());
        await Assert.That(bytes.Length).IsEqualTo(8);
    }

    [Test]
    public async Task StageUnlocked_WritesTheIdThenTheContainerVerbatim()
    {
        var container = SailingActivityContainer.FromRaw([0x01, 0x02, 0x03]);
        var bytes = new SCSailingActivityStageUnlockedPacket(4, container).Write(new PacketStream()).GetBytes();

        await Assert.That(bytes).IsEquivalentTo(
            new PacketStream().Write(4).Write((byte)0x01).Write((byte)0x02).Write((byte)0x03).GetBytes());
    }

    [Test]
    public async Task StageUnlocked_WritesOnlyTheIdWhenTheContainerIsEmpty()
    {
        var bytes = new SCSailingActivityStageUnlockedPacket(4, null).Write(new PacketStream()).GetBytes();

        await Assert.That(bytes.Length).IsEqualTo(4);
        await Assert.That(bytes).IsEquivalentTo(new PacketStream().Write(4).GetBytes());
    }

    [Test]
    public async Task ClaimRewardResponse_WritesTheIdThenAllThreeContainers()
    {
        var bytes = new SCSailingActivityClaimRewardResponsePacket(
                1,
                SailingActivityContainer.FromRaw([0x0A]),
                SailingActivityContainer.Empty,
                SailingActivityContainer.FromRaw([0x0B, 0x0C]))
            .Write(new PacketStream()).GetBytes();

        await Assert.That(bytes).IsEquivalentTo(
            new PacketStream().Write(1).Write((byte)0x0A).Write((byte)0x0B).Write((byte)0x0C).GetBytes());
    }

    [Test]
    public async Task List_LeadsWithACountThenTheRows()
    {
        SailingActivityListRow[] rows =
        [
            new(1, 10ul, 20ul),
            new(2, 30ul, 40ul)
        ];

        var stream = new SCSailingActivityListPacket(rows).Write(new PacketStream());
        var bytes = stream.GetBytes();
        stream.Rollback();

        await Assert.That(stream.ReadInt32()).IsEqualTo(2);
        await Assert.That(stream.ReadInt32()).IsEqualTo(1);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(10ul);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(20ul);
        await Assert.That(stream.ReadInt32()).IsEqualTo(2);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(30ul);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(40ul);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);

        // 4 + 2 * (4 + 8 + 8)
        await Assert.That(bytes.Length).IsEqualTo(4 + 2 * 20);
    }

    [Test]
    public async Task List_WritesOnlyTheCountWhenThereAreNoRows()
    {
        var bytes = new SCSailingActivityListPacket([]).Write(new PacketStream()).GetBytes();

        await Assert.That(bytes).IsEquivalentTo(new PacketStream().Write(0).GetBytes());
        await Assert.That(bytes.Length).IsEqualTo(4);
    }

    [Test]
    public async Task List_TreatsANullRowArrayAsEmpty()
    {
        var bytes = new SCSailingActivityListPacket(null).Write(new PacketStream()).GetBytes();

        await Assert.That(bytes).IsEquivalentTo(new PacketStream().Write(0).GetBytes());
    }

    [Test]
    public async Task List_RefusesMoreRowsThanTheSerializationBound()
    {
        var rows = new SailingActivityListRow[SCSailingActivityListPacket.MaximumRows + 1];

        await Assert.That(() => new SCSailingActivityListPacket(rows).Write(new PacketStream()))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task List_CarriesAFullWidthStamp()
    {
        // A 64-bit stamp must not be truncated: the largest value the wire allows has to survive.
        var bytes = new SCSailingActivityListPacket([new SailingActivityListRow(1, ulong.MaxValue, ulong.MaxValue)])
            .Write(new PacketStream()).GetBytes();

        var stream = new PacketStream(bytes);
        stream.Rollback();
        await Assert.That(stream.ReadInt32()).IsEqualTo(1);
        await Assert.That(stream.ReadInt32()).IsEqualTo(1);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(ulong.MaxValue);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(ulong.MaxValue);
    }

    [Test]
    public async Task Offsets_MatchTheExtractedSailingActivitySlots()
    {
        await Assert.That(CSOffsets.CSSailingActivityEnterPacket).IsEqualTo((ushort)0x212);
        await Assert.That(CSOffsets.CSSailingActivityLeavePacket).IsEqualTo((ushort)0x213);
        await Assert.That(CSOffsets.CSSailingActivityClaimRewardPacket).IsEqualTo((ushort)0x214);
        await Assert.That(CSOffsets.CSSailingActivityRequestDataPacket).IsEqualTo((ushort)0x215);

        await Assert.That(SCOffsets.SCSailingActivityStageUnlockedPacket).IsEqualTo((ushort)0x38D);
        await Assert.That(SCOffsets.SCSailingActivityClaimRewardResponsePacket).IsEqualTo((ushort)0x38E);
        await Assert.That(SCOffsets.SCSailingActivityErrorPacket).IsEqualTo((ushort)0x38F);
        await Assert.That(SCOffsets.SCSailingActivityEnterResponsePacket).IsEqualTo((ushort)0x390);
        await Assert.That(SCOffsets.SCSailingActivityListPacket).IsEqualTo((ushort)0x391);
        await Assert.That(SCOffsets.SCSailingActivityPointsChangedPacket).IsEqualTo((ushort)0x392);
    }

    [Test]
    public async Task UnresolvedPackets_KeepTheirNamedPlaceholderRatherThanAGuessedSlot()
    {
        // No extraction recovered a slot for these two, so no offset is registered for them and the
        // placeholders stay at zero. A future capture replaces the constant rather than the number
        // being quietly filled in.
        await Assert.That(SailingActivityUnresolvedOpcodes.SailingActivityDataUnresolved).IsEqualTo((ushort)0);
        await Assert.That(SailingActivityUnresolvedOpcodes.SailingActivityTaskProgressUnresolved).IsEqualTo((ushort)0);
    }
}
