using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Char.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

/// <summary>
/// Pins the actability entry layout and, more importantly, pins the two packets that carry it to the
/// same bytes. They used to write the fields separately and drifted apart, which left the rank-change
/// packet a field short.
/// </summary>
public class ActabilityEntryTests
{
    private static Actability Entry(uint id, int point, byte step) =>
        new(new ActabilityTemplate { Id = id }) { Point = point, Step = step };

    private static byte[] Written(Actability actability)
    {
        var stream = new PacketStream();
        actability.Write(stream);
        return stream.GetBytes();
    }

    [Test]
    public async Task Entry_IsPackedIdAndPointThenStep()
    {
        // pisc packs the two values behind a leading descriptor byte, so the entry is not a fixed width
        // and the step is whatever byte follows it.
        var bytes = Written(Entry(18, 20000, 3));

        var stream = new PacketStream();
        stream.Insert(0, bytes);
        var values = stream.ReadPisc(2);

        await Assert.That(values[0]).IsEqualTo(18u);
        await Assert.That(values[1]).IsEqualTo(20000u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)3);
        await Assert.That(stream.Count - stream.Pos).IsEqualTo(0);
    }

    [Test]
    public async Task Entry_WidthVariesWithValue()
    {
        // A packed block is only as wide as its values need, so nothing may assume a fixed entry size.
        var small = Written(Entry(1, 0, 0));
        var large = Written(Entry(uint.MaxValue, int.MaxValue, 12));

        await Assert.That(large.Length > small.Length).IsTrue();
    }

    [Test]
    public async Task Entry_ClampsNegativePointInsteadOfWrapping()
    {
        var bytes = Written(Entry(7, -50, 1));

        var stream = new PacketStream();
        stream.Insert(0, bytes);
        var values = stream.ReadPisc(2);

        await Assert.That(values[1]).IsEqualTo(0u);
    }

    [Test]
    public async Task BothPackets_WriteTheSameEntryBytes()
    {
        // The guard that matters: a layout change must not be able to move one packet and not the other.
        var actability = Entry(18, 20000, 3);
        var entry = Written(actability);

        var listBody = new PacketStream();
        new SCActabilityPacket(true, [actability]).Write(listBody);

        var modifiedBody = new PacketStream();
        new SCExpertLimitModifiedPacket(true, actability).Write(modifiedBody);

        // list: bool last, u8 count, entry -- modified: bool isUpgrade, entry
        await Assert.That(listBody.GetBytes()[2..]).IsEquivalentTo(entry);
        await Assert.That(modifiedBody.GetBytes()[1..]).IsEquivalentTo(entry);
    }

    [Test]
    public async Task ListPacket_CountMatchesEntriesWritten()
    {
        var entries = new[] { Entry(1, 10, 1), Entry(2, 20, 2), Entry(3, 30, 3) };

        var body = new PacketStream();
        new SCActabilityPacket(false, entries).Write(body);
        var bytes = body.GetBytes();

        await Assert.That(bytes[0]).IsEqualTo((byte)0);   // last
        await Assert.That(bytes[1]).IsEqualTo((byte)3);   // count

        var expected = new List<byte>();
        foreach (var entry in entries)
            expected.AddRange(Written(entry));

        await Assert.That(bytes[2..]).IsEquivalentTo(expected.ToArray());
    }
}
