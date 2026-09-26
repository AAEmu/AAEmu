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
    public async Task Read_TakesTheClaimContainerAsACountedVectorOfIds()
    {
        // A count of 3 followed by three ids. The count is on the wire; without it the packet is short.
        var body = new PacketStream().Write(1).Write(3).Write(0x0A).Write(0x0B).Write(0x0C).GetBytes();

        var packet = new CSSailingActivityClaimRewardPacket();
        packet.Read(new PacketStream(body));

        await Assert.That(packet.ActivityId).IsEqualTo(1);
        await Assert.That(packet.Container.Ids).IsEquivalentTo(new[] { 0x0A, 0x0B, 0x0C });
    }

    [Test]
    public async Task Read_RefusesAContainerThatClaimsMoreElementsThanTheStreamHolds()
    {
        // Declares 4 ids, supplies 2. A remainder-consuming reader would have accepted the tail.
        var body = new PacketStream().Write(1).Write(4).Write(0x0A).Write(0x0B).GetBytes();
        var packet = new CSSailingActivityClaimRewardPacket();

        var threw = false;
        try { packet.Read(new PacketStream(body)); }
        catch (InvalidDataException) { threw = true; }

        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task Read_RefusesAHugeCountWithoutAllocatingForIt()
    {
        // The count is a signed 32-bit field, so a body can claim far more elements than it carries.
        // The reader has to refuse that on the bytes that are actually left, before it asks for the
        // array: allocating first would let a four-byte body request gigabytes of int[] and take the
        // process down before the truncation was ever noticed.
        // The stream is positioned at the count itself: int.MaxValue elements claimed, one supplied.
        var body = new PacketStream().Write(int.MaxValue).Write(0x0A).GetBytes();
        var stream = new PacketStream(body);

        var threw = false;
        try
        {
            SailingActivityContainer.Read(stream, "huge-count");
        }
        catch (InvalidDataException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task Read_RefusesANegativeCountBeforeAllocatingForIt()
    {
        // A negative count must be refused on its own, not handed to an allocation: the array length
        // would be a negative value, and the failure would surface as an overflow rather than as the
        // malformed field it is.
        var body = new PacketStream().Write(-1).GetBytes();

        var threw = false;
        try
        {
            SailingActivityContainer.Read(new PacketStream(body), "negative-count");
        }
        catch (InvalidDataException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task Read_LeavesTheContainerEmptyWhenTheBodyCarriesNothing()
    {
        // A zero count: four bytes. The old design consumed "the rest of the stream", which for an
        // empty body produced a zero-byte container and a packet four bytes short of the wire format.
        var packet = new CSSailingActivityClaimRewardPacket();
        packet.Read(new PacketStream(new PacketStream().Write(1).Write(0).GetBytes()));

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
    public async Task StageUnlocked_WritesTheIdThenTheContainerCountThenItsIds()
    {
        var container = SailingActivityContainer.FromIds(1, 2, 3);
        var bytes = new SCSailingActivityStageUnlockedPacket(4, container).Write(new PacketStream()).GetBytes();

        await Assert.That(bytes).IsEquivalentTo(
            new PacketStream().Write(4).Write(3).Write(1).Write(2).Write(3).GetBytes());
        // 4 id + 4 count + 3*4 elements
        await Assert.That(bytes.Length).IsEqualTo(20);
    }

    [Test]
    public async Task StageUnlocked_WritesTheZeroCountAfterTheIdWhenTheContainerIsEmpty()
    {
        // SC 0x38D calls the vector helper exactly once, so an empty container still costs the four
        // bytes of a zero count. Writing only the id made this packet 4 bytes short of the wire format.
        var bytes = new SCSailingActivityStageUnlockedPacket(4, null).Write(new PacketStream()).GetBytes();

        await Assert.That(bytes.Length).IsEqualTo(8);
        await Assert.That(bytes).IsEquivalentTo(new PacketStream().Write(4).Write(0).GetBytes());
    }

    [Test]
    public async Task ClaimRewardResponse_WritesTheIdThenAllThreeContainers()
    {
        var bytes = new SCSailingActivityClaimRewardResponsePacket(
                1,
                SailingActivityContainer.FromIds(0x0A),
                SailingActivityContainer.Empty,
                SailingActivityContainer.FromIds(0x0B, 0x0C))
            .Write(new PacketStream()).GetBytes();

        // Three helpers are called on 0x38E, so three counts: 1, 0, 2.
        await Assert.That(bytes).IsEquivalentTo(
            new PacketStream().Write(1).Write(1).Write(0x0A).Write(0).Write(2).Write(0x0B).Write(0x0C).GetBytes());
        // 4 id + (4+4) + (4+0) + (4+8) = 28
        await Assert.That(bytes.Length).IsEqualTo(28);
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
    public async Task List_RefusesMoreThanTheThirtyTwoRowsTheClientHolds()
    {
        // The client copies rows into a fixed 32-entry array with no bound check, so a 33rd row would
        // be written past the end of its buffer. The cap is the client's, not a defensive choice.
        var rows = Enumerable.Range(0, 32)
            .Select(i => new SailingActivityListRow(i, 0, 0))
            .ToArray();
        var ok = new SCSailingActivityListPacket(rows).Write(new PacketStream()).GetBytes();
        await Assert.That(ok.Length).IsEqualTo(4 + 32 * 20);

        var tooMany = Enumerable.Range(0, 33)
            .Select(i => new SailingActivityListRow(i, 0, 0))
            .ToArray();
        var refused = false;
        try { _ = new SCSailingActivityListPacket(tooMany).Write(new PacketStream()); }
        catch (ArgumentOutOfRangeException) { refused = true; }
        await Assert.That(refused).IsTrue();
    }

    [Test]
    public async Task TheTwoUnsentPackets_HaveTheirRecoveredSlotsRatherThanAGuessedOne()
    {
        // An earlier revision claimed both carried opcode null and inferred a slot by counting gaps.
        // The raw schema assigns them explicitly: 0x38B and 0x38C. They remain unsent because their
        // container calls carry no field name, not because the slot is unknown.
        await Assert.That(SailingActivityUnresolvedOpcodes.SailingActivityDataPacket).IsEqualTo((ushort)0x38B);
        await Assert.That(SailingActivityUnresolvedOpcodes.SailingActivityTaskProgressPacket).IsEqualTo((ushort)0x38C);
    }
}
