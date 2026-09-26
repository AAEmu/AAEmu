using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.CommonFarm;
using AAEmu.Game.Models.Game.CommonFarm.Static;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// Pins the farm show-area writer (SC 0x220) to the real wire, recovered from the raw client packet
/// schema (the authoritative source — the derived struct dumps drop guarded loops and mistype the
/// count as unsigned):
/// <list type="bullet">
///   <item><description><c>u32 type</c> (object offset 16)</description></item>
///   <item><description><b>signed</b> <c>s32 count</c> (object offset 20)</description></item>
///   <item><description>only when <c>count &gt; 0</c>, a loop of <c>count</c> elements starting at
///   object offset 24 with a 24-byte stride; each element is the standard 11-byte quantized
///   world-position block</description></item>
/// </list>
/// The 24-byte stride is pinned by the sibling packet that places the same shared position
/// serializer at object offset 24 with its next field at object offset 48.
/// </summary>
public class SCShowCommonFarmPacketTests
{
    /// <summary>Bytes one quantized world position occupies on the wire.</summary>
    private const int PositionBytes = 11;

    private static readonly Vector3[] TwoPositions = [new(10f, 20f, 30f), new(-40f, -50f, -60f)];

    [Test]
    public async Task Write_ZeroCount_IsHeaderOnly()
    {
        var body = Write((uint)FarmType.Farm, 0, []);

        // u32 type + s32 count and nothing else: the guarded loop is skipped entirely.
        await Assert.That(body.Length).IsEqualTo(8);
        await Assert.That(ReadUInt32(body, 0)).IsEqualTo((uint)FarmType.Farm);
        await Assert.That(ReadInt32(body, 4)).IsEqualTo(0);
    }

    [Test]
    public async Task Write_OneCount_EmitsExactlyOnePosition()
    {
        var body = Write((uint)FarmType.Nursery, 1, [TwoPositions[0]]);

        await Assert.That(ReadUInt32(body, 0)).IsEqualTo((uint)FarmType.Nursery);
        await Assert.That(ReadInt32(body, 4)).IsEqualTo(1);
        await Assert.That(body.Length).IsEqualTo(8 + PositionBytes);

        // The payload is byte-for-byte the standard quantized position block, not a private record.
        var expected = Helpers.ConvertPosition(TwoPositions[0].X, TwoPositions[0].Y, TwoPositions[0].Z);
        await Assert.That(expected.Length).IsEqualTo(PositionBytes);
        for (var index = 0; index < PositionBytes; index++)
        {
            await Assert.That(body[8 + index]).IsEqualTo(expected[index]);
        }
    }

    [Test]
    public async Task Write_CountN_EmitsExactlyNPositions()
    {
        var body = Write((uint)FarmType.Ranch, 2, TwoPositions);

        await Assert.That(ReadInt32(body, 4)).IsEqualTo(2);
        await Assert.That(body.Length).IsEqualTo(8 + (2 * PositionBytes));

        // Each element is its own 11-byte block, so element two starts at the documented offset.
        var secondStart = 8 + PositionBytes;
        var expected = Helpers.ConvertPosition(TwoPositions[1].X, TwoPositions[1].Y, TwoPositions[1].Z);
        for (var index = 0; index < PositionBytes; index++)
        {
            await Assert.That(body[secondStart + index]).IsEqualTo(expected[index]);
        }
    }

    [Test]
    public async Task Write_NegativeCount_IsRefusedInsteadOfLooping()
    {
        // The count is signed on the wire, so a negative value is malformed, not a huge unsigned
        // loop. The writer must refuse rather than write a body.
        await Assert.That(() => Write((uint)FarmType.Farm, -1, TwoPositions))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Write_CountBeyondAvailablePositions_IsBounded()
    {
        // Never claim more elements than exist, or the client would read past the end of the body.
        var body = Write((uint)FarmType.Farm, 2, [TwoPositions[0]]);

        await Assert.That(ReadInt32(body, 4)).IsEqualTo(1);
        await Assert.That(body.Length).IsEqualTo(8 + PositionBytes);
    }

    [Test]
    public async Task Write_HostileCount_IsBoundedByTheWireClamp()
    {
        var many = new Vector3[CommonFarmShowAreaRules.MaxPositionCount + 10];
        for (var index = 0; index < many.Length; index++)
            many[index] = new Vector3(index, index, index);

        var body = Write((uint)FarmType.Farm, int.MaxValue, many);

        await Assert.That(ReadInt32(body, 4)).IsEqualTo(CommonFarmShowAreaRules.MaxPositionCount);
        await Assert.That(body.Length).IsEqualTo(8 + (CommonFarmShowAreaRules.MaxPositionCount * PositionBytes));
    }

    [Test]
    public async Task TryResolveCount_RejectsNegative()
    {
        await Assert.That(CommonFarmShowAreaRules.TryResolveCount(-1, 3, out var bounded)).IsFalse();
        await Assert.That(bounded).IsEqualTo(0);
    }

    [Test]
    public async Task TryResolveCount_ClampsToTheWireBound()
    {
        await Assert.That(CommonFarmShowAreaRules.TryResolveCount(500, 500, out var bounded)).IsTrue();
        await Assert.That(bounded).IsEqualTo(CommonFarmShowAreaRules.MaxPositionCount);
    }

    [Test]
    public async Task TryResolveCount_NeverExceedsWhatExists()
    {
        await Assert.That(CommonFarmShowAreaRules.TryResolveCount(4, 2, out var bounded)).IsTrue();
        await Assert.That(bounded).IsEqualTo(2);
    }

    /// <summary>
    /// Writes the packet body (without the frame header) and returns the raw bytes, so the tests
    /// measure the wire rather than the writer's own bookkeeping.
    /// </summary>
    private static byte[] Write(uint farmType, int count, IReadOnlyList<Vector3> positions)
    {
        var packet = new SCShowCommonFarmPacket(farmType, count, positions);

        var stream = new PacketStream();
        packet.Write(stream);

        // The stream's buffer is larger than what was written, so slice to the written count.
        return stream.Buffer[..stream.Count];
    }

    private static uint ReadUInt32(byte[] body, int offset) =>
        BitConverter.ToUInt32(body, offset);

    private static int ReadInt32(byte[] body, int offset) =>
        BitConverter.ToInt32(body, offset);
}
