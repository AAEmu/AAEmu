using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCRankSnapshotPacketTests
{
    private static RankingOrderedEntry Row(uint ranking, long v1)
    {
        return new RankingOrderedEntry
        {
            V1 = v1,
            V2 = 0,
            Timestamp = 1789530000,
            WorldId = 4,
            Id = 8,
            AccountId = 3,
            Type = 23,
            PrivacyStatus = 0,
            Ranking = ranking
        };
    }

    [Test]
    public async Task Snapshot_WritesTheRequestThenTheLinesThenTheBoardAndItsTime()
    {
        var stream = new SCRankSnapshotPacket(23, 1, [Row(1, 12345)], 23, 1789530000)
            .Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadUInt32()).IsEqualTo(23u);      // the board asked for
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);       // its division
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);       // one line

        await Assert.That(stream.ReadInt64()).IsEqualTo(12345L);    // V1
        await Assert.That(stream.ReadInt64()).IsEqualTo(0L);        // V2
        await Assert.That(stream.ReadInt64()).IsEqualTo(1789530000L);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)4);    // worldId
        await Assert.That(stream.ReadUInt64()).IsEqualTo(8UL);      // id
        await Assert.That(stream.ReadUInt64()).IsEqualTo(3UL);      // accountId
        await Assert.That(stream.ReadInt64()).IsEqualTo(23L);       // type
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);    // privacyStatus
        await Assert.That(stream.ReadBoolean()).IsFalse();          // isAllocated
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);       // the place the line holds

        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);       // no tiers
        await Assert.That(stream.ReadInt64()).IsEqualTo(23L);       // the board
        await Assert.That(stream.ReadUInt64()).IsEqualTo(1789530000UL); // when it was read
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Snapshot_WithNoLinesStillCarriesTheCountsAndTheBoard()
    {
        var stream = new SCRankSnapshotPacket(23, 2003, [], 23, 0).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadUInt32()).IsEqualTo(23u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(2003u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadInt64()).IsEqualTo(23L);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(0UL);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Snapshot_CapsTheLinesTheClientReads()
    {
        var rows = Enumerable.Range(0, SCRankSnapshotPacket.MaxEntries + 5).Select(i => Row((uint)i, i)).ToList();

        var stream = new SCRankSnapshotPacket(23, 0, rows, 23, 0).Write(new PacketStream());

        stream.Rollback();
        stream.ReadBytes(8);
        await Assert.That(stream.ReadUInt32()).IsEqualTo((uint)SCRankSnapshotPacket.MaxEntries);
        stream.ReadBytes(SCRankSnapshotPacket.MaxEntries * 55);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadInt64()).IsEqualTo(23L);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(0UL);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}
